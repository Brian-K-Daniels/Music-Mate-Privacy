using musicmate.Services;

namespace musicmate.Tests;

public class IntervalEarTrainingCatalogTests
{
    [Fact]
    public void Intervals_AreThirteenEntries_ZeroThroughTwelve()
    {
        Assert.Equal(13, IntervalEarTrainingCatalog.Intervals.Count);
        for (int i = 0; i <= 12; i++)
            Assert.Equal(i, IntervalEarTrainingCatalog.Intervals[i].Semitones);
    }

    [Theory]
    [InlineData(0, "Perfect unison")]
    [InlineData(3, "Minor third")]
    [InlineData(6, "Tritone")]
    [InlineData(12, "Perfect octave")]
    public void GetName_MapsSemitones(int semitones, string expected)
        => Assert.Equal(expected, IntervalEarTrainingCatalog.GetName(semitones));

    [Fact]
    public void FormatButtonLabel_IncludesSemitonesAndName()
        => Assert.Equal("3 — Minor third", IntervalEarTrainingCatalog.FormatButtonLabel(3));

    [Fact]
    public void FormatButtonLabel_LongestNames_StaySingleLine()
    {
        Assert.Equal("0 — Perfect unison", IntervalEarTrainingCatalog.FormatButtonLabel(0));
        Assert.Equal("10 — Minor seventh", IntervalEarTrainingCatalog.FormatButtonLabel(10));
        Assert.Equal("11 — Major seventh", IntervalEarTrainingCatalog.FormatButtonLabel(11));
        Assert.Equal("12 — Perfect octave", IntervalEarTrainingCatalog.FormatButtonLabel(12));
        Assert.DoesNotContain('\n', IntervalEarTrainingCatalog.FormatButtonLabel(12));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(12, true)]
    [InlineData(13, false)]
    public void IsValidSemitoneCount(int value, bool expected)
        => Assert.Equal(expected, IntervalEarTrainingCatalog.IsValidSemitoneCount(value));
}
