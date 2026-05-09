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
            NoteDuration.Whole      => 4.0,
            NoteDuration.Half       => 2.0,
            NoteDuration.Quarter    => 1.0,
            NoteDuration.Eighth     => 0.5,
            NoteDuration.Sixteenth  => 0.25,
            _                       => 1.0
        };
    }
}
