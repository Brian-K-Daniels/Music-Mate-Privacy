using Microsoft.Maui.Storage;

namespace musicmate.Services
{
    /// <summary>
    /// Remembers the last committed generated tune in this app process so the next
    /// selection can avoid showing it twice in succession. Not persisted.
    /// Candidates are evaluated against <see cref="PreviousSignature"/>; only
    /// <see cref="CommitDisplayed"/> (or <see cref="TryAccept"/>) updates it.
    /// </summary>
    public sealed class DisplayedTuneHistory
    {
        public const int MaxGenerationAttempts = 10;

        private readonly object _gate = new();
        private string? _previousSignature;

        public int Count
        {
            get { lock (_gate) return string.IsNullOrEmpty(_previousSignature) ? 0 : 1; }
        }

        public string? PreviousSignature
        {
            get { lock (_gate) return _previousSignature; }
        }

        public bool Contains(string? signature)
        {
            if (string.IsNullOrEmpty(signature))
                return false;
            lock (_gate)
                return string.Equals(_previousSignature, signature, StringComparison.Ordinal);
        }

        /// <summary>
        /// True when <paramref name="signature"/> is the already-displayed tune.
        /// Empty signatures are not treated as repeats. Does not change previous.
        /// </summary>
        public bool IsRepeatOfPrevious(string? signature)
        {
            if (string.IsNullOrEmpty(signature))
                return false;
            lock (_gate)
                return string.Equals(_previousSignature, signature, StringComparison.Ordinal);
        }

        /// <summary>
        /// Records the tune that was actually shown. Call once after selection, not
        /// for rejected candidates.
        /// </summary>
        public void CommitDisplayed(string? signature)
        {
            if (string.IsNullOrEmpty(signature))
                return;
            lock (_gate)
                _previousSignature = signature;
        }

        /// <summary>
        /// Records <paramref name="signature"/> if it is not the previous displayed tune.
        /// Returns false when this would be a consecutive repeat (caller should retry).
        /// Empty signatures are ignored and not recorded.
        /// </summary>
        public bool TryAccept(string? signature)
        {
            if (string.IsNullOrEmpty(signature))
                return true;
            if (IsRepeatOfPrevious(signature))
                return false;
            CommitDisplayed(signature);
            return true;
        }

        /// <summary>
        /// True when the current play mode is generated music that should not
        /// repeat the immediately previous generated tune. User-picked saved tunes,
        /// named scales, and named arpeggios are exempt. Assortment by Level scale
        /// walks are not.
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
            if (session.HasTemporaryNoteEmphasis)
                return true;
            if (session.IsRandomMode)
                return true;
            if (PracticeCompositionSelector.IsUserExplicitPlayMode(
                    session.Tune ?? string.Empty,
                    session.ScaleSelectionMode,
                    session.IsRandomMode,
                    selectedTunePreference))
                return false;

            return true;
        }
    }
}
