using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using SmartBowl.Api.Data;
using SmartBowl.Api.Notifications;
using SmartBowl.Contracts;
using Lib.Net.Http.WebPush;

using Xunit;

namespace SmartBowl.Api.Tests;

public sealed class NotificationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static async Task<Guid> Device(HttpClient client)
    {
        var id = Guid.NewGuid();
        (await client.PostAsJsonAsync("/api/v1/devices", new DeviceDto(id, "Notification test bowl", new(0, 1)))).EnsureSuccessStatusCode();
        return id;
    }
    private static async Task Post(HttpClient client, Guid device, long sequence, DateTimeOffset time, decimal weight)
        => (await client.PostAsJsonAsync($"/api/v1/devices/{device}/readings:batch",
            new ReadingBatchRequest($"test-{sequence}", [new(sequence, time, weight, null)]))).EnsureSuccessStatusCode();
    private static async Task<BowlNotificationDto[]> Alerts(HttpClient client, Guid device)
        => (await client.GetFromJsonAsync<BowlNotificationDto[]>("/api/v1/notifications"))!.Where(item => item.DeviceId == device).ToArray();

    [PostgresFact]
    public async Task EmptyRetryRefillAndSecondEmptyCreateOneAlertPerEpisode()
    {
        using var client = fixture.CreateClient();
        var id = await Device(client); var time = DateTimeOffset.UtcNow.AddSeconds(-20);
        await Post(client, id, 1, time, 0);
        var alert = Assert.Single(await Alerts(client, id));
        Assert.Null(alert.ResolvedAt);
        await Post(client, id, 1, time, 0);
        await Post(client, id, 2, time.AddSeconds(1), 8);
        await Post(client, id, 3, time.AddSeconds(2), 30);
        Assert.Equal(alert.Id, Assert.Single(await Alerts(client, id)).Id);
        Assert.Null(Assert.Single(await Alerts(client, id)).ResolvedAt);
        await Post(client, id, 4, time.AddSeconds(3), -200);
        Assert.Null(Assert.Single(await Alerts(client, id)).ResolvedAt);
        await Post(client, id, 5, time.AddSeconds(4), 50);
        Assert.NotNull(Assert.Single(await Alerts(client, id)).ResolvedAt);
        await Post(client, id, 6, time.AddSeconds(5), -0.1m);
        var alerts = await Alerts(client, id);
        Assert.Equal(2, alerts.Length);
        Assert.Single(alerts, item => item.ResolvedAt is null);
    }

    [PostgresFact]
    public async Task OldRemovedAndLateReadingsDoNotCreateAlerts()
    {
        using var client = fixture.CreateClient(); var id = await Device(client);
        var time = DateTimeOffset.UtcNow.AddSeconds(-10);
        await Post(client, id, 1, time.AddHours(-1), 0);
        await Post(client, id, 2, time, 500);
        await Post(client, id, 3, time.AddSeconds(-1), 0);
        await Post(client, id, 4, time.AddSeconds(1), -200);
        Assert.Empty(await Alerts(client, id));
    }

    [PostgresFact]
    public async Task AnIncorrectFutureTimestampDoesNotBlockCurrentAlerts()
    {
        using var client = fixture.CreateClient(); var id = await Device(client);
        await Post(client, id, 1, DateTimeOffset.UtcNow.AddHours(1), 0);
        Assert.Empty(await Alerts(client, id));
        await Post(client, id, 2, DateTimeOffset.UtcNow.AddSeconds(-1), 0);
        Assert.Single(await Alerts(client, id));
    }

    [PostgresFact]
    public async Task BatchChronologyAndConflictsKeepAlertStateAtomic()
    {
        using var client = fixture.CreateClient(); var id = await Device(client);
        var time = DateTimeOffset.UtcNow.AddSeconds(-10);
        var batch = new ReadingBatchRequest("out-of-order", [new(2, time.AddSeconds(1), 500, null), new(1, time, 0, null)]);
        (await client.PostAsJsonAsync($"/api/v1/devices/{id}/readings:batch", batch)).EnsureSuccessStatusCode();
        Assert.NotNull(Assert.Single(await Alerts(client, id)).ResolvedAt);
        var conflict = new ReadingBatchRequest("conflict", [new(2, time.AddSeconds(1), 200, null), new(3, time.AddSeconds(2), 0, null)]);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/devices/{id}/readings:batch", conflict)).StatusCode);
        Assert.Single(await Alerts(client, id));
        Assert.NotNull(Assert.Single(await Alerts(client, id)).ResolvedAt);
    }

    [PostgresFact]
    public async Task PushIdentitySurvivesRepositoryInstances()
    {
        using var client = fixture.CreateClient(); (await client.GetAsync("/health")).EnsureSuccessStatusCode();
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var first = await new NotificationStore(source).IdentityAsync("mailto:test@example.org", default);
        var second = await new NotificationStore(source).IdentityAsync("mailto:test@example.org", default);
        Assert.Equal(first.PublicKey, second.PublicKey); Assert.Equal(first.PrivateKey, second.PrivateKey);
        var configuration = await client.GetFromJsonAsync<PushConfigurationDto>("/api/v1/notifications/push/configuration");
        Assert.Equal(first.PublicKey, configuration!.PublicKey); Assert.False(configuration.Configured);
    }

    [PostgresFact]
    public async Task PushRetriesThenSendsAndRefillCancelsPendingDelivery()
    {
        using var client = fixture.CreateClient(); var id = await Device(client);
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var store = new NotificationStore(source);
        var subscription = Subscription();
        await store.SubscribeAsync(subscription, default);
        var time = DateTimeOffset.UtcNow.AddSeconds(-10);
        await Post(client, id, 1, time, 0);
        var sender = new FakeSender { Fail = true };
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["WebPush:Subject"] = "mailto:test@example.org" }).Build();
        var worker = new PushDeliveryWorker(source, store, sender, config, NullLogger<PushDeliveryWorker>.Instance);
        // Other fixture tests can leave alerts, so drain all due deliveries.
        while (await worker.DispatchOneAsync(default)) { }
        await using var retry = source.CreateCommand("UPDATE web_push_deliveries SET next_attempt_at = now() WHERE finished_at IS NULL");
        await retry.ExecuteNonQueryAsync(); sender.Fail = false;
        while (await worker.DispatchOneAsync(default)) { }
        Assert.Contains(sender.Payloads, payload => payload.Contains("Notification test bowl"));
        await Post(client, id, 2, time.AddSeconds(1), 500);
        await Post(client, id, 3, time.AddSeconds(2), 0);
        await Post(client, id, 4, time.AddSeconds(3), 500);
        var before = sender.Payloads.Count;
        while (await worker.DispatchOneAsync(default)) { }
        Assert.Equal(before, sender.Payloads.Count);
        await store.RemoveAsync(subscription.Endpoint, default);
    }

    [PostgresFact]
    public async Task SubscriptionWritesRequireKeyAndRejectUnsafeEndpoints()
    {
        using var client = fixture.CreateClient(false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/notifications/push/subscriptions", Subscription())).StatusCode);
        client.DefaultRequestHeaders.Add("X-Device-Key", "integration-test-key");
        var invalid = Subscription() with { Endpoint = "https://127.0.0.1/private" };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/notifications/push/subscriptions", invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/v1/notifications/push/subscriptions", Subscription())).StatusCode);
    }

    [PostgresFact]
    public async Task ExpiredPushSubscriptionIsRemoved()
    {
        using var client = fixture.CreateClient(); var id = await Device(client);
        await using var source = NpgsqlDataSource.Create(fixture.ConnectionString);
        var store = new NotificationStore(source); var subscription = Subscription();
        await store.SubscribeAsync(subscription, default);
        await Post(client, id, 1, DateTimeOffset.UtcNow.AddSeconds(-1), 0);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["WebPush:Subject"] = "mailto:test@example.org" }).Build();
        var worker = new PushDeliveryWorker(source, store, new FakeSender { Gone = true }, config, NullLogger<PushDeliveryWorker>.Instance);
        while (await worker.DispatchOneAsync(default)) { }
        await using var count = source.CreateCommand("SELECT count(*) FROM web_push_subscriptions WHERE endpoint = $1");
        count.Parameters.AddWithValue(subscription.Endpoint);
        Assert.Equal(0L, await count.ExecuteScalarAsync());
        Assert.Null(Assert.Single(await Alerts(client, id)).ResolvedAt);
    }

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/test", true)]
    [InlineData("https://web.push.apple.com/test", true)]
    [InlineData("https://updates.push.services.mozilla.com/test", true)]
    [InlineData("https://wns2.notify.windows.com/w/test", true)]
    [InlineData("https://fcm.googleapis.com.evil.example/test", false)]
    [InlineData("https://fcm.googleapis.com:444/test", false)]
    [InlineData("http://fcm.googleapis.com/test", false)]
    [InlineData("https://localhost/test", false)]
    public void EndpointValidation(string endpoint, bool expected) => Assert.Equal(expected, PushValidation.ValidEndpoint(endpoint));

    [Fact]
    public void BrowserKeysMustContainAValidCurvePointAndAuthSecret()
    {
        var valid = Subscription(); Assert.True(PushValidation.ValidSubscription(valid));
        Assert.False(PushValidation.ValidSubscription(valid with { Keys = new("invalid", "invalid") }));
    }

    [Fact]
    public async Task TransportEncryptsThePayloadAndIncludesVapidAuthentication()
    {
        var handler = new CaptureHandler();
        using var sender = new WebPushSender(new HttpClient(handler));
        var keys = PushIdentity.Create("mailto:test@example.org");
        var subscription = Subscription();
        await sender.SendAsync(subscription,
            "test bowl payload", new("mailto:test@example.org", keys.PublicKey, keys.PrivateKey), default);
        Assert.NotNull(handler.Authorization);
        Assert.Contains("vapid", handler.Authorization, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("test bowl payload", System.Text.Encoding.UTF8.GetString(handler.Body!));
        Assert.True(handler.Body!.Length > 20);
        Assert.Equal("aes128gcm", handler.Encoding);
    }

    [Theory]
    [InlineData("mailto:owner@example.org", true)]
    [InlineData("https://bowl.example.org/contact", true)]
    [InlineData("owner@example.org", false)]
    [InlineData("", false)]
    public void ContactValidation(string subject, bool expected) => Assert.Equal(expected, PushValidation.ValidSubject(subject));

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Authorization { get; private set; }
        public byte[]? Body { get; private set; }
        public string? Encoding { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Authorization = request.Headers.GetValues("Authorization").Single();
            Encoding = request.Content!.Headers.ContentEncoding.Single();
            Body = await request.Content.ReadAsByteArrayAsync(ct);
            return new(HttpStatusCode.Created);
        }
    }

    private static PushSubscriptionRequest Subscription()
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var point = key.ExportParameters(false).Q;
        static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return new($"https://fcm.googleapis.com/fcm/send/test-{Guid.NewGuid():N}",
            new(Encode([4, ..point.X!, ..point.Y!]), Encode(RandomNumberGenerator.GetBytes(16))));
    }
    private sealed class FakeSender : IPushSender
    {
        public bool Fail { get; set; }
        public bool Gone { get; set; }
        public List<string> Payloads { get; } = [];
        public Task SendAsync(PushSubscriptionRequest subscription, string payload, PushIdentity identity, CancellationToken ct)
        {
            if (Fail) throw new HttpRequestException("Simulated network failure.");
            if (Gone) throw new PushServiceClientException("Expired subscription.", HttpStatusCode.Gone);
            Payloads.Add(payload); return Task.CompletedTask;
        }
    }
}
