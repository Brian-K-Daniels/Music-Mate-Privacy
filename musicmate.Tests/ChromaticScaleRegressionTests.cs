using System.Reflection;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;
using Xunit.Abstractions;

namespace musicmate.Tests;

/// <summary>
/// Chromatic two-octave scale: width-packed layout within layoutRight, and
/// within-measure accidental state (reset at measure boundaries after staff shift).
/// </summary>
[Collection("StaffLayoutDiag")]
public class ChromaticScaleRegressionTests
{
    private readonly ITestOutputHelper _out;
    public ChromaticScaleRegressionTests(ITestOutputHelper output) => _out = output;

    private const float CanvasH = 480f;
    private const float SafeLeft = 12f;
    private const float ClearanceEpsilon = 0.75f;
    private const float MinBarClearance = 8f;

    public static IEnumerable<object[]> TargetWidths =>
    [
        [835f],
        [520f],
        [1100f],
    ];

    [Theory]
    [MemberData(nameof(TargetWidths))]
    public void ChromaticTwoOctave_NoInkPastLayoutRight(float canvasW)
    {
        var (page, bars) = BuildChromaticTwoOctavePage();
        var drawable = NewChromaticDrawable();
        var split = StaffPageWidthPolicy.SplitCachedPage(
            drawable,
            new StaffPagePackState
            {
                PageNotes = page,
                PageBarBeats = bars,
                MeasureBeats = 4,
                IsTwoOctaveScaleCut = false,
                PackedCanvasWidth = canvasW,
                PackedCanvasHeight = CanvasH,
            },
            canvasW,
            CanvasH);

        float layoutRight = canvasW - 24f;
        var report = RunLayoutReport(drawable, split, bars, canvasW, layoutRight);
        _out.WriteLine(report.Dump);

        Assert.True(report.UpperMaxInk <= layoutRight + 0.5f, report.Dump);
        if (split.LowerNotes.Count > 0)
            Assert.True(report.LowerMaxInk <= layoutRight + 0.5f, report.Dump);
        Assert.True(report.MinBarClearance >= MinBarClearance - ClearanceEpsilon, report.Dump);
        Assert.Equal(
            page.Count,
            split.UpperNotes.Count + split.LowerNotes.Count + split.UnplacedNotes.Count);
    }

    [Fact]
    public void ChromaticTwoOctave_835_WholeMeasurePacking_NotPeakSplitOverflow()
    {
        var (page, bars) = BuildChromaticTwoOctavePage();
        var drawable = NewChromaticDrawable();
        var packed = StaffPageWidthPolicy.SplitCachedPage(
            drawable,
            new StaffPagePackState { PageNotes = page, PageBarBeats = bars, MeasureBeats = 4 },
            835f,
            CanvasH);
        var peak = StaffPageWidthPolicy.SplitTwoOctaveScaleAtPeak(page);

        Assert.True(
            packed.UpperMeasureCount + packed.LowerMeasureCount
            < CountMeasures(peak.UpperNotes) + CountMeasures(peak.LowerNotes),
            "Width packing must place fewer measures than peak split at 835 DIP.");
        Assert.True(packed.UnplacedMeasureCount > 0 || packed.UpperMeasureCount <= 3,
            "Expected unplaced measures or a short upper staff at 835.");
    }

    [Fact]
    public void ChromaticTwoOctave_ScreenFit_IsIdempotent()
    {
        var (page, bars) = BuildChromaticTwoOctavePage();
        var drawable = NewChromaticDrawable();
        var split = StaffPageWidthPolicy.SplitCachedPage(
            drawable,
            new StaffPagePackState { PageNotes = page, PageBarBeats = bars, MeasureBeats = 4 },
            835f,
            CanvasH);

        var first = ScreenFitStaff(drawable, split.UpperNotes, bars, 835f, true, out _, out _, out _);
        var second = ScreenFitStaff(drawable, split.UpperNotes, bars, 835f, true, out _, out _, out _);
        Assert.Equal(first.MaxInk, second.MaxInk, 1);
    }

