using System.Text.Json.Serialization;

namespace SmartBowl.Contracts;

public sealed record CalibrationDto(
    [property: JsonRequired, JsonPropertyName("zero_offset")] decimal ZeroOffset,
    [property: JsonRequired, JsonPropertyName("calibration_factor")] decimal CalibrationFactor);

public sealed record DeviceDto([property: JsonRequired] Guid DeviceId, string Name, CalibrationDto Calibration);

public sealed record DeviceStatusDto(
    Guid DeviceId,
    string Name,
    CalibrationDto Calibration,
    DateTimeOffset? LastSeenAt,
    WeightReadingDto? LatestReading);

public sealed record WeightReadingDto(
    [property: JsonRequired] long Sequence,
    [property: JsonRequired] DateTimeOffset ObservedAt,
    [property: JsonRequired] decimal WeightGrams,
    decimal? RawReading = null);

public sealed record ReadingBatchRequest(string BatchId, WeightReadingDto[] Readings);

public sealed record SequenceRangeDto(long From, long To);

public sealed record ReadingBatchResponse(
    string BatchId,
    int InsertedCount,
    int DuplicateCount,
    SequenceRangeDto[] AcknowledgedRanges);

public sealed record ReadingsPageDto(WeightReadingDto[] Readings, long? NextAfterSequence);
