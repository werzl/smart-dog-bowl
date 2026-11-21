using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Npgsql;
using SmartBowl.Api.Data;
using SmartBowl.Contracts;
using Xunit;

namespace SmartBowl.Api.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SMARTBOWL_TEST_DATABASE")))
            Skip = "Set SMARTBOWL_TEST_DATABASE to run tests with PostgreSQL.";
    }
}

public sealed class ApiFixture : IDisposable
{
    private readonly string? adminConnection = Environment.GetEnvironmentVariable("SMARTBOWL_TEST_DATABASE");
    private readonly string schema = "test_" + Guid.NewGuid().ToString("N");
    private readonly WebApplicationFactory<Program>? factory;
    public string ConnectionString { get; } = "";

    public ApiFixture()
    {
        if (string.IsNullOrWhiteSpace(adminConnection)) return;
        using var connection = new NpgsqlConnection(adminConnection);
        connection.Open();
        using var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", connection);
        create.ExecuteNonQuery();
        ConnectionString = new NpgsqlConnectionStringBuilder(adminConnection) { SearchPath = schema }.ConnectionString;
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:bowldb"] = ConnectionString,
                ["DeviceApiKey"] = "integration-test-key",
                ["SeedData:Enabled"] = "true"
            }));
        });
    }

    public HttpClient CreateClient(bool authorized = true)
    {
        var client = factory!.CreateClient();
        if (authorized) client.DefaultRequestHeaders.Add("X-Device-Key", "integration-test-key");
        return client;
    }

    public void Dispose()
    {
        factory?.Dispose();
        if (string.IsNullOrWhiteSpace(adminConnection)) return;
        using var connection = new NpgsqlConnection(adminConnection);
        connection.Open();
        using var drop = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", connection);
        drop.ExecuteNonQuery();
    }
}

