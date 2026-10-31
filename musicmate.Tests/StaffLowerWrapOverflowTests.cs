using System.Reflection;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;
using Xunit.Abstractions;

namespace musicmate.Tests;

/// <summary>
/// Lower-staff wrap must keep every ink mark inside the drawable width.
/// Regression for Music-page screenshots where notes/accidentals ran past the staff end.
/// </summary>
public class StaffLowerWrapOverflowTests
{
    private readonly ITestOutputHelper _out;
    public StaffLowerWrapOverflowTests(ITestOutputHelper output) => _out = output;

    private const float CanvasH = 480f;
    private const float LayoutRightPad = 2f;
    private const float RightMargin = 36f;
    private const float Epsilon = 0.75f;

    public static IEnumerable<object[]> ScreenWidths =>
    [
        [360f],
        [400f],
        [520f],
        [700f],
        [835f],
    ];

    [Theory]
    [MemberData(nameof(ScreenWidths))]
    public void LongDenseTune_WrapsAcrossStaffs_FinalInkInsideWidth(float canvasW)
    {
        var page = BuildDenseAccidentalPage(seed: 47, measures: 8, accPct: 40);
        var report = LaySplitLikeMusicPage(page, canvasW);
        _out.WriteLine(report.Dump);

        Assert.True(report.UpperMaxRight <= report.LayoutRightLimit + Epsilon, report.Dump);
        if (report.LowerNoteCount > 0)
            Assert.True(report.LowerMaxRight <= report.LayoutRightLimit + Epsilon, report.Dump);
    }

    [Theory]
    [MemberData(nameof(ScreenWidths))]
    public void AccidentalNearStaffEnd_DoesNotRunOffEdge(float canvasW)
    {
        var page = BuildDenseAccidentalPage(seed: 2, measures: 8, accPct: 55, key: "F#", scale: "Major");
        var report = LaySplitLikeMusicPage(page, canvasW);
        _out.WriteLine(report.Dump);

        Assert.True(report.UpperHasAccidental || report.LowerHasAccidental,
            "Expected at least one body accidental in the sample");
        Assert.True(report.UpperMaxRight <= report.LayoutRightLimit + Epsilon, report.Dump);
        if (report.LowerNoteCount > 0)
            Assert.True(report.LowerMaxRight <= report.LayoutRightLimit + Epsilon, report.Dump);
        Assert.True(report.UpperAccidentalMaxRight <= report.LayoutRightLimit + Epsilon, report.Dump);
        Assert.True(report.LowerAccidentalMaxRight <= report.LayoutRightLimit + Epsilon, report.Dump);
    }

    [Theory]
    [MemberData(nameof(ScreenWidths))]
    public void WrapPreservesOrder_NoLostOrDuplicatedNotes(float canvasW)
    {
        var page = BuildDenseAccidentalPage(seed: 11, measures: 10, accPct: 35);
        var bars = BarsFor(page);
        var drawable = NewDrawable(40);
        var split = drawable.SplitMeasuresAcrossStaves(page, bars, canvasW, CanvasH);

        var placed = split.UpperNotes.Concat(split.LowerNotes).Concat(split.UnplacedNotes).ToList();
        Assert.Equal(page.Count, placed.Count);
        Assert.Equal(
            split.UpperMeasureCount + split.LowerMeasureCount + split.UnplacedMeasureCount,
            split.TotalMeasureCount);

        for (int i = 0; i < page.Count; i++)
        {
            Assert.Equal(page[i].SpelledName, placed[i].SpelledName);
            Assert.Equal(page[i].BeatPosition, placed[i].BeatPosition);
        }

        var upperMax = split.UpperNotes.Count == 0 ? -1 : split.UpperNotes.Max(n => n.MeasureIndex ?? 0);
        var lowerMin = split.LowerNotes.Count == 0 ? int.MaxValue : split.LowerNotes.Min(n => n.MeasureIndex ?? 0);
        var lowerMax = split.LowerNotes.Count == 0 ? -1 : split.LowerNotes.Max(n => n.MeasureIndex ?? 0);
        var unplacedMin = split.UnplacedNotes.Count == 0 ? int.MaxValue : split.UnplacedNotes.Min(n => n.MeasureIndex ?? 0);
        if (split.LowerNotes.Count > 0 && split.UpperNotes.Count > 0)
            Assert.True(lowerMin > upperMax);
        if (split.UnplacedNotes.Count > 0 && split.LowerNotes.Count > 0)
            Assert.True(unplacedMin > lowerMax);
        else if (split.UnplacedNotes.Count > 0 && split.UpperNotes.Count > 0)
            Assert.True(unplacedMin > upperMax);
    }

