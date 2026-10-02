using System.Reflection;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Extra Music-page canvas height must enlarge notation (Sls / noteheads), not sit as empty slack.
/// Both staves remain inside the canvas.
/// </summary>
public class MusicStaffHeightScaleTests
{
    [Fact]
    public void TwoStaff_TallCanvas_UsesRaisedStaffLineSpacingCap()
    {
        var drawable = MakeDrawable();
        var upper = MidRangeNotes();
        var lower = MidRangeNotes(beatOffset: 16);
        var layout = Warm(drawable, 480f, upper, lower);

        Assert.Equal(StaffDrawable.MusicMaxStaffLineSpacing, layout.Sls);
        Assert.True(layout.NoteHeadR > 4.5f, $"Noteheads should grow with Sls=18; got r={layout.NoteHeadR:F2}");
        Assert.True(layout.LowerBot < 480f, "Lower staff must stay inside the canvas");
        Assert.True(layout.UpperTop >= 0f, "Upper staff must stay inside the canvas");
    }

    [Fact]
    public void TwoStaff_TallerCanvas_DoesNotLeaveSlsAtLegacyCapOf12()
    {
        var drawable = MakeDrawable();
        var upper = MidRangeNotes();
        var lower = MidRangeNotes(beatOffset: 16);
        var atLegacyHeight = Warm(drawable, 360f, upper, lower);
        var atTallHeight = Warm(drawable, 480f, upper, lower);

        Assert.True(atTallHeight.Sls > 12f,
            $"Tall Music canvas must exceed the legacy Sls cap of 12; got {atTallHeight.Sls:F2}");
        Assert.True(atTallHeight.Sls >= atLegacyHeight.Sls - 0.01f);
        Assert.True(atTallHeight.NoteHeadR >= atLegacyHeight.NoteHeadR - 0.01f);
        float scale = atTallHeight.Sls / 12f;
        Assert.InRange(scale, 1.2f, 1.51f);
    }

    [Fact]
    public void TwoStaff_ExtremeLedgers_StayInsideTallerCanvas()
    {
        var drawable = MakeDrawable();
        var upper = new List<GeneratedNote> { Note("C6", 0), Note("A2", 4) };
        var lower = new List<GeneratedNote> { Note("B5", 0), Note("C3", 4) };
        const float h = 520f;
        var layout = Warm(drawable, h, upper, lower);

        Assert.True(layout.UpperTop >= 0f);
        Assert.True(layout.LowerBot <= h,
            $"Lower staff bottom {layout.LowerBot:F1} must stay within canvas {h}");
        foreach (var (note, top, mid, bot) in new[]
        {
            (upper[0], layout.UpperTop, layout.UpperMid, layout.UpperBot),
            (upper[1], layout.UpperTop, layout.UpperMid, layout.UpperBot),
            (lower[0], layout.LowerTop, layout.LowerMid, layout.LowerBot),
            (lower[1], layout.LowerTop, layout.LowerMid, layout.LowerBot),
        })
        {
            float ny = NoteY(drawable, note, top, mid);
            var (ledgerTop, ledgerBot) = StaffDrawable.EstimateLedgerVerticalExtent(
                ny, top, bot, layout.Sls);
            Assert.True(ledgerTop >= -0.5f, $"Ledger top {ledgerTop:F1} clips for {note.SpelledName}");
            Assert.True(ledgerBot <= h + 0.5f, $"Ledger bottom {ledgerBot:F1} clips for {note.SpelledName}");

            var label = StaffDrawable.ResolveNoteNameLabelLayout(
                200f, ny, layout.NoteHeadR, top, bot, 0f, h,
                StaffDrawable.NoteNameMinEdgeClearancePx, layout.Sls / 12f);
            Assert.True(label.Y >= -0.5f, $"Label top clips for {note.SpelledName}");
            Assert.True(label.Y + label.Height <= h + 0.5f, $"Label bottom clips for {note.SpelledName}");
        }
    }

    private static StaffDrawable MakeDrawable()
    {
        var session = new NoteSessionService
        {
            Key = "C",
            SelectedScale = "Major",
            MeterTimeSignature = "4/4",
            ChildLevel = 40,
            ShowSignaturesOnBothStaffs = true,
            IsRandomMode = true,
            Tune = "Random",
        };
        return new StaffDrawable(session, new ThemeService(), safeArea: null);
    }

    private static List<GeneratedNote> MidRangeNotes(double beatOffset = 0)
        => new()
        {
            Note("C4", beatOffset),
            Note("E4", beatOffset + 1),
            Note("G4", beatOffset + 2),
            Note("C5", beatOffset + 3),
        };

    private static GeneratedNote Note(string name, double beat)
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

    private static LayoutSnap Warm(
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
        return ReadLayout(drawable);
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
            F<float>("UpperTop"), F<float>("UpperBot"),
            F<float>("LowerTop"), F<float>("LowerBot"),
            F<float>("LowerMid"), F<float>("UpperMid"));
    }

    private readonly record struct LayoutSnap(
        float Sls, float NoteHeadR,
        float UpperTop, float UpperBot,
        float LowerTop, float LowerBot,
        float LowerMid, float UpperMid);
}
