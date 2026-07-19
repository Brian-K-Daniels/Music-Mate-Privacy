using musicmate.Models;

namespace musicmate.Services;

/// <summary>
/// Classifies written-note statistics into Note Mastery display states.
/// Mastered always delegates to <see cref="MasteryEvaluator"/> — no second mastery rule.
/// </summary>
public static class NoteMasteryClassifier
{
    public static NoteMasteryState Classify(NoteStat? stat, NoteSessionService session)
        => Classify(
            stat,
            session.MasteredMethod,
            session.StreakCrit,
            session.MinCorrectCount,
            session.CorrectThreshold,
            session.ChildLevel,
            session.OmitMsAvgThreshold);

    public static NoteMasteryState Classify(
        NoteStat? stat,
        string masteredMethod,
        int streakCrit,
        int minCorrectCount,
        int correctThreshold,
        int childLevel,
        int omitMsAvgThreshold)
    {
        if (stat is null || TotalAttempts(stat) <= 0)
            return NoteMasteryState.NotYetAttempted;

        if (MasteryEvaluator.IsFullyMastered(
                stat, masteredMethod, streakCrit, minCorrectCount,
                correctThreshold, childLevel, omitMsAvgThreshold))
            return NoteMasteryState.Mastered;

        if (IsImproving(stat, masteredMethod, streakCrit, correctThreshold))
            return NoteMasteryState.Improving;

        return NoteMasteryState.NeedsPractice;
    }

    public static bool IsImproving(
        NoteStat stat,
        string masteredMethod,
        int streakCrit,
        int correctThreshold)
    {
        if (masteredMethod == "Streak")
        {
            int need = Math.Max(1, (int)Math.Ceiling(streakCrit * NoteMasteryPreferenceDefaults.ImprovingProgressFraction));
            return stat.Streak >= need;
        }

        double improvingFloor = correctThreshold * NoteMasteryPreferenceDefaults.ImprovingProgressFraction;
        return stat.PercentOverallCorrect >= improvingFloor
               || stat.PercentPitchCorrect >= improvingFloor;
    }

    public static int TotalAttempts(NoteStat stat)
        => Math.Max(
            stat.Correct + stat.Wrong,
            stat.OverallCorrectCount + stat.OverallWrongCount);
}
