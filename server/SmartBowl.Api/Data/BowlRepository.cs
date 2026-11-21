using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using SmartBowl.Contracts;

namespace SmartBowl.Api.Data;

public sealed class BowlRepository(NpgsqlDataSource dataSource)
{
    public async Task<DeviceDto[]> GetDevicesAsync(CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("SELECT device_id, name, zero_offset, calibration_factor FROM devices ORDER BY device_id");
        await using var reader = await command.ExecuteReaderAsync(ct);
        var devices = new List<DeviceDto>();
        while (await reader.ReadAsync(ct)) devices.Add(ReadDevice(reader));
        return devices.ToArray();
    }

    public async Task<bool> CreateDeviceAsync(DeviceDto device, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            INSERT INTO devices (device_id, name, zero_offset, calibration_factor) VALUES ($1, $2, $3, $4)
            ON CONFLICT DO NOTHING
            """);
        command.Parameters.AddWithValue(device.DeviceId);
        command.Parameters.AddWithValue(device.Name);
        command.Parameters.AddWithValue(device.Calibration.ZeroOffset);
        command.Parameters.AddWithValue(device.Calibration.CalibrationFactor);
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<DeviceStatusDto?> GetStatusAsync(Guid deviceId, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT d.device_id, d.name, d.zero_offset, d.calibration_factor, d.last_seen_at,
                r.sequence, r.observed_at, r.weight_grams, r.raw_reading
            FROM devices d LEFT JOIN LATERAL (
                SELECT sequence, observed_at, weight_grams, raw_reading FROM weight_readings
                WHERE device_id = d.device_id ORDER BY observed_at DESC, sequence DESC LIMIT 1
            ) r ON true WHERE d.device_id = $1
            """);
        command.Parameters.AddWithValue(deviceId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var device = ReadDevice(reader);
        return new(device.DeviceId, device.Name, device.Calibration,
            reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4),
            reader.IsDBNull(5) ? null : ReadReading(reader, 5));
    }

    public async Task<bool> UpdateCalibrationAsync(Guid deviceId, CalibrationDto calibration, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE devices SET zero_offset = $2, calibration_factor = $3 WHERE device_id = $1
            """);
        command.Parameters.AddWithValue(deviceId);
        command.Parameters.AddWithValue(calibration.ZeroOffset);
        command.Parameters.AddWithValue(calibration.CalibrationFactor);
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<ReadingsPageDto> GetReadingsAsync(Guid deviceId, DateTimeOffset? from, DateTimeOffset? to,
        long afterSequence, int limit, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT sequence, observed_at, weight_grams, raw_reading FROM weight_readings
            WHERE device_id = $1 AND sequence > $2
                AND ($3::timestamptz IS NULL OR observed_at >= $3)
                AND ($4::timestamptz IS NULL OR observed_at < $4)
            ORDER BY sequence LIMIT $5
            """);
        command.Parameters.AddWithValue(deviceId);
        command.Parameters.AddWithValue(afterSequence);
        command.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, (object?)from?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, (object?)to?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue(limit + 1);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var readings = new List<WeightReadingDto>();
        while (await reader.ReadAsync(ct)) readings.Add(ReadReading(reader));
        var hasMore = readings.Count > limit;
        if (hasMore) readings.RemoveAt(limit);
        return new(readings.ToArray(), hasMore ? readings[^1].Sequence : null);
    }

    public async Task<IngestResult> IngestAsync(Guid deviceId, ReadingBatchRequest request, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using var deviceLock = new NpgsqlCommand("SELECT device_id FROM devices WHERE device_id = $1 FOR UPDATE", connection, transaction);
        deviceLock.Parameters.AddWithValue(deviceId);
        if (await deviceLock.ExecuteScalarAsync(ct) is null) return new(null, false);

        var json = JsonSerializer.Serialize(request.Readings.Select(r => r with { ObservedAt = r.ObservedAt.ToUniversalTime() }), JsonSerializerOptions.Web);
        const string source = """
            jsonb_to_recordset($2::jsonb) AS incoming("sequence" bigint, "observedAt" timestamptz, "weightGrams" numeric, "rawReading" numeric)
            """;
        await using var check = new NpgsqlCommand($"""
            SELECT EXISTS (
                SELECT 1 FROM {source}
                JOIN weight_readings stored ON stored.device_id = $1 AND stored.sequence = incoming.sequence
                WHERE stored.observed_at IS DISTINCT FROM incoming."observedAt"
                   OR stored.weight_grams IS DISTINCT FROM incoming."weightGrams"
                   OR stored.raw_reading IS DISTINCT FROM incoming."rawReading"
            )
            """, connection, transaction);
        check.Parameters.AddWithValue(deviceId);
        check.Parameters.AddWithValue(json);
        if ((bool)(await check.ExecuteScalarAsync(ct))!) return new(null, true);

        await using var insert = new NpgsqlCommand($"""
            INSERT INTO weight_readings (device_id, sequence, observed_at, weight_grams, raw_reading)
            SELECT $1, incoming.sequence, incoming."observedAt", incoming."weightGrams", incoming."rawReading"
            FROM {source} ON CONFLICT DO NOTHING
            """, connection, transaction);
        insert.Parameters.AddWithValue(deviceId);
        insert.Parameters.AddWithValue(json);
        var inserted = await insert.ExecuteNonQueryAsync(ct);
        await using var seen = new NpgsqlCommand("UPDATE devices SET last_seen_at = now() WHERE device_id = $1", connection, transaction);
        seen.Parameters.AddWithValue(deviceId);
        await seen.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
        return new(new(request.BatchId, inserted, request.Readings.Length - inserted, AcknowledgedRanges(request.Readings)), false);
    }

    public static SequenceRangeDto[] AcknowledgedRanges(IEnumerable<WeightReadingDto> readings)
    {
        var ranges = new List<SequenceRangeDto>();
        foreach (var sequence in readings.Select(r => r.Sequence).Distinct().Order())
        {
            if (ranges.Count > 0 && ranges[^1].To < long.MaxValue && sequence == ranges[^1].To + 1)
                ranges[^1] = ranges[^1] with { To = sequence };
            else
                ranges.Add(new(sequence, sequence));
        }
        return ranges.ToArray();
    }

    private static DeviceDto ReadDevice(NpgsqlDataReader reader) =>
        new(reader.GetGuid(0), reader.GetString(1), new(reader.GetDecimal(2), reader.GetDecimal(3)));

    private static WeightReadingDto ReadReading(NpgsqlDataReader reader, int start = 0) =>
        new(reader.GetInt64(start), reader.GetFieldValue<DateTimeOffset>(start + 1),
            reader.GetDecimal(start + 2), reader.IsDBNull(start + 3) ? null : reader.GetDecimal(start + 3));
}

public sealed record IngestResult(ReadingBatchResponse? Response, bool Conflict);
