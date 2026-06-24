using System.Text;

namespace musicmate.Services
{
    /// <summary>
    /// Staged child-level curriculum (1–100).  Each 10-level band mainly introduces
    /// one new idea.  Scale and key are chosen per session from weighted pools.
    /// </summary>
    internal static class ChildLevelProgression
    {
        private static readonly HashSet<string> SupportedKeys = new(StringComparer.Ordinal)
        {
            "C", "G", "F", "D", "Bb", "A", "Eb", "E", "Ab", "B", "Db", "F#"
        };

        private static readonly HashSet<string> SupportedScales =
            new(NoteSessionService.AvailableScales, StringComparer.Ordinal);

        /// <summary>Human-readable band label for Child Practice.</summary>
        public static string GetStageLabel(int level)
        {
            level = Math.Clamp(level, 1, 100);
            return ((level - 1) / 10) switch
            {
                0 => "Beginner",
                1 => "New keys",
                2 => "Rests",
                3 => "Minor & 16ths",
                4 => "Syncopation",
                5 => "Modes & blues",
                6 => "Full syncopation",
                _ => "Mastery"
            };
        }

        /// <summary>One-line description of what this level band is teaching.</summary>
        public static string GetMainFocus(int level)
        {
            level = Math.Clamp(level, 1, 100);
            return ((level - 1) / 10) switch
            {
                0 => "More notes, C major, quarter notes",
                1 => "More keys, longer note values",
                2 => "Rests and natural minor",
                3 => "Minor scales and sixteenth notes",
                4 => "Simple off-beat rhythms",
                5 => "Modes and blues sounds",
                6 => "Full syncopation",
                _ => "Wider range, speed, and chromatic notes"
            };
        }

        public static ChildLevelDifficultyProfile GetProfile(int level)
        {
            level = Math.Clamp(level, 1, 100);
            var range = NoteRangeForLevel(level);
            return new ChildLevelDifficultyProfile
            {
                Level = level,
                ScalePool = ScalePoolForLevel(level),
                KeyPool = KeyPoolForLevel(level),
                LowestNote = range.Lo,
                HighestNote = range.Hi,
                AccidentalPercent = AccidentalPercentForLevel(level),
                StageLabel = GetStageLabel(level),
                MainFocus = GetMainFocus(level),
            };
        }

        /// <summary>Pick one scale and one key from the level's weighted pools.</summary>
        public static (string Scale, string Key) PickScaleAndKey(
            ChildLevelDifficultyProfile profile, Random? rng = null)
        {
            var scale = WeightedChoice.Pick(profile.ScalePool, o => o.Weight, rng).Scale;
            var key = WeightedChoice.Pick(profile.KeyPool, o => o.Weight, rng).Key;
            return (scale, key);
        }

        public static (string Scale, string Key) PickScaleAndKey(int level, Random? rng = null)
            => PickScaleAndKey(GetProfile(level), rng);

        /// <summary>Scale names allowed at this child level.</summary>
        public static IReadOnlySet<string> GetAllowedScales(int level)
        {
            var profile = GetProfile(level);
            return profile.ScalePool
                .Select(o => o.Scale)
                .ToHashSet(StringComparer.Ordinal);
        }

        /// <summary>Key names allowed at this child level.</summary>
        public static IReadOnlySet<string> GetAllowedKeys(int level)
        {
            var profile = GetProfile(level);
            return profile.KeyPool
                .Select(o => o.Key)
                .ToHashSet(StringComparer.Ordinal);
        }

        /// <summary>Highest-weight scale in the level pool (typically Major).</summary>
        public static string GetDefaultScale(int level)
            => GetProfile(level).ScalePool.MaxBy(o => o.Weight).Scale;

        /// <summary>Highest-weight key in the level pool (typically C).</summary>
        public static string GetDefaultKey(int level)
            => GetProfile(level).KeyPool.MaxBy(o => o.Weight).Key;

        /// <summary>Keeps <paramref name="currentScale"/> when allowed; otherwise returns the level default.</summary>
        public static string ValidateScaleForLevel(int level, string? currentScale)
        {
            var allowed = GetAllowedScales(level);
            if (!string.IsNullOrWhiteSpace(currentScale) && allowed.Contains(currentScale))
                return currentScale;
            return GetDefaultScale(level);
        }

        /// <summary>Keeps <paramref name="currentKey"/> when allowed; otherwise returns the level default.</summary>
        public static string ValidateKeyForLevel(int level, string? currentKey)
        {
            var allowed = GetAllowedKeys(level);
            if (!string.IsNullOrWhiteSpace(currentKey) && allowed.Contains(currentKey))
                return currentKey;
            return GetDefaultKey(level);
        }

        /// <summary>True when the level pool contains more than one distinct scale.</summary>
        public static bool LevelHasMultipleScales(int level)
            => GetAllowedScales(level).Count > 1;

