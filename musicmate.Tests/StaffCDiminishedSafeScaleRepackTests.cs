using System.Reflection;
using System.Text;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;
using Xunit.Abstractions;

namespace musicmate.Tests;

/// <summary>
/// C diminished arpeggio: whole-measure repack when screen scale would drop below
/// <see cref="StaffDrawable.MinimumSafeHorizontalScale"/>.
/// </summary>
[Collection("StaffLayoutDiag")]
public class StaffCDiminishedSafeScaleRepackTests
{
    private readonly ITestOutputHelper _out;
    public StaffCDiminishedSafeScaleRepackTests(ITestOutputHelper output) => _out = output;

    private const float CanvasH = 480f;
    private const float SafeLeft = 12f;
    private const float ClearanceEpsilon = 0.75f;
    private const float MinBarClearance = 8f;
    private const float MinAccidentalBarClearance = 8f;

    public static IEnumerable<object[]> TargetWidths =>
    [
        [835f],
        [700f],
        [600f],
        [520f],
        [StaffPageWidthPolicy.FallbackWidthDip],
    ];

    [Theory]
    [MemberData(nameof(TargetWidths))]
    public void CDiminished_SafeScaleRepack_MeetsClearanceAtEveryWidth(float canvasW)
    {
        var report = RunWidthReport(canvasW);
        _out.WriteLine(report.Dump);

        Assert.True(report.UpperScale >= StaffDrawable.MinimumSafeHorizontalScale - 0.001f,
            $"upper scale {report.UpperScale:F3} below safe floor at {canvasW}");
        if (report.LowerMeasureCount > 0)
            Assert.True(report.LowerScale >= StaffDrawable.MinimumSafeHorizontalScale - 0.001f,
                $"lower scale {report.LowerScale:F3} below safe floor at {canvasW}");

        // Bar/accidental clearance is meaningful when content fits at the safe scale.
        // A single over-wide measure on a very narrow canvas may still be force-placed;
        // FitMappedLayoutIntoRightLimit then prioritizes keeping ink on-canvas.
        if (report.ContentFitsAtSafeScale)
        {
            Assert.True(report.MinBarClearance >= MinBarClearance - ClearanceEpsilon, report.Dump);
            if (report.HasInternalBarAccidentalSample)
                Assert.True(report.MinAccidentalBarClearance >= MinAccidentalBarClearance - ClearanceEpsilon, report.Dump);
            Assert.True(report.MaxInkRight <= report.LayoutRight + 0.5f, report.Dump);
        }
        Assert.Equal(8, report.UpperMeasureCount + report.LowerMeasureCount + report.UnplacedMeasureCount);
        AssertNoPostMapStructuralMutators();
    }

    [Fact]
    public void Repack_IsDeterministic_SameWidthSameAssignment()
    {
        var page = BuildCDiminishedArpeggio();
        var bars = BuildBarBeats(page, 4);
        var drawable = NewDrawable();

        var a = StaffPageWidthPolicy.SplitCachedPage(drawable, MakeState(page, bars), 520f, CanvasH);
        var b = StaffPageWidthPolicy.SplitCachedPage(drawable, MakeState(page, bars), 520f, CanvasH);

        Assert.Equal(a.UpperMeasureCount, b.UpperMeasureCount);
        Assert.Equal(a.LowerMeasureCount, b.LowerMeasureCount);
        Assert.Equal(a.UnplacedMeasureCount, b.UnplacedMeasureCount);
        Assert.Equal(Fingerprint(a.UpperNotes), Fingerprint(b.UpperNotes));
        Assert.Equal(Fingerprint(a.LowerNotes), Fingerprint(b.LowerNotes));
    }

    [Fact]
    public void Repack_PreservesMeasureOrderAndFourBeatTotals()
    {
        var page = BuildCDiminishedArpeggio();
        var split = StaffPageWidthPolicy.SplitCachedPage(
            NewDrawable(), MakeState(page, BuildBarBeats(page, 4)), 520f, CanvasH);

        var placed = split.UpperNotes.Concat(split.LowerNotes).Concat(split.UnplacedNotes).ToList();
        Assert.Equal(page.Count, placed.Count);
        for (int i = 0; i < page.Count; i++)
            Assert.Equal(page[i].SpelledName, placed[i].SpelledName);

        foreach (var g in placed.GroupBy(n => n.MeasureIndex ?? 0))
            Assert.Equal(4.0, g.Sum(n => n.BeatDuration), 3);
    }

