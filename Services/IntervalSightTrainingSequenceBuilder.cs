using musicmate.Models;

namespace musicmate.Services
{
    /// <summary>
    /// Builds an independent Sight Training sequence: Child Level key/range/% Accidentals,
    /// MSG rhythm scaffolding, and a Sight-specific interval-selection pitch layer.
    /// Does not mutate Music-page session cursors or Music melody weights.
    /// </summary>
    public static class IntervalSightTrainingSequenceBuilder
    {
        public const int DefaultMeasureCount = 8;
        public const int MaxGenerateAttempts = 24;

        public sealed class SightExercise
        {
            public required List<GeneratedNote> Notes { get; init; }
            public required List<double> BarBeats { get; init; }
            public required string Key { get; init; }
            public required string Scale { get; init; }
            /// <summary>Absolute magnitude of the last consecutive pitched interval (0 if &lt;2 notes).</summary>
            public int FinalIntervalMagnitude { get; init; }
            /// <summary>Sight-only max magnitude used for this exercise.</summary>
            public int MaxAbsoluteSemitones { get; init; }
        }

        public static MusicSequenceGenerator CreateGenerator(
            NoteSessionService session,
            string key,
            string scale,
            string lowestNote,
            string highestNote,
            int maxMelodicIntervalSemitones,
            int accidentalPercent,
            int measureCount,
            int? randomSeed = null,
            int? childLevelOverride = null)
        {
            ArgumentNullException.ThrowIfNull(session);

            var timeSig = TimeSignature.FromDisplayString(session.MeterTimeSignature);
            int maxInterval = IntervalSightTrainingMelody.ResolveMaxAbsIntervalSemitones(
                maxMelodicIntervalSemitones);
            int childLevel = childLevelOverride is int ov && ov > 0
                ? Math.Clamp(ov, 1, 100)
                : IntervalSightTrainingLogic.LoadPersistedLevel();

            return new MusicSequenceGenerator
            {
                Key = key,
                Scale = scale,
                LowestNote = lowestNote,
                HighestNote = highestNote,
                TimeSignature = timeSig,
                MeasureCount = Math.Max(1, measureCount),
                RhythmVarietyPercent = session.RhythmVarietyPercent >= 0
                    ? session.RhythmVarietyPercent
                    : session.RhythmMode == "Mixed" ? 60 : 0,
                SmallestDuration = session.SmallestRhythmNote switch
                {
                    "Sixteenth" => NoteDuration.Sixteenth,
                    "Eighth" => NoteDuration.Eighth,
                    _ => NoteDuration.Quarter
                },
                StartMeasureIndex = 0,
                StartBeatOffset = 0,
                StartGlobalNoteIndex = 0,
                StartPrevPitch = -1,
                ExcludedMidiNumbers = new HashSet<int>(),
                UseScaleOrder = false,
                ScaleWalkOffset = 0,
                AccidentalPercent = Math.Clamp(accidentalPercent, 0, 100),
                // MSG still gets a generous cap so rhythm generation is not starved;
                // pitches are replaced by the Sight training layer below.
                MaxMelodicIntervalSemitones = maxInterval,
                SyncopationLevel = SyncopationLevelHelper.Parse(session.SyncopationSetting),
                RestChancePercent = 0,
                EmphasizedMidiNumber = null,
                EmphasizedNoteSelectionPercent = 0,
                ActivityType = "IntervalSightTraining",
                ChildLevel = childLevel,
                MinDistinctPitches = MelodicVarietyRules.GetMinimumDistinctPitchesForLevel(childLevel),
                RandomSeed = randomSeed ?? (Environment.TickCount ^ Guid.NewGuid().GetHashCode()),
            };
        }

