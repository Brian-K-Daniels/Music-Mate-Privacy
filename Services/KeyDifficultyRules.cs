namespace musicmate.Services
{
    /// <summary>
    /// Level-gated key-signature difficulty and frequency-weighted key selection.
    /// Filters by accidental count first, then applies musical-frequency weights.
    /// </summary>
    public static class KeyDifficultyRules
    {
        /// <summary>Root key names the app may assign (after filtering by level).</summary>
        public static readonly IReadOnlySet<string> SupportedKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "C", "G", "F", "D", "Bb", "A", "Eb", "E", "Ab", "B", "Db", "F#", "C#", "Cb", "G#", "A#"
        };

        /// <summary>Valid major-family signature roots (major, pentatonic, modes).</summary>
        private static readonly HashSet<string> MajorSignatureRoots = new(StringComparer.Ordinal)
        {
            "C", "G", "D", "A", "E", "B", "F#", "C#", "F", "Bb", "Eb", "Ab", "Db", "Gb", "Cb"
        };

        /// <summary>Valid minor-family signature roots (relative-major mapping).</summary>
        private static readonly HashSet<string> MinorSignatureRoots = new(StringComparer.Ordinal)
        {
            "A", "E", "B", "F#", "C#", "G#", "D#", "D", "G", "C", "F", "Bb", "Eb", "Ab", "A#"
        };

        /// <summary>
        /// Relative frequency weights for modern/common keys. Applied only after level filtering.
        /// </summary>
        private static readonly IReadOnlyList<WeightedKeyOption> MasterKeyFrequencyWeights =
        [
            new("C", 60),
            new("G", 20),
            new("F", 20),
            new("D", 10),
            new("Bb", 10),
            new("A", 5),
            new("Eb", 5),
            new("E", 2),
            new("Ab", 2),
            new("B", 1),
            new("Db", 1),
            new("F#", 1),
            new("C#", 1),
            new("Cb", 1),
            new("G#", 1),
            new("A#", 1),
        ];

        /// <summary>Maximum key-signature difficulty (0–7 sharps/flats) allowed at <paramref name="level"/>.</summary>
        public static int GetMaxKeySignatureDifficulty(int level)
        {
            level = Math.Clamp(level, 1, 100);
            return level switch
            {
                <= 10 => 0,
                <= 20 => 1,
                <= 35 => 2,
                <= 50 => 3,
                <= 60 => 4,
                <= 65 => 5,
                <= 70 => 6,
                _ => 7
            };
        }

        /// <summary>Absolute sharps/flats in the displayed signature for key + scale.</summary>
        public static int GetKeySignatureDifficulty(string key, string scale)
            => KeySignatureRules.GetAccidentalCount(key, scale);

        public static bool IsKeyAllowedAtLevel(string key, string scale, int level)
        {
            if (!SupportedKeys.Contains(key) || !IsKeyValidForScale(key, scale))
                return false;

            return GetKeySignatureDifficulty(key, scale) <= GetMaxKeySignatureDifficulty(level);
        }

        /// <summary>True when <paramref name="key"/> is a sensible root for <paramref name="scale"/>.</summary>
        public static bool IsKeyValidForScale(string key, string scale)
        {
            if (KeySignatureRules.ScaleUsesRelativeMajorKeySignature(scale))
                return MinorSignatureRoots.Contains(key);

            return MajorSignatureRoots.Contains(key);
        }

        /// <summary>Master frequency table (before level filtering).</summary>
        public static IReadOnlyList<WeightedKeyOption> GetMasterKeyFrequencyWeights()
            => MasterKeyFrequencyWeights;

        /// <summary>
        /// Keys from the master table that are allowed for <paramref name="scale"/> at
        /// <paramref name="level"/>, preserving frequency weights.
        /// </summary>
        public static IReadOnlyList<WeightedKeyOption> GetWeightedKeysForScale(int level, string scale)
        {
            level = Math.Clamp(level, 1, 100);
            var filtered = MasterKeyFrequencyWeights
                .Where(o => o.Weight > 0 && IsKeyAllowedAtLevel(o.Key, scale, level))
                .ToArray();

            return filtered.Length > 0
                ? filtered
                : [GetFallbackKeyOption(level, scale)];
        }

        /// <summary>
        /// Union of keys allowed for any scale in <paramref name="allowedScales"/> at this level.
        /// </summary>
        public static IReadOnlyList<WeightedKeyOption> BuildKeyPoolForLevel(
            int level,
            IEnumerable<string> allowedScales)
        {
            level = Math.Clamp(level, 1, 100);
            var allowedKeyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (string scale in allowedScales)
            {
                foreach (var option in GetWeightedKeysForScale(level, scale))
                    allowedKeyNames.Add(option.Key);
            }

            var pool = MasterKeyFrequencyWeights
                .Where(o => o.Weight > 0 && allowedKeyNames.Contains(o.Key))
                .ToArray();

            return pool.Length > 0 ? pool : [new WeightedKeyOption("C", 1)];
        }

        /// <summary>Key names allowed at this level (across all scales in the level pool).</summary>
        public static IReadOnlySet<string> GetAllowedKeyNames(int level, IEnumerable<string> allowedScales)
            => BuildKeyPoolForLevel(level, allowedScales)
                .Select(o => o.Key)
                .ToHashSet(StringComparer.Ordinal);

        /// <summary>Key names allowed for one scale at this level.</summary>
        public static IReadOnlySet<string> GetAllowedKeyNamesForScale(int level, string scale)
            => GetWeightedKeysForScale(level, scale)
                .Select(o => o.Key)
                .ToHashSet(StringComparer.Ordinal);

        /// <summary>
        /// Picks a key with 50% flat vs sharp displayed signature, using frequency weights
        /// within the level-allowed pool for <paramref name="scale"/>.
        /// </summary>
        public static string PickBalancedKeyForSignature(string scale, int level, Random? rng = null)
        {
            level = Math.Clamp(level, 1, 100);
            rng ??= Random.Shared;

            var flatOptions = new List<WeightedKeyOption>();
            var sharpOptions = new List<WeightedKeyOption>();
            var naturalOptions = new List<WeightedKeyOption>();

            foreach (var option in GetWeightedKeysForScale(level, scale))
            {
                int signed = KeySignatureRules.GetSignedAccidentalCount(option.Key, scale);
                if (signed < 0)
                    flatOptions.Add(option);
                else if (signed > 0)
                    sharpOptions.Add(option);
                else
                    naturalOptions.Add(option);
            }

            bool wantFlat = rng.Next(2) == 0;
            var primary = wantFlat ? flatOptions : sharpOptions;
            var fallback = wantFlat ? sharpOptions : flatOptions;

            if (primary.Count > 0)
                return WeightedChoice.Pick(primary, o => o.Weight, rng).Key;
            if (fallback.Count > 0)
                return WeightedChoice.Pick(fallback, o => o.Weight, rng).Key;
            if (naturalOptions.Count > 0)
                return WeightedChoice.Pick(naturalOptions, o => o.Weight, rng).Key;

            return GetFallbackKeyOption(level, scale).Key;
        }

        public static string PickWeightedRandomKey(int level, string scale, Random? rng = null)
            => WeightedChoice.Pick(GetWeightedKeysForScale(level, scale), o => o.Weight, rng).Key;

        public static string GetDefaultKey(int level, string scale)
            => GetWeightedKeysForScale(level, scale).MaxBy(o => o.Weight)!.Key;

        public static string ValidateKeyForLevel(int level, string scale, string? currentKey)
        {
            var allowed = GetAllowedKeyNamesForScale(level, scale);
            if (!string.IsNullOrWhiteSpace(currentKey) && allowed.Contains(currentKey))
                return currentKey;
            return GetDefaultKey(level, scale);
        }

        private static WeightedKeyOption GetFallbackKeyOption(int level, string scale)
        {
            if (IsKeyAllowedAtLevel("C", scale, level))
                return new WeightedKeyOption("C", 1);
            if (IsKeyAllowedAtLevel("A", scale, level))
                return new WeightedKeyOption("A", 1);

            var first = GetWeightedKeysForScale(level, scale).FirstOrDefault();
            return first.Key is not null ? first : new WeightedKeyOption("C", 1);
        }
    }
}
