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
        /// User chose a fixed play mode (Random, named scale, arpeggio, tune, or tuner).
        /// </summary>
        public static bool IsExplicitMode(NoteSessionService session)
        {
            var tune = session.Tune ?? string.Empty;
            if (tune is "Tuner" or "Arpeggio" or "Practice Tune")
                return true;

            if (session.ScaleSelectionMode == ScaleSelectionMode.Named)
                return true;

            if (session.ScaleSelectionMode == ScaleSelectionMode.Random)
                return true;

            // Random/Tuner picker "Random" (not the child default of random + By Level).
            if (session.IsRandomMode
                && string.Equals(
                    Preferences.Default.Get<string?>("SelectedTune", null),
                    "Random",
                    StringComparison.Ordinal))
                return true;

            return false;
        }

        /// <summary>
        /// Child practice, By Level, or adult mixed practice — roll composition sliders.
        /// </summary>
        public static bool UsesCompositionSliders(NoteSessionService session)
        {
            if (IsExplicitMode(session))
                return false;

            return session.ChildLevel > 0
                   || session.ScaleSelectionMode == ScaleSelectionMode.ByLevel;
        }

        /// <summary>
        /// Weighted pick among tunes / random / scales / arpeggios (Pc* sum to 100).
        /// </summary>
        public static ExerciseKind PickExerciseKind(NoteSessionService session, Random rng)
        {
            int level = ResolveLevel(session);
            int arpeggioWeight = session.PcArpeggios;
            if (ArpeggioCatalog.GetAvailablePatterns(level).Count == 0)
                arpeggioWeight = 0;

            var buckets = new (ExerciseKind Kind, int Weight)[]
            {
                (ExerciseKind.Tune, session.PcTunes),
                (ExerciseKind.Random, session.PcRandom),
                (ExerciseKind.Scale, session.PcScales),
                (ExerciseKind.Arpeggio, arpeggioWeight),
            };

            int total = buckets.Sum(b => b.Weight);
            if (total <= 0)
                return ExerciseKind.Random;

            int roll = rng.Next(total);
            foreach (var (kind, weight) in buckets)
            {
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
                    break;

                case ExerciseKind.Scale:
                    session.IsRandomMode = false;
                    session.Tune = "Selected Scale";
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

        private static int ResolveLevel(NoteSessionService session)
        {
            if (session.ChildLevel > 0)
                return session.ChildLevel;
            return Math.Clamp(Preferences.Default.Get("ChildPractice.Level", 1), 1, 100);
        }

        private static void ApplyRandomTune(NoteSessionService session, Random rng)
        {
            var tunes = TuneLibrary.All;
            if (tunes.Count == 0)
            {
                session.IsRandomMode = false;
                session.Tune = "Selected Scale";
                return;
            }

            session.IsRandomMode = false;
            session.SelectPracticeTune(tunes[rng.Next(tunes.Count)]);
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