        /// <summary>Generates a Sight exercise (primary API).</summary>
        /// <param name="childLevelOverride">
        /// When set, used instead of <see cref="NoteSessionService.ChildLevel"/> so the
        /// Sight Training page can pick a practice level without mutating Music settings.
        /// </param>
        public static SightExercise Generate(
            NoteSessionService session,
            int? measureCount = null,
            int? randomSeed = null,
            int? excludeFirstMagnitude = null,
            int? childLevelOverride = null)
        {
            ArgumentNullException.ThrowIfNull(session);

            int seed = randomSeed ?? (Environment.TickCount ^ Guid.NewGuid().GetHashCode());
            var rng = new Random(seed);

            int level = childLevelOverride is int ov && ov > 0
                ? Math.Clamp(ov, 1, 100)
                : IntervalSightTrainingLogic.LoadPersistedLevel();
            var levelSettings = DifficultyLevelMapper.ResolveSessionSettings(level, rng);

            string key = levelSettings.ForceKey;
            string scale = levelSettings.SuggestedScale;

            // Written range from Settings (session Lowest/Highest). Sight Level
            // still controls interval sizes and key; it must not pick pitches
            // outside the user's configured range.
            var (low, high) = ResolveSettingsNoteRange(
                session, levelSettings.LowestNote, levelSettings.HighestNote);

            // Preserve the user's current Accidental % setting.
            int accidentalPercent = session.AccidentalPercent;
            int maxAbs = IntervalSightTrainingMelody.MaxSightIntervalForLevel(level);

            int poolSize = maxAbs + 1;
            int maxPitchedNotes = poolSize + 1;
            int measures = measureCount
                ?? Math.Clamp(
                    Math.Max(1, (maxPitchedNotes + 2) / 3),
                    1,
                    Math.Max(1, levelSettings.MeasureBatchSize));

            for (int attempt = 0; attempt < MaxGenerateAttempts; attempt++)
            {
                int attemptSeed = unchecked(seed + attempt * 9973);
                var gen = CreateGenerator(
                    session, key, scale, low, high, maxAbs, accidentalPercent, measures, attemptSeed,
                    childLevelOverride: level);
                var flat = MusicSequenceGenerator.Flatten(gen.GenerateSequence()).ToList();
                flat.RemoveAll(n => n.IsRest);
                EnsureMinimumPitchedSlots(flat, maxPitchedNotes, key, scale, low);
                TrimToMaxPitchedNotes(flat, maxPitchedNotes);

                if (flat.Count < 2)
                    continue;

                ApplyTrainingIntervals(
                    flat, key, scale, low, high, maxAbs, accidentalPercent,
                    attemptSeed ^ 0xA11CE, excludeFirstMagnitude);

                if (!IntervalSightTrainingMelody.TryValidateCompleteCycle(
                        flat, maxAbs, excludeFirstMagnitude, out var mags))
                {
                    continue;
                }

                ClampNotesToSettingsRange(flat, low, high, key, scale);

                return new SightExercise
                {
                    Notes = flat,
                    BarBeats = BuildBarBeats(flat, gen.TimeSignature.TotalBeats),
                    Key = key,
                    Scale = scale,
                    FinalIntervalMagnitude = mags.Count > 0 ? mags[^1] : 0,
                    MaxAbsoluteSemitones = maxAbs,
                };
            }

            // Guaranteed path: build enough note slots then apply training pitches.
            var fallbackGen = CreateGenerator(
                session, key, scale, low, high, maxAbs, accidentalPercent, measures, seed ^ 0x51C07,
                childLevelOverride: level);
            var fallbackFlat = MusicSequenceGenerator.Flatten(fallbackGen.GenerateSequence()).ToList();
            fallbackFlat.RemoveAll(n => n.IsRest);
            EnsureMinimumPitchedSlots(fallbackFlat, maxPitchedNotes, key, scale, low);
            TrimToMaxPitchedNotes(fallbackFlat, maxPitchedNotes);

            ApplyTrainingIntervals(
                fallbackFlat, key, scale, low, high, maxAbs, accidentalPercent,
                seed ^ 0xBEEF, excludeFirstMagnitude);

            var fallbackMags = IntervalSightTrainingMelody.CollectMagnitudes(fallbackFlat);
            ClampNotesToSettingsRange(fallbackFlat, low, high, key, scale);
            return new SightExercise
            {
                Notes = fallbackFlat,
                BarBeats = BuildBarBeats(fallbackFlat, fallbackGen.TimeSignature.TotalBeats),
                Key = key,
                Scale = scale,
                FinalIntervalMagnitude = fallbackMags.Count > 0 ? fallbackMags[^1] : 0,
                MaxAbsoluteSemitones = maxAbs,
            };
        }

