#if DEBUG
using System.Diagnostics;
using System.Text;

namespace musicmate.Services
{
    /// <summary>Debug self-checks for level-based scale pools and weighted random picks.</summary>
    internal static class ChildLevelScaleSelectionTests
    {
        private static int _testsRun;

        public static void RunSelfChecks()
        {
            if (Interlocked.CompareExchange(ref _testsRun, 1, 0) != 0)
                return;

            var sb = new StringBuilder();
            sb.AppendLine("[ScaleLevel] Self-check START");

            AssertLevel1RandomOnlyPentatonic(sb);
            AssertLevel10RandomPool(sb);
            AssertLevel60RandomPool(sb);
            AssertLevel70HasMixolydianNotDorian(sb);
            AssertLevel80HasDorian(sb);
            AssertLevel90HasLydianPhrygianNotEnigmatic(sb);
            AssertLevel95AllowsAll(sb);
            AssertLevelChangeResetsDisallowedNamed(sb);

            sb.AppendLine("[ScaleLevel] Self-check END");
            Utilities.DebugTestLog.Write(sb.ToString());
        }

        private static void AssertLevel1RandomOnlyPentatonic(StringBuilder sb)
        {
            var allowed = ChildLevelProgression.GetAllowedScalesForLevel(1);
            AssertContains(allowed, "Major Pentatonic", sb, "L1 allowed");
            AssertCount(allowed, 1, sb, "L1 allowed count");

            var forbidden = new[]
            {
                "Major", "Natural Minor", "Blues", "Dorian", "Mixolydian",
                "Lydian", "Phrygian", "Locrian", "Enigmatic"
            };
            foreach (var scale in forbidden)
                AssertNotIn(allowed, scale, sb, "L1 forbidden");

            for (int i = 0; i < 50; i++)
            {
                var pick = ChildLevelProgression.PickWeightedRandomScale(1, new Random(i));
                AssertEqual(pick, "Major Pentatonic", sb, "L1 random pick");
            }
        }

        private static void AssertLevel10RandomPool(StringBuilder sb)
        {
            var allowed = ChildLevelProgression.GetAllowedScalesForLevel(10);
            AssertContains(allowed, "Major Pentatonic", sb, "L10");
            AssertContains(allowed, "Major", sb, "L10");
            AssertCount(allowed, 2, sb, "L10 allowed count");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < 100; i++)
                seen.Add(ChildLevelProgression.PickWeightedRandomScale(10, new Random(i + 100)));
            AssertTrue(seen.SetEquals(allowed), sb, "L10 random only from allowed");
        }

        private static void AssertLevel60RandomPool(StringBuilder sb)
        {
            var allowed = ChildLevelProgression.GetAllowedScalesForLevel(60);
            foreach (var scale in new[]
                     {
                         "Major", "Natural Minor", "Harmonic Minor", "Melodic Minor",
                         "Minor Pentatonic", "Blues"
                     })
                AssertContains(allowed, scale, sb, "L60");

            foreach (var scale in new[] { "Dorian", "Lydian", "Phrygian", "Locrian", "Enigmatic" })
                AssertNotIn(allowed, scale, sb, "L60 forbidden");
        }

        private static void AssertLevel70HasMixolydianNotDorian(StringBuilder sb)
        {
            var allowed = ChildLevelProgression.GetAllowedScalesForLevel(70);
            AssertContains(allowed, "Mixolydian", sb, "L70");
            AssertNotIn(allowed, "Dorian", sb, "L70 no Dorian");
            AssertNotIn(allowed, "Lydian", sb, "L70 no Lydian");
        }

        private static void AssertLevel80HasDorian(StringBuilder sb)
        {
            var allowed = ChildLevelProgression.GetAllowedScalesForLevel(80);
            AssertContains(allowed, "Dorian", sb, "L80");
        }

        private static void AssertLevel90HasLydianPhrygianNotEnigmatic(StringBuilder sb)
        {
            var allowed = ChildLevelProgression.GetAllowedScalesForLevel(90);
            AssertContains(allowed, "Lydian", sb, "L90");
            AssertContains(allowed, "Phrygian", sb, "L90");
            if (NoteSessionService.AvailableScales.Contains("Enigmatic"))
                AssertNotIn(allowed, "Enigmatic", sb, "L90 no Enigmatic");
        }

        private static void AssertLevel95AllowsAll(StringBuilder sb)
        {
            var allowed = ChildLevelProgression.GetAllowedScalesForLevel(95);
            foreach (var scale in NoteSessionService.AvailableScales)
            {
                if (scale == "Chromatic")
                    continue;
                AssertContains(allowed, scale, sb, "L95");
            }
        }

        private static void AssertLevelChangeResetsDisallowedNamed(StringBuilder sb)
        {
            var session = new NoteSessionService();
            session.ChildLevel = 95;
            session.ScaleSelectionMode = ScaleSelectionMode.Named;
            session.SelectedScale = "Locrian";
            session.ChildLevel = 10;
            session.ApplyScaleSelectionOnLevelChange(10);
            AssertEqual(session.ScaleSelectionMode, ScaleSelectionMode.ByLevel, sb, "level down mode");
            AssertContains(ChildLevelProgression.GetAllowedScalesForLevel(10), session.SelectedScale, sb, "level down scale");
        }

        private static void AssertContains(IReadOnlyList<string> list, string item, StringBuilder sb, string ctx)
        {
            if (!list.Contains(item, StringComparer.Ordinal))
                sb.AppendLine($"[ScaleLevel] FAIL {ctx}: expected '{item}' in [{string.Join(",", list)}]");
        }

        private static void AssertNotIn(IReadOnlyList<string> list, string item, StringBuilder sb, string ctx)
        {
            if (list.Contains(item, StringComparer.Ordinal))
                sb.AppendLine($"[ScaleLevel] FAIL {ctx}: '{item}' should not be allowed");
        }

        private static void AssertCount(IReadOnlyList<string> list, int expected, StringBuilder sb, string ctx)
        {
            if (list.Count != expected)
                sb.AppendLine($"[ScaleLevel] FAIL {ctx}: expected {expected} scales, got {list.Count}");
        }

        private static void AssertEqual<T>(T a, T b, StringBuilder sb, string ctx)
        {
            if (!EqualityComparer<T>.Default.Equals(a, b))
                sb.AppendLine($"[ScaleLevel] FAIL {ctx}: expected '{b}', got '{a}'");
        }

        private static void AssertTrue(bool condition, StringBuilder sb, string ctx)
        {
            if (!condition)
                sb.AppendLine($"[ScaleLevel] FAIL {ctx}");
        }
    }
}
#else
namespace musicmate.Services
{
    internal static class ChildLevelScaleSelectionTests
    {
        public static void RunSelfChecks() { }
    }
}
#endif
