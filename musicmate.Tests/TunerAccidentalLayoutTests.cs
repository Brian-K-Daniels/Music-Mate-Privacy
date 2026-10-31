using System.Reflection;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Tuner staff: body accidentals must clear the notehead using glyph bounds, not anchor points.
/// </summary>
public class TunerAccidentalLayoutTests
{
    private const float CanvasW = 835f;
    private const float CanvasH = 200f;
    private const float SafeLeft = 0f;

    public static IEnumerable<object[]> AccidentalSpellings =>
        new List<object[]>
        {
            new object[] { "Db4", true, false },
            new object[] { "C#4", false, false },
            new object[] { "F#4", false, false },
            new object[] { "Bb3", true, false },
            new object[] { "G##4", false, false },
            new object[] { "Cbb4", true, false },
        };

    [Theory]
    [MemberData(nameof(AccidentalSpellings))]
    public void FlatAndSharpSpellings_PreserveMinimumGap(string spelling, bool isFlat, bool isNatural)
    {
        var snap = LayoutTunerNote(spelling);
        AssertAccidentalClearsNotehead(snap.Drawable, snap.NoteLayouts, 0);
        Assert.Equal(isFlat, ReadBool(snap.NoteLayouts, 0, "AccidentalIsFlat"));
        Assert.Equal(isNatural, ReadBool(snap.NoteLayouts, 0, "AccidentalIsNatural"));
    }

    [Fact]
    public void NaturalSign_PreservesMinimumGap()
    {
        var snap = LayoutTunerNote("Db4");
        float centerX = ReadFloat(snap.NoteLayouts, 0, "X");
        InvokeResolveBounds(snap.Drawable, centerX, isFlat: false, isNatural: true,
            out float boxLeft, out float boxRight);
        float noteHeadLeft = InvokeFloat(snap.Drawable, "NoteHeadLeft", centerX);
        float minGap = MinimumGap(snap.Drawable);
        Assert.True(boxRight <= noteHeadLeft - minGap + 0.01f);
        Assert.True(boxLeft < noteHeadLeft);
    }

    [Theory]
    [InlineData("Db2")]
    [InlineData("F#6")]
    public void HighAndLowStaffPositions_PreserveMinimumGap(string spelling)
    {
        var snap = LayoutTunerNote(spelling);
        Assert.True(ReadBool(snap.NoteLayouts, 0, "HasAccidental"));
        AssertAccidentalClearsNotehead(snap.Drawable, snap.NoteLayouts, 0);
    }

    [Fact]
    public void LedgerLineNote_PreservesMinimumGap()
    {
        var snap = LayoutTunerNote("Db2");
        Assert.True(ReadBool(snap.NoteLayouts, 0, "HasAccidental"));
        AssertAccidentalClearsNotehead(snap.Drawable, snap.NoteLayouts, 0);
    }

    [Fact]
    public void InstrumentChange_RelayoutPreservesMinimumGap()
    {
        var bbSnap = LayoutTunerNote("F#4", instrumentTranspose: 2);
        var fluteSnap = LayoutTunerNote("G#4", instrumentTranspose: 0);

        AssertAccidentalClearsNotehead(bbSnap.Drawable, bbSnap.NoteLayouts, 0);
        AssertAccidentalClearsNotehead(fluteSnap.Drawable, fluteSnap.NoteLayouts, 0);
    }

    [Fact]
    public void ContinuousTunerUpdate_NaturalFlatSharp_RecalculatesAccidentalX()
    {
        var natural = LayoutTunerNote("D4");
        var flat = LayoutTunerNote("Db4");
        var sharp = LayoutTunerNote("C#4");

        if (ReadBool(flat.NoteLayouts, 0, "HasAccidental"))
            AssertAccidentalClearsNotehead(flat.Drawable, flat.NoteLayouts, 0);
        if (ReadBool(sharp.NoteLayouts, 0, "HasAccidental"))
            AssertAccidentalClearsNotehead(sharp.Drawable, sharp.NoteLayouts, 0);

        if (ReadBool(flat.NoteLayouts, 0, "HasAccidental") && ReadBool(sharp.NoteLayouts, 0, "HasAccidental"))
        {
            float flatAccX = ReadFloat(flat.NoteLayouts, 0, "AccidentalX");
            float sharpAccX = ReadFloat(sharp.NoteLayouts, 0, "AccidentalX");
            Assert.NotEqual(flatAccX, sharpAccX);
        }

        Assert.NotEqual(ReadFloat(natural.NoteLayouts, 0, "AccidentalX"),
            ReadFloat(flat.NoteLayouts, 0, "AccidentalX"));
    }

