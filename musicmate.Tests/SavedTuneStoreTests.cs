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
    }

    private static PracticeTune BuildSimpleTune(string title)
    {
        var tune = new PracticeTune(title, TimeSignature.FourFour, "C");
        var m = tune.AppendMeasure();
        m.AddNote(new MusicNote(60, "C4", NoteDuration.Whole));
        return tune;
    }
}
