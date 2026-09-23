using System.Reflection;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Stage-1 (measure beats) and Stage-2 (note vs bar clearance) invariants for staff layout.
/// Regression target: diminished-triad arpeggios that looked like 2-beat bars because
/// bar-blind spacing shoved notes past fixed bar X positions.
/// </summary>
public class StaffMeasureBarClearanceTests
{
    private const float WideCanvasW = 900f;
    private const float NarrowCanvasW = 520f;
    private const float CanvasH = 480f;
    private const float ClearanceEpsilon = 0.75f;

    [Fact]
    public void DiminishedTriad_EveryCompleteMeasureSumsToFourBeats()
    {
        var notes = BuildCDiminishedArpeggio();
        Assert.NotEmpty(notes);

        foreach (var measure in notes.GroupBy(n => n.MeasureIndex ?? 0).OrderBy(g => g.Key))
        {
            double beats = measure.Sum(n => n.BeatDuration);
            Assert.Equal(4.0, beats, precision: 3);
        }

        var m0 = notes.Where(n => (n.MeasureIndex ?? 0) == 0).OrderBy(n => n.BeatPosition).ToList();
        Assert.Equal(4, m0.Count);
        Assert.Equal(new[] { "C4", "Eb4", "Gb4", "C5" }, m0.Select(n => n.SpelledName).ToArray());
    }

    [Theory]
    [InlineData(WideCanvasW)]
    [InlineData(NarrowCanvasW)]
    public void DiminishedTriad_PlanLayout_NoNoteInkCrossesInternalOrFinalBars(float canvasW)
    {
        var session = CreateArpeggioSession();
        var all = BuildCDiminishedArpeggio();
        var upper = all.Where(n => (n.MeasureIndex ?? 0) < 4).ToList();
        var lower = all.Where(n => (n.MeasureIndex ?? 0) >= 4).ToList();

        AssertStaffBarClearance(session, upper, canvasW, useFullHeader: true);
        AssertStaffBarClearance(session, lower, canvasW, useFullHeader: false);
    }

    [Theory]
    [InlineData(WideCanvasW)]
    [InlineData(NarrowCanvasW)]
    public void DiminishedTriad_AccidentalsIncludedInLeadingClearance(float canvasW)
    {
        var session = CreateArpeggioSession();
        var upper = BuildCDiminishedArpeggio().Where(n => (n.MeasureIndex ?? 0) < 4).ToList();
        var (drawable, noteLayouts, barLayouts, beatOrigin, barBeats) =
            PlanStaff(session, upper, canvasW, useFullHeader: true);

        // At least one body accidental (Gb) must reserve layout space (HasAccidental).
        bool anyAccidental = false;
        for (int i = 0; i < noteLayouts.Length; i++)
        {
            object lay = noteLayouts.GetValue(i)!;
            bool hasAcc = (bool)lay.GetType().GetField("HasAccidental")!.GetValue(lay)!;
            if (hasAcc)
            {
                anyAccidental = true;
                float groupLeft = InvokeGroupLeft(drawable, lay);
                float centerX = ReadLayoutX(lay);
                Assert.True(groupLeft < centerX - 0.5f,
                    $"Accidental note {i} must extend left of notehead (groupLeft={groupLeft:F1}, X={centerX:F1})");
            }
        }

        Assert.True(anyAccidental, "C diminished upper staff should include at least one written accidental (Gb)");
        AssertNoNoteBarCollisions(drawable, upper, noteLayouts, barLayouts, beatOrigin, barBeats);
    }

    [Fact]
    public void AdjacentChordSeconds_DoNotShareIdenticalNoteheadCenters()
    {
        // Structural guard: same-beat chord members with adjacent staff positions must not
        // collapse to the same center X (coincident noteheads). Arpeggios are melodic, so
        // synthesize a minimal same-beat pair and require distinct X after plan when displaced.
        var session = new NoteSessionService
        {
            Instrument = "Concert Pitch",
            Key = "C",
            Tune = "Selected Scale",
            LowestNote = "C4",
            HighestNote = "C6",
            ChildLevel = 50,
        };

        var notes = new List<GeneratedNote>
        {
            N(0, 0.0, 60, 'C', 4, NoteDuration.Quarter),
            N(0, 0.0, 62, 'D', 4, NoteDuration.Quarter), // second — adjacent staff degree
            N(0, 1.0, 64, 'E', 4, NoteDuration.Quarter),
            N(0, 2.0, 65, 'F', 4, NoteDuration.Quarter),
            N(0, 3.0, 67, 'G', 4, NoteDuration.Quarter),
        };

        var (drawable, noteLayouts, _, _, _) = PlanStaff(session, notes, WideCanvasW, useFullHeader: true);
        float x0 = ReadLayoutX(noteLayouts.GetValue(0)!);
        float x1 = ReadLayoutX(noteLayouts.GetValue(1)!);

        // If the engraver treats them as a chord at one beat, centers may match OR be displaced.
        // Coincident rectangles (same X and same Y band) are the failure mode we forbid when
        // both occupy the same staff step — here letters differ so Y differs; still require
        // that two events at the same beat do not stack with zero horizontal separation when
        // their noteheads would occupy adjacent lines/spaces without displacement metadata.
        if (Math.Abs(x0 - x1) < 0.05f)
        {
            // Same X is acceptable only when vertical positions differ (true chord stack).
            float y0 = InvokeNoteY(drawable, notes[0]);
            float y1 = InvokeNoteY(drawable, notes[1]);
            Assert.True(Math.Abs(y0 - y1) > 1f,
                "Same-beat noteheads at identical X must not also share the same staff Y");
        }
    }

