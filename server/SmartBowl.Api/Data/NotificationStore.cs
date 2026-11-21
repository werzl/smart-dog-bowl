using Npgsql;
using SmartBowl.Contracts;
using SmartBowl.Api.Notifications;

namespace SmartBowl.Api.Data;

public sealed class NotificationStore(NpgsqlDataSource source)
{
    public static async Task ProcessReadingsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid deviceId, IEnumerable<WeightReadingDto> readings, CancellationToken ct)
    {
        foreach (var reading in readings.OrderBy(r => r.ObservedAt).ThenBy(r => r.Sequence))
        {
            if (reading.ObservedAt > DateTimeOffset.UtcNow.AddMinutes(1)) continue;
            await using var state = new NpgsqlCommand("""
                SELECT observed_at, sequence, empty FROM bowl_alert_state WHERE device_id = $1
                """, connection, transaction);
            state.Parameters.AddWithValue(deviceId);
            var empty = false;
            await using (var reader = await state.ExecuteReaderAsync(ct))
            {
                if (await reader.ReadAsync(ct))
                {
                    var time = reader.GetFieldValue<DateTimeOffset>(0);
                    if (reading.ObservedAt < time || (reading.ObservedAt == time && reading.Sequence <= reader.GetInt64(1))) continue;
                    empty = reader.GetBoolean(2);
                }
            }
            // Ignore old uploads and a removed bowl. Retain the alert until a refill arrives.
            var current = reading.ObservedAt >= DateTimeOffset.UtcNow.AddMinutes(-5)
                && reading.ObservedAt <= DateTimeOffset.UtcNow.AddMinutes(1);
            var nextEmpty = current && reading.WeightGrams >= -20
                ? reading.WeightGrams <= 10 || (empty && reading.WeightGrams < 50) : empty;
            if (nextEmpty && !empty)
            {
                await using var alert = new NpgsqlCommand("""
                    WITH alert AS (
                        INSERT INTO bowl_notifications(id, device_id, created_at, weight_grams)
                        VALUES ($1, $2, $3, $4) RETURNING id
                    )
                    INSERT INTO web_push_deliveries(notification_id, subscription_id)
                    SELECT alert.id, subscriptions.id FROM alert CROSS JOIN web_push_subscriptions subscriptions
                    """, connection, transaction);
                alert.Parameters.AddWithValue(Guid.NewGuid());
                alert.Parameters.AddWithValue(deviceId);
                alert.Parameters.AddWithValue(reading.ObservedAt.ToUniversalTime());
                alert.Parameters.AddWithValue(reading.WeightGrams);
                await alert.ExecuteNonQueryAsync(ct);
            }
            else if (!nextEmpty && empty)
            {
                await using var resolve = new NpgsqlCommand("""
                    WITH resolved AS (
                        UPDATE bowl_notifications SET resolved_at = $2 WHERE device_id = $1 AND resolved_at IS NULL RETURNING id
                    )
                    UPDATE web_push_deliveries SET finished_at = now() WHERE finished_at IS NULL
                        AND notification_id IN (SELECT id FROM resolved)
                    """, connection, transaction);
                resolve.Parameters.AddWithValue(deviceId);
                resolve.Parameters.AddWithValue(reading.ObservedAt.ToUniversalTime());
                await resolve.ExecuteNonQueryAsync(ct);
            }
            await using var save = new NpgsqlCommand("""
                INSERT INTO bowl_alert_state VALUES ($1, $2, $3, $4)
                ON CONFLICT (device_id) DO UPDATE SET observed_at = $2, sequence = $3, empty = $4
                """, connection, transaction);
            save.Parameters.AddWithValue(deviceId);
            save.Parameters.AddWithValue(reading.ObservedAt.ToUniversalTime());
            save.Parameters.AddWithValue(reading.Sequence);
            save.Parameters.AddWithValue(nextEmpty);
            await save.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<BowlNotificationDto[]> ListAsync(CancellationToken ct)
    {
        await using var command = source.CreateCommand("""
            SELECT n.id, n.device_id, d.name, n.created_at, n.resolved_at, n.weight_grams
            FROM bowl_notifications n JOIN devices d USING(device_id)
            ORDER BY (n.resolved_at IS NULL) DESC, n.created_at DESC LIMIT 100
            """);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var notifications = new List<BowlNotificationDto>();
        while (await reader.ReadAsync(ct)) notifications.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
            reader.GetFieldValue<DateTimeOffset>(3), reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4), reader.GetDecimal(5)));
        return notifications.ToArray();
    }

    public async Task<PushIdentity> IdentityAsync(string subject, CancellationToken ct)
    {
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var check = new NpgsqlCommand("SELECT pg_advisory_xact_lock(836125)", connection, transaction);
        await check.ExecuteNonQueryAsync(ct);
        await using var read = new NpgsqlCommand("SELECT public_key, private_key FROM web_push_identity", connection, transaction);
        string publicKey, privateKey;
        await using (var reader = await read.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct)) { publicKey = reader.GetString(0); privateKey = reader.GetString(1); }
            else { var keys = PushIdentity.Create(subject); publicKey = keys.PublicKey; privateKey = keys.PrivateKey; }
        }
        await using var save = new NpgsqlCommand("INSERT INTO web_push_identity(singleton, public_key, private_key) VALUES (true, $1, $2) ON CONFLICT DO NOTHING", connection, transaction);
        save.Parameters.AddWithValue(publicKey);
        save.Parameters.AddWithValue(privateKey);
        await save.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return new(subject, publicKey, privateKey);
    }

    public async Task SubscribeAsync(PushSubscriptionRequest request, CancellationToken ct)
    {
        await using var command = source.CreateCommand("""
            WITH subscription AS (
                INSERT INTO web_push_subscriptions(id, endpoint, p256dh, auth) VALUES ($1, $2, $3, $4)
                ON CONFLICT(endpoint) DO UPDATE SET p256dh = $3, auth = $4 RETURNING id
            )
            INSERT INTO web_push_deliveries(notification_id, subscription_id)
            SELECT n.id, s.id FROM bowl_notifications n CROSS JOIN subscription s WHERE n.resolved_at IS NULL
            ON CONFLICT DO NOTHING
            """);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(request.Endpoint);
        command.Parameters.AddWithValue(request.Keys.P256dh);
        command.Parameters.AddWithValue(request.Keys.Auth);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task RemoveAsync(string endpoint, CancellationToken ct)
    {
        await using var command = source.CreateCommand("DELETE FROM web_push_subscriptions WHERE endpoint = $1");
        command.Parameters.AddWithValue(endpoint);
        await command.ExecuteNonQueryAsync(ct);
    }
}
