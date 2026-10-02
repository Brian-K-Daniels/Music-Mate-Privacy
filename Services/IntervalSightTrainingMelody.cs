namespace musicmate.Services
{
    /// <summary>
    /// Interval-magnitude helpers and Sight-only training pitch selection.
    /// Does not alter Music-page <see cref="MusicSequenceGenerator"/> weights.
    /// </summary>
    public static class IntervalSightTrainingMelody
    {
        public const int MaxAbsoluteSemitones = IntervalSightTrainingLogic.MaxAbsoluteSemitones;

        /// <summary>
        /// Sight Training max consecutive interval (semitones). Early bands track the
        /// Child Level Music cap; upper bands open toward a full octave so buttons 0–12
        /// are actually exercised. Does not change <see cref="ChildLevelProgression.MaxIntervalForLevel"/>.
        /// </summary>
        public static int MaxSightIntervalForLevel(int childLevel)
        {
            int level = Math.Clamp(childLevel, 1, 100);
            if (level <= 10) return 2;
            if (level <= 20) return 3;
            if (level <= 40) return 4;
            if (level <= 60) return 6;
            if (level <= 80) return 8;
            if (level <= 90) return 10;
            return 12;
        }

        public static int ResolveMaxAbsIntervalSemitones(int sessionMaxMelodicIntervalSemitones)
        {
            int raw = sessionMaxMelodicIntervalSemitones > 0
                ? sessionMaxMelodicIntervalSemitones
                : MaxAbsoluteSemitones;
            return Math.Clamp(raw, 1, MaxAbsoluteSemitones);
        }

        /// <summary>Magnitude pool: 0, 1, …, maxAbs.</summary>
        public static List<int> CreateIntervalPool(int maxAbsSemitones)
        {
            maxAbsSemitones = Math.Clamp(maxAbsSemitones, 1, MaxAbsoluteSemitones);
            var pool = new List<int>(maxAbsSemitones + 1);
            for (int d = 0; d <= maxAbsSemitones; d++)
                pool.Add(d);
            return pool;
        }

        /// <summary>
        /// One shuffled copy of 0…maxAbs. When <paramref name="avoidFirst"/> is set and the
        /// pool has more than one value, the first slot is swapped if it would repeat that magnitude.
        /// </summary>
        public static List<int> CreateShuffledCycle(
            int maxAbsSemitones,
            Random rng,
            int? avoidFirst = null)
        {
            ArgumentNullException.ThrowIfNull(rng);
            var pool = CreateIntervalPool(maxAbsSemitones);
            Shuffle(pool, rng);
            if (avoidFirst is int excl && pool.Count > 1 && pool[0] == excl)
            {
                int j = 1 + rng.Next(pool.Count - 1);
                (pool[0], pool[j]) = (pool[j], pool[0]);
            }
            return pool;
        }

        /// <summary>
        /// True when <paramref name="magnitudes"/> is a permutation of 0…maxAbs
        /// (each available interval once, none missing, none repeated).
        /// </summary>
        public static bool IsCompleteCycle(IReadOnlyList<int> magnitudes, int maxAbsSemitones)
        {
            ArgumentNullException.ThrowIfNull(magnitudes);
            maxAbsSemitones = ResolveMaxAbsIntervalSemitones(maxAbsSemitones);
            int pool = maxAbsSemitones + 1;
            if (magnitudes.Count != pool)
                return false;
            var seen = new HashSet<int>();
            foreach (int mag in magnitudes)
            {
                if (mag < 0 || mag > maxAbsSemitones)
                    return false;
                if (!seen.Add(mag))
                    return false;
            }
            return seen.Count == pool;
        }

        /// <summary>
        /// Splits a magnitude stream into successive complete cycles (permutations of 0…maxAbs).
        /// A trailing partial cycle is ignored.
        /// </summary>
        public static List<List<int>> SplitCompleteCycles(
            IReadOnlyList<int> magnitudes,
            int maxAbsSemitones)
        {
            ArgumentNullException.ThrowIfNull(magnitudes);
            maxAbsSemitones = ResolveMaxAbsIntervalSemitones(maxAbsSemitones);
            int pool = maxAbsSemitones + 1;
            var cycles = new List<List<int>>();
            for (int i = 0; i + pool <= magnitudes.Count; i += pool)
            {
                var slice = magnitudes.Skip(i).Take(pool).ToList();
                if (!IsCompleteCycle(slice, maxAbsSemitones))
                    break;
                cycles.Add(slice);
            }
            return cycles;
        }

        public static List<int> CollectMagnitudes(IReadOnlyList<Models.GeneratedNote> notes)
        {
            var pitched = new List<int>();
            for (int i = 0; i < notes.Count; i++)
            {
                if (!notes[i].IsRest)
                    pitched.Add(notes[i].MidiNumber);
            }

            var mags = new List<int>(Math.Max(0, pitched.Count - 1));
            for (int i = 1; i < pitched.Count; i++)
                mags.Add(Math.Abs(pitched[i] - pitched[i - 1]));
            return mags;
        }

        /// <summary>
        /// True when consecutive magnitudes stay in 0…maxAbs, do not repeat until the pool
        /// is exhausted, and the first magnitude is not <paramref name="excludeFirstMagnitude"/>
        /// (when provided). Complete-cycle checks use <see cref="IsCompleteCycle"/>.
        /// </summary>
        public static bool TryValidateUniqueMagnitudes(
            IReadOnlyList<Models.GeneratedNote> notes,
            int maxAbsSemitones,
            int? excludeFirstMagnitude,
            out List<int> magnitudes)
        {
            magnitudes = CollectMagnitudes(notes);
            maxAbsSemitones = ResolveMaxAbsIntervalSemitones(maxAbsSemitones);

            if (magnitudes.Count == 0)
                return false;

            if (excludeFirstMagnitude is int excl && magnitudes[0] == excl)
                return false;

            return HasNoRepeatsUntilPoolExhausted(magnitudes, maxAbsSemitones);
        }

        /// <summary>
        /// True when the melody is one complete shuffled cycle (every 0…maxAbs once)
        /// and the first magnitude is not <paramref name="excludeFirstMagnitude"/> when set.
        /// </summary>
        public static bool TryValidateCompleteCycle(
            IReadOnlyList<Models.GeneratedNote> notes,
            int maxAbsSemitones,
            int? excludeFirstMagnitude,
            out List<int> magnitudes)
        {
            magnitudes = CollectMagnitudes(notes);
            if (!IsCompleteCycle(magnitudes, maxAbsSemitones))
                return false;
            if (excludeFirstMagnitude is int excl && magnitudes[0] == excl)
                return false;
            return true;
        }

        /// <summary>
        /// Builds a MIDI chain for Sight Training: unique consecutive magnitudes from the
        /// level pool, random direction when both fit, start pitch biased toward mid-range
        /// so larger leaps remain realizable. Optional pitch set enforces diatonic/%-accidental
        /// eligibility without changing the target magnitude.
        /// </summary>
        public static List<int> BuildTrainingMidiChain(
            int lowMidi,
            int highMidi,
            int stepCount,
            int maxAbsSemitones,
            Random rng,
            int? excludeFirstMagnitude = null,
            IReadOnlyCollection<int>? allowedMidis = null)
        {
            ArgumentNullException.ThrowIfNull(rng);
            if (highMidi < lowMidi)
                throw new ArgumentException("highMidi must be >= lowMidi.");
            if (stepCount <= 0)
                return new List<int>();

            maxAbsSemitones = ResolveMaxAbsIntervalSemitones(maxAbsSemitones);
            var allowed = BuildAllowedSet(lowMidi, highMidi, allowedMidis);
            if (allowed.Count == 0)
                allowed = Enumerable.Range(lowMidi, highMidi - lowMidi + 1).ToHashSet();

            int startMidi = PickStartMidi(allowed, lowMidi, highMidi, maxAbsSemitones, rng);
            var deltas = PickIntervalsWithoutRepetition(
                startMidi, lowMidi, highMidi, stepCount, maxAbsSemitones, rng,
                excludeFirstMagnitude, allowed);
            return BuildMidiChain(startMidi, deltas);
        }

        /// <summary>
        /// Picks signed MIDI steps from successive shuffled cycles of 0…maxAbs.
        /// Each cycle tests every magnitude once before any repeat. Direction is random
        /// when both fit; if a magnitude cannot land from the current pitch, the opposite
        /// direction or a new starting note is used — the magnitude is never skipped.
        /// </summary>
        public static List<int> PickIntervalsWithoutRepetition(
            int startMidi,
            int lowMidi,
            int highMidi,
            int stepCount,
            int maxAbsSemitones,
            Random rng,
            int? excludeFirstMagnitude = null,
            IReadOnlyCollection<int>? allowedMidis = null)
        {
            ArgumentNullException.ThrowIfNull(rng);
            if (highMidi < lowMidi)
                throw new ArgumentException("highMidi must be >= lowMidi.");
            if (stepCount <= 0)
                return new List<int>();

            var allowed = BuildAllowedSet(lowMidi, highMidi, allowedMidis);
            startMidi = Math.Clamp(startMidi, lowMidi, highMidi);
            if (allowed.Count == 0)
                allowed = Enumerable.Range(lowMidi, highMidi - lowMidi + 1).ToHashSet();
            if (!allowed.Contains(startMidi))
            {
                startMidi = allowed
                    .OrderBy(m => Math.Abs(m - startMidi))
                    .ThenBy(m => m)
                    .First();
            }

            maxAbsSemitones = ResolveMaxAbsIntervalSemitones(maxAbsSemitones);
            var magnitudes = BuildCycledMagnitudeSequence(
                stepCount, maxAbsSemitones, rng, excludeFirstMagnitude);

            if (!TryRealizeMagnitudeSequence(
                    magnitudes, startMidi, lowMidi, highMidi, rng, allowed, out var signed))
            {
                var chromatic = Enumerable.Range(lowMidi, highMidi - lowMidi + 1).ToHashSet();
                if (!TryRealizeMagnitudeSequence(
                        magnitudes, startMidi, lowMidi, highMidi, rng, chromatic, out signed))
                {
                    // This permutation cannot be realized in range — reshuffle cycles, never drop a mag.
                    for (int reshuffle = 0; reshuffle < 12 && signed.Count == 0; reshuffle++)
                    {
                        magnitudes = BuildCycledMagnitudeSequence(
                            stepCount, maxAbsSemitones, rng, excludeFirstMagnitude);
                        TryRealizeMagnitudeSequence(
                            magnitudes, startMidi, lowMidi, highMidi, rng, chromatic, out signed);
                    }
                }
            }

            return signed;
        }

        /// <summary>
        /// Concatenates shuffled complete cycles until <paramref name="stepCount"/> magnitudes
        /// are filled. The first magnitude of a new cycle avoids the previous cycle's last
        /// when the pool has more than one value.
        /// </summary>
        public static List<int> BuildCycledMagnitudeSequence(
            int stepCount,
            int maxAbsSemitones,
            Random rng,
            int? excludeFirstMagnitude = null)
        {
            ArgumentNullException.ThrowIfNull(rng);
            maxAbsSemitones = ResolveMaxAbsIntervalSemitones(maxAbsSemitones);
            var seq = new List<int>(Math.Max(0, stepCount));
            int? avoidFirst = excludeFirstMagnitude;
            while (seq.Count < stepCount)
            {
                var cycle = CreateShuffledCycle(maxAbsSemitones, rng, avoidFirst);
                seq.AddRange(cycle);
                avoidFirst = cycle[^1];
            }

            if (seq.Count > stepCount)
                seq.RemoveRange(stepCount, seq.Count - stepCount);
            return seq;
        }

        public static List<int> BuildMidiChain(int startMidi, IReadOnlyList<int> signedIntervals)
        {
            ArgumentNullException.ThrowIfNull(signedIntervals);
            var midis = new List<int>(signedIntervals.Count + 1) { startMidi };
            int pitch = startMidi;
            for (int i = 0; i < signedIntervals.Count; i++)
            {
                pitch += signedIntervals[i];
                midis.Add(pitch);
            }
            return midis;
        }

        /// <summary>
        /// True when absolute magnitudes do not repeat until 0…maxAbs have all appeared
        /// (a new cycle may then repeat). Signed or unsigned lists are accepted.
        /// </summary>
        public static bool HasNoRepeatsUntilPoolExhausted(
            IReadOnlyList<int> signedIntervals,
            int maxAbsSemitones)
        {
            ArgumentNullException.ThrowIfNull(signedIntervals);
            maxAbsSemitones = ResolveMaxAbsIntervalSemitones(maxAbsSemitones);
            int pool = maxAbsSemitones + 1;
            var seen = new HashSet<int>();
            int inCycle = 0;
            foreach (int d in signedIntervals)
            {
                int mag = Math.Abs(d);
                if (mag > maxAbsSemitones)
                    return false;
                if (inCycle == 0)
                    seen.Clear();
                if (!seen.Add(mag))
                    return false;
                inCycle++;
                if (inCycle >= pool)
                    inCycle = 0;
            }
            return true;
        }

        /// <summary>
        /// Eligible MIDI numbers in range: always all diatonic pitches (so leaps 0…max remain
        /// realizable); chromatics added in proportion to <paramref name="accidentalPercent"/>
        /// (same spirit as MSG pool inflation) for written accidentals.
        /// </summary>
        public static HashSet<int> BuildPitchEligibilitySet(
            int lowMidi,
            int highMidi,
            string key,
            string scale,
            int accidentalPercent,
            Random? rng = null)
        {
            var scalePcs = NoteSessionService.GetScalePitchClasses(key, scale);
            var diatonic = new List<int>();
            var chromatic = new List<int>();
            for (int m = lowMidi; m <= highMidi; m++)
            {
                int pc = ((m % 12) + 12) % 12;
                if (scalePcs.Contains(pc))
                    diatonic.Add(m);
                else
                    chromatic.Add(m);
            }

            var set = new HashSet<int>(diatonic);
            accidentalPercent = Math.Clamp(accidentalPercent, 0, 100);
            if (accidentalPercent <= 0 || chromatic.Count == 0)
                return set;

            // Mirror MSG: wantChromatic ≈ diatonic * pct / (100 - pct), capped by available.
            int want = accidentalPercent >= 100
                ? chromatic.Count
                : (int)Math.Round(diatonic.Count * (double)accidentalPercent / (100 - accidentalPercent));
            want = Math.Clamp(want, 0, chromatic.Count);

            var shuffled = chromatic.ToList();
            if (rng != null)
                Shuffle(shuffled, rng);
            else
            {
                // Deterministic spread across the range when no RNG is supplied.
                int stride = Math.Max(1, chromatic.Count / Math.Max(1, want));
                shuffled = Enumerable.Range(0, chromatic.Count)
                    .Select(i => chromatic[(i * stride) % chromatic.Count])
                    .Distinct()
                    .ToList();
            }

            foreach (int m in shuffled.Take(want))
                set.Add(m);

            return set;
        }

        private static HashSet<int> BuildAllowedSet(
            int lowMidi, int highMidi, IReadOnlyCollection<int>? allowedMidis)
        {
            if (allowedMidis == null || allowedMidis.Count == 0)
                return Enumerable.Range(lowMidi, highMidi - lowMidi + 1).ToHashSet();

            var set = new HashSet<int>();
            foreach (int m in allowedMidis)
            {
                if (m >= lowMidi && m <= highMidi)
                    set.Add(m);
            }
            return set;
        }

        private static int PickStartMidi(
            HashSet<int> allowed,
            int lowMidi,
            int highMidi,
            int maxAbs,
            Random rng)
        {
            // Prefer the central band so most magnitudes 0…maxAbs have room to land.
            int span = highMidi - lowMidi;
            int centerLo = lowMidi + Math.Max(0, span / 4);
            int centerHi = highMidi - Math.Max(0, span / 4);
            if (centerHi < centerLo)
                (centerLo, centerHi) = (lowMidi, highMidi);

            var preferred = allowed.Where(m => m >= centerLo && m <= centerHi).ToList();
            if (preferred.Count == 0)
                preferred = allowed.ToList();

            // Bias toward starts that can realize at least half the pool upward or downward.
            var roomy = preferred
                .Where(m => (m - lowMidi) >= maxAbs / 2 || (highMidi - m) >= maxAbs / 2)
                .ToList();
            if (roomy.Count > 0)
                preferred = roomy;

            return preferred[rng.Next(preferred.Count)];
        }

        private static bool TryRealizeMagnitudeSequence(
            IReadOnlyList<int> magnitudes,
            int startMidi,
            int lowMidi,
            int highMidi,
            Random rng,
            HashSet<int> allowed,
            out List<int> signed)
        {
            signed = new List<int>();
            if (magnitudes.Count == 0)
                return true;

            var starts = BuildStartCandidates(allowed, lowMidi, highMidi, startMidi, rng);
            var path = new List<int>(magnitudes.Count);
            foreach (int start in starts)
            {
                path.Clear();
                if (TryRealizeFrom(start, 0, magnitudes, lowMidi, highMidi, allowed, rng, path))
                {
                    signed = new List<int>(path);
                    return true;
                }
            }

            return false;
        }

        private static List<int> BuildStartCandidates(
            HashSet<int> allowed,
            int lowMidi,
            int highMidi,
            int preferredStart,
            Random rng)
        {
            var starts = allowed.ToList();
            Shuffle(starts, rng);
            int idx = starts.IndexOf(preferredStart);
            if (idx > 0)
            {
                (starts[0], starts[idx]) = (starts[idx], starts[0]);
            }
            else if (idx < 0 && preferredStart >= lowMidi && preferredStart <= highMidi)
            {
                starts.Insert(0, preferredStart);
            }

            const int maxStarts = 32;
            if (starts.Count > maxStarts)
                starts.RemoveRange(maxStarts, starts.Count - maxStarts);

            return starts;
        }

        private static bool TryRealizeFrom(
            int pitch,
            int index,
            IReadOnlyList<int> magnitudes,
            int lowMidi,
            int highMidi,
            HashSet<int> allowed,
            Random rng,
            List<int> path)
        {
            if (index >= magnitudes.Count)
                return true;

            int mag = magnitudes[index];
            var candidates = new List<int>(2);
            if (mag == 0)
            {
                if (pitch >= lowMidi && pitch <= highMidi && allowed.Contains(pitch))
                    candidates.Add(0);
            }
            else
            {
                int up = pitch + mag;
                int down = pitch - mag;
                bool upOk = up <= highMidi && allowed.Contains(up);
                bool downOk = down >= lowMidi && allowed.Contains(down);
                if (upOk && downOk)
                {
                    if (rng.Next(2) == 0)
                    {
                        candidates.Add(mag);
                        candidates.Add(-mag);
                    }
                    else
                    {
                        candidates.Add(-mag);
                        candidates.Add(mag);
                    }
                }
                else if (upOk)
                    candidates.Add(mag);
                else if (downOk)
                    candidates.Add(-mag);
            }

            foreach (int signed in candidates)
            {
                path.Add(signed);
                if (TryRealizeFrom(pitch + signed, index + 1, magnitudes, lowMidi, highMidi, allowed, rng, path))
                    return true;
                path.RemoveAt(path.Count - 1);
            }

            return false;
        }

        private static List<int> Shuffle(List<int> values, Random rng)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (values[i], values[j]) = (values[j], values[i]);
            }
            return values;
        }
    }
}
