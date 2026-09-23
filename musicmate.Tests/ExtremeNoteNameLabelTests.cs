using System.Reflection;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Extreme ledger notes: smart note-name placement and vertical clearance.
/// </summary>
public class ExtremeNoteNameLabelTests
{
    private const float CanvasW = 835f;
    private const float CanvasH = 280f;
    private const float MinClearance = StaffDrawable.NoteNameMinEdgeClearancePx;

    [Fact]
    public void OrdinaryMidStaffNote_KeepsPreferredOutsidePlacement()
    {
        // B4 at staff mid → prefer above (not below mid).
        float staffTop = 80f, staffBot = 128f, mid = 104f, noteR = 5f;
        float safeTop = 0f, safeBottom = 280f;
        var above = StaffDrawable.ResolveNoteNameLabelLayout(
            200f, mid - 2f, noteR, staffTop, staffBot, safeTop, safeBottom);
        Assert.Equal(StaffDrawable.NoteNameLabelSide.Above, above.Side);

        var below = StaffDrawable.ResolveNoteNameLabelLayout(
            200f, mid + 8f, noteR, staffTop, staffBot, safeTop, safeBottom);
        Assert.Equal(StaffDrawable.NoteNameLabelSide.Below, below.Side);
    }

    [Fact]
    public void HighE6Class_FlipsInwardWhenPreferredAboveWouldViolateClearance()
    {
        float staffTop = 40f, staffBot = 88f, noteR = 5f;
        // Note near top of canvas — preferred above would cross safe top.
        float noteY = 22f;
        float safeTop = 0f, safeBottom = 280f;
        float preferredAboveY = noteY - noteR - 15f;
        Assert.True(preferredAboveY < safeTop + MinClearance);

        var label = StaffDrawable.ResolveNoteNameLabelLayout(
            120f, noteY, noteR, staffTop, staffBot, safeTop, safeBottom);
        Assert.Equal(StaffDrawable.NoteNameLabelSide.Below, label.Side);
        AssertLabelClearance(label, safeTop, safeBottom);
        AssertNoNoteheadOverlap(label, 120f, noteY, noteR);
    }

    [Fact]
    public void LowF3Class_FlipsInwardWhenPreferredBelowWouldViolateClearance()
    {
        float staffTop = 180f, staffBot = 228f, noteR = 5f;
        float noteY = 255f;
        float safeTop = 0f, safeBottom = 280f;
        float preferredBelowY = noteY + noteR + 5f;
        Assert.True(preferredBelowY + StaffDrawable.NoteNameLabelHeight > safeBottom - MinClearance);

        var label = StaffDrawable.ResolveNoteNameLabelLayout(
            120f, noteY, noteR, staffTop, staffBot, safeTop, safeBottom);
        Assert.Equal(StaffDrawable.NoteNameLabelSide.Above, label.Side);
        AssertLabelClearance(label, safeTop, safeBottom);
        AssertNoNoteheadOverlap(label, 120f, noteY, noteR);
    }

    [Fact]
    public void BothVerticalSidesUnsafe_PlacesBesideRight_AwayFromAccidentalSide()
    {
        float staffTop = 40f, staffBot = 88f, noteR = 5f;
        float noteY = 40f;
        // Band tall enough for the label with clearance, but not for above/below of this noteY.
        float safeTop = 30f, safeBottom = 70f;
        var label = StaffDrawable.ResolveNoteNameLabelLayout(
            200f, noteY, noteR, staffTop, staffBot, safeTop, safeBottom);
        Assert.Equal(StaffDrawable.NoteNameLabelSide.BesideRight, label.Side);
        Assert.True(label.X >= 200f + noteR,
            "Beside-right keeps label clear of left-side accidental zone");
        AssertLabelClearance(label, safeTop, safeBottom);
        AssertNoNoteheadOverlap(label, 200f, noteY, noteR);
    }

    [Fact]
    public void HighNoteWithAccidental_BesideOrInward_DoesNotCoverNotehead()
    {
        float staffTop = 50f, staffBot = 98f, noteR = 5f;
        float noteY = 24f;
        var label = StaffDrawable.ResolveNoteNameLabelLayout(
            180f, noteY, noteR, staffTop, staffBot, safeTop: 0f, safeBottom: 280f);
        Assert.NotEqual(StaffDrawable.NoteNameLabelSide.Above, label.Side);
        AssertNoNoteheadOverlap(label, 180f, noteY, noteR);
        // Accidental sits left of head — label must not extend into that strip when beside.
        if (label.Side == StaffDrawable.NoteNameLabelSide.BesideRight)
            Assert.True(label.X >= 180f + noteR);
    }

