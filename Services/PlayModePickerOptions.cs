using Microsoft.Maui.Storage;
using musicmate.Models;

namespace musicmate.Services
{
    /// <summary>Which What To Play picker row reflects the user's saved choice.</summary>
    public enum PlayModePickerCategory
    {
        Tunes,
        Scales,
        Arpeggios,
        Other
    }

    /// <summary>
    /// What To Play picker contents and shared selection logic for Scales vs Other.
    /// </summary>
    public static class PlayModePickerOptions
    {
        public const string HalfThroughSixteenthNotes = "Half through Sixteenth Notes";
        internal const string LegacyFixedTune = "Fixed Tune";
        public const string RandomMelodic = NoteSessionService.ScaleSelectionRandom;
        public const string Tuner = "Tuner";

        /// <summary>True when the session is in Tuner mode (hamburger or Other → Tuner).</summary>
        public static bool IsTunerMode(NoteSessionService? session)
            => session?.Tune == Tuner;

        public static bool IsTunerMode(string? tune)
            => string.Equals(tune, Tuner, StringComparison.Ordinal);

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

            // Explicit Scales / Tunes / Arpeggios picks are never shown on the Other row.
            if (NoteSessionService.IsNamedScaleOption(selectedTunePreference))
                return false;

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
            if (NoteSessionService.IsNamedScaleOption(selectedTunePreference))
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
        /// What To Play should display from the user's saved picker choice, not from
        /// composition-assigned <see cref="NoteSessionService.Tune"/> / arpeggio / tune title.
        /// Tuner is an exception: main menu and Other picker both set <see cref="NoteSessionService.Tune"/>,
        /// so the Other row must follow that session state.
        /// </summary>
        public static (PlayModePickerCategory Category, string Selection) ResolveDisplayedPicker(
            NoteSessionService session,
            bool layoutTestTuneEnabled,
            string? selectedTunePreference = null)
        {
            if (session.Tune == Tuner)
                return (PlayModePickerCategory.Other, Tuner);

            return ResolveDisplayedPicker(
                layoutTestTuneEnabled,
                NormalizeRhythmNoteTunePreference(
                    selectedTunePreference ?? Preferences.Default.Get<string?>("SelectedTune", null)),
                session.ScaleSelectionMode,
                session.SelectedScale);
        }

        /// <summary>Testable/display resolver from persisted preference + scale mode.</summary>
        public static (PlayModePickerCategory Category, string Selection) ResolveDisplayedPicker(
            bool layoutTestTuneEnabled,
            string? selectedTunePreference,
            ScaleSelectionMode scaleSelectionMode,
            string? selectedScale)
        {
            selectedTunePreference = NormalizeRhythmNoteTunePreference(selectedTunePreference);

            if (layoutTestTuneEnabled || IsRhythmNoteTuneSelection(selectedTunePreference))
                return (PlayModePickerCategory.Tunes, HalfThroughSixteenthNotes);

            if (string.Equals(selectedTunePreference, Tuner, StringComparison.Ordinal))
                return (PlayModePickerCategory.Other, Tuner);

            if (string.Equals(selectedTunePreference, RandomMelodic, StringComparison.Ordinal))
                return (PlayModePickerCategory.Other, RandomMelodic);

            if (string.Equals(selectedTunePreference, "Selected Scale", StringComparison.Ordinal)
                && scaleSelectionMode == ScaleSelectionMode.ByLevel)
                return (PlayModePickerCategory.Other, NoteSessionService.ScaleSelectionByLevel);

            // Explicit Scales-picker choice (SelectedTune = "Major", etc.) wins over By Level mode.
            if (NoteSessionService.IsNamedScaleOption(selectedTunePreference))
                return (PlayModePickerCategory.Scales, selectedTunePreference!);

            if (IsUserSelectedPracticeTuneTitle(selectedTunePreference))
                return (PlayModePickerCategory.Tunes, selectedTunePreference!);

            if (IsUserSelectedArpeggioTitle(selectedTunePreference))
                return (PlayModePickerCategory.Arpeggios, selectedTunePreference!);

            if (scaleSelectionMode == ScaleSelectionMode.Named && !string.IsNullOrWhiteSpace(selectedScale))
                return (PlayModePickerCategory.Scales, selectedScale);

            return (PlayModePickerCategory.Other, NoteSessionService.ScaleSelectionByLevel);
        }

