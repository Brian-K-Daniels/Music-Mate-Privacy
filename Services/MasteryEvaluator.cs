using musicmate.Diagnostics;

namespace musicmate.Services;
/// <summary>
/// Determines whether a written note is fully mastered for tune-generation omission.
/// </summary>
public static class MasteryEvaluator
{
    /// <summary>At this child level and above, timing accuracy is required for full mastery.</summary>
    public const int TimingCountsForMasteryStartingLevel = 21;

    public static bool TimingAffectsMastery(int childLevel)
        => childLevel >= TimingCountsForMasteryStartingLevel;

    /// <summary>
    /// When timing is not active for the level, overall mastery follows pitch only.
    /// When timing is active, pitch, timing, and overall thresholds must all pass.
    /// </summary>
    public static bool IsFullyMastered(NoteStat stat, NoteSessionService session)
        => IsFullyMastered(
            stat,
            session.MasteredMethod,
            session.StreakCrit,
            session.MinCorrectCount,
            session.CorrectThreshold,
            session.ChildLevel,
            session.OmitMsAvgThreshold);

    public static bool IsFullyMastered(
        NoteStat stat,
        string masteredMethod,
        int streakCrit,
        int minCorrectCount,
        int correctThreshold,
        int childLevel,
        int omitMsAvgThreshold)
    {
        if (masteredMethod == "Streak")
        {
            bool mastered = stat.Streak >= streakCrit;
            LogMasteryDecision(stat.WrittenName, mastered, "Streak", stat);
            return mastered;
        }

        int totalAttempts = stat.OverallCorrectCount + stat.OverallWrongCount;
        if (totalAttempts < minCorrectCount)
        {
            LogMasteryDecision(stat.WrittenName, false, "InsufficientAttempts", stat);
            return false;
        }

        bool pitchOk = stat.PercentPitchCorrect >= correctThreshold;
        if (!pitchOk)
        {
            LogMasteryDecision(stat.WrittenName, false, "PitchBelowThreshold", stat);
            return false;
        }

        bool timingRequired = TimingAffectsMastery(childLevel);
        if (timingRequired)
        {
            int timingAttempts = stat.TimingCorrectCount + stat.TimingWrongCount;
            if (timingAttempts > 0)
            {
                bool timingOk = stat.PercentTimingCorrect >= correctThreshold;
                if (!timingOk)
                {
                    LogMasteryDecision(stat.WrittenName, false, "TimingBelowThreshold", stat);
                    return false;
                }
            }
        }

        bool overallOk = stat.PercentOverallCorrect >= correctThreshold;
        if (!overallOk)
        {
            LogMasteryDecision(stat.WrittenName, false, "OverallBelowThreshold", stat);
            return false;
        }

        if (omitMsAvgThreshold > 0 && stat.MsCount > 0 && stat.MsAverage >= omitMsAvgThreshold)
        {
            LogMasteryDecision(stat.WrittenName, false, "MsAverageTooSlow", stat);
            return false;
        }

        LogMasteryDecision(stat.WrittenName, true, "FullyMastered", stat);
        return true;
    }

    /// <summary>Updates persisted and display mastery fields from current stats and session settings.</summary>
    public static void RefreshMasteredFields(NoteStat stat, NoteSessionService session)
        => RefreshMasteredFields(
            stat,
            session.MasteredMethod,
            session.StreakCrit,
            session.MinCorrectCount,
            session.CorrectThreshold,
            session.ChildLevel,
            session.OmitMsAvgThreshold);

    public static void RefreshMasteredFields(
        NoteStat stat,
        string masteredMethod,
        int streakCrit,
        int minCorrectCount,
        int correctThreshold,
        int childLevel,
        int omitMsAvgThreshold)
    {
        bool mastered = IsFullyMastered(
            stat, masteredMethod, streakCrit, minCorrectCount, correctThreshold, childLevel, omitMsAvgThreshold);
        stat.Mastered = mastered ? 1 : 0;
        stat.MasteredDisplay = mastered ? "Yes" : string.Empty;
    }

    private static void LogMasteryDecision(string writtenName, bool mastered, string reason, NoteStat stat)
    {
        DebugLog.RunIfEnabled(DebugLogCategory.Timing, () =>
            DebugLog.WriteLine(
                $"[Mastery] note={writtenName} mastered={mastered} reason={reason} " +
                $"pitch={stat.PercentPitchCorrect:F1}% timing={stat.PercentTimingCorrect:F1}% " +
                $"overall={stat.PercentOverallCorrect:F1}% attempts={stat.OverallCorrectCount + stat.OverallWrongCount}"));
    }
}