    [Fact]
    public void Layout_E6AndFSharp3_WithNamesOn_LabelsMeetClearance()
    {
        var session = Session(95, "F", "Major", "E3", "E6");
        session.NoteNameDisplay = "All notes";
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);

        var upper = new List<GeneratedNote> { Make("E6", 0), Make("C5", 4) };
        var lower = new List<GeneratedNote> { Make("A4", 0), Make("F#3", 4) };
        WarmLayout(drawable, CanvasH, upper, lower);
        var layout = ReadLayout(drawable);

        float safeTop = 0f;
        float safeBottom = CanvasH;

        // E6 on upper
        float e6Y = NoteY(drawable, upper[0], layout.UpperTop, layout.UpperMid);
        var e6Label = StaffDrawable.ResolveNoteNameLabelLayout(
            200f, e6Y, layout.NoteHeadR, layout.UpperTop, layout.UpperBot,
            safeTop, safeBottom);
        AssertLabelClearance(e6Label, safeTop, safeBottom);
        Assert.NotEqual(StaffDrawable.NoteNameLabelSide.Above, e6Label.Side);

        // F#3 on lower — preferred below when in lower half; flip only if unsafe.
        float f3Y = NoteY(drawable, lower[1], layout.LowerTop, layout.LowerMid);
        var f3Label = StaffDrawable.ResolveNoteNameLabelLayout(
            400f, f3Y, layout.NoteHeadR, layout.LowerTop, layout.LowerBot,
            safeTop, safeBottom);
        AssertLabelClearance(f3Label, safeTop, safeBottom);
        AssertNoNoteheadOverlap(f3Label, 400f, f3Y, layout.NoteHeadR);
        if (f3Y + layout.NoteHeadR + 5f + StaffDrawable.NoteNameLabelHeight
            > safeBottom - MinClearance)
        {
            Assert.NotEqual(StaffDrawable.NoteNameLabelSide.Below, f3Label.Side);
        }
    }

    [Fact]
    public void Layout_BothStavesExtreme_LabelsAndLedgers()
    {
        var session = Session(95, "Bb", "Natural Minor", "E3", "C6");
        session.NoteNameDisplay = "Current only";
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        var upper = new List<GeneratedNote> { Make("Db6", 0), Make("F3", 4) };
        var lower = new List<GeneratedNote> { Make("C6", 0), Make("F#3", 4) };
        WarmLayout(drawable, CanvasH, upper, lower);
        var layout = ReadLayout(drawable);

        float safeTop = 0f, safeBottom = CanvasH;
        foreach (var (note, top, mid, bot) in new[]
        {
            (upper[0], layout.UpperTop, layout.UpperMid, layout.UpperBot),
            (upper[1], layout.UpperTop, layout.UpperMid, layout.UpperBot),
            (lower[0], layout.LowerTop, layout.LowerMid, layout.LowerBot),
            (lower[1], layout.LowerTop, layout.LowerMid, layout.LowerBot),
        })
        {
            float ny = NoteY(drawable, note, top, mid);
            var label = StaffDrawable.ResolveNoteNameLabelLayout(
                220f, ny, layout.NoteHeadR, top, bot, safeTop, safeBottom);
            AssertLabelClearance(label, safeTop, safeBottom);
            AssertNoNoteheadOverlap(label, 220f, ny, layout.NoteHeadR);

            var (ledgerTop, ledgerBot) = StaffDrawable.EstimateLedgerVerticalExtent(
                ny, top, bot, layout.Sls);
            // Ledgers themselves should stay within the canvas (with a small stroke fudge).
            Assert.True(ledgerTop >= safeTop - 0.5f,
                $"Ledger top {ledgerTop:F1} clips safe top for {note.SpelledName}");
            Assert.True(ledgerBot <= safeBottom + 0.5f,
                $"Ledger bottom {ledgerBot:F1} clips safe bottom for {note.SpelledName}");
        }
    }

    [Fact]
    public void NoteNameDisplayOff_DoesNotAffectPlacementHelper_RangeUnchanged()
    {
        var range = ChildLevelProgression.NoteRangeForLevel(95);
        Assert.Equal("E2", range.Lo);
        Assert.Equal("C8", range.Hi);

        // Placement helper is independent of display mode; Off simply skips DrawNoteName.
        var label = StaffDrawable.ResolveNoteNameLabelLayout(
            100f, 100f, 5f, 80f, 128f, 0f, 280f);
        Assert.Equal(StaffDrawable.NoteNameLabelSide.Above, label.Side);
    }

    [Fact]
    public void G6Class_HighLedger_LabelClearanceOnLiveLayout()
    {
        var session = Session(95, "G", "Major", "G3", "G6");
        session.NoteNameDisplay = "All notes";
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        var upper = new List<GeneratedNote> { Make("G6", 0), Make("G4", 4) };
        var lower = new List<GeneratedNote> { Make("G5", 0), Make("G3", 4) };
        WarmLayout(drawable, CanvasH, upper, lower);
        var layout = ReadLayout(drawable);

        float ny = NoteY(drawable, upper[0], layout.UpperTop, layout.UpperMid);
        var label = StaffDrawable.ResolveNoteNameLabelLayout(
            180f, ny, layout.NoteHeadR, layout.UpperTop, layout.UpperBot, 0f, CanvasH);
        AssertLabelClearance(label, 0f, CanvasH);

        var (ledgerTop, _) = StaffDrawable.EstimateLedgerVerticalExtent(
            ny, layout.UpperTop, layout.UpperBot, layout.Sls);
        Assert.True(ledgerTop >= -0.5f, $"G6 ledger top {ledgerTop:F1}");
    }

    private static void AssertLabelClearance(
        StaffDrawable.NoteNameLabelLayout label, float safeTop, float safeBottom)
    {
        Assert.True(label.Y >= safeTop + MinClearance - 0.05f,
            $"Label top {label.Y:F1} needs ≥{MinClearance} from safeTop {safeTop:F1}");
        Assert.True(label.Y + label.Height <= safeBottom - MinClearance + 0.05f,
            $"Label bottom {label.Y + label.Height:F1} needs ≥{MinClearance} from safeBottom {safeBottom:F1}");
    }

    private static void AssertNoNoteheadOverlap(
        StaffDrawable.NoteNameLabelLayout label, float noteX, float noteY, float noteR)
    {
        float headTop = noteY - noteR * 1.5f * 0.5f;
        float headBot = noteY + noteR * 1.5f * 0.5f;
        float headLeft = noteX - noteR;
        float headRight = noteX + noteR;
        bool overlapX = label.X < headRight && label.X + label.Width > headLeft;
        bool overlapY = label.Y < headBot && label.Y + label.Height > headTop;
        Assert.False(overlapX && overlapY,
            $"Label {label.Side} overlaps notehead at ({noteX:F0},{noteY:F0})");
    }

    private static NoteSessionService Session(
        int level, string key, string scale, string lo, string hi)
        => new()
        {
            Key = key,
            SelectedScale = scale,
            ChildLevel = level,
            MeterTimeSignature = "4/4",
            ShowSignaturesOnBothStaffs = true,
            IsRandomMode = true,
            LowestNote = lo,
            HighestNote = hi,
        };

    private static GeneratedNote Make(string name, double beat)
    {
        int midi = NoteSessionService.NoteNameToMidi(name);
        char letter = name[0];
        int i = 1;
        Accidental acc = Accidental.None;
        if (i < name.Length && name[i] == '#') { acc = Accidental.Sharp; i++; }
        else if (i < name.Length && name[i] == 'b') { acc = Accidental.Flat; i++; }
        int oct = int.Parse(name[i..]);
        return new GeneratedNote
        {
            MidiNumber = midi,
            Letter = letter,
            Octave = oct,
            Accidental = acc,
            SpelledName = name,
            Duration = NoteDuration.Quarter,
            IsRest = false,
            BeatPosition = beat,
            MeasureIndex = (int)(beat / 4),
        };
    }

    private static void WarmLayout(
        StaffDrawable drawable, float height,
        List<GeneratedNote> upper, List<GeneratedNote> lower)
    {
        typeof(StaffDrawable)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(m => m.Name == "ComputeLayout" && m.GetParameters().Length == 3)
            .Invoke(drawable, new object[]
            {
                height,
                (IReadOnlyList<GeneratedNote>)upper,
                (IReadOnlyList<GeneratedNote>)lower,
            });
    }

    private static float NoteY(StaffDrawable d, GeneratedNote note, float staffTop, float staffMid)
        => (float)typeof(StaffDrawable)
            .GetMethod("NoteY", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(d, new object[] { note, staffTop, staffMid })!;

    private static LayoutSnap ReadLayout(StaffDrawable d)
    {
        var layout = typeof(StaffDrawable)
            .GetField("_layout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(d)!;
        T F<T>(string n) => (T)layout.GetType().GetField(n)!.GetValue(layout)!;
        return new LayoutSnap(
            F<float>("Sls"), F<float>("NoteHeadR"),
            F<float>("UpperTop"), F<float>("UpperMid"), F<float>("UpperBot"),
            F<float>("LowerTop"), F<float>("LowerMid"), F<float>("LowerBot"));
    }

    private readonly record struct LayoutSnap(
        float Sls, float NoteHeadR,
        float UpperTop, float UpperMid, float UpperBot,
        float LowerTop, float LowerMid, float LowerBot);
}