        /// <summary>
        /// Replaces pitched MIDI with a Sight training chain: unique magnitudes, random
        /// direction, Settings note range, key spelling, and %-accidental eligibility.
        /// </summary>
        public static void ApplyTrainingIntervals(
            List<GeneratedNote> notes,
            string key,
            string scale,
            string lowestNote,
            string highestNote,
            int maxAbsSemitones,
            int accidentalPercent,
            int randomSeed,
            int? excludeFirstMagnitude = null)
        {
            ArgumentNullException.ThrowIfNull(notes);

            var pitchedIdx = new List<int>();
            for (int i = 0; i < notes.Count; i++)
            {
                if (!notes[i].IsRest)
                    pitchedIdx.Add(i);
            }

            if (pitchedIdx.Count == 0)
                return;

            int lowMidi = NoteSessionService.NoteNameToMidi(lowestNote);
            int highMidi = NoteSessionService.NoteNameToMidi(highestNote);
            if (lowMidi <= 0 || highMidi <= 0 || highMidi < lowMidi)
            {
                lowMidi = 60;
                highMidi = 72;
            }

            int maxAbs = IntervalSightTrainingMelody.ResolveMaxAbsIntervalSemitones(maxAbsSemitones);
            var rng = new Random(randomSeed);
            var allowed = IntervalSightTrainingMelody.BuildPitchEligibilitySet(
                lowMidi, highMidi, key, scale, accidentalPercent, rng);

            int steps = pitchedIdx.Count - 1;
            var midis = IntervalSightTrainingMelody.BuildTrainingMidiChain(
                lowMidi, highMidi, steps, maxAbs, rng, excludeFirstMagnitude, allowed);

            // If eligibility was too sparse for large leaps, retry without chromatic filter
            // (still within range) so enabled magnitudes are not silently dropped.
            if (midis.Count < pitchedIdx.Count
                || !MagnitudesCoverExpected(midis, maxAbs, steps, excludeFirstMagnitude))
            {
                midis = IntervalSightTrainingMelody.BuildTrainingMidiChain(
                    lowMidi, highMidi, steps, maxAbs, new Random(randomSeed ^ 0x5A17),
                    excludeFirstMagnitude, allowedMidis: null);
            }

            int prevMidi = -1;
            for (int p = 0; p < pitchedIdx.Count; p++)
            {
                int noteIndex = pitchedIdx[p];
                int midi = p < midis.Count
                    ? midis[p]
                    : Math.Clamp(notes[noteIndex].MidiNumber, lowMidi, highMidi);
                midi = Math.Clamp(midi, lowMidi, highMidi);
                var old = notes[noteIndex];
                notes[noteIndex] = RespellPitchedNote(old, midi, key, scale, prevMidi);
                prevMidi = midi;
            }
        }

        /// <summary>Backward-compatible alias used by older call sites/tests.</summary>
        public static void ApplyNoRepeatIntervals(
            List<GeneratedNote> notes,
            string key,
            string scale,
            string lowestNote,
            string highestNote,
            int maxAbsSemitones,
            int randomSeed,
            int? excludeFirstMagnitude = null)
            => ApplyTrainingIntervals(
                notes, key, scale, lowestNote, highestNote, maxAbsSemitones,
                accidentalPercent: 0, randomSeed, excludeFirstMagnitude);

