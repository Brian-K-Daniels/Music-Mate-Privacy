using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Regression tests for "first note heard → entire tune turns red".
/// </summary>
public class FirstNoteStaffColorRegressionTests
{
    private static readonly int[] FifteenNoteMidis =
    [
        67, 69, 71, 72, 74, 76, 77, 79, 77, 76, 74, 72, 71, 69, 67,
    ];

    [Fact]
    public void FirstCorrectNote_DoesNotMarkFutureNotesWrong()
    {
        double elapsed = 0;
        var session = CreateSession(FifteenNoteMidis, bpm: 98, () => elapsed);

        elapsed = 0;
        AssertAccepted(session, Freq(FifteenNoteMidis[0]), expectedIndex: 0);

        for (int i = 1; i < FifteenNoteMidis.Length; i++)
            AssertNoWrongFeedback(session, i);

        AssertStaffColors(session, upperPitchCount: FifteenNoteMidis.Length, onlyIndex0Correct: true);
    }

    [Fact]
    public void WrongFirstPitch_OnlyMarksCurrentNoteWrong()
    {
        double elapsed = 0;
        var session = CreateSession(FifteenNoteMidis, bpm: 98, () => elapsed);

        elapsed = 0;
        var wrong = session.Evaluate(Freq(FifteenNoteMidis[0] + 2));
        Assert.True(session.UpdateFeedbackForCurrent(Freq(FifteenNoteMidis[0] + 2), wrong));

        Assert.Equal(0, session.CurrentNoteIndex);
        AssertWrongOnlyAt(session, 0);

        for (int i = 1; i < FifteenNoteMidis.Length; i++)
            AssertNoWrongFeedback(session, i);

        AssertStaffColors(session, upperPitchCount: FifteenNoteMidis.Length, wrongIndices: [0]);
    }

    [Fact]
    public void TooEarlyOnSecondNote_DoesNotMarkLaterNotesWrong()
    {
        const int bpm = 30;
        double elapsed = 0;
        var session = CreateSession([60, 62, 64, 65], bpm, () => elapsed);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        elapsed = 500;
        var earlySecond = session.Evaluate(Freq(62));
        Assert.True(earlySecond.correct);
        Assert.True(session.UpdateFeedbackForCurrent(Freq(62), earlySecond));

        Assert.Equal(1, session.CurrentNoteIndex);
        AssertWrongOnlyAt(session, 1);
        AssertNoWrongFeedback(session, 2);
        AssertNoWrongFeedback(session, 3);
    }

