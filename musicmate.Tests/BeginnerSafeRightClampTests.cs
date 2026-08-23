using System.Reflection;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;
using Xunit.Abstractions;

namespace musicmate.Tests;

/// <summary>
/// Beginner FinishBeginnerHorizontalLayout must clamp to safe-right after Expand/Nudge/Align
/// (same protection as the adult path) so high-ledger pages cannot clip at the right edge.
/// </summary>
public class BeginnerSafeRightClampTests
{
    private readonly ITestOutputHelper _out;
    public BeginnerSafeRightClampTests(ITestOutputHelper output) => _out = output;

    private const float CanvasW = 835f;
    private const float CanvasH = 480f;
    private const float MinInkGap = 8f;
    private const float RightMargin = 36f;
    private const float LayoutRightPad = 2f;

    [Fact]
    public void FinishBeginner_AfterNudgeAlign_ClampsMaxRightToLayoutLimit()
    {
        var (drawable, upper, lower, leftMargin, lowerMargin, layoutRightLimit,
            upperNotesLay, upperBarsLay, lowerNotesLay, lowerBarsLay,
            upperTotal, lowerTotal, upperUsable, lowerUsable, expandLower) =
            BuildCNaturalMinorStyleBeginnerLayouts();

        // Run the full beginner finish (now ends with ClampLayoutToSafeRight).
        InvokeFinishBeginner(
            drawable, upper, lower,
            upperNotesLay, upperBarsLay, upperTotal, upperUsable,
            lowerNotesLay, lowerBarsLay, lowerTotal, lowerUsable,
            leftMargin, lowerMargin, layoutRightLimit, expandLower);

        float pipelineUpper = GetLayoutMaxRight(drawable, upperNotesLay, upperBarsLay);
        float pipelineLower = GetLayoutMaxRight(drawable, lowerNotesLay, lowerBarsLay);
        Assert.True(pipelineUpper <= layoutRightLimit + 0.5f,
            $"Full FinishBeginner upper maxRight={pipelineUpper:F1} must be <= {layoutRightLimit:F1}");
        Assert.True(pipelineLower <= layoutRightLimit + 0.5f,
            $"Full FinishBeginner lower maxRight={pipelineLower:F1} must be <= {layoutRightLimit:F1}");

        // Isolated before/after for ClampLayoutToSafeRight (C Natural Minor-style overflow).
        ShiftAll(upperNotesLay, upperBarsLay, dx: 48f);
        ShiftAll(lowerNotesLay, lowerBarsLay, dx: 36f);

        float beforeUpper = GetLayoutMaxRight(drawable, upperNotesLay, upperBarsLay);
        float beforeLower = GetLayoutMaxRight(drawable, lowerNotesLay, lowerBarsLay);
        Assert.True(beforeUpper > layoutRightLimit + 0.5f,
            $"Regression setup: upper maxRight {beforeUpper:F1} should exceed limit {layoutRightLimit:F1}");
        Assert.True(beforeLower > layoutRightLimit + 0.5f,
            $"Regression setup: lower maxRight {beforeLower:F1} should exceed limit {layoutRightLimit:F1}");

        Clamp(drawable, upperNotesLay, upperBarsLay, layoutRightLimit, 0f, leftMargin);
        Clamp(drawable, lowerNotesLay, lowerBarsLay, layoutRightLimit, 0f, lowerMargin);

        float afterUpper = GetLayoutMaxRight(drawable, upperNotesLay, upperBarsLay);
        float afterLower = GetLayoutMaxRight(drawable, lowerNotesLay, lowerBarsLay);

        Assert.True(afterUpper <= layoutRightLimit + 0.5f,
            $"Upper after clamp {afterUpper:F1} must be <= {layoutRightLimit:F1} (before={beforeUpper:F1})");
        Assert.True(afterLower <= layoutRightLimit + 0.5f,
            $"Lower after clamp {afterLower:F1} must be <= {layoutRightLimit:F1} (before={beforeLower:F1})");
        Assert.True(beforeUpper > afterUpper && beforeLower > afterLower,
            $"Clamp must reduce extent: upper {beforeUpper:F1}→{afterUpper:F1}, lower {beforeLower:F1}→{afterLower:F1}");

        _out.WriteLine(
            $"C Natural Minor-style clamp: layoutRightLimit={layoutRightLimit:F1}; " +
            $"upper maxRight before={beforeUpper:F1} after={afterUpper:F1}; " +
            $"lower maxRight before={beforeLower:F1} after={afterLower:F1}; " +
            $"pipeline (with clamp) upper={pipelineUpper:F1} lower={pipelineLower:F1}");
    }

