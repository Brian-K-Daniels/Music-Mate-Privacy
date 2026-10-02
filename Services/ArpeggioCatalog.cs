using musicmate.Models;

namespace musicmate.Services
{
    /// <summary>
    /// Master arpeggio catalog plus level availability rules.  This is intentionally
    /// not wired into Random generation yet; it is the data model that future
    /// PcArpeggios pool selection will read.
    /// </summary>
    public static class ArpeggioCatalog
    {
        public static ArpeggioPattern MajorTriad { get; } = new(
            Id: "major-triad",
            DisplayName: "Major triad",
            Quality: "Major",
            SemitoneIntervals: new[] { 0, 4, 7, 12 },
            DegreeLabels: new[] { "1", "3", "5", "8" },
            FirstAvailableLevel: 1);

        public static ArpeggioPattern MinorTriad { get; } = new(
            Id: "minor-triad",
            DisplayName: "Minor triad",
            Quality: "Minor",
            SemitoneIntervals: new[] { 0, 3, 7, 12 },
            DegreeLabels: new[] { "1", "b3", "5", "8" },
            FirstAvailableLevel: 21);

        public static ArpeggioPattern DominantSeventh { get; } = new(
            Id: "dominant-seventh",
            DisplayName: "Dominant 7th",
            Quality: "Dominant 7th",
            SemitoneIntervals: new[] { 0, 4, 7, 10, 12 },
            DegreeLabels: new[] { "1", "3", "5", "b7", "8" },
            FirstAvailableLevel: 41,
            IncludesSeventh: true);

        public static ArpeggioPattern MajorSeventh { get; } = new(
            Id: "major-seventh",
            DisplayName: "Major 7th",
            Quality: "Major 7th",
            SemitoneIntervals: new[] { 0, 4, 7, 11, 12 },
            DegreeLabels: new[] { "1", "3", "5", "7", "8" },
            FirstAvailableLevel: 51,
            IncludesSeventh: true);

        public static ArpeggioPattern MinorSeventh { get; } = new(
            Id: "minor-seventh",
            DisplayName: "Minor 7th",
            Quality: "Minor 7th",
            SemitoneIntervals: new[] { 0, 3, 7, 10, 12 },
            DegreeLabels: new[] { "1", "b3", "5", "b7", "8" },
            FirstAvailableLevel: 51,
            IncludesSeventh: true);

        public static ArpeggioPattern DiminishedTriad { get; } = new(
            Id: "diminished-triad",
            DisplayName: "Diminished triad",
            Quality: "Diminished",
            SemitoneIntervals: new[] { 0, 3, 6, 12 },
            DegreeLabels: new[] { "1", "b3", "b5", "8" },
            FirstAvailableLevel: 61);

        public static ArpeggioPattern AugmentedTriad { get; } = new(
            Id: "augmented-triad",
            DisplayName: "Augmented triad",
            Quality: "Augmented",
            SemitoneIntervals: new[] { 0, 4, 8, 12 },
            DegreeLabels: new[] { "1", "3", "#5", "8" },
            FirstAvailableLevel: 71);

        public static ArpeggioPattern HalfDiminishedSeventh { get; } = new(
            Id: "half-diminished-seventh",
            DisplayName: "Half-diminished 7th",
            Quality: "Half-diminished 7th",
            SemitoneIntervals: new[] { 0, 3, 6, 10, 12 },
            DegreeLabels: new[] { "1", "b3", "b5", "b7", "8" },
            FirstAvailableLevel: 81,
            IncludesSeventh: true);

        public static ArpeggioPattern DiminishedSeventh { get; } = new(
            Id: "diminished-seventh",
            DisplayName: "Diminished 7th",
            Quality: "Diminished 7th",
            SemitoneIntervals: new[] { 0, 3, 6, 9, 12 },
            DegreeLabels: new[] { "1", "b3", "b5", "bb7", "8" },
            FirstAvailableLevel: 91,
            IncludesSeventh: true);

        public static IReadOnlyList<ArpeggioPattern> All { get; } =
        [
            MajorTriad,
            MinorTriad,
            DominantSeventh,
            MajorSeventh,
            MinorSeventh,
            DiminishedTriad,
            AugmentedTriad,
            HalfDiminishedSeventh,
            DiminishedSeventh
        ];

        public static IReadOnlyList<ArpeggioPattern> GetAvailablePatterns(int level)
        {
            level = Math.Clamp(level, 1, 100);
            return All
                .Where(pattern => pattern.FirstAvailableLevel <= level)
                .ToArray();
        }

