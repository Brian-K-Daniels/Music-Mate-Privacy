using System.Diagnostics;
using musicmate.Models;

namespace musicmate.Diagnostics
{
    /// <summary>
    /// DEBUG helpers for verifying autoplay respects rests and rhythmic spans.
    /// </summary>
    public static class PlaybackRhythmDiagnostics
    {
        public static double TotalBeatSpan(IReadOnlyList<GeneratedNote> sequence)
        {
            double beats = 0;
            for (int i = 0; i < sequence.Count; i++)
                beats += sequence[i].BeatDuration;
            return beats;
        }

        /// <summary>
        /// True when sequence is quarter, quarter-rest, quarter, quarter-rest (four beats).
        /// </summary>
        public static bool IsAlternatingQuarterRestPattern(IReadOnlyList<GeneratedNote> sequence)
        {
            if (sequence.Count != 4)
                return false;

            return !sequence[0].IsRest && sequence[0].Duration == NoteDuration.Quarter
                && sequence[1].IsRest && sequence[1].Duration == NoteDuration.Quarter
                && !sequence[2].IsRest && sequence[2].Duration == NoteDuration.Quarter
                && sequence[3].IsRest && sequence[3].Duration == NoteDuration.Quarter;
        }

        public static void LogRhythmSpan(string label, IReadOnlyList<GeneratedNote> sequence)
        {
#if DEBUG
            Debug.WriteLine($"[Autoplay] {label}: events={sequence.Count}, beats={TotalBeatSpan(sequence):F2}");
#endif
        }
    }
}
