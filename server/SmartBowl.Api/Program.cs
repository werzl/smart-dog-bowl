using System.Security.Cryptography;
using System.Text;
using Microsoft.OpenApi;
using Npgsql;
using SmartBowl.Api;
using SmartBowl.Api.Data;
using SmartBowl.Api.Analysis;
using SmartBowl.Contracts;
using SmartBowl.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "Smart Bowl API";
    document.Components ??= new OpenApiComponents();
    document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    document.Components.SecuritySchemes["DeviceKey"] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-Device-Key",
        Description = "Enter the configured device API key. Development uses local-development-key."
    };

    foreach (var path in document.Paths.Where(path => path.Key.StartsWith("/api/v1/", StringComparison.Ordinal)))
    {
        if (path.Value.Operations?.TryGetValue(HttpMethod.Post, out var operation) == true)
        {
            operation.Security = [new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("DeviceKey", document)] = []
            }];
        }
    }

    return Task.CompletedTask;
}));
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("bowldb")
    ?? throw new InvalidOperationException("Set ConnectionStrings:bowldb or start the API through Aspire.")));
builder.Services.AddSingleton<BowlRepository>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

var app = builder.Build();
var apiKey = app.Configuration["DeviceApiKey"];
if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("Set DeviceApiKey before you start the API.");
var expectedKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));


app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = exception => exception is BadHttpRequestException request ? request.StatusCode : 500
});
app.UseStatusCodePages();
app.MapDefaultEndpoints();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("../openapi/v1.json", "Smart Bowl API v1");
        options.DocumentTitle = "Smart Bowl API";
    });
}

using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
{
    await DatabaseInitializer.InitializeAsync(app.Services.GetRequiredService<NpgsqlDataSource>(),
        app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("SeedData:Enabled"), timeout.Token);
}

var devices = app.MapGroup("/api/v1/devices");
devices.MapGet("/", async (BowlRepository repository, CancellationToken ct) => await repository.GetDevicesAsync(ct));
devices.MapGet("/{deviceId}/status", async (Guid deviceId, BowlRepository repository, CancellationToken ct) =>
    await repository.GetStatusAsync(deviceId, ct) is { } status ? Results.Ok(status) : Results.NotFound());
devices.MapGet("/{deviceId}/calibration", async (Guid deviceId, BowlRepository repository, CancellationToken ct) =>
    await repository.GetStatusAsync(deviceId, ct) is { } status ? Results.Ok(status.Calibration) : Results.NotFound());
devices.MapGet("/{deviceId}/readings", async (Guid deviceId, DateTimeOffset? from, DateTimeOffset? to,
    long? afterSequence, int? limit, BowlRepository repository, CancellationToken ct) =>
{
    if (limit is < 1 or > 1000 || afterSequence is < -1 || (from.HasValue && to.HasValue && from >= to))
        return Results.Problem("Use a limit from 1 to 1000. Use a cursor of at least -1. Ensure that from precedes to.", statusCode: 400);
    if (await repository.GetStatusAsync(deviceId, ct) is null) return Results.NotFound();
    return Results.Ok(await repository.GetReadingsAsync(deviceId, from, to, afterSequence ?? -1, limit ?? 100, ct));
});

devices.MapGet("/{deviceId}/drinking-summary", async (Guid deviceId, DateTimeOffset? from, DateTimeOffset? to,
    string? timeZone, BowlRepository repository, CancellationToken ct) =>
{
    var end = to ?? DateTimeOffset.UtcNow;
    var start = from ?? (end >= DateTimeOffset.MinValue.AddDays(1) ? end.AddDays(-1) : DateTimeOffset.MinValue);
    if (start >= end || end - start > TimeSpan.FromDays(7) || end > DateTimeOffset.UtcNow.AddMinutes(5) || start < DateTimeOffset.MinValue.AddMinutes(5))
        return Results.Problem("Use a past time range of at most seven days. Ensure that from precedes to.", statusCode: 400);
    TimeZoneInfo zone;
    try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone ?? "UTC"); }
    catch (TimeZoneNotFoundException) { return Results.Problem("Supply a valid time zone.", statusCode: 400); }
    catch (InvalidTimeZoneException) { return Results.Problem("Supply a valid time zone.", statusCode: 400); }
    if (await repository.GetStatusAsync(deviceId, ct) is null) return Results.NotFound();
    var readings = await repository.GetAnalysisReadingsAsync(deviceId, start.AddMinutes(-5), end.AddMinutes(5), ct);
    return Results.Ok(DrinkingAnalyzer.Summarize(readings, start, end, zone));
}).Produces<DrinkingSummaryDto>().ProducesProblem(400).Produces(404);
Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> authorize = async (context, next) =>
{
    var supplied = context.HttpContext.Request.Headers["X-Device-Key"].ToString();
    var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
    return CryptographicOperations.FixedTimeEquals(expectedKeyHash, suppliedHash)
        ? await next(context) : Results.Unauthorized();
};
var writes = app.MapGroup("/api/v1/devices").AddEndpointFilter(authorize);
writes.MapPost("/", async (DeviceDto request, BowlRepository repository, CancellationToken ct) =>
{
    if (request.DeviceId == Guid.Empty ||
        string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200 || !ValidCalibration(request.Calibration))
        return Results.Problem("Supply a nonempty device GUID, a name, and a valid calibration.", statusCode: 400);
    return await repository.CreateDeviceAsync(request, ct)
        ? Results.Created($"/api/v1/devices/{request.DeviceId:D}/status", request)
        : Results.Conflict();
});
writes.MapPost("/{deviceId}/calibration", async (Guid deviceId, CalibrationDto request, BowlRepository repository, CancellationToken ct) =>
{
    if (!ValidCalibration(request)) return Results.Problem("Use a signed 24-bit zero offset and a nonzero calibration factor.", statusCode: 400);
    return await repository.UpdateCalibrationAsync(deviceId, request, ct) ? Results.Ok(request) : Results.NotFound();
});
writes.MapPost("/{deviceId}/readings:batch", async (Guid deviceId, ReadingBatchRequest request, BowlRepository repository, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.BatchId) || request.BatchId.Length > 100 || request.Readings is not { Length: > 0 and <= 1000 } ||
        request.Readings.Any(r => r is null || r.Sequence < 0 || r.ObservedAt == default || r.RawReading is < -8388608 or > 8388607) ||
        request.Readings.Select(r => r.Sequence).Distinct().Count() != request.Readings.Length)
        return Results.Problem("Supply a batch ID and 1 to 1000 readings. Use unique, nonnegative sequences, timestamps, and signed 24-bit raw readings.", statusCode: 400);
    var result = await repository.IngestAsync(deviceId, request, ct);
    if (result.Conflict) return Results.Problem("A stored sequence contains different reading values.", statusCode: 409);
    return result.Response is { } response ? Results.Ok(response) : Results.NotFound();
});

app.Run();

static bool ValidCalibration(CalibrationDto? value) => value is { ZeroOffset: >= -8388608 and <= 8388607, CalibrationFactor: not 0 };

public partial class Program;
