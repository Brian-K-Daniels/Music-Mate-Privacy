using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

public class TimeSignatureTests
{
    [Theory]
    [InlineData("4/4", 4, 4.0)]
    [InlineData("3/4", 3, 3.0)]
    [InlineData("2/4", 2, 2.0)]
    [InlineData("2/2", 2, 4.0)]
    [InlineData("5/4", 5, 5.0)]
    [InlineData("3/8", 3, 1.5)]
    [InlineData("6/8", 6, 3.0)]
    [InlineData("9/8", 9, 4.5)]
    [InlineData("12/8", 12, 6.0)]
    public void TryParse_CommonMeters_HaveExpectedCapacity(
        string display,
        int expectedBeats,
        double expectedTotalBeats)
    {
        Assert.True(TimeSignature.TryParse(display, out var ts));
        Assert.Equal(expectedBeats, ts.Beats);
        Assert.Equal(expectedTotalBeats, ts.TotalBeats, precision: 3);
        Assert.Equal(display, ts.ToString());
    }

    [Fact]
    public void GetDisplayMeasureBeats_UsesSelectedMeter_ForGeneratedMusic()
    {
        var session = new NoteSessionService
        {
            Tune = "Selected Scale",
            MeterTimeSignature = "6/8",
        };

        Assert.Equal("6/8", session.GetDisplayTimeSignature());
        Assert.Equal(3.0, session.GetDisplayMeasureBeats(), precision: 3);
    }

    [Fact]
    public void GetDisplayMeasureBeats_PracticeTuneKeepsOwnMeter()
    {
        var tune = new PracticeTune("Minuet", TimeSignature.ThreeFour);
        var session = new NoteSessionService();
        session.SelectPracticeTune(tune);
        session.MeterTimeSignature = "6/8";

        Assert.Equal("3/4", session.GetDisplayTimeSignature());
        Assert.Equal(3.0, session.GetDisplayMeasureBeats(), precision: 3);
    }

    [Fact]
    public void CommonDisplayOptions_IncludeCompoundMeters()
    {
        Assert.Contains("6/8", TimeSignature.CommonDisplayOptions);
        Assert.Contains("9/8", TimeSignature.CommonDisplayOptions);
        Assert.Contains("12/8", TimeSignature.CommonDisplayOptions);
        Assert.Contains("2/2", TimeSignature.CommonDisplayOptions);
    }
}
