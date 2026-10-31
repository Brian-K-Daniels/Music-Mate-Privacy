using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class ArpeggioQualityKeyTests
{
    [Fact]
    public void TryResolveQuality_StripsLegacyRootFromLabel()
    {
        Assert.True(ArpeggioCatalog.TryResolveQuality("C major triad", out var pattern));
        Assert.Equal(ArpeggioCatalog.MajorTriad.Id, pattern.Id);
        Assert.Equal("Major triad", ArpeggioCatalog.QualityLabel(pattern));
        Assert.Equal("Major triad", ArpeggioCatalog.NormalizeQualityLabel("★ C major triad"));
    }

    [Fact]
    public void ApplyArpeggioQuality_KeepsWrittenTonic_AndUsesConcertRoot()
    {
        var session = new NoteSessionService
        {
            Instrument = "Concert Pitch",
            Key = "D",
            LowestNote = "A3",
            HighestNote = "C6",
        };

        session.ApplyArpeggioQuality(ArpeggioCatalog.MinorTriad);

        Assert.Equal("Arpeggio", session.Tune);
        Assert.Equal("D", session.Key);
        Assert.Equal("Minor triad", session.SelectedArpeggioDisplay);
        Assert.Equal(ArpeggioCatalog.MinorTriad.Id, session.SelectedArpeggioId);
        Assert.StartsWith("D", session.SelectedArpeggioRoot);
        Assert.Equal(("D", "Natural Minor"), session.GetNotationKeyAndScale());
    }

    [Fact]
    public void SyncArpeggioRootToCurrentKey_RetargetsConcertRootWithoutChangingQuality()
    {
        var session = new NoteSessionService
        {
            Instrument = "Concert Pitch",
            Key = "C",
            LowestNote = "A3",
            HighestNote = "C6",
        };
        session.ApplyArpeggioQuality(ArpeggioCatalog.MajorTriad);
        Assert.StartsWith("C", session.SelectedArpeggioRoot);

        session.Key = "G";
        session.SyncArpeggioRootToCurrentKey();

        Assert.Equal("Major triad", session.SelectedArpeggioDisplay);
        Assert.StartsWith("G", session.SelectedArpeggioRoot);
        Assert.Equal("G", session.Key);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(20, 1)]
    [InlineData(21, 2)]
    [InlineData(41, 3)]
    [InlineData(51, 5)]
    [InlineData(61, 6)]
    [InlineData(71, 7)]
    [InlineData(81, 8)]
    [InlineData(91, 9)]
    [InlineData(100, 9)]
    public void GetAvailablePatterns_UnlocksQualitiesByLevel(int level, int expectedCount)
    {
        var patterns = ArpeggioCatalog.GetAvailablePatterns(level);
        Assert.Equal(expectedCount, patterns.Count);
        Assert.Contains(patterns, p => p.Id == ArpeggioCatalog.MajorTriad.Id);
        if (level >= 21)
            Assert.Contains(patterns, p => p.Id == ArpeggioCatalog.MinorTriad.Id);
        if (level >= 100)
            Assert.Equal(ArpeggioCatalog.All.Count, patterns.Count);
    }

    [Fact]
    public void ResolvePracticeLevel_UsesSavedPreferenceWhenChildLevelUnset()
    {
        SessionPreferences.TestStore = new Dictionary<string, object?>();
        try
        {
            SessionPreferences.Set("ChildPractice.Level", 100);
            var session = new NoteSessionService { ChildLevel = 0 };
            Assert.Equal(100, session.ResolvePracticeLevel());
            Assert.Equal(9, ArpeggioCatalog.GetAvailablePatterns(session.ResolvePracticeLevel()).Count);
        }
        finally
        {
            SessionPreferences.TestStore = null;
        }
    }

    [Fact]
    public void IsUserSelectedArpeggioTitle_AcceptsQualityAndLegacyRootLabels()
    {
        Assert.True(PlayModePickerOptions.IsUserSelectedArpeggioTitle("Major triad"));
        Assert.True(PlayModePickerOptions.IsUserSelectedArpeggioTitle("C major triad"));
        Assert.False(PlayModePickerOptions.IsUserSelectedArpeggioTitle("Major"));
    }

    [Theory]
    [InlineData("major-triad")]
    [InlineData("diminished-seventh")]
    public void ArpeggioExercise_EachMeasureSumsToFourBeats(string patternId)
    {
        var pattern = ArpeggioCatalog.All.First(p => p.Id == patternId);
        var notes = new ArpeggioSequenceBuilder
        {
            Key = "G",
            Scale = "Major",
            LowestNote = "G3",
            HighestNote = "G5",
        }.Build(pattern, "G4");

        Assert.NotEmpty(notes);
        foreach (var measure in notes.GroupBy(n => n.MeasureIndex ?? 0))
        {
            double beats = measure.Sum(n => n.BeatDuration);
            Assert.Equal(4.0, beats, precision: 3);
        }
    }

    [Fact]
    public void DominantSeventh_IncludesChordalSeventh_NotJustTriad()
    {
        var notes = new ArpeggioSequenceBuilder
        {
            Key = "F",
            Scale = "Major",
            LowestNote = "A3",
            HighestNote = "C6",
        }.Build(ArpeggioCatalog.DominantSeventh, "F4");

        var measure1 = notes.Where(n => (n.MeasureIndex ?? 0) == 0).Select(n => n.SpelledName).ToArray();
        Assert.Equal(new[] { "F4", "A4", "C5", "Eb5" }, measure1);
    }

    [Fact]
    public void DominantSeventh_InEMajor_MarksSeventhAsNatural()
    {
        var notes = new ArpeggioSequenceBuilder
        {
            Key = "E",
            Scale = "Major",
            LowestNote = "A3",
            HighestNote = "C6",
        }.Build(ArpeggioCatalog.DominantSeventh, "E4");

        var firstSeventh = notes.First(n => (n.MeasureIndex ?? 0) == 0 && n.Letter == 'D');
        Assert.Equal(Accidental.Natural, firstSeventh.Accidental);
        Assert.Equal("D5", firstSeventh.SpelledName);
        Assert.True(MeasureAccidentalRules.ShouldDrawNatural(
            KeySignatureRules.GetSignatureAccidentalForLetter('D', "E", "Major"),
            priorInBar: null));
    }

    [Fact]
    public void DiminishedTriad_SpellsDiminishedFifth_AndFillsEachMeasure()
    {
        var notes = new ArpeggioSequenceBuilder
        {
            Key = "D",
            Scale = "Natural Minor",
            LowestNote = "A3",
            HighestNote = "C6",
        }.Build(ArpeggioCatalog.DiminishedTriad, "D4");

        var measure1 = notes.Where(n => (n.MeasureIndex ?? 0) == 0).OrderBy(n => n.BeatPosition).ToList();
        Assert.Equal(4, measure1.Count);
        Assert.Equal(new[] { "D4", "F4", "Ab4", "D5" }, measure1.Select(n => n.SpelledName).ToArray());
        Assert.Equal(Accidental.Flat, measure1[2].Accidental);
        Assert.Equal(4.0, measure1.Sum(n => n.BeatDuration), precision: 3);

        foreach (var measure in notes.GroupBy(n => n.MeasureIndex ?? 0))
            Assert.Equal(4.0, measure.Sum(n => n.BeatDuration), precision: 3);
    }

    [Fact]
    public void MinorTriad_SpellsMinorThird_AgainstNaturalMinorKey()
    {
        var notes = new ArpeggioSequenceBuilder
        {
            Key = "F",
            Scale = "Natural Minor",
            LowestNote = "A3",
            HighestNote = "C6",
        }.Build(ArpeggioCatalog.MinorTriad, "F4");

        var measure1 = notes.Where(n => (n.MeasureIndex ?? 0) == 0).Select(n => n.SpelledName).ToArray();
        Assert.Equal(new[] { "F4", "Ab4", "C5", "F5" }, measure1);
    }

    [Theory]
    [InlineData("dominant-seventh", "F4", "F", "Major", 10)]
    [InlineData("major-seventh", "C4", "C", "Major", 11)]
    [InlineData("minor-seventh", "D4", "D", "Natural Minor", 10)]
    [InlineData("diminished-seventh", "C4", "C", "Major", 9)]
    public void SeventhChordPatterns_ExposeSeventhSemitoneInSequence(
        string patternId, string root, string key, string scale, int seventhSemitone)
    {
        var pattern = ArpeggioCatalog.All.First(p => p.Id == patternId);
        Assert.True(pattern.IncludesSeventh);

        var notes = new ArpeggioSequenceBuilder
        {
            Key = key,
            Scale = scale,
            LowestNote = "A3",
            HighestNote = "C6",
        }.Build(pattern, root);

        int rootMidi = NoteSessionService.NoteNameToMidi(root);
        int expectedSeventh = rootMidi + seventhSemitone;
        Assert.Contains(notes, n => n.MidiNumber == expectedSeventh);
    }
}