        /// <summary>Weighted random scale pick for one generated tune.</summary>
        public static string PickScaleFromPool(int level, Random rng)
            => WeightedChoice.Pick(GetProfile(level).ScalePool, o => o.Weight, rng).Scale;

        /// <summary>
        /// Human-readable report of weighted pools and fixed settings for inspection.
        /// </summary>
        public static string BuildDiagnosticReport(IEnumerable<int>? levels = null)
        {
            levels ??= new[] { 1, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
            var sb = new StringBuilder();
            foreach (int lv in levels)
            {
                var p = GetProfile(lv);
                sb.AppendLine($"=== Level {lv} ({p.StageLabel}) ===");
                sb.AppendLine($"Range: {p.LowestNote}–{p.HighestNote}, Accidental%: {p.AccidentalPercent}");
                sb.AppendLine("Scale pool:");
                foreach (var s in p.ScalePool)
                    sb.AppendLine($"  {s.Scale} (weight {s.Weight})");
                sb.AppendLine("Key pool:");
                foreach (var k in p.KeyPool)
                    sb.AppendLine($"  {k.Key} (weight {k.Weight})");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static IReadOnlyList<WeightedScaleOption> ScalePoolForLevel(int level)
        {
            WeightedScaleOption[] raw = level switch
            {
                <= 20 =>
                [
                    new("Major", 100)
                ],
                <= 35 =>
                [
                    new("Major", 70),
                    new("Natural Minor", 30)
                ],
                <= 50 =>
                [
                    new("Major", 50),
                    new("Natural Minor", 25),
                    new("Major Pentatonic", 15),
                    new("Minor Pentatonic", 10)
                ],
                <= 65 =>
                [
                    new("Major", 40),
                    new("Natural Minor", 25),
                    new("Major Pentatonic", 12),
                    new("Minor Pentatonic", 10),
                    new("Blues", 7),
                    new("Dorian", 3),
                    new("Mixolydian", 3)
                ],
                <= 80 =>
                [
                    new("Major", 32),
                    new("Natural Minor", 22),
                    new("Major Pentatonic", 10),
                    new("Minor Pentatonic", 8),
                    new("Blues", 8),
                    new("Dorian", 5),
                    new("Mixolydian", 5),
                    new("Harmonic Minor", 4),
                    new("Melodic Minor", 3),
                    new("Lydian", 2),
                    new("Phrygian", 1)
                ],
                <= 95 =>
                [
                    new("Major", 25),
                    new("Natural Minor", 18),
                    new("Major Pentatonic", 8),
                    new("Minor Pentatonic", 8),
                    new("Blues", 8),
                    new("Dorian", 7),
                    new("Mixolydian", 7),
                    new("Harmonic Minor", 6),
                    new("Melodic Minor", 5),
                    new("Lydian", 4),
                    new("Phrygian", 3),
                    new("Locrian", 1)
                ],
                _ =>
                [
                    new("Major", 20),
                    new("Natural Minor", 15),
                    new("Major Pentatonic", 7),
                    new("Minor Pentatonic", 7),
                    new("Blues", 8),
                    new("Dorian", 8),
                    new("Mixolydian", 8),
                    new("Harmonic Minor", 7),
                    new("Melodic Minor", 6),
                    new("Lydian", 5),
                    new("Phrygian", 5),
                    new("Locrian", 4)
                ]
            };
            return FilterScales(raw);
        }

        private static IReadOnlyList<WeightedKeyOption> KeyPoolForLevel(int level)
        {
            WeightedKeyOption[] raw = level switch
            {
                <= 20 =>
                [
                    new("C", 60),
                    new("G", 20),
                    new("F", 20)
                ],
                <= 35 =>
                [
                    new("C", 40),
                    new("G", 20),
                    new("F", 20),
                    new("D", 10),
                    new("Bb", 10)
                ],
                <= 50 =>
                [
                    new("C", 30),
                    new("G", 18),
                    new("F", 18),
                    new("D", 12),
                    new("Bb", 12),
                    new("A", 5),
                    new("Eb", 5)
                ],
                <= 65 =>
                [
                    new("C", 24),
                    new("G", 16),
                    new("F", 16),
                    new("D", 12),
                    new("Bb", 12),
                    new("A", 8),
                    new("Eb", 8),
                    new("E", 2),
                    new("Ab", 2)
                ],
                <= 80 =>
                [
                    new("C", 20),
                    new("G", 14),
                    new("F", 14),
                    new("D", 10),
                    new("Bb", 10),
                    new("A", 8),
                    new("Eb", 8),
                    new("E", 5),
                    new("Ab", 5),
                    new("B", 3),
                    new("Db", 3)
                ],
                <= 95 =>
                [
                    new("C", 16),
                    new("G", 12),
                    new("F", 12),
                    new("D", 9),
                    new("Bb", 9),
                    new("A", 7),
                    new("Eb", 7),
                    new("E", 6),
                    new("Ab", 6),
                    new("B", 4),
                    new("Db", 4),
                    new("F#", 2)
                ],
                _ =>
                [
                    new("C", 12),
                    new("G", 10),
                    new("F", 10),
                    new("D", 8),
                    new("Bb", 8),
                    new("A", 7),
                    new("Eb", 7),
                    new("E", 6),
                    new("Ab", 6),
                    new("B", 5),
                    new("Db", 5),
                    new("F#", 3)
                ]
            };
            return FilterKeys(raw);
        }

        private static IReadOnlyList<WeightedScaleOption> FilterScales(IEnumerable<WeightedScaleOption> options)
        {
            var list = options
                .Where(o => o.Weight > 0 && SupportedScales.Contains(o.Scale))
                .ToArray();
            return list.Length > 0 ? list : new[] { new WeightedScaleOption("Major", 1) };
        }

        private static IReadOnlyList<WeightedKeyOption> FilterKeys(IEnumerable<WeightedKeyOption> options)
        {
            var list = options
                .Where(o => o.Weight > 0 && SupportedKeys.Contains(o.Key))
                .ToArray();
            return list.Length > 0 ? list : new[] { new WeightedKeyOption("C", 1) };
        }

        public static int NoteCountForLevel(int level)
        {
            level = Math.Clamp(level, 1, 100);
            int band = (level - 1) / 10;
            int pos = (level - 1) % 10;
            double t = pos / 9.0;
            return band switch
            {
                0 => (int)Math.Round(Lerp(3, 6, t)),
                1 => (int)Math.Round(Lerp(6, 12, t)),
                2 => (int)Math.Round(Lerp(8, 14, t)),
                3 => (int)Math.Round(Lerp(10, 16, t)),
                4 => (int)Math.Round(Lerp(12, 16, t)),
                5 => (int)Math.Round(Lerp(14, 18, t)),
                6 => (int)Math.Round(Lerp(16, 20, t)),
                _ => (int)Math.Round(Lerp(18, 28, t + (band - 7) * 0.15))
            };
        }

        public static (string Lo, string Hi) NoteRangeForLevel(int level)
        {
            level = Math.Clamp(level, 1, 100);
            if (level <= 20) return ("C4", "C5");
            if (level <= 30) return ("B3", "D5");
            if (level <= 40) return ("A3", "E5");
            if (level <= 55) return ("A3", "G5");
            if (level <= 70) return ("G3", "A5");
            return ("E3", "C6");
        }

        public static string SmallestNoteForLevel(int level)
        {
            level = Math.Clamp(level, 1, 100);
            if (level <= 15) return "Quarter";
            if (level <= 35) return "Eighth";
            return "Sixteenth";
        }

        public static int RhythmVarietyPercentForLevel(int level)
        {
            level = Math.Clamp(level, 1, 100);
            if (level <= 10) return 0;
            if (level <= 15) return 25;
            if (level <= 20) return 35;
            if (level <= 30) return 40;
            if (level <= 40) return 50;
            if (level <= 50) return 55;
            if (level <= 60) return 60;
            if (level <= 70) return 70;
            if (level <= 85) return 80;
            return 95;
        }

        public static int RestChancePercentForLevel(int level)
        {
            level = Math.Clamp(level, 1, 100);
            if (level <= 20) return 0;
            if (level <= 30) return 14;
            if (level <= 50) return 18;
            if (level <= 70) return 22;
            return 28;
        }

        public static string SyncopationForLevel(int level)
        {
            level = Math.Clamp(level, 1, 100);
            if (level <= 40) return "None";
            if (level <= 60) return "Simple";
            if (level <= 65) return "Simple";
            return "Full";
        }

        public static int AccidentalPercentForLevel(int level)
        {
            level = Math.Clamp(level, 1, 100);
            if (level <= 30) return 0;
            if (level <= 40) return 5;
            if (level <= 50) return 10;
            if (level <= 60) return 12;
            if (level <= 70) return 15;
            if (level <= 80) return 20;
            if (level <= 90) return 25;
            return 30;
        }

        public static int MaxIntervalForLevel(int level)
        {
            level = Math.Clamp(level, 1, 100);
            if (level <= 10) return 2;
            if (level <= 20) return 3;
            if (level <= 40) return 4;
            if (level <= 60) return 5;
            if (level <= 80) return 6;
            return 8;
        }

        public static int MeasureBatchSizeForLevel(int level, int targetNoteCount)
        {
            int measures = (int)Math.Ceiling(targetNoteCount / 3.5);
            return Math.Clamp(measures, 1, level <= 5 ? 1 : level <= 10 ? 2 : level <= 30 ? 4 : 8);
        }

        private static double Lerp(double a, double b, double t)
            => a + (b - a) * Math.Clamp(t, 0, 1);
    }
}
