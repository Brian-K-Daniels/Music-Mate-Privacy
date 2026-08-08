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
    [InlineData(100f, 200f, 180f, 0.85f)]
    [InlineData(200f, 200f, 100f, 1.0f)]
    [InlineData(150f, 200f, 100f, 0.85f)]
    public void MeasureFitScale_NeverCrushesBelowReadableFloor(
        float available, float span, float minSpan, float expectedMin)
    {
        float scale = (float)typeof(StaffDrawable)
            .GetMethod("ComputeMeasureFitScale", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { available, span, minSpan })!;

        Assert.True(scale >= expectedMin - 1e-3f, $"scale {scale} < expected floor {expectedMin}");
        Assert.True(scale <= 1f + 1e-3f);
        Assert.True(scale >= HorizontalCompressFloor - 1e-3f);
    }

    public static IEnumerable<object[]> AccidentalPercents()
    {
        foreach (int acc in new[] { 0, 10, 50, 100 })
            yield return new object[] { acc };
    }

    public static IEnumerable<object[]> ScaleKeys()
    {
        yield return new object[] { "D", "Major" };
        yield return new object[] { "A", "Natural Minor" };
        yield return new object[] { "C", "Harmonic Minor" };
        yield return new object[] { "G", "Melodic Minor" };
    }

    private static MusicSequenceGenerator MakeDenseGen(int seed, int accPct, string key, string scale)
        => new()
        {
            Key = key,
            Scale = scale,
            LowestNote = "A3",
            HighestNote = "E5",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 8,
            RhythmVarietyPercent = 70,
            SmallestDuration = NoteDuration.Sixteenth,
            RestChancePercent = 22,
            AccidentalPercent = accPct,
            SyncopationLevel = SyncopationLevel.None,
            MaxMelodicIntervalSemitones = 4,
            UseScaleOrder = false,
            UseMotifPhrases = true,
            ChildLevel = 45,
            RandomSeed = seed,
        };

    [Fact]
    public void DenseSeed47_PlanKeepsReadableCenterGaps()
    {
        var gen = MakeDenseGen(47, 10, "D", "Major");
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

    [Theory]
    [MemberData(nameof(AccidentalPercents))]
    public void SplitMeasures_PacksFewerWhenDense_AcrossAccidentalPercent(int accPct)
    {
        var gen = MakeDenseGen(47 ^ (accPct * 17), accPct, "D", "Major");
        var flat = MusicSequenceGenerator.Flatten(gen.GenerateSequence()).ToList();
        Assert.All(
            flat.GroupBy(n => n.MeasureIndex ?? 0),
            g => Assert.Equal(16, g.Sum(n => (int)Math.Round(n.Duration.ToBeatValue() * 4))));

        double beats = 4.0;
        var bars = new List<double>();
        for (double b = beats; b < 8 * beats - 1e-6; b += beats)
            bars.Add(b);

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

        Assert.True(split.UpperMeasureCount >= 1);
        Assert.True(split.UpperMeasureCount + split.LowerMeasureCount <= split.TotalMeasureCount);
        // Narrow phone width must not force all 8 dense measures onto two staves.
        Assert.True(
            split.UpperMeasureCount + split.LowerMeasureCount < 8
            || split.UnplacedMeasureCount > 0
            || split.TotalMeasureCount < 8,
            $"Expected width packing to reduce measures; upper={split.UpperMeasureCount} lower={split.LowerMeasureCount} unplaced={split.UnplacedMeasureCount}");

        AssertNoSharedMeasures(split);
        AssertPlannedStaffFitsWithoutCrush(drawable, split.UpperNotes, 360f, fullHeader: true);
        if (split.LowerNotes.Count > 0)
            AssertPlannedStaffFitsWithoutCrush(drawable, split.LowerNotes, 360f, fullHeader: false);
    }

    [Theory]
    [MemberData(nameof(ScaleKeys))]
    public void SplitMeasures_WorksForRepresentativeScales(string key, string scale)
    {
        var gen = MakeDenseGen(101, 10, key, scale);
        var flat = MusicSequenceGenerator.Flatten(gen.GenerateSequence()).ToList();
        var bars = new List<double> { 4, 8, 12, 16, 20, 24, 28 };
        var session = new NoteSessionService
        {
            Key = key,
            SelectedScale = scale,
            MeterTimeSignature = "4/4",
            ChildLevel = 45,
        };
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        var split = drawable.SplitMeasuresAcrossStaves(flat, bars, canvasWidth: 400f, canvasHeight: 480f);

        Assert.True(split.UpperMeasureCount >= 1);
        Assert.Equal(
            split.UpperMeasureCount + split.LowerMeasureCount + split.UnplacedMeasureCount,
            split.TotalMeasureCount);
        AssertNoSharedMeasures(split);
    }

    private static void AssertNoSharedMeasures(StaffDrawable.StaffMeasureSplitResult split)
    {
        var upperMs = split.UpperNotes.Select(n => n.MeasureIndex ?? -1).ToHashSet();
        var lowerMs = split.LowerNotes.Select(n => n.MeasureIndex ?? -1).ToHashSet();
        Assert.Empty(upperMs.Intersect(lowerMs));
    }

        private static void AssertPlannedStaffFitsWithoutCrush(
        StaffDrawable drawable,
        List<GeneratedNote> notes,
        float canvasWidth,
        bool fullHeader)
    {
        if (notes.Count == 0)
            return;

        double origin = notes.Min(n => n.BeatPosition ?? 0);
        double end = notes.Max(n => (n.BeatPosition ?? 0) + n.BeatDuration);
        var bars = new List<double>();
        for (double b = origin + 4; b < end - 1e-6; b += 4)
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

        // Mirror SplitMeasuresAcrossStaves usable-width math via header metrics.
        var header = typeof(StaffDrawable)
            .GetMethod("ComputeHeaderMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { 0f })!;
        float leftMargin = fullHeader
            ? (float)header.GetType().GetProperty("LeftMargin")!.GetValue(header)!
            : (float)header.GetType().GetProperty("ClefOnlyLeftMargin")!.GetValue(header)!;
        const float rightMargin = 36f;
        const float barRightPad = 20f;
        float usable = Math.Max(64f, canvasWidth - leftMargin - rightMargin - barRightPad);

        object plan = typeof(StaffDrawable)
            .GetMethod("PlanHorizontalLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { notes, bars, usable, leftMargin, fullHeader, true, true })!;

        float totalW = (float)plan.GetType().GetField("Item3")!.GetValue(plan)!;
        var layouts = (Array)plan.GetType().GetField("Item1")!.GetValue(plan)!;
        var trail = typeof(StaffDrawable).GetMethod(
            "NoteTrailingRight",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { typeof(GeneratedNote), typeof(float) },
            null)!;

        float maxInk = leftMargin;
        for (int i = 0; i < notes.Count; i++)
        {
            float x = (float)layouts.GetValue(i)!.GetType().GetField("X")!.GetValue(layouts.GetValue(i)!)!;
            maxInk = Math.Max(maxInk, (float)trail.Invoke(drawable, new object[] { notes[i], x })!);
        }

        float content = Math.Max(totalW - leftMargin - rightMargin, maxInk - leftMargin);
        // Single unavoidable oversize measure may still exceed the staff; multi-measure must fit.
        int approxMeasures = Math.Max(1, (int)Math.Round(notes.Sum(n => n.BeatDuration) / 4.0));
        if (approxMeasures > 1)
        {
            Assert.True(
                content <= usable + 8f,
                $"Staff content {content:0.#} exceeds usable {usable:0.#} after width packing (measures≈{approxMeasures})");
        }
        var groupLeft = typeof(StaffDrawable).GetMethod(
            "NoteGroupLeftFromLayout",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        float prevRight = float.NegativeInfinity;
        float prevX = float.NegativeInfinity;
        for (int i = 0; i < notes.Count; i++)
        {
            var lay = layouts.GetValue(i)!;
            float x = (float)lay.GetType().GetField("X")!.GetValue(lay)!;
            if (prevX > float.NegativeInfinity)
                Assert.True(x - prevX >= 11.5f, $"Centers too close: dx={x - prevX:0.#}");

            float left = (float)groupLeft.Invoke(drawable, new object[] { lay })!;
            if (prevRight > float.NegativeInfinity)
                Assert.True(left + 0.5f >= prevRight, $"Ink overlap: left={left:0.#} prevRight={prevRight:0.#}");

            prevRight = (float)trail.Invoke(drawable, new object[] { notes[i], x })!;
            prevX = x;

            if ((bool)lay.GetType().GetField("HasAccidental")!.GetValue(lay)!)
            {
                float accX = (float)lay.GetType().GetField("AccidentalX")!.GetValue(lay)!;
                Assert.True(accX < x - 1f, "Accidental must sit left of notehead center");
            }
        }
    }
}