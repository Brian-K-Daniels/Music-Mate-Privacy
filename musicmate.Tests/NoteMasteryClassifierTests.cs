using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

public class NoteMasteryClassifierTests
{
    [Fact]
    public void Classify_NullOrZeroAttempts_IsNotYetAttempted()
    {
        Assert.Equal(
            NoteMasteryState.NotYetAttempted,
            NoteMasteryClassifier.Classify(null, "% Correct", 3, 6, 60, 1, 0));

        var empty = new NoteStat { WrittenName = "C4" };
        Assert.Equal(
            NoteMasteryState.NotYetAttempted,
            NoteMasteryClassifier.Classify(empty, "% Correct", 3, 6, 60, 1, 0));
    }

    [Fact]
    public void Classify_UsesExistingMasteryRule_ForMastered()
    {
        var mastered = new NoteStat
        {
            WrittenName = "G4",
            Streak = 5,
            OverallCorrectCount = 10,
            OverallWrongCount = 0,
            PitchCorrectCount = 10,
            PitchWrongCount = 0,
            Correct = 10,
            Wrong = 0,
        };

        Assert.True(MasteryEvaluator.IsFullyMastered(mastered, "Streak", 3, 6, 60, 1, 0));
        Assert.Equal(
            NoteMasteryState.Mastered,
            NoteMasteryClassifier.Classify(mastered, "Streak", 3, 6, 60, 1, 0));
    }

    [Fact]
    public void Classify_Improving_WhenHalfwayToThreshold()
    {
        var improving = new NoteStat
        {
            WrittenName = "A4",
            OverallCorrectCount = 4,
            OverallWrongCount = 4,
            PitchCorrectCount = 4,
            PitchWrongCount = 4,
            Correct = 4,
            Wrong = 4,
        };

        // 50% overall, CorrectThreshold 60 → improving floor 30 → Improving, not Mastered
        Assert.False(MasteryEvaluator.IsFullyMastered(improving, "% Correct", 3, 6, 60, 1, 0));
        Assert.Equal(
            NoteMasteryState.Improving,
            NoteMasteryClassifier.Classify(improving, "% Correct", 3, 6, 60, 1, 0));
    }

    [Fact]
    public void Classify_NeedsPractice_WhenBelowImprovingFloor()
    {
        var weak = new NoteStat
        {
            WrittenName = "B4",
            OverallCorrectCount = 1,
            OverallWrongCount = 9,
            PitchCorrectCount = 1,
            PitchWrongCount = 9,
            Correct = 1,
            Wrong = 9,
        };

        Assert.Equal(
            NoteMasteryState.NeedsPractice,
            NoteMasteryClassifier.Classify(weak, "% Correct", 3, 6, 60, 1, 0));
    }
}
