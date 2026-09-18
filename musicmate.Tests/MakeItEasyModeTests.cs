using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Make It Easy: temporary beginner detection preset must widen timing / debounce
/// via the existing grading pipeline and restore Advanced settings exactly on toggle-off.
/// </summary>
[Collection("SessionPreferences")]
public class MakeItEasyModeTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public MakeItEasyModeTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose()
    {
        SessionPreferences.TestStore = null;
    }

    [Fact]
    public void Easy_AcceptsCorrectNoteSubstantiallyEarly()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateEasySession([60, 62], bpm, () => elapsed);

        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);
        double normalEarly = ConductorOnsetTiming.EarlyToleranceMs(bpm);
        double easyEarly = ConductorOnsetTiming.EarlyToleranceMs(bpm, MakeItEasyMode.TimingProfile);

        Assert.True(easyEarly > normalEarly + 200);

        elapsed = 0;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), session.Evaluate(Freq(60))));

        // ~800 ms early: outside normal window, inside Easy window.
        double earlyOffset = Math.Min(800, easyEarly - 50);
        Assert.True(earlyOffset > normalEarly + 50);
        elapsed = msPerBeat - earlyOffset;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(62), session.Evaluate(Freq(62))));
        Assert.Equal(2, session.CurrentNoteIndex);
        Assert.DoesNotContain(
            session.GetSessionAttemptOutcomes(),
            o => o.NoteIndex == 1 && o.WrongReason == "Early");
    }

    [Fact]
    public void Easy_AcceptsCorrectNoteSubstantiallyLate()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateEasySession([60, 62], bpm, () => elapsed);

        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);
        double normalLate = ConductorOnsetTiming.LateToleranceMs(bpm);
        double easyLate = ConductorOnsetTiming.LateToleranceMs(bpm, MakeItEasyMode.TimingProfile);
        Assert.True(easyLate > normalLate + 200);

        elapsed = 0;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), session.Evaluate(Freq(60))));

        double lateOffset = Math.Min(1200, easyLate - 50);
        Assert.True(lateOffset > normalLate + 50);
        elapsed = msPerBeat + lateOffset;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(62), session.Evaluate(Freq(62))));
        Assert.Equal(2, session.CurrentNoteIndex);
        Assert.Contains(session.CorrectNoteIndices, i => i == 1);
    }

    [Fact]
    public void Easy_ShortWrongPitchTransient_DoesNotMarkRed()
    {
        const int bpm = 60;
        DateTime utc = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        double elapsed = 0;
        var session = CreateEasySession([60, 62], bpm, () => elapsed);
        session.UtcNowForTests = () => utc;
        Assert.Equal(MakeItEasyMode.WrongDebounceMs, session.WrongDebounceMs);

        elapsed = 0;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), session.Evaluate(Freq(60))));

        elapsed = ConductorOnsetTiming.MsPerBeat(bpm);
        // Brief wrong pitch (arms hold, does not mark).
        Assert.False(session.UpdateFeedbackForCurrent(Freq(64), session.Evaluate(Freq(64))));
        Assert.False(session.NoteFeedbacks.TryGetValue(1, out var fb) && fb.Wrong > 0);

        // Silence clears the Easy wrong-arm so a later brief glitch cannot inherit it.
        utc = utc.AddMilliseconds(200);
        session.NotifySilence();

        // Correct pitch shortly after — still green path.
        Assert.True(session.UpdateFeedbackForCurrent(Freq(62), session.Evaluate(Freq(62))));
        Assert.DoesNotContain(
            session.GetSessionAttemptOutcomes(),
            o => o.NoteIndex == 1 && o.WrongReason == "WrongPitch");
    }

    [Fact]
    public void Easy_PauseDoesNotCascadeFollowingNotesRed_AndCanResume()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateEasySession([60, 62, 64, 65], bpm, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), session.Evaluate(Freq(60))));
        elapsed = msPerBeat;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(62), session.Evaluate(Freq(62))));

        // Long pause past the Easy late window → musical pause, not a cascade of Missed.
        elapsed = 2 * msPerBeat + MakeItEasyMode.LateToleranceBeats * msPerBeat + 500;
        session.NotifySilence();
        Assert.False(session.AdvanceTimelineForExpiredNotes());
        Assert.True(session.IsMusicalTimelinePaused);
        Assert.Equal(2, session.CurrentNoteIndex);
        Assert.False(session.NoteFeedbacks.TryGetValue(2, out var w2) && w2.Wrong > 0);
        Assert.False(session.NoteFeedbacks.TryGetValue(3, out var w3) && w3.Wrong > 0);

        // Resume on the expected pitch.
        Assert.True(session.UpdateFeedbackForCurrent(Freq(64), session.Evaluate(Freq(64))));
        Assert.Equal(3, session.CurrentNoteIndex);
        Assert.Contains(session.CorrectNoteIndices, i => i == 2);
        Assert.False(session.IsMusicalTimelinePaused);
    }

    /// <summary>
    /// User scenario: pause several seconds without a second AdvanceTimeline poll
    /// (BecameSilent fired early). Resume must rebase to musical time ≈ expected note,
    /// not score Late against wall-clock t=14s when the note was due at t=8s.
    /// </summary>
    [Fact]
    public void Easy_LongPauseWithoutAdvancePoll_ResyncsToCurrentNote_NotWallClock()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateEasySession([60, 62, 64, 65, 67], bpm, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), session.Evaluate(Freq(60))));
        elapsed = msPerBeat;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(62), session.Evaluate(Freq(62))));

        // BecameSilent early — window not expired yet, so no pause entered.
        elapsed = 2 * msPerBeat + 100;
        session.NotifySilence();
        Assert.False(session.AdvanceTimelineForExpiredNotes());
        Assert.False(session.IsMusicalTimelinePaused);

        // Wall clock advances 6s past the expected note (user example: due at ~2s, resume at ~8s+).
        double resumeWall = 2 * msPerBeat + 6000;
        elapsed = resumeWall;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(64), session.Evaluate(Freq(64))));
        Assert.Equal(3, session.CurrentNoteIndex);
        Assert.Contains(2, session.CorrectNoteIndices);
        Assert.DoesNotContain(
            session.GetSessionAttemptOutcomes(),
            o => o.NoteIndex == 2 && (o.WrongReason == "Late" || o.WrongReason == "Missed"));

        // Origin shifted: current note's expected onset ≈ resume wall time.
        double expectedAtResume = session.GetConductorExpectedOnsetMsPublic(2);
        Assert.InRange(expectedAtResume, resumeWall - 1, resumeWall + 1);

        // Following notes are not red and keep a full Easy late window from the new origin.
        Assert.False(session.NoteFeedbacks.TryGetValue(3, out var w3) && w3.Wrong > 0);
        Assert.False(session.NoteFeedbacks.TryGetValue(4, out var w4) && w4.Wrong > 0);

        double nextExpected = session.GetConductorExpectedOnsetMsPublic(3);
        double easyLate = ConductorOnsetTiming.LateToleranceMs(bpm, MakeItEasyMode.TimingProfile);
        elapsed = nextExpected + easyLate - 50;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(65), session.Evaluate(Freq(65))));
        Assert.Equal(4, session.CurrentNoteIndex);
    }

    [Fact]
    public void Easy_PauseThenResumeOnNextNote_GivesFullEasyWindowAfterward()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateEasySession([60, 62, 64, 65], bpm, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), session.Evaluate(Freq(60))));

        elapsed = msPerBeat + 5000;
        session.NotifySilence();
        session.AdvanceTimelineForExpiredNotes();
        Assert.True(session.IsMusicalTimelinePaused);
        Assert.Equal(1, session.CurrentNoteIndex);

        elapsed = msPerBeat + 8000;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(62), session.Evaluate(Freq(62))));
        Assert.False(session.IsMusicalTimelinePaused);

        double nextExpected = session.GetConductorExpectedOnsetMsPublic(2);
        double easyLate = ConductorOnsetTiming.LateToleranceMs(bpm, MakeItEasyMode.TimingProfile);
        elapsed = nextExpected + easyLate - 25;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(64), session.Evaluate(Freq(64))));
        Assert.Equal(3, session.CurrentNoteIndex);
    }

    [Fact]
    public void Easy_NoFollowingNotesBecomeRedDuringPause()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateEasySession([60, 62, 64, 65, 67], bpm, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), session.Evaluate(Freq(60))));

        elapsed = 8 * msPerBeat;
        session.NotifySilence();
        Assert.False(session.AdvanceTimelineForExpiredNotes());
        Assert.True(session.IsMusicalTimelinePaused);

        for (int i = 1; i < 5; i++)
            Assert.False(session.NoteFeedbacks.TryGetValue(i, out var fb) && fb.Wrong > 0);
        Assert.Equal(1, session.CurrentNoteIndex);
    }

    [Fact]
    public void Easy_MultiplePausesAccumulate_EachResumeRebases()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateEasySession([60, 62, 64, 65, 67], bpm, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), session.Evaluate(Freq(60))));

        elapsed = msPerBeat + 4000;
        session.NotifySilence();
        session.AdvanceTimelineForExpiredNotes();
        Assert.True(session.IsMusicalTimelinePaused);
        elapsed = msPerBeat + 7000;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(62), session.Evaluate(Freq(62))));
        double originAfterFirst = session.ConductorOriginMs;
        Assert.True(originAfterFirst > 1000);

        elapsed = elapsed + msPerBeat + 5000;
        session.NotifySilence();
        session.AdvanceTimelineForExpiredNotes();
        Assert.True(session.IsMusicalTimelinePaused);
        elapsed += 3000;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(64), session.Evaluate(Freq(64))));
        Assert.True(session.ConductorOriginMs > originAfterFirst);
        Assert.False(session.NoteFeedbacks.TryGetValue(3, out var w) && w.Wrong > 0);
    }

    [Fact]
    public void Easy_OrdinaryShortGapBetweenNotes_DoesNotCountAsPause()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateEasySession([60, 62, 64], bpm, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);
        double originBefore = session.ConductorOriginMs;

        elapsed = 0;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), session.Evaluate(Freq(60))));
        // Half-beat gap — normal phrasing, not a pause.
        elapsed = msPerBeat * 0.5;
        session.NotifySilence();
        Assert.False(session.AdvanceTimelineForExpiredNotes());
        Assert.False(session.IsMusicalTimelinePaused);

        elapsed = msPerBeat;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(62), session.Evaluate(Freq(62))));
        Assert.Equal(originBefore, session.ConductorOriginMs, precision: 3);
    }

    [Fact]
    public void Easy_StopGoAndRestart_ResetPauseOffsets()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateEasySession([60, 62, 64], bpm, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), session.Evaluate(Freq(60))));
        elapsed = msPerBeat + 5000;
        session.NotifySilence();
        session.AdvanceTimelineForExpiredNotes();
        Assert.True(session.IsMusicalTimelinePaused);
        elapsed = msPerBeat + 8000;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(62), session.Evaluate(Freq(62))));
        Assert.True(session.ConductorOriginMs > 0);

        session.StopSession();
        session.Reset();
        session.SetMakeItEasyActive(true);
        session.CooldownMs = 0;
        session.SessionElapsedMsOverride = () => elapsed;
        session.NotesToDraw.Clear();
        session.FeedbackViewModels.Clear();
        for (int i = 0; i < 3; i++)
        {
            int midi = 60 + i * 2;
            session.NotesToDraw.Add(new NoteInfo
            {
                Midi = midi,
                Name = NoteSessionService.MidiToNoteName(midi, flats: false),
                TargetFreq = NoteSessionService.MidiToFreqPublic(midi),
                Duration = NoteDuration.Quarter,
                DurationBeats = 1,
                StartBeat = i,
                GateBeatsAfterPrevious = i == 0 ? 0 : 1,
            });
            session.FeedbackViewModels.Add(new FeedbackItem(i, 0, 0, false));
        }
        session.ConfigureRhythmStartGates();
        elapsed = 0;
        session.StartListeningClock();

        Assert.Equal(0, session.ConductorOriginMs, precision: 3);
        Assert.False(session.IsMusicalTimelinePaused);
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), session.Evaluate(Freq(60))));
        Assert.Equal(0, session.ConductorOriginMs, precision: 3);
    }

    [Fact]
    public void NormalMode_ShortPauseStillUsesTwoBeatSilenceThreshold()
    {
        // Easy changed PauseSilenceBeats; normal mode must keep the 2-beat threshold
        // and must not pick up Easy's expired-window rebase-without-pause behavior.
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateSession([60, 62, 64], bpm, () => elapsed);
        Assert.False(session.IsMakeItEasyActive);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), session.Evaluate(Freq(60))));
        elapsed = msPerBeat + ConductorOnsetTiming.LateToleranceMs(bpm) + 50;
        session.NotifySilence();
        Assert.False(session.AdvanceTimelineForExpiredNotes());
        Assert.True(session.IsMusicalTimelinePaused);
    }

    [Fact]
    public void Easy_SustainedWrongPitch_CanStillMarkWrong()
    {
        const int bpm = 60;
        DateTime utc = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        double elapsed = 0;
        var session = CreateEasySession([60, 62], bpm, () => elapsed);
        session.UtcNowForTests = () => utc;

        elapsed = 0;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), session.Evaluate(Freq(60))));

        elapsed = ConductorOnsetTiming.MsPerBeat(bpm);
        Assert.False(session.UpdateFeedbackForCurrent(Freq(64), session.Evaluate(Freq(64))));
        Assert.False(session.NoteFeedbacks.TryGetValue(1, out var early) && early.Wrong > 0);

        utc = utc.AddMilliseconds(MakeItEasyMode.WrongDebounceMs + 50);
        Assert.True(session.UpdateFeedbackForCurrent(Freq(64), session.Evaluate(Freq(64))));
        Assert.True(session.NoteFeedbacks.TryGetValue(1, out var marked) && marked.Wrong > 0);
        Assert.Contains(
            session.GetSessionAttemptOutcomes(),
            o => o.NoteIndex == 1 && o.WrongReason == "WrongPitch");
    }

    [Fact]
    public void Easy_ToggleOff_RestoresPreviousSettingsExactly()
    {
        var session = new NoteSessionService();
        if (session.IsMakeItEasyActive)
            session.SetMakeItEasyActive(false);

        session.Tolerance = 42;
        session.WrongDebounceMs = 275;
        session.CooldownMs = 65;
        Assert.False(session.IsMakeItEasyActive);

        session.SetMakeItEasyActive(true);
        Assert.True(session.IsMakeItEasyActive);
        Assert.Equal(MakeItEasyMode.ToleranceCents, session.Tolerance);
        Assert.Equal(MakeItEasyMode.WrongDebounceMs, session.WrongDebounceMs);
        Assert.Equal(MakeItEasyMode.CooldownMs, session.CooldownMs);

        session.SetMakeItEasyActive(false);
        Assert.False(session.IsMakeItEasyActive);
        Assert.Equal(42, session.Tolerance);
        Assert.Equal(275, session.WrongDebounceMs);
        Assert.Equal(65, session.CooldownMs);
    }

    [Fact]
    public void FactoryDefault_MakeItEasyIsOn()
    {
        Assert.True(MakeItEasyMode.DefaultActive);
        var session = new NoteSessionService();
        Assert.True(session.IsMakeItEasyActive);
        Assert.Equal(MakeItEasyMode.ToleranceCents, session.Tolerance);
    }

    [Fact]
    public void NormalMode_TimingWindowsUnchangedWhenEasyOff()
    {
        const int bpm = 60;
        Assert.Equal(
            ConductorOnsetTiming.ClampToleranceMs(
                ConductorOnsetTiming.EarlyToleranceBeats * ConductorOnsetTiming.MsPerBeat(bpm)),
            ConductorOnsetTiming.EarlyToleranceMs(bpm),
            precision: 6);
        Assert.Equal(
            ConductorOnsetTiming.ClampToleranceMs(
                ConductorOnsetTiming.LateToleranceBeats * ConductorOnsetTiming.MsPerBeat(bpm)),
            ConductorOnsetTiming.LateToleranceMs(bpm),
            precision: 6);
    }

    private static NoteSessionService CreateSession(int[] midis, int bpm, Func<double> elapsed)
    {
        var session = new NoteSessionService
        {
            Instrument = "concert-pitch",
            Tune = "Selected Scale",
            CooldownMs = 0,
            Tempo = bpm,
            ShowConductorCues = false,
            ChildLevel = 1,
        };
        // Factory default is Easy ON — normal-mode tests must turn it off explicitly.
        if (session.IsMakeItEasyActive)
            session.SetMakeItEasyActive(false);
        session.Reset();
        session.Tune = "Selected Scale";
        session.Instrument = "concert-pitch";
        session.Tempo = bpm;
        session.CooldownMs = 0;
        session.WrongDebounceMs = 0;
        session.SessionElapsedMsOverride = elapsed;

        for (int i = 0; i < midis.Length; i++)
        {
            int midi = midis[i];
            session.NotesToDraw.Add(new NoteInfo
            {
                Midi = midi,
                Name = NoteSessionService.MidiToNoteName(midi, flats: false),
                TargetFreq = NoteSessionService.MidiToFreqPublic(midi),
                Duration = NoteDuration.Quarter,
                DurationBeats = 1,
                StartBeat = i,
                GateBeatsAfterPrevious = i == 0 ? 0 : 1,
            });
            session.FeedbackViewModels.Add(new FeedbackItem(i, 0, 0, false));
        }

        session.ConfigureRhythmStartGates();
        session.StartListeningClock();
        return session;
    }

    private static NoteSessionService CreateEasySession(int[] midis, int bpm, Func<double> elapsed)
    {
        var session = CreateSession(midis, bpm, elapsed);
        session.SetMakeItEasyActive(true);
        // Keep Easy timing / wrong-hold; allow rapid test frames without audio cooldown.
        session.CooldownMs = 0;
        return session;
    }

    private static double Freq(int midi) => NoteSessionService.MidiToFreqPublic(midi);
}
