using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

public class ConductorBeatHelperTests
{
    public static TheoryData<string, int, double[]> ConductedBeatOffsetCases { get; } = new()
    {
        { "4/4", 4, new[] { 0.0, 1.0, 2.0, 3.0 } },
        { "3/4", 3, new[] { 0.0, 1.0, 2.0 } },
        { "2/4", 2, new[] { 0.0, 1.0 } },
        { "6/8", 2, new[] { 0.0, 1.5 } },
        { "9/8", 3, new[] { 0.0, 1.5, 3.0 } },
        { "12/8", 4, new[] { 0.0, 1.5, 3.0, 4.5 } },
    };

    [Theory]
    [MemberData(nameof(ConductedBeatOffsetCases))]
    public void GetConductedBeatOffsets_MatchesMeter(
        string signature,
        int expectedCount,
        double[] expectedOffsets)
    {
        Assert.True(ConductorBeatHelper.TryParseDisplayTimeSignature(signature, out var ts));
        var offsets = ConductorBeatHelper.GetConductedBeatOffsetsInMeasure(ts);

        Assert.Equal(expectedCount, offsets.Count);
        Assert.Equal(expectedOffsets.Length, offsets.Count);
        for (int i = 0; i < expectedOffsets.Length; i++)
            Assert.Equal(expectedOffsets[i], offsets[i], precision: 3);
    }

    [Fact]
    public void GetConductedBeatStartForRelativeBeat_6_8_UsesDottedQuarterBeats()
    {
        var ts = new TimeSignature(6, NoteDuration.Eighth);
        var measureStarts = new[] { 0.0, 3.0 };

        Assert.Equal(0.0, ConductorBeatHelper.GetConductedBeatStartForRelativeBeat(0.25, ts, measureStarts));
        Assert.Equal(1.5, ConductorBeatHelper.GetConductedBeatStartForRelativeBeat(1.75, ts, measureStarts));
        Assert.Equal(3.0, ConductorBeatHelper.GetConductedBeatStartForRelativeBeat(3.1, ts, measureStarts));
    }

    [Fact]
    public void BeatOffsetToXInMeasure_UsesTimeProportionalLane()
    {
        float x0 = ConductorBeatHelper.BeatOffsetToXInMeasure(100f, 200f, 0.0, 4.0, 16f);
        float x2 = ConductorBeatHelper.BeatOffsetToXInMeasure(100f, 200f, 2.0, 4.0, 16f);
        float x1 = ConductorBeatHelper.BeatOffsetToXInMeasure(100f, 200f, 1.0, 4.0, 16f);

        Assert.True(x0 < x1);
        Assert.True(x1 < x2);
        Assert.Equal((x0 + x2) * 0.5f, x1, precision: 1);
    }

    [Theory]
    [InlineData("4/4", false)]
    [InlineData("6/8", true)]
    [InlineData("9/8", true)]
    [InlineData("12/8", true)]
    public void IsCompoundConductedMeter_ClassifiesExpectedSignatures(string signature, bool expectedCompound)
    {
        Assert.True(ConductorBeatHelper.TryParseDisplayTimeSignature(signature, out var ts));
        Assert.Equal(expectedCompound, ConductorBeatHelper.IsCompoundConductedMeter(ts));
    }
}
