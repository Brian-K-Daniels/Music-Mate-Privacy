using System.Reflection;
using System.Text;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;
using Xunit.Abstractions;

namespace musicmate.Tests;

/// <summary>
/// Permanent regressions for staff-local vs screen-stage separation:
/// PlanHorizontalLayout owns musical geometry; after MapStaffLayoutToScreen only
/// uniform affine fit is allowed (no Strict / PlaceBarLines / Refinish repair).
/// </summary>
[Collection("StaffLayoutDiag")]
public class StaffHorizontalScreenStageTests
{
    private readonly ITestOutputHelper _out;
    public StaffHorizontalScreenStageTests(ITestOutputHelper output) => _out = output;

    private const float CanvasW = 835f;
    private const float CanvasH = 480f;
    private const float SafeLeft = 12f;
    private const float Clearance = 0.75f;

    [Fact]
    public void PlanHorizontalLayout_IsIdempotent()
    {
        var (drawable, upper, available, leftMargin, barBeats) = WarmUpper();
        var a = Plan(drawable, upper, barBeats, available, leftMargin);
        var b = Plan(drawable, upper, barBeats, available, leftMargin);
        AssertSameCoords(a.Notes, b.Notes, a.Bars, b.Bars);
    }

    [Fact]
    public void MapStaffLayoutToScreen_IdenticalLocalGeometryTwice_SameScreenGeometry()
    {
        var (drawable, upper, available, leftMargin, barBeats) = WarmUpper();
        var plan = Plan(drawable, upper, barBeats, available, leftMargin);

        var n1 = Clone(plan.Notes);
        var b1 = Clone(plan.Bars);
        var n2 = Clone(plan.Notes);
        var b2 = Clone(plan.Bars);

        Invoke(drawable, "MapStaffLayoutToScreen", n1, b1, SafeLeft, leftMargin, 1f);
        // Reset screen stage so a second map of a fresh local clone is allowed to run.
        typeof(StaffDrawable)
            .GetField("_horizontalScreenStage", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(drawable, false);
        Invoke(drawable, "MapStaffLayoutToScreen", n2, b2, SafeLeft, leftMargin, 1f);

        AssertSameCoords(n1, n2, b1, b2);
    }

    [Fact]
    public void AfterMap_StructuralSpacingMethodsAreBlocked()
    {
        StaffLayoutDiag.Enabled = true;
        StaffLayoutDiag.Reset();
        try
        {
            var (drawable, upper, available, leftMargin, barBeats) = WarmUpper();
            var plan = Plan(drawable, upper, barBeats, available, leftMargin);
            var notes = Clone(plan.Notes);
            var bars = Clone(plan.Bars);
            double beatOrigin = GetBeatOrigin(upper, barBeats);

            Invoke(drawable, "MapStaffLayoutToScreen", notes, bars, SafeLeft, leftMargin, 1f);
            StaffLayoutDiag.Reset(); // count only post-map attempts
            float[] noteBefore = CopyXs(notes);
            float[] barBefore = CopyXs(bars);

            Invoke(drawable, "EnforceStrictBeatOrderSpacing",
                (IReadOnlyList<GeneratedNote>)upper, notes, beatOrigin, 8f, (IReadOnlyList<double>)barBeats);
            Invoke(drawable, "PlaceBarLinesFromMeasureContent",
                (IReadOnlyList<GeneratedNote>)upper, notes, bars, (IReadOnlyList<double>)barBeats, beatOrigin);
            Invoke(drawable, "RefinishMeasureSpacing", upper, notes, bars, (IReadOnlyList<double>)barBeats, beatOrigin);

            Assert.Equal(noteBefore, CopyXs(notes));
            Assert.Equal(barBefore, CopyXs(bars));
            Assert.Equal(0, StaffLayoutDiag.GetCount("EnforceStrictBeatOrderSpacing"));
            Assert.Equal(0, StaffLayoutDiag.GetCount("PlaceBarLinesFromMeasureContent"));
            Assert.Equal(0, StaffLayoutDiag.GetCount("RefinishMeasureSpacing"));
            Assert.True(StaffLayoutDiag.GetCount("EnforceStrictBeatOrderSpacing_BLOCKED_AFTER_SCREEN_MAP") >= 1);
            Assert.True(StaffLayoutDiag.GetCount("PlaceBarLinesFromMeasureContent_BLOCKED_AFTER_SCREEN_MAP") >= 1);
            Assert.True(StaffLayoutDiag.GetCount("RefinishMeasureSpacing_BLOCKED_AFTER_SCREEN_MAP") >= 1);
        }
        finally
        {
            StaffLayoutDiag.Enabled = false;
            StaffLayoutDiag.Reset();
        }
    }

    [Fact]
    public void PlaceBarLines_AfterPlan_IsNotInvokedAgainByScreenFitPipeline()
    {
        StaffLayoutDiag.Enabled = true;
        StaffLayoutDiag.Reset();
        try
        {
            var (drawable, upper, available, leftMargin, barBeats) = WarmUpper();
            StaffLayoutDiag.Reset();
            var plan = Plan(drawable, upper, barBeats, available, leftMargin);
            int placeAfterPlan = StaffLayoutDiag.GetCount("PlaceBarLinesFromMeasureContent");
            Assert.Equal(1, placeAfterPlan);

            float layoutRight = CanvasW - 24f;
            ScreenFit(drawable, Clone(plan.Notes), Clone(plan.Bars), plan.TotalWidth, leftMargin, layoutRight);

            Assert.Equal(1, StaffLayoutDiag.GetCount("PlaceBarLinesFromMeasureContent"));
            Assert.Equal(0, StaffLayoutDiag.GetCount("PlaceBarLinesFromMeasureContent_BLOCKED_AFTER_SCREEN_MAP"));
        }
        finally
        {
            StaffLayoutDiag.Enabled = false;
            StaffLayoutDiag.Reset();
        }
    }

    [Theory]
    [InlineData(835f)]
    [InlineData(520f)]
    [InlineData(1100f)]
    public void CDiminished_ScreenFit_FitsLayoutRight_NoNoteBarCollisions_OrderedBars(float canvasW)
    {
        // Full-page split + screen fit (same contract as live repack path).
        var page = BuildCDiminishedArpeggio();
        var bars = BuildBarBeats(page, 4);
        var drawable = new StaffDrawable(CreateArpeggioSession(), new ThemeService(), safeArea: null);
        var split = StaffPageWidthPolicy.SplitCachedPage(
            drawable,
            new StaffPagePackState { PageNotes = page, PageBarBeats = bars, MeasureBeats = 4 },
            canvasW,
            CanvasH);

        float layoutRight = canvasW - 24f;
        float minBar = float.PositiveInfinity;
        float maxInk = float.NegativeInfinity;

        foreach (var (notes, useFullHeader) in new[]
        {
            (split.UpperNotes, true),
            (split.LowerNotes, false),
        })
        {
            if (notes.Count == 0) continue;
            var metrics = ScreenFitAndMeasure(drawable, notes, bars, canvasW, useFullHeader, layoutRight);
            minBar = Math.Min(minBar, metrics.MinBarClearance);
            maxInk = Math.Max(maxInk, metrics.MaxInkRight);
        }

        _out.WriteLine($"canvasW={canvasW} U={split.UpperMeasureCount} L={split.LowerMeasureCount} " +
                       $"minBar={minBar:F1} maxInk={maxInk:F1} layoutRight={layoutRight:F1}");

        Assert.True(maxInk <= layoutRight + 0.5f);
        Assert.True(minBar >= 8f - Clearance,
            $"min bar clearance {minBar:F1} at width {canvasW}");
    }

    private static (float MinBarClearance, float MaxInkRight) ScreenFitAndMeasure(
        StaffDrawable drawable,
        List<GeneratedNote> notes,
        List<double> pageBarBeats,
        float canvasW,
        bool useFullHeader,
        float layoutRight)
    {
        typeof(StaffDrawable)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(m => m.Name == "ComputeLayout" && m.GetParameters().Length == 3)
            .Invoke(drawable, new object[]
            {
                CanvasH,
                (IReadOnlyList<GeneratedNote>)notes,
                (IReadOnlyList<GeneratedNote>)Array.Empty<GeneratedNote>(),
            });

        object header = typeof(StaffDrawable)
            .GetMethod("ComputeHeaderMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { SafeLeft })!;
        float leftMargin = useFullHeader
            ? (float)header.GetType().GetProperty("LeftMargin")!.GetValue(header)!
            : (float)header.GetType().GetProperty("ClefOnlyLeftMargin")!.GetValue(header)!;
        float usable = Math.Max(64f, layoutRight - SafeLeft - leftMargin);

        double start = notes.Min(n => n.BeatPosition ?? 0);
        double end = notes.Max(n => (n.BeatPosition ?? 0) + n.BeatDuration);
        var barBeats = pageBarBeats.Where(b => b > start + 1e-6 && b < end - 1e-6).ToList();

        var plan = Plan(drawable, notes, barBeats, usable, leftMargin);
        var noteLayouts = Clone(plan.Notes);
        var barLayouts = Clone(plan.Bars);
        ScreenFit(drawable, noteLayouts, barLayouts, plan.TotalWidth, leftMargin, layoutRight);

        float minBar = float.PositiveInfinity;
        float maxInk = float.NegativeInfinity;
        for (int i = 0; i < noteLayouts.Length; i++)
        {
            float right = InvokeFloat(drawable, "NoteInkRightForLayout", noteLayouts.GetValue(i)!);
            maxInk = Math.Max(maxInk, right);
        }
        for (int b = 0; b < barLayouts.Length - 1; b++)
        {
            float barX = ReadBarX(barLayouts, b);
            for (int i = 0; i < noteLayouts.Length; i++)
            {
                float right = InvokeFloat(drawable, "NoteInkRightForLayout", noteLayouts.GetValue(i)!);
                if (right <= barX + 0.5f)
                    minBar = Math.Min(minBar, barX - right);
            }
        }
        return (minBar, maxInk);
    }

    [Fact]
    public void CDiminished_835_ScreenFit_IsIdempotent()
    {
        var page = BuildCDiminishedArpeggio();
        var bars = BuildBarBeats(page, 4);
        var drawable = new StaffDrawable(CreateArpeggioSession(), new ThemeService(), safeArea: null);
        var split = StaffPageWidthPolicy.SplitCachedPage(
            drawable,
            new StaffPagePackState { PageNotes = page, PageBarBeats = bars, MeasureBeats = 4 },
            CanvasW,
            CanvasH);
        float layoutRight = CanvasW - 24f;

        var n1 = ScreenFitStaffNotes(drawable, split.UpperNotes, bars, layoutRight, out float[] notesA, out float[] barsA);
        _ = n1;
        typeof(StaffDrawable)
            .GetField("_horizontalScreenStage", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(drawable, false);
        ScreenFitStaffNotes(drawable, split.UpperNotes, bars, layoutRight, out float[] notesB, out float[] barsB);

        Assert.Equal(notesA, notesB);
        Assert.Equal(barsA, barsB);
    }

    private static Array ScreenFitStaffNotes(
        StaffDrawable drawable,
        List<GeneratedNote> notes,
        List<double> pageBarBeats,
        float layoutRight,
        out float[] noteXs,
        out float[] barXs)
    {
        typeof(StaffDrawable)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(m => m.Name == "ComputeLayout" && m.GetParameters().Length == 3)
            .Invoke(drawable, new object[]
            {
                CanvasH,
                (IReadOnlyList<GeneratedNote>)notes,
                (IReadOnlyList<GeneratedNote>)Array.Empty<GeneratedNote>(),
            });
        object header = typeof(StaffDrawable)
            .GetMethod("ComputeHeaderMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { SafeLeft })!;
        float leftMargin = (float)header.GetType().GetProperty("LeftMargin")!.GetValue(header)!;
        float usable = Math.Max(64f, layoutRight - SafeLeft - leftMargin);
        double start = notes.Min(n => n.BeatPosition ?? 0);
        double end = notes.Max(n => (n.BeatPosition ?? 0) + n.BeatDuration);
        var barBeats = pageBarBeats.Where(b => b > start + 1e-6 && b < end - 1e-6).ToList();
        var plan = Plan(drawable, notes, barBeats, usable, leftMargin);
        var noteLayouts = Clone(plan.Notes);
        var barLayouts = Clone(plan.Bars);
        ScreenFit(drawable, noteLayouts, barLayouts, plan.TotalWidth, leftMargin, layoutRight);
        noteXs = CopyXs(noteLayouts);
        barXs = CopyXs(barLayouts);
        return noteLayouts;
    }

    private static List<GeneratedNote> BuildCDiminishedArpeggio()
        => new ArpeggioSequenceBuilder
        {
            Key = "C",
            Scale = "Natural Minor",
            LowestNote = "A3",
            HighestNote = "C6",
        }.Build(ArpeggioCatalog.DiminishedTriad, "C4");

    [Fact]
    public void UniformScale_PreservesNormalizedRelativePositions()
    {
        var (drawable, upper, available, leftMargin, barBeats) = WarmUpper();
        var plan = Plan(drawable, upper, barBeats, available, leftMargin);
        var notes = Clone(plan.Notes);
        var bars = Clone(plan.Bars);

        float localLeft = leftMargin;
        float[] localNoteRel = CopyXs(notes).Select(x => x - localLeft).ToArray();
        float[] localBarRel = CopyXs(bars).Select(x => x - localLeft).ToArray();

        const float scale = 0.72f;
        Invoke(drawable, "MapStaffLayoutToScreen", notes, bars, SafeLeft, leftMargin, scale);

        float[] screenNoteRel = CopyXs(notes).Select(x => (x - SafeLeft - leftMargin) / scale).ToArray();
        float[] screenBarRel = CopyXs(bars).Select(x => (x - SafeLeft - leftMargin) / scale).ToArray();

        for (int i = 0; i < localNoteRel.Length; i++)
            Assert.Equal(localNoteRel[i], screenNoteRel[i], precision: 2);
        for (int i = 0; i < localBarRel.Length; i++)
            Assert.Equal(localBarRel[i], screenBarRel[i], precision: 2);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed record FitResult(
        string Dump,
        float LayoutRight,
        float MaxInkRight,
        float MaxBarRight,
        float MinBarClearance,
        bool BarsStrictlyIncreasing,
        bool FourBeatsPerMeasure,
        bool AccidentalsClear,
        bool FinalBarClear,
        bool EventsStayInMeasure);

    private FitResult RunCDimScreenFit(float canvasW)
    {
        var (drawable, upper, available, leftMargin, barBeats) = WarmUpper(canvasW);
        float layoutRight = canvasW - 24f;
        var plan = Plan(drawable, upper, barBeats, available, leftMargin);
        var notes = Clone(plan.Notes);
        var bars = Clone(plan.Bars);
        ScreenFit(drawable, notes, bars, plan.TotalWidth, leftMargin, layoutRight);

        double beatOrigin = GetBeatOrigin(upper, barBeats);
        var sb = new StringBuilder();
        sb.AppendLine($"canvasW={canvasW:F0} available={available:F1} leftMargin={leftMargin:F1} layoutRight={layoutRight:F1}");
        sb.AppendLine($"planTotalWidth={plan.TotalWidth:F1}");

        float maxInk = float.NegativeInfinity;
        float maxBar = float.NegativeInfinity;
        float minClear = float.PositiveInfinity;
        var sortedBarsRel = barBeats.Select(b => b - beatOrigin).OrderBy(b => b).ToList();
        int internalCount = Math.Min(sortedBarsRel.Count, bars.Length - 1);

        for (int i = 0; i < notes.Length; i++)
        {
            float left = InvokeFloat(drawable, "NoteGroupLeftFromLayout", notes.GetValue(i)!);
            float right = InvokeFloat(drawable, "NoteInkRightForLayout", notes.GetValue(i)!);
            maxInk = Math.Max(maxInk, right);

            double relBeat = (upper[i].BeatPosition ?? 0) - beatOrigin;
            int measure = 0;
            for (int b = 0; b < sortedBarsRel.Count; b++)
                if (relBeat + 1e-9 >= sortedBarsRel[b]) measure = b + 1;

            if (measure < internalCount)
            {
                float barX = ReadBarX(bars, measure);
                minClear = Math.Min(minClear, barX - right);
            }
            if (measure > 0 && measure - 1 < internalCount)
            {
                float prev = ReadBarX(bars, measure - 1);
                minClear = Math.Min(minClear, left - prev);
            }

            sb.AppendLine(
                $"note[{i}] {upper[i].SpelledName} m={upper[i].MeasureIndex} " +
                $"X={ReadX(notes, i):F1} inkL={left:F1} inkR={right:F1}");
        }

        for (int i = 0; i < bars.Length; i++)
        {
            float bx = ReadBarX(bars, i);
            maxBar = Math.Max(maxBar, BarLineRightEdge(bars.GetValue(i)!));
            sb.AppendLine($"bar[{i}] X={bx:F1}");
        }

        bool barsInc = true;
        for (int i = 1; i < bars.Length; i++)
            if (ReadBarX(bars, i) <= ReadBarX(bars, i - 1) + 0.01f)
                barsInc = false;

        bool fourBeats = upper.GroupBy(n => n.MeasureIndex ?? 0)
            .All(g => Math.Abs(g.Sum(n => n.BeatDuration) - 4.0) < 1e-3);

        bool accClear = true;
        for (int i = 0; i < notes.Length; i++)
        {
            object lay = notes.GetValue(i)!;
            bool hasAcc = (bool)lay.GetType().GetField("HasAccidental")!.GetValue(lay)!;
            if (!hasAcc) continue;
            float groupLeft = InvokeFloat(drawable, "NoteGroupLeftFromLayout", lay);
            float x = ReadX(notes, i);
            if (groupLeft >= x - 0.5f) accClear = false;
        }

        float endBar = ReadBarX(bars, bars.Length - 1);
        bool finalClear = true;
        for (int i = 0; i < notes.Length; i++)
        {
            float right = InvokeFloat(drawable, "NoteInkRightForLayout", notes.GetValue(i)!);
            if (right > endBar + Clearance) finalClear = false;
        }

        bool eventsInMeasure = true;
        for (int i = 0; i < notes.Length; i++)
        {
            double relBeat = (upper[i].BeatPosition ?? 0) - beatOrigin;
            int expected = 0;
            for (int b = 0; b < sortedBarsRel.Count; b++)
                if (relBeat + 1e-9 >= sortedBarsRel[b]) expected = b + 1;
            float x = ReadX(notes, i);
            float leftBound = expected == 0 ? SafeLeft + leftMargin - 1f : ReadBarX(bars, expected - 1) - 1f;
            float rightBound = expected < internalCount
                ? ReadBarX(bars, expected) + 1f
                : endBar + 1f;
            if (x < leftBound || x > rightBound)
                eventsInMeasure = false;
        }

        if (float.IsPositiveInfinity(minClear))
            minClear = endBar - maxInk;

        sb.AppendLine($"maxInkRight={maxInk:F1} maxBarRight={maxBar:F1} minBarClearance={minClear:F1}");
        return new FitResult(
            sb.ToString(), layoutRight, maxInk, maxBar, minClear,
            barsInc, fourBeats, accClear, finalClear, eventsInMeasure);
    }

    private static void ScreenFit(
        StaffDrawable drawable,
        Array notes,
        Array bars,
        float planTotalWidth,
        float leftMargin,
        float layoutRight)
    {
        // Mirror adult Step 3: optional floor-clamped scale, then exact FitMappedLayoutIntoRightLimit.
        float contentWidth = Math.Max(1f, planTotalWidth - leftMargin);
        float usable = Math.Max(64f, layoutRight - SafeLeft - leftMargin);
        float scale = contentWidth > usable ? usable / contentWidth : 1f;
        scale = Math.Clamp(scale, StaffDrawable.MinimumSafeHorizontalScale, 1f);
        Invoke(drawable, "MapStaffLayoutToScreen", notes, bars, SafeLeft, leftMargin, scale);
        Invoke(drawable, "FitMappedLayoutIntoRightLimit", notes, bars, layoutRight, SafeLeft, leftMargin);
    }

    private static (StaffDrawable drawable, List<GeneratedNote> upper, float available, float leftMargin, List<double> barBeats)
        WarmUpper(float canvasW = CanvasW)
    {
        var session = CreateArpeggioSession();
        var upper = BuildCDiminishedArpeggio().Where(n => (n.MeasureIndex ?? 0) < 4).ToList();
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        typeof(StaffDrawable)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(m => m.Name == "ComputeLayout" && m.GetParameters().Length == 3)
            .Invoke(drawable, new object[]
            {
                CanvasH,
                (IReadOnlyList<GeneratedNote>)upper,
                (IReadOnlyList<GeneratedNote>)Array.Empty<GeneratedNote>(),
            });

        object header = typeof(StaffDrawable)
            .GetMethod("ComputeHeaderMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { SafeLeft })!;
        float leftMargin = (float)header.GetType().GetProperty("LeftMargin")!.GetValue(header)!;
        float available = Math.Max(64f, canvasW - 24f - SafeLeft - leftMargin);
        return (drawable, upper, available, leftMargin, BuildBarBeats(upper, 4));
    }

    private static (Array Notes, Array Bars, float TotalWidth) Plan(
        StaffDrawable drawable,
        List<GeneratedNote> notes,
        List<double> barBeats,
        float available,
        float leftMargin)
    {
        object plan = typeof(StaffDrawable)
            .GetMethod("PlanHorizontalLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { notes, barBeats, available, leftMargin, true, true, false })!;
        return (
            (Array)plan.GetType().GetField("Item1")!.GetValue(plan)!,
            (Array)plan.GetType().GetField("Item2")!.GetValue(plan)!,
            (float)plan.GetType().GetField("Item3")!.GetValue(plan)!);
    }

    private static void Invoke(StaffDrawable drawable, string name, params object[] args)
    {
        var method = typeof(StaffDrawable)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(m => m.Name == name && m.GetParameters().Length == args.Length);
        method.Invoke(drawable, args);
    }

    private static void AssertSameCoords(Array n1, Array n2, Array b1, Array b2)
    {
        Assert.Equal(n1.Length, n2.Length);
        Assert.Equal(b1.Length, b2.Length);
        for (int i = 0; i < n1.Length; i++)
            Assert.Equal(ReadX(n1, i), ReadX(n2, i), precision: 3);
        for (int i = 0; i < b1.Length; i++)
            Assert.Equal(ReadBarX(b1, i), ReadBarX(b2, i), precision: 3);
    }

    private static Array Clone(Array src)
    {
        var dst = Array.CreateInstance(src.GetValue(0)!.GetType(), src.Length);
        for (int i = 0; i < src.Length; i++)
            dst.SetValue(src.GetValue(i), i);
        return dst;
    }

    private static float[] CopyXs(Array layouts)
        => Enumerable.Range(0, layouts.Length).Select(i =>
            (float)layouts.GetValue(i)!.GetType().GetField("X")!.GetValue(layouts.GetValue(i)!)!).ToArray();

    private static float ReadX(Array layouts, int i)
        => (float)layouts.GetValue(i)!.GetType().GetField("X")!.GetValue(layouts.GetValue(i)!)!;

    private static float ReadBarX(Array bars, int i) => ReadX(bars, i);

    private static float BarLineRightEdge(object bar)
    {
        float x = (float)bar.GetType().GetField("X")!.GetValue(bar)!;
        bool isDouble = (bool)bar.GetType().GetField("IsDouble")!.GetValue(bar)!;
        return x + (isDouble ? 4f : 0f);
    }

    private static float InvokeFloat(StaffDrawable drawable, string method, object arg)
        => (float)typeof(StaffDrawable)
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new[] { arg })!;

    private static double GetBeatOrigin(List<GeneratedNote> notes, List<double> barBeats)
    {
        double o = notes.Min(n => n.BeatPosition ?? 0);
        if (barBeats.Count > 0) o = Math.Min(o, barBeats.Min());
        return o;
    }

    private static NoteSessionService CreateArpeggioSession()
        => new()
        {
            Instrument = "Concert Pitch",
            Key = "C",
            Tune = "Arpeggio",
            LowestNote = "A3",
            HighestNote = "C6",
            ChildLevel = 80,
            SelectedArpeggioId = ArpeggioCatalog.DiminishedTriad.Id,
            SelectedArpeggioDisplay = ArpeggioCatalog.DiminishedTriad.DisplayName,
            SelectedArpeggioRoot = "C4",
        };

    private static List<double> BuildBarBeats(IReadOnlyList<GeneratedNote> notes, double measureBeats)
    {
        var result = new List<double>();
        if (notes.Count == 0) return result;
        double origin = notes.Min(n => n.BeatPosition ?? 0);
        double end = notes.Max(n => (n.BeatPosition ?? 0) + n.BeatDuration);
        for (double bar = origin + measureBeats; bar < end - 1e-6; bar += measureBeats)
            result.Add(bar);
        return result;
    }
}
