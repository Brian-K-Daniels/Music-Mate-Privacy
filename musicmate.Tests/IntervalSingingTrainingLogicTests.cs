using musicmate.Services;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class IntervalSingingTrainingLogicTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();
    private readonly Random _rng = new(42);

    public IntervalSingingTrainingLogicTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Fact]
    public void C4_MajorThird_Ascending_ExpectsE4()
    {
        Assert.True(IntervalSingingTrainingLogic.TryExpectedPitches(
            startWrittenMidi: 60, lowWrittenMidi: 48, highWrittenMidi: 84,
            semitones: 4, ascending: true, out var pitches));
        Assert.Equal(60, pitches.StartWrittenMidi);
        Assert.Equal(64, pitches.EndWrittenMidi);
        Assert.Equal(4, pitches.Semitones);
        Assert.True(pitches.IsAscending);
    }

    [Fact]
    public void G4_PerfectFifth_Descending_ExpectsC4()
    {
        Assert.True(IntervalSingingTrainingLogic.TryExpectedPitches(
            startWrittenMidi: 67, lowWrittenMidi: 48, highWrittenMidi: 84,
            semitones: 7, ascending: false, out var pitches));
        Assert.Equal(67, pitches.StartWrittenMidi);
        Assert.Equal(60, pitches.EndWrittenMidi);
        Assert.Equal(7, pitches.Semitones);
        Assert.False(pitches.IsAscending);
    }

    [Fact]
    public void StartNearUpperLimit_ExcludesImpossibleAscending()
    {
        int high = 72; // C5
        Assert.False(IntervalSingingTrainingLogic.TryExpectedPitches(
            startWrittenMidi: high, lowWrittenMidi: 60, highWrittenMidi: high,
            semitones: 7, ascending: true, out _));
        Assert.False(IntervalEarTrainingLogic.CanFormIntervalFromStart(
            high, 60, high, 7, IntervalDirectionMode.Ascending));
    }

    [Fact]
    public void StartNearLowerLimit_ExcludesImpossibleDescending()
    {
        int low = 60; // C4
        Assert.False(IntervalSingingTrainingLogic.TryExpectedPitches(
            startWrittenMidi: low, lowWrittenMidi: low, highWrittenMidi: 72,
            semitones: 7, ascending: false, out _));
        Assert.False(IntervalEarTrainingLogic.CanFormIntervalFromStart(
            low, low, 72, 7, IntervalDirectionMode.Descending));
    }

    [Fact]
    public void CorrectDetectedPitch_Succeeds()
    {
        double hz = NoteSessionService.MidiToFreqPublic(64);
        var verdict = IntervalSingingTrainingLogic.JudgeDetectedPitch(hz, expectedConcertMidi: 64, toleranceCents: 50);
        Assert.Equal(SungPitchVerdict.Correct, verdict);
        Assert.Equal("Correct — E4", IntervalSingingTrainingLogic.FormatPitchFeedback(
            verdict, TunerReferenceNoteCatalog.FormatCompactWrittenLabel(64)));
    }

    [Theory]
    [InlineData(80, SungPitchVerdict.ALittleHigh)]
    [InlineData(-80, SungPitchVerdict.ALittleLow)]
    public void SlightlyHighOrLow_GivesNearMissFeedback(int centsOff, SungPitchVerdict expected)
    {
        double targetHz = NoteSessionService.MidiToFreqPublic(64);
        double hz = targetHz * Math.Pow(2.0, centsOff / 1200.0);
        var verdict = IntervalSingingTrainingLogic.JudgeDetectedPitch(hz, 64, toleranceCents: 50);
        Assert.Equal(expected, verdict);
    }

    [Fact]
    public void LowConfidencePitch_IsNotAcceptedImmediately()
    {
        var stabilizer = new SungPitchStabilizer(requiredWindows: 2);
        Assert.Null(stabilizer.Observe(SungPitchVerdict.Unstable));
        Assert.Null(stabilizer.Observe(SungPitchVerdict.Correct));
        Assert.Equal(SungPitchVerdict.Correct, stabilizer.Observe(SungPitchVerdict.Correct));
    }

    [Fact]
    public void Reveal_DoesNotCountAsCorrect()
    {
        var stats = new IntervalSingingSessionStats();
        stats.RecordExerciseStarted();
        stats.RecordReveal(alreadySucceeded: false);
        stats.RecordCorrect(revealed: true);
        Assert.Equal(1, stats.Attempts);
        Assert.Equal(0, stats.CorrectWithoutReveal);
        Assert.Equal(1, stats.Reveals);
        Assert.False(IntervalSingingTrainingLogic.CountsAsIndependentCorrect(revealed: true, succeeded: true));
    }

    [Fact]
    public void Imitate_TwoCorrectSungNotes_Succeed()
    {
        double first = NoteSessionService.MidiToFreqPublic(60);
        double second = NoteSessionService.MidiToFreqPublic(64);
        var tracker = new IntervalSingingImitateTracker(60, 64, toleranceCents: 50);

        Assert.Equal(ImitateObserveKind.Pending, tracker.Observe(first).Kind);
        Assert.Equal(ImitateObserveKind.FirstCorrect, tracker.Observe(first).Kind);
        tracker.NotifySilence();
        Assert.Equal(ImitateObserveKind.Pending, tracker.Observe(second).Kind);
        Assert.Equal(ImitateObserveKind.BothCorrect, tracker.Observe(second).Kind);
    }

    [Fact]
    public void Imitate_WrongSecondNote_Fails()
    {
        double first = NoteSessionService.MidiToFreqPublic(60);
        double lowSecond = NoteSessionService.MidiToFreqPublic(62);
        var tracker = new IntervalSingingImitateTracker(60, 64, toleranceCents: 50);

        tracker.Observe(first);
        tracker.Observe(first);
        tracker.NotifySilence();
        tracker.Observe(lowSecond);
        var result = tracker.Observe(lowSecond);
        Assert.Equal(ImitateObserveKind.SecondIncorrect, result.Kind);
        Assert.Contains("second note", result.FormatFeedback(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Imitate_NoteOrderMatters()
    {
        double second = NoteSessionService.MidiToFreqPublic(64);
        var tracker = new IntervalSingingImitateTracker(60, 64, toleranceCents: 50);
        tracker.Observe(second);
        var result = tracker.Observe(second);
        Assert.Equal(ImitateObserveKind.FirstIncorrect, result.Kind);
        Assert.NotEqual(ImitateObserveKind.BothCorrect, result.Kind);
    }

    [Fact]
    public void HearAndIdentify_NotationHiddenUntilCorrect()
    {
        Assert.False(IntervalSingingTrainingLogic.ShouldShowNotation(IntervalSingingExerciseState.WaitingForSinger));
        Assert.False(IntervalSingingTrainingLogic.ShouldShowNotation(IntervalSingingExerciseState.Incorrect));
        Assert.False(IntervalSingingTrainingLogic.ShouldShowNotation(IntervalSingingExerciseState.Presenting));
        Assert.True(IntervalSingingTrainingLogic.ShouldShowNotation(IntervalSingingExerciseState.Correct));
        Assert.True(IntervalSingingTrainingLogic.ShouldShowNotation(IntervalSingingExerciseState.Revealed));
        Assert.False(IntervalEarTrainingLogic.ShouldShowIntervalNotes(IntervalEarTrainingInteraction.UnansweredQuiz));
        Assert.True(IntervalEarTrainingLogic.ShouldShowIntervalNotes(IntervalEarTrainingInteraction.RevealedQuiz));
    }

    [Fact]
    public void LeavePageWhileListening_StopsCaptureAndClearsBusy()
    {
        var gate = new IntervalSingingListenGate
        {
            PageVisible = true,
            PlaybackActive = false,
            State = IntervalSingingExerciseState.WaitingForSinger,
        };
        Assert.True(gate.TryStartCapture());
        Assert.True(gate.CaptureActive);

        gate.PageVisible = false;
        gate.StopAll();

        Assert.False(gate.CaptureActive);
        Assert.False(gate.PlaybackActive);
        Assert.False(gate.ShouldCapture);
        Assert.Equal(0, gate.BusyDepth);
        Assert.Equal(IntervalSingingExerciseState.Idle, gate.State);
    }

    [Fact]
    public void ReturnToPage_DoesNotAutoStartCapture()
    {
        var gate = new IntervalSingingListenGate();
        gate.StopAll();
        gate.PageVisible = true;
        gate.State = IntervalSingingExerciseState.Idle;
        Assert.False(gate.ShouldCapture);
        Assert.False(gate.TryStartCapture());
    }

    [Fact]
    public void Persistence_ModeDirectionStartAndLevel_AreIndependent()
    {
        SessionPreferences.Set("ChildPractice.Level", 40);
        IntervalSightTrainingLogic.PersistLevel(7);

        IntervalSingingTrainingLogic.PersistMode(IntervalSingingTrainingMode.ImitateInterval);
        IntervalSingingTrainingLogic.PersistDirection(IntervalDirectionMode.Descending);
        IntervalSingingTrainingLogic.PersistStartNote(60);
        IntervalSingingTrainingLogic.PersistLevel(9);

        Assert.Equal(IntervalSingingTrainingMode.ImitateInterval, IntervalSingingTrainingLogic.LoadPersistedMode());
        Assert.Equal(IntervalDirectionMode.Descending, IntervalSingingTrainingLogic.LoadPersistedDirection());
        Assert.Equal(60, IntervalSingingTrainingLogic.LoadPersistedStartNote());
        Assert.Equal(9, IntervalSingingTrainingLogic.LoadPersistedLevel());
        Assert.Equal(40, SessionPreferences.Get("ChildPractice.Level", 1));
        Assert.Equal(7, IntervalSightTrainingLogic.LoadPersistedLevel());
        Assert.NotEqual(IntervalSingingTrainingLogic.LevelPreferenceKey, IntervalSightTrainingLogic.LevelPreferenceKey);
        Assert.NotEqual("ChildPractice.Level", IntervalSingingTrainingLogic.LevelPreferenceKey);
    }

    [Fact]
    public void AutomaticLevel_DoesNotIncludeTritoneUntilLate()
    {
        Assert.DoesNotContain(6, IntervalSingingTrainingLogic.GetAllowedSingingIntervals(1));
        Assert.Contains(4, IntervalSingingTrainingLogic.GetAllowedSingingIntervals(1));
        Assert.Contains(6, IntervalSingingTrainingLogic.GetAllowedSingingIntervals(100));
    }

    [Fact]
    public void AllowedIntervals_DoNotScatter_ManualStillPossibleAtLevelOne()
    {
        Assert.True(IntervalSingingTrainingLogic.TryPickManualInterval(
            6, 48, 84, IntervalDirectionMode.Ascending, startWrittenMidi: 60, _rng, out var pitches));
        Assert.Equal(6, pitches.Semitones);
        Assert.DoesNotContain(6, IntervalSingingTrainingLogic.GetAllowedSingingIntervals(1));
    }

    [Theory]
    [InlineData(IntervalSingingTrainingMode.SingInterval)]
    [InlineData(IntervalSingingTrainingMode.ImitateInterval)]
    [InlineData(IntervalSingingTrainingMode.HearAndIdentify)]
    public void VisibleButtons_AlwaysShowsSingingChrome(IntervalSingingTrainingMode mode)
    {
        var visible = IntervalSingingTrainingLogic.GetVisibleButtons(mode);
        Assert.True(IntervalSingingTrainingLogic.Shows(visible, IntervalSingingTrainingLogic.VisibleButtons.NewExercise));
        Assert.True(IntervalSingingTrainingLogic.Shows(visible, IntervalSingingTrainingLogic.VisibleButtons.HearAgain));
        Assert.True(IntervalSingingTrainingLogic.Shows(visible, IntervalSingingTrainingLogic.VisibleButtons.Reveal));
        Assert.True(IntervalSingingTrainingLogic.Shows(visible, IntervalSingingTrainingLogic.VisibleButtons.Direction));
    }

    [Fact]
    public void VisibleButtons_SingAndImitate_DoNotUseIntervalAnswerPad()
    {
        var sing = IntervalSingingTrainingLogic.GetVisibleButtons(IntervalSingingTrainingMode.SingInterval);
        var imitate = IntervalSingingTrainingLogic.GetVisibleButtons(IntervalSingingTrainingMode.ImitateInterval);
        var identify = IntervalSingingTrainingLogic.GetVisibleButtons(IntervalSingingTrainingMode.HearAndIdentify);

        Assert.False(IntervalSingingTrainingLogic.Shows(sing, IntervalSingingTrainingLogic.VisibleButtons.IntervalChoices));
        Assert.False(IntervalSingingTrainingLogic.Shows(imitate, IntervalSingingTrainingLogic.VisibleButtons.IntervalChoices));
        Assert.True(IntervalSingingTrainingLogic.Shows(identify, IntervalSingingTrainingLogic.VisibleButtons.IntervalChoices));
    }

    [Fact]
    public void VisibleButtons_SwitchingModes_HidesAndRestoresIntervalPad()
    {
        var sing = IntervalSingingTrainingLogic.GetVisibleButtons(IntervalSingingTrainingMode.SingInterval);
        var identify = IntervalSingingTrainingLogic.GetVisibleButtons(IntervalSingingTrainingMode.HearAndIdentify);
        var imitate = IntervalSingingTrainingLogic.GetVisibleButtons(IntervalSingingTrainingMode.ImitateInterval);
        var singAgain = IntervalSingingTrainingLogic.GetVisibleButtons(IntervalSingingTrainingMode.SingInterval);
        var identifyAgain = IntervalSingingTrainingLogic.GetVisibleButtons(IntervalSingingTrainingMode.HearAndIdentify);

        Assert.Equal(sing, imitate);
        Assert.Equal(sing, singAgain);
        Assert.Equal(identify, identifyAgain);
        Assert.NotEqual(sing, identify);
        Assert.True(IntervalSingingTrainingLogic.Shows(identifyAgain, IntervalSingingTrainingLogic.VisibleButtons.IntervalChoices));
        Assert.False(IntervalSingingTrainingLogic.Shows(singAgain, IntervalSingingTrainingLogic.VisibleButtons.IntervalChoices));
    }
}