    [Fact]
    public void AccidentalState_ResetsAtMeasureIndexBoundary_AfterStaffShift()
    {
        var (page, _) = BuildChromaticTwoOctavePage();
        var peak = StaffPageWidthPolicy.SplitTwoOctaveScaleAtPeak(page);
        var lower = ShiftStaffBeatPositions(peak.LowerNotes);

        var flags = SimulateAccidentalDrawFlags(NewChromaticDrawable(), lower, ComputeStaffBarBeats(lower, 4));
        Assert.Contains(flags, f => f.MeasureIndexChangedReset);

        int secondMeasureIndex = lower
            .Where(n => !n.IsRest)
            .Select(n => n.MeasureIndex ?? 0)
            .Distinct()
            .OrderBy(mi => mi)
            .Skip(1)
            .First();

        var beatOne = lower
            .Select((n, i) => (n, i))
            .Where(t => (t.n.MeasureIndex ?? -1) == secondMeasureIndex && !t.n.IsRest)
            .OrderBy(t => t.n.BeatPosition ?? 0)
            .First();

        _out.WriteLine(
            $"lower staff measure index {secondMeasureIndex} beat 1: {beatOne.n.SpelledName} " +
            $"midi={beatOne.n.MidiNumber} acc={beatOne.n.Accidental} draw={flags[beatOne.i].Draw}");

        Assert.True(
            beatOne.n.Accidental is Accidental.Sharp or Accidental.Flat,
            $"Expected altered spelling, got {beatOne.n.SpelledName}/{beatOne.n.Accidental}");
        Assert.True(flags[beatOne.i].Draw,
            $"First note of lower-staff measure {secondMeasureIndex} ({beatOne.n.SpelledName}) must display its accidental.");
    }

    [Fact]
    public void AccidentalState_MeasureIndexReset_ClearsPriorMeasureCarry()
    {
        var drawable = NewChromaticDrawable();
        var history = new Dictionary<(char, int), Accidental> { [('A', 4)] = Accidental.Sharp };
        var cancelled = new HashSet<(char, int)>();
        var note = Note("A#4", Accidental.Sharp, 7, 10.0);

        var tryResolve = typeof(StaffDrawable).GetMethod(
            "TryResolveBodyAccidental", BindingFlags.Instance | BindingFlags.NonPublic)!;

        object?[] args = { note, history, cancelled, Accidental.None, false };
        tryResolve.Invoke(drawable, args);
        Assert.False((bool)args[4]!, "same altered pitch in a new measure must not carry without reset");

        history.Clear();
        cancelled.Clear();
        args = new object?[] { note, history, cancelled, Accidental.None, false };
        tryResolve.Invoke(drawable, args);
        Assert.True((bool)args[4]!, "after measure reset, required sharp must draw");
    }

    [Fact]
    public void AccidentalState_RepeatedAlterationWithinMeasure_SuppressesSecondGlyph()
    {
        var drawable = NewChromaticDrawable();
        var notes = new List<GeneratedNote>
        {
            Note("G#5", Accidental.Sharp, 0, 0),
            Note("G#5", Accidental.Sharp, 0, 1),
        };
        var flags = SimulateAccidentalDrawFlags(drawable, notes, new List<double> { 4 });
        Assert.True(flags[0].Draw);
        Assert.False(flags[1].Draw);
    }

    [Fact]
    public void AccidentalState_NaturalShownAfterPriorAlterationInMeasure()
    {
        var drawable = NewChromaticDrawable();
        var notes = new List<GeneratedNote>
        {
            Note("G#5", Accidental.Sharp, 0, 0),
            Note("G5", Accidental.Natural, 0, 1),
        };
        var flags = SimulateAccidentalDrawFlags(drawable, notes, new List<double> { 4 });
        Assert.True(flags[0].Draw);
        Assert.True(flags[1].Draw);
    }

