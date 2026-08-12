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

        /// <summary>Other picker: Assortment by Level, Random, Tuner.</summary>
        public static readonly string[] OtherOptions =
        [
            NoteSessionService.ScaleSelectionByLevel,
            RandomMelodic,
            Tuner
        ];

        /// <summary>Tunes picker: rhythm-note exercise first, then library tunes, then saved tunes.</summary>
        public static string[] BuildTunePickerOptions(SavedTuneStore? savedTunes = null)
        {
            savedTunes ??= ServiceHelper.GetService<SavedTuneStore>();
            IEnumerable<string> savedTitles = savedTunes?.Titles ?? Array.Empty<string>();
            return new[] { HalfThroughSixteenthNotes }
                .Concat(TuneLibrary.All.Select(t => t.Title))
                .Concat(savedTitles)
                .ToArray();
        }

        /// <summary>Resolves a Tunes-picker title to a <see cref="PracticeTune"/> (built-in or saved).</summary>
        public static PracticeTune? TryResolvePracticeTune(string? title, SavedTuneStore? savedTunes = null)
        {
            if (string.IsNullOrWhiteSpace(title))
                return null;

            var builtIn = TuneLibrary.All.FirstOrDefault(t => t.Title == title);
            if (builtIn != null)
                return builtIn;

            savedTunes ??= ServiceHelper.GetService<SavedTuneStore>();
            return savedTunes?.GetByTitle(title);
        }

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

        /// <summary>True when the user explicitly chose a built-in or saved tune from the Tunes picker.</summary>
        public static bool IsUserSelectedPracticeTuneTitle(string? selectedTunePreference)
        {
            if (string.IsNullOrEmpty(selectedTunePreference))
                return false;
            if (TuneLibrary.All.Any(t => t.Title == selectedTunePreference))
                return true;
            if (IsRhythmNoteTuneSelection(selectedTunePreference))
                return false;
            var store = ServiceHelper.GetService<SavedTuneStore>();
            return store?.GetByTitle(selectedTunePreference) != null;
        }

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
            => MigrateLegacySelectedTunePreference(
                () => Preferences.Default.Get<string?>("SelectedTune", null),
                value => Preferences.Default.Set("SelectedTune", value));

        public static void MigrateLegacySelectedTunePreference(
            Func<string?> getSelectedTune,
            Action<string> setSelectedTune)
        {
            var saved = getSelectedTune();
            if (string.Equals(saved, LegacyFixedTune, StringComparison.Ordinal))
                setSelectedTune(HalfThroughSixteenthNotes);
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
            // Assortment by Level composition may set IsRandomMode without that preference —
            // keep the picker on Assortment by Level in that case.
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

            if (string.Equals(selectedTunePreference, NoteSessionService.ScaleSelectionByLevel, StringComparison.Ordinal)
                || (string.Equals(selectedTunePreference, "Selected Scale", StringComparison.Ordinal)
                    && scaleSelectionMode == ScaleSelectionMode.ByLevel))
                return (PlayModePickerCategory.Other, NoteSessionService.ScaleSelectionByLevel);

            // Explicit Scales-picker choice (SelectedTune = "Major", etc.) wins over Assortment by Level mode.
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

        /// <summary>
        /// Stable key for the current What To Play pick (category + selection).
        /// Used so a different item in the same picker (e.g. Major → Dorian) counts as a new choice.
        /// </summary>
        public static string ResolvePlaySelectionKey(PlayModePickerCategory category, string? selection)
            => $"{category}:{selection ?? string.Empty}";

        public static string ResolvePlaySelectionKey(NoteSessionService session, bool layoutTestTuneEnabled)
        {
            var (category, selection) = ResolveDisplayedPicker(session, layoutTestTuneEnabled);
            return ResolvePlaySelectionKey(category, selection);
        }

        /// <summary>True when the user picked a different What To Play item than before.</summary>
        public static bool IsNewPlaySelection(string? previousSelectionKey, string? nextSelectionKey)
            => !string.IsNullOrEmpty(nextSelectionKey)
               && !string.Equals(previousSelectionKey, nextSelectionKey, StringComparison.Ordinal);

        public static string BuildSessionWhatLabel(
            NoteSessionService session,
            bool layoutTestTuneEnabled,
            string? selectedTunePreference = null)
        {
            var (category, selection) = ResolveDisplayedPicker(
                session, layoutTestTuneEnabled, selectedTunePreference);
            return AbbreviateDisplayedSelection(category, selection);
        }

        /// <summary>
        /// Music status / practice label for the exercise currently on the staff.
        /// Assortment by Level keeps the What To Play picker on that mode, but composition
        /// may assign a practice tune or arpeggio — status must name that exercise, not the
        /// leftover level-default scale (e.g. Blues at L62).
        /// </summary>
        public static string ResolveExerciseStatusLabel(
            bool layoutTestTuneEnabled,
            string tune,
            ScaleSelectionMode scaleSelectionMode,
            bool isRandomMode,
            string? practiceTuneTitle,
            string? arpeggioDisplay,
            string key,
            string effectiveScale,
            string? selectedScale,
            string? selectedTunePreference)
        {
            if (layoutTestTuneEnabled)
                return HalfThroughSixteenthNotes;

            if (string.Equals(tune, Tuner, StringComparison.Ordinal))
                return Tuner;

            bool assortmentAssigned =
                scaleSelectionMode == ScaleSelectionMode.ByLevel
                && !IsUserSelectedPracticeTuneTitle(selectedTunePreference)
                && !IsUserSelectedArpeggioTitle(selectedTunePreference);

            if (string.Equals(tune, "Practice Tune", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(practiceTuneTitle))
            {
                return assortmentAssigned
                    ? $"{practiceTuneTitle} (Assortment by Level)"
                    : practiceTuneTitle;
            }

            if (string.Equals(tune, "Arpeggio", StringComparison.Ordinal))
            {
                string arp = string.IsNullOrWhiteSpace(arpeggioDisplay) ? "Arpeggio" : arpeggioDisplay;
                return assortmentAssigned
                    ? $"{arp} (Assortment by Level)"
                    : arp;
            }

            var (category, selection) = ResolveDisplayedPicker(
                layoutTestTuneEnabled,
                selectedTunePreference,
                scaleSelectionMode,
                selectedScale);

            if (category == PlayModePickerCategory.Other
                && selection == NoteSessionService.ScaleSelectionByLevel)
                return $"{key} {effectiveScale} (Assortment by Level)";

            if (category == PlayModePickerCategory.Other
                && selection == RandomMelodic)
                return FormatEffectiveScaleDisplay(
                    scaleSelectionMode, isRandomMode, key, effectiveScale, selectedScale);

            if (category == PlayModePickerCategory.Scales
                && !string.IsNullOrWhiteSpace(selection))
                return selection;

            if (category == PlayModePickerCategory.Tunes
                && !string.IsNullOrWhiteSpace(selection))
                return selection;

            if (category == PlayModePickerCategory.Arpeggios
                && !string.IsNullOrWhiteSpace(selection))
                return selection;

            if (category == PlayModePickerCategory.Other
                && !string.IsNullOrWhiteSpace(selection))
                return selection;

            if (isRandomMode)
                return FormatEffectiveScaleDisplay(
                    scaleSelectionMode, isRandomMode, key, effectiveScale, selectedScale);

            if (scaleSelectionMode == ScaleSelectionMode.ByLevel)
                return $"{key} {effectiveScale} (Assortment by Level)";

            if (scaleSelectionMode == ScaleSelectionMode.Random)
                return FormatEffectiveScaleDisplay(
                    scaleSelectionMode, isRandomMode, key, effectiveScale, selectedScale);

            return string.IsNullOrWhiteSpace(selectedScale) ? "Selected Scale" : selectedScale;
        }

        private static string FormatEffectiveScaleDisplay(
            ScaleSelectionMode scaleSelectionMode,
            bool isRandomMode,
            string key,
            string effectiveScale,
            string? selectedScale)
            => scaleSelectionMode switch
            {
                ScaleSelectionMode.ByLevel => $"{key} {effectiveScale} (Assortment by Level)",
                ScaleSelectionMode.Random => $"Random — {key} {effectiveScale}",
                _ when isRandomMode => $"Random — {key} {effectiveScale}",
                _ => $"{key} {selectedScale}"
            };

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
        /// Applies an Other-picker choice to session state (same behavior as legacy Random/Tuner + Assortment by Level).
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
                persistSelectedTune(NoteSessionService.ScaleSelectionByLevel);
                return;
            }

            if (selected == Tuner)
            {
                // Shared transition for hamburger Tuner and WhatToPlay → Other → Tuner.
                // Overrides prior Assortment by Level / random play-mode selection; composition and
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
        /// Restores Tune / scale / random / practice-tune state from the persisted
        /// <c>SelectedTune</c> preference (and related session prefs already loaded).
        /// Call on app start and when opening What to Play / Music.
        /// </summary>
        public static void ApplyPersistedSelection(
            NoteSessionService session,
            Func<string?>? getSelectedTune = null,
            Action<string>? setSelectedTune = null)
        {
            ArgumentNullException.ThrowIfNull(session);
            getSelectedTune ??= () => Preferences.Default.Get<string?>("SelectedTune", null);
            setSelectedTune ??= value => Preferences.Default.Set("SelectedTune", value);

            MigrateLegacySelectedTunePreference(getSelectedTune, setSelectedTune);
            MigrateLegacySessionSelection(session, migrateSelectedTunePreference: false);

            var saved = NormalizeRhythmNoteTunePreference(getSelectedTune());

            // Assortment by Level used to be saved as "Selected Scale"; upgrade when mode says ByLevel.
            if (string.IsNullOrEmpty(saved)
                || string.Equals(saved, "Selected Scale", StringComparison.Ordinal))
            {
                if (session.ScaleSelectionMode == ScaleSelectionMode.ByLevel && !session.IsRandomMode
                    && session.Tune != Tuner && session.Tune != "Practice Tune" && session.Tune != "Arpeggio")
                {
                    ApplyOtherSelection(session, NoteSessionService.ScaleSelectionByLevel, setSelectedTune);
                    return;
                }
            }

            if (string.IsNullOrEmpty(saved))
                return;

            if (string.Equals(saved, Tuner, StringComparison.Ordinal))
            {
                ApplyOtherSelection(session, Tuner, setSelectedTune);
                return;
            }

            if (string.Equals(saved, RandomMelodic, StringComparison.Ordinal)
                || string.Equals(saved, NoteSessionService.ScaleSelectionRandom, StringComparison.Ordinal))
            {
                ApplyOtherSelection(session, RandomMelodic, setSelectedTune);
                return;
            }

            if (string.Equals(saved, NoteSessionService.ScaleSelectionByLevel, StringComparison.Ordinal))
            {
                ApplyOtherSelection(session, NoteSessionService.ScaleSelectionByLevel, setSelectedTune);
                return;
            }

            if (IsRhythmNoteTuneSelection(saved))
            {
                ApplyRhythmNoteTuneSelection(session, setSelectedTune);
                return;
            }

            if (IsUserSelectedPracticeTuneTitle(saved))
            {
                var tune = TryResolvePracticeTune(saved);
                if (tune != null)
                {
                    session.IsRandomMode = false;
                    session.SelectPracticeTune(tune);
                    setSelectedTune(saved);
                }
                return;
            }

            if (NoteSessionService.IsNamedScaleOption(saved))
            {
                session.IsRandomMode = false;
                session.RepeatSameTune = false;
                session.Tune = "Selected Scale";
                session.TryApplyScalePickerSelection(saved, out _);
                setSelectedTune(saved);
                return;
            }

            // Arpeggio labels are free-form; MusicPage wires concrete pattern selection.
            if (IsUserSelectedArpeggioTitle(saved))
            {
                session.IsRandomMode = false;
                setSelectedTune(saved);
            }
        }

        /// <summary>
        /// Migrates legacy Scales-picker Random (level scale pool) to Assortment by Level in the Other picker.
        /// </summary>
        public static void MigrateLegacySessionSelection(
            NoteSessionService session,
            bool migrateSelectedTunePreference = true)
        {
            if (migrateSelectedTunePreference)
                MigrateLegacySelectedTunePreference();

            if (session.ScaleSelectionMode != ScaleSelectionMode.Random || session.IsRandomMode)
                return;

            session.TryApplyScalePickerSelection(NoteSessionService.ScaleSelectionByLevel, out _);
        }
    }
}