        public static string BuildSessionWhatLabel(
            NoteSessionService session,
            bool layoutTestTuneEnabled,
            string? selectedTunePreference = null)
        {
            var (category, selection) = ResolveDisplayedPicker(
                session, layoutTestTuneEnabled, selectedTunePreference);
            return AbbreviateDisplayedSelection(category, selection);
        }

        /// <summary>Abbreviated label for session statistics (What column).</summary>
        public static string AbbreviateDisplayedSelection(
            PlayModePickerCategory category,
            string selection)
        {
            if (string.IsNullOrWhiteSpace(selection))
                return string.Empty;

            if (string.Equals(selection, NoteSessionService.ScaleSelectionByLevel, StringComparison.Ordinal))
                return "ByLvl";
            if (string.Equals(selection, RandomMelodic, StringComparison.Ordinal))
                return "Rnd";
            if (string.Equals(selection, Tuner, StringComparison.Ordinal))
                return "Tuner";
            if (string.Equals(selection, HalfThroughSixteenthNotes, StringComparison.Ordinal))
                return "Rhythm";

            return category switch
            {
                PlayModePickerCategory.Scales => AbbreviateScaleName(selection),
                PlayModePickerCategory.Tunes => AbbreviateTitle(selection, 14),
                PlayModePickerCategory.Arpeggios => AbbreviateArpeggioLabel(selection),
                _ => AbbreviateTitle(selection, 12)
            };
        }

        private static string AbbreviateScaleName(string scale)
            => scale switch
            {
                "Natural Minor" => "Nat Min",
                "Harmonic Minor" => "Harm Min",
                "Melodic Minor" => "Mel Min",
                "Jazz Melodic Minor" => "Jazz Mel",
                "Major Pentatonic" => "Maj Pent",
                "Minor Pentatonic" => "Min Pent",
                "Major Blues" => "Maj Blues",
                "Minor Blues" => "Min Blues",
                "Phrygian Dominant" => "Phryg Dom",
                "Lydian Dominant" => "Lyd Dom",
                "Double Harmonic" => "Dbl Harm",
                "Neapolitan Minor" => "Nap Min",
                "Whole Tone" => "W Tone",
                _ => AbbreviateTitle(scale, 12)
            };

        private static string AbbreviateArpeggioLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return string.Empty;

            var trimmed = label.Trim();
            var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 3
                && words[1].Equals("major", StringComparison.OrdinalIgnoreCase)
                && words[2].StartsWith("tri", StringComparison.OrdinalIgnoreCase))
                return $"{words[0]} maj tri";
            if (words.Length >= 3
                && words[1].Equals("minor", StringComparison.OrdinalIgnoreCase)
                && words[2].StartsWith("tri", StringComparison.OrdinalIgnoreCase))
                return $"{words[0]} min tri";

            return AbbreviateTitle(trimmed, 14);
        }

        private static string AbbreviateTitle(string text, int maxLen)
        {
            if (text.Length <= maxLen)
                return text;
            return maxLen <= 3 ? text[..maxLen] : text[..(maxLen - 1)] + "…";
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
            session.ClearTemporaryNoteEmphasis("what-to-play-changed");

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
                // Shared transition for hamburger Tuner and WhatToPlay → Other → Tuner.
                // Overrides prior By Level / random play-mode selection; composition and
                // note generation must not run while Tune == Tuner.
                session.IsRandomMode = false;
                session.RepeatSameTune = false;
                bool enteringTuner = session.Tune != Tuner;
                session.Tune = Tuner;
                if (enteringTuner)
                {
                    session.ClearTunerDetection();
                    session.NotesToDraw.Clear();
                    session.FeedbackViewModels.Clear();
                }
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
