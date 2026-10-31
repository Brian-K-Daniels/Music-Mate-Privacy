namespace musicmate.Services
{
    /// <summary>
    /// Applies level-based tune weight restrictions for Other → Assortment by Level composition.
    /// Slider values are desired weights; tune share is scaled by level before normalization.
    /// </summary>
    public static class CompositionLevelWeights
    {
        /// <summary>
        /// Multiplier applied to the tune slider before normalizing category weights.
        /// Levels 1–10: 0; 11–25: 0.25; 26–50: 0.60; 51–100: 1.00.
        /// </summary>
        public static double GetTuneWeightFactor(int level)
        {
            level = Math.Clamp(level, 1, 100);
            if (level <= 10)
                return 0.0;
            if (level <= 25)
                return 0.25;
            if (level <= 50)
                return 0.60;
            return 1.0;
        }

        /// <summary>
        /// Effective integer weights (sum 100) after level tune restriction and normalization.
        /// </summary>
        public static (int Tunes, int Random, int Scales, int Arpeggios) ComputeEffectiveWeights(
            int pcTunes,
            int pcRandom,
            int pcScales,
            int pcArpeggios,
            int level,
            bool applyByLevelTuneRestriction,
            bool hasEligibleTunes,
            bool hasEligibleArpeggios)
        {
            int tuneW = hasEligibleTunes ? pcTunes : 0;
            if (applyByLevelTuneRestriction)
                tuneW = (int)Math.Round(tuneW * GetTuneWeightFactor(level));

            int randomW = pcRandom;
            int scaleW = pcScales;
            int arpW = hasEligibleArpeggios ? pcArpeggios : 0;

            int total = tuneW + randomW + scaleW + arpW;
            if (total <= 0)
            {
                tuneW = hasEligibleTunes ? NoteSessionService.DefaultPcTunes : 0;
                if (applyByLevelTuneRestriction)
                    tuneW = (int)Math.Round(tuneW * GetTuneWeightFactor(level));

                randomW = NoteSessionService.DefaultPcRandom;
                scaleW = NoteSessionService.DefaultPcScales;
                arpW = hasEligibleArpeggios ? NoteSessionService.DefaultPcArpeggios : 0;

                total = tuneW + randomW + scaleW + arpW;
                if (total <= 0)
                    return (0, 100, 0, 0);
            }

            return NormalizeTo100(tuneW, randomW, scaleW, arpW);
        }

        internal static (int Tunes, int Random, int Scales, int Arpeggios) NormalizeTo100(
            int tuneW,
            int randomW,
            int scaleW,
            int arpW)
        {
            int total = tuneW + randomW + scaleW + arpW;
            if (total <= 0)
                return (0, 0, 0, 0);

            var exact = new double[]
            {
                tuneW * 100.0 / total,
                randomW * 100.0 / total,
                scaleW * 100.0 / total,
                arpW * 100.0 / total,
            };

            var floors = new int[4];
            var fractional = new (int Index, double Fraction)[4];
            int sum = 0;
            for (int i = 0; i < 4; i++)
            {
                floors[i] = (int)Math.Floor(exact[i]);
                sum += floors[i];
                fractional[i] = (i, exact[i] - floors[i]);
            }

            int remainder = 100 - sum;
            foreach (var (index, _) in fractional.OrderByDescending(f => f.Fraction).Take(remainder))
                floors[index]++;

            return (floors[0], floors[1], floors[2], floors[3]);
        }
    }
}
