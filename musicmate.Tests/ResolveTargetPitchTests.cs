using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

public class ResolveTargetPitchTests
{
    [Fact]
    public void KeySignatureNote_GLineInEMajor_ExpectsGSharp()
    {
        var note = new GeneratedNote
        {
            MidiNumber = 67,
            Letter = 'G',
            Octave = 4,
            SpelledName = "G4",
            Accidental = Accidental.None,
        };

        var (midi, name) = NoteSessionService.ResolveTargetPitch(note, "E", "Major");

        Assert.Equal(68, midi);
        Assert.Equal("G#4", name);
    }

    [Fact]
    public void KeySignatureNote_AlreadyCorrectMidi_StillSpellsGSharp()
    {
        var note = new GeneratedNote
        {
            MidiNumber = 68,
            Letter = 'G',
            Octave = 4,
            SpelledName = "G4",
            Accidental = Accidental.None,
        };

        var (midi, name) = NoteSessionService.ResolveTargetPitch(note, "E", "Major");

        Assert.Equal(68, midi);
        Assert.Equal("G#4", name);
    }

    [Fact]
    public void ExplicitNatural_InSharpKey_StaysNatural()
    {
        var note = new GeneratedNote
        {
            MidiNumber = 67,
            Letter = 'G',
            Octave = 4,
            SpelledName = "G4",
            Accidental = Accidental.Natural,
        };

        var (midi, name) = NoteSessionService.ResolveTargetPitch(note, "E", "Major");

        Assert.Equal(67, midi);
        Assert.Equal("G4", name);
    }

    [Fact]
    public void ExplicitAccidental_UsesStoredMidi()
    {
        var note = new GeneratedNote
        {
            MidiNumber = 68,
            Letter = 'G',
            Octave = 4,
            SpelledName = "G#4",
            Accidental = Accidental.Sharp,
        };

        var (midi, name) = NoteSessionService.ResolveTargetPitch(note, "C", "Major");

        Assert.Equal(68, midi);
        Assert.Equal("G#4", name);
    }
}
