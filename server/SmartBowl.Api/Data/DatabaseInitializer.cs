using Npgsql;

namespace SmartBowl.Api.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(NpgsqlDataSource dataSource, bool seed, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var setup = new NpgsqlCommand("""
            SELECT pg_advisory_xact_lock(836124);
            CREATE TABLE IF NOT EXISTS schema_migrations (version text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now());
            """, connection, transaction);
        await setup.ExecuteNonQueryAsync(cancellationToken);

        var assembly = typeof(DatabaseInitializer).Assembly;
        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.EndsWith(".sql", StringComparison.Ordinal)).Order())
        {
            await using var check = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM schema_migrations WHERE version = $1)", connection, transaction);
            check.Parameters.AddWithValue(resource);
            if ((bool)(await check.ExecuteScalarAsync(cancellationToken))!) continue;

            using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
            await using var migrate = new NpgsqlCommand(await reader.ReadToEndAsync(cancellationToken), connection, transaction);
            await migrate.ExecuteNonQueryAsync(cancellationToken);
            await using var record = new NpgsqlCommand("INSERT INTO schema_migrations (version) VALUES ($1)", connection, transaction);
            record.Parameters.AddWithValue(resource);
            await record.ExecuteNonQueryAsync(cancellationToken);
        }

        if (seed)
        {
            await using var command = new NpgsqlCommand("""
                WITH new_device AS (
                    INSERT INTO devices (device_id, name, zero_offset, calibration_factor, last_seen_at)
                    VALUES ('7e8c0d1a-6b39-47b5-928a-64f3f1039e21', 'Mock kitchen bowl', -120000, -420.5, now())
                    ON CONFLICT DO NOTHING RETURNING device_id
                ), samples AS (
                    SELECT device_id, n AS sequence,
                        date_trunc('minute', now()) - interval '24 hours' + n * interval '1 minute' AS observed_at,
                        900 - (n % 720) / 120 * 65 + ((n % 7) - 3) * 0.1 AS weight
                    FROM new_device CROSS JOIN generate_series(0, 1440) AS n
                )
                INSERT INTO weight_readings (device_id, sequence, observed_at, weight_grams, raw_reading)
                SELECT device_id, sequence, observed_at, weight, -120000 + weight * -420.5 FROM samples;
                """, connection, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