        /// <summary>
        /// Quality-only picker label (no tonic). Tonic comes from the Music page Key picker.
        /// </summary>
        public static string QualityLabel(ArpeggioPattern pattern) => pattern.DisplayName;

        /// <summary>
        /// Maps a picker or persisted title to a catalog quality, including legacy
        /// labels that baked in a root (<c>C major triad</c> → Major triad).
        /// </summary>
        public static bool TryResolveQuality(string? label, out ArpeggioPattern pattern)
        {
            pattern = null!;
            var quality = NormalizeQualityLabel(label);
            if (quality == null)
                return false;

            var match = All.FirstOrDefault(p =>
                p.DisplayName.Equals(quality, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                return false;

            pattern = match;
            return true;
        }

        /// <summary>Canonical quality name, or null when <paramref name="label"/> is not an arpeggio title.</summary>
        public static string? NormalizeQualityLabel(string? label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return null;

            var text = label.Trim();
            if (text.StartsWith("★ ", StringComparison.Ordinal))
                text = text[2..].Trim();

            if (TryMatchDisplayName(text, out var exact))
                return exact.DisplayName;

            var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && IsPitchClassToken(parts[0]) && TryMatchDisplayName(parts[1], out var fromRoot))
                return fromRoot.DisplayName;

            return null;
        }

        private static bool TryMatchDisplayName(string text, out ArpeggioPattern pattern)
        {
            var match = All.FirstOrDefault(p =>
                p.DisplayName.Equals(text, StringComparison.OrdinalIgnoreCase));
            pattern = match!;
            return match != null;
        }

        private static bool IsPitchClassToken(string token)
            => token is "C" or "C#" or "Cb" or "D" or "D#" or "Db" or "E" or "E#" or "Eb"
                or "F" or "F#" or "Fb" or "G" or "G#" or "Gb" or "A" or "A#" or "Ab"
                or "B" or "B#" or "Bb";

        public static ArpeggioLevelAvailability GetAvailabilityForLevel(int level)
        {
            level = Math.Clamp(level, 1, 100);
            return new ArpeggioLevelAvailability(
                Level: level,
                Patterns: GetAvailablePatterns(level),
                RootOptions: GetRootOptionsForLevel(level),
                AllowsInversions: level >= 71,
                MaxOctaves: level >= 81 ? 2 : 1,
                Summary: SummaryForLevel(level));
        }

        private static IReadOnlyList<ArpeggioRootOption> GetRootOptionsForLevel(int level)
        {
            if (level <= 20)
                return
                [
                    new(1, "Tonic (I/i)", 50),
                    new(4, "Subdominant (IV/iv)", 25),
                    new(5, "Dominant (V)", 25)
                ];

            if (level <= 40)
                return
                [
                    new(1, "Tonic (I/i)", 60),
                    new(4, "Subdominant (IV/iv)", 20),
                    new(5, "Dominant (V)", 20)
                ];

            if (level <= 60)
                return
                [
                    new(1, "Tonic (I/i)", 45),
                    new(4, "Subdominant (IV/iv)", 20),
                    new(5, "Dominant (V)", 25),
                    new(6, "Submediant (vi/VI)", 10)
                ];

            if (level <= 80)
                return
                [
                    new(1, "Tonic (I/i)", 35),
                    new(2, "Supertonic (ii/II)", 10),
                    new(4, "Subdominant (IV/iv)", 15),
                    new(5, "Dominant (V)", 25),
                    new(6, "Submediant (vi/VI)", 15)
                ];

            return
            [
                new(1, "Tonic (I/i)", 25),
                new(2, "Supertonic (ii/II)", 15),
                new(3, "Mediant (iii/III)", 10),
                new(4, "Subdominant (IV/iv)", 15),
                new(5, "Dominant (V)", 20),
                new(6, "Submediant (vi/VI)", 10),
                new(7, "Leading-tone/Subtonic (vii/VII)", 5)
            ];
        }

        private static string SummaryForLevel(int level) => level switch
        {
            <= 20 => "Primary-root major triads, one octave.",
            <= 40 => "Major/minor triads on primary roots.",
            <= 60 => "Adds seventh-chord colors on common roots.",
            <= 80 => "Adds altered triads and inversions for advanced levels.",
            _ => "Full arpeggio catalog, wider roots, and two-octave eligibility."
        };
    }
}