    [Fact]
    public void FinishBeginner_FullPipeline_CNaturalMinorStyle_StaysWithinSafeRight()
    {
        var (drawable, upper, lower, leftMargin, lowerMargin, layoutRightLimit,
            upperNotesLay, upperBarsLay, lowerNotesLay, lowerBarsLay,
            upperTotal, lowerTotal, upperUsable, lowerUsable, expandLower) =
            BuildCNaturalMinorStyleBeginnerLayouts();

        int upperCount = upper.Count;
        int lowerCount = lower.Count;

        InvokeFinishBeginner(
            drawable, upper, lower,
            upperNotesLay, upperBarsLay, upperTotal, upperUsable,
            lowerNotesLay, lowerBarsLay, lowerTotal, lowerUsable,
            leftMargin, lowerMargin, layoutRightLimit, expandLower);

        float upperRight = GetLayoutMaxRight(drawable, upperNotesLay, upperBarsLay);
        float lowerRight = GetLayoutMaxRight(drawable, lowerNotesLay, lowerBarsLay);

        Assert.True(upperRight <= layoutRightLimit + 0.5f,
            $"Upper clipped: maxRight={upperRight:F1} layoutRightLimit={layoutRightLimit:F1}");
        Assert.True(lowerRight <= layoutRightLimit + 0.5f,
            $"Lower clipped: maxRight={lowerRight:F1} layoutRightLimit={layoutRightLimit:F1}");
        AssertNotesDoNotPassFinalBar(drawable, upperNotesLay, upperBarsLay);
        AssertNotesDoNotPassFinalBar(drawable, lowerNotesLay, lowerBarsLay);

        // No events dropped by the finish pass.
        Assert.Equal(upperCount, upper.Count);
        Assert.Equal(lowerCount, lower.Count);
        Assert.Equal(upperCount, upperNotesLay.Length);
        Assert.Equal(lowerCount, lowerNotesLay.Length);

        // End bars present and not past the limit.
        if (upperBarsLay.Length > 0)
        {
            float endX = ReadBarX(upperBarsLay, upperBarsLay.Length - 1);
            Assert.True(endX <= layoutRightLimit + 0.5f, $"Upper end bar X={endX:F1} past limit");
        }
        if (lowerBarsLay.Length > 0)
        {
            float endX = ReadBarX(lowerBarsLay, lowerBarsLay.Length - 1);
            Assert.True(endX <= layoutRightLimit + 0.5f, $"Lower end bar X={endX:F1} past limit");
        }

        AssertStaffGaps(drawable, upper, upperNotesLay);
        AssertStaffGaps(drawable, lower, lowerNotesLay);
    }

    [Fact]
    public void FinishBeginner_Clamp_DoesNotDropPackedMeasuresOrReintroduceCollisions()
    {
        var session = new NoteSessionService
        {
            Key = "C",
            SelectedScale = "Natural Minor",
            MeterTimeSignature = "4/4",
            ChildLevel = 40,
            ShowSignaturesOnBothStaffs = true,
            IsRandomMode = true,
        };
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);

