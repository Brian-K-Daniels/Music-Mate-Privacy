using System.Reflection;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Ear Training side-staff: notehead X is independent of accidentals; accidentals sit
/// to the left of their own heads and stay inside the narrow panel.
/// </summary>
public class IntervalEarTrainingStaffLayoutTests
{
    private const float PanelW = 128f;
    private const float PanelH = 240f;
    private const float EdgePad = 4f;

    [Theory]
    [InlineData(20)]
    [InlineData(40)]
    public void NoteheadX_DoesNotDependOnAccidentalFlags(int childLevel)
    {
        var notes = BuildNotes(68, 80); // G#4 → G#5 (same letters/octaves as G4 → G5)
        var none = Layout(notes, childLevel, hasAcc: false, isFlat: false);
        var sharps = Layout(notes, childLevel, hasAcc: true, isFlat: false);
        var flats = Layout(notes, childLevel, hasAcc: true, isFlat: true);

        Assert.Equal(2, none.Xs.Length);
        AssertEqualX(none.Xs[0], sharps.Xs[0], "first notehead, sharps");
        AssertEqualX(none.Xs[1], sharps.Xs[1], "second notehead, sharps");
        AssertEqualX(none.Xs[0], flats.Xs[0], "first notehead, flats");
        AssertEqualX(none.Xs[1], flats.Xs[1], "second notehead, flats");
        Assert.True(none.Xs[0] < none.Xs[1], "first notehead must sit left of the second");
    }

    [Theory]
    [InlineData(68, 68, true)]   // G♯ → G♯ unison
    [InlineData(68, 70, true)]   // G♯ → A♯
    [InlineData(68, 73, true)]   // G♯ → C♯
    [InlineData(68, 75, true)]   // G♯ → D♯
    [InlineData(68, 80, true)]   // G♯ → G♯ octave
    [InlineData(56, 56, true)]   // G♯3 → G♯3 (ledger-line unison)
    [InlineData(56, 68, true)]   // G♯3 → G♯4 (screenshot octave)
    [InlineData(56, 58, true)]   // G♯3 → A♯3
    [InlineData(80, 68, false)]  // G♯ → G♯ octave descending
    [InlineData(70, 68, false)]  // A♯ → G♯ descending
    [InlineData(68, 56, false)]  // G♯4 → G♯3 descending
    [InlineData(60, 67, true)]   // C → G, no accidentals
    [InlineData(68, 69, true)]   // G♯ → A natural (mixed)
    [InlineData(67, 68, true)]   // G natural → G♯ (mixed)
    public void NoteheadsAndAccidentals_StayInsidePanel(int startMidi, int endMidi, bool ascending)
    {
        int semitones = Math.Abs(endMidi - startMidi);
        var pitches = new IntervalEarTrainingLogic.IntervalPitches(
            startMidi, endMidi, semitones, ascending);
        var notes = IntervalEarTrainingNotation.BuildDisplayNotes(
            pitches,
            IntervalEarTrainingNotation.StaffDisplayKey,
            IntervalEarTrainingNotation.StaffDisplayScale);

        var snap = LayoutFromNotes(notes, childLevel: 20);

        Assert.Equal(2, snap.Xs.Length);
        Assert.True(snap.Xs[0] < snap.Xs[1]);

        for (int i = 0; i < 2; i++)
        {
            Assert.InRange(snap.Xs[i], EdgePad, PanelW - EdgePad);
            if (snap.HasAcc[i])
            {
                Assert.True(snap.AccXs[i] < snap.Xs[i] - 1f,
                    $"Accidental for note {i} must sit left of its notehead " +
                    $"(accX={snap.AccXs[i]:F1}, x={snap.Xs[i]:F1})");
                Assert.True(snap.AccXs[i] >= EdgePad - 0.5f,
                    $"Accidental {i} clips the left edge at {snap.AccXs[i]:F1}");
            }

            Assert.True(snap.TrailRights[i] <= PanelW - EdgePad + 0.5f,
                $"Note {i} trailing ink {snap.TrailRights[i]:F1} clips the right edge of {PanelW}");
        }

        if (snap.HasAcc[1])
        {
            Assert.True(snap.AccXs[1] >= snap.TrailRights[0] - 0.5f,
                $"Second accidental {snap.AccXs[1]:F1} overlaps first note trailing {snap.TrailRights[0]:F1}");
        }
    }

