using System.Reflection;
using System.Text;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;
using Xunit.Abstractions;

namespace musicmate.Tests;

/// <summary>
/// Diagnostic dump for the C-diminished @ 835 screen-fit path (Phase 1 + safe-scale repack).
/// </summary>
[Collection("StaffLayoutDiag")]
public class StaffHorizontalPipelineDiagTests
{
    private readonly ITestOutputHelper _out;
    public StaffHorizontalPipelineDiagTests(ITestOutputHelper output) => _out = output;

    private const float CanvasW = 835f;
    private const float CanvasH = 480f;
    private const float SafeLeft = 12f;
    private const int FocusMeasureLocal = 2;

    [Fact]
    public void PlanHorizontalLayout_IsIdempotent_IdenticalInputsSameCoordinates()
    {
        var session = CreateArpeggioSession();
        var upper = BuildCDiminishedArpeggio().Where(n => (n.MeasureIndex ?? 0) < 4).ToList();
        var (drawable, available, leftMargin, barBeats) = Warm(session, upper);

        var a = Plan(drawable, upper, barBeats, available, leftMargin);
        var b = Plan(drawable, upper, barBeats, available, leftMargin);

        Assert.Equal(a.Notes.Length, b.Notes.Length);
        for (int i = 0; i < a.Notes.Length; i++)
            Assert.Equal(ReadX(a.Notes, i), ReadX(b.Notes, i), precision: 3);

        Assert.Equal(a.Bars.Length, b.Bars.Length);
        for (int i = 0; i < a.Bars.Length; i++)
            Assert.Equal(ReadBarX(a.Bars, i), ReadBarX(b.Bars, i), precision: 3);
    }

