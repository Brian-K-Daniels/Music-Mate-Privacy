using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

public class SavedTuneStoreTests : IDisposable
{
    private readonly string _path;
    private readonly SavedTuneStore _store;

    public SavedTuneStoreTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"mm_saved_tunes_{Guid.NewGuid():N}.json");
        _store = new SavedTuneStore(_path);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch { /* ignore */ }
    }

    [Fact]
    public void SuggestNextTitle_UsesLowestAvailableNumber()
    {
        Assert.Equal("Saved Tune 1", _store.SuggestNextTitle());

        _store.Save(BuildSimpleTune("Saved Tune 1"));
        _store.Save(BuildSimpleTune("Saved Tune 3"));
        Assert.Equal("Saved Tune 2", _store.SuggestNextTitle());

        _store.Save(BuildSimpleTune("Saved Tune 2"));
        Assert.Equal("Saved Tune 4", _store.SuggestNextTitle());

        Assert.True(_store.Delete("Saved Tune 2"));
        Assert.Equal("Saved Tune 2", _store.SuggestNextTitle());
    }

    [Fact]
    public void RoundTrip_PreservesNotesRestsKeyAndMeter()
    {
        var original = new PracticeTune("Saved Tune 1", TimeSignature.ThreeFour, key: "G");
        var m1 = original.AppendMeasure();
        m1.AddNote(new MusicNote(67, "G4", NoteDuration.Quarter));
        m1.AddNote(MusicNote.Rest(NoteDuration.Eighth));
        m1.AddNote(new MusicNote(69, "A4", NoteDuration.Eighth));
        var m2 = original.AppendMeasure();
        m2.AddNote(new MusicNote(71, "B4", NoteDuration.Half));
        m2.AddNote(new MusicNote(72, "C5", NoteDuration.Quarter));

        _store.Save(original);
        var loaded = _store.GetByTitle("Saved Tune 1");
        Assert.NotNull(loaded);
        Assert.Equal("G", loaded!.Key);
        Assert.Equal("3/4", loaded.TimeSignature.ToString());
        Assert.Equal(2, loaded.Measures.Count);
        Assert.Equal(3, loaded.Measures[0].Notes.Count);
        Assert.True(loaded.Measures[0].Notes[1].IsRest);
        Assert.Equal(NoteDuration.Eighth, loaded.Measures[0].Notes[1].Duration);
        Assert.Equal("B4", loaded.Measures[1].Notes[0].SpelledName);
        Assert.Equal(71, loaded.Measures[1].Notes[0].MidiNumber);
    }

    [Fact]
    public void IsSavedTuneTitle_RequiresMarker()
    {
        Assert.True(SavedTuneStore.IsSavedTuneTitle("Saved Tune 1"));
        Assert.True(SavedTuneStore.IsSavedTuneTitle("My Saved Tune Remix"));
        Assert.False(SavedTuneStore.IsSavedTuneTitle("Mary Had a Little Lamb"));
        Assert.False(SavedTuneStore.IsSavedTuneTitle(null));
    }

    [Fact]
    public void Delete_DoesNotRemoveBuiltInNames()
    {
        Assert.False(_store.Delete("Mary Had a Little Lamb"));
    }

    [Fact]
    public void FromGeneratedNotes_GroupsByMeasureIndex()
    {
        var notes = new List<GeneratedNote>
        {
            new() { MidiNumber = 60, SpelledName = "C4", Duration = NoteDuration.Quarter, MeasureIndex = 0, BeatPosition = 0 },
            new() { MidiNumber = 62, SpelledName = "D4", Duration = NoteDuration.Quarter, MeasureIndex = 0, BeatPosition = 1 },
            GeneratedNote.Rest(NoteDuration.Half, measureIndex: 1, beatPosition: 4),
            new() { MidiNumber = 64, SpelledName = "E4", Duration = NoteDuration.Half, MeasureIndex = 1, BeatPosition = 6 },
        };

        var tune = SavedTuneStore.FromGeneratedNotes("Saved Tune 1", notes, TimeSignature.FourFour, "C");
        Assert.Equal(2, tune.Measures.Count);
        Assert.Equal(2, tune.Measures[0].Notes.Count);
        Assert.True(tune.Measures[1].Notes[0].IsRest);
        Assert.Equal("E4", tune.Measures[1].Notes[1].SpelledName);
        Assert.All(tune.Measures, m =>
            Assert.True(m.BeatsUsed <= m.BeatsAvailable + 1e-9));
    }

    [Fact]
    public void FromGeneratedNotes_DropsOverlappingStaffSameMeasureIndexOverflow()
    {
        // Dual-staff Random used MeasureIndex 0 on both staves; naive GroupBy merged
        // two full bars into one 8-beat measure. Same-index overflow is dropped.
        var notes = new List<GeneratedNote>
        {
            new() { MidiNumber = 76, SpelledName = "E5", Duration = NoteDuration.Whole, MeasureIndex = 0, BeatPosition = 0 },
            new() { MidiNumber = 60, SpelledName = "C4", Duration = NoteDuration.Whole, MeasureIndex = 0, BeatPosition = 0 },
        };

        var tune = SavedTuneStore.FromGeneratedNotes("Saved Tune 1", notes, TimeSignature.FourFour, "C");
        Assert.Single(tune.Measures);
        Assert.Single(tune.Measures[0].Notes);
        Assert.Equal("E5", tune.Measures[0].Notes[0].SpelledName);
        Assert.Equal(4.0, tune.Measures[0].BeatsUsed, 3);
    }

    [Fact]
    public void FromGeneratedNotes_DropsOverfullSameMeasureIndexStrays()
    {
        // Q + H + E + Q = 4.5 in one MeasureIndex — keep prefix that fits, drop overflow.
        var notes = new List<GeneratedNote>
        {
            new() { MidiNumber = 60, SpelledName = "C4", Duration = NoteDuration.Quarter, MeasureIndex = 0, BeatPosition = 0 },
            new() { MidiNumber = 62, SpelledName = "D4", Duration = NoteDuration.Half, MeasureIndex = 0, BeatPosition = 1 },
            new() { MidiNumber = 64, SpelledName = "E4", Duration = NoteDuration.Eighth, MeasureIndex = 0, BeatPosition = 3 },
            new() { MidiNumber = 65, SpelledName = "F4", Duration = NoteDuration.Quarter, MeasureIndex = 0, BeatPosition = 3.5 },
        };

        var tune = SavedTuneStore.FromGeneratedNotes("Saved Tune 1", notes, TimeSignature.FourFour, "C");
        Assert.Single(tune.Measures);
        Assert.All(tune.Measures, m =>
            Assert.True(
                m.BeatsUsed <= m.BeatsAvailable + 1e-9,
                $"Overfull measure: {m.BeatsUsed}/{m.BeatsAvailable}"));
        Assert.Equal(3.5, tune.Measures.Sum(m => m.BeatsUsed), 3);
        Assert.DoesNotContain(tune.Measures[0].Notes, n => n.SpelledName == "F4");
    }

    [Fact]
    public void FromGeneratedNotes_DropsStraySixteenthOverflowLikeSavedTuneBug()
    {
        // Reproduces Q+Q+Q+Sixteenth+E+E (= 4.25): keep notes that fit, drop the rest.
        var notes = new List<GeneratedNote>
        {
            new() { MidiNumber = 69, SpelledName = "A4", Duration = NoteDuration.Quarter, MeasureIndex = 1, BeatPosition = 4 },
            new() { MidiNumber = 67, SpelledName = "G4", Duration = NoteDuration.Quarter, MeasureIndex = 1, BeatPosition = 5 },
            new() { MidiNumber = 69, SpelledName = "A4", Duration = NoteDuration.Quarter, MeasureIndex = 1, BeatPosition = 6 },
            new() { MidiNumber = 69, SpelledName = "A4", Duration = NoteDuration.Sixteenth, MeasureIndex = 1, BeatPosition = 7 },
            new() { MidiNumber = 67, SpelledName = "G4", Duration = NoteDuration.Eighth, MeasureIndex = 1, BeatPosition = 7.25 },
            new() { MidiNumber = 69, SpelledName = "A4", Duration = NoteDuration.Eighth, MeasureIndex = 1, BeatPosition = 7.75 },
        };

        var tune = SavedTuneStore.FromGeneratedNotes("Saved Tune 3", notes, TimeSignature.FourFour, "C");
        Assert.Single(tune.Measures);
        Assert.True(tune.Measures[0].BeatsUsed <= 4.0 + 1e-9);
        Assert.Equal(3.75, tune.Measures[0].BeatsUsed, 3);
        Assert.Equal(5, tune.Measures[0].Notes.Count);
    }

    [Fact]
    public void FromGeneratedNotes_NullMeasureIndex_StillPacksIntoSuccessiveBars()
    {
        var notes = Enumerable.Range(0, 8)
            .Select(i => new GeneratedNote
            {
                MidiNumber = 60 + i,
                SpelledName = "C4",
                Duration = NoteDuration.Quarter,
                BeatPosition = i,
            })
            .ToList();

        var tune = SavedTuneStore.FromGeneratedNotes("Saved Tune 1", notes, TimeSignature.FourFour, "C");
        Assert.Equal(2, tune.Measures.Count);
        Assert.All(tune.Measures, m => Assert.Equal(4.0, m.BeatsUsed, 3));
    }

    [Fact]
    public void FromGeneratedNotes_MultiBarDistinctIndexes_KeepsAllFittingNotes()
    {
        var notes = new List<GeneratedNote>
        {
            new() { MidiNumber = 60, SpelledName = "C4", Duration = NoteDuration.Whole, MeasureIndex = 0, BeatPosition = 0 },
            new() { MidiNumber = 62, SpelledName = "D4", Duration = NoteDuration.Whole, MeasureIndex = 1, BeatPosition = 4 },
        };

        var tune = SavedTuneStore.FromGeneratedNotes("Saved Tune 1", notes, TimeSignature.FourFour, "C");
        Assert.Equal(2, tune.Measures.Count);
        Assert.Equal("C4", tune.Measures[0].Notes[0].SpelledName);
        Assert.Equal("D4", tune.Measures[1].Notes[0].SpelledName);
    }

    [Fact]
    public void NormalizeMeasureDurations_AndLoad_DropLegacyOverfullBars()
    {
        var broken = new PracticeTune("Saved Tune 9", TimeSignature.FourFour, "C");
        var m = broken.AppendMeasure();
        m.AddNote(new MusicNote(60, "C4", NoteDuration.Quarter));
        m.AddNote(new MusicNote(62, "D4", NoteDuration.Half));
        m.AddNote(new MusicNote(64, "E4", NoteDuration.Eighth));
        m.AddNote(new MusicNote(65, "F4", NoteDuration.Quarter)); // 4.5

        var normalized = SavedTuneStore.NormalizeMeasureDurations(broken);
        Assert.Single(normalized.Measures);
        Assert.Equal(3.5, normalized.Measures[0].BeatsUsed, 3);
        Assert.All(normalized.Measures, x =>
            Assert.True(x.BeatsUsed <= x.BeatsAvailable + 1e-9));

        _store.Save(broken);
        var loaded = _store.GetByTitle("Saved Tune 9");
        Assert.NotNull(loaded);
        Assert.All(loaded!.Measures, x =>
            Assert.True(x.BeatsUsed <= x.BeatsAvailable + 1e-9));
        Assert.Equal(3.5, loaded.Measures.Sum(x => x.BeatsUsed), 3);
    }

    private static PracticeTune BuildSimpleTune(string title)
    {
        var tune = new PracticeTune(title, TimeSignature.FourFour, "C");
        var m = tune.AppendMeasure();
        m.AddNote(new MusicNote(60, "C4", NoteDuration.Whole));
        return tune;
    }
}