public sealed class ApiTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [PostgresFact]
    public async Task SeedAndMigrationsAreRepeatable()
    {
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        var devices = await client.GetFromJsonAsync<DeviceDto[]>("/api/v1/devices");
        Assert.Contains(devices!, device => device.DeviceId == Guid.Parse("7e8c0d1a-6b39-47b5-928a-64f3f1039e21"));
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        await DatabaseInitializer.InitializeAsync(source, true, default);
        await using var count = source.CreateCommand("SELECT count(*) FROM weight_readings WHERE device_id = '7e8c0d1a-6b39-47b5-928a-64f3f1039e21'");
        Assert.Equal(1441L, await count.ExecuteScalarAsync());
        var status = await client.GetFromJsonAsync<DeviceStatusDto>("/api/v1/devices/7e8c0d1a-6b39-47b5-928a-64f3f1039e21/status");
        Assert.Equal((status!.LatestReading!.RawReading - status.Calibration.ZeroOffset) / status.Calibration.CalibrationFactor,
            status.LatestReading.WeightGrams);
    }

    [PostgresFact]
    public async Task BatchRetriesAreDurableAndIdempotent()
    {
        using var client = fixture.CreateClient();
        var id = await CreateDevice(client);
        var observedAt = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.FromHours(1));
        var request = new ReadingBatchRequest("first", [new(1, observedAt, -0.1m, -119957.95m), new(3, observedAt.AddSeconds(1), 850.25m, -477530.125m)]);
        var response = await client.PostAsJsonAsync($"/api/v1/devices/{id}/readings:batch", request);
        response.EnsureSuccessStatusCode();
        Assert.Equal(2, (await response.Content.ReadFromJsonAsync<ReadingBatchResponse>())!.InsertedCount);
        response = await client.PostAsJsonAsync($"/api/v1/devices/{id}/readings:batch", request);
        var retry = await response.Content.ReadFromJsonAsync<ReadingBatchResponse>();
        Assert.Equal(0, retry!.InsertedCount);
        Assert.Equal(2, retry.DuplicateCount);
        Assert.Equal(new[] { new SequenceRangeDto(1, 1), new(3, 3) }, retry.AcknowledgedRanges);
        using var secondClient = fixture.CreateClient();
        var readings = await secondClient.GetFromJsonAsync<ReadingsPageDto>($"/api/v1/devices/{id}/readings");
        Assert.Equal(2, readings!.Readings.Length);
        Assert.Equal(TimeSpan.Zero, readings.Readings[0].ObservedAt.Offset);
        Assert.Equal(-0.1m, readings.Readings[0].WeightGrams);
        Assert.Equal(-477530.125m, readings.Readings[1].RawReading);
    }

    [PostgresFact]
    public async Task ConflictingRetriesRejectTheWholeBatch()
    {
        using var client = fixture.CreateClient();
        var id = await CreateDevice(client);
        var reading = new WeightReadingDto(1, DateTimeOffset.UtcNow, 12.5m);
        (await client.PostAsJsonAsync($"/api/v1/devices/{id}/readings:batch", new ReadingBatchRequest("a", [reading]))).EnsureSuccessStatusCode();
        var response = await client.PostAsJsonAsync($"/api/v1/devices/{id}/readings:batch",
            new ReadingBatchRequest("b", [reading with { WeightGrams = 1 }, reading with { Sequence = 2 }]));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var page = await client.GetFromJsonAsync<ReadingsPageDto>($"/api/v1/devices/{id}/readings");
        Assert.Single(page!.Readings);
        Assert.Equal(12.5m, page.Readings[0].WeightGrams);
    }

    [PostgresFact]
    public async Task ConcurrentRetriesCreateOneReading()
    {
        using var client = fixture.CreateClient();
        var id = await CreateDevice(client);
        var request = new ReadingBatchRequest("same", [new(1, DateTimeOffset.UtcNow, 10)]);
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync($"/api/v1/devices/{id}/readings:batch", request)));
        var inserted = 0;
        foreach (var response in responses)
        {
            response.EnsureSuccessStatusCode();
            inserted += (await response.Content.ReadFromJsonAsync<ReadingBatchResponse>())!.InsertedCount;
        }
        Assert.Equal(1, inserted);
    }

    [PostgresFact]
    public async Task ReadingsSupportPagingAndTimeFilters()
    {
        using var client = fixture.CreateClient();
        var first = await client.GetFromJsonAsync<ReadingsPageDto>("/api/v1/devices/7e8c0d1a-6b39-47b5-928a-64f3f1039e21/readings?limit=2");
        Assert.Equal(2, first!.Readings.Length);
        Assert.Equal(1L, first.NextAfterSequence);
        var second = await client.GetFromJsonAsync<ReadingsPageDto>("/api/v1/devices/7e8c0d1a-6b39-47b5-928a-64f3f1039e21/readings?limit=2&afterSequence=1");
        Assert.Equal(2L, second!.Readings[0].Sequence);
        var from = Uri.EscapeDataString(first.Readings[0].ObservedAt.ToString("O"));
        var to = Uri.EscapeDataString(first.Readings[1].ObservedAt.ToString("O"));
        var filtered = await client.GetFromJsonAsync<ReadingsPageDto>($"/api/v1/devices/7e8c0d1a-6b39-47b5-928a-64f3f1039e21/readings?from={from}&to={to}");
        Assert.Single(filtered!.Readings);
        Assert.Equal(0L, filtered.Readings[0].Sequence);
    }

    [PostgresFact]
    public async Task CalibrationAcceptsNegativeFactorsAndPreservesReadingHistory()
    {
        using var client = fixture.CreateClient();
        var id = await CreateDevice(client);
        var reading = new WeightReadingDto(1, DateTimeOffset.UtcNow, 23.5m);
        (await client.PostAsJsonAsync($"/api/v1/devices/{id}/readings:batch", new ReadingBatchRequest("a", [reading]))).EnsureSuccessStatusCode();
        var calibration = new CalibrationDto(-100.25m, -12.5m);
        (await client.PostAsJsonAsync($"/api/v1/devices/{id}/calibration", calibration)).EnsureSuccessStatusCode();
        Assert.Equal(calibration, await client.GetFromJsonAsync<CalibrationDto>($"/api/v1/devices/{id}/calibration"));
        var status = await client.GetFromJsonAsync<DeviceStatusDto>($"/api/v1/devices/{id}/status");
        Assert.Equal(23.5m, status!.LatestReading!.WeightGrams);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/devices/{id}/calibration", new CalibrationDto(0, 0))).StatusCode);
    }

    [PostgresFact]
    public async Task InvalidInputAndUnauthorizedWritesDoNotChangeTheDatabase()
    {
        using var client = fixture.CreateClient();
        using var unauthorized = fixture.CreateClient(false);
        var id = await CreateDevice(client);
        var path = $"/api/v1/devices/{id}/readings:batch";
        var reading = new WeightReadingDto(1, DateTimeOffset.UtcNow, 5);
        Assert.Equal(HttpStatusCode.Unauthorized, (await unauthorized.PostAsJsonAsync(path, new ReadingBatchRequest("a", [reading]))).StatusCode);
        foreach (var json in new[] { "{}", "{\"batchId\":\"a\",\"readings\":[null]}", "{\"batchId\":\"a\",\"readings\":[{\"sequence\":1,\"observedAt\":\"2026-11-21T12:00:00Z\"}]}" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path, new ReadingBatchRequest("a", [reading, reading]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/devices/{id}/readings?limit=0")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/devices/df479026-ea8c-42bc-8e48-b62e2678d48e/status")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/v1/devices/df479026-ea8c-42bc-8e48-b62e2678d48e/readings:batch", new ReadingBatchRequest("a", [reading]))).StatusCode);
        var page = await client.GetFromJsonAsync<ReadingsPageDto>($"/api/v1/devices/{id}/readings");
        Assert.Empty(page!.Readings);
    }

    [PostgresFact]
    public async Task DeviceIdsRequireNonemptyGuids()
    {
        using var client = fixture.CreateClient();
        foreach (var id in new[] { "bowl-01", "", "00000000-0000-0000-0000-000000000000" })
        {
            var response = await client.PostAsJsonAsync("/api/v1/devices",
                new { deviceId = id, name = "Invalid ID", calibration = new CalibrationDto(0, 1) });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/devices/bowl-01/status")).StatusCode);
        var deviceId = await CreateDevice(client);
        var status = await client.GetFromJsonAsync<DeviceStatusDto>($"/api/v1/devices/{deviceId}/status");
        Assert.Equal(deviceId, status!.DeviceId);
    }
    [PostgresFact]
    public async Task DrinkingSummaryUsesTheRequestedPeriodAndRejectsInvalidQueries()
    {
        using var client = fixture.CreateClient();
        const string id = "7e8c0d1a-6b39-47b5-928a-64f3f1039e21";
        var status = await client.GetFromJsonAsync<DeviceStatusDto>($"/api/v1/devices/{id}/status");
        var end = status!.LatestReading!.ObservedAt.AddSeconds(1);
        var from = Uri.EscapeDataString(end.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(end.ToString("O"));
        var summary = await client.GetFromJsonAsync<DrinkingSummaryDto>($"/api/v1/devices/{id}/drinking-summary?from={from}&to={to}&timeZone=Europe%2FLondon");
        Assert.InRange(summary!.EstimatedConsumedMilliliters, 640, 660);
        Assert.NotEmpty(summary.MostConsumedHours);
        Assert.NotEmpty(summary.LeastConsumedHours);
        Assert.Null(summary.AverageDrinkDurationSeconds);
        Assert.All(summary.Events, drink => Assert.Null(drink.DurationSeconds));
        var shortFrom = Uri.EscapeDataString(end.AddHours(-1).ToString("O"));
        var shortPeriod = await client.GetFromJsonAsync<DrinkingSummaryDto>($"/api/v1/devices/{id}/drinking-summary?from={shortFrom}&to={to}");
        Assert.True(shortPeriod!.EstimatedConsumedMilliliters < summary.EstimatedConsumedMilliliters);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/devices/{id}/drinking-summary?timeZone=invalid-zone")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/devices/{id}/drinking-summary?to=0001-01-01T00:00:00Z")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/devices/{id}/drinking-summary?from={to}&to={from}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/devices/df479026-ea8c-42bc-8e48-b62e2678d48e/drinking-summary")).StatusCode);
    }
    private static async Task<Guid> CreateDevice(HttpClient client)
    {
        var id = Guid.NewGuid();
        var response = await client.PostAsJsonAsync("/api/v1/devices", new DeviceDto(id, "Test bowl", new(-120000, -420.5m)));
        response.EnsureSuccessStatusCode();
        return id;
    }
}
