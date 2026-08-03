using musicmate.Services;

namespace musicmate.Tests;

public class TunerReferenceNoteCatalogTests
{
    [Theory]
    [InlineData("concert-pitch", 60, 60)]   // written C4 → concert C4
    [InlineData("bb-clarinet", 60, 58)]     // written C4 → concert Bb3
    [InlineData("eb-alto-sax", 60, 51)]     // written C4 → concert Eb3
    [InlineData("f-horn", 60, 53)]          // written C4 → concert F3
    public void ConcertMidi_MatchesInstrumentTranspose(string instrumentId, int writtenMidi, int expectedConcertMidi)
    {
        var profile = InstrumentCatalog.All.First(p => p.Id == instrumentId);
        double hz = TunerReferenceNoteCatalog.ConcertFrequencyHz(writtenMidi, profile.TransposeOffset);
        double expectedHz = NoteSessionService.MidiToFreqPublic(expectedConcertMidi);
        Assert.Equal(expectedHz, hz, precision: 6);
    }

    [Fact]
    public void PickerLabel_BlackKeysShowBothSpellingsWithUnicodeAccidentals()
    {
        int cSharp = NoteSessionService.NoteNameToMidi("C#4");
        Assert.Equal("C♯4 / D♭4", TunerReferenceNoteCatalog.FormatPickerLabel(cSharp));
        Assert.Equal("D4", TunerReferenceNoteCatalog.FormatPickerLabel(NoteSessionService.NoteNameToMidi("D4")));
        Assert.Equal("Written D♭4 / C♯4 + 9¢",
            TunerReferenceNoteCatalog.FormatHeardDisplayLabel(cSharp, 9));
        Assert.Equal("Written D4",
            TunerReferenceNoteCatalog.FormatWrittenDisplayLabel(
                NoteSessionService.NoteNameToMidi("D4"), preferFlats: false));
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
    }

    [Fact]
    public void ClampToRange_PicksNearestWhenOutside()
    {
        var midis = new[] { 60, 61, 62, 63 };
        Assert.Equal(60, TunerReferenceNoteCatalog.ClampToRange(50, midis));
        Assert.Equal(63, TunerReferenceNoteCatalog.ClampToRange(90, midis));
        Assert.Equal(61, TunerReferenceNoteCatalog.ClampToRange(61, midis));
    }
}
