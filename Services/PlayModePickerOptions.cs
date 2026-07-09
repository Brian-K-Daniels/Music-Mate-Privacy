using Microsoft.Maui.Storage;
using musicmate.Models;

namespace musicmate.Services
{
    /// <summary>
    /// What To Play picker contents and shared selection logic for Scales vs Other.
    /// </summary>
    public static class PlayModePickerOptions
    {
        public const string HalfThroughSixteenthNotes = "Half through Sixteenth Notes";
        internal const string LegacyFixedTune = "Fixed Tune";
        public const string RandomMelodic = NoteSessionService.ScaleSelectionRandom;
        public const string Tuner = "Tuner";

        /// <summary>Other picker: By Level, Random, Tuner.</summary>
        public static readonly string[] OtherOptions =
        [
            NoteSessionService.ScaleSelectionByLevel,
            RandomMelodic,
            Tuner
        ];

        /// <summary>Tunes picker: rhythm-note exercise first, then library tunes.</summary>
        public static string[] BuildTunePickerOptions()
            => new[] { HalfThroughSixteenthNotes }
                .Concat(TuneLibrary.All.Select(t => t.Title))
                .ToArray();

        /// <summary>Scales picker: named scales only.</summary>
        public static string[] NamedScaleOptions => NoteSessionService.ScalePickerOptions;

        public static bool IsOtherOption(string? option)
            => !string.IsNullOrWhiteSpace(option) && OtherOptions.Contains(option, StringComparer.Ordinal);

        public static bool UsesOtherPicker(NoteSessionService session, bool layoutTestTuneEnabled)
            => UsesOtherPicker(
                layoutTestTuneEnabled,
                session.Tune ?? string.Empty,
                session.IsRandomMode,
                session.ScaleSelectionMode,
                Preferences.Default.Get<string?>("SelectedTune", null));

        public static bool UsesOtherPicker(
            bool layoutTestTuneEnabled,
            string tune,
            bool isRandomMode,
            ScaleSelectionMode scaleSelectionMode,
            string? selectedTunePreference = null)
        {
            if (layoutTestTuneEnabled || IsRhythmNoteTuneSelection(selectedTunePreference))
                return false;

            if (tune == Tuner)
                return true;

            if (tune == "Practice Tune" && IsUserSelectedPracticeTuneTitle(selectedTunePreference))
                return false;

            if (tune == "Arpeggio" && IsUserSelectedArpeggioTitle(selectedTunePreference))
                return false;

            if (scaleSelectionMode == ScaleSelectionMode.ByLevel)
                return true;

            return tune == "Selected Scale" && isRandomMode;
        }

        public static bool IsRhythmNoteTuneSelection(string? selectedTunePreference)
            => string.Equals(selectedTunePreference, HalfThroughSixteenthNotes, StringComparison.Ordinal)
               || string.Equals(selectedTunePreference, LegacyFixedTune, StringComparison.Ordinal);

        /// <summary>True when the user explicitly chose a tune from the Tunes picker.</summary>
        public static bool IsUserSelectedPracticeTuneTitle(string? selectedTunePreference)
            => !string.IsNullOrEmpty(selectedTunePreference)
               && TuneLibrary.All.Any(t => t.Title == selectedTunePreference);

        /// <summary>True when the user explicitly chose an arpeggio from the Arpeggios picker.</summary>
        public static bool IsUserSelectedArpeggioTitle(string? selectedTunePreference)
        {
            if (string.IsNullOrEmpty(selectedTunePreference))
                return false;
            if (IsUserSelectedPracticeTuneTitle(selectedTunePreference))
                return false;
            if (IsRhythmNoteTuneSelection(selectedTunePreference))
                return false;
            return selectedTunePreference is not ("Random" or "Tuner" or "Selected Scale"
                or NoteSessionService.ScaleSelectionByLevel);
        }

