using System.Reflection;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

public class RestInkLayoutTests
{
    private const float HorizontalCompressFloor = 0.85f;

    [Fact]
    public void RestInkWidth_MatchesDrawerFormula()
    {
        const float sls = 12f;
        const float scale = 0.72f;
        float width = SmuFLRestDrawer.GetInkWidth(sls, scale);
        Assert.Equal(sls * 4.4f * scale, width, precision: 3);
        Assert.True(width > sls, "Rest ink must be wider than one staff space (notehead-sized packing was wrong).");
    }

    [Fact]
    public void BeamedInternalInkGap_IsReadableNotTwoPixels()
    {
        float gap = (float)typeof(StaffDrawable)
            .GetField("BeamedInternalInkGap", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        Assert.True(gap >= 6f, $"BeamedInternalInkGap={gap} must be readable (was 2px).");
    }

    [Theory]
    [InlineData(100f, 200f, 180f, 1.0f)] // minSpan > available → allow overflow, do not crush
    [InlineData(200f, 200f, 100f, 1.0f)]
    [InlineData(150f, 200f, 100f, 0.85f)]
    public void MeasureFitScale_NeverCrushesBelowReadableFloor(
        float available, float span, float minSpan, float expected)
    {
        float scale = (float)typeof(StaffDrawable)
            .GetMethod("ComputeMeasureFitScale", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { available, span, minSpan })!;

        Assert.Equal(expected, scale, precision: 2);
        Assert.True(scale <= 1f + 1e-3f);
        if (minSpan <= available + 0.5f)
            Assert.True(scale >= HorizontalCompressFloor - 1e-3f);
    }

    public static IEnumerable<object[]> AccidentalPercents()
    {
        foreach (int acc in new[] { 0, 10, 50, 100 })
            yield return new object[] { acc };
    }

    private static MusicSequenceGenerator MakeGen(
        int seed, int accPct, string key, string scale, int childLevel, NoteDuration smallest)
        => new()
        {
            Key = key,
            Scale = scale,
            LowestNote = "A3",
            HighestNote = "E5",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 8,
            RhythmVarietyPercent = childLevel <= 35 ? 50 : 70,
            SmallestDuration = smallest,
            RestChancePercent = childLevel <= 35 ? 18 : 22,
            AccidentalPercent = accPct,
            SyncopationLevel = SyncopationLevel.None,
            MaxMelodicIntervalSemitones = 4,
            UseScaleOrder = false,
            UseMotifPhrases = true,
            ChildLevel = childLevel,
            RandomSeed = seed,
        };

    private static List<double> BarsForMeasures(int measureCount, double beatsPerMeasure)
    {
        var bars = new List<double>();
        for (double b = beatsPerMeasure; b < measureCount * beatsPerMeasure - 1e-6; b += beatsPerMeasure)
            bars.Add(b);
        return bars;
    }

    private static void AssertNoSilentMeasureLoss(
        StaffDrawable.StaffMeasureSplitResult split,
        int generatedMeasureCount,
        int generatedEventCount)
    {
        Assert.Equal(generatedMeasureCount, split.TotalMeasureCount);
        Assert.Equal(
            split.UpperMeasureCount + split.LowerMeasureCount + split.UnplacedMeasureCount,
            split.TotalMeasureCount);
        Assert.Equal(
            generatedEventCount,
            split.UpperNotes.Count + split.LowerNotes.Count + split.UnplacedNotes.Count);
    }

    private static void AssertNoSharedMeasures(StaffDrawable.StaffMeasureSplitResult split)
    {
        var upperMs = split.UpperNotes.Select(n => n.MeasureIndex ?? -1).ToHashSet();
        var lowerMs = split.LowerNotes.Select(n => n.MeasureIndex ?? -1).ToHashSet();
        var unplacedMs = split.UnplacedNotes.Select(n => n.MeasureIndex ?? -1).ToHashSet();
        Assert.Empty(upperMs.Intersect(lowerMs));
        Assert.Empty(upperMs.Intersect(unplacedMs));
        Assert.Empty(lowerMs.Intersect(unplacedMs));
    }

    private static void AssertNoInkOverlap(StaffDrawable drawable, List<GeneratedNote> notes, double beatsPerMeasure = 4.0)
    {
        if (notes.Count < 2)
            return;

        double origin = notes.Min(n => n.BeatPosition ?? 0);
        double end = notes.Max(n => (n.BeatPosition ?? 0) + n.BeatDuration);
        var bars = new List<double>();
        for (double b = origin + beatsPerMeasure; b < end - 1e-6; b += beatsPerMeasure)
            bars.Add(b);

        typeof(StaffDrawable)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(m => m.Name == "ComputeLayout" && m.GetParameters().Length == 3)
            .Invoke(drawable, new object[]
            {
                360f,
                (IReadOnlyList<GeneratedNote>)notes,
                (IReadOnlyList<GeneratedNote>)new List<GeneratedNote>(),
            });

        object plan = typeof(StaffDrawable)
            .GetMethod("PlanHorizontalLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { notes, bars, 500f, 100f, true, true, true })!;

        var layouts = (Array)plan.GetType().GetField("Item1")!.GetValue(plan)!;
        var trail = typeof(StaffDrawable).GetMethod(
            "NoteTrailingRight",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { typeof(GeneratedNote), typeof(float) },
            null)!;
        var groupLeft = typeof(StaffDrawable).GetMethod(
            "NoteGroupLeftFromLayout",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        float prevRight = float.NegativeInfinity;
        var order = Enumerable.Range(0, notes.Count)
            .OrderBy(i => notes[i].BeatPosition ?? 0)
            .ThenBy(i => i)
            .ToList();
        for (int oi = 0; oi < order.Count; oi++)
        {
            int i = order[oi];
            var lay = layouts.GetValue(i)!;
            float x = (float)lay.GetType().GetField("X")!.GetValue(lay)!;
            float left = (float)groupLeft.Invoke(drawable, new object[] { lay })!;
            // Duration-midpoint placement can put a long note's center left of a later short
            // onset; require ink boxes not to overlap in beat order.
            if (prevRight > float.NegativeInfinity)
                Assert.True(left + 0.5f >= prevRight, $"Ink overlap: left={left:0.#} prevRight={prevRight:0.#}");

            prevRight = (float)trail.Invoke(drawable, new object[] { notes[i], x })!;
        }
    }

    [Fact]
    public void DenseSeed47_PlanKeepsReadableCenterGaps()
    {
        var gen = MakeGen(47, 10, "D", "Major", 45, NoteDuration.Sixteenth);
        gen.MeasureCount = 4;
        var measures = gen.GenerateSequence();
        Assert.All(measures, m =>
            Assert.Equal(16, m.GeneratedNotes.Sum(n => (int)Math.Round(n.Duration.ToBeatValue() * 4))));

        var flat = MusicSequenceGenerator.Flatten(measures).ToList();
        var bars = new List<double> { 4, 8, 12 };
        var session = new NoteSessionService
        {
            Key = "D",
            SelectedScale = "Major",
            MeterTimeSignature = "4/4",
            ChildLevel = 45,
        };
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);

        typeof(StaffDrawable)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(m => m.Name == "ComputeLayout" && m.GetParameters().Length == 3)
            .Invoke(drawable, new object[]
            {
                360f,
                (IReadOnlyList<GeneratedNote>)flat,
                (IReadOnlyList<GeneratedNote>)new List<GeneratedNote>(),
            });

        object plan = typeof(StaffDrawable)
            .GetMethod("PlanHorizontalLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { flat, bars, 420f, 115f, true, true, true })!;

        var layouts = (Array)plan.GetType().GetField("Item1")!.GetValue(plan)!;
        double mStart = 4.0, mEnd = 8.0;
        var xs = new List<float>();
        for (int i = 0; i < flat.Count; i++)
        {
            double bp = flat[i].BeatPosition ?? -1;
            if (bp < mStart - 1e-6 || bp >= mEnd - 1e-6)
                continue;
            float x = (float)layouts.GetValue(i)!.GetType().GetField("X")!.GetValue(layouts.GetValue(i)!)!;
            xs.Add(x);
        }

        Assert.True(xs.Count >= 2, "Expected dense measure events");
        for (int k = 1; k < xs.Count; k++)
        {
            float dx = xs[k] - xs[k - 1];
            Assert.True(
                dx >= 12f - 0.5f,
                $"Consecutive centers too close after plan: gap[{k - 1}->{k}]={dx:0.#}px");
        }
    }

    [Fact]
    public void WideCanvas_PackResultIsNotCollapsedToOneMeasure()
    {
        // Forensic: Pack estimated ~4 on upper at ~900px; Trim used to collapse to 1.
        var gen = MakeGen(100, 5, "Ab", "Major", 35, NoteDuration.Eighth);
        var flat = MusicSequenceGenerator.Flatten(gen.GenerateSequence()).ToList();
        var bars = BarsForMeasures(8, 4);
        var session = new NoteSessionService
        {
            Key = "Ab",
            SelectedScale = "Major",
            MeterTimeSignature = "4/4",
            ChildLevel = 35,
        };
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        var split = drawable.SplitMeasuresAcrossStaves(flat, bars, canvasWidth: 900f, canvasHeight: 480f);

        AssertNoSilentMeasureLoss(split, generatedMeasureCount: 8, generatedEventCount: flat.Count);
        Assert.True(
            split.UpperMeasureCount >= 2,
            $"Pack must keep multi-measure upper staff; got upper={split.UpperMeasureCount} (Trim regression)");
        Assert.True(
            split.UpperMeasureCount + split.LowerMeasureCount >= 3,
            $"Expected several placed measures on wide canvas; upper={split.UpperMeasureCount} lower={split.LowerMeasureCount}");
    }

    [Theory]
    [MemberData(nameof(AccidentalPercents))]
    public void SplitMeasures_NeverSilentlyLosesGeneratedMusic(int accPct)
    {
        var gen = MakeGen(47 ^ (accPct * 17), accPct, "D", "Major", 45, NoteDuration.Sixteenth);
        var flat = MusicSequenceGenerator.Flatten(gen.GenerateSequence()).ToList();
        Assert.All(
            flat.GroupBy(n => n.MeasureIndex ?? 0),
            g => Assert.Equal(16, g.Sum(n => (int)Math.Round(n.Duration.ToBeatValue() * 4))));

        var bars = BarsForMeasures(8, 4);
        var session = new NoteSessionService
        {
            Key = "D",
            SelectedScale = "Major",
            MeterTimeSignature = "4/4",
            ChildLevel = 45,
            AccidentalPercent = accPct,
        };
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        var split = drawable.SplitMeasuresAcrossStaves(flat, bars, canvasWidth: 360f, canvasHeight: 480f);

        AssertNoSilentMeasureLoss(split, 8, flat.Count);
        AssertNoSharedMeasures(split);

        // Event order preserved across placed+unplaced partition by beat.
        var reconstructed = split.UpperNotes.Concat(split.LowerNotes).Concat(split.UnplacedNotes)
            .OrderBy(n => n.BeatPosition ?? 0).ThenBy(n => n.MeasureIndex ?? 0).ToList();
        var originalOrder = flat.OrderBy(n => n.BeatPosition ?? 0).ThenBy(n => n.MeasureIndex ?? 0).ToList();
        Assert.Equal(originalOrder.Count, reconstructed.Count);
        for (int i = 0; i < originalOrder.Count; i++)
        {
            Assert.Equal(originalOrder[i].BeatPosition, reconstructed[i].BeatPosition);
            Assert.Equal(originalOrder[i].Duration, reconstructed[i].Duration);
            Assert.Equal(originalOrder[i].IsRest, reconstructed[i].IsRest);
        }

        if (split.UpperNotes.Count > 0)
            AssertNoInkOverlap(drawable, split.UpperNotes);
        if (split.LowerNotes.Count > 0)
            AssertNoInkOverlap(drawable, split.LowerNotes);
    }

    [Theory]
    [InlineData("Ab", "Major", 100, 35, "Eighth")]
    [InlineData("Bb", "Natural Minor", 200, 45, "Sixteenth")]
    [InlineData("D", "Major", 47, 45, "Sixteenth")]
    public void SparseFourFour_DoesNotCollapsePackToSingleMeasurePerStaff(
        string key, string scale, int seed, int level, string smallestName)
    {
        var smallest = smallestName == "Sixteenth" ? NoteDuration.Sixteenth : NoteDuration.Eighth;
        var gen = MakeGen(seed, level <= 35 ? 5 : 10, key, scale, level, smallest);
        var flat = MusicSequenceGenerator.Flatten(gen.GenerateSequence()).ToList();
        var bars = BarsForMeasures(8, 4);
        var session = new NoteSessionService
        {
            Key = key,
            SelectedScale = scale,
            MeterTimeSignature = "4/4",
            ChildLevel = level,
        };
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);

        // Phone-landscape-ish width: after Trim removal, Pack should place >1 total when room exists.
        var split = drawable.SplitMeasuresAcrossStaves(flat, bars, canvasWidth: 720f, canvasHeight: 480f);
        AssertNoSilentMeasureLoss(split, 8, flat.Count);
        Assert.True(
            split.UpperMeasureCount + split.LowerMeasureCount >= 2,
            $"{key} {scale}: expected >=2 placed measures on 720px; upper={split.UpperMeasureCount} lower={split.LowerMeasureCount} unplaced={split.UnplacedMeasureCount}");
    }

    [Fact]
    public void MixolydianThreeFour_ComparisonCase_PlacesMultipleMeasures()
    {
        var gen = new MusicSequenceGenerator
        {
            Key = "C",
            Scale = "Mixolydian",
            LowestNote = "C4",
            HighestNote = "C5",
            TimeSignature = TimeSignature.ThreeFour,
            MeasureCount = 8,
            RhythmVarietyPercent = 40,
            SmallestDuration = NoteDuration.Eighth,
            RestChancePercent = 14,
            AccidentalPercent = 0,
            SyncopationLevel = SyncopationLevel.None,
            UseScaleOrder = false,
            UseMotifPhrases = true,
            ChildLevel = 25,
            RandomSeed = 1,
        };
        var measures = gen.GenerateSequence();
        Assert.All(measures, m =>
            Assert.Equal(12, m.GeneratedNotes.Sum(n => (int)Math.Round(n.Duration.ToBeatValue() * 4))));

        var flat = MusicSequenceGenerator.Flatten(measures).ToList();
        var bars = BarsForMeasures(8, 3);
        var session = new NoteSessionService
        {
            Key = "C",
            SelectedScale = "Mixolydian",
            MeterTimeSignature = "3/4",
            ChildLevel = 25,
        };
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        var split = drawable.SplitMeasuresAcrossStaves(flat, bars, canvasWidth: 720f, canvasHeight: 480f);

        AssertNoSilentMeasureLoss(split, 8, flat.Count);
        Assert.True(
            split.UpperMeasureCount >= 2,
            $"C Mixolydian 3/4 should pack several upper measures; got {split.UpperMeasureCount}");
    }
}