        var gen = new MusicSequenceGenerator
        {
            Key = "C",
            Scale = "Natural Minor",
            LowestNote = "A2",
            HighestNote = "C6",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 8,
            RhythmVarietyPercent = 55,
            SmallestDuration = NoteDuration.Eighth,
            RestChancePercent = 20,
            AccidentalPercent = 25,
            SyncopationLevel = SyncopationLevel.None,
            MaxMelodicIntervalSemitones = 5,
            UseScaleOrder = false,
            UseMotifPhrases = true,
            ChildLevel = 40,
            RandomSeed = 102,
        };
        var page = MusicSequenceGenerator.Flatten(gen.GenerateSequence());
        var bars = Enumerable.Range(1, 7).Select(i => i * 4.0).ToList();
        var split = drawable.SplitMeasuresAcrossStaves(page, bars, CanvasW, CanvasH);
        int placedBefore = split.UpperMeasureCount + split.LowerMeasureCount;
        int unplacedBefore = split.UnplacedMeasureCount;

        Assert.Equal(page.Count,
            split.UpperNotes.Count + split.LowerNotes.Count + split.UnplacedNotes.Count);

        // Layout the packed staffs through FinishBeginner (includes new clamp).
        var laid = LayBeginner(drawable, session, split.UpperNotes, split.LowerNotes);
        Assert.True(laid.UpperMaxRight <= laid.LayoutRightLimit + 0.5f);
        Assert.True(laid.LowerMaxRight <= laid.LayoutRightLimit + 0.5f);

        // Pack counts unchanged by draw clamp.
        var split2 = drawable.SplitMeasuresAcrossStaves(page, bars, CanvasW, CanvasH);
        Assert.Equal(placedBefore, split2.UpperMeasureCount + split2.LowerMeasureCount);
        Assert.Equal(unplacedBefore, split2.UnplacedMeasureCount);

