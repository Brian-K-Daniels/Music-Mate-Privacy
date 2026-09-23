using System.Reflection;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Body accidentals must follow the key signature that is actually drawn.
/// Chromatic (and other suppress modes) hide the signature, so every written
/// alteration must appear as an explicit accidental until established in-bar.
/// </summary>
public class HiddenKeySignatureAccidentalTests
{
    [Fact]
    public void EbMajor_KeySignatureVisible_FirstEb_OmitsBodyFlat()
    {
        var drawable = NewDrawable("Eb", "Major");
        Assert.True(IsKeySignatureVisiblyDrawn(drawable));
        Assert.Equal("b", GetSignatureAccidentalForLetter(drawable, 'B'));
        Assert.Equal("b", GetSignatureAccidentalForLetter(drawable, 'E'));
        Assert.Equal("b", GetSignatureAccidentalForLetter(drawable, 'A'));

        var flags = Simulate(drawable, [Note("Eb4", Accidental.Flat, 0, 0)]);
        Assert.False(flags[0], "visible Eb signature already flats E — no body ♭");
    }

    [Fact]
    public void EbChromatic_KeySignatureHidden_FirstEb_DrawsFlat()
    {
        var drawable = NewDrawable("Eb", "Chromatic");
        Assert.False(IsKeySignatureVisiblyDrawn(drawable));
        Assert.Null(GetSignatureAccidentalForLetter(drawable, 'E'));

        var flags = Simulate(drawable, [Note("Eb4", Accidental.Flat, 0, 0)]);
        Assert.True(flags[0], "hidden signature → first Eb must show ♭");
    }

    [Fact]
    public void EbChromatic_KeySignatureHidden_FirstBb_DrawsFlat()
    {
        var flags = Simulate(NewDrawable("Eb", "Chromatic"), [Note("Bb4", Accidental.Flat, 0, 0)]);
        Assert.True(flags[0]);
    }

    [Fact]
    public void EbChromatic_KeySignatureHidden_FirstAb_DrawsFlat()
    {
        var flags = Simulate(NewDrawable("Eb", "Chromatic"), [Note("Ab4", Accidental.Flat, 0, 0)]);
        Assert.True(flags[0]);
    }

    [Fact]
    public void AccidentalState_ResetsAtEveryBarLine_WhenSignatureHidden()
    {
        var drawable = NewDrawable("Eb", "Chromatic");
        var notes = new List<GeneratedNote>
        {
            Note("Eb4", Accidental.Flat, 0, 0.0),
            Note("Eb4", Accidental.Flat, 0, 1.0),
            Note("Eb4", Accidental.Flat, 1, 4.0),
        };
        var flags = Simulate(drawable, notes, barBeats: [4.0]);
        Assert.True(flags[0]);
        Assert.False(flags[1], "same Eb later in measure may carry");
        Assert.True(flags[2], "new measure with no signature must redraw ♭");
    }

    [Fact]
    public void RepeatedEb_SameMeasure_MaySuppressRedundantFlat()
    {
        var flags = Simulate(
            NewDrawable("Eb", "Chromatic"),
            [Note("Eb4", Accidental.Flat, 0, 0), Note("Eb4", Accidental.Flat, 0, 1)]);
        Assert.True(flags[0]);
        Assert.False(flags[1]);
    }

    [Fact]
    public void NewMeasure_Eb_AgainReceivesFlat_WhenSignatureHidden()
    {
        var flags = Simulate(
            NewDrawable("Eb", "Chromatic"),
            [Note("Eb4", Accidental.Flat, 0, 0), Note("Eb4", Accidental.Flat, 1, 4)],
            barBeats: [4.0]);
        Assert.True(flags[0]);
        Assert.True(flags[1]);
    }

    [Fact]
    public void CMajor_NaturalNotes_Unaffected()
    {
        var drawable = NewDrawable("C", "Major");
        Assert.True(IsKeySignatureVisiblyDrawn(drawable));
        var flags = Simulate(drawable, [Note("E4", Accidental.None, 0, 0), Note("C4", Accidental.None, 0, 1)]);
        Assert.False(flags[0]);
        Assert.False(flags[1]);
    }

