namespace musicmate.Models
{
    /// <summary>
    /// Represents a musical time signature (e.g. 4/4, 3/4, 6/8).
    /// <para>
    /// <see cref="Beats"/> is the top number — how many beats per measure.
    /// <see cref="BeatUnit"/> is the bottom number expressed as a <see cref="NoteDuration"/>
    /// — which note value equals one written beat (Quarter = 4, Eighth = 8, etc.).
    /// </para>
    /// </summary>
    public sealed class TimeSignature
    {
        /// <summary>Number of beats in each measure (top number).</summary>
        public int Beats { get; }

        /// <summary>Note value that equals one beat (bottom number).</summary>
        public NoteDuration BeatUnit { get; }

        /// <summary>2/2 time — cut time / alla breve.</summary>
        public static readonly TimeSignature TwoTwo = new(2, NoteDuration.Half);

        /// <summary>2/4 time — march, polka.</summary>
        public static readonly TimeSignature TwoFour = new(2, NoteDuration.Quarter);

        /// <summary>3/4 time — waltz, minuet.</summary>
        public static readonly TimeSignature ThreeFour = new(3, NoteDuration.Quarter);

        /// <summary>4/4 time — the default for most practice tunes.</summary>
        public static readonly TimeSignature FourFour = new(4, NoteDuration.Quarter);

        /// <summary>5/4 time.</summary>
        public static readonly TimeSignature FiveFour = new(5, NoteDuration.Quarter);

        /// <summary>3/8 time.</summary>
        public static readonly TimeSignature ThreeEight = new(3, NoteDuration.Eighth);

        /// <summary>6/8 time — compound duple (two dotted-quarter conducted beats).</summary>
        public static readonly TimeSignature SixEight = new(6, NoteDuration.Eighth);

        /// <summary>9/8 time — compound triple.</summary>
        public static readonly TimeSignature NineEight = new(9, NoteDuration.Eighth);

        /// <summary>12/8 time — compound quadruple.</summary>
        public static readonly TimeSignature TwelveEight = new(12, NoteDuration.Eighth);

        /// <summary>
        /// Display strings offered in Settings (common meters for practice).
        /// </summary>
        public static IReadOnlyList<string> CommonDisplayOptions { get; } = new[]
        {
            "2/2", "2/4", "3/4", "4/4", "5/4",
            "3/8", "6/8", "9/8", "12/8",
        };

        /// <summary>
        /// Total beat capacity of one measure, expressed as quarter-note beats.
        /// E.g. 4/4 → 4.0, 3/4 → 3.0, 6/8 → 3.0 (six eighth-notes = three quarter beats).
        /// </summary>
        public double TotalBeats => Beats * BeatUnit.ToBeatValue();

        public TimeSignature(int beats, NoteDuration beatUnit)
        {
            if (beats <= 0) throw new ArgumentOutOfRangeException(nameof(beats));
            Beats = beats;
            BeatUnit = beatUnit;
        }

        /// <summary>
        /// Parses a display string such as <c>4/4</c> or <c>6/8</c>.
        /// </summary>
        public static bool TryParse(string? display, out TimeSignature timeSignature)
        {
            timeSignature = FourFour;
            if (string.IsNullOrWhiteSpace(display))
                return false;

            var parts = display.Split('/');
            if (parts.Length != 2
                || !int.TryParse(parts[0].Trim(), out int beats)
                || !int.TryParse(parts[1].Trim(), out int denominator)
                || beats <= 0)
            {
                return false;
            }

            if (!TryDenominatorToBeatUnit(denominator, out var beatUnit))
                return false;

            timeSignature = new TimeSignature(beats, beatUnit);
            return true;
        }

        /// <summary>
        /// Parses a settings display string, falling back to <see cref="FourFour"/> when invalid.
        /// </summary>
        public static TimeSignature FromDisplayString(string? display)
            => TryParse(display, out var ts) ? ts : FourFour;

        private static bool TryDenominatorToBeatUnit(int denominator, out NoteDuration beatUnit)
        {
            beatUnit = NoteDuration.Quarter;
            switch (denominator)
            {
                case 1:
                    beatUnit = NoteDuration.Whole;
                    return true;
                case 2:
                    beatUnit = NoteDuration.Half;
                    return true;
                case 4:
                    beatUnit = NoteDuration.Quarter;
                    return true;
                case 8:
                    beatUnit = NoteDuration.Eighth;
                    return true;
                case 16:
                    beatUnit = NoteDuration.Sixteenth;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns true when the two time signatures have the same effective capacity
        /// (same top number and the same beat-unit duration), regardless of object identity.
        /// </summary>
        public bool Equals(TimeSignature? other) =>
            other is not null && Beats == other.Beats && BeatUnit == other.BeatUnit;

        public override bool Equals(object? obj) => Equals(obj as TimeSignature);
        public override int GetHashCode() => HashCode.Combine(Beats, BeatUnit);
        public override string ToString() => $"{Beats}/{(int)(4.0 / BeatUnit.ToBeatValue())}";
    }
}