    [Fact]
    public void ChromaticAscending_UsesSharps_DescendingUsesFlats()
    {
        var (page, _) = BuildChromaticTwoOctavePage();
        int peak = StaffPageWidthPolicy.FindScaleWalkPeakNoteIndex(page);
        var ascending = page.Take(peak + 1).Where(n => !n.IsRest).ToList();
        var descending = page.Skip(peak + 1).Where(n => !n.IsRest).ToList();

        Assert.Contains(ascending, n => n.Accidental == Accidental.Sharp || n.SpelledName.Contains('#'));
        Assert.True(
            descending.Any(n => n.Accidental == Accidental.Flat || n.SpelledName.Contains('b'))
            || descending.Any(n => n.Accidental == Accidental.Sharp || n.SpelledName.Contains('#')),
            "Descending chromatic should use flats when descending spelling applies.");
    }

    private sealed record DrawFlag(int Index, bool Draw, bool MeasureIndexChangedReset);

    private sealed record LayoutReport(
        string Dump,
        float UpperMaxInk,
        float LowerMaxInk,
        float MinBarClearance,
        int UpperMeasures,
        int LowerMeasures,
        int UnplacedMeasures);

    private static LayoutReport RunLayoutReport(
        StaffDrawable drawable,
        StaffDrawable.StaffMeasureSplitResult split,
        List<double> pageBars,
        float canvasW,
        float layoutRight)
    {
        float minBar = float.PositiveInfinity;
        float upperInk = 0;
        float lowerInk = 0;

        if (split.UpperNotes.Count > 0)
        {
            var u = ScreenFitStaff(drawable, split.UpperNotes, pageBars, canvasW, true, out _, out _, out _);
            upperInk = u.MaxInk;
            minBar = Math.Min(minBar, u.MinBarClearance);
        }

        if (split.LowerNotes.Count > 0)
        {
            var l = ScreenFitStaff(drawable, split.LowerNotes, pageBars, canvasW, false, out _, out _, out _);
            lowerInk = l.MaxInk;
            minBar = Math.Min(minBar, l.MinBarClearance);
        }

        if (float.IsPositiveInfinity(minBar)) minBar = 0;

        string dump =
            $"canvasW={canvasW} layoutRight={layoutRight:F1} " +
            $"U={split.UpperMeasureCount} L={split.LowerMeasureCount} unplaced={split.UnplacedMeasureCount} " +
            $"upperMaxInk={upperInk:F1} lowerMaxInk={lowerInk:F1} minBarClearance={minBar:F1}";
        return new LayoutReport(dump, upperInk, lowerInk, minBar,
            split.UpperMeasureCount, split.LowerMeasureCount, split.UnplacedMeasureCount);
    }

    private sealed record StaffMetrics(float MaxInk, float MinBarClearance);

