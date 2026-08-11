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
        /// True when every consecutive magnitude is unique, within 0…maxAbs, and the first
        /// magnitude is not <paramref name="excludeFirstMagnitude"/> (when provided).
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

            var seen = new HashSet<int>();
            foreach (int mag in magnitudes)
            {
                if (mag < 0 || mag > maxAbsSemitones)
                    return false;
                if (!seen.Add(mag))
                    return false;
            }

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
        /// Picks signed MIDI steps with unique absolute magnitudes (no repeats in the list).
        /// Direction is chosen randomly when both up and down fit.
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
            if (!allowed.Contains(startMidi))
            {
                startMidi = allowed
                    .OrderBy(m => Math.Abs(m - startMidi))
                    .ThenBy(m => m)
                    .First();
            }

            maxAbsSemitones = ResolveMaxAbsIntervalSemitones(maxAbsSemitones);

            var chosen = new List<int>(stepCount);
            var bag = Shuffle(CreateIntervalPool(maxAbsSemitones), rng);
            if (excludeFirstMagnitude is int excl)
                bag.RemoveAll(m => m == excl);

            int bagIndex = 0;
            int pitch = startMidi;
            var usedMags = new HashSet<int>();

            for (int step = 0; step < stepCount; step++)
            {
                if (bagIndex >= bag.Count)
                {
                    var remaining = CreateIntervalPool(maxAbsSemitones)
                        .Where(m => !usedMags.Contains(m))
                        .ToList();
                    if (remaining.Count == 0)
                        break;
                    bag = Shuffle(remaining, rng);
                    bagIndex = 0;
                }

                int signed = TakeFittingMagnitude(
                    bag, ref bagIndex, pitch, lowMidi, highMidi, maxAbsSemitones, rng, usedMags, allowed);
                int mag = Math.Abs(signed);
                usedMags.Add(mag);
                chosen.Add(signed);
                pitch += signed;
            }

            return chosen;
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

        /// <summary>True when all absolute magnitudes in the list are unique.</summary>
        public static bool HasNoRepeatsUntilPoolExhausted(
            IReadOnlyList<int> signedIntervals,
            int maxAbsSemitones)
        {
            ArgumentNullException.ThrowIfNull(signedIntervals);
            maxAbsSemitones = ResolveMaxAbsIntervalSemitones(maxAbsSemitones);
            var seen = new HashSet<int>();
            foreach (int d in signedIntervals)
            {
                int mag = Math.Abs(d);
                if (mag > maxAbsSemitones)
                    return false;
                if (!seen.Add(mag))
                    return false;
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

        private static int TakeFittingMagnitude(
            List<int> bag,
            ref int bagIndex,
            int pitch,
            int lowMidi,
            int highMidi,
            int maxAbsSemitones,
            Random rng,
            HashSet<int> usedMags,
            HashSet<int> allowed)
        {
            for (int attempt = bagIndex; attempt < bag.Count; attempt++)
            {
                int mag = bag[attempt];
                if (usedMags.Contains(mag))
                    continue;
                if (!TryResolveSignedDelta(mag, pitch, lowMidi, highMidi, rng, allowed, out int signed))
                    continue;

                (bag[bagIndex], bag[attempt]) = (bag[attempt], bag[bagIndex]);
                bagIndex++;
                return signed;
            }

            var fittingMags = new List<int>();
            foreach (int mag in CreateIntervalPool(maxAbsSemitones))
            {
                if (usedMags.Contains(mag))
                    continue;
                if (TryResolveSignedDelta(mag, pitch, lowMidi, highMidi, rng, allowed, out _))
                    fittingMags.Add(mag);
            }

            if (fittingMags.Count == 0)
            {
                // Last resort: any unused magnitude that fits the hard range (ignore allowed set).
                foreach (int mag in CreateIntervalPool(maxAbsSemitones))
                {
                    if (usedMags.Contains(mag))
                        continue;
                    if (TryResolveSignedDelta(mag, pitch, lowMidi, highMidi, rng, allowed: null, out int signedAny))
                    {
                        bagIndex = bag.Count;
                        return signedAny;
                    }
                }

                bagIndex = bag.Count;
                return 0;
            }

            bag.Clear();
            bag.AddRange(Shuffle(fittingMags, rng));
            bagIndex = 1;
            TryResolveSignedDelta(bag[0], pitch, lowMidi, highMidi, rng, allowed, out int fallback);
            return fallback;
        }

        private static bool TryResolveSignedDelta(
            int magnitude,
            int pitch,
            int lowMidi,
            int highMidi,
            Random rng,
            HashSet<int>? allowed,
            out int signedDelta)
        {
            signedDelta = 0;
            if (magnitude == 0)
            {
                if (pitch < lowMidi || pitch > highMidi)
                    return false;
                if (allowed != null && !allowed.Contains(pitch))
                    return false;
                signedDelta = 0;
                return true;
            }

            bool upOk = pitch + magnitude <= highMidi
                && (allowed == null || allowed.Contains(pitch + magnitude));
            bool downOk = pitch - magnitude >= lowMidi
                && (allowed == null || allowed.Contains(pitch - magnitude));
            if (!upOk && !downOk)
                return false;

            if (upOk && downOk)
                signedDelta = rng.Next(2) == 0 ? magnitude : -magnitude;
            else if (upOk)
                signedDelta = magnitude;
            else
                signedDelta = -magnitude;

            return true;
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
