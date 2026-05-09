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

        /// <summary>3/4 time.</summary>
        public static readonly TimeSignature ThreeFour = new(3, NoteDuration.Quarter);

        public TimeSignature(int beats, NoteDuration beatUnit)
        {
            if (beats <= 0) throw new ArgumentOutOfRangeException(nameof(beats));
            Beats = beats;
            BeatUnit = beatUnit;
        }

        public override string ToString() => $"{Beats}/{(int)(4.0 / BeatUnit.ToBeatValue())}";
    }
}
