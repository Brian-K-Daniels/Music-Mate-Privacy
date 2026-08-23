using musicmate.Diagnostics;

namespace musicmate.Services
{
    /// <summary>
    /// Level-gated key-signature difficulty and frequency-weighted key selection.
    /// Builds the permitted key list for the level first, then applies sharp/flat balance
    /// and frequency weights only within that list.
    /// </summary>
    public static class KeyDifficultyRules
    {
        /// <summary>Root key names the app may assign (after filtering by level).</summary>
        public static readonly IReadOnlySet<string> SupportedKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "C", "G", "F", "D", "Bb", "A", "Eb", "E", "Ab", "B", "Db", "F#", "Gb", "C#", "Cb", "G#", "A#"
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
            new("Gb", 1),
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

        /// <summary>
        /// Lowest child level at which a signature with <paramref name="difficulty"/> accidentals is permitted.
        /// </summary>
        public static int GetMinimumLevelForKeySignatureDifficulty(int difficulty)
        {
            difficulty = Math.Clamp(difficulty, 0, 7);
            for (int level = 1; level <= 100; level++)
            {
                if (GetMaxKeySignatureDifficulty(level) >= difficulty)
                    return level;
            }

            return 100;
        }

        /// <summary>
        /// Lowest child level at which <paramref name="key"/> + <paramref name="scale"/> is permitted,
        /// based on displayed accidental count (covers enharmonics such as F♯ / G♭).
        /// </summary>
        public static int GetMinimumLevelForKey(string key, string scale)
            => GetMinimumLevelForKeySignatureDifficulty(GetKeySignatureDifficulty(key, scale));

        /// <summary>Absolute sharps/flats in the displayed signature for key + scale.</summary>
        public static int GetKeySignatureDifficulty(string key, string scale)
            => KeySignatureRules.GetAccidentalCount(key, scale);

        public static bool IsKeyAllowedAtLevel(string key, string scale, int level)
        {
            if (string.IsNullOrWhiteSpace(key) || !SupportedKeys.Contains(key) || !IsKeyValidForScale(key, scale))
                return false;

            level = Math.Clamp(level, 1, 100);
            return GetMinimumLevelForKey(key, scale) <= level;
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
        /// Permitted keys for <paramref name="scale"/> at <paramref name="level"/>, with frequency weights.
        /// This is the only pool sharp/flat balancing may draw from.
        /// </summary>
        public static IReadOnlyList<WeightedKeyOption> GetWeightedKeysForScale(int level, string scale)
        {
            level = Math.Clamp(level, 1, 100);
            var permitted = MasterKeyFrequencyWeights
                .Where(o => o.Weight > 0 && IsKeyAllowedAtLevel(o.Key, scale, level))
                .ToArray();

            return permitted.Length > 0
                ? permitted
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
        /// Picks a key from the level-permitted list only, with 50% flat vs sharp balance
        /// applied inside that list (never picks first and downgrades afterward).
        /// </summary>
        public static string PickBalancedKeyForSignature(string scale, int level, Random? rng = null)
        {
            level = Math.Clamp(level, 1, 100);
            rng ??= Random.Shared;

            // 1) Permitted keys for this level + scale — only source for selection.
            var permitted = GetWeightedKeysForScale(level, scale);

            // 2) Sharp/flat balance only within the permitted list.
            var flatOptions = new List<WeightedKeyOption>();
            var sharpOptions = new List<WeightedKeyOption>();
            var naturalOptions = new List<WeightedKeyOption>();

            foreach (var option in permitted)
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

            string picked;
            if (primary.Count > 0)
                picked = WeightedChoice.Pick(primary, o => o.Weight, rng).Key;
            else if (fallback.Count > 0)
                picked = WeightedChoice.Pick(fallback, o => o.Weight, rng).Key;
            else if (naturalOptions.Count > 0)
                picked = WeightedChoice.Pick(naturalOptions, o => o.Weight, rng).Key;
            else
                picked = GetFallbackKeyOption(level, scale).Key;

            // Safety: never return a key outside the permitted list.
            if (!IsKeyAllowedAtLevel(picked, scale, level))
                return GetFallbackKeyOption(level, scale).Key;

            return picked;
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

        /// <summary>
        /// Final gate before music generation: if <paramref name="key"/> is not allowed at
        /// <paramref name="level"/>, replace it with a permitted key and log the rejection.
        /// </summary>
        public static string EnsureKeyAllowedAtLevel(
            string key,
            string scale,
            int level,
            Random? rng = null)
        {
            level = Math.Clamp(level, 1, 100);
            if (IsKeyAllowedAtLevel(key, scale, level))
                return key;

            string replacement = PickBalancedKeyForSignature(scale, level, rng);
            DebugLog.WriteLine(
                $"[KeyDifficulty] Rejected key '{key}' for {scale} at L{level} " +
                $"(difficulty={GetKeySignatureDifficulty(key, scale)}, " +
                $"minLevel={GetMinimumLevelForKey(key, scale)}); using '{replacement}'");
            return replacement;
        }

        private static WeightedKeyOption GetFallbackKeyOption(int level, string scale)
        {
            if (IsKeyAllowedAtLevel("C", scale, level))
                return new WeightedKeyOption("C", 1);
            if (IsKeyAllowedAtLevel("A", scale, level))
                return new WeightedKeyOption("A", 1);

            // Avoid recursion through GetWeightedKeysForScale when the pool is empty.
            foreach (var option in MasterKeyFrequencyWeights)
            {
                if (option.Weight > 0 && IsKeyAllowedAtLevel(option.Key, scale, level))
                    return option;
            }

            return new WeightedKeyOption("C", 1);
        }
    }
}