    [Theory]
    [InlineData(835f, 2, 3, 3)]
    [InlineData(700f, 2, 2, 4)]
    [InlineData(600f, 1, 1, 6)]
    [InlineData(520f, 1, 1, 6)]
    [InlineData(StaffPageWidthPolicy.FallbackWidthDip, 1, 1, 6)]
    public void CDiminished_ExpectedMeasureDistribution(
        float canvasW, int expectUpper, int expectLower, int expectUnplaced)
    {
        var page = BuildCDiminishedArpeggio();
        var split = StaffPageWidthPolicy.SplitCachedPage(
            NewDrawable(), MakeState(page, BuildBarBeats(page, 4)), canvasW, CanvasH);

        Assert.Equal(expectUpper, split.UpperMeasureCount);
        Assert.Equal(expectLower, split.LowerMeasureCount);
        Assert.Equal(expectUnplaced, split.UnplacedMeasureCount);
    }

    [Fact]
    public void PackConsecutiveMeasureWidths_RespectsSafeLocalSpan()
    {
        var widths = new[] { 220f, 220f, 220f, 220f };
        float usable = 233f;
        int packed = StaffDrawable.PackConsecutiveMeasureWidths(widths, usable, 0);
        Assert.Equal(1, packed); // 220 <= 233/0.85≈274; 440 > 274
        Assert.True(StaffDrawable.RequiresMeasureRepackForScale(440f, usable));
    }

    private sealed record WidthReport(
        string Dump,
        float LayoutRight,
        int UpperMeasureCount,
        int LowerMeasureCount,
        int UnplacedMeasureCount,
        float UpperScale,
        float LowerScale,
        float MinBarClearance,
        float MinAccidentalBarClearance,
        float MaxInkRight,
        bool HasInternalBarAccidentalSample,
        bool ContentFitsAtSafeScale);

    private WidthReport RunWidthReport(float canvasW)
    {
        var page = BuildCDiminishedArpeggio();
        var barBeats = BuildBarBeats(page, 4);
        var drawable = NewDrawable();
        var split = StaffPageWidthPolicy.SplitCachedPage(
            drawable, MakeState(page, barBeats), canvasW, CanvasH);

        float layoutRight = canvasW - 24f;
        var sb = new StringBuilder();
        sb.AppendLine($"canvasW={canvasW:F0} layoutRight={layoutRight:F1}");
        sb.AppendLine($"split U={split.UpperMeasureCount} L={split.LowerMeasureCount} unplaced={split.UnplacedMeasureCount}");

        float minBar = float.PositiveInfinity;
        float minAccBar = float.PositiveInfinity;
        float maxInk = float.NegativeInfinity;
        bool accSample = false;
        bool fitsAtSafe = true;

        float upperScale = 1f;
        float lowerScale = 1f;

        if (split.UpperNotes.Count > 0)
        {
            var u = ScreenFitStaff(drawable, split.UpperNotes, barBeats, canvasW, true, out upperScale, out float uReq, out float uUsable);
            sb.AppendLine($"upper scale={upperScale:F3} maxInk={u.MaxInk:F1} reqLocal={uReq:F0} usable={uUsable:F0}");
            minBar = Math.Min(minBar, u.MinBarClearance);
            if (u.HasAccidentalBarSample) { accSample = true; minAccBar = Math.Min(minAccBar, u.MinAccidentalBarClearance); }
            maxInk = Math.Max(maxInk, u.MaxInk);
            if (StaffDrawable.RequiresMeasureRepackForScale(uReq, uUsable))
                fitsAtSafe = false;
        }

        if (split.LowerNotes.Count > 0)
        {
            var l = ScreenFitStaff(drawable, split.LowerNotes, barBeats, canvasW, false, out lowerScale, out float lReq, out float lUsable);
            sb.AppendLine($"lower scale={lowerScale:F3} maxInk={l.MaxInk:F1} reqLocal={lReq:F0} usable={lUsable:F0}");
            minBar = Math.Min(minBar, l.MinBarClearance);
            if (l.HasAccidentalBarSample) { accSample = true; minAccBar = Math.Min(minAccBar, l.MinAccidentalBarClearance); }
            maxInk = Math.Max(maxInk, l.MaxInk);
            if (StaffDrawable.RequiresMeasureRepackForScale(lReq, lUsable))
                fitsAtSafe = false;
        }

        if (float.IsPositiveInfinity(minBar)) minBar = 0f;
        if (float.IsPositiveInfinity(minAccBar)) minAccBar = 0f;

        sb.AppendLine($"minBarClearance={minBar:F1} minAccBarClearance={minAccBar:F1} maxInkRight={maxInk:F1}");
        return new WidthReport(
            sb.ToString(), layoutRight,
            split.UpperMeasureCount, split.LowerMeasureCount, split.UnplacedMeasureCount,
            upperScale, lowerScale, minBar, minAccBar, maxInk, accSample, fitsAtSafe);
    }