    [Theory]
    [InlineData(360f)]
    [InlineData(520f)]
    [InlineData(835f)]
    public void SameTune_RendersInsideWidth_AtDifferentScreenWidths(float canvasW)
    {
        var page = BuildDenseAccidentalPage(seed: 19, measures: 8, accPct: 45, key: "Ab", scale: "Major");
        var report = LaySplitLikeMusicPage(page, canvasW);
        _out.WriteLine(report.Dump);

        Assert.Equal(page.Count, report.TotalNotesAccounted);
        Assert.True(report.UpperMaxRight <= report.LayoutRightLimit + Epsilon, report.Dump);
        if (report.LowerNoteCount > 0)
            Assert.True(report.LowerMaxRight <= report.LayoutRightLimit + Epsilon, report.Dump);
    }

    [Fact]
    public void PackNotationScale_TracksDrawTwoStaffScale()
    {
        var page = BuildDenseAccidentalPage(seed: 47, measures: 8, accPct: 40);
        var bars = BarsFor(page);
        var drawable = NewDrawable(40);

        InvokeComputeLayout(drawable, CanvasH, page, Array.Empty<GeneratedNote>());
        float allOnUpperR = ReadNoteHeadR(drawable);

        var split = drawable.SplitMeasuresAcrossStaves(page, bars, 400f, CanvasH);
        Assert.True(split.LowerNotes.Count > 0, "Need a lower staff for this regression");
        float packR = ReadNoteHeadR(drawable);

        InvokeComputeLayout(drawable, CanvasH, split.UpperNotes, split.LowerNotes);
        float drawR = ReadNoteHeadR(drawable);

        _out.WriteLine($"allOnUpper R={allOnUpperR:F2} pack R={packR:F2} draw R={drawR:F2}");
        Assert.True(
            Math.Abs(packR - drawR) <= Math.Max(0.35f, drawR * 0.08f),
            $"Pack notation scale R={packR:F2} must track draw R={drawR:F2} " +
            $"(all-on-upper was R={allOnUpperR:F2})");
    }

    private sealed record LayReport(
        string Dump,
        float LayoutRightLimit,
        float UpperMaxRight,
        float LowerMaxRight,
        float UpperAccidentalMaxRight,
        float LowerAccidentalMaxRight,
        int LowerNoteCount,
        int TotalNotesAccounted,
        bool UpperHasAccidental,
        bool LowerHasAccidental);

