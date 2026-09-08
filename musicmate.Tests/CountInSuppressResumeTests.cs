using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Count-In self-sound suppress must ignore click bleed, then reliably re-enable evaluation.
/// </summary>
[Collection("SessionPreferences")]
public class CountInSuppressResumeTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public CountInSuppressResumeTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Fact]
    public void MatchingPitch_DuringCountInSuppress_IsIgnored()
    {
        var session = CreateSession();
        session.StartListeningClock();
        session.SuppressCountInClickSelfSoundCapped(200, msPerBeat: 1000, "Count-In click");
        Assert.True(session.ShouldIgnoreAudio(DateTime.UtcNow));

        double freq = NoteSessionService.MidiToFreqPublic(60);
        var result = session.Evaluate(freq);
        Assert.True(result.correct);
        Assert.False(
            WaitingCountInLogic.ShouldAcceptFirstNoteToEndCountIn(
                result.correct, true, 0, withinSelfSoundSuppressWindow: true));
        Assert.False(session.UpdateFeedbackForCurrent(freq, result));
        Assert.Equal(0, session.CurrentNoteIndex);
    }

    [Fact]
    public void MatchingPitch_DuringResidualGuard_IsIgnored()
    {
        var session = CreateSession();
        session.StartListeningClock();
        session.SuppressCountInClickSelfSound(0, "residual guard");
        Assert.True(session.ShouldIgnoreAudio(DateTime.UtcNow));

        double freq = NoteSessionService.MidiToFreqPublic(60);
        Assert.False(session.UpdateFeedbackForCurrent(freq, session.Evaluate(freq)));
        Assert.Equal(0, session.CurrentNoteIndex);
    }

    [Fact]
    public void MatchingPitch_AfterClearSuppress_IsAccepted()
    {
        var session = CreateSession();
        session.StartListeningClock();
        session.SuppressCountInClickSelfSound(100, "Count-In click");
        Assert.True(session.ShouldIgnoreAudio(DateTime.UtcNow));

        session.ClearCountInClickSelfSoundSuppress("Count-In stopped");
        Assert.False(session.ShouldIgnoreAudio(DateTime.UtcNow));

        double freq = NoteSessionService.MidiToFreqPublic(60);
        Assert.True(session.UpdateFeedbackForCurrent(freq, session.Evaluate(freq)));
        Assert.Equal(1, session.CurrentNoteIndex);
    }

    [Fact]
    public void EarlyAcceptPath_ClearThenScore_ThenResidual_AllowsSubsequentNotes()
    {
        // Mirrors MusicPage early-accept: clear suppress → score note 0 → residual → clear → note 1.
        double elapsed = 0;
        var session = CreateSession(60, 62, 64);
        session.SessionElapsedMsOverride = () => elapsed;
        session.StartListeningClock();
        session.SessionElapsedMsOverride = () => elapsed;

        session.SuppressCountInClickSelfSound(300, "Count-In click");
        session.ClearCountInClickSelfSoundSuppress("first note accepted — clear before score");
        Assert.False(session.ShouldIgnoreAudio(DateTime.UtcNow));

        double f0 = NoteSessionService.MidiToFreqPublic(60);
        Assert.True(session.UpdateFeedbackForCurrent(f0, session.Evaluate(f0)));
        Assert.Equal(1, session.CurrentNoteIndex);

        session.SuppressCountInClickSelfSound(0, "residual guard after first-note accept");
        Assert.True(session.ShouldIgnoreAudio(DateTime.UtcNow));

        // Cancelling residual / guard completed → resume.
        session.ClearCountInClickSelfSoundSuppress("guard completed");
        session.NotifySilence();
        elapsed = 2000;

        double f1 = NoteSessionService.MidiToFreqPublic(62);
        Assert.True(session.UpdateFeedbackForCurrent(f1, session.Evaluate(f1)));
        Assert.Equal(2, session.CurrentNoteIndex);

        session.NotifySilence();
        elapsed = 4000;
        double f2 = NoteSessionService.MidiToFreqPublic(64);
        Assert.True(session.UpdateFeedbackForCurrent(f2, session.Evaluate(f2)));
        Assert.Equal(3, session.CurrentNoteIndex);
    }

    [Fact]
    public void CountInOff_DoesNotActivateSuppress()
    {
        WaitingCountInSettings.Enabled = false;
        var session = CreateSession();
        Assert.False(WaitingCountInSettings.Enabled);
        Assert.False(session.ShouldIgnoreAudio(DateTime.UtcNow));

        session.StartListeningClock();
        double freq = NoteSessionService.MidiToFreqPublic(60);
        Assert.True(session.UpdateFeedbackForCurrent(freq, session.Evaluate(freq)));
        Assert.Equal(1, session.CurrentNoteIndex);
    }

    [Fact]
    public void CancellingCountIn_ClearSuppress_RestoresDetection()
    {
        var session = CreateSession();
        session.SuppressCountInClickSelfSoundCapped(
            WaitingCountInLogic.ResolveClickSelfSoundDurationMs(400),
            msPerBeat: 500,
            "Count-In click");
        Assert.True(session.ShouldIgnoreAudio(DateTime.UtcNow));

        // StopWaitingCountIn equivalent:
        session.ClearCountInClickSelfSoundSuppress("Count-In stopped");
        Assert.False(session.ShouldIgnoreAudio(DateTime.UtcNow));

        session.StartListeningClock();
        double freq = NoteSessionService.MidiToFreqPublic(60);
        Assert.True(session.UpdateFeedbackForCurrent(freq, session.Evaluate(freq)));
        Assert.Equal(1, session.CurrentNoteIndex);
    }

    [Fact]
    public void ConductorCuesOn_DoesNotLeaveSuppressActive()
    {
        var session = CreateSession();
        session.ShowConductorCues = true;
        Assert.False(session.ShouldIgnoreAudio(DateTime.UtcNow));

        session.StartListeningClock();
        // Visual cues only — no SuppressCountInClickSelfSound.
        Assert.False(session.ShouldIgnoreAudio(DateTime.UtcNow));

        double freq = NoteSessionService.MidiToFreqPublic(60);
        Assert.True(session.UpdateFeedbackForCurrent(freq, session.Evaluate(freq)));
        // Accept sets CooldownMs debounce only — not a sticky Count-In suppress.
        // After clear of any cooldown:
        session.ClearCountInClickSelfSoundSuppress("cue ended / cooldown clear");
        Assert.False(session.ShouldIgnoreAudio(DateTime.UtcNow));
    }

    [Fact]
    public void CapSelfSoundSuppress_LeavesListeningGapWithinBeat()
    {
        int raw = WaitingCountInLogic.ComputeSelfSoundSuppressMs(400, 4096, 44100);
        int capped = WaitingCountInLogic.CapSelfSoundSuppressMs(raw, msPerBeat: 500);
        Assert.True(capped < 500);
        Assert.True(capped <= 500 - 80);
        Assert.True(capped >= 40);
    }

    [Fact]
    public void SuppressThenStop_MustNotBlockUpdateFeedback()
    {
        // Regression: StopWaitingCountIn used to Suppress(0) before UpdateFeedbackForCurrent,
        // so the accepted first note was rejected and detection appeared permanently broken.
        var session = CreateSession();
        session.SuppressCountInClickSelfSound(250, "Count-In click");
        session.ClearCountInClickSelfSoundSuppress("Count-In stopped");
        session.StartListeningClock();

        double freq = NoteSessionService.MidiToFreqPublic(60);
        var result = session.Evaluate(freq);
        Assert.True(result.correct);
        Assert.True(session.UpdateFeedbackForCurrent(freq, result));
        Assert.Contains(0, session.CorrectNoteIndices);
    }

    private static NoteSessionService CreateSession(params int[] midis)
    {
        if (midis.Length == 0)
            midis = [60, 62, 64, 65];

        var session = new NoteSessionService
        {
            Instrument = "concert-pitch",
            Tune = "Selected Scale",
            CooldownMs = 0,
            Tempo = 60,
            ShowConductorCues = true,
            ChildLevel = 1,
            SampleRate = 44100,
            PitchWindowSize = 4096,
        };
        session.Reset();
        session.Tune = "Selected Scale";
        session.Instrument = "concert-pitch";
        session.CooldownMs = 0;
        session.Tempo = 60;
        session.MeterTimeSignature = "4/4";
        session.SampleRate = 44100;
        session.PitchWindowSize = 4096;
        session.ShowConductorCues = true;

        session.NotesToDraw.Clear();
        session.FeedbackViewModels.Clear();
        for (int i = 0; i < midis.Length; i++)
        {
            int midi = midis[i];
            session.NotesToDraw.Add(new NoteInfo
            {
                Midi = midi,
                Name = NoteSessionService.MidiToNoteName(midi, flats: false),
                TargetFreq = NoteSessionService.MidiToFreqPublic(midi),
                DurationBeats = 1,
                Duration = NoteDuration.Quarter,
                StartBeat = i,
                GateBeatsAfterPrevious = i == 0 ? 0 : 1,
            });
            session.FeedbackViewModels.Add(new FeedbackItem(i, 0, 0, false));
        }

        return session;
    }
}