    [Fact]
    public void AccidentalBounds_DoNotOverlapNoteheadBounds()
    {
        var snap = LayoutTunerNote("Db4");
        float centerX = ReadFloat(snap.NoteLayouts, 0, "X");
        float headHalf = InvokeFloat(snap.Drawable, "NoteHeadVisualHalfWidth");
        float headLeft = centerX - headHalf;
        float headRight = centerX + headHalf;

        InvokeResolveBounds(snap.Drawable, snap.NoteLayouts, 0, out float boxLeft, out float boxRight);
        Assert.True(boxRight <= headLeft - MinimumGap(snap.Drawable) + 0.01f);
        Assert.True(boxLeft < headLeft);
        Assert.True(boxRight < headRight);
    }

    [Theory]
    [InlineData(520f)]
    [InlineData(835f)]
    [InlineData(1100f)]
    public void MinimumGapPreserved_AfterScaling(float canvasWidth)
    {
        var snap = LayoutTunerNote("Db4", canvasWidth: canvasWidth);
        AssertAccidentalClearsNotehead(snap.Drawable, snap.NoteLayouts, 0);
    }

    [Fact]
    public void TunerRedraw_IsIdempotent()
    {
        var snap = LayoutTunerNote("Db4");
        float x1 = ReadFloat(snap.NoteLayouts, 0, "X");
        float acc1 = ReadFloat(snap.NoteLayouts, 0, "AccidentalX");

        InvokeCenterTuner(snap.Drawable, snap.NoteLayouts, snap.SafeLeft, snap.LeftMargin, snap.LayoutRightLimit);
        float x2 = ReadFloat(snap.NoteLayouts, 0, "X");
        float acc2 = ReadFloat(snap.NoteLayouts, 0, "AccidentalX");

        Assert.Equal(x1, x2, precision: 3);
        Assert.Equal(acc1, acc2, precision: 3);
    }

    private sealed class TunerLayoutSnap
    {
        public required StaffDrawable Drawable { get; init; }
        public required Array NoteLayouts { get; init; }
        public required float LayoutRightLimit { get; init; }
        public required float LeftMargin { get; init; }
        public required float SafeLeft { get; init; }
    }

    private static TunerLayoutSnap LayoutTunerNote(
        string spelling,
        string? keyOverride = null,
        string? scaleOverride = null,
        int instrumentTranspose = 0,
        float canvasWidth = CanvasW)
    {
        var note = NoteSessionService.TryBuildGeneratedNoteFromSpelledName(spelling)
            ?? throw new InvalidOperationException($"Could not build note from '{spelling}'");
        return LayoutTunerNote(note, keyOverride, scaleOverride, instrumentTranspose, canvasWidth);
    }

    private static TunerLayoutSnap LayoutTunerNote(
        GeneratedNote note,
        string? keyOverride = null,
        string? scaleOverride = null,
        int instrumentTranspose = 0,
        float canvasWidth = CanvasW)
    {
        var session = new NoteSessionService
        {
            Tune = "Tuner",
            ChildLevel = 31,
            Key = keyOverride ?? "C",
            SelectedScale = scaleOverride ?? "Major",
            LowestNote = "C2",
            HighestNote = "C7",
            MeterTimeSignature = "4/4",
        };

        if (instrumentTranspose != 0)
            session.Instrument = instrumentTranspose == 2 ? "Bb Clarinet" : "Flute";

        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null)
        {
            SingleStaffLayout = true,
            AvailableHeight = CanvasH,
            UpperNotes = new List<GeneratedNote> { note },
            LowerNotes = new List<GeneratedNote>(),
            UpperBarBeats = new List<double>(),
            LowerBarBeats = new List<double>(),
            UpperNoteStates = new[] { StaffNoteState.Correct },
            IsUpperActive = true,
            UpperHasEndBar = false,
            NotationKeyOverride = keyOverride,
            NotationScaleOverride = scaleOverride,
        };

        WarmVerticalLayout(drawable);

        object header = GetMethod(drawable, "ComputeHeaderMetrics")
            .Invoke(drawable, new object[] { SafeLeft })!;
        float leftMargin = (float)header.GetType().GetProperty("LeftMargin")!.GetValue(header)!;
        float layoutRightLimit = canvasWidth - 8f;
        float upperUsable = layoutRightLimit - SafeLeft - leftMargin - 16f;

