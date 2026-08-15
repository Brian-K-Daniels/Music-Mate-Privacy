namespace musicmate.Services
{
    /// <summary>
    /// Soft educational colors for Interval Ear Training buttons by interval family.
    /// Correct/wrong feedback colors are separate so they can override family colors.
    /// </summary>
    public static class IntervalEarTrainingButtonColors
    {
        /// <summary>Dark text for readable contrast on pale family backgrounds.</summary>
        public static readonly Color LabelText = Color.FromArgb("#1F2937");

        /// <summary>Soft outline so pale fills remain distinct from the page background.</summary>
        public static readonly Color Border = Color.FromArgb("#00000028");

        /// <summary>Gold outline for Play Random / Play Again only.</summary>
        public static readonly Color PlayButtonBorder = Color.FromArgb("#D4AF37");

        /// <summary>Temporary bold outline while an interval is being demonstrated (explore taps).</summary>
        public static readonly Color DemoHighlightBorder = Color.FromArgb("#DC2626");
        public const double DemoHighlightBorderWidth = 3;

        // Family fills (pale / unsaturated — not used for correct/wrong feedback).
        public static readonly Color Unison = Color.FromArgb("#E5E7EB");       // light gray
        public static readonly Color Minor = Color.FromArgb("#BFDBFE");        // light blue
        public static readonly Color Major = Color.FromArgb("#BBF7D0");        // light green (mint)
        public static readonly Color Perfect = Color.FromArgb("#FDE68A");      // light gold/yellow
        public static readonly Color Tritone = Color.FromArgb("#E9D5FF");      // light purple

        // Feedback overrides (distinct from family palettes; reserved red/green use).
        public static readonly Color CorrectBackground = Color.FromArgb("#22AA44");
        public static readonly Color WrongBackground = Color.FromArgb("#DC2626");
        public static readonly Color FeedbackLabelText = Colors.White;

        public enum IntervalFamily
        {
            Unison,
            Minor,
            Major,
            Perfect,
            Tritone,
        }

        public static IntervalFamily GetFamily(int semitones)
        {
            if (!IntervalEarTrainingCatalog.IsValidSemitoneCount(semitones))
                throw new ArgumentOutOfRangeException(nameof(semitones), semitones, "Interval must be 0–12 semitones.");

            return semitones switch
            {
                0 => IntervalFamily.Unison,
                6 => IntervalFamily.Tritone,
                1 or 3 or 8 or 10 => IntervalFamily.Minor,
                2 or 4 or 9 or 11 => IntervalFamily.Major,
                5 or 7 or 12 => IntervalFamily.Perfect,
                _ => IntervalFamily.Unison,
            };
        }

        public static Color FamilyBackground(int semitones)
            => GetFamily(semitones) switch
            {
                IntervalFamily.Unison => Unison,
                IntervalFamily.Minor => Minor,
                IntervalFamily.Major => Major,
                IntervalFamily.Perfect => Perfect,
                IntervalFamily.Tritone => Tritone,
                _ => Unison,
            };
    }
}
