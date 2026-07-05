using System.Diagnostics;
using Microsoft.Maui.Storage;
using musicmate.Models;

namespace musicmate.Services
{
    /// <summary>
    /// Picks the next exercise type from practice-composition sliders for
    /// Child / By Level / mixed practice. Explicit user modes are left unchanged.
    /// </summary>
    public static class PracticeCompositionSelector
    {
        public enum ExerciseKind
        {
            Tune,
            Random,
            Scale,
            Arpeggio
        }

        /// <summary>
        /// User chose a fixed play mode from What To Play (not composition-assigned).
        /// </summary>
        public static bool IsUserExplicitPlayMode(NoteSessionService session)
            => IsUserExplicitPlayMode(
                session.Tune ?? string.Empty,
                session.ScaleSelectionMode,
                session.IsRandomMode,
                Preferences.Default.Get<string?>("SelectedTune", null));

        public static bool IsUserExplicitPlayMode(
            string tune,
            ScaleSelectionMode scaleSelectionMode,
            bool isRandomMode,
            string? selectedTunePreference)
        {
            if (tune == "Tuner")
                return true;

            if (scaleSelectionMode == ScaleSelectionMode.Named)
                return true;

            if (scaleSelectionMode == ScaleSelectionMode.Random)
                return true;

            if (isRandomMode
                && string.Equals(selectedTunePreference, "Random", StringComparison.Ordinal))
                return true;

            if (string.Equals(selectedTunePreference, PlayModePickerOptions.HalfThroughSixteenthNotes, StringComparison.Ordinal)
                || string.Equals(selectedTunePreference, PlayModePickerOptions.LegacyFixedTune, StringComparison.Ordinal))
                return true;

            if (tune == "Practice Tune" && IsUserSelectedPracticeTune(selectedTunePreference))
                return true;

            if (tune == "Arpeggio" && IsUserSelectedArpeggio(selectedTunePreference))
                return true;

            return false;
        }

        /// <summary>Alias kept for callers that treated explicit mode as composition-blocking.</summary>
        public static bool IsExplicitMode(NoteSessionService session)
            => IsUserExplicitPlayMode(session);

        /// <summary>
        /// Child practice, By Level, or adult mixed practice — roll composition sliders.
        /// </summary>
        public static bool UsesCompositionSliders(NoteSessionService session)
        {
            if (!IsCompositionEligibleSession(session))
                return false;

            if (IsUserExplicitPlayMode(session))
                return false;

            return true;
        }

        private static bool IsCompositionEligibleSession(NoteSessionService session)
            => session.ChildLevel > 0
               || session.ScaleSelectionMode == ScaleSelectionMode.ByLevel;

        /// <summary>
        /// Weighted pick among tunes / random / scales / arpeggios (Pc* sum to 100).
        /// </summary>
        public static ExerciseKind PickExerciseKind(NoteSessionService session, Random rng)
        {
            int level = ResolveLevel(session);
            var tuneContext = CompositionTuneEligibility.FromSession(session, level);
            bool hasEligibleTunes = CompositionTuneEligibility.GetEligibleTuneTitles(tuneContext).Count > 0;
            bool hasEligibleArpeggios = ArpeggioCatalog.GetAvailablePatterns(level).Count > 0;

            bool applyByLevelTuneRestriction =
                session.ScaleSelectionMode == ScaleSelectionMode.ByLevel;

            var (tuneW, randomW, scaleW, arpW) = CompositionLevelWeights.ComputeEffectiveWeights(
                session.PcTunes,
                session.PcRandom,
                session.PcScales,
                session.PcArpeggios,
                level,
                applyByLevelTuneRestriction,
                hasEligibleTunes,
                hasEligibleArpeggios);

            return PickExerciseKindFromWeights(
                tuneW,
                randomW,
                scaleW,
                arpW,
                hasEligibleTunes,
                rng);
        }