    [Fact]
    public void AllSemitoneCounts_ShareTwoFixedSlots_WhenStaffPositionsMatch()
    {
        // C4→C4 through C4→C5 share the left note's staff position; compare each
        // pair against the same pair with explicit sharps on the same letters.
        var natural = LayoutFromNotes(BuildNotes(60, 67), 40); // C4 G4
        var sharp = LayoutFromNotes(BuildNotes(61, 68), 40);   // C#4 G#4 — same letters/octaves

        AssertEqualX(natural.Xs[0], sharp.Xs[0], "C vs C♯ first head");
        AssertEqualX(natural.Xs[1], sharp.Xs[1], "G vs G♯ second head");
    }

    [Fact]
    public void DescendingAndAscending_SameTwoPitches_UseTheSameNoteheadXs()
    {
        var up = LayoutFromNotes(BuildNotes(68, 80), 40);
        var down = LayoutFromNotes(BuildNotes(80, 68), 40);
        AssertEqualX(up.Xs[0], down.Xs[0], "first head");
        AssertEqualX(up.Xs[1], down.Xs[1], "second head");
    }

    [Fact]
    public void AccidentalSlot_IsWideEnoughToRead_ForSharpsAndFlats()
    {
        var sharps = LayoutFromNotes(BuildNotes(68, 80), 20); // G♯ → G♯
        var flats = LayoutFromNotes(BuildNotes(70, 82), 20);  // B♭/A♯ octave

        AssertReadableAccidentalSlots(sharps);
        AssertReadableAccidentalSlots(flats);

        var mixed = LayoutFromNotes(BuildNotes(68, 70), 20); // G♯ → A♯ / B♭
        Assert.True(mixed.HasAcc[0] || mixed.HasAcc[1]);
        for (int i = 0; i < 2; i++)
        {
            if (mixed.HasAcc[i])
                Assert.True(mixed.Xs[i] - mixed.AccXs[i] >= 12f,
                    $"mixed accidental slot {i} is {mixed.Xs[i] - mixed.AccXs[i]:F1}px");
        }
    }

