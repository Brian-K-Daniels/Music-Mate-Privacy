namespace musicmate.Services
{
    /// <summary>
    /// Interval names and labels for Interval Ear Training (0–12 ascending semitones).
    /// </summary>
    public static class IntervalEarTrainingCatalog
    {
        public const int MinSemitones = 0;
        public const int MaxSemitones = 12;

        public static readonly IReadOnlyList<(int Semitones, string Name)> Intervals =
        [
            (0, "Perfect unison"),
            (1, "Minor second"),
            (2, "Major second"),
            (3, "Minor third"),
            (4, "Major third"),
            (5, "Perfect fourth"),
            (6, "Tritone"),
            (7, "Perfect fifth"),
            (8, "Minor sixth"),
            (9, "Major sixth"),
            (10, "Minor seventh"),
            (11, "Major seventh"),
            (12, "Perfect octave"),
        ];

        public static string GetName(int semitones)
        {
            if (semitones < MinSemitones || semitones > MaxSemitones)
                throw new ArgumentOutOfRangeException(nameof(semitones), semitones, "Interval must be 0–12 semitones.");
            return Intervals[semitones].Name;
        }

        /// <summary>
        /// Left-aligned grid label; single-digit semitones are padded so units digits line up.
        /// </summary>
        public static string FormatButtonLabel(int semitones)
        {
            string n = GetName(semitones);
            return semitones < 10
                ? $" {semitones} — {n}"
                : $"{semitones} — {n}";
        }

        public static bool IsValidSemitoneCount(int semitones)
            => semitones >= MinSemitones && semitones <= MaxSemitones;
    }
}
