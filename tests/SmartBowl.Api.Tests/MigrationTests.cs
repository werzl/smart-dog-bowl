using Npgsql;
using SmartBowl.Api.Data;
using Xunit;

namespace SmartBowl.Api.Tests;

public sealed class MigrationTests
{
    [PostgresFact]
    public async Task GuidMigrationPreservesExistingDevicesReadingsAndConstraints()
    {
        var adminConnection = Environment.GetEnvironmentVariable("SMARTBOWL_TEST_DATABASE")!;
        var schema = "migration_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(adminConnection);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            var connectionString = new NpgsqlConnectionStringBuilder(adminConnection) { SearchPath = schema }.ConnectionString;
            await using var source = NpgsqlDataSource.Create(connectionString);
            var assembly = typeof(DatabaseInitializer).Assembly;
            var initialMigration = assembly.GetManifestResourceNames().Single(name => name.EndsWith("001_initial.sql", StringComparison.Ordinal));
            using var reader = new StreamReader(assembly.GetManifestResourceStream(initialMigration)!);
            await using (var initial = source.CreateCommand(await reader.ReadToEndAsync())) await initial.ExecuteNonQueryAsync();
            await using (var createHistory = source.CreateCommand("CREATE TABLE schema_migrations (version text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now())"))
                await createHistory.ExecuteNonQueryAsync();
            await using (var history = source.CreateCommand("INSERT INTO schema_migrations (version) VALUES ($1)"))
            {
                history.Parameters.AddWithValue(initialMigration);
                await history.ExecuteNonQueryAsync();
            }
            const string existingGuid = "35ca90ac-484c-4bdb-8bb0-15c99aab1663";
            await using (var data = source.CreateCommand("""
                INSERT INTO devices (device_id, name, zero_offset, calibration_factor, last_seen_at) VALUES
                    ('bowl-01', 'Original mock bowl', -120000, -420.5, '2026-11-21T12:00:00Z'),
                    ('esp32-kitchen', 'Existing device', 100, 25.5, '2026-11-21T13:00:00Z'),
                    ('35ca90ac-484c-4bdb-8bb0-15c99aab1663', 'Existing GUID', -100, -12.5, NULL);
                INSERT INTO weight_readings (device_id, sequence, observed_at, weight_grams, raw_reading)
                    SELECT device_id, 17, '2026-11-21T12:34:56.123456Z', 81.25, 777.75 FROM devices;
                """)) await data.ExecuteNonQueryAsync();

            await DatabaseInitializer.InitializeAsync(source, true, default);
            await DatabaseInitializer.InitializeAsync(source, true, default);

            await using (var check = source.CreateCommand("""
                SELECT d.device_id, d.legacy_device_id, d.name, d.zero_offset, d.calibration_factor,
                    r.sequence, r.observed_at, r.weight_grams, r.raw_reading,
                    pg_typeof(d.device_id)::text, pg_typeof(r.device_id)::text
                FROM devices d JOIN weight_readings r ON d.device_id = r.device_id ORDER BY legacy_device_id;
                """))
            await using (var rows = await check.ExecuteReaderAsync())
            {
                var ids = new Dictionary<string, Guid>();
                while (await rows.ReadAsync())
                {
                    ids.Add(rows.GetString(1), rows.GetGuid(0));
                    Assert.NotEqual(Guid.Empty, rows.GetGuid(0));
                    Assert.Equal(17L, rows.GetInt64(5));
                    Assert.Equal(DateTimeOffset.Parse("2026-11-21T12:34:56.123456Z"), rows.GetFieldValue<DateTimeOffset>(6));
                    Assert.Equal(81.25m, rows.GetDecimal(7));
                    Assert.Equal(777.75m, rows.GetDecimal(8));
                    Assert.Equal("uuid", rows.GetString(9));
                    Assert.Equal("uuid", rows.GetString(10));
                    if (rows.GetString(1) == "bowl-01")
                    {
                        Assert.Equal("Original mock bowl", rows.GetString(2));
                        Assert.Equal(-120000m, rows.GetDecimal(3));
                        Assert.Equal(-420.5m, rows.GetDecimal(4));
                    }
                }
                Assert.Equal(3, ids.Count);
                Assert.Equal(Guid.Parse(existingGuid), ids[existingGuid]);
                Assert.Equal(Guid.Parse("7e8c0d1a-6b39-47b5-928a-64f3f1039e21"), ids["bowl-01"]);
            }

            await using var orphan = source.CreateCommand("""
                INSERT INTO weight_readings (device_id, sequence, observed_at, weight_grams)
                VALUES (gen_random_uuid(), 1, now(), 1);
                """);
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation,
                (await Assert.ThrowsAsync<PostgresException>(() => orphan.ExecuteNonQueryAsync())).SqlState);
            await using var empty = source.CreateCommand("""
                INSERT INTO devices (device_id, name, zero_offset, calibration_factor)
                VALUES ('00000000-0000-0000-0000-000000000000', 'Empty', 0, 1);
                """);
            Assert.Equal(PostgresErrorCodes.CheckViolation,
                (await Assert.ThrowsAsync<PostgresException>(() => empty.ExecuteNonQueryAsync())).SqlState);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
