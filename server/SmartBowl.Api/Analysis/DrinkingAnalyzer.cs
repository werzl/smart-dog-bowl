using SmartBowl.Contracts;

namespace SmartBowl.Api.Analysis;

public static class DrinkingAnalyzer
{
    public const string Version = "plateau-v1";
    private const decimal StableToleranceGrams = 1m;
    private const decimal MinimumDrinkGrams = 5m;
    private const decimal MaximumDrinkGrams = 250m;
    private static readonly TimeSpan MaximumGap = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan MaximumEvent = TimeSpan.FromMinutes(5);

    public static DrinkingSummaryDto Summarize(IEnumerable<WeightReadingDto> source,
        DateTimeOffset from, DateTimeOffset to, TimeZoneInfo timeZone)
    {
        var readings = source.OrderBy(reading => reading.ObservedAt).ThenBy(reading => reading.Sequence)
            .GroupBy(reading => reading.ObservedAt).Select(group => group.Last()).ToArray();
        var events = DetectEvents(readings).Where(drink => drink.StartedAt >= from && drink.EndedAt < to).ToArray();
        var coverage = new double[24];
        for (var index = 1; index < readings.Length; index++)
        {
            var previous = readings[index - 1].ObservedAt;
            var current = readings[index].ObservedAt;
            if (current - previous > MaximumGap) continue;
            var cursor = previous < from ? from : previous;
            var end = current > to ? to : current;
            while (cursor < end)
            {
                var nextMinute = new DateTimeOffset((cursor.UtcTicks / TimeSpan.TicksPerMinute + 1) * TimeSpan.TicksPerMinute, TimeSpan.Zero);
                var next = nextMinute < end ? nextMinute : end;
                coverage[TimeZoneInfo.ConvertTime(cursor, timeZone).Hour] += (next - cursor).TotalSeconds;
                cursor = next;
            }
        }

        var volumes = new decimal[24];
        foreach (var drink in events) volumes[TimeZoneInfo.ConvertTime(drink.StartedAt, timeZone).Hour] += drink.EstimatedMilliliters;
        var observedHours = Enumerable.Range(0, 24).Where(hour => coverage[hour] >= 1800).ToArray();
        var rates = observedHours.ToDictionary(hour => hour, hour => volumes[hour] / (decimal)(coverage[hour] / 3600));
        var total = events.Sum(drink => drink.EstimatedMilliliters);
        int[] most = [], least = [];
        if (total > 0 && observedHours.Length >= 2)
        {
            var maximum = rates.Values.Max();
            var minimum = rates.Values.Min();
            most = observedHours.Where(hour => maximum > 0 && Math.Abs(rates[hour] - maximum) <= 0.1m).ToArray();
            least = observedHours.Where(hour => Math.Abs(rates[hour] - minimum) <= 0.1m).ToArray();
        }
        var durations = events.Where(drink => drink.DurationSeconds.HasValue).Select(drink => drink.DurationSeconds!.Value).ToArray();
        return new(from, to, timeZone.Id, decimal.Round(total, 1), most, least,
            durations.Length > 0 ? durations.Average() : null, coverage.Sum(), events, Version);
    }

    private static IEnumerable<DrinkingEventDto> DetectEvents(WeightReadingDto[] readings)
    {
        decimal? baseline = null;
        var stableIndex = -1;
        var changeStart = -1;
        var segmentStart = 0;
        for (var index = 0; index < readings.Length; index++)
        {
            if (index > 0 && readings[index].ObservedAt - readings[index - 1].ObservedAt > MaximumGap)
            {
                baseline = null;
                stableIndex = -1;
                changeStart = -1;
                segmentStart = index;
            }
            if (baseline.HasValue && changeStart < 0 && Math.Abs(readings[index].WeightGrams - baseline.Value) > StableToleranceGrams)
                changeStart = stableIndex;
            if (index - segmentStart < 2) continue;
            var stableStart = index - 2;
            while (stableStart > segmentStart && readings[index].ObservedAt - readings[stableStart].ObservedAt < TimeSpan.FromSeconds(2)) stableStart--;
            if (readings[index].ObservedAt - readings[stableStart].ObservedAt < TimeSpan.FromSeconds(2)) continue;
            var weights = readings[stableStart..(index + 1)].Select(reading => reading.WeightGrams).ToArray();
            if (weights.Max() - weights.Min() > StableToleranceGrams) continue;
            Array.Sort(weights);
            var settled = weights[weights.Length / 2];
            if (baseline.HasValue && changeStart >= 0)
            {
                var endIndex = stableStart;
                var drop = baseline.Value - settled;
                var duration = readings[endIndex].ObservedAt - readings[changeStart].ObservedAt;
                var disturbed = false;
                var reliableDuration = true;
                for (var sample = changeStart; sample <= index; sample++)
                {
                    if (readings[sample].WeightGrams > baseline.Value + StableToleranceGrams || readings[sample].WeightGrams < settled - StableToleranceGrams)
                        disturbed = true;
                    if (sample > changeStart && readings[sample].ObservedAt - readings[sample - 1].ObservedAt > TimeSpan.FromSeconds(2))
                        reliableDuration = false;
                }
                if (drop is >= MinimumDrinkGrams and <= MaximumDrinkGrams && baseline.Value >= 0 && settled >= 0 &&
                    duration > TimeSpan.Zero && duration <= MaximumEvent && !disturbed)
                    yield return new(readings[changeStart].ObservedAt, readings[endIndex].ObservedAt,
                        decimal.Round(drop, 2), reliableDuration ? duration.TotalSeconds : null);
            }
            baseline = settled;
            stableIndex = index;
            changeStart = -1;
        }
    }
}
