using musicmate.Services;

namespace musicmate.Tests;

public class TunerPageHeadingTests
{
    [Fact]
    public void StatusBesideHeading_DropsTheRepeatedTunerWord()
    {
        Assert.Equal(string.Empty, TunerPageHeading.StatusBesideHeading("Tuner"));
        Assert.Equal(string.Empty, TunerPageHeading.StatusBesideHeading("  Tuner "));
    }

    [Fact]
    public void StatusBesideHeading_KeepsOtherStatusText()
    {
        Assert.Equal("Playing reference tone.", TunerPageHeading.StatusBesideHeading("Playing reference tone."));
        Assert.Equal("Stopped listening.", TunerPageHeading.StatusBesideHeading("Stopped listening."));
        Assert.Equal(string.Empty, TunerPageHeading.StatusBesideHeading("   "));
    }

    [Fact]
    public void Title_IsASingleTunerHeading()
    {
        Assert.Equal("Tuner", TunerPageHeading.Title);
        Assert.True(TunerPageHeading.TitleFontSize >= 26);
    }
}