        AssertStaffGaps(drawable, split.UpperNotes.ToList(), laid.UpperNoteLayouts);
        if (split.LowerNotes.Count >= 2)
            AssertStaffGaps(drawable, split.LowerNotes.ToList(), laid.LowerNoteLayouts);
    }

    private sealed class LaidResult
    {
        public float UpperMaxRight { get; init; }
        public float LowerMaxRight { get; init; }
        public float LayoutRightLimit { get; init; }
        public Array UpperNoteLayouts { get; init; } = Array.Empty<object>();
        public Array LowerNoteLayouts { get; init; } = Array.Empty<object>();
    }

    private static LaidResult LayBeginner(
        StaffDrawable drawable,
        NoteSessionService session,
        List<GeneratedNote> upper,
        List<GeneratedNote> lower)
    {
        var built = BuildLayoutsFromNotes(drawable, session, upper, lower);
        InvokeFinishBeginner(
            drawable, upper, lower,
            built.UpperNotesLay, built.UpperBarsLay, built.UpperTotal, built.UpperUsable,
            built.LowerNotesLay, built.LowerBarsLay, built.LowerTotal, built.LowerUsable,
            built.LeftMargin, built.LowerMargin, built.LayoutRightLimit, built.ExpandLower);

        return new LaidResult
        {
            UpperMaxRight = GetLayoutMaxRight(drawable, built.UpperNotesLay, built.UpperBarsLay),
            LowerMaxRight = GetLayoutMaxRight(drawable, built.LowerNotesLay, built.LowerBarsLay),
            LayoutRightLimit = built.LayoutRightLimit,
            UpperNoteLayouts = built.UpperNotesLay,
            LowerNoteLayouts = built.LowerNotesLay,
        };
    }

    private static (
        StaffDrawable drawable,
        List<GeneratedNote> upper,
        List<GeneratedNote> lower,
        float leftMargin,
        float lowerMargin,
        float layoutRightLimit,
        Array upperNotesLay,
        Array upperBarsLay,
        Array lowerNotesLay,
        Array lowerBarsLay,
        float upperTotal,
        float lowerTotal,
        float upperUsable,
        float lowerUsable,
        bool expandLower)
        BuildCNaturalMinorStyleBeginnerLayouts()
    {
        var session = new NoteSessionService
        {
            Key = "C",
            SelectedScale = "Natural Minor",
            MeterTimeSignature = "4/4",
            ChildLevel = 40,
            ShowSignaturesOnBothStaffs = true,
            IsRandomMode = true,
        };
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);

        // High ledger + accidental content similar to the clipped phone screenshot.
        var upper = new List<GeneratedNote>
        {
            GeneratedNote.Rest(NoteDuration.Quarter, 0, 0),
            N(0, 1.0, 75, 'E', NoteDuration.Eighth, Accidental.Flat),
            N(0, 1.5, 71, 'B', NoteDuration.Half, Accidental.Natural),
            N(1, 4.0, 94, 'B', NoteDuration.Quarter, Accidental.Flat), // Bb6 — three ledgers
            GeneratedNote.Rest(NoteDuration.Eighth, 1, 5.0),
            GeneratedNote.Rest(NoteDuration.Eighth, 1, 5.5),
            N(1, 6.0, 91, 'G', NoteDuration.Quarter),
            GeneratedNote.Rest(NoteDuration.Quarter, 1, 7.0),
        };
        var lower = new List<GeneratedNote>
        {
            N(0, 0.0, 94, 'B', NoteDuration.Quarter, Accidental.Flat),
            GeneratedNote.Rest(NoteDuration.Eighth, 0, 1.0),
            GeneratedNote.Rest(NoteDuration.Eighth, 0, 1.5),
            GeneratedNote.Rest(NoteDuration.Half, 0, 2.0),
            N(1, 4.0, 91, 'G', NoteDuration.Quarter),
            GeneratedNote.Rest(NoteDuration.Eighth, 1, 5.0),
            GeneratedNote.Rest(NoteDuration.Eighth, 1, 5.5),
            GeneratedNote.Rest(NoteDuration.Half, 1, 6.0),
        };

        var built = BuildLayoutsFromNotes(drawable, session, upper, lower);
        return (drawable, upper, lower, built.LeftMargin, built.LowerMargin, built.LayoutRightLimit,
            built.UpperNotesLay, built.UpperBarsLay, built.LowerNotesLay, built.LowerBarsLay,
            built.UpperTotal, built.LowerTotal, built.UpperUsable, built.LowerUsable, built.ExpandLower);
    }

    private sealed class BuiltLayouts
    {
        public float LeftMargin { get; init; }
        public float LowerMargin { get; init; }
        public float LayoutRightLimit { get; init; }
        public float UpperUsable { get; init; }
        public float LowerUsable { get; init; }
        public Array UpperNotesLay { get; init; } = Array.Empty<object>();
        public Array UpperBarsLay { get; init; } = Array.Empty<object>();
        public Array LowerNotesLay { get; init; } = Array.Empty<object>();
        public Array LowerBarsLay { get; init; } = Array.Empty<object>();
        public float UpperTotal { get; init; }
        public float LowerTotal { get; init; }
        public bool ExpandLower { get; init; }
    }

    private static BuiltLayouts BuildLayoutsFromNotes(
        StaffDrawable drawable,
        NoteSessionService session,
        List<GeneratedNote> upper,
        List<GeneratedNote> lower)
    {
        drawable.UpperNotes = upper;
        drawable.LowerNotes = lower;
        drawable.UpperBarBeats = BarsFor(upper);
        drawable.LowerBarBeats = BarsFor(lower);
        drawable.InvalidateLayoutCache();

        typeof(StaffDrawable)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(m => m.Name == "ComputeLayout" && m.GetParameters().Length == 3)
            .Invoke(drawable, new object[]
            {
                CanvasH,
                (IReadOnlyList<GeneratedNote>)upper,
                (IReadOnlyList<GeneratedNote>)lower,
            });

        var header = typeof(StaffDrawable)
            .GetMethod("ComputeHeaderMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { 0f })!;
        float leftMargin = (float)header.GetType().GetProperty("LeftMargin")!.GetValue(header)!;
        float layoutRightLimit = CanvasW - LayoutRightPad;
        float safeWidth = layoutRightLimit;
        float upperUsable = safeWidth - leftMargin - RightMargin;
        float lowerMargin = leftMargin;
        float lowerUsable = safeWidth - lowerMargin - RightMargin;

        var plan = typeof(StaffDrawable)
            .GetMethod("PlanHorizontalLayout", BindingFlags.Instance | BindingFlags.NonPublic)!;

        object upperPlan = plan.Invoke(drawable, new object[]
        {
            upper, drawable.UpperBarBeats, upperUsable, leftMargin, true, false, false
        })!;
        var upperNotesLay = (Array)upperPlan.GetType().GetField("Item1")!.GetValue(upperPlan)!;
        var upperBarsLay = (Array)upperPlan.GetType().GetField("Item2")!.GetValue(upperPlan)!;
        float upperTotal = (float)upperPlan.GetType().GetField("Item3")!.GetValue(upperPlan)!;

        double upperBeats = upper.Sum(n => n.BeatDuration);
        double lowerBeats = lower.Sum(n => n.BeatDuration);
        float lowerPlanWidth = StaffDrawable.ComputeMatchedStaffUsableWidth(
            Math.Max(1f, upperTotal - leftMargin), upperBeats, lowerBeats, lowerUsable);
        bool lowerIsShort = StaffDrawable.IsShortLowerStaff(upperBeats, lowerBeats);
        bool expandLower = !lowerIsShort && lowerPlanWidth >= lowerUsable - 0.5f;

        object lowerPlan = plan.Invoke(drawable, new object[]
        {
            lower, drawable.LowerBarBeats, lowerPlanWidth, lowerMargin, true, true, false
        })!;
        var lowerNotesLay = (Array)lowerPlan.GetType().GetField("Item1")!.GetValue(lowerPlan)!;
        var lowerBarsLay = (Array)lowerPlan.GetType().GetField("Item2")!.GetValue(lowerPlan)!;
        float lowerTotal = (float)lowerPlan.GetType().GetField("Item3")!.GetValue(lowerPlan)!;

        return new BuiltLayouts
        {
            LeftMargin = leftMargin,
            LowerMargin = lowerMargin,
            LayoutRightLimit = layoutRightLimit,
            UpperUsable = upperUsable,
            LowerUsable = lowerUsable,
            UpperNotesLay = upperNotesLay,
            UpperBarsLay = upperBarsLay,
            LowerNotesLay = lowerNotesLay,
            LowerBarsLay = lowerBarsLay,
            UpperTotal = upperTotal,
            LowerTotal = lowerTotal,
            ExpandLower = expandLower,
        };
    }

    private static void InvokeFinishBeginner(
        StaffDrawable drawable,
        List<GeneratedNote> upper,
        List<GeneratedNote> lower,
        Array upperNotesLay,
        Array upperBarsLay,
        float upperTotal,
        float upperUsable,
        Array lowerNotesLay,
        Array lowerBarsLay,
        float lowerTotal,
        float lowerUsable,
        float leftMargin,
        float lowerMargin,
        float layoutRightLimit,
        bool expandLower)
    {
        typeof(StaffDrawable)
            .GetMethod("FinishBeginnerHorizontalLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[]
            {
                upper, upperNotesLay, upperBarsLay, upperTotal, upperUsable,
                lower, lowerNotesLay, lowerBarsLay, lowerTotal, lowerUsable,
                0f, leftMargin, lowerMargin, layoutRightLimit,
                40f, 60f, 80f, 140f, 160f, 180f,
                expandLower,
            });
    }

    private static void Clamp(
        StaffDrawable drawable,
        Array noteLayouts,
        Array barLayouts,
        float layoutRightLimit,
        float safeLeft,
        float staffLeftMargin)
    {
        typeof(StaffDrawable)
            .GetMethod("ClampLayoutToSafeRight", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[]
            {
                noteLayouts, barLayouts, layoutRightLimit, safeLeft, staffLeftMargin
            });
    }

    private static float GetLayoutMaxRight(StaffDrawable drawable, Array noteLayouts, Array barLayouts)
        => (float)typeof(StaffDrawable)
            .GetMethod("GetLayoutMaxRight", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { noteLayouts, barLayouts })!;

    private static void ShiftAll(Array noteLayouts, Array barLayouts, float dx)
    {
        for (int i = 0; i < noteLayouts.Length; i++)
        {
            object lay = noteLayouts.GetValue(i)!;
            var xField = lay.GetType().GetField("X")!;
            xField.SetValue(lay, (float)xField.GetValue(lay)! + dx);
            var ax = lay.GetType().GetField("AccidentalX")!;
            ax.SetValue(lay, (float)ax.GetValue(lay)! + dx);
            noteLayouts.SetValue(lay, i);
        }
        for (int i = 0; i < barLayouts.Length; i++)
        {
            object bar = barLayouts.GetValue(i)!;
            var xField = bar.GetType().GetField("X")!;
            xField.SetValue(bar, (float)xField.GetValue(bar)! + dx);
            barLayouts.SetValue(bar, i);
        }
    }

    private static float ReadBarX(Array bars, int index)
    {
        object bar = bars.GetValue(index)!;
        return (float)bar.GetType().GetField("X")!.GetValue(bar)!;
    }

    private static List<double> BarsFor(List<GeneratedNote> notes)
    {
        var bars = new List<double>();
        if (notes.Count == 0)
            return bars;
        int prev = -1;
        foreach (var n in notes.OrderBy(n => n.BeatPosition ?? 0))
        {
            int mi = n.MeasureIndex ?? 0;
            if (prev >= 0 && mi != prev && n.BeatPosition.HasValue)
                bars.Add(n.BeatPosition.Value);
            prev = mi;
        }
        return bars;
    }

    private static GeneratedNote N(
        int measure, double beat, int midi, char letter, NoteDuration dur, Accidental acc = Accidental.None)
        => new()
        {
            MidiNumber = midi,
            Letter = letter,
            Octave = midi / 12 - 1,
            Accidental = acc,
            SpelledName = $"{letter}",
            Duration = dur,
            MeasureIndex = measure,
            BeatPosition = beat,
        };

    private static void AssertNotesDoNotPassFinalBar(StaffDrawable drawable, Array noteLayouts, Array barLayouts)
    {
        if (noteLayouts.Length == 0 || barLayouts.Length == 0)
            return;

        float endX = ReadBarX(barLayouts, barLayouts.Length - 1);
        var inkRight = typeof(StaffDrawable).GetMethod(
            "NoteInkRightForLayout",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        for (int i = 0; i < noteLayouts.Length; i++)
        {
            object lay = noteLayouts.GetValue(i)!;
            float right = (float)inkRight.Invoke(drawable, new object[] { lay })!;
            Assert.True(right <= endX + 0.5f,
                $"Note {i} ink {right:F1} is past final bar X={endX:F1}");
        }
    }

    private static void AssertStaffGaps(StaffDrawable drawable, List<GeneratedNote> notes, Array layouts)
    {
        if (notes.Count < 2)
            return;

        var groupLeft = typeof(StaffDrawable).GetMethod("NoteGroupLeftFromLayout", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var trail = typeof(StaffDrawable).GetMethod(
            "NoteTrailingRight", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(GeneratedNote), typeof(float) }, null)!;

        var order = Enumerable.Range(0, notes.Count)
            .OrderBy(i => notes[i].BeatPosition ?? 0).ThenBy(i => i).ToList();
        for (int oi = 1; oi < order.Count; oi++)
        {
            int p = order[oi - 1], c = order[oi];
            float xP = (float)layouts.GetValue(p)!.GetType().GetField("X")!.GetValue(layouts.GetValue(p)!)!;
            float right = (float)trail.Invoke(drawable, new object[] { notes[p], xP })!;
            float left = (float)groupLeft.Invoke(drawable, new object[] { layouts.GetValue(c)! })!;
            float gap = left - right;
            Assert.True(gap + 0.05f >= MinInkGap,
                $"Gap {gap:F1} < MinInkGap between events {p} and {c} after beginner safe-right clamp");
        }
    }
}
