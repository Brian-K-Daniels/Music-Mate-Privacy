using musicmate.Services;

namespace musicmate.Tests;

public class MusicAudioSignalTraceTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    public void UiListening_RequiresLiveCapture(bool uiSaysListening, bool isCapturing, bool inconsistent)
    {
        Assert.Equal(
            inconsistent,
            MusicAudioSignalTrace.UiSaysListeningWhileCaptureStopped(uiSaysListening, isCapturing));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SilenceBeforeFirstNote_DoesNotDisableDetector(bool clockArmed)
    {
        Assert.False(MusicAudioSignalTrace.SilenceBeforeFirstNoteDisablesDetector(clockArmed));
    }

    [Fact]
    public void ActiveCapture_ReplacesStoppedListening()
    {
        Assert.Equal(
            MusicAudioSignalTrace.ListeningMessage,
            MusicAudioSignalTrace.StatusWhileMusicCaptureActive(
                MusicAudioSignalTrace.StoppedListeningMessage,
                listeningSessionActive: true));
    }

    [Fact]
    public void IdleOrPitchStatus_IsLeftAlone()
    {
        Assert.Null(MusicAudioSignalTrace.StatusWhileMusicCaptureActive(
            MusicAudioSignalTrace.StoppedListeningMessage,
            listeningSessionActive: false));
        Assert.Null(MusicAudioSignalTrace.StatusWhileMusicCaptureActive(
            "Expected: C4, Heard: C4, 0¢, Notes: 4",
            listeningSessionActive: true));
        Assert.Null(MusicAudioSignalTrace.StatusWhileMusicCaptureActive(
            MusicAudioSignalTrace.ListeningMessage,
            listeningSessionActive: true));
    }

    [Fact]
    public void CountInBaseline_DoesNotRestoreStoppedListening()
    {
        Assert.Equal(
            MusicAudioSignalTrace.ListeningMessage,
            MusicAudioSignalTrace.CountInRestoreBaseline(MusicAudioSignalTrace.StoppedListeningMessage));
        Assert.Equal(
            MusicAudioSignalTrace.ListeningMessage,
            MusicAudioSignalTrace.CountInRestoreBaseline(MusicAudioSignalTrace.ListeningPausedMessage));
        Assert.Equal(
            "Expected: A4",
            MusicAudioSignalTrace.CountInRestoreBaseline("Expected: A4"));
    }
}
