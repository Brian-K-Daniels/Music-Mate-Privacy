using musicmate.Models;

namespace musicmate.Services
{
    /// <summary>
    /// Resolves Settings → Music rhythm fields into values the sequence generator consumes.
    /// </summary>
    public static class RhythmSettingsResolver
    {
        /// <summary>
        /// Baseline variety used when Smallest Note permits sub-quarter durations but
        /// Rhythm Mode / Level left variety at 0 (e.g. Fixed + Simple + Eighth).
        /// Ensures enabled shorter notes have a real nonzero selection probability.
        /// </summary>
        public const int SubQuarterVarietyFloor = 40;

        /// <summary>
        /// Maps the persisted Smallest Note display string to a <see cref="NoteDuration"/>.
        /// </summary>
        public static NoteDuration ParseSmallestDuration(string? smallestRhythmNote)
            => smallestRhythmNote switch
            {
                "Sixteenth" => NoteDuration.Sixteenth,
                "Eighth" => NoteDuration.Eighth,
                _ => NoteDuration.Quarter
            };

        /// <summary>
        /// True when Smallest Note permits durations shorter than a quarter.
        /// </summary>
        public static bool AllowsSubQuarter(string? smallestRhythmNote)
            => ParseSmallestDuration(smallestRhythmNote).ToBeatValue()
               < NoteDuration.Quarter.ToBeatValue() - 1e-9;

        /// <summary>
        /// True when <paramref name="duration"/> is permitted by the Smallest Note floor
        /// (duration may be equal to or longer than the smallest allowed).
        /// </summary>
        public static bool IsDurationAllowed(NoteDuration duration, NoteDuration smallestAllowed)
            => duration.ToBeatValue() + 1e-9 >= smallestAllowed.ToBeatValue();

        /// <summary>
        /// Resolves the rhythm-variety percent passed into <see cref="MusicSequenceGenerator"/>.
        /// When Smallest Note enables eighths/sixteenths, variety is floored so those
        /// durations are not silently omitted under Simple / Level-0 variety.
        /// </summary>
        public static int ResolveVarietyPercent(
            int rhythmVarietyPercent,
            string? rhythmMode,
            string? smallestRhythmNote)
        {
            int variety = rhythmVarietyPercent >= 0
                ? rhythmVarietyPercent
                : string.Equals(rhythmMode, "Mixed", StringComparison.OrdinalIgnoreCase)
                    ? 60
                    : 0;

            if (variety <= 0 && AllowsSubQuarter(smallestRhythmNote))
                return SubQuarterVarietyFloor;

            return variety;
        }

        /// <summary>
        /// Session overload used by Music page / interval builders.
        /// </summary>
        public static int ResolveVarietyPercent(NoteSessionService session)
            => ResolveVarietyPercent(
                session.RhythmVarietyPercent,
                session.RhythmMode,
                session.SmallestRhythmNote);
    }
}