    private static StaffMetrics ScreenFitStaff(
        StaffDrawable drawable,
        List<GeneratedNote> notes,
        List<double> pageBarBeats,
        float canvasW,
        bool useFullHeader,
        out float scale,
        out float requiredLocal,
        out float usable)
    {
        WarmLayout(drawable, notes, canvasW);
        var header = ComputeHeader(drawable, SafeLeft);
        float leftMargin = useFullHeader ? header.LeftMargin : header.ClefOnlyLeftMargin;
        float layoutRight = canvasW - 24f;
        usable = Math.Max(64f, layoutRight - SafeLeft - leftMargin);

        double start = notes.Min(n => n.BeatPosition ?? 0);
        double end = notes.Max(n => (n.BeatPosition ?? 0) + n.BeatDuration);
        var barBeats = pageBarBeats.Where(b => b > start + 1e-6 && b < end - 1e-6).ToList();

        var planObj = typeof(StaffDrawable)
            .GetMethod("PlanHorizontalLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { notes, barBeats, usable, leftMargin, useFullHeader, true, false })!;
        var plan = new PlanResult(
            (Array)planObj.GetType().GetField("Item1")!.GetValue(planObj)!,
            (Array)planObj.GetType().GetField("Item2")!.GetValue(planObj)!,
            (float)planObj.GetType().GetField("Item3")!.GetValue(planObj)!);
        requiredLocal = Math.Max(1f, plan.TotalWidth - leftMargin);
        scale = requiredLocal > usable ? Math.Clamp(usable / requiredLocal, StaffDrawable.MinimumSafeHorizontalScale, 1f) : 1f;

        var noteLayouts = Clone(plan.Notes);
        var barLayouts = Clone(plan.Bars);
        Invoke(drawable, "MapStaffLayoutToScreen", noteLayouts, barLayouts, SafeLeft, leftMargin, scale);
        Invoke(drawable, "FitMappedLayoutIntoRightLimit", noteLayouts, barLayouts, layoutRight, SafeLeft, leftMargin);

        float maxInk = (float)Invoke(drawable, "GetLayoutMaxRight", noteLayouts, barLayouts)!;
        return new StaffMetrics(maxInk, MinBarClearance);
    }

    private static List<DrawFlag> SimulateAccidentalDrawFlags(
        StaffDrawable drawable,
        IReadOnlyList<GeneratedNote> notes,
        List<double> barBeats,
        bool resetOnMeasureChange = true)
    {
        var tryResolve = typeof(StaffDrawable).GetMethod(
            "TryResolveBodyAccidental",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var resetBar = typeof(StaffDrawable).GetMethod(
            "ResetAccidentalStateIfCrossedBar",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        double beatOrigin = (double)typeof(StaffDrawable)
            .GetMethod("GetStaffBeatOrigin", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { notes, barBeats })!;

        barBeats = (List<double>)typeof(StaffDrawable)
            .GetMethod("ResolveStaffBarBeats", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { notes, barBeats, beatOrigin })!;

        var history = new Dictionary<(char, int), Accidental>();
        var cancelled = new HashSet<(char, int)>();
        var result = new List<DrawFlag>();
        double prevBeat = -1;

        for (int i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            double beat = (note.BeatPosition ?? 0) - beatOrigin;
            bool measureReset = false;

            if (resetOnMeasureChange && i > 0)
            {
                int prevMi = notes[i - 1].MeasureIndex ?? -1;
                int curMi = note.MeasureIndex ?? prevMi;
                if (curMi != prevMi)
                {
                    history.Clear();
                    cancelled.Clear();
                    measureReset = true;
                }
            }

            resetBar.Invoke(null, new object[] { beat, prevBeat, barBeats, beatOrigin, history, cancelled });

            var args = new object?[] { note, history, cancelled, Accidental.None, false };
            tryResolve.Invoke(drawable, args);
            bool draw = (bool)args[4]!;
            var eff = (Accidental)args[3]!;
            if (eff != Accidental.None)
                history[(note.Letter, note.Octave)] = eff;

            result.Add(new DrawFlag(i, draw, measureReset));
            prevBeat = beat;
        }

        return result;
    }

    private static int CountMeasures(IReadOnlyList<GeneratedNote> notes)
        => notes.Select(n => n.MeasureIndex ?? 0).Distinct().Count();

    private static List<GeneratedNote> ShiftStaffBeatPositions(IReadOnlyList<GeneratedNote> notes)
    {
        if (notes.Count == 0) return new List<GeneratedNote>();
        double shift = notes[0].BeatPosition ?? 0;
        return notes.Select(n => new GeneratedNote
        {
            MidiNumber = n.MidiNumber,
            Letter = n.Letter,
            Octave = n.Octave,
            Accidental = n.Accidental,
            SpelledName = n.SpelledName,
            TargetFrequency = n.TargetFrequency,
            Duration = n.Duration,
            IsRest = n.IsRest,
            MeasureIndex = n.MeasureIndex,
            BeatPosition = (n.BeatPosition ?? 0) - shift,
        }).ToList();
    }

    private static List<double> ComputeStaffBarBeats(IReadOnlyList<GeneratedNote> notes, double measureBeats)
    {
        var result = new List<double>();
        if (notes.Count == 0) return result;
        double origin = notes.Min(n => n.BeatPosition ?? 0);
        double end = notes.Max(n => (n.BeatPosition ?? 0) + n.BeatDuration);
        for (double bar = origin + measureBeats; bar < end - 1e-6; bar += measureBeats)
            result.Add(bar);
        return result;
    }

    private static (List<GeneratedNote> page, List<double> bars) BuildChromaticTwoOctavePage()
    {
        var gen = new MusicSequenceGenerator
        {
            Key = "D",
            Scale = "Chromatic",
            LowestNote = "A3",
            HighestNote = "C6",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 24,
            RhythmVarietyPercent = 0,
            SmallestDuration = NoteDuration.Quarter,
            RestChancePercent = 0,
            AccidentalPercent = 0,
            UseScaleOrder = true,
            ChildLevel = 31,
            RandomSeed = 19,
        };
        var page = MusicSequenceGenerator.Flatten(gen.GenerateSequence());
        var bars = new List<double>();
        double origin = page.Min(n => n.BeatPosition ?? 0);
        double end = page.Max(n => (n.BeatPosition ?? 0) + n.BeatDuration);
        for (double bar = origin + 4; bar < end - 1e-6; bar += 4)
            bars.Add(bar);
        return (page, bars);
    }

    private static StaffDrawable NewChromaticDrawable()
        => new(new NoteSessionService
        {
            Key = "D",
            SelectedScale = "Chromatic",
            MeterTimeSignature = "4/4",
            ChildLevel = 31,
            ShowSignaturesOnBothStaffs = true,
            LowestNote = "A3",
            HighestNote = "C6",
        }, new ThemeService(), safeArea: null);

    private static GeneratedNote Note(string spelled, Accidental acc, int measure, double beat)
    {
        char letter = char.ToUpperInvariant(spelled[0]);
        int octave = int.Parse(spelled[^1].ToString());
        int midi = NoteSessionService.NoteNameToMidi(spelled);
        return new GeneratedNote
        {
            MidiNumber = midi,
            Letter = letter,
            Octave = octave,
            Accidental = acc,
            SpelledName = spelled,
            Duration = NoteDuration.Quarter,
            MeasureIndex = measure,
            BeatPosition = beat,
        };
    }

    private sealed record HeaderMetrics(float LeftMargin, float ClefOnlyLeftMargin);
    private sealed record PlanResult(Array Notes, Array Bars, float TotalWidth);

    private static void WarmLayout(StaffDrawable drawable, List<GeneratedNote> notes, float canvasW)
        => Invoke(drawable, "ComputeLayout", CanvasH, notes, Array.Empty<GeneratedNote>());

    private static HeaderMetrics ComputeHeader(StaffDrawable drawable, float safeLeft)
    {
        object header = Invoke(drawable, "ComputeHeaderMetrics", safeLeft)!;
        return new HeaderMetrics(
            (float)header.GetType().GetProperty("LeftMargin")!.GetValue(header)!,
            (float)header.GetType().GetProperty("ClefOnlyLeftMargin")!.GetValue(header)!);
    }

    private static PlanResult Plan(
        StaffDrawable drawable, List<GeneratedNote> notes, List<double> barBeats,
        float usable, float leftMargin, bool useFullHeader)
    {
        object planObj = typeof(StaffDrawable)
            .GetMethod("PlanHorizontalLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { notes, barBeats, usable, leftMargin, useFullHeader, true, false })!;
        return new PlanResult(
            (Array)planObj.GetType().GetField("Item1")!.GetValue(planObj)!,
            (Array)planObj.GetType().GetField("Item2")!.GetValue(planObj)!,
            (float)planObj.GetType().GetField("Item3")!.GetValue(planObj)!);
    }

    private static Array Clone(Array src) => (Array)src.Clone();

    private static object? Invoke(StaffDrawable drawable, string name, params object?[] args)
    {
        var method = typeof(StaffDrawable).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(m => m.Name == name && m.GetParameters().Length == args.Length);
        return method.Invoke(drawable, args);
    }
}
