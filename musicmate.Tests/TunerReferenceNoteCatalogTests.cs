using musicmate.Services;

namespace musicmate.Tests;

public class TunerReferenceNoteCatalogTests
{
    [Theory]
    // C instrument
    [InlineData("concert-pitch", 60, 60)]   // written C4 → concert C4 (~261.63 Hz)
    [InlineData("concert-pitch", 71, 71)]   // written B4 → concert B4 (~493.88 Hz) — must NOT become C6 (84)
    [InlineData("concert-pitch", 72, 72)]   // written C5 → concert C5 (~523.25 Hz)
    // Bb instrument (offset -2): written C4 → concert Bb3
    [InlineData("bb-clarinet", 60, 58)]
    // Eb instrument (offset -9): written C4 → concert Eb3
    [InlineData("eb-alto-sax", 60, 51)]
    // F instrument (offset -7): written C4 → concert F3
    [InlineData("f-horn", 60, 53)]
    public void ConcertPitchPath_WrittenMidi_TransposeOnce(
        string instrumentId, int writtenMidi, int expectedConcertMidi)
    {
        var profile = InstrumentCatalog.All.First(p => p.Id == instrumentId);

        int concertMidi = TunerReferenceNoteCatalog.ToConcertMidi(writtenMidi, profile.TransposeOffset);
        double hz = TunerReferenceNoteCatalog.ConcertFrequencyHz(writtenMidi, profile.TransposeOffset);
        double expectedHz = NoteSessionService.MidiToFreqPublic(expectedConcertMidi);

        Assert.Equal(expectedConcertMidi, concertMidi);
        Assert.Equal(expectedHz, hz, precision: 6);

        // Transposition applied exactly once (no extra octave).
        Assert.Equal(writtenMidi + profile.TransposeOffset, concertMidi);
    }

    [Fact]
    public void StandardMidiNumbering_C4_B4_C5_C6()
    {
        Assert.Equal(60, NoteSessionService.NoteNameToMidi("C4"));
        Assert.Equal(71, NoteSessionService.NoteNameToMidi("B4"));
        Assert.Equal(72, NoteSessionService.NoteNameToMidi("C5"));
        Assert.Equal(84, NoteSessionService.NoteNameToMidi("C6"));
        Assert.Equal("B4", NoteSessionService.MidiToNoteName(71, flats: false));
        Assert.InRange(NoteSessionService.MidiToFreqPublic(71), 493.0, 494.5);
        Assert.InRange(NoteSessionService.MidiToFreqPublic(84), 1046.0, 1047.5);
    }

    [Fact]
    public void ClampToRange_DescendingPickerOrder_DoesNotCollapseToHighest()
    {
        // Reproduce the B4→C6 bug: picker choices are high→low, and the old
        // ClampToRange treated midis[0] as the low bound (actually C6).
        var descending = new[] { 84, 83, 72, 71, 60, 55 }; // C6 … G3 style
        Assert.Equal(71, TunerReferenceNoteCatalog.ClampToRange(71, descending));
        Assert.Equal(60, TunerReferenceNoteCatalog.ClampToRange(60, descending));
        Assert.Equal(84, TunerReferenceNoteCatalog.ClampToRange(84, descending));
        Assert.Equal(55, TunerReferenceNoteCatalog.ClampToRange(50, descending));
        Assert.Equal(84, TunerReferenceNoteCatalog.ClampToRange(90, descending));
    }

    [Fact]
    public void ClampToRange_AscendingOrder_StillWorks()
    {
        var midis = new[] { 60, 61, 62, 63 };
        Assert.Equal(60, TunerReferenceNoteCatalog.ClampToRange(50, midis));
        Assert.Equal(63, TunerReferenceNoteCatalog.ClampToRange(90, midis));
        Assert.Equal(61, TunerReferenceNoteCatalog.ClampToRange(61, midis));
    }

    [Fact]
    public void PickerLabel_BlackKeysShowBothSpellingsWithUnicodeAccidentals()
    {
        int cSharp = NoteSessionService.NoteNameToMidi("C#4");
        Assert.Equal("C♯4 / D♭4", TunerReferenceNoteCatalog.FormatPickerLabel(cSharp));
        Assert.Equal("C♯4", TunerReferenceNoteCatalog.FormatCompactWrittenLabel(cSharp));
        Assert.Equal("G♯4", TunerReferenceNoteCatalog.FormatCompactWrittenLabel(
            NoteSessionService.NoteNameToMidi("G#4")));
        Assert.Equal("D4", TunerReferenceNoteCatalog.FormatPickerLabel(NoteSessionService.NoteNameToMidi("D4")));
        Assert.Equal("Written D♭4 / C♯4 + 9¢",
            TunerReferenceNoteCatalog.FormatHeardDisplayLabel(cSharp, 9));
        Assert.Equal("Written D4",
            TunerReferenceNoteCatalog.FormatWrittenDisplayLabel(
                NoteSessionService.NoteNameToMidi("D4"), preferFlats: false));
        Assert.Equal("B4", TunerReferenceNoteCatalog.FormatPickerLabel(71));
    }

    [Fact]
    public void BuildChoices_UsesFullPracticalRange_HighestFirst()
    {
        var clarinet = InstrumentCatalog.All.First(p => p.Id == "bb-clarinet");
        var choices = TunerReferenceNoteCatalog.BuildChoices(clarinet, preferFlatsForStaff: true);
        Assert.Equal(NoteSessionService.NoteNameToMidi(clarinet.PracticalHighestNote), choices[0].WrittenMidi);
        Assert.Equal(NoteSessionService.NoteNameToMidi(clarinet.PracticalLowestNote), choices[^1].WrittenMidi);
        Assert.True(choices[0].WrittenMidi > choices[^1].WrittenMidi);
        Assert.Contains(choices, c => c.PickerLabel.Contains('♯') && c.PickerLabel.Contains('♭'));

        // Selecting B4 from descending choices must keep WrittenMidi 71 (not clamp to highest).
        var midis = choices.Select(c => c.WrittenMidi).ToList();
        Assert.Equal(71, TunerReferenceNoteCatalog.ClampToRange(71, midis));
    }

    [Fact]
    public void DefaultMiddleMidi_IsPitchMiddle_RegardlessOfListOrder()
    {
        var ascending = new[] { 55, 60, 65, 70, 84 };
        var descending = ascending.Reverse().ToArray();
        int midAsc = TunerReferenceNoteCatalog.DefaultMiddleMidi(ascending);
        int midDesc = TunerReferenceNoteCatalog.DefaultMiddleMidi(descending);
        Assert.Equal(midAsc, midDesc);
        Assert.InRange(midAsc, 55, 84);
    }
}
