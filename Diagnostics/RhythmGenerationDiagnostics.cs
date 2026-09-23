using musicmate.Models;
using musicmate.Services;

namespace musicmate.Diagnostics
{
    /// <summary>
    /// Temporary Random-tune rhythm diagnostics: configured vs effective Smallest Note,
    /// allowed duration set, per-note duration picks, and rejected eighth candidates.
    /// Gated by <see cref="DebugLogCategory.StaffAndSequence"/>.
    /// </summary>
    public static class RhythmGenerationDiagnostics
    {
        public static void LogTuneHeader(
            string configuredSmallest,
            NoteDuration effectiveSmallest,
            int configuredVariety,
            int effectiveVariety,
            IReadOnlyDictionary<NoteDuration, int> durationWeights,
            bool isRandomMode)
        {
            if (!isRandomMode)
                return;

            DebugLog.RunIfEnabled(DebugLogCategory.StaffAndSequence, () =>
            {
                string allowed = string.Join(
                    ", ",
                    durationWeights
                        .OrderByDescending(kv => kv.Key.ToBeatValue())
                        .Select(kv => $"{kv.Key}:{kv.Value}"));
                DebugLog.WriteLine(
                    DebugLogCategory.StaffAndSequence,
                    $"[RhythmGen] configuredSmallest={configuredSmallest} " +
                    $"effectiveSmallest={effectiveSmallest} " +
                    $"configuredVariety={configuredVariety} effectiveVariety={effectiveVariety} " +
                    $"allowed=[{allowed}]");
            });
        }

        public static void LogDurationSelected(
            NoteDuration duration,
            double beatsRemaining,
            bool isRest,
            bool isRandomMode)
        {
            if (!isRandomMode)
                return;

            DebugLog.WriteLine(
                DebugLogCategory.StaffAndSequence,
                $"[RhythmGen] selected={duration} rest={isRest} beatsRemaining={beatsRemaining:F3}");
        }

        public static void LogEighthRejected(
            string reason,
            double beatsRemaining,
            NoteDuration smallestAllowed,
            bool isRandomMode)
        {
            if (!isRandomMode)
                return;

            DebugLog.WriteLine(
                DebugLogCategory.StaffAndSequence,
                $"[RhythmGen] eighthRejected reason={reason} " +
                $"beatsRemaining={beatsRemaining:F3} smallest={smallestAllowed}");
        }
    }
}