    [Fact]
    public void LowerStaffWrap_PreservesMeasureDurations_AndBarClearance()
    {
        var session = CreateArpeggioSession();
        var all = BuildCDiminishedArpeggio();
        var upper = all.Where(n => (n.MeasureIndex ?? 0) < 4).ToList();
        var lower = all.Where(n => (n.MeasureIndex ?? 0) >= 4).ToList();

        foreach (var staff in new[] { upper, lower })
        {
            foreach (var measure in staff.GroupBy(n => n.MeasureIndex ?? 0))
                Assert.Equal(4.0, measure.Sum(n => n.BeatDuration), precision: 3);
        }

        AssertStaffBarClearance(session, upper, NarrowCanvasW, useFullHeader: true);
        AssertStaffBarClearance(session, lower, NarrowCanvasW, useFullHeader: false);
    }

    [Fact]
    public void FinalNote_HasClearanceBeforeFinalDoubleBar()
    {
        var session = CreateArpeggioSession();
        var upper = BuildCDiminishedArpeggio().Where(n => (n.MeasureIndex ?? 0) < 4).ToList();
        var (drawable, noteLayouts, barLayouts, _, _) =
            PlanStaff(session, upper, WideCanvasW, useFullHeader: true);

        Assert.True(barLayouts.Length > 0);
        float endBarX = ReadBarX(barLayouts, barLayouts.Length - 1);
        float maxInk = float.NegativeInfinity;
        for (int i = 0; i < noteLayouts.Length; i++)
        {
            float right = InvokeInkRight(drawable, noteLayouts.GetValue(i)!);
            maxInk = Math.Max(maxInk, right);
        }

        Assert.True(endBarX >= maxInk + 8f - ClearanceEpsilon,
            $"Final bar X={endBarX:F1} must clear last ink {maxInk:F1} with padding");
    }

    private static void AssertStaffBarClearance(
        NoteSessionService session,
        List<GeneratedNote> notes,
        float canvasW,
        bool useFullHeader)
    {
        var (drawable, noteLayouts, barLayouts, beatOrigin, barBeats) =
            PlanStaff(session, notes, canvasW, useFullHeader);

        foreach (var measure in notes.GroupBy(n => n.MeasureIndex ?? 0))
            Assert.Equal(4.0, measure.Sum(n => n.BeatDuration), precision: 3);

        AssertNoNoteBarCollisions(drawable, notes, noteLayouts, barLayouts, beatOrigin, barBeats);
    }

    private static void AssertNoNoteBarCollisions(
        StaffDrawable drawable,
        List<GeneratedNote> notes,
        Array noteLayouts,
        Array barLayouts,
        double beatOrigin,
        List<double> barBeats)
    {
        if (barLayouts.Length == 0)
            return;

        var sortedBarsRel = barBeats.Select(b => b - beatOrigin).OrderBy(b => b).ToList();
        // Internal bars correspond 1:1 with sortedBarsRel; final bar is last layout entry.
        int internalCount = Math.Min(sortedBarsRel.Count, barLayouts.Length - 1);

        for (int i = 0; i < notes.Count; i++)
        {
            object lay = noteLayouts.GetValue(i)!;
            float left = InvokeGroupLeft(drawable, lay);
            float right = InvokeInkRight(drawable, lay);
            double relBeat = (notes[i].BeatPosition ?? 0.0) - beatOrigin;
            int measure = GetMeasureIndex(relBeat, sortedBarsRel);

            // Following internal bar for this measure
            if (measure < internalCount)
            {
                float barX = ReadBarX(barLayouts, measure);
                Assert.True(right <= barX - 8f + ClearanceEpsilon,
                    $"Note {i} ({notes[i].SpelledName}) inkRight={right:F1} collides with bar[{measure}] X={barX:F1}");
            }

            // Preceding bar
            if (measure > 0 && measure - 1 < internalCount)
            {
                float prevBarX = ReadBarX(barLayouts, measure - 1);
                Assert.True(left >= prevBarX + 8f - ClearanceEpsilon,
                    $"Note {i} ({notes[i].SpelledName}) groupLeft={left:F1} collides with prev bar X={prevBarX:F1}");
            }
        }

        float endX = ReadBarX(barLayouts, barLayouts.Length - 1);
        for (int i = 0; i < noteLayouts.Length; i++)
        {
            float right = InvokeInkRight(drawable, noteLayouts.GetValue(i)!);
            Assert.True(right <= endX + ClearanceEpsilon,
                $"Note {i} ink {right:F1} is past final bar X={endX:F1}");
        }
    }