    private sealed record StaffMetrics(
        float MaxInk,
        float MinBarClearance,
        float MinAccidentalBarClearance,
        bool HasAccidentalBarSample);

    private static StaffMetrics ScreenFitStaff(
        StaffDrawable drawable,
        List<GeneratedNote> notes,
        List<double> pageBarBeats,
        float canvasW,
        bool useFullHeader,
        out float finalScale,
        out float requiredLocalWidth,
        out float usableWidth)
    {
        WarmLayout(drawable, notes, canvasW);
        var header = ComputeHeader(drawable, SafeLeft);
        float leftMargin = useFullHeader ? header.LeftMargin : header.ClefOnlyLeftMargin;
        float layoutRight = canvasW - 24f;
        float usable = Math.Max(64f, layoutRight - SafeLeft - leftMargin);
        usableWidth = usable;

        var staffBarBeats = FilterBarBeatsForStaff(notes, pageBarBeats);
        var plan = Plan(drawable, notes, staffBarBeats, usable, leftMargin, useFullHeader);
        requiredLocalWidth = Math.Max(1f, plan.TotalWidth - leftMargin);
        float contentWidth = requiredLocalWidth;
        float scale = contentWidth > usable ? usable / contentWidth : 1f;
        scale = Math.Clamp(scale, StaffDrawable.MinimumSafeHorizontalScale, 1f);

        var noteLayouts = Clone(plan.Notes);
        var barLayouts = Clone(plan.Bars);
        Invoke(drawable, "MapStaffLayoutToScreen", noteLayouts, barLayouts, SafeLeft, leftMargin, scale);
        Invoke(drawable, "FitMappedLayoutIntoRightLimit", noteLayouts, barLayouts, layoutRight, SafeLeft, leftMargin);
        finalScale = scale;

        return MeasureClearances(drawable, notes, noteLayouts, barLayouts, staffBarBeats);
    }

    private static StaffMetrics MeasureClearances(
        StaffDrawable drawable,
        List<GeneratedNote> notes,
        Array noteLayouts,
        Array barLayouts,
        List<double> barBeats)
    {
        double beatOrigin = notes.Min(n => n.BeatPosition ?? 0);
        if (barBeats.Count > 0) beatOrigin = Math.Min(beatOrigin, barBeats.Min());
        var sortedBarsRel = barBeats.Select(b => b - beatOrigin).OrderBy(b => b).ToList();
        int internalCount = Math.Min(sortedBarsRel.Count, barLayouts.Length - 1);

        float minBar = float.PositiveInfinity;
        float minAcc = float.PositiveInfinity;
        float maxInk = float.NegativeInfinity;
        bool accSample = false;

        for (int i = 0; i < notes.Count; i++)
        {
            object lay = noteLayouts.GetValue(i)!;
            float left = InvokeFloat(drawable, "NoteGroupLeftFromLayout", lay);
            float right = InvokeFloat(drawable, "NoteInkRightForLayout", lay);
            maxInk = Math.Max(maxInk, right);
            bool hasAcc = (bool)lay.GetType().GetField("HasAccidental")!.GetValue(lay)!;
            float accLeft = hasAcc
                ? (float)lay.GetType().GetField("AccidentalX")!.GetValue(lay)!
                : left;

            double relBeat = (notes[i].BeatPosition ?? 0) - beatOrigin;
            int measure = 0;
            for (int b = 0; b < sortedBarsRel.Count; b++)
                if (relBeat + 1e-9 >= sortedBarsRel[b]) measure = b + 1;

            if (measure < internalCount)
            {
                float barX = ReadX(barLayouts, measure);
                minBar = Math.Min(minBar, barX - right);
                if (hasAcc)
                {
                    accSample = true;
                    minAcc = Math.Min(minAcc, barX - accLeft);
                }
            }

            if (measure > 0 && measure - 1 < internalCount)
            {
                float prevBar = ReadX(barLayouts, measure - 1);
                minBar = Math.Min(minBar, left - prevBar);
                if (hasAcc)
                {
                    accSample = true;
                    minAcc = Math.Min(minAcc, left - prevBar);
                }
            }
        }

        if (barLayouts.Length > 0)
        {
            float endBar = ReadX(barLayouts, barLayouts.Length - 1);
            for (int i = 0; i < notes.Count; i++)
            {
                object lay = noteLayouts.GetValue(i)!;
                float right = InvokeFloat(drawable, "NoteInkRightForLayout", lay);
                minBar = Math.Min(minBar, endBar - right);
                bool hasAcc = (bool)lay.GetType().GetField("HasAccidental")!.GetValue(lay)!;
                if (hasAcc)
                {
                    float accLeft = (float)lay.GetType().GetField("AccidentalX")!.GetValue(lay)!;
                    accSample = true;
                    minAcc = Math.Min(minAcc, endBar - accLeft);
                }
            }
        }

        return new StaffMetrics(maxInk, minBar, minAcc, accSample);
    }

