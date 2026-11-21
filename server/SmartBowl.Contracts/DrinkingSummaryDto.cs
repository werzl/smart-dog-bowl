namespace SmartBowl.Contracts;

public sealed record DrinkingEventDto(
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    decimal EstimatedMilliliters,
    double? DurationSeconds);

public sealed record DrinkingSummaryDto(
    DateTimeOffset From,
    DateTimeOffset To,
    string TimeZone,
    decimal EstimatedConsumedMilliliters,
    int[] MostConsumedHours,
    int[] LeastConsumedHours,
    double? AverageDrinkDurationSeconds,
    double ObservedCoverageSeconds,
    DrinkingEventDto[] Events,
    string ClassifierVersion);
