namespace musicmate.Models
{
    /// <summary>
    /// Rhythmic duration of a single note.
    /// </summary>
    public enum NoteDuration
    {
        Whole,
        Half,
        Quarter,
        Eighth,
        Sixteenth
    }

    /// <summary>
    /// Helper methods for <see cref="NoteDuration"/>.
    /// </summary>
    public static class NoteDurationHelper
    {
        /// <summary>
        /// Returns the beat value of a duration relative to a quarter-note beat.
        /// Whole = 4, Half = 2, Quarter = 1, Eighth = 0.5, Sixteenth = 0.25.
        /// </summary>
        public static double ToBeatValue(this NoteDuration duration) => duration switch
        {
            NoteDuration.Whole => 4.0,
            NoteDuration.Half => 2.0,
            NoteDuration.Quarter => 1.0,
            NoteDuration.Eighth => 0.5,
            NoteDuration.Sixteenth => 0.25,
            _ => 1.0
        };

        /// <summary>
        /// Exact enum match for a beat length on the sixteenth grid, or null when the
        /// value needs multiple glyphs (e.g. 1.5 → quarter + eighth).
        /// </summary>
        public static NoteDuration? TryFromExactBeatValue(double beats)
        {
            const double eps = 1e-9;
            if (Math.Abs(beats - 4.0) < eps) return NoteDuration.Whole;
            if (Math.Abs(beats - 2.0) < eps) return NoteDuration.Half;
            if (Math.Abs(beats - 1.0) < eps) return NoteDuration.Quarter;
            if (Math.Abs(beats - 0.5) < eps) return NoteDuration.Eighth;
            if (Math.Abs(beats - 0.25) < eps) return NoteDuration.Sixteenth;
            return null;
        }

        /// <summary>
        /// Greedy decomposition of a positive beat length into undotted note values
        /// on the sixteenth grid. Segments always sum to <paramref name="beats"/>.
        /// </summary>
        public static List<NoteDuration> DecomposeBeats(double beats)
        {
            var parts = new List<NoteDuration>();
            double remaining = Math.Round(beats * 4.0) / 4.0; // snap to sixteenth ticks
            if (remaining <= 1e-9)
                return parts;

            var units = new[]
            {
                NoteDuration.Whole,
                NoteDuration.Half,
                NoteDuration.Quarter,
                NoteDuration.Eighth,
                NoteDuration.Sixteenth,
            };

            foreach (var unit in units)
            {
                double unitBeats = unit.ToBeatValue();
                while (remaining + 1e-9 >= unitBeats)
                {
                    parts.Add(unit);
                    remaining -= unitBeats;
                }
            }

            return parts;
        }
    }
}
