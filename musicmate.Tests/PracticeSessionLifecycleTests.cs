using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

public class PracticeSessionLifecycleTests
{
    [Theory]
    [InlineData(true, true, 4, false, PracticeExerciseStartAction.GenerateFresh)]
    [InlineData(false, true, 4, false, PracticeExerciseStartAction.RestoreRepeatSame)]
    [InlineData(false, false, 4, false, PracticeExerciseStartAction.GenerateFresh)]
    [InlineData(false, true, 0, false, PracticeExerciseStartAction.GenerateFresh)]
    [InlineData(false, true, 4, true, PracticeExerciseStartAction.RestoreRepeatSame)]
    public void PlanExerciseStart_SelectsExpectedAction(
        bool forceNewNotes,
        bool repeatSameTune,
        int snapshotNoteCount,
        bool logScaleKey,
        PracticeExerciseStartAction expected)
    {
        PracticeSessionSnapshot? snapshot = snapshotNoteCount > 0
            ? new PracticeSessionSnapshot { Notes = Enumerable.Range(0, snapshotNoteCount).Select(_ => new NoteInfo()).ToList() }
            : null;

        var plan = PracticeSessionLifecycle.PlanExerciseStart(
            repeatSameTune, snapshot, forceNewNotes, logScaleKey ? "GoButton" : null);

        Assert.Equal(expected, plan.Action);
        Assert.Equal(logScaleKey, plan.LogScaleKeyWithoutChanging);
    }

    [Theory]
    [InlineData(true, true, 4, PracticeSessionLifecycle.StopToggleAction.StopRestoreRepeatSame, false, false)]
    [InlineData(true, true, 0, PracticeSessionLifecycle.StopToggleAction.StopRegenerateFresh, false, true)]
    [InlineData(true, false, 4, PracticeSessionLifecycle.StopToggleAction.StopRegenerateFresh, false, true)]
    [InlineData(false, true, 4, PracticeSessionLifecycle.StopToggleAction.StartListening, false, false)]
    [InlineData(false, true, 0, PracticeSessionLifecycle.StopToggleAction.StartListening, false, false)]
    [InlineData(false, false, 4, PracticeSessionLifecycle.StopToggleAction.StartListening, false, false)]
    [InlineData(false, false, 0, PracticeSessionLifecycle.StopToggleAction.StartListening, false, false)]
    public void PlanStopToggle_SelectsExpectedAction(
        bool isRunning,
        bool repeatSameTune,
        int snapshotNoteCount,
        PracticeSessionLifecycle.StopToggleAction expected,
        bool forceNewNotesOnStart,
        bool clearSnapshotOnStop)
    {
        PracticeSessionSnapshot? snapshot = snapshotNoteCount > 0
            ? new PracticeSessionSnapshot { Notes = Enumerable.Range(0, snapshotNoteCount).Select(_ => new NoteInfo()).ToList() }
            : null;

        var plan = PracticeSessionLifecycle.PlanStopToggle(isRunning, repeatSameTune, snapshot);

        Assert.Equal(expected, plan.Action);
        Assert.Equal(forceNewNotesOnStart, plan.ForceNewNotes);
        Assert.Equal(clearSnapshotOnStop, plan.ClearRepeatSameSnapshot);
        if (expected == PracticeSessionLifecycle.StopToggleAction.StartListening)
            Assert.Equal("GoButton", plan.ScaleKeyTrigger);
    }

    [Fact]
    public void PlanStopToggle_GoNeverForcesNewNotes()
    {
        var withSnapshot = new PracticeSessionSnapshot
        {
            Notes = Enumerable.Range(0, 3).Select(_ => new NoteInfo()).ToList()
        };

        Assert.False(PracticeSessionLifecycle.PlanStopToggle(false, repeatSameTune: false, null).ForceNewNotes);
        Assert.False(PracticeSessionLifecycle.PlanStopToggle(false, repeatSameTune: true, withSnapshot).ForceNewNotes);
        Assert.False(PracticeSessionLifecycle.PlanStopToggle(false, repeatSameTune: true, null).ForceNewNotes);
    }

    [Theory]
    [InlineData(false, false, 4, true)]  // Go with notes → reuse
    [InlineData(true, false, 4, true)]   // Play with notes → reuse
    [InlineData(false, true, 4, false)]  // force new → generate
    [InlineData(false, false, 0, false)] // Go empty → do not reuse
    [InlineData(true, false, 0, false)]
    public void ShouldReuseDisplayedExercise_GoAndPlayReuseWhenNotesPresent(
        bool playBack,
        bool forceNewNotes,
        int noteCount,
        bool expected)
        => Assert.Equal(
            expected,
            PracticeSessionLifecycle.ShouldReuseDisplayedExercise(playBack, forceNewNotes, noteCount));

