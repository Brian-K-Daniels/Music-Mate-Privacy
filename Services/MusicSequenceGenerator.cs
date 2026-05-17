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

        /// <summary>
        /// When <c>true</c>, notes are drawn in ascending then descending scale order
        /// (scale walk) instead of being picked randomly from the pool.
        /// Use this for "Selected Scale" mode so the player sees a proper scale sequence.
        /// </summary>
        public bool UseScaleOrder { get; set; } = false;

        /// <summary>
        /// When appending batches in scale-order mode, the number of pitch notes already
        /// generated (= <see cref="StartGlobalNoteIndex"/>).  The generator uses this to
        /// resume the ascending/descending walk at the correct position instead of
        /// restarting from the bottom tonic every batch.
        /// Leave at 0 for a fresh sequence.
        /// </summary>
        public int ScaleWalkOffset { get; set; } = 0;

        /// <summary>
        /// 0–100.  Percentage of note slots that receive a chromatic accidental (sharp or flat)
        /// that is not in the key signature.  Mirrors the v1 AccidentalPercent setting.
        /// Only applied in random mode (<see cref="UseScaleOrder"/> = false).
        /// 0 = no accidentals added, 100 = all slots get an accidental when possible.
        /// </summary>
        public int AccidentalPercent { get; set; } = 0;

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

            // 3. Build a scale-ordered pitch queue when UseScaleOrder is set.
            //    The queue walks up the pool then back down (excluding duplicate endpoints).
            Queue<int>? scaleQueue = null;
            if (UseScaleOrder)
            {
                var sortedPool = pool.OrderBy(m => m).ToList();
                var walk = new List<int>(sortedPool);
                // Add descending portion: reverse of pool minus both endpoints to avoid
                // repeating the top or bottom tonic — the turnaround has exactly one note.
                for (int d = sortedPool.Count - 2; d > 0; d--)
                    walk.Add(sortedPool[d]);
                // Do NOT append the bottom tonic: the walk ends one step above it so that
                // when the queue wraps the bottom tonic appears exactly once (start of the
                // next ascending pass), giving the natural …D4 | C4 D4… turnaround.
                // When appending a batch, resume the walk at the correct position so
                // the descending branch continues instead of jumping back to the bottom.
                int walkLen = walk.Count;
                int startIdx = walkLen > 0 ? ScaleWalkOffset % walkLen : 0;
                scaleQueue = startIdx == 0
                    ? new Queue<int>(walk)
                    : new Queue<int>(walk.Skip(startIdx).Concat(walk.Take(startIdx)));
            }

            // 4. Fill measures.
            var measures = new List<Measure>(MeasureCount);
            int globalNoteIndex = StartGlobalNoteIndex;
            int prevPitch = -1;  // tracks last non-rest MIDI for ascending/descending spelling

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
                        int pitch;
                        if (scaleQueue != null && scaleQueue.Count > 0)
                        {
                            // Scale-order mode: consume next note from the walk queue;
                            // refill the queue when exhausted so the phrase loops seamlessly.
                            pitch = scaleQueue.Dequeue();
                            if (scaleQueue.Count == 0)
                            {
                                var sortedPool2 = pool.OrderBy(m => m).ToList();
                                var walk2 = new List<int>(sortedPool2);
                                for (int d = sortedPool2.Count - 2; d > 0; d--)
                                    walk2.Add(sortedPool2[d]);
                                // No trailing tonic: the walk ends on the note above the bottom,
                                // so the bottom tonic appears naturally at the start of each cycle.
                                scaleQueue = new Queue<int>(walk2);
                            }
                        }
                        else
                        {
                            pitch = PickPitch(rng, pool);
                        }
                        note = BuildNote(pitch, dur, absoluteMi,
                            globalBeatCursor + localCursor, globalNoteIndex, prevPitch);
                        prevPitch = pitch;
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

            if (UseScaleOrder)
            {
                // Restrict the range to complete octaves that start and end on the key tonic
                // so the scale walk always begins and ends cleanly on the root.
                int tonicPc = ((NoteSessionService.NoteNameToMidi($"{Key}4") % 12) + 12) % 12;

                // First tonic at or above the user's low bound.
                int octaveStart = minMidi;
                while (octaveStart <= maxMidi && ((octaveStart % 12 + 12) % 12) != tonicPc)
                    octaveStart++;

                // Last tonic at or below the user's high bound.
                int octaveEnd = maxMidi;
                while (octaveEnd >= minMidi && ((octaveEnd % 12 + 12) % 12) != tonicPc)
                    octaveEnd--;

                // Use the tonic-bounded range only when at least one complete octave fits.
                if (octaveStart < octaveEnd)
                {
                    minMidi = octaveStart;
                    maxMidi = octaveEnd;
                }
            }

            var fullPool = new List<int>();
            for (int midi = minMidi; midi <= maxMidi; midi++)
            {
                int pc = ((midi % 12) + 12) % 12;
                if (scalePcs.Contains(pc))
                    fullPool.Add(midi);
            }

            if (ExcludedMidiNumbers.Count == 0 && (!UseScaleOrder && AccidentalPercent <= 0))
                return fullPool;

            // When AccidentalPercent > 0 (random mode only) add chromatic non-scale tones
            // to the pool weighted by the percentage.  We do this by duplicating chromatic
            // entries proportionally: for every 100 diatonic slots we add
            // AccidentalPercent chromatic slots so the random picker naturally hits them
            // at roughly the requested frequency.
            if (!UseScaleOrder && AccidentalPercent > 0)
            {
                var chromaticPool = new List<int>();
                for (int midi = minMidi; midi <= maxMidi; midi++)
                {
                    int pc = ((midi % 12) + 12) % 12;
                    if (!scalePcs.Contains(pc))
                        chromaticPool.Add(midi);
                }

                if (chromaticPool.Count > 0)
                {
                    // Add enough chromatic entries to achieve the requested accidental ratio.
                    // ratio = accidental_slots / total_slots = AccidentalPercent / 100
                    // accidental_count = diatonic_count * AccidentalPercent / (100 - AccidentalPercent)
                    int diatonicCount = fullPool.Count;
                    int wantChromatic = AccidentalPercent >= 100
                        ? diatonicCount * 4      // near-100%: flood with chromatic
                        : (int)Math.Round((double)diatonicCount * AccidentalPercent / (100 - AccidentalPercent));

                    var rngLocal = new Random();
                    for (int added = 0; added < wantChromatic; added++)
                        fullPool.Add(chromaticPool[rngLocal.Next(chromaticPool.Count)]);
                }
            }

            if (ExcludedMidiNumbers.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[V2Pool] AccPct={AccidentalPercent} diatonic={fullPool.Count(m => { int p=((m%12)+12)%12; return scalePcs.Contains(p); })} chromatic={fullPool.Count(m => { int p=((m%12)+12)%12; return !scalePcs.Contains(p); })} total={fullPool.Count}");
                return fullPool;
            }

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
        private GeneratedNote BuildNote(int midi, NoteDuration dur, int measureIndex, double beatPos, int globalIndex, int prevMidi = -1)
        {
            bool preferFlats = KeyUsesFlats(Key);

            // For C major (and other keys with no key signature), choose sharp/flat for
            // chromatic notes based on melodic direction: ascending → sharp, descending → flat.
            // This matches standard music-theory enharmonic spelling practice.
            if (!preferFlats && GetKeySigAccidentalCount(Key, Scale) == 0 && prevMidi >= 0)
            {
                bool isChromatic = !GetScalePitchClasses(Key, Scale).Contains(((midi % 12) + 12) % 12);
                if (isChromatic)
                    preferFlats = midi < prevMidi;  // descending → flat; ascending → sharp
            }

            string spelledName = NoteSessionService.MidiToNoteName(midi, preferFlats);
            double freq        = MidiToFreq(midi);

            // Parse letter, accidental, octave from the spelled name.
            char letter = char.ToUpperInvariant(spelledName[0]);
            int  octave = int.TryParse(spelledName[^1].ToString(), out var o) ? o : 4;

            Accidental accidental = Accidental.None;
            if (spelledName.Contains('#'))      accidental = Accidental.Sharp;
            else if (spelledName.Contains('b')) accidental = Accidental.Flat;
            else
            {
                // No sharp/flat in the spelled name — but if this letter is altered by the
                // key signature the note needs an explicit natural sign to cancel it.
                accidental = NeedsNaturalSign(letter, Key, Scale)
                    ? Accidental.Natural
                    : Accidental.None;
            }

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

        /// <summary>
        /// Returns true when <paramref name="letter"/> is altered by the key signature
        /// (sharped or flatted) and therefore a natural note on that letter contradicts
        /// the key and needs an explicit natural sign.
        /// </summary>
        private static bool NeedsNaturalSign(char letter, string key, string scale)
        {
            int accCount = GetKeySigAccidentalCount(key, scale);
            if (accCount == 0) return false;

            bool useFlats = KeyUsesFlats(key);
            // Flats order:  Bb Eb Ab Db Gb Cb Fb
            char[] flatLetters  = { 'B', 'E', 'A', 'D', 'G', 'C', 'F' };
            // Sharps order: F# C# G# D# A# E# B#
            char[] sharpLetters = { 'F', 'C', 'G', 'D', 'A', 'E', 'B' };
            char[] keySigLetters = useFlats ? flatLetters : sharpLetters;

            for (int i = 0; i < Math.Min(accCount, keySigLetters.Length); i++)
                if (keySigLetters[i] == char.ToUpperInvariant(letter)) return true;
            return false;
        }

        /// <summary>Circle-of-fifths accidental count — mirrors the drawable's logic.</summary>
        private static int GetKeySigAccidentalCount(string key, string scale)
        {
            string majorKey = scale switch
            {
                "Natural Minor" or "Aeolian" or "Harmonic Minor"
                    or "Melodic Minor" or "Jazz Melodic Minor" => RelativeMajorForKeySig(key),
                _ => key
            };
            return majorKey switch
            {
                "C"  => 0,
                "G"  => 1, "D"  => 2, "A"  => 3, "E"  => 4, "B"  => 5, "F#" => 6, "C#" => 7,
                "F"  => 1, "Bb" => 2, "Eb" => 3, "Ab" => 4, "Db" => 5, "Gb" => 6, "Cb" => 7,
                _ => 0
            };
        }

        private static string RelativeMajorForKeySig(string minorKey) => minorKey switch
        {
            "A" => "C", "E" => "G", "B" => "D", "F#" => "A", "C#" => "E", "G#" => "B", "D#" => "F#",
            "D" => "F", "G" => "Bb", "C" => "Eb", "F" => "Ab", "Bb" => "Db", "Eb" => "Gb",
            _ => minorKey
        };

        private static bool KeyUsesFlats(string key)
            => key is "F" or "Bb" or "Eb" or "Ab" or "Db" or "Gb" or "Cb";

        private static double MidiToFreq(int midi)
            => 440.0 * Math.Pow(2.0, (midi - 69) / 12.0);
    }
}