        private static bool MagnitudesCoverExpected(
            IReadOnlyList<int> midis,
            int maxAbs,
            int steps,
            int? excludeFirst)
        {
            if (midis.Count < steps + 1)
                return false;

            var seen = new HashSet<int>();
            for (int i = 1; i < midis.Count; i++)
            {
                int mag = Math.Abs(midis[i] - midis[i - 1]);
                if (mag > maxAbs)
                    return false;
                if (i == 1 && excludeFirst is int excl && mag == excl)
                    return false;
                if (!seen.Add(mag))
                    return false;
            }

            int pool = maxAbs + 1;
            if (steps >= pool)
                return seen.Count == pool;
            return seen.Count == steps;
        }

        private static void TrimToMaxPitchedNotes(List<GeneratedNote> notes, int maxPitched)
        {
            if (maxPitched <= 0 || notes.Count == 0)
                return;

            int pitched = 0;
            int keepThrough = -1;
            for (int i = 0; i < notes.Count; i++)
            {
                if (notes[i].IsRest)
                    continue;
                pitched++;
                keepThrough = i;
                if (pitched >= maxPitched)
                    break;
            }

            if (keepThrough >= 0 && keepThrough < notes.Count - 1)
                notes.RemoveRange(keepThrough + 1, notes.Count - keepThrough - 1);
        }

        private static void EnsureMinimumPitchedSlots(
            List<GeneratedNote> notes,
            int minPitched,
            string key,
            string scale,
            string fallbackNoteName)
        {
            int pitched = notes.Count(n => !n.IsRest);
            if (pitched >= minPitched)
                return;

            int midi = NoteSessionService.NoteNameToMidi(fallbackNoteName);
            if (midi <= 0)
                midi = 60;

            while (notes.Count(n => !n.IsRest) < minPitched)
            {
                notes.Add(RespellPitchedNote(
                    new GeneratedNote
                    {
                        Duration = NoteDuration.Quarter,
                        MeasureIndex = notes.Count > 0 ? notes[^1].MeasureIndex : 0,
                        BeatPosition = notes.Count > 0
                            ? (notes[^1].BeatPosition ?? 0) + notes[^1].BeatDuration
                            : 0,
                    },
                    midi,
                    key,
                    scale,
                    prevMidi: -1));
            }
        }

        private static GeneratedNote RespellPitchedNote(
            GeneratedNote template,
            int midi,
            string key,
            string scale,
            int prevMidi)
        {
            bool preferFlats = KeySignatureRules.KeySignatureUsesFlats(key, scale);
            string spelledName = EnsureSpellingMatchesMidi(
                NoteSessionService.SpellWrittenPitch(midi, key, scale, prevMidi),
                midi,
                preferFlats);

            char letter = char.ToUpperInvariant(spelledName[0]);
            int octave = NoteSessionService.ParseOctaveFromSpelledName(spelledName);
            var (accidental, finalSpelledName) = NoteSessionService.ResolveAccidentalAndSpelling(
                spelledName, midi, letter, octave, key, scale);

            int written = IntervalSightTrainingLogic.SightSoundingMidi(
                new GeneratedNote
                {
                    MidiNumber = midi,
                    Letter = letter,
                    Octave = octave,
                    Accidental = accidental,
                    SpelledName = finalSpelledName,
                },
                key,
                scale);
            if (written != midi)
            {
                spelledName = NoteSessionService.MidiToNoteName(midi, preferFlats);
                letter = char.ToUpperInvariant(spelledName[0]);
                octave = NoteSessionService.ParseOctaveFromSpelledName(spelledName);
                (accidental, finalSpelledName) = NoteSessionService.ResolveAccidentalAndSpelling(
                    spelledName, midi, letter, octave, key, scale);
            }

            return new GeneratedNote
            {
                MidiNumber = midi,
                Letter = letter,
                Octave = octave,
                Accidental = accidental,
                SpelledName = finalSpelledName,
                TargetFrequency = 440.0 * Math.Pow(2.0, (midi - 69) / 12.0),
                Duration = template.Duration,
                IsRest = false,
                MeasureIndex = template.MeasureIndex,
                BeatPosition = template.BeatPosition,
                IsPlayedCorrectly = false,
            };
        }

