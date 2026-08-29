using System.Reflection;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Interval Sight Training: final-pair layout must not pan Current notes past the double bar.
/// </summary>
public class IntervalSightTrainingStaffLayoutTests
{
    private const float CanvasW = 835f;
    private const float CanvasH = 160f;
    private const float SafeLeft = 0f;

    [Fact]
    public void FinalPair_SkipsPan_CurrentNotesStayInsideEndBar()
    {
        var (drawable, notes, states, pair) = PrepareFinalPair(seed: 177);
        var snap = LayoutSightStaff(drawable, notes, states, out Array endBars);

        float anchorXBefore = snap.Xs[pair.AnchorFlatIndex];
        float targetXBefore = snap.Xs[pair.TargetFlatIndex];

        InvokeEnsureActivePairVisible(drawable, notes, states, snap.NoteLayouts, endBars, snap);

        Assert.Equal(anchorXBefore, ReadX(snap.NoteLayouts, pair.AnchorFlatIndex), precision: 2);
        Assert.Equal(targetXBefore, ReadX(snap.NoteLayouts, pair.TargetFlatIndex), precision: 2);

        var finalized = FinalizeEndBar(drawable, snap.NoteLayouts);
        AssertNotesDoNotPassFinalBar(drawable, notes, snap.NoteLayouts, finalized);
    }

    [Fact]
    public void FinalPair_D4E4Seed177_IndicesAreLastTwoPitchedNotes()
    {
        var session = CreateSightSession();
        var ex = IntervalSightTrainingSequenceBuilder.Generate(
            session, randomSeed: 177, childLevelOverride: 47);
        var (_, notes, _, pair) = PrepareFinalPair(seed: 177);
        int last = LastPitchedFlatIndex(notes);
        int prev = PreviousPitchedFlatIndex(notes, last);
        Assert.Equal(prev, pair.AnchorFlatIndex);
        Assert.Equal(last, pair.TargetFlatIndex);
        Assert.Equal(62, IntervalSightTrainingLogic.SightSoundingMidi(
            notes[pair.AnchorFlatIndex], ex.Key, ex.Scale));
        Assert.Equal(64, IntervalSightTrainingLogic.SightSoundingMidi(
            notes[pair.TargetFlatIndex], ex.Key, ex.Scale));
    }

    [Fact]
    public void AfterFinalCorrectAnswer_NoCurrentHighlight()
    {
        var session = CreateSightSession();
        var ex = IntervalSightTrainingSequenceBuilder.Generate(
            session, randomSeed: 177, childLevelOverride: 47);
        var logic = new IntervalSightTrainingLogic(ex.Notes, ex.Key, ex.Scale);
        while (logic.HasCurrentPair)
            logic.SubmitAnswer(logic.CurrentPair!.Value.AbsoluteSemitones);

        var states = logic.BuildNoteStates();
        Assert.True(logic.IsSessionComplete);
        Assert.DoesNotContain(StaffNoteState.Current, states);
    }

    private static (StaffDrawable drawable, List<GeneratedNote> notes, StaffNoteState[] states, IntervalSightTrainingLogic.NotePair pair)
        PrepareFinalPair(int seed)
    {
        var session = CreateSightSession();
        var ex = IntervalSightTrainingSequenceBuilder.Generate(
            session, randomSeed: seed, childLevelOverride: 47);
        var logic = new IntervalSightTrainingLogic(ex.Notes, ex.Key, ex.Scale);
        while (logic.HasCurrentPair)
        {
            var p = logic.CurrentPair!.Value;
            if (p.TargetFlatIndex == LastPitchedFlatIndex(ex.Notes))
                break;
            logic.SubmitAnswer(p.AbsoluteSemitones);
        }

        Assert.True(logic.HasCurrentPair);
        var pair = logic.CurrentPair!.Value;
        var drawable = CreateSightDrawable(session, ex);
        var states = logic.BuildNoteStates();
        return (drawable, ex.Notes, states, pair);
    }

    private static NoteSessionService CreateSightSession()
        => new()
        {
            ChildLevel = 47,
            AccidentalPercent = 0,
            Key = "G",
            SelectedScale = "Major",
            LowestNote = "C4",
            HighestNote = "C5",
            MeterTimeSignature = "4/4",
        };

    private static StaffDrawable CreateSightDrawable(
        NoteSessionService session,
        IntervalSightTrainingSequenceBuilder.SightExercise ex)
    {
        return new StaffDrawable(session, new ThemeService(), safeArea: null)
        {
            SingleStaffLayout = true,
            AvailableHeight = CanvasH,
            NotationKeyOverride = ex.Key,
            NotationScaleOverride = ex.Scale,
            UpperNotes = ex.Notes,
            LowerNotes = new List<GeneratedNote>(),
            UpperBarBeats = ex.BarBeats,
            LowerBarBeats = new List<double>(),
            IsUpperActive = true,
        };
    }

