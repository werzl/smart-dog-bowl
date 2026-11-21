using SmartBowl.Api.Analysis;
using SmartBowl.Contracts;
using Xunit;

namespace SmartBowl.Api.Tests;

public sealed class DrinkingAnalyzerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AStableDropProducesOneEventAndItsChangeDuration()
    {
        var readings = Samples([500, 500, 500, 490, 480, 470, 470, 470]);
        var summary = Analyze(readings);
        var drink = Assert.Single(summary.Events);
        Assert.Equal(30m, summary.EstimatedConsumedMilliliters);
        Assert.Equal(Start.AddSeconds(2), drink.StartedAt);
        Assert.Equal(Start.AddSeconds(5), drink.EndedAt);
        Assert.Equal(3d, drink.DurationSeconds);
        Assert.Equal(3d, summary.AverageDrinkDurationSeconds);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(3)]
    public void SparseSamplesDoNotInventDrinkDurations(int interval)
    {
        var summary = Analyze(Samples([500, 500, 500, 450, 450, 450], interval));
        Assert.Equal(50m, summary.EstimatedConsumedMilliliters);
        Assert.Null(Assert.Single(summary.Events).DurationSeconds);
        Assert.Null(summary.AverageDrinkDurationSeconds);
    }

    [Theory]
    [InlineData(new double[] { 500, 500.2, 499.8, 500, 500.1, 499.9 })]
    [InlineData(new double[] { 500, 500, 500, 498, 498, 498 })]
    [InlineData(new double[] { 500, 500, 500, 600, 600, 600 })]
    [InlineData(new double[] { 500, 500, 500, -100, -100, -100, 500, 500, 500 })]
    [InlineData(new double[] { 500, 500, 500, 20, 20, 20 })]
    [InlineData(new double[] { 500, 500, 500, 450, 500, 500, 500 })]
    public void NoiseSmallChangesRefillsAndHandlingDoNotCountAsConsumption(double[] weights)
    {
        var summary = Analyze(Samples(weights.Select(value => (decimal)value).ToArray()));
        Assert.Empty(summary.Events);
        Assert.Equal(0m, summary.EstimatedConsumedMilliliters);
    }

    [Fact]
    public void ALongDataGapDoesNotBecomeAClosingDrink()
    {
        var summary = Analyze(Samples([500, 500, 500, 450, 450, 450], 120));
        Assert.Empty(summary.Events);
        Assert.Equal(0, summary.ObservedCoverageSeconds);
    }

    [Fact]
    public void AChangeWithoutASettledEndingDoesNotCount()
    {
        Assert.Empty(Analyze(Samples([500, 500, 500, 490, 480, 470])).Events);
    }

    [Fact]
    public void APeriodIncludesOnlyCompleteEventsAndClipsCoverage()
    {
        var readings = Samples([500, 500, 500, 490, 480, 470, 470, 470]);
        var summary = DrinkingAnalyzer.Summarize(readings, Start.AddSeconds(3), Start.AddMinutes(1), TimeZoneInfo.Utc);
        Assert.Empty(summary.Events);
        Assert.Equal(4d, summary.ObservedCoverageSeconds);
    }

    [Fact]
    public void TimeOfDayUsesTheRequestedZoneAndExcludesUnobservedHours()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("test-plus-two", TimeSpan.FromHours(2), "Test", "Test");
        var readings = Enumerable.Range(0, 181).Select(index => new WeightReadingDto(index, Start.AddMinutes(index),
            index < 30 ? 500 : index < 90 ? 450 : 430)).ToArray();
        var summary = DrinkingAnalyzer.Summarize(readings, Start, Start.AddHours(4), zone);
        Assert.Equal(70m, summary.EstimatedConsumedMilliliters);
        Assert.Equal([2], summary.MostConsumedHours);
        Assert.Equal([4], summary.LeastConsumedHours);
        Assert.Equal(3 * 3600d, summary.ObservedCoverageSeconds);
        Assert.DoesNotContain(5, summary.LeastConsumedHours);
    }

    [Fact]
    public void EmptyDataDoesNotClaimQuietHoursOrConsumption()
    {
        var summary = Analyze([]);
        Assert.Equal(0m, summary.EstimatedConsumedMilliliters);
        Assert.Empty(summary.MostConsumedHours);
        Assert.Empty(summary.LeastConsumedHours);
        Assert.Null(summary.AverageDrinkDurationSeconds);
    }

    [Fact]
    public void SubsecondSamplesCanEstablishStablePlateaus()
    {
        var weights = Enumerable.Repeat(500m, 30).Concat(new[] { 490m, 480m, 470m }).Concat(Enumerable.Repeat(470m, 30)).ToArray();
        var readings = weights.Select((weight, index) => new WeightReadingDto(index, Start.AddMilliseconds(index * 100), weight)).ToArray();
        var summary = Analyze(readings);
        Assert.Equal(30m, summary.EstimatedConsumedMilliliters);
        Assert.Equal(0.3d, Assert.Single(summary.Events).DurationSeconds);
    }
    private static WeightReadingDto[] Samples(decimal[] weights, int interval = 1) =>
        weights.Select((weight, index) => new WeightReadingDto(index, Start.AddSeconds(index * interval), weight)).ToArray();

    private static DrinkingSummaryDto Analyze(WeightReadingDto[] readings) =>
        DrinkingAnalyzer.Summarize(readings, Start, Start.AddDays(1), TimeZoneInfo.Utc);
}