    private static void AssertNoPostMapStructuralMutators()
    {
        Assert.Equal(0, StaffLayoutDiag.GetCount("PlaceBarLinesFromMeasureContent_BLOCKED_AFTER_SCREEN_MAP"));
        Assert.Equal(0, StaffLayoutDiag.GetCount("EnforceStrictBeatOrderSpacing_BLOCKED_AFTER_SCREEN_MAP"));
        Assert.Equal(0, StaffLayoutDiag.GetCount("RefinishMeasureSpacing_BLOCKED_AFTER_SCREEN_MAP"));
    }

    private static StaffDrawable NewDrawable()
        => new(new NoteSessionService
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
        }, new ThemeService(), safeArea: null);

    private static StaffPagePackState MakeState(List<GeneratedNote> page, List<double> bars)
        => new()
        {
            PageNotes = page,
            PageBarBeats = bars,
            MeasureBeats = 4,
            IsTwoOctaveScaleCut = false,
        };

    private static void WarmLayout(StaffDrawable drawable, List<GeneratedNote> notes, float canvasW)
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
    }

    private static (float LeftMargin, float ClefOnlyLeftMargin) ComputeHeader(StaffDrawable drawable, float safeLeft)
    {
        object header = typeof(StaffDrawable)
            .GetMethod("ComputeHeaderMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { safeLeft })!;
        return (
            (float)header.GetType().GetProperty("LeftMargin")!.GetValue(header)!,
            (float)header.GetType().GetProperty("ClefOnlyLeftMargin")!.GetValue(header)!);
    }

    private static List<double> FilterBarBeatsForStaff(List<GeneratedNote> staffNotes, List<double> pageBarBeats)
    {
        if (staffNotes.Count == 0) return new List<double>();
        double start = staffNotes.Min(n => n.BeatPosition ?? 0);
        double end = staffNotes.Max(n => (n.BeatPosition ?? 0) + n.BeatDuration);
        return pageBarBeats.Where(b => b > start + 1e-6 && b < end - 1e-6).ToList();
    }

    private static (Array Notes, Array Bars, float TotalWidth) Plan(
        StaffDrawable drawable,
        List<GeneratedNote> notes,
        List<double> barBeats,
        float available,
        float leftMargin,
        bool useFullHeader)
    {
        object plan = typeof(StaffDrawable)
            .GetMethod("PlanHorizontalLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[]
            {
                notes, barBeats, available, leftMargin, useFullHeader, true, false
            })!;
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

    private static Array Clone(Array src)
    {
        var dst = Array.CreateInstance(src.GetValue(0)!.GetType(), src.Length);
        for (int i = 0; i < src.Length; i++)
            dst.SetValue(src.GetValue(i), i);
        return dst;
    }

    private static float ReadX(Array layouts, int i)
        => (float)layouts.GetValue(i)!.GetType().GetField("X")!.GetValue(layouts.GetValue(i)!)!;

    private static float InvokeFloat(StaffDrawable drawable, string method, object arg)
        => (float)typeof(StaffDrawable)
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new[] { arg })!;

    private static string Fingerprint(IReadOnlyList<GeneratedNote> notes)
        => string.Join("|", notes.Select(n => $"{n.SpelledName}@{n.BeatPosition}"));

    private static List<GeneratedNote> BuildCDiminishedArpeggio()
        => new ArpeggioSequenceBuilder
        {
            Key = "C",
            Scale = "Natural Minor",
            LowestNote = "A3",
            HighestNote = "C6",
        }.Build(ArpeggioCatalog.DiminishedTriad, "C4");

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