        /// <summary>Weighted category pick for tests and composition logic.</summary>
        public static ExerciseKind PickExerciseKindFromWeights(
            int pcTunes,
            int pcRandom,
            int pcScales,
            int pcArpeggios,
            bool hasEligibleTunes,
            Random rng)
        {
            int tuneWeight = hasEligibleTunes ? pcTunes : 0;

            var buckets = new (ExerciseKind Kind, int Weight)[]
            {
                (ExerciseKind.Tune, tuneWeight),
                (ExerciseKind.Random, pcRandom),
                (ExerciseKind.Scale, pcScales),
                (ExerciseKind.Arpeggio, pcArpeggios),
            };

            int total = buckets.Sum(b => b.Weight);
            if (total <= 0)
            {
                buckets =
                [
                    (ExerciseKind.Tune, NoteSessionService.DefaultPcTunes),
                    (ExerciseKind.Random, NoteSessionService.DefaultPcRandom),
                    (ExerciseKind.Scale, NoteSessionService.DefaultPcScales),
                    (ExerciseKind.Arpeggio, NoteSessionService.DefaultPcArpeggios),
                ];
                if (!hasEligibleTunes)
                    buckets[0] = (ExerciseKind.Tune, 0);
                total = buckets.Sum(b => b.Weight);
                if (total <= 0)
                    return ExerciseKind.Random;
            }

            int roll = rng.Next(total);
            foreach (var (kind, weight) in buckets)
            {
                if (weight <= 0)
                    continue;
                if (roll < weight)
                    return kind;
                roll -= weight;
            }

            return ExerciseKind.Random;
        }

        /// <summary>
        /// Configures session state for the chosen composition exercise.
        /// </summary>
        public static void ApplyExerciseKind(NoteSessionService session, ExerciseKind kind, Random rng)
        {
            switch (kind)
            {
                case ExerciseKind.Random:
                    session.IsRandomMode = true;
                    session.Tune = "Selected Scale";
                    Preferences.Default.Set("SelectedTune", "Selected Scale");
                    break;

                case ExerciseKind.Scale:
                    session.IsRandomMode = false;
                    session.Tune = "Selected Scale";
                    Preferences.Default.Set("SelectedTune", "Selected Scale");
                    break;

                case ExerciseKind.Tune:
                    ApplyRandomTune(session, rng);
                    break;

                case ExerciseKind.Arpeggio:
                    if (!TryApplyRandomArpeggio(session, rng))
                        ApplyExerciseKind(session, ExerciseKind.Random, rng);
                    break;
            }

#if DEBUG
            Debug.WriteLine(
                $"[Composition] Applied {kind}: Tune={session.Tune} Random={session.IsRandomMode} " +
                $"ScaleMode={session.ScaleSelectionMode} Key={session.Key}");
#endif
        }

        /// <summary>
        /// When composition applies, pick and configure the next exercise before generation.
        /// </summary>
        public static void ApplyNextExerciseIfNeeded(NoteSessionService session, int seed)
        {
            if (!UsesCompositionSliders(session))
                return;

            var rng = new Random(seed);
            var kind = PickExerciseKind(session, rng);
#if DEBUG
            Debug.WriteLine(
                $"[Composition] Picked {kind} (Pc: tunes={session.PcTunes}% random={session.PcRandom}% " +
                $"scales={session.PcScales}% arpeggios={session.PcArpeggios}%)");
#endif
            ApplyExerciseKind(session, kind, rng);
        }

        private static bool IsUserSelectedPracticeTune(string? selectedTunePreference)
            => PlayModePickerOptions.IsUserSelectedPracticeTuneTitle(selectedTunePreference);

        private static bool IsUserSelectedArpeggio(string? selectedTunePreference)
            => PlayModePickerOptions.IsUserSelectedArpeggioTitle(selectedTunePreference);

        private static int ResolveLevel(NoteSessionService session)
        {
            if (session.ChildLevel > 0)
                return session.ChildLevel;
            return Math.Clamp(Preferences.Default.Get("ChildPractice.Level", 1), 1, 100);
        }