        public static string NormalizeRhythmNoteTunePreference(string? value)
            => string.Equals(value, LegacyFixedTune, StringComparison.Ordinal)
                ? HalfThroughSixteenthNotes
                : value ?? string.Empty;

        public static void MigrateLegacySelectedTunePreference()
        {
            var saved = Preferences.Default.Get<string?>("SelectedTune", null);
            if (string.Equals(saved, LegacyFixedTune, StringComparison.Ordinal))
                Preferences.Default.Set("SelectedTune", HalfThroughSixteenthNotes);
        }

        public static string ResolveOtherSelection(NoteSessionService session, bool layoutTestTuneEnabled)
            => ResolveOtherSelection(
                layoutTestTuneEnabled,
                session.Tune ?? string.Empty,
                session.IsRandomMode,
                session.ScaleSelectionMode,
                Preferences.Default.Get<string?>("SelectedTune", null));

        public static string ResolveOtherSelection(
            bool layoutTestTuneEnabled,
            string tune,
            bool isRandomMode,
            ScaleSelectionMode scaleSelectionMode = ScaleSelectionMode.ByLevel,
            string? selectedTunePreference = null)
        {
            if (tune == Tuner)
                return Tuner;

            // Explicit Other → Random persists SelectedTune as "Random".
            // By Level composition may set IsRandomMode without that preference —
            // keep the picker on By Level in that case.
            if (isRandomMode
                && string.Equals(selectedTunePreference, RandomMelodic, StringComparison.Ordinal))
                return RandomMelodic;

            if (scaleSelectionMode == ScaleSelectionMode.ByLevel)
                return NoteSessionService.ScaleSelectionByLevel;

            if (isRandomMode)
                return RandomMelodic;

            return NoteSessionService.ScaleSelectionByLevel;
        }

        /// <summary>
        /// Applies an Other-picker choice to session state (same behavior as legacy Random/Tuner + By Level).
        /// </summary>
        public static void ApplyOtherSelection(
            NoteSessionService session,
            string selected,
            Action<string>? persistSelectedTune = null)
        {
            persistSelectedTune ??= value => Preferences.Default.Set("SelectedTune", value);

            if (selected == NoteSessionService.ScaleSelectionByLevel)
            {
                session.IsRandomMode = false;
                session.RepeatSameTune = false;
                session.Tune = "Selected Scale";
                session.TryApplyScalePickerSelection(NoteSessionService.ScaleSelectionByLevel, out _);
                persistSelectedTune("Selected Scale");
                return;
            }

            if (selected == Tuner)
            {
                session.Tune = Tuner;
                session.IsRandomMode = false;
                persistSelectedTune(Tuner);
                return;
            }

            if (selected == RandomMelodic)
            {
                session.Tune = "Selected Scale";
                session.IsRandomMode = true;
                persistSelectedTune(RandomMelodic);
            }
        }

        /// <summary>
        /// Applies Tunes → Half through Sixteenth Notes (legacy Fixed Tune behavior).
        /// </summary>
        public static void ApplyRhythmNoteTuneSelection(
            NoteSessionService session,
            Action<string>? persistSelectedTune = null)
        {
            persistSelectedTune ??= value => Preferences.Default.Set("SelectedTune", value);

            session.IsRandomMode = false;
            if (session.Tune == Tuner)
                session.Tune = "Selected Scale";
            persistSelectedTune(HalfThroughSixteenthNotes);
        }

        /// <summary>
        /// Migrates legacy Scales-picker Random (level scale pool) to By Level in the Other picker.
        /// </summary>
        public static void MigrateLegacySessionSelection(NoteSessionService session)
        {
            MigrateLegacySelectedTunePreference();

            if (session.ScaleSelectionMode != ScaleSelectionMode.Random || session.IsRandomMode)
                return;

            session.TryApplyScalePickerSelection(NoteSessionService.ScaleSelectionByLevel, out _);
        }
    }
}
