using musicmate.Models;

namespace musicmate.Services
{
    /// <summary>
    /// Music Mate v2 — musical sequence generator.
    /// <para>
    /// Produces a list of <see cref="GeneratedNote"/> objects organised into
    /// <see cref="Measure"/> instances for a given key, scale, note range, and
    /// time signature.  The generator is completely self-contained and does NOT
    /// touch any v1 session state in <see cref="NoteSessionService"/>.
    /// </para>
    /// <para>
    /// Entry point: <see cref="GenerateSequence"/>.
    /// </para>
    /// </summary>
    public sealed class MusicSequenceGenerator
    {
        // ── Configuration ─────────────────────────────────────────────────────────

        /// <summary>Key root letter, e.g. "C", "F#", "Bb".</summary>
        public string Key { get; set; } = "C";

        /// <summary>Scale name matching the v1 vocabulary, e.g. "Major", "Natural Minor".</summary>
        public string Scale { get; set; } = "Major";

        /// <summary>Lowest allowed note name in scientific pitch notation, e.g. "C4".</summary>
        public string LowestNote { get; set; } = "C4";

        /// <summary>Highest allowed note name in scientific pitch notation, e.g. "B5".</summary>
        public string HighestNote { get; set; } = "B5";

        /// <summary>
        /// Time signature for generated measures.
        /// Defaults to 4/4.  Supported presets: <see cref="TimeSignature.FourFour"/>,
        /// <see cref="TimeSignature.ThreeFour"/>, <see cref="TimeSignature.TwoFour"/>.
        /// </summary>
        public TimeSignature TimeSignature { get; set; } = TimeSignature.FourFour;

        /// <summary>Total number of measures to generate.</summary>
        public int MeasureCount { get; set; } = 4;

        /// <summary>
        /// 0–100.  Probability that any given note slot receives a rhythmic duration
        /// other than a quarter note.  0 = all quarters, 100 = maximum variety.
        /// </summary>
        public int RhythmVarietyPercent { get; set; } = 40;

        /// <summary>
        /// The smallest note value that may appear in the generated sequence.
        /// Defaults to <see cref="NoteDuration.Quarter"/> so no note is shorter
        /// than a quarter unless the user chooses Eighth or Sixteenth.
        /// </summary>
        public NoteDuration SmallestDuration { get; set; } = NoteDuration.Quarter;

        /// <summary>
        /// Optional seed for the random number generator.
        /// Null (default) uses a time-based seed so each call differs.
        /// </summary>
        public int? RandomSeed { get; set; }

        // ── Append-mode offsets ───────────────────────────────────────────────────
        // When appending to an existing sequence, set these so that generated notes
        // carry globally correct MeasureIndex, BeatPosition, and session indices.
        // Leave at defaults (0) for a fresh sequence.

        /// <summary>Measure index to assign to the first generated measure (0 for fresh sequences).</summary>
        public int StartMeasureIndex { get; set; } = 0;

        /// <summary>Global beat offset to add to every generated note's BeatPosition (0 for fresh sequences).</summary>
        public double StartBeatOffset { get; set; } = 0.0;

        /// <summary>Global note index counter start (0 for fresh sequences).</summary>
        public int StartGlobalNoteIndex { get; set; } = 0;

        /// <summary>
        /// Optional set of MIDI numbers (written pitch) to exclude from pitch selection.
        /// Used to skip notes the player has already mastered.  When all pool notes are
        /// excluded the full pool is used as fallback so the sequence never runs dry.
        /// </summary>
        public HashSet<int> ExcludedMidiNumbers { get; set; } = new();

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Generates a sequence of measures filled with <see cref="GeneratedNote"/> objects.
        /// </summary>
        /// <returns>
        /// A list of <see cref="Measure"/> instances, each containing one or more
        /// <see cref="GeneratedNote"/> slots (pitches or rests).
        /// Returns an empty list when no valid pitch pool can be built from the
        /// current <see cref="LowestNote"/> / <see cref="HighestNote"/> / scale settings.
        /// </returns>
        public List<Measure> GenerateSequence()
        {
            var rng = RandomSeed.HasValue ? new Random(RandomSeed.Value) : new Random();

            // 1. Build the allowed pitch pool from the scale + range settings.
            var pool = BuildPitchPool();
            if (pool.Count == 0)
                return new List<Measure>();

            // 2. Decide which durations are available and with what weights.
            var durationWeights = BuildDurationWeights();

            // 3. Fill measures.
            var measures = new List<Measure>(MeasureCount);
            int globalNoteIndex = StartGlobalNoteIndex;

            // globalBeatCursor accumulates across measures so every note carries a
            // sequence-wide beat position suitable for proportional layout and scrolling.
            double globalBeatCursor = StartBeatOffset;

            for (int mi = 0; mi < MeasureCount; mi++)
            {
                var measure        = new Measure(TimeSignature);
                double localCursor = 0.0;              // beats used within this measure
                int absoluteMi     = StartMeasureIndex + mi;

                while (measure.BeatsRemaining > 1e-9)
                {
                    // Pick a duration that fits in the remaining space.
                    var dur = PickFittingDuration(rng, durationWeights, measure.BeatsRemaining, SmallestDuration);

                    // Occasionally insert a rest (roughly 1-in-8 chance when variety is on).
                    bool isRest = RhythmVarietyPercent > 0 && rng.Next(8) == 0;

                    GeneratedNote note;
                    if (isRest)
                    {
                        note = GeneratedNote.Rest(dur,
                            measureIndex:  absoluteMi,
                            beatPosition:  globalBeatCursor + localCursor);
                    }
                    else
                    {
                        var pitch = PickPitch(rng, pool);
                        note = BuildNote(pitch, dur, absoluteMi,
                            globalBeatCursor + localCursor, globalNoteIndex);
                        globalNoteIndex++;
                    }

                    measure.AddNote(note);
                    localCursor += dur.ToBeatValue();
                }

                // Advance global beat cursor by the full measure length.
                globalBeatCursor += TimeSignature.TotalBeats;
                measures.Add(measure);
            }

            return measures;
        }

