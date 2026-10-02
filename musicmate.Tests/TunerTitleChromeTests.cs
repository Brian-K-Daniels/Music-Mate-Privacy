using musicmate.Services;

namespace musicmate.Tests;

public class TunerTitleChromeTests
{
    [Theory]
    [InlineData("Tuner", false, false, false)]
    [InlineData("Tuner", true, false, false)]
    [InlineData("Tuner", false, true, false)]
    [InlineData("Tuner", true, true, false)]
    [InlineData("Selected Scale", false, false, true)]
    [InlineData("Selected Scale", true, false, false)]
    [InlineData("Selected Scale", true, true, true)]
    [InlineData("Selected Scale", false, true, true)]
    public void PlayButton_NeverVisibleOnTuner(
        string tune, bool isRunning, bool isPlaying, bool expectedVisible)
        => Assert.Equal(
            expectedVisible,
            TunerTitleChrome.IsPlayButtonVisible(tune, isRunning, isPlaying));

    [Fact]
    public void MetronomeRunning_TitleIsGreenGo_EvenIfListeningIntentRemains()
        => Assert.Equal(
            TunerTitleChrome.TitleAction.Go,
            TunerTitleChrome.ResolveTitleAction("Tuner", isListeningRunning: true, isMetronomePlaying: true));

    [Fact]
    public void MetronomeStoppedWhileListening_TitleIsStop()
        => Assert.Equal(
            TunerTitleChrome.TitleAction.Stop,
            TunerTitleChrome.ResolveTitleAction("Tuner", isListeningRunning: true, isMetronomePlaying: false));

    [Fact]
    public void MetronomeStoppedWhileIdle_TitleIsGo()
        => Assert.Equal(
            TunerTitleChrome.TitleAction.Go,
            TunerTitleChrome.ResolveTitleAction("Tuner", isListeningRunning: false, isMetronomePlaying: false));

    [Fact]
    public void MetronomeStartStopCycles_KeepTitleConsistent()
    {
        // Idle → Go
        Assert.Equal(
            TunerTitleChrome.TitleAction.Go,
            TunerTitleChrome.ResolveTitleAction("Tuner", false, false));

        // Listening → Stop
        Assert.Equal(
            TunerTitleChrome.TitleAction.Stop,
            TunerTitleChrome.ResolveTitleAction("Tuner", true, false));

        // Metronome while listening intent paused → Go
        Assert.Equal(
            TunerTitleChrome.TitleAction.Go,
            TunerTitleChrome.ResolveTitleAction("Tuner", true, true));

        // Stop metronome, still listening → Stop
        Assert.Equal(
            TunerTitleChrome.TitleAction.Stop,
            TunerTitleChrome.ResolveTitleAction("Tuner", true, false));

        // Metronome again → Go
        Assert.Equal(
            TunerTitleChrome.TitleAction.Go,
            TunerTitleChrome.ResolveTitleAction("Tuner", true, true));

        // Stop metronome and stop listening → Go
        Assert.Equal(
            TunerTitleChrome.TitleAction.Go,
            TunerTitleChrome.ResolveTitleAction("Tuner", false, false));
    }

    [Fact]
    public void MusicMode_IgnoresMetronomeFlag_ForTitleStop()
        => Assert.Equal(
            TunerTitleChrome.TitleAction.Stop,
            TunerTitleChrome.ResolveTitleAction("Selected Scale", isListeningRunning: true, isMetronomePlaying: true));
}
