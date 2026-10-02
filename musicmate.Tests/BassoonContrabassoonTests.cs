using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class BassoonContrabassoonTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public BassoonContrabassoonTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Fact]
    public void BothInstruments_AppearInThePicker_WithPracticalRanges()
    {
        Assert.Contains("Bassoon", InstrumentCatalog.DisplayNames);
        Assert.Contains("Contrabassoon", InstrumentCatalog.DisplayNames);

        var bassoon = InstrumentCatalog.Resolve("Bassoon");
        Assert.Equal("bassoon", bassoon.Id);
        Assert.Equal("C", bassoon.InstrumentKey);
        Assert.Equal(0, bassoon.TransposeOffset);
        Assert.Equal("Bb1", bassoon.PracticalLowestNote);
        Assert.Equal("Eb5", bassoon.PracticalHighestNote);
        Assert.Equal(Clef.Bass, bassoon.DefaultClef);
        Assert.True(bassoon.CanToggleNotationClef);
        Assert.Contains(Clef.Treble, bassoon.NotationClefs);

        var contra = InstrumentCatalog.Resolve("Contrabassoon");
        Assert.Equal("contrabassoon", contra.Id);
        Assert.Equal("C - 1 octave", contra.InstrumentKey);
        Assert.Equal(-12, contra.TransposeOffset);
        Assert.Equal("Bb1", contra.PracticalLowestNote);
        Assert.Equal("F4", contra.PracticalHighestNote);
        Assert.Equal(Clef.Bass, contra.DefaultClef);
        Assert.False(contra.CanToggleNotationClef);
        Assert.Equal(new[] { Clef.Bass }, contra.NotationClefs);
    }

    [Fact]
    public void Bassoon_SoundsAsWritten_ContrabassoonSoundsAnOctaveLower()
    {
        int writtenBb1 = NoteSessionService.NoteNameToMidi("Bb1");
        int writtenF4 = NoteSessionService.NoteNameToMidi("F4");
        Assert.Equal(34, writtenBb1);

        var bassoon = InstrumentCatalog.Resolve("bassoon");
        var contra = InstrumentCatalog.Resolve("contrabassoon");

        Assert.Equal(writtenBb1, TunerReferenceNoteCatalog.ToConcertMidi(writtenBb1, bassoon.TransposeOffset));
        Assert.Equal(writtenBb1 - 12, TunerReferenceNoteCatalog.ToConcertMidi(writtenBb1, contra.TransposeOffset));
        Assert.Equal(
            NoteSessionService.MidiToFreqPublic(writtenBb1),
            TunerReferenceNoteCatalog.ConcertFrequencyHz(writtenBb1, bassoon.TransposeOffset),
            precision: 6);
        Assert.Equal(
            NoteSessionService.MidiToFreqPublic(writtenBb1 - 12),
            TunerReferenceNoteCatalog.ConcertFrequencyHz(writtenBb1, contra.TransposeOffset),
            precision: 6);
        Assert.Equal(writtenF4 - 12, TunerReferenceNoteCatalog.ToConcertMidi(writtenF4, contra.TransposeOffset));
    }

    [Theory]
    [InlineData(0, "bassoon", "Bb1", "Eb5")]
    [InlineData(0, "contrabassoon", "Bb1", "F4")]
    [InlineData(25, "bassoon", "B3", "D5")]
    [InlineData(25, "contrabassoon", "B3", "F4")]
    [InlineData(100, "bassoon", "Bb1", "Eb5")]
    [InlineData(100, "contrabassoon", "Bb1", "F4")]
    public void LevelRange_UsesThePracticalCompass(int level, string instrumentId, string low, string high)
    {
        var session = new NoteSessionService { ChildLevel = level, Key = "C" };
        session.ClearNoteRangeCustomization();
        session.Instrument = instrumentId;

        Assert.Equal(NoteSessionService.NoteNameToMidi(low), NoteSessionService.NoteNameToMidi(session.LowestNote));
        Assert.Equal(NoteSessionService.NoteNameToMidi(high), NoteSessionService.NoteNameToMidi(session.HighestNote));
        if (level <= 0)
        {
            Assert.Equal(low, session.LowestNote);
            Assert.Equal(high, session.HighestNote);
        }
        Assert.Equal(
            NoteSessionService.NoteNameToMidi(low),
            session.AvailableInstrumentMidis.Min());
        Assert.Equal(
            NoteSessionService.NoteNameToMidi(high),
            session.AvailableInstrumentMidis.Max());
        Assert.Equal(instrumentId, _store["musicmate.Instrument"]);
    }

    [Fact]
    public void SelectingEitherInstrument_KeepsTheWrittenKey()
    {
        var session = new NoteSessionService { ChildLevel = 0, Key = "F" };
        session.ClearNoteRangeCustomization();

        session.Instrument = "bassoon";
        Assert.Equal("F", session.Key);
        Assert.Equal("F", session.GetConcertKey());
        Assert.Equal(0, session.InstrumentTransposeOffset);

        session.Instrument = "contrabassoon";
        Assert.Equal("F", session.Key);
        Assert.Equal("F", session.GetConcertKey());
        Assert.Equal(-12, session.InstrumentTransposeOffset);
        Assert.Equal(Clef.Bass, session.NotationClef);
    }

    [Fact]
    public void BassoonClefToggle_RedrawsStaffPlacement_AndPersists()
    {
        var session = new NoteSessionService { Instrument = "Bassoon", Key = "Bb" };
        Assert.Equal(Clef.Bass, session.NotationClef);
        int written = NoteSessionService.NoteNameToMidi("C3");
        double hz = TunerReferenceNoteCatalog.ConcertFrequencyHz(written, 0);
        session.NotesToDraw.Add(new NoteInfo { Midi = written, Name = "C3", TargetFreq = hz });

        int bassSteps = ClefStaffPosition.StepsBelowMiddle(Clef.Bass, 'C', 3);
        int trebleSteps = ClefStaffPosition.StepsBelowMiddle(Clef.Treble, 'C', 3);
        Assert.NotEqual(bassSteps, trebleSteps);
        Assert.Equal(bassSteps, ClefStaffPosition.StepsBelowMiddle(session.NotationClef, 'C', 3));

        session.ToggleNotationClef();
        Assert.Equal(Clef.Treble, session.NotationClef);
        Assert.Equal(trebleSteps, ClefStaffPosition.StepsBelowMiddle(session.NotationClef, 'C', 3));
        Assert.Equal(written, session.NotesToDraw[0].Midi);
        Assert.Equal("C3", session.NotesToDraw[0].Name);
        Assert.Equal(hz, session.NotesToDraw[0].TargetFreq);
        Assert.Equal(0, session.InstrumentTransposeOffset);
        Assert.Equal("Bb", session.Key);
        Assert.NotEqual(
            ClefStaffPosition.KeySignaturePositions(Clef.Bass, flats: true),
            ClefStaffPosition.KeySignaturePositions(session.NotationClef, flats: true));

        string? saved = session.BeginPlaybackInstrumentOverride();
        Assert.Null(saved);
        Assert.Equal(Clef.Treble, session.NotationClef);

        var reopened = new NoteSessionService { Instrument = "bassoon" };
        Assert.Equal(Clef.Treble, reopened.NotationClef);
        Assert.Equal("Treble", _store[NoteSessionService.NotationClefPreferenceKey("bassoon")]);
    }

    [Fact]
    public void Contrabassoon_StaysOnBassClef_EvenIfTrebleWasSaved()
    {
        _store[NoteSessionService.NotationClefPreferenceKey("contrabassoon")] = "Treble";
        var session = new NoteSessionService { Instrument = "Contrabassoon", ChildLevel = 0 };
        session.ClearNoteRangeCustomization();

        Assert.Equal(Clef.Bass, session.NotationClef);
        Assert.False(session.CanToggleNotationClef);
        Assert.Equal("Bass clef.", session.NotationClefAccessibilityText);
        Assert.Equal(9, ClefStaffPosition.StepsBelowMiddle(session.NotationClef, 'B', 1));
        Assert.NotEqual(
            ClefStaffPosition.StepsBelowMiddle(Clef.Treble, 'B', 1),
            ClefStaffPosition.StepsBelowMiddle(session.NotationClef, 'B', 1));

        int written = NoteSessionService.NoteNameToMidi("Bb1");
        double sounding = TunerReferenceNoteCatalog.ConcertFrequencyHz(written, -12);
        session.NotesToDraw.Add(new NoteInfo { Midi = written, Name = "Bb1", TargetFreq = sounding });

        session.ToggleNotationClef();
        Assert.Equal(Clef.Bass, session.NotationClef);
        Assert.Equal(written, session.NotesToDraw[0].Midi);
        Assert.Equal("Bb1", session.NotesToDraw[0].Name);
        Assert.Equal(sounding, session.NotesToDraw[0].TargetFreq);
        Assert.Equal(-12, session.InstrumentTransposeOffset);
        Assert.Equal("Bb1", session.LowestNote);
        Assert.Equal("F4", session.HighestNote);

        string? saved = session.BeginPlaybackInstrumentOverride();
        Assert.Equal("contrabassoon", saved);
        session.EndPlaybackInstrumentOverride(saved);
        Assert.Equal("contrabassoon", session.Instrument);
        Assert.Equal(Clef.Bass, session.NotationClef);
        Assert.Equal(written, session.NotesToDraw[0].Midi);
    }
}