        /// <summary>
        /// Flattens the measures returned by <see cref="GenerateSequence"/> into an
        /// ordered list of all <see cref="GeneratedNote"/> slots (including rests).
        /// Useful for rendering and session tracking.
        /// </summary>
        public static List<GeneratedNote> Flatten(IEnumerable<Measure> measures)
        {
            var result = new List<GeneratedNote>();
            foreach (var m in measures)
                result.AddRange(m.GeneratedNotes);
            return result;
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Builds the list of valid MIDI numbers for the configured key/scale/range.
        /// Re-uses the same lookup tables that v1 <c>NoteSessionService</c> uses.
        /// </summary>
        private List<int> BuildPitchPool()
        {
            int minMidi = NoteSessionService.NoteNameToMidi(LowestNote);
            int maxMidi = NoteSessionService.NoteNameToMidi(HighestNote);
            if (minMidi < 0 || maxMidi < 0 || minMidi > maxMidi)
                return new List<int>();

            bool preferFlats = KeyUsesFlats(Key);
            var scalePcs = GetScalePitchClasses(Key, Scale);

            var fullPool = new List<int>();
            for (int midi = minMidi; midi <= maxMidi; midi++)
            {
                int pc = ((midi % 12) + 12) % 12;
                if (scalePcs.Contains(pc))
                    fullPool.Add(midi);
            }

            if (ExcludedMidiNumbers.Count == 0)
                return fullPool;

            // Remove mastered notes; fall back to the full pool when too few remain
            // so the sequence never runs dry.
            var filtered = fullPool.Where(m => !ExcludedMidiNumbers.Contains(m)).ToList();
            return filtered.Count >= 2 ? filtered : fullPool;
        }

        /// <summary>
        /// Returns a dictionary mapping <see cref="NoteDuration"/> to a relative weight
        /// based on <see cref="RhythmVarietyPercent"/> and <see cref="SmallestDuration"/>.
        /// </summary>
        private Dictionary<NoteDuration, int> BuildDurationWeights()
        {
            // Quarter notes are always the baseline.
            var weights = new Dictionary<NoteDuration, int>
            {
                [NoteDuration.Quarter] = 100
            };

            if (RhythmVarietyPercent > 0)
            {
                // Scale secondary-duration weights linearly with variety setting.
                // Only include durations that are >= SmallestDuration (beat-value check).
                double smallestBeats = SmallestDuration.ToBeatValue();

                int halfW      = RhythmVarietyPercent / 2;          // max 50
                int eighthW    = RhythmVarietyPercent * 3 / 10;     // max 30
                int sixteenthW = RhythmVarietyPercent / 5;          // max 20

                if (halfW   > 0 && NoteDuration.Half.ToBeatValue()      >= smallestBeats)
                    weights[NoteDuration.Half]      = halfW;
                if (eighthW > 0 && NoteDuration.Eighth.ToBeatValue()    >= smallestBeats)
                    weights[NoteDuration.Eighth]    = eighthW;
                if (sixteenthW > 0 && NoteDuration.Sixteenth.ToBeatValue() >= smallestBeats)
                    weights[NoteDuration.Sixteenth] = sixteenthW;
            }

            return weights;
        }

        /// <summary>
        /// Picks a duration that both the weights table selects AND that fits within
        /// <paramref name="beatsRemaining"/>.  Falls back to the shortest fitting
        /// duration when the weighted pick doesn't fit.
        /// </summary>
        private static NoteDuration PickFittingDuration(
            Random rng,
            Dictionary<NoteDuration, int> weights,
            double beatsRemaining,
            NoteDuration smallestAllowed = NoteDuration.Quarter)
        {
            // Filter to durations that fit.
            var fitting = weights
                .Where(kv => kv.Key.ToBeatValue() <= beatsRemaining + 1e-9)
                .ToList();

            if (fitting.Count == 0)
            {
                // No weight-table duration fits the remaining space.
                // Search ALL defined durations (smallest to largest) and return
                // the largest one that still fits, guaranteeing no measure overflow.
                var allDurations = new[] { NoteDuration.Whole, NoteDuration.Half, NoteDuration.Quarter, NoteDuration.Eighth, NoteDuration.Sixteenth };
                return allDurations
                    .Where(d => d.ToBeatValue() <= beatsRemaining + 1e-9)
                    .OrderByDescending(d => d.ToBeatValue())
                    .FirstOrDefault(NoteDuration.Sixteenth);
            }

            int total = fitting.Sum(kv => kv.Value);
            int pick  = rng.Next(total);
            int acc   = 0;
            foreach (var (dur, w) in fitting)
            {
                acc += w;
                if (pick < acc) return dur;
            }
            return fitting[^1].Key;
        }

        /// <summary>Picks a random MIDI number from the pool.</summary>
        private static int PickPitch(Random rng, List<int> pool)
            => pool[rng.Next(pool.Count)];

        /// <summary>
        /// Constructs a <see cref="GeneratedNote"/> from a raw MIDI number.
        /// Derives letter, octave, accidental, spelled name, and frequency.
        /// </summary>
        private GeneratedNote BuildNote(int midi, NoteDuration dur, int measureIndex, double beatPos, int globalIndex)
        {
            bool preferFlats = KeyUsesFlats(Key);
            string spelledName = NoteSessionService.MidiToNoteName(midi, preferFlats);
            double freq        = MidiToFreq(midi);

            // Parse letter, accidental, octave from the spelled name.
            char letter = char.ToUpperInvariant(spelledName[0]);
            int  octave = int.TryParse(spelledName[^1].ToString(), out var o) ? o : 4;

            Accidental accidental = Accidental.None;
            if (spelledName.Contains('#'))      accidental = Accidental.Sharp;
            else if (spelledName.Contains('b')) accidental = Accidental.Flat;

            return new GeneratedNote
            {
                MidiNumber       = midi,
                Letter           = letter,
                Octave           = octave,
                Accidental       = accidental,
                SpelledName      = spelledName,
                TargetFrequency  = freq,
                Duration         = dur,
                IsRest           = false,
                MeasureIndex     = measureIndex,
                BeatPosition     = beatPos,
                IsPlayedCorrectly = false
            };
        }

        // ── Static pitch-class helpers ────────────────────────────────────────────

        /// <summary>
        /// Returns the set of chromatic pitch classes (0–11) that belong to the
        /// given key and scale.  Uses the same interval patterns as v1.
        /// </summary>
        private static HashSet<int> GetScalePitchClasses(string key, string scale)
        {
            // Semitone intervals from tonic for common scales.
            int[] intervals = scale switch
            {
                "Major" or "Ionian"       => new[] { 0, 2, 4, 5, 7, 9, 11 },
                "Natural Minor" or
                "Aeolian"                 => new[] { 0, 2, 3, 5, 7, 8, 10 },
                "Harmonic Minor"          => new[] { 0, 2, 3, 5, 7, 8, 11 },
                "Melodic Minor" or
                "Jazz Melodic Minor"      => new[] { 0, 2, 3, 5, 7, 9, 11 },
                "Dorian"                  => new[] { 0, 2, 3, 5, 7, 9, 10 },
                "Phrygian"                => new[] { 0, 1, 3, 5, 7, 8, 10 },
                "Lydian"                  => new[] { 0, 2, 4, 6, 7, 9, 11 },
                "Mixolydian"              => new[] { 0, 2, 4, 5, 7, 9, 10 },
                "Locrian"                 => new[] { 0, 1, 3, 5, 6, 8, 10 },
                "Major Pentatonic"        => new[] { 0, 2, 4, 7, 9 },
                "Minor Pentatonic"        => new[] { 0, 3, 5, 7, 10 },
                "Blues" or "Minor Blues"  => new[] { 0, 3, 5, 6, 7, 10 },
                "Major Blues"             => new[] { 0, 2, 3, 4, 7, 9 },
                "Chromatic"               => new[] { 0,1,2,3,4,5,6,7,8,9,10,11 },
                "Lydian Dominant"         => new[] { 0, 2, 4, 6, 7, 9, 10 },
                "Harmonic Major"          => new[] { 0, 2, 4, 5, 7, 8, 11 },
                "Phrygian Dominant"       => new[] { 0, 1, 4, 5, 7, 8, 10 },
                "Hungarian Minor"         => new[] { 0, 2, 3, 6, 7, 8, 11 },
                "Double Harmonic"         => new[] { 0, 1, 4, 5, 7, 8, 11 },
                "Bebop"                   => new[] { 0, 2, 4, 5, 7, 9, 10, 11 },
                _                         => new[] { 0, 2, 4, 5, 7, 9, 11 }  // default to Major
            };

            int tonicPc = ((NoteSessionService.NoteNameToMidi($"{key}4") % 12) + 12) % 12;

            var pcs = new HashSet<int>();
            foreach (var interval in intervals)
                pcs.Add((tonicPc + interval) % 12);

            return pcs;
        }

        private static bool KeyUsesFlats(string key)
            => key is "F" or "Bb" or "Eb" or "Ab" or "Db" or "Gb" or "Cb";

        private static double MidiToFreq(int midi)
            => 440.0 * Math.Pow(2.0, (midi - 69) / 12.0);
    }
}
