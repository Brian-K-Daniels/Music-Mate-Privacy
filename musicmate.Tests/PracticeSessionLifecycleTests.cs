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
}
