using musicmate.Services;

namespace musicmate.Tests;

public class MasteryEvaluatorTests
{
    [Fact]
    public void IsFullyMastered_StreakMethod_RequiresStreakAtOrAboveCrit()
    {
        var stat = new NoteStat { WrittenName = "C4", Streak = 2 };

        Assert.False(MasteryEvaluator.IsFullyMastered(stat, "Streak", streakCrit: 3, 0, 0, 0, 0));

        stat.Streak = 3;
        Assert.True(MasteryEvaluator.IsFullyMastered(stat, "Streak", streakCrit: 3, 0, 0, 0, 0));
    }

    [Fact]
    public void IsFullyMastered_PercentCorrect_RequiresMinAttemptsAndThresholds()
    {
        var stat = new NoteStat
        {
            WrittenName = "C4",
            OverallCorrectCount = 1,
            OverallWrongCount = 1,
            PitchCorrectCount = 1,
            PitchWrongCount = 1,
        };
        Assert.False(MasteryEvaluator.IsFullyMastered(
            stat, "% Correct", streakCrit: 3, minCorrectCount: 3, correctThreshold: 50, childLevel: 1, omitMsAvgThreshold: 0));

        stat.OverallCorrectCount = 3;
        stat.OverallWrongCount = 0;
        stat.PitchCorrectCount = 3;
        stat.PitchWrongCount = 0;
        Assert.True(MasteryEvaluator.IsFullyMastered(
            stat, "% Correct", streakCrit: 3, minCorrectCount: 3, correctThreshold: 50, childLevel: 1, omitMsAvgThreshold: 0));
    }

    [Fact]
    public void IsFullyMastered_PercentCorrect_RejectsSlowMsAverageWhenThresholdSet()
    {
        var stat = new NoteStat
        {
            WrittenName = "C4",
            OverallCorrectCount = 5,
            PitchCorrectCount = 5,
            MsCount = 3,
            MsAverage = 600,
        };

        Assert.False(MasteryEvaluator.IsFullyMastered(
            stat, "% Correct", streakCrit: 3, minCorrectCount: 1, correctThreshold: 50, childLevel: 1, omitMsAvgThreshold: 500));

        stat.MsAverage = 400;
        Assert.True(MasteryEvaluator.IsFullyMastered(
            stat, "% Correct", streakCrit: 3, minCorrectCount: 1, correctThreshold: 50, childLevel: 1, omitMsAvgThreshold: 500));
    }

    [Fact]
    public void RefreshMasteredFields_SetsPersistedAndDisplayFields()
    {
        var stat = new NoteStat
        {
            WrittenName = "C4",
            Streak = 5,
        };

        MasteryEvaluator.RefreshMasteredFields(
            stat,
            masteredMethod: "Streak",
            streakCrit: 3,
            minCorrectCount: 6,
            correctThreshold: 95,
            childLevel: 0,
            omitMsAvgThreshold: 400);

        Assert.Equal(1, stat.Mastered);
        Assert.Equal("Yes", stat.MasteredDisplay);

        stat.Streak = 1;
        MasteryEvaluator.RefreshMasteredFields(
            stat,
            masteredMethod: "Streak",
            streakCrit: 3,
            minCorrectCount: 6,
            correctThreshold: 95,
            childLevel: 0,
            omitMsAvgThreshold: 400);

        Assert.Equal(0, stat.Mastered);
        Assert.Equal(string.Empty, stat.MasteredDisplay);
    }
}
