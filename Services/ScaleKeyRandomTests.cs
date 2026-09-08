#if DEBUG
using System.Text;

namespace musicmate.Services
{
    /// <summary>Debug self-checks for fresh scale/key selection when Repeat Same is off.</summary>
    internal static class ScaleKeyRandomTests
    {
        private static int _testsRun;

        public static void RunSelfChecks()
        {
            if (Interlocked.CompareExchange(ref _testsRun, 1, 0) != 0)
                return;

            var sb = new StringBuilder();
            sb.AppendLine("[ScaleKeyRandom] Self-check START");

            AssertRepeatOffRandomUsuallyChangesScaleOrKey(sb);
            AssertRepeatOffNamedKeepsScaleAndKey(sb);
            AssertRepeatOnPreservesScaleAndKey(sb);
            AssertLowLevelPoolsRespected(sb);
            AssertBalancedKeySignatureMixWhenBothBucketsExist(sb);

            sb.AppendLine("[ScaleKeyRandom] Self-check END");
            Utilities.DebugTestLog.Write(sb.ToString());
        }

        private static void AssertRepeatOffRandomUsuallyChangesScaleOrKey(StringBuilder sb)
        {
            var session = new NoteSessionService
            {
                ChildLevel = 42,
                ScaleSelectionMode = ScaleSelectionMode.Random,
                Key = "C",
                SelectedScale = "Major"
            };
            session.SelectedScale = "Major";
            session.PrepareEffectiveScaleForGeneration(0);

            bool anyChange = false;
            for (int seed = 1; seed <= 20; seed++)
            {
                session.PrepareFreshScaleAndKeyForGeneration("GoButton", repeatSame: false, seed);
                if (session.EffectiveScale != "Major" || session.Key != "C")
                    anyChange = true;
            }

            AssertTrue(anyChange, sb, "Random mode GO usually changes scale or key");
        }

        private static void AssertRepeatOffNamedKeepsScaleAndKey(StringBuilder sb)
        {
            // Named + Selected Scale intentionally keeps the user's key (What To Play choice).
            // Only Assortment by Level / Random redraw keys from the level pool.
            var session = new NoteSessionService
            {
                ChildLevel = 42,
                ScaleSelectionMode = ScaleSelectionMode.Named,
                SelectedScale = "Major",
                Tune = "Selected Scale",
                IsRandomMode = false,
                Key = "C"
            };
            session.SelectedScale = "Major";
            session.PrepareEffectiveScaleForGeneration(0);

            session.PrepareFreshScaleAndKeyForGeneration("GoButton", repeatSame: false, 99);
            AssertEqual(session.EffectiveScale, "Major", sb, "Named scale preserved");
            AssertEqual(session.Key, "C", sb, "Named mode keeps user key");
        }

        private static void AssertRepeatOnPreservesScaleAndKey(StringBuilder sb)
        {
            var session = new NoteSessionService
            {
                ChildLevel = 42,
                ScaleSelectionMode = ScaleSelectionMode.Random,
                Key = "C",
                SelectedScale = "Major"
            };
            session.SelectedScale = "Major";
            session.PrepareEffectiveScaleForGeneration(0);

            session.PrepareFreshScaleAndKeyForGeneration("AutoStart", repeatSame: true, 7);
            AssertEqual(session.EffectiveScale, "Major", sb, "RepeatSame keeps scale");
            AssertEqual(session.Key, "C", sb, "RepeatSame keeps key");
        }

        private static void AssertLowLevelPoolsRespected(StringBuilder sb)
        {
            var session = new NoteSessionService
            {
                ChildLevel = 1,
                ScaleSelectionMode = ScaleSelectionMode.Random,
                Key = "C"
            };

            var allowedScales = ChildLevelProgression.GetAllowedScalesForLevel(1);
            var allowedKeys = ChildLevelProgression.GetAllowedKeys(1);

            for (int seed = 0; seed < 30; seed++)
            {
                session.PrepareFreshScaleAndKeyForGeneration("GoButton", repeatSame: false, seed + 500);
                AssertTrue(allowedScales.Contains(session.EffectiveScale), sb, "L1 scale in pool");
                AssertTrue(allowedKeys.Contains(session.Key), sb, "L1 key in pool");
            }
        }

        private static void AssertBalancedKeySignatureMixWhenBothBucketsExist(StringBuilder sb)
        {
            const int level = 80;
            const string scale = "Major";
            int flatCount = 0;
            int sharpCount = 0;

            for (int seed = 0; seed < 200; seed++)
            {
                string key = ChildLevelProgression.PickBalancedKeyForSignature(scale, level, new Random(seed));
                if (KeySignatureRules.KeySignatureUsesFlats(key, scale))
                    flatCount++;
                else if (KeySignatureRules.GetSignedAccidentalCount(key, scale) > 0)
                    sharpCount++;
            }

            AssertTrue(flatCount > 50 && sharpCount > 50, sb,
                $"L{level} Major balanced keys (~50/50): flats={flatCount}, sharps={sharpCount}");

            // L1 Natural Minor: only A (0 accidentals). Sharp and flat buckets are empty,
            // so selection falls back to the natural bucket — not to inventing flats.
            string l1Minor = ChildLevelProgression.PickBalancedKeyForSignature(
                "Natural Minor", level: 1, new Random(42));
            AssertEqual(l1Minor, "A", sb,
                "L1 Natural Minor falls back to natural when sharp/flat buckets empty");

            // L18 Natural Minor: max difficulty 1 → both D (1 flat) and E (1 sharp) exist.
            // Picks must stay within that band (empty-bucket fallback is not this case).
            bool sawFlat = false;
            bool sawSharp = false;
            for (int seed = 0; seed < 80; seed++)
            {
                string key = ChildLevelProgression.PickBalancedKeyForSignature(
                    "Natural Minor", level: 18, new Random(seed));
                int difficulty = KeyDifficultyRules.GetKeySignatureDifficulty(key, "Natural Minor");
                AssertTrue(difficulty <= 1, sb, "L18 Natural Minor respects max difficulty 1");
                AssertTrue(
                    KeyDifficultyRules.IsKeyAllowedAtLevel(key, "Natural Minor", 18),
                    sb,
                    "L18 Natural Minor pick is level-permitted");
                if (KeySignatureRules.KeySignatureUsesFlats(key, "Natural Minor"))
                    sawFlat = true;
                else if (KeySignatureRules.GetSignedAccidentalCount(key, "Natural Minor") > 0)
                    sawSharp = true;
            }

            AssertTrue(sawFlat && sawSharp, sb,
                "L18 Natural Minor uses both flat and sharp buckets when both exist");
        }

        private static void AssertTrue(bool condition, StringBuilder sb, string label)
        {
            sb.AppendLine(condition
                ? $"[ScaleKeyRandom] PASS {label}"
                : $"[ScaleKeyRandom] FAIL {label}");
        }

        private static void AssertEqual(string actual, string expected, StringBuilder sb, string label)
        {
            AssertTrue(string.Equals(actual, expected, StringComparison.Ordinal), sb, $"{label} ({actual} vs {expected})");
        }
    }
}
#endif
