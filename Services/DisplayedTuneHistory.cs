using Microsoft.Maui.Storage;

namespace musicmate.Services
{
    /// <summary>
    /// In-memory record of generated tunes displayed during this app process.
    /// Cleared only when the process ends (a new launch). Not persisted.
    /// </summary>
    public sealed class DisplayedTuneHistory
    {
        public const int MaxGenerationAttempts = 10;

        private readonly object _gate = new();
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

        public int Count
        {
            get { lock (_gate) return _seen.Count; }
        }

        public bool Contains(string? signature)
        {
            if (string.IsNullOrEmpty(signature))
                return false;
            lock (_gate)
                return _seen.Contains(signature);
        }

        /// <summary>
        /// Records <paramref name="signature"/> if it is new.
        /// Returns false when this content was already displayed (caller should retry).
        /// Empty signatures are ignored and not recorded.
        /// </summary>
        public bool TryAccept(string? signature)
        {
            if (string.IsNullOrEmpty(signature))
                return true;

            lock (_gate)
                return _seen.Add(signature);
        }

        /// <summary>
        /// True when the current play mode is generated music that should not
        /// repeat during this app session. Saved tunes, scales, and arpeggios are exempt.
        /// </summary>
        public static bool IsUniquenessRequired(
            NoteSessionService session,
            bool layoutTestTuneEnabled)
            => IsUniquenessRequired(
                session,
                layoutTestTuneEnabled,
                Preferences.Default.Get<string?>("SelectedTune", null));

        public static bool IsUniquenessRequired(
            NoteSessionService session,
            bool layoutTestTuneEnabled,
            string? selectedTunePreference)
        {
            if (session == null)
                return false;
            if (layoutTestTuneEnabled)
                return false;
            if (PlayModePickerOptions.IsTunerMode(session))
                return false;
            if (session.Tune == "Arpeggio")
                return false;
            if (session.Tune == "Practice Tune"
                && PracticeCompositionSelector.IsUserExplicitPlayMode(
                    session.Tune ?? string.Empty,
                    session.ScaleSelectionMode,
                    session.IsRandomMode,
                    selectedTunePreference))
                return false;
            if (session.Tune == "Selected Scale"
                && !session.IsRandomMode
                && !session.HasTemporaryNoteEmphasis)
                return false;

            return true;
        }
    }
}
