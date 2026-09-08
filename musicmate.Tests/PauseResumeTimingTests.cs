using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Pause/resume: wall-clock silence must not run the score ahead of the player.
/// </summary>
[Collection("SessionPreferences")]
public class PauseResumeTimingTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    private static readonly int[] CMajorScaleMidi = [60, 62, 64, 65, 67, 69, 71, 72];

    public PauseResumeTimingTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Fact]
    public void WrongNote_ThenImmediateContinue_DoesNotPause()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateSession(CMajorScaleMidi, bpm, showConductorCues: true, countInArmed: true, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), 0);
        elapsed = msPerBeat;
        AssertAccepted(session, Freq(62), 1);

        elapsed = 2 * msPerBeat;
        var wrong = session.Evaluate(Freq(65)); // F while expecting E
        Assert.True(session.UpdateFeedbackForCurrent(Freq(65), wrong));
        Assert.Equal(2, session.CurrentNoteIndex);
        Assert.True(session.NoteFeedbacks[2].Wrong > 0);
        Assert.False(session.IsMusicalTimelinePaused);

        var recovery = session.Evaluate(Freq(64));
        Assert.True(session.UpdateFeedbackForCurrent(Freq(64), recovery));
        Assert.Equal(3, session.CurrentNoteIndex);
        AssertNoWrongFeedback(session, 3);
        AssertNoWrongFeedback(session, 4);
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(60, true)]
    [InlineData(120, false)]
    public void WrongNote_WaitTwoSeconds_ThenContinue_NoCascade(int bpm, bool showConductorCues)
    {
        double elapsed = 0;
        var session = CreateSession(CMajorScaleMidi, bpm, showConductorCues, countInArmed: true, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), 0);
        elapsed = msPerBeat;
        AssertAccepted(session, Freq(62), 1);

        elapsed = 2 * msPerBeat;
        var wrong = session.Evaluate(Freq(65));
        Assert.True(session.UpdateFeedbackForCurrent(Freq(65), wrong));
        Assert.Equal(2, session.CurrentNoteIndex);

        elapsed = 2 * msPerBeat + 2000;
        session.NotifySilence();
        Assert.False(session.AdvanceTimelineForExpiredNotes());
        Assert.True(session.IsMusicalTimelinePaused);
        Assert.Equal(2, session.CurrentNoteIndex);
        Assert.Equal(2, session.MusicalPauseNoteIndex);
        for (int i = 3; i < CMajorScaleMidi.Length; i++)
            AssertNoWrongFeedback(session, i);

        AssertAccepted(session, Freq(64), 2);
        Assert.False(session.IsMusicalTimelinePaused);
        AssertConductorSyncedWithExpectedNote(session, resumeIndex: 2);
        for (int i = 3; i < CMajorScaleMidi.Length; i++)
            AssertNoWrongFeedback(session, i);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void PauseLongerThanOneBeat_DoesNotMarkFutureNotesRed(int bpm)
    {
        double elapsed = 0;
        var session = CreateSession(CMajorScaleMidi, bpm, showConductorCues: true, countInArmed: true, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), 0);

        elapsed = 3 * msPerBeat;
        session.NotifySilence();
        int wrongBefore = CountWrongNotes(session);
        Assert.False(session.AdvanceTimelineForExpiredNotes());
        Assert.True(session.IsMusicalTimelinePaused);
        Assert.Equal(1, session.CurrentNoteIndex);
        Assert.Equal(wrongBefore, CountWrongNotes(session));
        for (int i = 1; i < CMajorScaleMidi.Length; i++)
            AssertNoWrongFeedback(session, i);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public void PauseLongerThanFourFourMeasure_StaysAtStoppedNote(int bpm)
    {
        double elapsed = 0;
        var session = CreateSession(CMajorScaleMidi, bpm, showConductorCues: true, countInArmed: true, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), 0);
        elapsed = msPerBeat;
        AssertAccepted(session, Freq(62), 1);

        elapsed = msPerBeat + 4 * msPerBeat + 50; // more than a full 4/4 bar of silence
        session.NotifySilence();
        Assert.False(session.AdvanceTimelineForExpiredNotes());
        Assert.True(session.IsMusicalTimelinePaused);
        Assert.Equal(2, session.CurrentNoteIndex);
        for (int i = 2; i < CMajorScaleMidi.Length; i++)
            AssertNoWrongFeedback(session, i);

        AssertAccepted(session, Freq(64), 2);
        AssertConductorSyncedWithExpectedNote(session, resumeIndex: 2);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void PauseResume_WorksForAllCountInAndConductorCombinations(bool countInArmed, bool showConductorCues)
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = countInArmed
            ? CreateSession(CMajorScaleMidi, bpm, showConductorCues, countInArmed: true, () => elapsed)
            : CreateDeferredClockSession(CMajorScaleMidi, bpm, showConductorCues, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        if (!countInArmed)
        {
            Assert.False(session.IsListeningClockRunning);
            Assert.True(session.TryArmListeningClockOnFirstCorrectPitch(pitchCorrect: true));
            elapsed = 0;
        }

        AssertAccepted(session, Freq(60), 0);

        elapsed = 4 * msPerBeat;
        session.NotifySilence();
        Assert.False(session.AdvanceTimelineForExpiredNotes());
        Assert.True(session.IsMusicalTimelinePaused);
        Assert.Equal(1, session.CurrentNoteIndex);
        for (int i = 1; i < CMajorScaleMidi.Length; i++)
            AssertNoWrongFeedback(session, i);

        AssertAccepted(session, Freq(62), 1);
        Assert.False(session.IsMusicalTimelinePaused);
        AssertConductorSyncedWithExpectedNote(session, resumeIndex: 1);
    }

    [Fact]
    public void OneSilentCallback_CannotMarkMultipleNotesWrong()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateSession(CMajorScaleMidi, bpm, showConductorCues: true, countInArmed: true, () => elapsed);

        elapsed = 0;
        AssertAccepted(session, Freq(60), 0);

        elapsed = 6 * ConductorOnsetTiming.MsPerBeat(bpm);
        int wrongBefore = CountWrongNotes(session);
        Assert.False(session.AdvanceTimelineForExpiredNotes());
        Assert.Equal(wrongBefore, CountWrongNotes(session));
        Assert.Equal(1, session.CurrentNoteIndex);
        Assert.True(session.IsMusicalTimelinePaused);
    }

    [Fact]
    public void Resume_RebasesExpectedOnsetToNow_AndKeepsConductorBeatOfCurrentNote()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateSession(CMajorScaleMidi, bpm, showConductorCues: true, countInArmed: true, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), 0);
        elapsed = msPerBeat;
        AssertAccepted(session, Freq(62), 1);

        // Stop around E (index 2 = beat 3 of measure 1 in 4/4).
        elapsed = 2 * msPerBeat;
        var wrong = session.Evaluate(Freq(67));
        Assert.True(session.UpdateFeedbackForCurrent(Freq(67), wrong));

        elapsed = 2 * msPerBeat + 5 * msPerBeat;
        session.NotifySilence();
        session.AdvanceTimelineForExpiredNotes();
        Assert.True(session.IsMusicalTimelinePaused);
        Assert.Equal(2, session.CurrentNoteIndex);

        var (measureBefore, beatBefore) = session.GetConductorMeasureBeatPublic(2);
        Assert.Equal(1, measureBefore);
        Assert.Equal(3, beatBefore);

        elapsed = 2 * msPerBeat + 8 * msPerBeat;
        AssertAccepted(session, Freq(64), 2);

        var (measureAfter, beatAfter) = session.GetConductorMeasureBeatPublic(2);
        Assert.Equal(measureBefore, measureAfter);
        Assert.Equal(beatBefore, beatAfter);
        AssertConductorSyncedWithExpectedNote(session, resumeIndex: 2);

        double expectedNow = session.GetConductorExpectedOnsetMsPublic(2);
        // Note 2 was accepted at resume time; origin should keep note 2's onset at that instant.
        Assert.InRange(expectedNow, elapsed - 1, elapsed + 1);
    }


    [Fact]
    public void Pause_ThenCorrectExpectedNote_ReanchorsWithoutRedCascade()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateSession(CMajorScaleMidi, bpm, showConductorCues: true, countInArmed: true, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), 0);
        elapsed = msPerBeat;
        AssertAccepted(session, Freq(62), 1);

        elapsed = 2 * msPerBeat + 5 * msPerBeat;
        session.NotifySilence();
        Assert.False(session.AdvanceTimelineForExpiredNotes());
        Assert.True(session.IsMusicalTimelinePaused);
        Assert.Equal(2, session.CurrentNoteIndex);

        // Wrong pitch while paused: mark wrong, stay paused, no cascade.
        var wrong = session.Evaluate(Freq(67));
        Assert.True(session.UpdateFeedbackForCurrent(Freq(67), wrong));
        Assert.True(session.IsMusicalTimelinePaused);
        Assert.Equal(2, session.CurrentNoteIndex);
        Assert.True(session.NoteFeedbacks[2].Wrong > 0);
        for (int i = 3; i < CMajorScaleMidi.Length; i++)
            AssertNoWrongFeedback(session, i);

        // Correct expected note re-anchors.
        AssertAccepted(session, Freq(64), 2);
        Assert.False(session.IsMusicalTimelinePaused);
        AssertConductorSyncedWithExpectedNote(session, resumeIndex: 2);
        for (int i = 3; i < CMajorScaleMidi.Length; i++)
            AssertNoWrongFeedback(session, i);
    }

    [Fact]
    public void TimingError_AfterResync_IsStillMarkedWrong()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateSession(CMajorScaleMidi, bpm, showConductorCues: true, countInArmed: true, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), 0);

        elapsed = 4 * msPerBeat;
        session.NotifySilence();
        Assert.False(session.AdvanceTimelineForExpiredNotes());
        Assert.True(session.IsMusicalTimelinePaused);

        AssertAccepted(session, Freq(62), 1);
        Assert.False(session.IsMusicalTimelinePaused);
        Assert.Equal(2, session.CurrentNoteIndex);

        // After resync, play the next note far too early — must still mark wrong.
        double earlyMs = elapsed + 10;
        elapsed = earlyMs;
        var early = session.Evaluate(Freq(64));
        Assert.True(early.correct);
        Assert.True(session.UpdateFeedbackForCurrent(Freq(64), early));
        Assert.Equal(2, session.CurrentNoteIndex);
        Assert.True(session.NoteFeedbacks[2].Wrong > 0);
        AssertNoWrongFeedback(session, 3);
        AssertNoWrongFeedback(session, 4);
    }
    private static void AssertConductorSyncedWithExpectedNote(NoteSessionService session, int resumeIndex)
    {
        int current = session.CurrentNoteIndex;
        Assert.Equal(resumeIndex + 1, current);

        int scoredIndex = resumeIndex;
        var (measure, beat) = session.GetConductorMeasureBeatPublic(scoredIndex);
        double expectedBeat = session.GetConductorExpectedBeatPublic(scoredIndex);
        Assert.Equal(expectedBeat, (measure - 1) * 4 + (beat - 1), precision: 6);

        double expectedMs = session.GetConductorExpectedOnsetMsPublic(current < session.NotesToDraw.Count ? current : scoredIndex);
        double now = session.GetSessionElapsedMsPublic();
        if (current < session.NotesToDraw.Count)
        {
            double early = ConductorOnsetTiming.EarlyToleranceMs(session.GetConductorTimingBpmPublic());
            double late = ConductorOnsetTiming.LateToleranceMs(session.GetConductorTimingBpmPublic());
            // Next note is one beat after the resumed note; elapsed is still at resume time
            // so the next expected onset is in the future — not expired, not a cascade.
            Assert.True(now <= expectedMs + late);
            Assert.True(session.GetConductorExpectedBeatPublic(current) > session.GetConductorExpectedBeatPublic(scoredIndex));
        }
    }

    private static int CountWrongNotes(NoteSessionService session)
        => session.NoteFeedbacks.Count(kv => kv.Value.Wrong > 0);

    private static void AssertNoWrongFeedback(NoteSessionService session, int index)
    {
        if (session.NoteFeedbacks.TryGetValue(index, out var fb))
            Assert.Equal(0, fb.Wrong);
    }

    private static void AssertAccepted(NoteSessionService session, double freq, int expectedIndex)
    {
        var result = session.Evaluate(freq);
        Assert.True(result.correct, $"Expected pitch match at index {expectedIndex}");
        Assert.True(
            session.UpdateFeedbackForCurrent(freq, result),
            $"Expected note {expectedIndex} to be accepted");
        Assert.Contains(expectedIndex, session.CorrectNoteIndices);
        Assert.Equal(expectedIndex + 1, session.CurrentNoteIndex);
    }

    private static double Freq(int midi) => NoteSessionService.MidiToFreqPublic(midi);

    private static NoteSessionService CreateDeferredClockSession(
        int[] midiNotes,
        int bpm,
        bool showConductorCues,
        Func<double> elapsedMs)
    {
        var session = CreateSessionCore(midiNotes, bpm, showConductorCues, elapsedMs);
        session.SessionElapsedMsOverride = elapsedMs;
        return session;
    }

    private static NoteSessionService CreateSession(
        int[] midiNotes,
        int bpm,
        bool showConductorCues,
        bool countInArmed,
        Func<double> elapsedMs)
    {
        var session = CreateSessionCore(midiNotes, bpm, showConductorCues, elapsedMs);
        if (countInArmed)
            session.StartListeningClock();
        session.SessionElapsedMsOverride = elapsedMs;
        return session;
    }

    private static NoteSessionService CreateSessionCore(
        int[] midiNotes,
        int bpm,
        bool showConductorCues,
        Func<double> elapsedMs)
    {
        var session = new NoteSessionService
        {
            Instrument = "concert-pitch",
            Tune = "Selected Scale",
            CooldownMs = 0,
            Tempo = bpm,
            ShowConductorCues = showConductorCues,
            ChildLevel = 1,
        };
        session.Reset();
        session.Tune = "Selected Scale";
        session.Instrument = "concert-pitch";
        session.CooldownMs = 0;
        session.Tempo = bpm;
        session.ShowConductorCues = showConductorCues;
        session.MeterTimeSignature = "4/4";
        session.SamePitchSilenceMs = NoteSessionService.DefaultSamePitchSilenceMs;
        session.SessionElapsedMsOverride = elapsedMs;

        for (int i = 0; i < midiNotes.Length; i++)
        {
            int midi = midiNotes[i];
            session.NotesToDraw.Add(new NoteInfo
            {
                Midi = midi,
                Name = NoteSessionService.MidiToNoteName(midi, flats: false),
                TargetFreq = Freq(midi),
                Duration = NoteDuration.Quarter,
                DurationBeats = 1,
                StartBeat = i,
                GateBeatsAfterPrevious = i == 0 ? 0 : 1,
            });
            session.FeedbackViewModels.Add(new FeedbackItem(i, 0, 0, false));
        }

        session.ConfigureRhythmStartGates();
        return session;
    }
}
