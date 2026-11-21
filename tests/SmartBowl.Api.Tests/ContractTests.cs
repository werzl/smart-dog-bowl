using System.Text.Json;
using SmartBowl.Api.Data;
using SmartBowl.Contracts;
using Xunit;

namespace SmartBowl.Api.Tests;

public sealed class ContractTests
{
    [Fact]
    public void CalibrationUsesThePythonJsonKeys()
    {
        const string json = """{"zero_offset":-120000.5,"calibration_factor":-420.5}""";
        var calibration = JsonSerializer.Deserialize<CalibrationDto>(json)!;
        Assert.Equal(-120000.5m, calibration.ZeroOffset);
        Assert.Equal(-420.5m, calibration.CalibrationFactor);
        Assert.Equal(json, JsonSerializer.Serialize(calibration));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"sequence\":0,\"observedAt\":\"2026-11-21T12:00:00Z\"}")]
    public void ReadingsRequireTheMeasuredValues(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WeightReadingDto>(json, JsonSerializerOptions.Web));

    [Fact]
    public void AcknowledgementsPreserveGapsAndTheMaximumSequence()
    {
        var sequences = new[] { long.MaxValue, 12, 2, 1, 10, 11, 2 };
        var readings = sequences.Select(sequence => new WeightReadingDto(sequence, DateTimeOffset.UtcNow, 0));
        Assert.Equal(new[] { new SequenceRangeDto(1, 2), new(10, 12), new(long.MaxValue, long.MaxValue) },
            BowlRepository.AcknowledgedRanges(readings));
    }
}