        private static void ApplyRandomTune(NoteSessionService session, Random rng)
        {
            int level = ResolveLevel(session);
            var context = CompositionTuneEligibility.FromSession(session, level);
            string? title = CompositionTuneShuffleBag.DrawNextTitle(context, rng);
            if (string.IsNullOrEmpty(title))
            {
                session.IsRandomMode = false;
                session.Tune = "Selected Scale";
                return;
            }

            var tune = TuneLibrary.All.FirstOrDefault(t => t.Title == title);
            if (tune == null)
            {
                session.IsRandomMode = false;
                session.Tune = "Selected Scale";
                return;
            }

            session.IsRandomMode = false;
            session.SelectPracticeTune(tune);
        }

        private static bool TryApplyRandomArpeggio(NoteSessionService session, Random rng)
        {
            int level = ResolveLevel(session);
            var patterns = ArpeggioCatalog.GetAvailablePatterns(level);
            if (patterns.Count == 0)
                return false;

            var pattern = patterns[rng.Next(patterns.Count)];
            string rootKey = PickArpeggioRootKey(session, level, rng);
            string rootNote = ChooseArpeggioRootInRange(session, rootKey);
            string displayName = $"{TrimOctave(rootNote)} {pattern.DisplayName.ToLowerInvariant()}";
            session.IsRandomMode = false;
            session.SelectArpeggio(pattern, rootNote, displayName);
            session.Key = GetArpeggioKeySignature(pattern, rootNote);
            return true;
        }

        private static string PickArpeggioRootKey(NoteSessionService session, int level, Random rng)
        {
            var keys = ChildLevelProgression.GetAllowedKeys(level).ToList();
            if (keys.Count == 0)
                keys.Add(session.Key);
            return keys[rng.Next(keys.Count)];
        }

        private static string ChooseArpeggioRootInRange(NoteSessionService session, string rootKey)
        {
            const int baseOctave = 4;
            int rootMidi = NoteSessionService.NoteNameToMidi($"{rootKey}{baseOctave}");
            int minMidi = NoteSessionService.NoteNameToMidi(session.LowestNote);
            int maxMidi = NoteSessionService.NoteNameToMidi(session.HighestNote);
            if (minMidi < 0 || maxMidi < minMidi)
                return $"{rootKey}{baseOctave}";

            int candidate = rootMidi;
            while (candidate < minMidi)
                candidate += 12;
            while (candidate > maxMidi)
                candidate -= 12;

            int octave = baseOctave + ((candidate - rootMidi) / 12);
            return $"{rootKey}{octave}";
        }

        private static string TrimOctave(string noteName)
            => new(noteName.TakeWhile(c => !char.IsDigit(c)).ToArray());

        private static string GetArpeggioKeySignature(ArpeggioPattern pattern, string rootNote)
        {
            var root = NormalizeMajorKeyName(TrimOctave(rootNote));
            if (UsesMinorFamilyKeySignature(pattern))
                return RelativeMajorKeyForMinorRoot(root);
            return root;
        }

        private static bool UsesMinorFamilyKeySignature(ArpeggioPattern pattern)
            => pattern.SemitoneIntervals.Contains(3) && !pattern.SemitoneIntervals.Contains(4);

        private static string RelativeMajorKeyForMinorRoot(string minorRoot) => minorRoot switch
        {
            "A" => "C",
            "E" => "G",
            "B" => "D",
            "F#" => "A",
            "C#" => "E",
            "G#" => "B",
            "D#" => "F#",
            "A#" => "C#",
            "D" => "F",
            "G" => "Bb",
            "C" => "Eb",
            "F" => "Ab",
            "Bb" => "Db",
            "Eb" => "Gb",
            "Ab" => "Cb",
            _ => minorRoot
        };

        private static string NormalizeMajorKeyName(string key) => key switch
        {
            "A#" => "Bb",
            "D#" => "Eb",
            "G#" => "Ab",
            "C#" => "Db",
            "F#" => "Gb",
            _ => key
        };
    }
}