    [Fact]
    public void AdultScreenFit_CDiminished_835_DumpsGeometryAndInvocationCounts()
    {
        StaffLayoutDiag.Enabled = true;
        StaffLayoutDiag.Reset();
        try
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
            var sb = new StringBuilder();
            sb.AppendLine($"split U={split.UpperMeasureCount} L={split.LowerMeasureCount} unplaced={split.UnplacedMeasureCount}");

            Warm(drawable, split.UpperNotes);
            var (available, leftMargin, staffBars) = Metrics(drawable, split.UpperNotes, bars, layoutRight);

            StaffLayoutDiag.Reset();
            var plan = Plan(drawable, split.UpperNotes, staffBars, available, leftMargin);
            int placeAfterPlan = StaffLayoutDiag.GetCount("PlaceBarLinesFromMeasureContent");
            double beatOrigin = GetBeatOrigin(split.UpperNotes, staffBars);

            sb.AppendLine($"=== available={available:F1} leftMargin={leftMargin:F1} layoutRight={layoutRight:F1} planTotal={plan.TotalWidth:F1} ===");
            sb.AppendLine($"PlaceBarLines after Plan only: {placeAfterPlan}");
            DumpFocus(sb, "After PlanHorizontalLayout (staff-local)", drawable, split.UpperNotes, plan.Notes, plan.Bars, beatOrigin, staffBars, leftMargin);

            var notes = Clone(plan.Notes);
            var barLayouts = Clone(plan.Bars);
            float contentWidth = Math.Max(1f, plan.TotalWidth - leftMargin);
            float scale = contentWidth > available ? available / contentWidth : 1f;
            scale = Math.Clamp(scale, StaffDrawable.MinimumSafeHorizontalScale, 1f);
            Invoke(drawable, "MapStaffLayoutToScreen", notes, barLayouts, SafeLeft, leftMargin, scale);
            DumpFocus(sb, $"After MapStaffLayoutToScreen (scale={scale:F3})", drawable, split.UpperNotes, notes, barLayouts, beatOrigin, staffBars, SafeLeft + leftMargin);

            Invoke(drawable, "FitMappedLayoutIntoRightLimit", notes, barLayouts, layoutRight, SafeLeft, leftMargin);
            DumpFocus(sb, "After FitMappedLayoutIntoRightLimit", drawable, split.UpperNotes, notes, barLayouts, beatOrigin, staffBars, SafeLeft + leftMargin);

            float maxInk = GetMaxRight(drawable, notes, barLayouts);
            sb.AppendLine($"maxInkOrBarRight={maxInk:F1} layoutRight={layoutRight:F1} gutter={layoutRight - maxInk:F1}");
            sb.AppendLine("--- execution counts ---");
            sb.Append(StaffLayoutDiag.FormatCounts());

            _out.WriteLine(sb.ToString());

            Assert.Equal(1, placeAfterPlan);
            Assert.True(maxInk <= layoutRight + 0.5f, sb.ToString());
            Assert.True(scale >= StaffDrawable.MinimumSafeHorizontalScale - 0.001f);
        }
        finally
        {
            StaffLayoutDiag.Enabled = false;
            StaffLayoutDiag.Reset();
        }
    }

    private static void Warm(StaffDrawable drawable, List<GeneratedNote> notes)
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

    private static (float available, float leftMargin, List<double> staffBars) Metrics(
        StaffDrawable drawable, List<GeneratedNote> notes, List<double> pageBars, float layoutRight)
    {
        object header = typeof(StaffDrawable)
            .GetMethod("ComputeHeaderMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { SafeLeft })!;
        float leftMargin = (float)header.GetType().GetProperty("LeftMargin")!.GetValue(header)!;
        float available = Math.Max(64f, layoutRight - SafeLeft - leftMargin);
        double start = notes.Min(n => n.BeatPosition ?? 0);
        double end = notes.Max(n => (n.BeatPosition ?? 0) + n.BeatDuration);
        var staffBars = pageBars.Where(b => b > start + 1e-6 && b < end - 1e-6).ToList();
        return (available, leftMargin, staffBars);
    }

    private static void DumpFocus(
        StringBuilder sb,
        string stage,
        StaffDrawable drawable,
        List<GeneratedNote> notes,
        Array noteLayouts,
        Array barLayouts,
        double beatOrigin,
        List<double> barBeats,
        float contentStartFallback)
    {
        sb.AppendLine();
        sb.AppendLine(stage);
        var idxs = Enumerable.Range(0, notes.Count)
            .Where(i => (notes[i].MeasureIndex ?? 0) == FocusMeasureLocal)
            .OrderBy(i => notes[i].BeatPosition)
            .ToList();
        if (idxs.Count == 0)
            return;

        var sortedBars = barBeats.Select(b => b - beatOrigin).OrderBy(b => b).ToList();
        float start = FocusMeasureLocal == 0
            ? contentStartFallback
            : ReadBarX(barLayouts, Math.Min(FocusMeasureLocal - 1, barLayouts.Length - 1));
        float end = FocusMeasureLocal < sortedBars.Count
            ? ReadBarX(barLayouts, FocusMeasureLocal)
            : ReadBarX(barLayouts, barLayouts.Length - 1);

        sb.AppendLine($"Measure {FocusMeasureLocal}: start={start:F1} end={end:F1} width={end - start:F1}");
        float lastRight = float.NegativeInfinity;
        for (int k = 0; k < idxs.Count; k++)
        {
            int i = idxs[k];
            object lay = noteLayouts.GetValue(i)!;
            float x = ReadLayoutX(lay);
            float left = InvokeFloat(drawable, "NoteGroupLeftFromLayout", lay);
            float right = InvokeFloat(drawable, "NoteInkRightForLayout", lay);
            sb.AppendLine(
                $"  note{k}: {notes[i].SpelledName} X={x:F1} inkL={left:F1} inkR={right:F1}");
            lastRight = Math.Max(lastRight, right);
        }

        sb.AppendLine($"  clearance lastInk→bar = {end - lastRight:F1}");
    }

    private static float GetMaxRight(StaffDrawable drawable, Array notes, Array bars)
        => (float)typeof(StaffDrawable)
            .GetMethod("GetLayoutMaxRight", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { notes, bars })!;

    private static (StaffDrawable drawable, float available, float leftMargin, List<double> barBeats)
        Warm(NoteSessionService session, List<GeneratedNote> notes)
    {
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        Warm(drawable, notes);
        var m = Metrics(drawable, notes, BuildBarBeats(notes, 4), CanvasW - 24f);
        return (drawable, m.available, m.leftMargin, BuildBarBeats(notes, 4));
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

    private static Array Clone(Array src)
    {
        var dst = Array.CreateInstance(src.GetValue(0)!.GetType(), src.Length);
        for (int i = 0; i < src.Length; i++)
            dst.SetValue(src.GetValue(i), i);
        return dst;
    }

    private static float ReadX(Array layouts, int i) => ReadLayoutX(layouts.GetValue(i)!);
    private static float ReadLayoutX(object layout) => (float)layout.GetType().GetField("X")!.GetValue(layout)!;
    private static float ReadBarX(Array bars, int i)
        => (float)bars.GetValue(i)!.GetType().GetField("X")!.GetValue(bars.GetValue(i)!)!;

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