    /// <summary>
    /// Mirrors MusicPage: split → shift lower beats → staff bar beats → FinishBeginner.
    /// </summary>
    private LayReport LaySplitLikeMusicPage(List<GeneratedNote> page, float canvasW)
    {
        var bars = BarsFor(page);
        var session = NewSession(40);
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        var split = drawable.SplitMeasuresAcrossStaves(page, bars, canvasW, CanvasH);

        var upper = split.UpperNotes.ToList();
        var lower = ShiftToZero(split.LowerNotes.ToList());
        var upperBars = StaffBarsFromMeasures(upper);
        var lowerBars = StaffBarsFromMeasures(lower);

        drawable.UpperNotes = upper;
        drawable.LowerNotes = lower;
        drawable.UpperBarBeats = upperBars;
        drawable.LowerBarBeats = lowerBars;
        drawable.UpperHasEndBar = lower.Count > 0;
        drawable.InvalidateLayoutCache();

        InvokeComputeLayout(drawable, CanvasH, upper, lower);
        var header = InvokeHeader(drawable, 0f);
        float left = header.LeftMargin;
        float lowerMargin = StaffDrawable.ResolveLowerStaffHeaderMargin(
            header.LeftMargin, header.ClefOnly, showSignaturesOnBothStaffs: true);
        float layoutRight = canvasW - LayoutRightPad;
        float upperUsable = Math.Max(64f, layoutRight - left - RightMargin);
        float lowerUsable = Math.Max(64f, layoutRight - lowerMargin - RightMargin);

        var upperPlan = Plan(drawable, upper, upperBars, upperUsable, left,
            fullHeader: true, final: lower.Count == 0, endSingle: lower.Count > 0);

        double upperBeats = upper.Sum(n => n.BeatDuration);
        double lowerBeats = lower.Sum(n => n.BeatDuration);
        float lowerPlanW = lower.Count == 0
            ? lowerUsable
            : StaffDrawable.ComputeMatchedStaffUsableWidth(
                Math.Max(1f, upperPlan.Total - left), upperBeats, lowerBeats, lowerUsable);

        var lowerPlan = lower.Count == 0
            ? (Notes: Array.Empty<object>() as Array, Bars: Array.Empty<object>() as Array, Total: lowerMargin)
            : Plan(drawable, lower, lowerBars, lowerPlanW, lowerMargin,
                fullHeader: true, final: true, endSingle: false);

        bool expandLower = lower.Count > 0
            && !StaffDrawable.IsShortLowerStaff(upperBeats, lowerBeats)
            && lowerPlanW >= lowerUsable - 0.5f;

        InvokeFinishBeginner(
            drawable, upper, lower,
            upperPlan.Notes, upperPlan.Bars, upperPlan.Total, upperUsable,
            lowerPlan.Notes, lowerPlan.Bars, lowerPlan.Total, lowerUsable,
            left, lowerMargin, layoutRight, expandLower);

        float uMax = GetMaxRight(drawable, upperPlan.Notes, upperPlan.Bars);
        float lMax = lower.Count == 0 ? 0f : GetMaxRight(drawable, lowerPlan.Notes, lowerPlan.Bars);
        var (uAccMax, uHasAcc) = AccidentalExtent(upperPlan.Notes);
        var (lAccMax, lHasAcc) = AccidentalExtent(lowerPlan.Notes);

        string dump =
            $"W={canvasW:F0} split={split.UpperMeasureCount}+{split.LowerMeasureCount}+u{split.UnplacedMeasureCount} " +
            $"notes={upper.Count}/{lower.Count}/{split.UnplacedNotes.Count} " +
            $"uMax={uMax:F1} lMax={lMax:F1} uAcc={uAccMax:F1} lAcc={lAccMax:F1} limit={layoutRight:F1} " +
            $"lowerPlanW={lowerPlanW:F0}/{lowerUsable:F0}";

        return new LayReport(
            dump, layoutRight, uMax, lMax, uAccMax, lAccMax,
            lower.Count,
            upper.Count + lower.Count + split.UnplacedNotes.Count,
            uHasAcc, lHasAcc);
    }

    private static List<GeneratedNote> BuildDenseAccidentalPage(
        int seed, int measures, int accPct, string key = "D", string scale = "Major")
    {
        var gen = new MusicSequenceGenerator
        {
            Key = key,
            Scale = scale,
            LowestNote = "A3",
            HighestNote = "E5",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = measures,
            RhythmVarietyPercent = 55,
            SmallestDuration = NoteDuration.Eighth,
            RestChancePercent = 15,
            AccidentalPercent = accPct,
            SyncopationLevel = SyncopationLevel.None,
            MaxMelodicIntervalSemitones = 5,
            UseScaleOrder = false,
            UseMotifPhrases = true,
            ChildLevel = 40,
            RandomSeed = seed,
        };
        return MusicSequenceGenerator.Flatten(gen.GenerateSequence());
    }

    private static List<double> BarsFor(List<GeneratedNote> page)
    {
        int measures = page.Max(n => n.MeasureIndex ?? 0) + 1;
        return Enumerable.Range(1, Math.Max(0, measures - 1)).Select(i => i * 4.0).ToList();
    }

    private static List<double> StaffBarsFromMeasures(List<GeneratedNote> staffNotes)
    {
        if (staffNotes.Count == 0)
            return new List<double>();
        double origin = staffNotes.Min(n => n.BeatPosition ?? 0);
        double end = staffNotes.Max(n => (n.BeatPosition ?? 0) + n.BeatDuration);
        var bars = new List<double>();
        for (double b = 4.0; b < end - 1e-6; b += 4.0)
        {
            if (b > origin + 1e-6)
                bars.Add(b);
        }
        return bars;
    }