    [Fact]
    public void ShouldAbortListeningBecauseEmpty_WhenGoAndNoNotes()
    {
        Assert.True(PracticeSessionLifecycle.ShouldAbortListeningBecauseEmpty(forceNewNotes: false, 0));
        Assert.False(PracticeSessionLifecycle.ShouldAbortListeningBecauseEmpty(forceNewNotes: false, 3));
        Assert.False(PracticeSessionLifecycle.ShouldAbortListeningBecauseEmpty(forceNewNotes: true, 0));
    }

    [Fact]
    public void StopRegenerateOwnership_AndGoReuseOwnership_AreExplicit()
    {
        var stopPlan = PracticeSessionLifecycle.PlanStopToggle(
            isRunning: true, repeatSameTune: false, snapshot: null);
        Assert.Equal(PracticeSessionLifecycle.StopToggleAction.StopRegenerateFresh, stopPlan.Action);

        var goPlan = PracticeSessionLifecycle.PlanStopToggle(
            isRunning: false, repeatSameTune: false, snapshot: null);
        Assert.Equal(PracticeSessionLifecycle.StopToggleAction.StartListening, goPlan.Action);
        Assert.False(goPlan.ForceNewNotes);
        Assert.True(PracticeSessionLifecycle.ShouldReuseDisplayedExercise(
            playBack: false, forceNewNotes: goPlan.ForceNewNotes, displayedNoteCount: 5));
    }

    [Fact]
    public void RepeatedGoReuse_PreservesDisplayedFingerprint()
    {
        var notes = new List<NoteInfo>
        {
            new() { Name = "C4", Midi = 60, TargetFreq = 261.63, DurationBeats = 1 },
            new() { Name = "E4", Midi = 64, TargetFreq = 329.63, DurationBeats = 1 },
            new() { Name = "G4", Midi = 67, TargetFreq = 392.00, DurationBeats = 1 },
        };
        string original = PracticeSessionLifecycle.DisplayedExerciseFingerprint(notes);

        for (int i = 0; i < 5; i++)
        {
            var goPlan = PracticeSessionLifecycle.PlanStopToggle(false, false, null);
            Assert.False(goPlan.ForceNewNotes);
            Assert.True(PracticeSessionLifecycle.ShouldReuseDisplayedExercise(
                false, goPlan.ForceNewNotes, notes.Count));
            Assert.Equal(original, PracticeSessionLifecycle.DisplayedExerciseFingerprint(notes));
        }
    }

    [Fact]
    public void RepeatedStopRegeneratePlans_AlwaysRequestFreshMusicWhenNotRepeatSame()
    {
        for (int i = 0; i < 5; i++)
        {
            var stopPlan = PracticeSessionLifecycle.PlanStopToggle(
                isRunning: true, repeatSameTune: false, snapshot: null);
            Assert.Equal(PracticeSessionLifecycle.StopToggleAction.StopRegenerateFresh, stopPlan.Action);
            Assert.True(stopPlan.ClearRepeatSameSnapshot);
        }
    }

