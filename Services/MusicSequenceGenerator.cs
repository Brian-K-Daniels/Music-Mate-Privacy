using musicmate.Models;
using musicmate.Diagnostics;

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
        /// Previous pitched MIDI for the note immediately before this batch (−1 = none).
        /// Keeps melodic intervals continuous across upper/lower staff generation.
        /// </summary>
        public int StartPrevPitch { get; set; } = -1;

        /// <summary>
        /// Optional set of MIDI numbers (written pitch) to exclude from pitch selection.
        /// Used to skip notes the player has already mastered.  When too few notes remain
        /// after exclusion the full pool is used so generation keeps melodic variety.
        /// </summary>
        public HashSet<int> ExcludedMidiNumbers { get; set; } = new();

        /// <summary>
        /// After mastery exclusions, fewer than this many pitches triggers fallback to the
        /// full pool so interval caps do not lock generation into a two-note oscillation.
        /// </summary>
        public const int MinPitchPoolAfterMasteryExclusion = 4;

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

        /// <summary>
        /// Maximum melodic interval in semitones allowed between consecutive notes.
        /// Only enforced in random mode (<see cref="UseScaleOrder"/> = false).
        /// 0 = no limit (full range allowed).
        /// </summary>
        public int MaxMelodicIntervalSemitones { get; set; } = 0;

        /// <summary>
        /// Off-beat rhythmic emphasis.  <see cref="SyncopationLevel.None"/> keeps the
        /// legacy on-beat sequential fill.  Requires eighth-note (or shorter) smallest
        /// duration for audible syncopation motifs.
        /// </summary>
        public SyncopationLevel SyncopationLevel { get; set; } = SyncopationLevel.None;

        /// <summary>
        /// Per-slot rest probability (0–100).  -1 uses legacy behaviour (rests only when
        /// <see cref="RhythmVarietyPercent"/> &gt; 0, roughly 1-in-8).
        /// </summary>
        public int RestChancePercent { get; set; } = -1;

        /// <summary>
        /// When true (default), random-mode fresh sequences reuse rhythmic motifs across
        /// an A–A′–B–A phrase before picking new pitches for each pass.
        /// </summary>
        public bool UseMotifPhrases { get; set; } = true;

        /// <summary>
        /// Optional written-pitch MIDI to emphasize in random selection.
        /// Weight is <see cref="EmphasizedNoteSelectionPercent"/> (default from
        /// <see cref="NoteMasteryPreferenceDefaults.EmphasizedNoteSelectionPercent"/>).
        /// Still obeys same-pitch-class and interval rules.
        /// </summary>
        public int? EmphasizedMidiNumber { get; set; }

        /// <summary>0–100 target share of selections for <see cref="EmphasizedMidiNumber"/>.</summary>
        public int EmphasizedNoteSelectionPercent { get; set; } =
            NoteMasteryPreferenceDefaults.EmphasizedNoteSelectionPercent;

        private readonly record struct RhythmSlot(NoteDuration Duration, bool IsRest);

        private enum PhraseRole { A, APrime, B, AReturn }

        /// <summary>Semitone steps between consecutive pitched notes in a phrase.</summary>
        private sealed class PhraseContour
        {
            public int FirstPitchMidi { get; init; }
            public List<int> SemitoneDeltas { get; init; } = new();

            public static PhraseContour FromPitches(IReadOnlyList<int> pitches)
            {
                if (pitches.Count == 0)
                    return new PhraseContour();

                var deltas = new List<int>(Math.Max(0, pitches.Count - 1));
                for (int i = 1; i < pitches.Count; i++)
                    deltas.Add(pitches[i] - pitches[i - 1]);

                return new PhraseContour
                {
                    FirstPitchMidi = pitches[0],
                    SemitoneDeltas = deltas,
                };
            }
        }

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
            ExcludedMidiNumbers ??= new HashSet<int>();   //  2026.07.08 1757  Mastered notes in the table.

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
                // Add descending portion: reverse of pool excluding only the top note
                // (already present as the turnaround peak), ending on the bottom tonic.
                for (int d = sortedPool.Count - 2; d >= 0; d--)
                    walk.Add(sortedPool[d]);
                // When appending a batch, resume the walk at the correct position so
                // the descending branch continues instead of jumping back to the bottom.
                int walkLen = walk.Count;
                int startIdx = walkLen > 0 ? ScaleWalkOffset % walkLen : 0;
                scaleQueue = startIdx == 0
                    ? new Queue<int>(walk)
                    : new Queue<int>(walk.Skip(startIdx).Concat(walk.Take(startIdx)));
            }

            // 4. Fill measures — motif phrases for random fresh sequences, else slot-by-slot.
            if (ShouldUseMotifPhrases(pool))
                return GenerateMotifPhraseSequence(rng, pool, durationWeights);

            return GenerateSlotBySlotSequence(rng, pool, durationWeights, scaleQueue);
        }

        private bool ShouldUseMotifPhrases(List<int> pool)
            => UseMotifPhrases
               && !UseScaleOrder
               && StartMeasureIndex == 0
               && StartGlobalNoteIndex == 0
               && SyncopationLevel != SyncopationLevel.Full
               && pool.Count >= MinPitchPoolAfterMasteryExclusion;

        /// <summary>
        /// Legacy slot-by-slot fill (scale walk, append batches, syncopation, and fallback).
        /// </summary>
        private List<Measure> GenerateSlotBySlotSequence(
            Random rng,
            List<int> pool,
            Dictionary<NoteDuration, int> durationWeights,
            Queue<int>? scaleQueue)
        {
            var measures = new List<Measure>(MeasureCount);
            int globalNoteIndex = StartGlobalNoteIndex;
            int prevPitch = StartPrevPitch;
            double globalBeatCursor = StartBeatOffset;

            for (int mi = 0; mi < MeasureCount; mi++)
            {
                if (UseScaleOrder && (scaleQueue == null || scaleQueue.Count == 0))
                    break;

                var measure = new Measure(TimeSignature);
                double localCursor = 0.0;
                int absoluteMi = StartMeasureIndex + mi;

                while (measure.BeatsRemaining > 1e-9)
                {
                    bool isPhraseEnding = (mi + 1) % 4 == 0 || (mi + 1) % 2 == 0;

                    if (TryApplySyncopationMotif(
                            rng, measure, ref localCursor, globalBeatCursor, absoluteMi,
                            ref globalNoteIndex, ref prevPitch, pool, scaleQueue,
                            isPhraseEnding))
                        continue;

                    var dur = PickFittingDuration(rng, durationWeights, measure.BeatsRemaining, SmallestDuration);
                    bool isRest = ShouldSlotBeRest(rng);
                    bool isLastNoteOfMeasure = measure.BeatsRemaining - dur.ToBeatValue() < 1e-9;
                    AddRhythmSlot(rng, measure, ref localCursor, globalBeatCursor, absoluteMi,
                        ref globalNoteIndex, ref prevPitch, pool, scaleQueue,
                        dur, isRest, isPhraseEnding && isLastNoteOfMeasure);
                }

                globalBeatCursor += TimeSignature.TotalBeats;
                measures.Add(measure);
            }

            if (UseScaleOrder && measures.Count > 0)
                measures[measures.Count - 1].TrimTrailingRests();

            return measures;
        }

        /// <summary>
        /// Builds an A–A′–B–A tune by reusing rhythmic motifs and remapping pitches each pass.
        /// </summary>
        private List<Measure> GenerateMotifPhraseSequence(
            Random rng,
            List<int> pool,
            Dictionary<NoteDuration, int> durationWeights)
        {
            var motifA = BuildTwoMeasureMotif(rng, durationWeights);
            var motifB = BuildContrastingMotif(rng, durationWeights, motifA);

            var measures = new List<Measure>(MeasureCount);
            int globalNoteIndex = StartGlobalNoteIndex;
            int prevPitch = StartPrevPitch;
            double globalBeatCursor = StartBeatOffset;
            PhraseContour? contourA = null;

            for (int mi = 0; mi < MeasureCount;)
            {
                var role = GetPhraseRole(mi);
                int phraseEnd = Math.Min(mi + 2, MeasureCount);
                var phraseRhythms = new List<List<RhythmSlot>>(phraseEnd - mi);

                for (int m = mi; m < phraseEnd; m++)
                    phraseRhythms.Add(SelectMotifMeasure(role, m % 2, motifA, motifB));

                bool cadenceLast = role is PhraseRole.APrime or PhraseRole.AReturn;
                PhraseContour? reuse = role is PhraseRole.APrime or PhraseRole.AReturn ? contourA : null;
                bool preferPracticeStart = role == PhraseRole.AReturn;

                var built = FillPhraseFromRhythm(
                    rng, pool, phraseRhythms, measures, mi, globalBeatCursor,
                    ref globalNoteIndex, ref prevPitch,
                    reuse, preferPracticeStart, cadenceLast,
                    durationWeights);

                if (role == PhraseRole.A)
                    contourA = built;

                globalBeatCursor += (phraseEnd - mi) * TimeSignature.TotalBeats;
                mi = phraseEnd;
            }

#if DEBUG
            DebugLog.WriteLine(
                $"[MotifPhrase] {MeasureCount} measures, A slots={motifA[0].Count}+{motifA[1].Count}, B slots={motifB[0].Count}+{motifB[1].Count}, contourSteps={contourA?.SemitoneDeltas.Count ?? 0}");
#endif
            return measures;
        }

        /// <summary>
        /// Fills one 1–2 measure phrase.  Returns a new contour when generating freely,
        /// or <c>null</c> when reusing an existing contour.
        /// </summary>
        private PhraseContour? FillPhraseFromRhythm(
            Random rng,
            List<int> pool,
            IReadOnlyList<List<RhythmSlot>> measureRhythms,
            List<Measure> measures,
            int startMeasureIndex,
            double globalBeatStart,
            ref int globalNoteIndex,
            ref int prevPitch,
            PhraseContour? contourToReuse,
            bool preferPracticeStart,
            bool cadenceOnLastPitch,
            Dictionary<NoteDuration, int> durationWeights)
        {
            int pitchedSlotCount = measureRhythms.Sum(m => m.Count(s => !s.IsRest));
            bool canReuse = contourToReuse != null
                            && pitchedSlotCount > 0
                            && contourToReuse.SemitoneDeltas.Count == pitchedSlotCount - 1
                            && MaxMelodicIntervalSemitones > 2
                            && pool.Count >= MinPitchPoolAfterMasteryExclusion;

            var pitchedMidis = new List<int>(pitchedSlotCount);
            double globalBeatCursor = globalBeatStart;

            for (int mi = 0; mi < measureRhythms.Count; mi++)
            {
                var rhythm = measureRhythms[mi];
                NormalizeRhythmMeasure(rhythm, rng, durationWeights);
                var measure = new Measure(TimeSignature);
                double localCursor = 0.0;
                int absoluteMi = StartMeasureIndex + startMeasureIndex + mi;

                int lastPitchIdx = -1;
                for (int i = 0; i < rhythm.Count; i++)
                {
                    if (!rhythm[i].IsRest)
                        lastPitchIdx = i;
                }

                for (int i = 0; i < rhythm.Count; i++)
                {
                    var slot = rhythm[i];
                    if (slot.IsRest)
                    {
                        measure.AddNote(GeneratedNote.Rest(slot.Duration, absoluteMi,
                            globalBeatCursor + localCursor));
                        localCursor += slot.Duration.ToBeatValue();
                        continue;
                    }

                    bool isLastPhrasePitch = cadenceOnLastPitch
                        && mi == measureRhythms.Count - 1
                        && i == lastPitchIdx;

                    int pitch;
                    if (!canReuse)
                    {
                        pitch = PickPitch(rng, pool, prevPitch, isLastPhrasePitch);
                    }
                    else if (pitchedMidis.Count == 0)
                    {
                        pitch = ChooseTransposedStart(rng, pool, contourToReuse!, prevPitch, preferPracticeStart);
                    }
                    else if (isLastPhrasePitch)
                    {
                        pitch = PickPitch(rng, pool, prevPitch, isPhraseEnding: true);
                    }
                    else
                    {
                        int deltaIdx = pitchedMidis.Count - 1;
                        int target = pitchedMidis[^1] + contourToReuse!.SemitoneDeltas[deltaIdx];
                        pitch = SnapPitchToPool(rng, pool, target, prevPitch, phraseEnding: false);
                    }

                    var note = BuildNote(pitch, slot.Duration, absoluteMi,
                        globalBeatCursor + localCursor, globalNoteIndex, prevPitch);
                    measure.AddNote(note);
                    pitchedMidis.Add(pitch);
                    prevPitch = pitch;
                    globalNoteIndex++;
                    localCursor += slot.Duration.ToBeatValue();
                }

                PadMeasureToFullBar(rng, measure, ref localCursor, globalBeatCursor, absoluteMi, durationWeights);

                measures.Add(measure);
                globalBeatCursor += TimeSignature.TotalBeats;
            }

            if (canReuse || pitchedMidis.Count == 0)
                return null;

            return PhraseContour.FromPitches(pitchedMidis);
        }

        /// <summary>Picks a starting note for a transposed contour repeat.</summary>
        private int ChooseTransposedStart(
            Random rng, List<int> pool, PhraseContour contour, int prevMidi, bool preferPracticeStart)
        {
            int baseStart = contour.FirstPitchMidi;
            int[] offsets = preferPracticeStart
                ? new[] { 0, 0, 2, -2, 3, -3 }
                : new[] { 2, -2, 3, -3, 4, -4, 5, -5, 0 };

            foreach (int off in offsets.OrderBy(_ => rng.Next()))
            {
                int candidate = SnapPitchToPool(rng, pool, baseStart + off, prevMidi, phraseEnding: false);
                if (candidate != baseStart || preferPracticeStart)
                    return candidate;
            }

            return SnapPitchToPool(rng, pool, baseStart, prevMidi, phraseEnding: false);
        }

        /// <summary>Nearest pool pitch to target, honouring interval cap and pitch-class variety.</summary>
        private int SnapPitchToPool(
            Random rng, List<int> pool, int targetMidi, int prevMidi, bool phraseEnding)
        {
            if (phraseEnding)
                return PickPitch(rng, pool, prevMidi, isPhraseEnding: true);

            IEnumerable<int> candidates = pool;
            if (MaxMelodicIntervalSemitones > 0 && prevMidi >= 0)
                candidates = candidates.Where(m => Math.Abs(m - prevMidi) <= MaxMelodicIntervalSemitones);

            var ordered = candidates
                .OrderBy(m => Math.Abs(m - targetMidi))
                .ThenBy(m => prevMidi >= 0 && m % 12 == prevMidi % 12 ? 1 : 0)
                .ToList();

            if (ordered.Count > 0)
                return ordered[0];

            return PickFallbackPitch(rng, pool, prevMidi, targetMidi);
        }

        /// <summary>
        /// Last-resort pitch pick that still honours <see cref="MaxMelodicIntervalSemitones"/>.
        /// </summary>
        private int PickFallbackPitch(Random rng, List<int> pool, int prevMidi, int targetMidi = -1)
        {
            if (pool.Count == 0)
                throw new InvalidOperationException("Pitch pool is empty.");

            if (prevMidi < 0)
            {
                if (MaxMelodicIntervalSemitones > 0)
                {
                    int center = (pool.Min() + pool.Max()) / 2;
                    return pool.OrderBy(m => Math.Abs(m - center)).ThenBy(m => m).First();
                }

                return pool[rng.Next(pool.Count)];
            }

            IEnumerable<int> candidates = pool;
            if (MaxMelodicIntervalSemitones > 0)
                candidates = candidates.Where(m => Math.Abs(m - prevMidi) <= MaxMelodicIntervalSemitones);

            var capped = candidates.ToList();
            if (capped.Count == 0)
                return pool.OrderBy(m => Math.Abs(m - prevMidi)).ThenBy(m => m).First();

            if (targetMidi >= 0)
                return capped.OrderBy(m => Math.Abs(m - targetMidi)).ThenBy(m => m).First();

            return capped[rng.Next(capped.Count)];
        }

        private static PhraseRole GetPhraseRole(int measureIndex)
            => ((measureIndex / 2) % 4) switch
            {
                0 => PhraseRole.A,
                1 => PhraseRole.APrime,
                2 => PhraseRole.B,
                _ => PhraseRole.AReturn,
            };

        private static List<RhythmSlot> SelectMotifMeasure(
            PhraseRole role, int measureInPhrase,
            List<List<RhythmSlot>> motifA, List<List<RhythmSlot>> motifB)
        {
            measureInPhrase = Math.Clamp(measureInPhrase, 0, 1);
            return role switch
            {
                PhraseRole.B => motifB[measureInPhrase],
                _ => motifA[measureInPhrase],
            };
        }

        private List<List<RhythmSlot>> BuildTwoMeasureMotif(
            Random rng, Dictionary<NoteDuration, int> durationWeights)
        {
            var m0 = BuildMeasureRhythmPattern(rng, durationWeights);
            var m1 = BuildMeasureRhythmPattern(rng, durationWeights);
            NormalizeRhythmMeasure(m0, rng, durationWeights);
            NormalizeRhythmMeasure(m1, rng, durationWeights);
            return new List<List<RhythmSlot>> { m0, m1 };
        }

        /// <summary>
        /// Builds a B phrase that shares the A skeleton but changes at least one rhythmic slot.
        /// </summary>
        private List<List<RhythmSlot>> BuildContrastingMotif(
            Random rng,
            Dictionary<NoteDuration, int> durationWeights,
            List<List<RhythmSlot>> motifA)
        {
            var motifB = motifA
                .Select(measure => measure.Select(slot => slot).ToList())
                .ToList();

            // Prefer mutating the second bar so the opening rhythm still feels familiar.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                int measureIdx = attempt < 3 ? 1 : 0;
                var measure = motifB[measureIdx];
                if (measure.Count == 0)
                    continue;

                int slotIdx = rng.Next(measure.Count);
                var (dur, isRest) = measure[slotIdx];
                if (isRest)
                {
                    measure[slotIdx] = new RhythmSlot(NoteDuration.Quarter, false);
                    return motifB;
                }

                var replacement = PickAlternativeDuration(rng, durationWeights, dur);
                if (replacement != dur)
                {
                    if (TryReplaceSlotDuration(measure, slotIdx, replacement, TimeSignature.TotalBeats))
                    {
                        NormalizeRhythmMeasure(measure, rng, durationWeights);
                        return motifB;
                    }
                }
            }

            motifB[1] = BuildMeasureRhythmPattern(rng, durationWeights);
            NormalizeRhythmMeasure(motifB[0], rng, durationWeights);
            NormalizeRhythmMeasure(motifB[1], rng, durationWeights);
            return motifB;
        }

        private static bool TryReplaceSlotDuration(
            List<RhythmSlot> measure, int slotIdx, NoteDuration replacement, double measureBeats)
        {
            double oldBeats = measure[slotIdx].Duration.ToBeatValue();
            double newBeats = replacement.ToBeatValue();
            double delta = newBeats - oldBeats;
            if (measure.Sum(s => s.Duration.ToBeatValue()) + delta > measureBeats + 1e-9)
                return false;

            measure[slotIdx] = new RhythmSlot(replacement, measure[slotIdx].IsRest);
            return true;
        }

        private List<RhythmSlot> BuildMeasureRhythmPattern(
            Random rng, Dictionary<NoteDuration, int> durationWeights)
        {
            var slots = new List<RhythmSlot>();
            double beatsRemaining = TimeSignature.TotalBeats;

            while (beatsRemaining > 1e-9)
            {
                var dur = PickFittingDuration(rng, durationWeights, beatsRemaining, SmallestDuration);
                slots.Add(new RhythmSlot(dur, ShouldSlotBeRest(rng)));
                beatsRemaining -= dur.ToBeatValue();
            }

            NormalizeRhythmMeasure(slots, rng, durationWeights);
            return slots;
        }

        /// <summary>
        /// Ensures a motif measure spans exactly one notated bar. Motif contrast edits can
        /// shorten a pattern without topping up, which leaves empty beat space before the
        /// next bar line.
        /// </summary>
        private void NormalizeRhythmMeasure(
            List<RhythmSlot> measure,
            Random rng,
            Dictionary<NoteDuration, int> durationWeights)
        {
            double measureBeats = TimeSignature.TotalBeats;
            double sum = measure.Sum(s => s.Duration.ToBeatValue());
            double remaining = measureBeats - sum;

            while (remaining > 1e-9)
            {
                var dur = PickFittingDuration(rng, durationWeights, remaining, SmallestDuration);
                measure.Add(new RhythmSlot(dur, true));
                remaining -= dur.ToBeatValue();
            }
        }

        private void PadMeasureToFullBar(
            Random rng,
            Measure measure,
            ref double localCursor,
            double globalBeatBase,
            int absoluteMi,
            Dictionary<NoteDuration, int> durationWeights)
        {
            double remaining = TimeSignature.TotalBeats - localCursor;
            while (remaining > 1e-9)
            {
                var dur = PickFittingDuration(rng, durationWeights, remaining, SmallestDuration);
                measure.AddNote(GeneratedNote.Rest(dur, absoluteMi, globalBeatBase + localCursor));
                localCursor += dur.ToBeatValue();
                remaining -= dur.ToBeatValue();
            }
        }

        private bool ShouldSlotBeRest(Random rng)
        {
            if (RestChancePercent >= 0)
                return RestChancePercent > 0 && rng.Next(100) < RestChancePercent;

            if (RhythmVarietyPercent <= 0)
                return false;

            if (SyncopationLevel == SyncopationLevel.Full && rng.Next(12) == 0)
                return true;

            return rng.Next(8) == 0;
        }

        private NoteDuration PickAlternativeDuration(
            Random rng,
            Dictionary<NoteDuration, int> durationWeights,
            NoteDuration current)
        {
            var alternatives = durationWeights.Keys
                .Where(d => d != current)
                .ToList();

            if (alternatives.Count == 0)
                return current;

            return alternatives[rng.Next(alternatives.Count)];
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

        /// <summary>Last non-rest MIDI in note order, or −1 if none.</summary>
        public static int LastPitchedMidi(IEnumerable<GeneratedNote>? notes)
        {
            if (notes == null)
                return -1;

            foreach (var n in notes.Reverse())
            {
                if (!n.IsRest && n.MidiNumber >= 0)
                    return n.MidiNumber;
            }

            return -1;
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

            var scalePcs = NoteSessionService.GetScalePitchClasses(Key, Scale);

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

                // If both searches landed on the same tonic the user's range holds less than
                // one complete octave. Extend upward first so beginner scales stay in the
                // middle register; step down only when the upper octave already fits within
                // the configured bounds.  When neither extension fits within the original
                // bounds (e.g. G major in a C4–C5 range where G4 is the only tonic), extend
                // upward unconditionally so the scale always spans a full tonic-to-tonic
                // octave regardless of how the range was initialised.
                if (octaveStart == octaveEnd)
                {
                    if (octaveEnd + 12 <= maxMidi)
                        octaveEnd += 12;
                    else if (octaveStart - 12 >= minMidi)
                        octaveStart -= 12;
                    else
                        octaveEnd += 12;   // extend past the configured ceiling — tonic walk requires it
                }

                // Use the tonic-bounded range only when at least one complete octave fits.
                if (octaveStart < octaveEnd)
                {
                    minMidi = octaveStart;
                    maxMidi = octaveEnd;
                }

#if DEBUG
                DebugLog.WriteLine(
                    $"[ScaleRootTest] Key={Key} Scale={Scale} tonicPc={tonicPc} " +
                    $"octaveStart={octaveStart}({NoteSessionService.MidiToNoteName(octaveStart, false)}) " +
                    $"octaveEnd={octaveEnd}({NoteSessionService.MidiToNoteName(octaveEnd, false)}) " +
                    $"pool range={NoteSessionService.MidiToNoteName(minMidi, false)}-{NoteSessionService.MidiToNoteName(maxMidi, false)}");
#endif
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
                DebugLog.WriteLine(DebugLogCategory.StaffAndSequence,
                    $"[StaffPool] AccPct={AccidentalPercent} diatonic={fullPool.Count(m => { int p = ((m % 12) + 12) % 12; return scalePcs.Contains(p); })} chromatic={fullPool.Count(m => { int p = ((m % 12) + 12) % 12; return !scalePcs.Contains(p); })} total={fullPool.Count}");
                return fullPool;
            }

            // Remove mastered notes; fall back to the full pool when too few remain
            // so tight interval caps cannot trap generation in a two-note oscillation.
            // Keep an emphasized pitch even if it would otherwise be excluded.
            var filtered = fullPool
                .Where(m => !ExcludedMidiNumbers.Contains(m)
                            || (EmphasizedMidiNumber.HasValue && m == EmphasizedMidiNumber.Value))
                .ToList();
            if (EmphasizedMidiNumber.HasValue
                && fullPool.Contains(EmphasizedMidiNumber.Value)
                && !filtered.Contains(EmphasizedMidiNumber.Value))
            {
                filtered.Add(EmphasizedMidiNumber.Value);
            }

            return filtered.Count >= MinPitchPoolAfterMasteryExclusion ? filtered : fullPool;
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

                int halfW = RhythmVarietyPercent / 2;          // max 50
                int eighthW = RhythmVarietyPercent * 3 / 10;     // max 30
                int sixteenthW = RhythmVarietyPercent / 5;          // max 20

                if (halfW > 0 && NoteDuration.Half.ToBeatValue() >= smallestBeats)
                    weights[NoteDuration.Half] = halfW;
                if (eighthW > 0 && NoteDuration.Eighth.ToBeatValue() >= smallestBeats)
                    weights[NoteDuration.Eighth] = eighthW;
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
            int pick = rng.Next(total);
            int acc = 0;
            foreach (var (dur, w) in fitting)
            {
                acc += w;
                if (pick < acc) return dur;
            }
            return fitting[^1].Key;
        }

        /// <summary>
        /// Adds a single rest or pitched note at <paramref name="localCursor"/> and advances the cursor.
        /// </summary>
        private void AddRhythmSlot(
            Random rng,
            Measure measure,
            ref double localCursor,
            double globalBeatBase,
            int absoluteMi,
            ref int globalNoteIndex,
            ref int prevPitch,
            List<int> pool,
            Queue<int>? scaleQueue,
            NoteDuration dur,
            bool isRest,
            bool isPhraseEnding)
        {
            bool scaleExhausted = UseScaleOrder && (scaleQueue == null || scaleQueue.Count == 0);
            GeneratedNote note;
            if (isRest || scaleExhausted)
            {
                note = GeneratedNote.Rest(dur,
                    measureIndex: absoluteMi,
                    beatPosition: globalBeatBase + localCursor);
            }
            else
            {
                int pitch;
                if (scaleQueue != null && scaleQueue.Count > 0)
                    pitch = scaleQueue.Dequeue();
                else
                    pitch = PickPitch(rng, pool, prevPitch, isPhraseEnding);

                note = BuildNote(pitch, dur, absoluteMi,
                    globalBeatBase + localCursor, globalNoteIndex, prevPitch);
                prevPitch = pitch;
                globalNoteIndex++;
            }

            measure.AddNote(note);
            localCursor += dur.ToBeatValue();
        }

        /// <summary>
        /// Attempts to place a short syncopated rest/note motif at the current beat position.
        /// Returns <c>true</c> when a motif was applied.
        /// </summary>
        private bool TryApplySyncopationMotif(
            Random rng,
            Measure measure,
            ref double localCursor,
            double globalBeatBase,
            int absoluteMi,
            ref int globalNoteIndex,
            ref int prevPitch,
            List<int> pool,
            Queue<int>? scaleQueue,
            bool isPhraseEnding)
        {
            if (SyncopationLevel == SyncopationLevel.None)
                return false;

            // Syncopation motifs need eighth-note (or shorter) subdivisions.
            if (SmallestDuration.ToBeatValue() > NoteDuration.Eighth.ToBeatValue())
                return false;

            // Only start motifs on the eighth-note grid (integer or half-beat positions).
            double grid = localCursor * 2.0;
            if (Math.Abs(grid - Math.Round(grid)) > 1e-9)
                return false;

            int chance = SyncopationLevel == SyncopationLevel.Simple ? 28 : 48;
            if (localCursor < 1e-9)
                chance += SyncopationLevel == SyncopationLevel.Simple ? 12 : 22;

            if (rng.Next(100) >= chance)
                return false;

            var motifs = BuildSyncopationMotifs();
            if (motifs.Count == 0)
                return false;

            // Shuffle pick order so the same motif doesn't dominate.
            int start = rng.Next(motifs.Count);
            for (int attempt = 0; attempt < motifs.Count; attempt++)
            {
                var motif = motifs[(start + attempt) % motifs.Count];
                double motifBeats = motif.Sum(s => s.Duration.ToBeatValue());
                if (motifBeats > measure.BeatsRemaining + 1e-9)
                    continue;

                bool isLastSlotOfMeasure = Math.Abs(measure.BeatsRemaining - motifBeats) < 1e-9;
                for (int i = 0; i < motif.Length; i++)
                {
                    var (isRest, dur) = motif[i];
                    bool phraseEnd = isPhraseEnding && isLastSlotOfMeasure && i == motif.Length - 1 && !isRest;
                    AddRhythmSlot(rng, measure, ref localCursor, globalBeatBase, absoluteMi,
                        ref globalNoteIndex, ref prevPitch, pool, scaleQueue,
                        dur, isRest, phraseEnd);
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns syncopated rest/note patterns for the active <see cref="SyncopationLevel"/>.
        /// Each slot is (isRest, duration).  All durations respect <see cref="SmallestDuration"/>.
        /// </summary>
        private List<(bool IsRest, NoteDuration Duration)[]> BuildSyncopationMotifs()
        {
            bool allowEighth = SmallestDuration.ToBeatValue() <= NoteDuration.Eighth.ToBeatValue();
            bool allowSixteenth = SmallestDuration.ToBeatValue() <= NoteDuration.Sixteenth.ToBeatValue();
            if (!allowEighth)
                return new List<(bool, NoteDuration)[]>();

            var motifs = new List<(bool IsRest, NoteDuration Duration)[]>();
            var eighth = NoteDuration.Eighth;
            var quarter = NoteDuration.Quarter;
            var sixteenth = NoteDuration.Sixteenth;

            if (SyncopationLevel == SyncopationLevel.Simple)
            {
                // Upbeat entry: rest on beat, note on "&".
                motifs.Add(new[] { (true, eighth), (false, quarter) });
                motifs.Add(new[] { (true, eighth), (false, eighth) });
                // Weak-beat accent followed by on-beat resolution.
                motifs.Add(new[] { (false, eighth), (true, eighth), (false, quarter) });
                motifs.Add(new[] { (true, eighth), (false, eighth), (true, eighth), (false, eighth) });
            }
            else // Full
            {
                // Include all simple motifs plus stronger patterns.
                motifs.Add(new[] { (true, eighth), (false, quarter) });
                motifs.Add(new[] { (false, eighth), (true, eighth), (false, quarter) });
                // Classic tied-feel: rest–note–rest–note across two beats.
                motifs.Add(new[] { (true, eighth), (false, eighth), (true, eighth), (false, quarter) });
                // Off-beat run.
                motifs.Add(new[] { (false, eighth), (true, eighth), (false, eighth), (false, eighth) });
                // Backbeat emphasis after a quarter rest.
                motifs.Add(new[] { (true, quarter), (false, eighth), (false, eighth) });
                // Anticipation into the next downbeat.
                motifs.Add(new[] { (false, eighth), (false, eighth), (true, eighth), (false, quarter) });

                if (allowSixteenth)
                {
                    motifs.Add(new[] { (true, sixteenth), (false, sixteenth), (true, eighth), (false, quarter) });
                    motifs.Add(new[] { (false, sixteenth), (true, sixteenth), (false, eighth), (false, eighth) });
                }
            }

            return motifs;
        }

        /// <summary>
        /// Picks a random MIDI number from the pool using guided melodic weighting.
        /// Prefers stepwise motion, occasional skips, rare larger leaps, more frequent
        /// tonic/chord tones, and phrase endings on the tonic.
        /// Avoids adjacent same-pitch-class repeats (e.g. C4→C5).
        /// </summary>
        /// <param name="isPhraseEnding">True if this is the last note of a phrase (measure 2, 4, etc.)</param>
        private int PickPitch(Random rng, List<int> pool, int prevMidi = -1, bool isPhraseEnding = false)
        {
            if (pool.Count <= 1)
                return pool[0];

            if (prevMidi < 0)
            {
                if (TryPickEmphasizedOpening(rng, pool, out int emphasizedOpen))
                    return emphasizedOpen;

                // Start near the middle of the allowed range so the first note is not
                // an extreme ledger-line pitch when an interval cap is active.
                if (MaxMelodicIntervalSemitones > 0)
                {
                    int center = (pool.Min() + pool.Max()) / 2;
                    var nearCenter = pool
                        .Where(m => Math.Abs(m - center) <= MaxMelodicIntervalSemitones * 2)
                        .ToList();
                    if (nearCenter.Count == 0)
                        nearCenter = pool;
                    return nearCenter[rng.Next(nearCenter.Count)];
                }

                return pool[rng.Next(pool.Count)];
            }

            int prevPc = prevMidi % 12;
            var scalePcs = NoteSessionService.GetScalePitchClasses(Key, Scale);
            int tonicPc = ((NoteSessionService.NoteNameToMidi($"{Key}4") % 12) + 12) % 12;

            // Determine chord tones: tonic (1), third (3), fifth (5) of the scale
            var degreeIntervals = NoteSessionService.GetSevenNoteScaleDegreeIntervals(Scale);
            HashSet<int> chordTonePcs = new HashSet<int>();
            if (degreeIntervals != null && degreeIntervals.Length >= 5)
            {
                chordTonePcs.Add(tonicPc);                                           // 1
                chordTonePcs.Add((tonicPc + degreeIntervals[2]) % 12);              // 3
                chordTonePcs.Add((tonicPc + degreeIntervals[4]) % 12);              // 5
            }

            var weighted = new List<(int midi, int weight)>(pool.Count);
            int totalWeight = 0;

            foreach (int m in pool)
            {
                if (m % 12 == prevPc) continue;  // skip same pitch-class repeats

                int pc = (m % 12 + 12) % 12;
                int dist = Math.Abs(m - prevMidi);

                // ── Max interval cap ──────────────────────────────────────────────
                // If MaxMelodicIntervalSemitones > 0, exclude candidates beyond the limit.
                if (MaxMelodicIntervalSemitones > 0 && dist > MaxMelodicIntervalSemitones)
                    continue;

                int semitones = dist % 12;
                if (semitones == 0) semitones = 12;  // octave leap

                // ── Base interval weight ──────────────────────────────────────────
                // Strongly favor small intervals; discourage large leaps.
                // Scale degrees (diatonic steps) get extra boost.
                int baseWeight = semitones switch
                {
                    1 or 2 => 100,  // half-step or whole-step: very common
                    3 or 4 => 50,   // minor/major third: common skip      (was 60)
                    5 => 25,   // perfect fourth: occasional skip     (was 40)
                    7 => 12,   // perfect fifth: rare leap            (was 35)
                    6 => 8,    // tritone: very rare                  (was 20)
                    8 or 9 => 4,    // minor/major sixth: very rare leap   (was 15)
                    10 or 11 => 2,   // minor/major seventh: almost never   (was 8)
                    12 => 1,    // octave: almost never                (was 5)
                    _ => 1
                };

                // ── Scale-degree boost ────────────────────────────────────────────
                // Notes in the current scale get 2× weight; chromatic notes allowed but rarer.
                if (scalePcs.Contains(pc))
                    baseWeight *= 2;

                // ── Chord-tone boost ──────────────────────────────────────────────
                // Tonic, third, and fifth get extra weight for harmonic stability.
                if (chordTonePcs.Contains(pc))
                    baseWeight = (int)(baseWeight * 1.5);

                // ── Tonic extra boost ─────────────────────────────────────────────
                // Tonic is especially common for phrase endings and stability.
                if (pc == tonicPc)
                {
                    if (isPhraseEnding)
                        baseWeight = (int)(baseWeight * 5.0);  // very strong preference at phrase endings
                    else
                        baseWeight = (int)(baseWeight * 1.3);  // general preference elsewhere
                }

                weighted.Add((m, Math.Max(1, baseWeight)));
                totalWeight += Math.Max(1, baseWeight);
            }

            if (weighted.Count == 0)
                return PickFallbackPitch(rng, pool, prevMidi);

            ApplyEmphasizedNoteWeight(ref weighted, ref totalWeight);

            int roll = rng.Next(totalWeight);
            int cumulative = 0;
            foreach (var (midi, weight) in weighted)
            {
                cumulative += weight;
                if (roll < cumulative)
                    return midi;
            }
            return weighted[weighted.Count - 1].midi;
        }

        private bool TryPickEmphasizedOpening(Random rng, List<int> pool, out int midi)
        {
            midi = 0;
            if (!EmphasizedMidiNumber.HasValue || !pool.Contains(EmphasizedMidiNumber.Value))
                return false;

            int pct = Math.Clamp(EmphasizedNoteSelectionPercent, 1, 95);
            if (rng.Next(100) < pct)
            {
                midi = EmphasizedMidiNumber.Value;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Rebalances weights so the emphasized MIDI receives approximately
        /// <see cref="EmphasizedNoteSelectionPercent"/> of selection probability among
        /// currently eligible candidates (already filtered by interval / pitch-class rules).
        /// </summary>
        private void ApplyEmphasizedNoteWeight(
            ref List<(int midi, int weight)> weighted,
            ref int totalWeight)
        {
            if (!EmphasizedMidiNumber.HasValue || weighted.Count == 0)
                return;

            int targetMidi = EmphasizedMidiNumber.Value;
            int idx = weighted.FindIndex(w => w.midi == targetMidi);
            if (idx < 0)
                return;

            int pct = Math.Clamp(EmphasizedNoteSelectionPercent, 1, 95);
            int otherWeight = totalWeight - weighted[idx].weight;
            if (otherWeight <= 0)
                return;

            int emphasizeWeight = Math.Max(
                1,
                (int)Math.Round(otherWeight * (double)pct / (100 - pct)));
            totalWeight = otherWeight + emphasizeWeight;
            weighted[idx] = (targetMidi, emphasizeWeight);
        }

        /// <summary>
        /// Constructs a <see cref="GeneratedNote"/> from a raw MIDI number.
        /// Derives letter, octave, accidental, spelled name, and frequency.
        /// </summary>
        private GeneratedNote BuildNote(int midi, NoteDuration dur, int measureIndex, double beatPos, int globalIndex, int prevMidi = -1)
        {
            string spelledName = NoteSessionService.SpellWrittenPitch(midi, Key, Scale, prevMidi);
            double freq = MidiToFreq(midi);

            // Parse letter, accidental, octave from the spelled name.
            char letter = char.ToUpperInvariant(spelledName[0]);
            int octave = int.TryParse(spelledName[^1].ToString(), out var o) ? o : 4;

            var (accidental, finalSpelledName) = NoteSessionService.ResolveAccidentalAndSpelling(
                spelledName, midi, letter, octave, Key, Scale);
            spelledName = finalSpelledName;

            return new GeneratedNote
            {
                MidiNumber = midi,
                Letter = letter,
                Octave = octave,
                Accidental = accidental,
                SpelledName = spelledName,
                TargetFrequency = freq,
                Duration = dur,
                IsRest = false,
                MeasureIndex = measureIndex,
                BeatPosition = beatPos,
                IsPlayedCorrectly = false
            };
        }

        private static double MidiToFreq(int midi)
            => 440.0 * Math.Pow(2.0, (midi - 69) / 12.0);
    }
}