    private static List<GeneratedNote> ShiftToZero(List<GeneratedNote> notes)
    {
        if (notes.Count == 0)
            return notes;
        double shift = notes.Min(n => n.BeatPosition ?? 0);
        for (int i = 0; i < notes.Count; i++)
        {
            var n = notes[i];
            notes[i] = new GeneratedNote
            {
                MidiNumber = n.MidiNumber,
                Letter = n.Letter,
                Octave = n.Octave,
                Accidental = n.Accidental,
                SpelledName = n.SpelledName,
                Duration = n.Duration,
                IsRest = n.IsRest,
                MeasureIndex = n.MeasureIndex,
                BeatPosition = (n.BeatPosition ?? 0) - shift,
            };
        }
        return notes;
    }

    private static NoteSessionService NewSession(int level) => new()
    {
        Instrument = "Concert Pitch",
        Key = "D",
        SelectedScale = "Major",
        MeterTimeSignature = "4/4",
        ChildLevel = level,
        ShowSignaturesOnBothStaffs = true,
        IsRandomMode = true,
        Tune = "Random",
    };

    private static StaffDrawable NewDrawable(int level)
        => new(NewSession(level), new ThemeService(), safeArea: null);

    private static void InvokeComputeLayout(
        StaffDrawable drawable, float height,
        IReadOnlyList<GeneratedNote> upper, IReadOnlyList<GeneratedNote> lower)
    {
        typeof(StaffDrawable)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(m => m.Name == "ComputeLayout" && m.GetParameters().Length == 3)
            .Invoke(drawable, new object[] { height, upper, lower });
    }

    private static float ReadNoteHeadR(StaffDrawable drawable)
    {
        var layout = typeof(StaffDrawable)
            .GetField("_layout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(drawable)!;
        return (float)layout.GetType().GetField("NoteHeadR")!.GetValue(layout)!;
    }

    private static (float LeftMargin, float ClefOnly) InvokeHeader(StaffDrawable drawable, float safeLeft)
    {
        var header = typeof(StaffDrawable)
            .GetMethod("ComputeHeaderMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { safeLeft })!;
        return (
            (float)header.GetType().GetProperty("LeftMargin")!.GetValue(header)!,
            (float)header.GetType().GetProperty("ClefOnlyLeftMargin")!.GetValue(header)!);
    }

    private static (Array Notes, Array Bars, float Total) Plan(
        StaffDrawable drawable,
        List<GeneratedNote> notes,
        List<double> bars,
        float usable,
        float margin,
        bool fullHeader,
        bool final,
        bool endSingle)
    {
        object plan = typeof(StaffDrawable)
            .GetMethod("PlanHorizontalLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { notes, bars, usable, margin, fullHeader, final, endSingle })!;
        return (
            (Array)plan.GetType().GetField("Item1")!.GetValue(plan)!,
            (Array)plan.GetType().GetField("Item2")!.GetValue(plan)!,
            (float)plan.GetType().GetField("Item3")!.GetValue(plan)!);
    }

    private static void InvokeFinishBeginner(
        StaffDrawable drawable,
        List<GeneratedNote> upper,
        List<GeneratedNote> lower,
        Array upperNotes, Array upperBars, float upperTotal, float upperUsable,
        Array lowerNotes, Array lowerBars, float lowerTotal, float lowerUsable,
        float left, float lowerMargin, float layoutRight, bool expandLower)
    {
        typeof(StaffDrawable)
            .GetMethod("FinishBeginnerHorizontalLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[]
            {
                upper, upperNotes, upperBars, upperTotal, upperUsable,
                lower, lowerNotes, lowerBars, lowerTotal, lowerUsable,
                0f, left, lowerMargin, layoutRight,
                40f, 60f, 80f, 140f, 160f, 180f,
                expandLower,
            });
    }

    private static float GetMaxRight(StaffDrawable drawable, Array notes, Array bars)
        => (float)typeof(StaffDrawable)
            .GetMethod("GetLayoutMaxRight", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { notes, bars })!;

    private static (float MaxRight, bool Any) AccidentalExtent(Array notes)
    {
        float max = float.NegativeInfinity;
        bool any = false;
        for (int i = 0; i < notes.Length; i++)
        {
            object lay = notes.GetValue(i)!;
            bool has = (bool)lay.GetType().GetField("HasAccidental")!.GetValue(lay)!;
            if (!has) continue;
            any = true;
            float accX = (float)lay.GetType().GetField("AccidentalX")!.GetValue(lay)!;
            max = Math.Max(max, accX);
        }
        return (any ? max : 0f, any);
    }
}
