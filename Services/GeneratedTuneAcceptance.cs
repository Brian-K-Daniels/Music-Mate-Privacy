using musicmate.Models;

namespace musicmate.Services
{
    /// <summary>
    /// Accept/retry rules for automatically generated tunes before they are displayed.
    /// Saved tunes, scales, and arpeggios are exempt via
    /// <see cref="DisplayedTuneHistory.IsUniquenessRequired"/>.
    /// </summary>
    public static class GeneratedTuneAcceptance
    {
        public const int MaxGenerationAttempts = DisplayedTuneHistory.MaxGenerationAttempts;

        public static bool ChecksRequired(
            NoteSessionService session,
            bool layoutTestTuneEnabled)
            => DisplayedTuneHistory.IsUniquenessRequired(session, layoutTestTuneEnabled);

        public static bool ChecksRequired(
            NoteSessionService session,
            bool layoutTestTuneEnabled,
            string? selectedTunePreference)
            => DisplayedTuneHistory.IsUniquenessRequired(
                session, layoutTestTuneEnabled, selectedTunePreference);

        public static bool HasEnoughDistinctSoundedPitches(
            IEnumerable<GeneratedNote>? upper,
            IEnumerable<GeneratedNote>? lower)
            => MelodicVarietyRules.HasAtLeastTwoDistinctSoundedPitches(upper, lower);

        /// <summary>
        /// True when another generation attempt should run. Always false on the last
        /// attempt so a candidate is always displayed.
        /// </summary>
        public static bool ShouldRetry(
            bool checksRequired,
            bool enoughDistinctPitches,
            bool uniquenessAccepted,
            int attemptIndex,
            int maxAttempts = MaxGenerationAttempts)
        {
            if (!checksRequired)
                return false;
            if (maxAttempts <= 1 || attemptIndex >= maxAttempts - 1)
                return false;
            if (!enoughDistinctPitches)
                return true;
            return !uniquenessAccepted;
        }
    }
}
