using System.Reflection;
using System.Text.Json;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Saved tunes persist and restore their key signature for staff display, accidentals,
/// and written-note spelling — independent of the current Music-page key setting.
/// </summary>
public class SavedTuneKeySignatureTests : IDisposable
{
    private readonly string _path;
    private readonly SavedTuneStore _store;

    public SavedTuneKeySignatureTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"mm_saved_key_{Guid.NewGuid():N}.json");
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
    public void SaveReopen_CMajor_StillShowsCMajor()
    {
        var original = BuildTune("Saved Tune C", "C", ("C4", 60), ("E4", 64), ("G4", 67));
        _store.Save(original);

        var loaded = _store.GetByTitle("Saved Tune C");
        Assert.NotNull(loaded);
        Assert.Equal("C", loaded!.Key);

        var session = OpenAsPracticeSession(loaded, musicPageKey: "G");
        Assert.Equal("C", session.Key);
        Assert.Equal(("C", NoteSessionService.PracticeTuneKeySignatureScale),
            session.GetNotationKeyAndScale());

        var drawable = NewPracticeDrawable(session);
        Assert.True(IsKeySignatureVisiblyDrawn(drawable));
        Assert.Equal("C", ActiveNotationKey(drawable));
        Assert.Equal(0, KeySignatureRules.GetAccidentalCount("C", "Major"));
    }

    [Fact]
    public void SaveReopen_GMajor_DisplaysAndUsesFSharpSignature()
    {
        var original = BuildTune("Saved Tune G", "G", ("G4", 67), ("A4", 69), ("B4", 71), ("F#4", 66));
        _store.Save(original);

        var loaded = _store.GetByTitle("Saved Tune G");
        Assert.NotNull(loaded);
        Assert.Equal("G", loaded!.Key);

        var session = OpenAsPracticeSession(loaded, musicPageKey: "C");
        Assert.Equal("G", session.GetNotationKeyAndScale().Key);

        var drawable = NewPracticeDrawable(session);
        Assert.True(IsKeySignatureVisiblyDrawn(drawable));
        Assert.Equal("G", ActiveNotationKey(drawable));
        Assert.Equal("#", GetSignatureAccidentalForLetter(drawable, 'F'));
        Assert.Null(GetSignatureAccidentalForLetter(drawable, 'C'));

        // Body F# is implied by the visible G major signature — no redundant sharp.
        var flags = SimulateBodyAccidentals(drawable, [Note("F#4", Accidental.Sharp, 0, 0)]);
        Assert.False(flags[0]);

        var spelled = BuildSpellingFromTune(loaded);
        Assert.Contains(spelled, s => s.StartsWith("F", StringComparison.Ordinal));
        Assert.Equal(66, NoteSessionService.ApplyKeySignatureToMidi(
            "F4", 65, "G", NoteSessionService.PracticeTuneKeySignatureScale));
    }

    [Fact]
    public void SaveReopen_BbMajor_DisplaysAndUsesBbEbSignature()
    {
        var original = BuildTune("Saved Tune Bb", "Bb", ("Bb4", 70), ("C5", 72), ("D5", 74), ("Eb5", 75));
        _store.Save(original);

        var loaded = _store.GetByTitle("Saved Tune Bb");
        Assert.NotNull(loaded);
        Assert.Equal("Bb", loaded!.Key);

        var session = OpenAsPracticeSession(loaded, musicPageKey: "C");
        var drawable = NewPracticeDrawable(session);
        Assert.True(IsKeySignatureVisiblyDrawn(drawable));
        Assert.Equal("Bb", ActiveNotationKey(drawable));
        Assert.Equal("b", GetSignatureAccidentalForLetter(drawable, 'B'));
        Assert.Equal("b", GetSignatureAccidentalForLetter(drawable, 'E'));

        var flags = SimulateBodyAccidentals(drawable,
        [
            Note("Bb4", Accidental.Flat, 0, 0),
            Note("Eb5", Accidental.Flat, 0, 1),
        ]);
        Assert.False(flags[0], "Bb signature already flats B");
        Assert.False(flags[1], "Bb signature already flats E");
    }

    [Fact]
    public void ChangingMusicSettingsKey_DoesNotChangeSavedTuneRestoredKey()
    {
        var original = BuildTune("Saved Tune Locked", "G", ("G4", 67), ("F#4", 66));
        _store.Save(original);

        // Simulate user changing Music settings key after save.
        var settingsSession = new NoteSessionService { Key = "Eb", Tune = "Selected Scale" };
        Assert.Equal("Eb", settingsSession.Key);

        var loaded = _store.GetByTitle("Saved Tune Locked");
        Assert.NotNull(loaded);
        Assert.Equal("G", loaded!.Key);

        // Reopen under a session that currently has a different Music key.
        settingsSession.SelectPracticeTune(loaded);
        Assert.Equal("G", settingsSession.Key);
        Assert.Equal("G", settingsSession.GetNotationKeyAndScale().Key);
        Assert.Equal("G", loaded.Key);

        // Changing session Key after open must not rewrite the saved model or notation source.
        settingsSession.Key = "Bb";
        Assert.Equal("G", loaded.Key);
        Assert.Equal("G", settingsSession.GetNotationKeyAndScale().Key);

        var drawable = NewPracticeDrawable(settingsSession);
        Assert.Equal("G", ActiveNotationKey(drawable));
        Assert.Equal("#", GetSignatureAccidentalForLetter(drawable, 'F'));
    }

    [Fact]
    public void LegacyJsonWithoutKey_StillOpensSuccessfully_AsCMajor()
    {
        var legacy = """
            [
              {
                "title": "Saved Tune Legacy",
                "timeSignature": "4/4",
                "measures": [
                  {
                    "notes": [
                      { "midiNumber": 60, "spelledName": "C4", "duration": "Quarter", "isRest": false },
                      { "midiNumber": 62, "spelledName": "D4", "duration": "Quarter", "isRest": false }
                    ]
                  }
                ]
              }
            ]
            """;
        File.WriteAllText(_path, legacy);

        var store = new SavedTuneStore(_path);
        var loaded = store.GetByTitle("Saved Tune Legacy");
        Assert.NotNull(loaded);
        Assert.Null(loaded!.Key);

        var (key, scale) = NoteSessionService.ResolvePracticeTuneNotation(loaded);
        Assert.Equal("C", key);
        Assert.Equal(NoteSessionService.PracticeTuneKeySignatureScale, scale);

        var session = new NoteSessionService { Key = "F#" };
        session.SelectPracticeTune(loaded);
        Assert.Equal("C", session.Key);
        Assert.Equal(("C", NoteSessionService.PracticeTuneKeySignatureScale),
            session.GetNotationKeyAndScale());

        var drawable = NewPracticeDrawable(session);
        Assert.True(IsKeySignatureVisiblyDrawn(drawable));
        Assert.Equal("C", ActiveNotationKey(drawable));
        Assert.Equal(2, loaded.Measures[0].Notes.Count);
    }

    [Fact]
    public void SaveReopen_NoteSequenceUnchanged()
    {
        var original = BuildTune(
            "Saved Tune Seq",
            "D",
            ("D4", 62),
            ("E4", 64),
            ("F#4", 66),
            ("A4", 69));
        // Insert a rest in the middle via store round-trip of a richer tune.
        var withRest = new PracticeTune("Saved Tune Seq", TimeSignature.FourFour, "D");
        var m = withRest.AppendMeasure();
        m.AddNote(new MusicNote(62, "D4", NoteDuration.Quarter));
        m.AddNote(MusicNote.Rest(NoteDuration.Eighth));
        m.AddNote(new MusicNote(64, "E4", NoteDuration.Eighth));
        m.AddNote(new MusicNote(66, "F#4", NoteDuration.Quarter));
        m.AddNote(new MusicNote(69, "A4", NoteDuration.Quarter));

        _store.Save(withRest);
        var loaded = _store.GetByTitle("Saved Tune Seq");
        Assert.NotNull(loaded);

        Assert.Equal(withRest.Key, loaded!.Key);
        Assert.Equal(withRest.TimeSignature.ToString(), loaded.TimeSignature.ToString());
        Assert.Equal(withRest.Measures.Count, loaded.Measures.Count);
        Assert.Equal(withRest.Measures[0].Notes.Count, loaded.Measures[0].Notes.Count);

        for (int i = 0; i < withRest.Measures[0].Notes.Count; i++)
        {
            var a = withRest.Measures[0].Notes[i];
            var b = loaded.Measures[0].Notes[i];
            Assert.Equal(a.IsRest, b.IsRest);
            Assert.Equal(a.Duration, b.Duration);
            Assert.Equal(a.MidiNumber, b.MidiNumber);
            Assert.Equal(a.SpelledName, b.SpelledName);
        }
    }

    [Fact]
    public void RoundTripJson_PersistsKeyField()
    {
        _store.Save(BuildTune("Saved Tune Json", "Eb", ("Eb4", 63), ("G4", 67)));
        using var doc = JsonDocument.Parse(File.ReadAllText(_path));
        var key = doc.RootElement[0].GetProperty("key").GetString();
        Assert.Equal("Eb", key);
    }

    private static PracticeTune BuildTune(string title, string key, params (string Name, int Midi)[] notes)
    {
        var tune = new PracticeTune(title, TimeSignature.FourFour, key);
        var measure = tune.AppendMeasure();
        foreach (var (name, midi) in notes)
            measure.AddNote(new MusicNote(midi, name, NoteDuration.Quarter));
        return tune;
    }

    private static NoteSessionService OpenAsPracticeSession(PracticeTune tune, string musicPageKey)
    {
        var session = new NoteSessionService
        {
            Key = musicPageKey,
            Tune = "Selected Scale",
            SelectedScale = "Major",
            MeterTimeSignature = "4/4",
            ChildLevel = 20,
        };
        session.SelectPracticeTune(tune);
        return session;
    }

    private static StaffDrawable NewPracticeDrawable(NoteSessionService session)
        => new(session, new ThemeService(), safeArea: null);

    private static List<string> BuildSpellingFromTune(PracticeTune tune)
    {
        var names = new List<string>();
        var (key, scale) = NoteSessionService.ResolvePracticeTuneNotation(tune);
        foreach (var note in tune.AllNotes)
        {
            if (note.IsRest) continue;
            int midi = NoteSessionService.ApplyKeySignatureToMidi(note.SpelledName, note.MidiNumber, key, scale);
            char letter = char.ToUpperInvariant(note.SpelledName.Trim()[0]);
            int octave = int.Parse(note.SpelledName.Trim()[^1].ToString());
            var (_, display) = NoteSessionService.ResolveAccidentalAndSpelling(
                note.SpelledName, midi, letter, octave, key, scale);
            names.Add(display);
        }
        return names;
    }

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

    private static string ActiveNotationKey(StaffDrawable drawable)
        => (string)typeof(StaffDrawable)
            .GetProperty("ActiveNotationKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(drawable)!;

    private static string? GetSignatureAccidentalForLetter(StaffDrawable drawable, char letter)
        => (string?)typeof(StaffDrawable)
            .GetMethod("GetSignatureAccidentalForLetter", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, [letter]);

    private static List<bool> SimulateBodyAccidentals(
        StaffDrawable drawable,
        IReadOnlyList<GeneratedNote> notes)
    {
        var tryResolve = typeof(StaffDrawable).GetMethod(
            "TryResolveBodyAccidental", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var resetBar = typeof(StaffDrawable).GetMethod(
            "ResetAccidentalStateIfCrossedBar", BindingFlags.Static | BindingFlags.NonPublic)!;

        var history = new Dictionary<(char, int), Accidental>();
        var cancelled = new HashSet<(char, int)>();
        double prevBeat = -1.0;
        double beatOrigin = notes.Count > 0 ? notes.Min(n => n.BeatPosition ?? 0) : 0;
        var flags = new List<bool>();
        List<double> barBeats = [];

        for (int i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            double beat = (note.BeatPosition ?? 0.0) - beatOrigin;
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