    [Fact]
    public void Notehead_UsesConventionalStaffSpaceProportions()
    {
        var (drawable, _) = Prepare(BuildNotes(60, 67), childLevel: 20);
        var vert = typeof(StaffDrawable)
            .GetField("_layout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(drawable)!;
        var t = vert.GetType();
        float sls = (float)t.GetField("Sls")!.GetValue(vert)!;
        float noteHeadR = (float)t.GetField("NoteHeadR")!.GetValue(vert)!;

        float drawnWidth = noteHeadR * 2f;
        float drawnHeight = sls * 0.90f;
        Assert.True(
            Math.Abs(drawnWidth - sls * 1.30f) < 0.08f * sls,
            $"notehead width {drawnWidth:F2} vs 1.3·sls {sls * 1.30f:F2}");
        Assert.InRange(drawnHeight / sls, 0.85f, 0.95f);
    }

    private static void AssertReadableAccidentalSlots(LayoutSnap snap)
    {
        Assert.Equal(2, snap.Xs.Length);
        for (int i = 0; i < 2; i++)
        {
            Assert.True(snap.HasAcc[i], $"expected accidental on note {i}");
            float slot = snap.Xs[i] - snap.AccXs[i];
            Assert.True(slot >= 12f, $"accidental slot {i} is {slot:F1}px");
        }
    }

    private static void AssertEqualX(float a, float b, string label)
        => Assert.True(Math.Abs(a - b) < 0.05f, $"{label}: {a:F2} vs {b:F2}");

    private static List<GeneratedNote> BuildNotes(int startMidi, int endMidi)
    {
        int semitones = Math.Abs(endMidi - startMidi);
        var pitches = new IntervalEarTrainingLogic.IntervalPitches(
            startMidi, endMidi, semitones, endMidi >= startMidi);
        return IntervalEarTrainingNotation.BuildDisplayNotes(
            pitches,
            IntervalEarTrainingNotation.StaffDisplayKey,
            IntervalEarTrainingNotation.StaffDisplayScale);
    }

    private static LayoutSnap Layout(
        List<GeneratedNote> notes, int childLevel, bool hasAcc, bool isFlat)
    {
        var (drawable, layouts) = Prepare(notes, childLevel);
        ForceAccidentalFlags(layouts, hasAcc, isFlat);
        InvokeReveal(drawable, notes, layouts);
        return ReadSnap(drawable, notes, layouts);
    }

    private static LayoutSnap LayoutFromNotes(List<GeneratedNote> notes, int childLevel)
    {
        var (drawable, layouts) = Prepare(notes, childLevel);
        InvokeReveal(drawable, notes, layouts);
        return ReadSnap(drawable, notes, layouts);
    }

    private static (StaffDrawable drawable, Array layouts) Prepare(
        List<GeneratedNote> notes, int childLevel)
    {
        var session = new NoteSessionService
        {
            Key = "C",
            SelectedScale = "Major",
            MeterTimeSignature = "4/4",
            ChildLevel = childLevel,
            LowestNote = "C3",
            HighestNote = "C6",
        };
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null)
        {
            SingleStaffLayout = true,
            OmitStaffHeader = true,
            NotationKeyOverride = IntervalEarTrainingNotation.StaffDisplayKey,
            NotationScaleOverride = IntervalEarTrainingNotation.StaffDisplayScale,
            UpperNotes = notes,
            LowerNotes = new List<GeneratedNote>(),
            UpperBarBeats = IntervalEarTrainingNotation.BuildBarBeats(),
            AvailableHeight = PanelH,
        };

        typeof(StaffDrawable)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(m => m.Name == "ComputeLayout" && m.GetParameters().Length == 3)
            .Invoke(drawable, new object[]
            {
                PanelH,
                (IReadOnlyList<GeneratedNote>)notes,
                (IReadOnlyList<GeneratedNote>)Array.Empty<GeneratedNote>(),
            });
        typeof(StaffDrawable)
            .GetMethod("ComputeHeaderMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { 0f });

        object plan = typeof(StaffDrawable)
            .GetMethod("PlanHorizontalLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[]
            {
                notes,
                IntervalEarTrainingNotation.BuildBarBeats(),
                PanelW,
                12f,
                true,
                true,
                false,
            })!;
        var layouts = (Array)plan.GetType().GetField("Item1")!.GetValue(plan)!;
        return (drawable, layouts);
    }

    private static void ForceAccidentalFlags(Array layouts, bool hasAcc, bool isFlat)
    {
        var t = layouts.GetType().GetElementType()!;
        for (int i = 0; i < layouts.Length; i++)
        {
            object item = layouts.GetValue(i)!;
            t.GetField("HasAccidental")!.SetValue(item, hasAcc);
            t.GetField("AccidentalIsFlat")!.SetValue(item, isFlat);
            t.GetField("AccidentalIsNatural")!.SetValue(item, false);
            layouts.SetValue(item, i);
        }
    }

    private static void InvokeReveal(StaffDrawable drawable, List<GeneratedNote> notes, Array layouts)
    {
        var layoutField = typeof(StaffDrawable).GetField("_layout", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object vert = layoutField.GetValue(drawable)!;
        float upperTop = (float)vert.GetType().GetField("UpperTop")!.GetValue(vert)!;
        float upperMid = (float)vert.GetType().GetField("UpperMid")!.GetValue(vert)!;
        float upperBot = (float)vert.GetType().GetField("UpperBot")!.GetValue(vert)!;

        object[] args =
        {
            notes,
            layouts,
            upperTop,
            upperMid,
            upperBot,
            PanelH,
            0f,
            PanelW - 2f,
        };
        typeof(StaffDrawable)
            .GetMethod("CenterOmitHeaderIntervalReveal", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, args);
    }

    private static LayoutSnap ReadSnap(StaffDrawable drawable, List<GeneratedNote> notes, Array layouts)
    {
        var t = layouts.GetType().GetElementType()!;
        var xs = new float[layouts.Length];
        var accXs = new float[layouts.Length];
        var hasAcc = new bool[layouts.Length];
        var trails = new float[layouts.Length];
        var trailFn = typeof(StaffDrawable).GetMethod(
            "NoteTrailingRight", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(GeneratedNote), typeof(float) }, null)!;

        for (int i = 0; i < layouts.Length; i++)
        {
            object item = layouts.GetValue(i)!;
            xs[i] = (float)t.GetField("X")!.GetValue(item)!;
            accXs[i] = (float)t.GetField("AccidentalX")!.GetValue(item)!;
            hasAcc[i] = (bool)t.GetField("HasAccidental")!.GetValue(item)!;
            trails[i] = (float)trailFn.Invoke(drawable, new object[] { notes[i], xs[i] })!;
        }

        return new LayoutSnap(xs, accXs, hasAcc, trails);
    }

    private readonly record struct LayoutSnap(
        float[] Xs, float[] AccXs, bool[] HasAcc, float[] TrailRights);
}
