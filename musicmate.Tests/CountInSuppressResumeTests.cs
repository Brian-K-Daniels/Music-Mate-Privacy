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

    public void Dispose()
    {
        SessionPreferences.TestStore = null;
        AppCueAudioGate.SuspendGrace = AppCueAudioGate.DefaultSuspendGrace;
        AppCueAudioGate.CancelPendingSuspend();
    }

    [Fact]
    public void MatchingPitch_DuringCountInSuppress_FarFromClick_MayEndCountIn()
    {
        var session = CreateSession();
        session.StartListeningClock();
        session.SuppressCountInClickSelfSoundCapped(200, msPerBeat: 1000, "Count-In click");
        Assert.True(session.ShouldIgnoreAudio(DateTime.UtcNow));

        double freq = NoteSessionService.MidiToFreqPublic(60);
        var result = session.Evaluate(freq);
        Assert.True(result.correct);
        // On-beat C4 is far from E6/A6 clicks — Count-In accept gate allows it.
        Assert.True(
            WaitingCountInLogic.ShouldAcceptFirstNoteToEndCountIn(
                result.correct, true, 0, withinSelfSoundSuppressWindow: true,
                heardHz: freq,
                accentedClickHz: WaitingCountInSettings.DefaultAccentedPitchHz,
                unaccentedClickHz: WaitingCountInSettings.DefaultUnaccentedPitchHz));
        // Session IgnoreAudio still blocks scoring until MusicPage clears suppress.
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
        int minGap = WaitingCountInLogic.ComputeSelfSoundGuardMs(4096, 44100);
        int capped = WaitingCountInLogic.CapSelfSoundSuppressMs(raw, msPerBeat: 500, minGap);
        Assert.True(capped < 500);
        int expectedGap = Math.Max(Math.Max(200, minGap), (int)Math.Floor(500 * 0.45));
        Assert.True(capped <= 500 - expectedGap);
        Assert.True((500 - capped) >= expectedGap - 1);
    }

    [Fact]
    public void CapSelfSoundSuppress_GapAllowsPitchWindowFill()
    {
        // Regression: 80ms gap < ~93ms McLeod window → no WindowReady during Count-In.
        double msPerBeat = 1000;
        int raw = WaitingCountInLogic.ComputeSelfSoundSuppressMs(340, 4096, 44100);
        int minGap = WaitingCountInLogic.ComputeSelfSoundGuardMs(4096, 44100);
        int capped = WaitingCountInLogic.CapSelfSoundSuppressMs(raw, msPerBeat, minGap);
        double windowMs = 1000.0 * 4096 / 44100;
        Assert.True(msPerBeat - capped >= windowMs,
            $"listening gap {msPerBeat - capped} must be >= window {windowMs:F0}ms");
    }

    [Fact]
    public void CapSelfSoundSuppress_At100Bpm_LeavesNearlyHalfBeatOpen()
    {
        // Log regression: 400ms suppress of 600ms beat blocked on-beat playing.
        double msPerBeat = 600;
        int raw = WaitingCountInLogic.ComputeSelfSoundSuppressMs(400, 4096, 44100);
        int minGap = WaitingCountInLogic.ComputeSelfSoundGuardMs(4096, 44100);
        int capped = WaitingCountInLogic.CapSelfSoundSuppressMs(raw, msPerBeat, minGap);
        Assert.True(msPerBeat - capped >= msPerBeat * 0.45 - 1);
        Assert.True(capped <= 330, $"expected suppress <=330ms at 100 BPM, got {capped}");
    }

    [Fact]
    public void AppCueAudioGate_NotifyInvokesSubscribers()
    {
        var previous = AppCueAudioGate.SuspendGrace;
        AppCueAudioGate.SuspendGrace = TimeSpan.Zero;
        int hits = 0;
        void Handler() => hits++;
        AppCueAudioGate.SuspendRequested += Handler;
        try
        {
            AppCueAudioGate.NotifyAppSuspended();
            Assert.Equal(1, hits);
        }
        finally
        {
            AppCueAudioGate.SuspendRequested -= Handler;
            AppCueAudioGate.SuspendGrace = previous;
            AppCueAudioGate.CancelPendingSuspend();
        }
    }

    [Fact]
    public async Task AppCueAudioGate_ResumeDuringGrace_DoesNotStopCues()
    {
        var previous = AppCueAudioGate.SuspendGrace;
        AppCueAudioGate.SuspendGrace = TimeSpan.FromMilliseconds(150);
        int hits = 0;
        void Handler() => hits++;
        AppCueAudioGate.SuspendRequested += Handler;
        try
        {
            AppCueAudioGate.NotifyAppSuspended();
            AppCueAudioGate.NotifyAppResumed();
            await Task.Delay(400);
            Assert.Equal(0, hits);
        }
        finally
        {
            AppCueAudioGate.SuspendRequested -= Handler;
            AppCueAudioGate.SuspendGrace = previous;
            AppCueAudioGate.CancelPendingSuspend();
        }
    }

    [Fact]
    public async Task AppCueAudioGate_GraceElapsed_SuspendsOnce()
    {
        var previous = AppCueAudioGate.SuspendGrace;
        AppCueAudioGate.SuspendGrace = TimeSpan.FromMilliseconds(80);
        int hits = 0;
        void Handler() => Interlocked.Increment(ref hits);
        AppCueAudioGate.SuspendRequested += Handler;
        try
        {
            AppCueAudioGate.NotifyAppSuspended();
            AppCueAudioGate.NotifyAppSuspended();
            var signaled = false;
            for (int i = 0; i < 40 && !signaled; i++)
            {
                await Task.Delay(25);
                signaled = Volatile.Read(ref hits) >= 1;
            }

            Assert.True(signaled);
            await Task.Delay(120);
            Assert.Equal(1, Volatile.Read(ref hits));
        }
        finally
        {
            AppCueAudioGate.SuspendRequested -= Handler;
            AppCueAudioGate.SuspendGrace = previous;
            AppCueAudioGate.CancelPendingSuspend();
        }
    }

    [Fact]
    public void AppCueAudioGate_NotifyResumeInvokesSubscribers()
    {
        int hits = 0;
        void Handler() => hits++;
        AppCueAudioGate.ResumeRequested += Handler;
        try
        {
            AppCueAudioGate.NotifyAppResumed();
            Assert.Equal(1, hits);
        }
        finally
        {
            AppCueAudioGate.ResumeRequested -= Handler;
        }
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