    [Fact]
    public void GMajorChromatic_HiddenSignature_ShowsFSharp()
    {
        var drawable = NewDrawable("G", "Chromatic");
        Assert.False(IsKeySignatureVisiblyDrawn(drawable));
        var flags = Simulate(drawable, [Note("F#4", Accidental.Sharp, 0, 0)]);
        Assert.True(flags[0]);
    }

    [Fact]
    public void DMajorChromatic_HiddenSignature_ShowsFSharpAndCSharp()
    {
        var drawable = NewDrawable("D", "Chromatic");
        Assert.False(IsKeySignatureVisiblyDrawn(drawable));
        var flags = Simulate(drawable,
        [
            Note("F#4", Accidental.Sharp, 0, 0),
            Note("C#5", Accidental.Sharp, 0, 1),
        ]);
        Assert.True(flags[0]);
        Assert.True(flags[1]);
    }

    [Fact]
    public void FMajorChromatic_HiddenSignature_ShowsBb()
    {
        var flags = Simulate(NewDrawable("F", "Chromatic"), [Note("Bb4", Accidental.Flat, 0, 0)]);
        Assert.True(flags[0]);
    }

    [Fact]
    public void GMajor_KeySignatureVisible_FirstFSharp_OmitsBodySharp()
    {
        var drawable = NewDrawable("G", "Major");
        Assert.True(IsKeySignatureVisiblyDrawn(drawable));
        Assert.Equal("#", GetSignatureAccidentalForLetter(drawable, 'F'));
        var flags = Simulate(drawable, [Note("F#4", Accidental.Sharp, 0, 0)]);
        Assert.False(flags[0]);
    }

    private static StaffDrawable NewDrawable(string key, string scale)
        => new(new NoteSessionService
        {
            Key = key,
            SelectedScale = scale,
            MeterTimeSignature = "4/4",
            ChildLevel = 31,
            ShowSignaturesOnBothStaffs = true,
            LowestNote = "C4",
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

    private static bool IsKeySignatureVisiblyDrawn(StaffDrawable drawable)
        => (bool)typeof(StaffDrawable)
            .GetMethod("IsKeySignatureVisiblyDrawn", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, null)!;

    private static string? GetSignatureAccidentalForLetter(StaffDrawable drawable, char letter)
        => (string?)typeof(StaffDrawable)
            .GetMethod("GetSignatureAccidentalForLetter", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, [letter]);

    private static List<bool> Simulate(
        StaffDrawable drawable,
        IReadOnlyList<GeneratedNote> notes,
        List<double>? barBeats = null)
    {
        barBeats ??= [];
        var tryResolve = typeof(StaffDrawable).GetMethod(
            "TryResolveBodyAccidental", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var resetBar = typeof(StaffDrawable).GetMethod(
            "ResetAccidentalStateIfCrossedBar", BindingFlags.Static | BindingFlags.NonPublic)!;

        var history = new Dictionary<(char, int), Accidental>();
        var cancelled = new HashSet<(char, int)>();
        double prevBeat = -1.0;
        double beatOrigin = notes.Count > 0 ? notes.Min(n => n.BeatPosition ?? 0) : 0;
        var flags = new List<bool>();

        for (int i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            double beat = (note.BeatPosition ?? 0.0) - beatOrigin;

            if (i > 0)
            {
                int prevMeasure = notes[i - 1].MeasureIndex ?? -1;
                int curMeasure = note.MeasureIndex ?? prevMeasure;
                if (curMeasure != prevMeasure)
                {
                    history.Clear();
                    cancelled.Clear();
                }
            }

            resetBar.Invoke(null, [beat, prevBeat, barBeats, beatOrigin, history, cancelled]);

            object?[] args = [note, history, cancelled, Accidental.None, false];
            tryResolve.Invoke(drawable, args);
            bool draw = (bool)args[4]!;
            var eff = (Accidental)args[3]!;
            if (eff != Accidental.None)
                history[(note.Letter, note.Octave)] = eff;
            flags.Add(draw);
            prevBeat = beat;
        }

        return flags;
    }
}