    [Fact]
    public void DetectedPitchCatchUp_ProcessesOnlyFirstExpiredNote()
    {
        const int bpm = 60;
        double elapsed = 5000;
        var session = CreateSession([60, 62, 64, 65, 67], bpm, () => elapsed);

        var result = session.Evaluate(Freq(60));
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), result));

        Assert.Equal(1, session.CurrentNoteIndex);
        AssertWrongOnlyAt(session, 0);
        for (int i = 1; i < 5; i++)
            AssertNoWrongFeedback(session, i);
    }

    [Fact]
    public void WrongPitch_ThenDelay_ThenCorrectRecovery_DoesNotMarkMultipleNotesWrong()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateSession([60, 62, 64, 65], bpm, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        elapsed = msPerBeat;
        var wrong = session.Evaluate(Freq(64));
        Assert.True(session.UpdateFeedbackForCurrent(Freq(64), wrong));
        Assert.Equal(1, session.CurrentNoteIndex);
        AssertWrongOnlyAt(session, 1);
        AssertNoWrongFeedback(session, 2);
        AssertNoWrongFeedback(session, 3);

        // Hesitation: clock passes several note windows while the player is silent.
        elapsed = 3 * msPerBeat;
        session.NotifySilence();
        Assert.False(session.AdvanceTimelineForExpiredNotes());
        Assert.Equal(1, session.CurrentNoteIndex);
        AssertNoWrongFeedback(session, 2);
        AssertNoWrongFeedback(session, 3);

        // Recovery with the correct pitch for the expected note.
        var recovery = session.Evaluate(Freq(62));
        Assert.True(session.UpdateFeedbackForCurrent(Freq(62), recovery));
        Assert.Equal(2, session.CurrentNoteIndex);
        for (int i = 2; i < 4; i++)
            AssertNoWrongFeedback(session, i);

        Assert.Equal(StaffNoteState.Correct,
            StaffNoteStateResolver.Resolve(0, session.CurrentNoteIndex, true,
                session.CorrectNoteIndices, session.NoteFeedbacks));
        Assert.Equal(StaffNoteState.Correct,
            StaffNoteStateResolver.Resolve(1, session.CurrentNoteIndex, true,
                session.CorrectNoteIndices, session.NoteFeedbacks));
        Assert.Equal(StaffNoteState.Current,
            StaffNoteStateResolver.Resolve(2, session.CurrentNoteIndex, true,
                session.CorrectNoteIndices, session.NoteFeedbacks));
        Assert.Equal(StaffNoteState.Pending,
            StaffNoteStateResolver.Resolve(3, session.CurrentNoteIndex, true,
                session.CorrectNoteIndices, session.NoteFeedbacks));
    }

    [Fact]
    public void SilentCatchUp_PausesInsteadOfMarkingFutureNotesMissed()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateSession([60, 62, 64, 65], bpm, () => elapsed);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        elapsed = 3 * ConductorOnsetTiming.MsPerBeat(bpm);
        Assert.False(session.AdvanceTimelineForExpiredNotes());
        Assert.True(session.IsMusicalTimelinePaused);
        Assert.Equal(1, session.CurrentNoteIndex);
        AssertNoWrongFeedback(session, 1);
        AssertNoWrongFeedback(session, 2);
        AssertNoWrongFeedback(session, 3);
    }

    [Fact]
    public void StaffResolver_CursorAheadWithoutFeedback_LeavesNotesPending()
    {
        var feedback = new Dictionary<int, (int Wrong, int Cents)>
        {
            [0] = (1, 0),
        };

        Assert.Equal(StaffNoteState.Wrong,
            StaffNoteStateResolver.Resolve(0, currentNoteIndex: 15, isActiveStaff: true, [], feedback));
        Assert.Equal(StaffNoteState.Pending,
            StaffNoteStateResolver.Resolve(1, currentNoteIndex: 15, isActiveStaff: true, [], feedback));
        Assert.Equal(StaffNoteState.Pending,
            StaffNoteStateResolver.Resolve(14, currentNoteIndex: 15, isActiveStaff: true, [], feedback));
    }

    [Fact]
    public void PracticeTuneStyleStartBeat_FirstHeardPitchAcceptsWithoutTooEarly()
    {
        const int bpm = 98;
        double elapsed = 0;
        var session = CreateTuneStyleSession(
            firstStartBeat: 4,
            midiNotes: [67, 69, 71],
            bpm,
            () => elapsed);

        elapsed = 0;
        AssertAccepted(session, Freq(67), expectedIndex: 0);
        AssertNoWrongFeedback(session, 1);
        AssertNoWrongFeedback(session, 2);
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

    private static void AssertNoWrongFeedback(NoteSessionService session, int index)
    {
        if (session.NoteFeedbacks.TryGetValue(index, out var fb))
            Assert.Equal(0, fb.Wrong);
    }

    private static void AssertWrongOnlyAt(NoteSessionService session, int index)
    {
        Assert.True(session.NoteFeedbacks.TryGetValue(index, out var fb) && fb.Wrong > 0);
    }

    private static void AssertStaffColors(
        NoteSessionService session,
        int upperPitchCount,
        bool onlyIndex0Correct = false,
        int[]? wrongIndices = null)
    {
        wrongIndices ??= Array.Empty<int>();
        var wrongSet = wrongIndices.ToHashSet();

        for (int i = 0; i < session.NotesToDraw.Count; i++)
        {
            bool isActive = i < upperPitchCount;
            var state = StaffNoteStateResolver.Resolve(
                i,
                session.CurrentNoteIndex,
                isActive,
                session.CorrectNoteIndices,
                session.NoteFeedbacks);

            if (onlyIndex0Correct && i == 0)
            {
                Assert.Equal(StaffNoteState.Correct, state);
                continue;
            }

            if (wrongSet.Contains(i))
            {
                Assert.Equal(StaffNoteState.Wrong, state);
                continue;
            }

            if (i == session.CurrentNoteIndex && isActive)
                Assert.Equal(StaffNoteState.Current, state);
            else
                Assert.Equal(StaffNoteState.Pending, state);
        }
    }

    [Fact]
    public void FirstDetectedSound_AfterCountInWait_DoesNotMarkFutureNotesOverdue()
    {
        const int bpm = 98;
        double elapsed = 5000;
        var session = CreateDeferredClockPracticeTuneSession(
            firstStartBeat: 4,
            FifteenNoteMidis,
            bpm,
            () => elapsed);

        Assert.False(session.IsListeningClockRunning);

        // Post-count-in silence: without a running clock, overdue catch-up must not run.
        Assert.False(session.AdvanceTimelineForExpiredNotes());
        Assert.Equal(0, session.CurrentNoteIndex);
        for (int i = 0; i < FifteenNoteMidis.Length; i++)
            AssertNoWrongFeedback(session, i);

        // First detected sound starts the conductor timeline at t = 0.
        session.StartListeningClock();
        elapsed = 0;

        var result = session.Evaluate(Freq(FifteenNoteMidis[0]));
        Assert.True(session.UpdateFeedbackForCurrent(Freq(FifteenNoteMidis[0]), result));

        Assert.Equal(1, session.CurrentNoteIndex);
        for (int i = 1; i < FifteenNoteMidis.Length; i++)
            AssertNoWrongFeedback(session, i);
    }

    [Fact]
    public void FirstDetectedSound_DeferredClockMatchesScaleAndPracticeTune()
    {
        const int bpm = 60;
        double elapsed = 4000;
        var scaleSession = CreateDeferredClockSession([60, 62, 64, 65], bpm, () => elapsed);
        var tuneSession = CreateDeferredClockPracticeTuneSession(
            firstStartBeat: 4,
            [67, 69, 71, 72],
            bpm,
            () => elapsed);

        foreach (var session in new[] { scaleSession, tuneSession })
        {
            Assert.False(session.IsListeningClockRunning);
            Assert.False(session.AdvanceTimelineForExpiredNotes());
            Assert.Equal(0, session.CurrentNoteIndex);

            session.StartListeningClock();
            elapsed = 0;
            int firstMidi = session.NotesToDraw[0].Midi;
            var result = session.Evaluate(Freq(firstMidi));
            Assert.True(session.UpdateFeedbackForCurrent(Freq(firstMidi), result));

            for (int i = 1; i < session.NotesToDraw.Count; i++)
                AssertNoWrongFeedback(session, i);
        }
    }

    [Fact]
    public void CountInOffPath_WrongPitchBeforeFirstCorrect_DoesNotMarkFutureNotesMissed()
    {
        const int bpm = 60;
        double elapsed = 5000;
        var session = CreateDeferredClockSession([60, 62, 64, 65], bpm, () => elapsed);

        Assert.False(session.IsListeningClockRunning);

        var wrong = session.Evaluate(Freq(62));
        Assert.False(session.TryArmListeningClockOnFirstCorrectPitch(wrong.correct));
        Assert.False(session.IsListeningClockRunning);

        session.NotifySilence();
        Assert.False(session.AdvanceTimelineForExpiredNotes());
        Assert.Equal(0, session.CurrentNoteIndex);
        for (int i = 0; i < 4; i++)
            AssertNoWrongFeedback(session, i);

        elapsed = 10_000;
        session.NotifySilence();
        Assert.False(session.AdvanceTimelineForExpiredNotes());
        Assert.Equal(0, session.CurrentNoteIndex);

        Assert.True(session.TryArmListeningClockOnFirstCorrectPitch(pitchCorrect: true));
        elapsed = 0;
        var correct = session.Evaluate(Freq(60));
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), correct));

        Assert.Equal(1, session.CurrentNoteIndex);
        for (int i = 1; i < 4; i++)
            AssertNoWrongFeedback(session, i);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    public void CountInOffPath_ConductorCuesOff_SpuriousPitchThenSilence_DoesNotCascadeRed(int bpm)
    {
        double elapsed = 8000;
        var session = CreateDeferredClockSession([60, 62, 64, 65], bpm, () => elapsed);
        session.ShowConductorCues = false;

        Assert.False(session.TryArmListeningClockOnFirstCorrectPitch(
            session.Evaluate(Freq(64)).correct));

        for (int i = 0; i < 3; i++)
        {
            session.NotifySilence();
            Assert.False(session.AdvanceTimelineForExpiredNotes());
        }

        Assert.Equal(0, session.CurrentNoteIndex);
        for (int i = 0; i < 4; i++)
            AssertNoWrongFeedback(session, i);

        Assert.True(session.TryArmListeningClockOnFirstCorrectPitch(pitchCorrect: true));
        elapsed = 0;
        var first = session.Evaluate(Freq(60));
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), first));
        Assert.Equal(1, session.CurrentNoteIndex);
    }

    [Fact]
    public void PrematureClockAtCountInDownbeat_PausesInsteadOfMarkingFutureNotesMissed()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateSession([60, 62, 64, 65, 67], bpm, () => elapsed);

        elapsed = 3 * ConductorOnsetTiming.MsPerBeat(bpm);
        Assert.False(session.AdvanceTimelineForExpiredNotes());
        Assert.True(session.IsMusicalTimelinePaused);
        Assert.Equal(0, session.CurrentNoteIndex);
        for (int i = 0; i < 5; i++)
            AssertNoWrongFeedback(session, i);
    }

    private static NoteSessionService CreateDeferredClockSession(
        int[] midiNotes,
        int bpm,
        Func<double> elapsedMs)
    {
        var session = new NoteSessionService
        {
            Instrument = "concert-pitch",
            Tune = "Selected Scale",
            CooldownMs = 0,
            Tempo = bpm,
            ShowConductorCues = true,
            ChildLevel = 1,
        };
        session.Reset();
        session.Tune = "Selected Scale";
        session.Instrument = "concert-pitch";
        session.CooldownMs = 0;
        session.Tempo = bpm;
        session.ShowConductorCues = true;
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

    private static NoteSessionService CreateDeferredClockPracticeTuneSession(
        double firstStartBeat,
        int[] midiNotes,
        int bpm,
        Func<double> elapsedMs)
    {
        var session = new NoteSessionService
        {
            Instrument = "concert-pitch",
            Tune = "Practice Tune",
            CooldownMs = 0,
            Tempo = bpm,
            ShowConductorCues = true,
            ChildLevel = 1,
        };
        session.Reset();
        session.Tune = "Practice Tune";
        session.Instrument = "concert-pitch";
        session.CooldownMs = 0;
        session.Tempo = bpm;
        session.ShowConductorCues = true;
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
                StartBeat = firstStartBeat + i,
                GateBeatsAfterPrevious = i == 0 ? 0 : 1,
            });
            session.FeedbackViewModels.Add(new FeedbackItem(i, 0, 0, false));
        }

        session.ConfigureRhythmStartGates();
        // Deliberately omit StartListeningClock — matches count-in / Go deferral.
        return session;
    }

    private static NoteSessionService CreateSession(
        int[] midiNotes,
        int bpm,
        Func<double> elapsedMs)
    {
        var session = new NoteSessionService
        {
            Instrument = "concert-pitch",
            Tune = "Selected Scale",
            CooldownMs = 0,
            Tempo = bpm,
            ShowConductorCues = true,
            ChildLevel = 1,
        };
        session.Reset();
        session.Tune = "Selected Scale";
        session.Instrument = "concert-pitch";
        session.CooldownMs = 0;
        session.Tempo = bpm;
        session.ShowConductorCues = true;
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
        session.StartListeningClock();
        session.SessionElapsedMsOverride = elapsedMs;
        return session;
    }

    private static NoteSessionService CreateTuneStyleSession(
        double firstStartBeat,
        int[] midiNotes,
        int bpm,
        Func<double> elapsedMs)
    {
        var session = new NoteSessionService
        {
            Instrument = "concert-pitch",
            Tune = "Practice Tune",
            CooldownMs = 0,
            Tempo = bpm,
            ShowConductorCues = true,
            ChildLevel = 1,
        };
        session.Reset();
        session.Tune = "Practice Tune";
        session.Instrument = "concert-pitch";
        session.CooldownMs = 0;
        session.Tempo = bpm;
        session.ShowConductorCues = true;
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
                StartBeat = firstStartBeat + i,
                GateBeatsAfterPrevious = i == 0 ? 0 : 1,
            });
            session.FeedbackViewModels.Add(new FeedbackItem(i, 0, 0, false));
        }

        session.ConfigureRhythmStartGates();
        session.StartListeningClock();
        session.SessionElapsedMsOverride = elapsedMs;
        return session;
    }

    private static double Freq(int midi) => NoteSessionService.MidiToFreqPublic(midi);
}
