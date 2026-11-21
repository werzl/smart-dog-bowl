using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using SmartBowl.Api.Data;
using SmartBowl.Contracts;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;

namespace SmartBowl.Api.Notifications;

public sealed record PushIdentity(string Subject, string PublicKey, string PrivateKey)
{
    public static PushIdentity Create(string subject)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(true);
        static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return new(subject, Encode([4, ..parameters.Q.X!, ..parameters.Q.Y!]), Encode(parameters.D!));
    }
}

public static class PushValidation
{
    public static bool ValidSubject(string? subject) => Uri.TryCreate(subject, UriKind.Absolute, out var uri)
        && (uri.Scheme == "https" || (uri.Scheme == "mailto" && uri.OriginalString.Contains('@')));

    public static bool ValidEndpoint(string? endpoint) => endpoint?.Length <= 4096
        && Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme == "https"
        && uri.Port == 443 && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0
        && (uri.Host == "fcm.googleapis.com" || uri.Host.EndsWith(".push.services.mozilla.com", StringComparison.Ordinal)
            || uri.Host.EndsWith(".notify.windows.com", StringComparison.Ordinal)
            || uri.Host == "web.push.apple.com" || uri.Host.EndsWith(".push.apple.com", StringComparison.Ordinal));

    public static bool ValidSubscription(PushSubscriptionRequest request)
    {
        if (!ValidEndpoint(request.Endpoint) || request.Keys is null) return false;
        try
        {
            var key = Decode(request.Keys.P256dh);
            if (key.Length != 65 || key[0] != 4 || Decode(request.Keys.Auth).Length != 16) return false;
            using var curve = ECDiffieHellman.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = key[1..33], Y = key[33..65] }
            });
            return true;
        }
        catch (Exception error) when (error is FormatException or CryptographicException or ArgumentException) { return false; }
    }

    private static byte[] Decode(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 100) throw new FormatException();
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight((padded.Length + 3) / 4 * 4, '='));
    }
}

public interface IPushSender
{
    Task SendAsync(PushSubscriptionRequest subscription, string payload, PushIdentity identity, CancellationToken ct);
}

public sealed class WebPushSender : IPushSender, IDisposable
{
    private readonly HttpClient http;
    public WebPushSender() : this(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) }) { }
    public WebPushSender(HttpClient httpClient) => http = httpClient;
    public async Task SendAsync(PushSubscriptionRequest subscription, string payload, PushIdentity identity, CancellationToken ct)
    {
        using var authentication = new VapidAuthentication(identity.PublicKey, identity.PrivateKey) { Subject = identity.Subject };
        var client = new PushServiceClient(http) { AutoRetryAfter = false };
        await client.RequestPushMessageDeliveryAsync(new PushSubscription
        {
            Endpoint = subscription.Endpoint,
            Keys = new Dictionary<string, string> { ["p256dh"] = subscription.Keys.P256dh, ["auth"] = subscription.Keys.Auth }
        }, new PushMessage(payload) { TimeToLive = 60, Urgency = PushMessageUrgency.High }, authentication, ct);
    }
    public void Dispose() => http.Dispose();
}

public sealed class PushDeliveryWorker(NpgsqlDataSource source, NotificationStore store, IPushSender sender,
    IConfiguration configuration, ILogger<PushDeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await DispatchOneAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning("The push delivery cycle failed ({ErrorType}).", error.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    public async Task<bool> DispatchOneAsync(CancellationToken ct)
    {
        var subject = configuration["WebPush:Subject"];
        if (!PushValidation.ValidSubject(subject)) return false;
        var identity = await store.IdentityAsync(subject!, ct);
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        // Lock the device first, as ingestion does. A refill cannot race the send.
        await using var command = new NpgsqlCommand("""
            SELECT q.notification_id, q.subscription_id, s.endpoint, s.p256dh, s.auth, d.name, q.attempts, n.created_at
            FROM web_push_deliveries q JOIN bowl_notifications n ON n.id = q.notification_id
            JOIN web_push_subscriptions s ON s.id = q.subscription_id JOIN devices d ON d.device_id = n.device_id
            WHERE q.finished_at IS NULL AND q.next_attempt_at <= now() AND n.resolved_at IS NULL
            ORDER BY q.next_attempt_at LIMIT 1 FOR UPDATE OF d, q SKIP LOCKED
            """, connection, transaction);
        Guid notificationId, subscriptionId;
        string endpoint, p256dh, auth, name;
        int attempts;
        DateTimeOffset createdAt;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return false;
            notificationId = reader.GetGuid(0); subscriptionId = reader.GetGuid(1);
            endpoint = reader.GetString(2); p256dh = reader.GetString(3); auth = reader.GetString(4);
            name = reader.GetString(5); attempts = reader.GetInt32(6); createdAt = reader.GetFieldValue<DateTimeOffset>(7);
        }
        var finished = false;
        var expired = false;
        if (createdAt < DateTimeOffset.UtcNow.AddMinutes(-5) || !PushValidation.ValidEndpoint(endpoint)) finished = true;
        else
        {
            try
            {
                await sender.SendAsync(new(endpoint, new(p256dh, auth)), JsonSerializer.Serialize(new
                {
                    id = notificationId, title = "Empty bowl reported", body = $"{name} needs water. Check the bowl.",
                    createdAt, url = "/#overview"
                }), identity, ct);
                finished = true;
            }
            catch (PushServiceClientException error) when (error.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
            { expired = true; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is PushServiceClientException or HttpRequestException or OperationCanceledException)
            { logger.LogWarning("Push delivery failed for alert {AlertId}. Attempt {Attempt} will retry.", notificationId, attempts + 1); }
        }
        await using var update = new NpgsqlCommand(expired
            ? "DELETE FROM web_push_subscriptions WHERE id = $2"
            : """
                UPDATE web_push_deliveries SET attempts = attempts + 1,
                    finished_at = CASE WHEN $3 THEN now() ELSE NULL END,
                    next_attempt_at = now() + $4 * interval '1 second'
                WHERE notification_id = $1 AND subscription_id = $2
                """, connection, transaction);
        // Positional parameters must remain present for either statement.
        if (expired)
        {
            update.CommandText = "DELETE FROM web_push_subscriptions WHERE id = $1";
            update.Parameters.AddWithValue(subscriptionId);
        }
        else
        {
            update.Parameters.AddWithValue(notificationId); update.Parameters.AddWithValue(subscriptionId);
            update.Parameters.AddWithValue(finished); update.Parameters.AddWithValue(Math.Min(300, 5 * (int)Math.Pow(2, Math.Min(attempts, 6))));
        }
        await update.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }
}