    private sealed class LayoutSnap
    {
        public required Array NoteLayouts { get; init; }
        public required float LayoutRightLimit { get; init; }
        public required float LeftMargin { get; init; }
        public required float[] Xs { get; init; }
    }

    private static LayoutSnap LayoutSightStaff(
        StaffDrawable drawable,
        List<GeneratedNote> notes,
        StaffNoteState[] states,
        out Array barLayoutsOut)
    {
        drawable.UpperNoteStates = states;

        WarmVerticalLayout(drawable, notes);

        object header = typeof(StaffDrawable)
            .GetMethod("ComputeHeaderMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { SafeLeft })!;
        float leftMargin = (float)header.GetType().GetProperty("LeftMargin")!.GetValue(header)!;
        float layoutRightLimit = CanvasW - 8f;
        float upperUsable = layoutRightLimit - SafeLeft - leftMargin - 16f;

        object plan = typeof(StaffDrawable)
            .GetMethod("PlanHorizontalLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[]
            {
                notes,
                drawable.UpperBarBeats,
                upperUsable,
                leftMargin,
                true,
                true,
                false,
            })!;

        var noteLayouts = (Array)plan.GetType().GetField("Item1")!.GetValue(plan)!;
        var barLayouts = (Array)plan.GetType().GetField("Item2")!.GetValue(plan)!;
        float totalWidth = (float)plan.GetType().GetField("Item3")!.GetValue(plan)!;

        typeof(StaffDrawable)
            .GetMethod("FinishBeginnerHorizontalLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[]
            {
                notes, noteLayouts, barLayouts, totalWidth, upperUsable,
                new List<GeneratedNote>(), Array.CreateInstance(noteLayouts.GetValue(0)!.GetType(), 0), Array.CreateInstance(barLayouts.GetValue(0)!.GetType(), 0), 0f, 0f,
                SafeLeft, leftMargin, leftMargin, layoutRightLimit,
                20f, 40f, 60f, 120f, 140f, 160f,
                false,
            });

        barLayoutsOut = Array.CreateInstance(barLayouts.GetValue(0)!.GetType(), 0);

        var xs = Enumerable.Range(0, noteLayouts.Length)
            .Select(i => ReadX(noteLayouts, i))
            .ToArray();

        return new LayoutSnap
        {
            NoteLayouts = noteLayouts,
            LayoutRightLimit = layoutRightLimit,
            LeftMargin = leftMargin,
            Xs = xs,
        };
    }

    private static void InvokeEnsureActivePairVisible(
        StaffDrawable drawable,
        List<GeneratedNote> notes,
        StaffNoteState[] states,
        Array noteLayouts,
        Array barLayouts,
        LayoutSnap snap)
    {
        typeof(StaffDrawable)
            .GetMethod("EnsureActivePairVisible", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[]
            {
                notes,
                states,
                noteLayouts,
                barLayouts,
                SafeLeft,
                snap.LeftMargin,
                snap.LayoutRightLimit,
            });
    }

    private static Array FinalizeEndBar(StaffDrawable drawable, Array noteLayouts)
        => (Array)typeof(StaffDrawable)
            .GetMethod("FinalizeSingleStaffEndBar", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { noteLayouts })!;

    private static void WarmVerticalLayout(StaffDrawable drawable, List<GeneratedNote> notes)
    {
        typeof(StaffDrawable)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(m => m.Name == "ComputeLayout" && m.GetParameters().Length == 1)
            .Invoke(drawable, new object[] { CanvasH });
        typeof(StaffDrawable)
            .GetField("_planInkGap", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(drawable, 12f);
    }

    private static int LastPitchedFlatIndex(IReadOnlyList<GeneratedNote> notes)
    {
        for (int i = notes.Count - 1; i >= 0; i--)
        {
            if (!notes[i].IsRest)
                return i;
        }
        return -1;
    }

    private static int PreviousPitchedFlatIndex(IReadOnlyList<GeneratedNote> notes, int lastPitched)
    {
        for (int i = lastPitched - 1; i >= 0; i--)
        {
            if (!notes[i].IsRest)
                return i;
        }
        return -1;
    }

    private static float ReadX(Array layouts, int i)
        => (float)layouts.GetValue(i)!.GetType().GetField("X")!.GetValue(layouts.GetValue(i)!)!;

    private static void AssertNotesDoNotPassFinalBar(
        StaffDrawable drawable,
        List<GeneratedNote> notes,
        Array noteLayouts,
        Array endBars)
    {
        Assert.NotEmpty(endBars);
        float barX = ReadX(endBars, 0);
        var inkRight = typeof(StaffDrawable).GetMethod(
            "NoteInkRightForLayout", BindingFlags.Instance | BindingFlags.NonPublic)!;

        for (int i = 0; i < notes.Count; i++)
        {
            if (notes[i].IsRest)
                continue;
            float right = (float)inkRight.Invoke(drawable, new object[] { noteLayouts.GetValue(i)! })!;
            Assert.True(right <= barX + 0.5f,
                $"Note {i} ink right {right:F1} extends past end bar at {barX:F1}");
        }
    }
}
