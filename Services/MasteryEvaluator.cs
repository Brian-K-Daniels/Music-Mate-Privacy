using System.Diagnostics;
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
    {
        if (session.MasteredMethod == "Streak")
        {
            bool mastered = stat.Streak >= session.StreakCrit;
            LogMasteryDecision(stat.WrittenName, mastered, "Streak", stat);
            return mastered;
        }

        int totalAttempts = stat.OverallCorrectCount + stat.OverallWrongCount;
        if (totalAttempts < session.MinCorrectCount)
        {
            LogMasteryDecision(stat.WrittenName, false, "InsufficientAttempts", stat);
            return false;
        }

        bool pitchOk = stat.PercentPitchCorrect >= session.CorrectThreshold;
        if (!pitchOk)
        {
            LogMasteryDecision(stat.WrittenName, false, "PitchBelowThreshold", stat);
            return false;
        }

        bool timingRequired = TimingAffectsMastery(session.ChildLevel);
        if (timingRequired)
        {
            int timingAttempts = stat.TimingCorrectCount + stat.TimingWrongCount;
            if (timingAttempts > 0)
            {
                bool timingOk = stat.PercentTimingCorrect >= session.CorrectThreshold;
                if (!timingOk)
                {
                    LogMasteryDecision(stat.WrittenName, false, "TimingBelowThreshold", stat);
                    return false;
                }
            }
        }

        bool overallOk = stat.PercentOverallCorrect >= session.CorrectThreshold;
        if (!overallOk)
        {
            LogMasteryDecision(stat.WrittenName, false, "OverallBelowThreshold", stat);
            return false;
        }

        if (session.OmitMsAvgThreshold > 0 && stat.MsCount > 0 && stat.MsAverage >= session.OmitMsAvgThreshold)
        {
            LogMasteryDecision(stat.WrittenName, false, "MsAverageTooSlow", stat);
            return false;
        }

        LogMasteryDecision(stat.WrittenName, true, "FullyMastered", stat);
        return true;
    }

    private static void LogMasteryDecision(string writtenName, bool mastered, string reason, NoteStat stat)
    {
        if (!TimingDiagnostics.EnableTimingDiagnostics)
            return;

        Debug.WriteLine(
            $"[Mastery] note={writtenName} mastered={mastered} reason={reason} " +
            $"pitch={stat.PercentPitchCorrect:F1}% timing={stat.PercentTimingCorrect:F1}% " +
            $"overall={stat.PercentOverallCorrect:F1}% attempts={stat.OverallCorrectCount + stat.OverallWrongCount}");
    }
}