    [Fact]
    public void ResetEvaluationKeepingNotes_ClearsProgressButKeepsStaffNotes()
    {
        SessionPreferences.TestStore = new Dictionary<string, object?>();
        try
        {
            var session = new NoteSessionService
            {
                Instrument = "concert-pitch",
                Tune = "Selected Scale",
                Key = "C",
                SelectedScale = "Major",
            };
            session.NotesToDraw.Add(new NoteInfo
            {
                Name = "C4",
                Midi = 60,
                TargetFreq = 261.63,
                DurationBeats = 1,
                Duration = NoteDuration.Quarter,
            });
            session.NotesToDraw.Add(new NoteInfo
            {
                Name = "D4",
                Midi = 62,
                TargetFreq = 293.66,
                DurationBeats = 1,
                Duration = NoteDuration.Quarter,
            });
            session.CorrectNoteIndices.Add(0);
            session.SessionCompleted = true;
            session.FeedbackViewModels.Add(new FeedbackItem(0, 0, 0, true));
            session.FeedbackViewModels.Add(new FeedbackItem(1, 2, -15, false));
            string before = PracticeSessionLifecycle.DisplayedExerciseFingerprint(session.NotesToDraw);

            session.ResetEvaluationKeepingNotes();

            Assert.Equal(2, session.NotesToDraw.Count);
            Assert.Equal(2, session.FeedbackViewModels.Count);
            Assert.All(session.FeedbackViewModels, item => Assert.False(item.IsCorrect));
            Assert.Empty(session.CorrectNoteIndices);
            Assert.False(session.SessionCompleted);
            Assert.Equal(0, session.CurrentNoteIndex);
            Assert.Equal(before, PracticeSessionLifecycle.DisplayedExerciseFingerprint(session.NotesToDraw));

            session.StartListeningClock();
            double freq = NoteSessionService.MidiToFreqPublic(60);
            var heard = session.Evaluate(freq);
            Assert.True(heard.correct);
            Assert.True(session.UpdateFeedbackForCurrent(freq, heard));
            Assert.Contains(0, session.CorrectNoteIndices);
        }
        finally
        {
            SessionPreferences.TestStore = null;
        }
    }

    [Fact]
    public void FormatSessionResultBanner_IncludesLevelUpWhenPresent()
    {
        var stats = new PracticeSessionLifecycle.CompletionSummaryStats(8, 2, 80, 120);
        var text = PracticeSessionLifecycle.FormatSessionResultBanner(stats, newChildLevel: 5);
        Assert.Contains("80%", text);
        Assert.Contains("8/10", text);
        Assert.Contains("120 BPM", text);
        Assert.Contains("Level 5", text);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ShouldForceNewNotesForRepeatMode_MatchesRepeatNew(bool repeatSameTune, bool expected)
        => Assert.Equal(expected, PracticeSessionLifecycle.ShouldForceNewNotesForRepeatMode(repeatSameTune));

    [Fact]
    public void PlanExerciseStart_RepeatNewWithSnapshotStillGeneratesFresh()
    {
        var snapshot = new PracticeSessionSnapshot
        {
            Notes = Enumerable.Range(0, 4).Select(_ => new NoteInfo { Name = "G4" }).ToList()
        };

        var plan = PracticeSessionLifecycle.PlanExerciseStart(
            repeatSameTune: false, snapshot, forceNewNotes: true, scaleKeyTrigger: "AutoStart");

        Assert.Equal(PracticeExerciseStartAction.GenerateFresh, plan.Action);
    }

    [Fact]
    public void ShouldAbortPlaybackBecauseEmpty_WhenPlayAndNoNotes()
    {
        Assert.True(PracticeSessionLifecycle.ShouldAbortPlaybackBecauseEmpty(true, 0));
        Assert.False(PracticeSessionLifecycle.ShouldAbortPlaybackBecauseEmpty(true, 3));
        Assert.False(PracticeSessionLifecycle.ShouldAbortPlaybackBecauseEmpty(false, 0));
    }

    [Fact]
    public void DisplayedExerciseFingerprint_UnchangedWhenNoteListUnchanged()
    {
        var notes = new List<NoteInfo>
        {
            new() { Name = "C4", Midi = 60, TargetFreq = 261.63, DurationBeats = 1 },
            new() { Name = "E4", Midi = 64, TargetFreq = 329.63, DurationBeats = 1 },
        };
        string before = PracticeSessionLifecycle.DisplayedExerciseFingerprint(notes);

        // Simulate Play: same instances, no regenerate.
        string after = PracticeSessionLifecycle.DisplayedExerciseFingerprint(notes);
        Assert.Equal(before, after);

        notes[0].Midi = 62;
        Assert.NotEqual(before, PracticeSessionLifecycle.DisplayedExerciseFingerprint(notes));
    }

    [Theory]
    [InlineData(true, "Major", true)]
    [InlineData(true, "Tuner", false)]
    [InlineData(false, "Major", false)]
    public void ShouldAutoRepeat_RespectsTuneAndSetting(bool autoRepeat, string tune, bool expected)
        => Assert.Equal(expected, PracticeSessionLifecycle.ShouldAutoRepeat(autoRepeat, tune));

    [Theory]
    [InlineData(2.0, 2000)]
    [InlineData(0.5, 500)]
    public void GetAutoRepeatDelayMs_ConvertsSeconds(double seconds, int expectedMs)
        => Assert.Equal(expectedMs, PracticeSessionLifecycle.GetAutoRepeatDelayMs(seconds));
}
