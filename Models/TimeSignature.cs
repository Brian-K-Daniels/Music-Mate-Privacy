namespace musicmate.Models
{
    /// <summary>
    /// Represents a musical time signature (e.g. 4/4, 3/4).
    /// <para>
    /// <see cref="Beats"/> is the top number — how many beats per measure.
    /// <see cref="BeatUnit"/> is the bottom number expressed as a <see cref="NoteDuration"/>
    /// — which note value equals one beat (Quarter = 4, Half = 2, etc.).
    /// </para>
    /// </summary>
    public sealed class TimeSignature
    {
        /// <summary>Number of beats in each measure (top number).</summary>
        public int Beats { get; }

        /// <summary>Note value that equals one beat (bottom number).</summary>
        public NoteDuration BeatUnit { get; }

        /// <summary>4/4 time — the default for most practice tunes.</summary>
        public static readonly TimeSignature FourFour = new(4, NoteDuration.Quarter);

        /// <summary>3/4 time — waltz, minuet.</summary>
        public static readonly TimeSignature ThreeFour = new(3, NoteDuration.Quarter);

        /// <summary>2/4 time — march, polka.</summary>
        public static readonly TimeSignature TwoFour = new(2, NoteDuration.Quarter);

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