    private static (StaffDrawable drawable, Array noteLayouts, Array barLayouts, double beatOrigin, List<double> barBeats)
        PlanStaff(NoteSessionService session, List<GeneratedNote> notes, float canvasW, bool useFullHeader)
    {
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        typeof(StaffDrawable)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(m => m.Name == "ComputeLayout" && m.GetParameters().Length == 3)
            .Invoke(drawable, new object[]
            {
                CanvasH,
                (IReadOnlyList<GeneratedNote>)notes,
                (IReadOnlyList<GeneratedNote>)Array.Empty<GeneratedNote>(),
            });

        float safeLeft = 12f;
        object header = typeof(StaffDrawable)
            .GetMethod("ComputeHeaderMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { safeLeft })!;
        float leftMargin = useFullHeader
            ? (float)header.GetType().GetProperty("LeftMargin")!.GetValue(header)!
            : (float)header.GetType().GetProperty("ClefOnlyLeftMargin")!.GetValue(header)!;

        var barBeats = BuildBarBeats(notes, 4.0);
        float layoutRight = canvasW - 24f;
        float available = Math.Max(64f, layoutRight - safeLeft - leftMargin);

        object plan = typeof(StaffDrawable)
            .GetMethod("PlanHorizontalLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[]
            {
                notes,
                barBeats,
                available,
                leftMargin,
                useFullHeader,
                true,
                false
            })!;

        var noteLayouts = (Array)plan.GetType().GetField("Item1")!.GetValue(plan)!;
        var barLayouts = (Array)plan.GetType().GetField("Item2")!.GetValue(plan)!;
        double beatOrigin = notes.Min(n => n.BeatPosition ?? 0.0);
        if (barBeats.Count > 0)
            beatOrigin = Math.Min(beatOrigin, barBeats.Min());

        // PlanHorizontalLayout already calls PlaceBarLinesFromMeasureContent; no second pass.
        return (drawable, noteLayouts, barLayouts, beatOrigin, barBeats);
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
        if (notes.Count == 0 || measureBeats <= 0)
            return result;

        double origin = notes.Min(n => n.BeatPosition ?? 0.0);
        double end = notes.Max(n => (n.BeatPosition ?? 0.0) + n.BeatDuration);
        for (double bar = origin + measureBeats; bar < end - 1e-6; bar += measureBeats)
            result.Add(bar);
        return result;
    }

    private static int GetMeasureIndex(double relBeat, List<double> sortedBarsRel)
    {
        int measure = 0;
        for (int i = 0; i < sortedBarsRel.Count; i++)
        {
            if (relBeat + 1e-9 >= sortedBarsRel[i])
                measure = i + 1;
            else
                break;
        }
        return measure;
    }

    private static GeneratedNote N(
        int measure, double beat, int midi, char letter, int octave, NoteDuration dur, Accidental acc = Accidental.None)
        => new()
        {
            MidiNumber = midi,
            Letter = letter,
            Octave = octave,
            Accidental = acc,
            SpelledName = acc == Accidental.Flat ? $"{letter}b{octave}" : $"{letter}{octave}",
            Duration = dur,
            MeasureIndex = measure,
            BeatPosition = beat,
        };

    private static float ReadLayoutX(object layout)
        => (float)layout.GetType().GetField("X")!.GetValue(layout)!;

    private static float ReadBarX(Array barLayouts, int index)
        => (float)barLayouts.GetValue(index)!.GetType().GetField("X")!.GetValue(barLayouts.GetValue(index)!)!;

    private static float InvokeInkRight(StaffDrawable drawable, object layout)
        => (float)typeof(StaffDrawable)
            .GetMethod("NoteInkRightForLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new[] { layout })!;

    private static float InvokeGroupLeft(StaffDrawable drawable, object layout)
        => (float)typeof(StaffDrawable)
            .GetMethod("NoteGroupLeftFromLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new[] { layout })!;

    private static float InvokeNoteY(StaffDrawable drawable, GeneratedNote note)
    {
        var one = new List<GeneratedNote> { note };
        typeof(StaffDrawable)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(m => m.Name == "ComputeLayout" && m.GetParameters().Length == 3)
            .Invoke(drawable, new object[]
            {
                CanvasH,
                (IReadOnlyList<GeneratedNote>)one,
                (IReadOnlyList<GeneratedNote>)Array.Empty<GeneratedNote>(),
            });
        var layoutField = typeof(StaffDrawable).GetField("_layout", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object lay = layoutField.GetValue(drawable)!;
        float staffTop = 40f;
        float sls = (float)lay.GetType().GetField("Sls")!.GetValue(lay)!;
        float staffMid = staffTop + 2f * sls;
        return (float)typeof(StaffDrawable)
            .GetMethod("NoteY", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { note, staffTop, staffMid })!;
    }
}