        /// <summary>
        /// Sight Training quizzes what the staff shows. Reject spellings that do not
        /// round-trip to <paramref name="midi"/> (e.g. C♯ for MIDI 62) and prefer
        /// single-accidental chromatic names over double sharps/flats.
        /// </summary>
        private static string EnsureSpellingMatchesMidi(string spelledName, int midi, bool preferFlats)
        {
            if (string.IsNullOrWhiteSpace(spelledName)
                || spelledName.Contains("##", StringComparison.Ordinal)
                || spelledName.Contains("bb", StringComparison.Ordinal)
                || !TryParseSpelledMidi(spelledName, out int parsed)
                || parsed != midi)
            {
                return NoteSessionService.MidiToNoteName(midi, preferFlats);
            }

            return spelledName;
        }

        private static bool TryParseSpelledMidi(string spelled, out int midi)
        {
            midi = 0;
            if (string.IsNullOrWhiteSpace(spelled) || spelled.Length < 2 || !char.IsDigit(spelled[^1]))
                return false;
            try
            {
                midi = NoteSessionService.NoteNameToMidi(spelled);
                return midi > 0;
            }
            catch (FormatException)
            {
                return false;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private static (string Low, string High) ResolveSettingsNoteRange(
            NoteSessionService session,
            string fallbackLow,
            string fallbackHigh)
        {
            string low = session.LowestNote;
            string high = session.HighestNote;
            int lo = NoteSessionService.NoteNameToMidi(low);
            int hi = NoteSessionService.NoteNameToMidi(high);
            if (lo <= 0 || hi <= 0)
                return (fallbackLow, fallbackHigh);
            if (hi < lo)
                return (high, low);
            return (low, high);
        }

        /// <summary>
        /// Final guard: every pitched MIDI must sit in the Settings written range.
        /// </summary>
        private static void ClampNotesToSettingsRange(
            List<GeneratedNote> notes,
            string lowestNote,
            string highestNote,
            string key,
            string scale)
        {
            int lo = NoteSessionService.NoteNameToMidi(lowestNote);
            int hi = NoteSessionService.NoteNameToMidi(highestNote);
            if (lo <= 0 || hi <= 0 || hi < lo)
                return;

            int prevMidi = -1;
            for (int i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                if (n.IsRest)
                    continue;
                int midi = Math.Clamp(n.MidiNumber, lo, hi);
                if (midi == n.MidiNumber)
                {
                    prevMidi = midi;
                    continue;
                }

                notes[i] = RespellPitchedNote(n, midi, key, scale, prevMidi);
                prevMidi = midi;
            }
        }

        private static List<double> BuildBarBeats(List<GeneratedNote> flat, double beatsPerMeasure)
        {
            var bars = new List<double>();
            if (flat.Count == 0)
                return bars;

            int prev = flat[0].MeasureIndex ?? 0;
            foreach (var n in flat)
            {
                int m = n.MeasureIndex ?? prev;
                if (m != prev && n.BeatPosition.HasValue)
                {
                    bars.Add(n.BeatPosition.Value);
                    prev = m;
                }
            }

            if (bars.Count == 0)
            {
                double maxBeat = flat.Max(n => (n.BeatPosition ?? 0) + n.BeatDuration);
                for (double b = beatsPerMeasure; b < maxBeat - 1e-6; b += beatsPerMeasure)
                    bars.Add(b);
            }

            return bars;
        }
    }
}
