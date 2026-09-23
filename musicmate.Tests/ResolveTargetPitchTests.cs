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

    [Fact]
    public void ResolveWrittenEvaluationMidi_PrefersWrittenNameOverStoredMidi()
    {
        var note = new NoteInfo { Midi = 69, Name = "B4" };
        Assert.Equal(71, NoteSessionService.ResolveWrittenEvaluationMidi(note));
    }

    [Fact]
    public void NotationScale_FollowsEffectiveScale_NotStaleSelectedScale()
    {
        // Assortment by Level / Random pick the effective scale; SelectedScale can still say "Major".
        // The staff draws B Natural Minor (2 sharps: F#, C#), so A must stay natural.
        var session = new NoteSessionService();
        session.RestoreRepeatSameGenerationContext(
            key: "B",
            selectedScale: "Major",
            effectiveScale: "Natural Minor",
            scaleMode: ScaleSelectionMode.ByLevel,
            isRandomMode: false,
            tune: "Selected Scale");

        var (key, scale) = session.GetNotationKeyAndScale();
        Assert.Equal("B", key);
        Assert.Equal("Natural Minor", scale);

        var note = new GeneratedNote
        {
            MidiNumber = 69,
            Letter = 'A',
            Octave = 4,
            SpelledName = "A4",
            Accidental = Accidental.None,
        };

        var (midi, name) = NoteSessionService.ResolveTargetPitch(note, key, scale);
        Assert.Equal(69, midi);
        Assert.Equal("A4", name);
    }

    [Fact]
    public void NaturalLetterInFlatKey_IgnoresStaleChromaticMidiFromPitchPool()
    {
        var note = new GeneratedNote
        {
            MidiNumber = 68,
            Letter = 'A',
            Octave = 4,
            SpelledName = "A4",
            Accidental = Accidental.None,
        };

        var (midi, name) = NoteSessionService.ResolveTargetPitch(note, "F", "Major");

        Assert.Equal(69, midi);
        Assert.Equal("A4", name);
    }

    [Fact]
    public void ResolveTargetPitch_BMajor_StillSharpensASoTheTestGuardsTheKeyDifference()
    {
        var note = new GeneratedNote
        {
            MidiNumber = 69,
            Letter = 'A',
            Octave = 4,
            SpelledName = "A4",
            Accidental = Accidental.None,
        };

        var (midi, name) = NoteSessionService.ResolveTargetPitch(note, "B", "Major");
        Assert.Equal(70, midi);
        Assert.Equal("A#4", name);
    }
}