        object plan = GetMethod(drawable, "PlanHorizontalLayout")
            .Invoke(drawable, new object[]
            {
                drawable.UpperNotes,
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
        Assert.NotEmpty(noteLayouts);

        var noteLayoutType = noteLayouts.GetValue(0)!.GetType();
        var barLayoutType = typeof(StaffDrawable).GetNestedType("BarLayout", BindingFlags.NonPublic)!;

        GetMethod(drawable, "FinishBeginnerHorizontalLayout")
            .Invoke(drawable, new object[]
            {
                drawable.UpperNotes, noteLayouts, barLayouts, totalWidth, upperUsable,
                new List<GeneratedNote>(), Array.CreateInstance(noteLayoutType, 0),
                Array.CreateInstance(barLayoutType, 0), 0f, 0f,
                SafeLeft, leftMargin, leftMargin, layoutRightLimit,
                30f, 50f, 70f, 130f, 150f, 170f,
                false,
            });

        InvokeCenterTuner(drawable, noteLayouts, SafeLeft, leftMargin, layoutRightLimit);

        return new TunerLayoutSnap
        {
            Drawable = drawable,
            NoteLayouts = noteLayouts,
            LayoutRightLimit = layoutRightLimit,
            LeftMargin = leftMargin,
            SafeLeft = SafeLeft,
        };
    }

    private static void InvokeCenterTuner(
        StaffDrawable drawable,
        Array noteLayouts,
        float safeLeft,
        float leftMargin,
        float layoutRightLimit)
    {
        GetMethod(drawable, "CenterTunerReferenceNote")
            .Invoke(drawable, new object[] { noteLayouts, safeLeft, leftMargin, layoutRightLimit });
    }

    private static void AssertAccidentalClearsNotehead(StaffDrawable drawable, Array noteLayouts, int index)
    {
        Assert.True(ReadBool(noteLayouts, index, "HasAccidental"));
        InvokeResolveBounds(drawable, noteLayouts, index, out _, out float boxRight);
        float centerX = ReadFloat(noteLayouts, index, "X");
        float noteHeadLeft = InvokeFloat(drawable, "NoteHeadLeft", centerX);
        float minGap = MinimumGap(drawable);
        Assert.True(boxRight <= noteHeadLeft - minGap + 0.01f,
            $"Accidental right {boxRight:F2} must be <= notehead left {noteHeadLeft:F2} minus gap {minGap:F2}");

        float syncedAccX = ReadFloat(noteLayouts, index, "AccidentalX");
        Assert.Equal(ReadFloat(noteLayouts, index, "AccidentalX"),
            InvokeAccidentalBoxLeft(drawable, noteLayouts, index), precision: 2);
        Assert.True(syncedAccX < noteHeadLeft);
    }

    private static void InvokeResolveBounds(
        StaffDrawable drawable,
        Array noteLayouts,
        int index,
        out float boxLeft,
        out float boxRight)
    {
        float centerX = ReadFloat(noteLayouts, index, "X");
        bool isFlat = ReadBool(noteLayouts, index, "AccidentalIsFlat");
        bool isNatural = ReadBool(noteLayouts, index, "AccidentalIsNatural");
        InvokeResolveBounds(drawable, centerX, isFlat, isNatural, out boxLeft, out boxRight);
    }

    private static void InvokeResolveBounds(
        StaffDrawable drawable,
        float centerX,
        bool isFlat,
        bool isNatural,
        out float boxLeft,
        out float boxRight)
    {
        object?[] args = { centerX, isFlat, isNatural, 0f, 0f };
        GetMethod(drawable, "ResolveBodyAccidentalHorizontalBounds")
            .Invoke(drawable, args);
        boxLeft = (float)args[3]!;
        boxRight = (float)args[4]!;
    }

    private static float InvokeAccidentalBoxLeft(StaffDrawable drawable, Array noteLayouts, int index)
    {
        float centerX = ReadFloat(noteLayouts, index, "X");
        bool isFlat = ReadBool(noteLayouts, index, "AccidentalIsFlat");
        bool isNatural = ReadBool(noteLayouts, index, "AccidentalIsNatural");
        return (float)GetMethod(drawable, "AccidentalBoxLeft")
            .Invoke(drawable, new object[] { centerX, isFlat, isNatural })!;
    }

    private static float MinimumGap(StaffDrawable drawable)
        => (float)typeof(StaffDrawable)
            .GetField("MinimumAccidentalNoteGap", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    private static float InvokeFloat(StaffDrawable drawable, string method, params object[] args)
        => (float)GetMethod(drawable, method).Invoke(drawable, args)!;

    private static void WarmVerticalLayout(StaffDrawable drawable)
    {
        typeof(StaffDrawable)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(m => m.Name == "ComputeLayout" && m.GetParameters().Length == 1)
            .Invoke(drawable, new object[] { CanvasH });
        typeof(StaffDrawable)
            .GetField("_planInkGap", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(drawable, 12f);
    }

    private static MethodInfo GetMethod(StaffDrawable drawable, string name)
        => typeof(StaffDrawable).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing method {name}");

    private static float ReadFloat(Array layouts, int i, string field)
        => (float)layouts.GetValue(i)!.GetType().GetField(field)!.GetValue(layouts.GetValue(i)!)!;

    private static bool ReadBool(Array layouts, int i, string field)
        => (bool)layouts.GetValue(i)!.GetType().GetField(field)!.GetValue(layouts.GetValue(i)!)!;
}
