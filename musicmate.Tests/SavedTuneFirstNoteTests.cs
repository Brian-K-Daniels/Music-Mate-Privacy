using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// A saved tune that begins on F# must grade that same F# after reload.
/// The count-in beep is a separate fixed tone and must not veto the note.
/// </summary>
[Collection("SessionPreferences")]
public class SavedTuneFirstNoteTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();
    private readonly string _path;
    private readonly SavedTuneStore _tunes;

    public SavedTuneFirstNoteTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
        _path = Path.Combine(Path.GetTempPath(), $"mm_saved_tunes_{Guid.NewGuid():N}.json");
        _tunes = new SavedTuneStore(_path);
    }

    public void Dispose()
    {
        SessionPreferences.TestStore = null;
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch { /* ignore */ }
    }

    [Fact]
    public void SaveAndReload_TuneBeginningOnFSharp_KeepsDisplayAndGradingTogether()
    {
        var tune = new PracticeTune("Saved Tune 2", TimeSignature.FourFour, "C", "Major", "Bb");
        var measure = tune.AppendMeasure();
        measure.AddNote(MusicNote.Rest(NoteDuration.Quarter));
        measure.AddNote(new MusicNote(78, "F#5", NoteDuration.Eighth));
        measure.AddNote(new MusicNote(78, "F#5", NoteDuration.Quarter));
        _tunes.Save(tune);

        var loaded = _tunes.GetByTitle("Saved Tune 2");
        Assert.NotNull(loaded);
        var pitched = loaded!.Measures[0].Notes.Where(n => !n.IsRest).ToList();
        Assert.Equal(2, pitched.Count);

        var first = ToGradedNote(pitched[0]);
        var later = ToGradedNote(pitched[1]);
        Assert.Equal('F', first.Letter);
        Assert.Equal(Accidental.Sharp, first.Accidental);
        Assert.Equal(5, first.Octave);
        Assert.Equal(78, first.MidiNumber);
        Assert.Equal("F#5", first.SpelledName);
        Assert.Equal(first.MidiNumber, later.MidiNumber);
        Assert.Equal(first.SpelledName, later.SpelledName);

        var session = new NoteSessionService
        {
            Instrument = "bb-clarinet",
            Tolerance = 15,
            Tune = "Practice Tune",
        };
        Assert.Equal(-2, session.InstrumentTransposeOffset);
        session.SelectPracticeTune(loaded);

        AddGraded(session, first);
        AddGraded(session, later);

        double concertE5 = NoteSessionService.MidiToFreqPublic(76);
        var firstDomains = session.DescribePitchDomains(session.NotesToDraw[0], concertE5);
        var laterDomains = session.DescribePitchDomains(session.NotesToDraw[1], concertE5);
        Assert.Equal("F#5", firstDomains.ExpectedWrittenPitch);
        Assert.Equal(78, firstDomains.ExpectedWrittenMidi);
        Assert.Equal("E5", firstDomains.ExpectedConcertPitch);
        Assert.Equal(76, firstDomains.ExpectedConcertMidi);
        Assert.Equal(firstDomains.ExpectedWrittenMidi, laterDomains.ExpectedWrittenMidi);
        Assert.Equal(firstDomains.ExpectedConcertMidi, laterDomains.ExpectedConcertMidi);
        Assert.Equal("F#5", firstDomains.HeardWrittenPitch);
        Assert.Equal("E5", firstDomains.HeardConcertPitch);

        // A newly graded F#5, not loaded from a save, uses the same concert pitch.
        var generated = session.DescribePitchDomains(new NoteInfo
        {
            Name = "F#5",
            Midi = 78,
            TargetFreq = NoteSessionService.MidiToFreqPublic(78),
        }, concertE5);
        Assert.Equal(firstDomains.ExpectedConcertMidi, generated.ExpectedConcertMidi);
        Assert.Equal(firstDomains.ExpectedWrittenPitch, generated.ExpectedWrittenPitch);

        session.StartListeningClock();
        var result = session.Evaluate(concertE5);
        Assert.True(result.correct);
        Assert.Equal(0, result.cents);
        Assert.True(session.UpdateFeedbackForCurrent(concertE5, result));
        Assert.Contains(0, session.CorrectNoteIndices);
        Assert.Equal(1, session.CurrentNoteIndex);

        // The sharp is not applied twice, and it is not dropped back to F.
        Assert.NotEqual(77, first.MidiNumber);
        Assert.NotEqual(79, first.MidiNumber);
        double beep = WaitingCountInSettings.DefaultUnaccentedPitchHz;
        double expectedHz = NoteSessionService.MidiToFreqPublic(firstDomains.ExpectedConcertMidi);
        Assert.NotEqual(beep, expectedHz);
        Assert.NotEqual(WaitingCountInSettings.DefaultAccentedPitchHz, expectedHz);
    }

    [Fact]
    public void CountInBeep_DoesNotRejectWrittenFSharp5_AfterTheBeepEnds()
    {
        double concertE5 = NoteSessionService.MidiToFreqPublic(76);
        double beepE6 = WaitingCountInSettings.DefaultUnaccentedPitchHz;
        double beepA6 = WaitingCountInSettings.DefaultAccentedPitchHz;

        // E5 is one octave below the unaccented beep. That proximity is real.
        Assert.True(WaitingCountInLogic.IsNearCountInClickFrequency(concertE5, beepA6, beepE6));

        // While the beep is sounding, do not let it score the note.
        Assert.False(WaitingCountInLogic.ShouldAcceptFirstNoteToEndCountIn(
            evaluateCorrect: true,
            countInActive: true,
            currentNoteIndex: 0,
            withinSelfSoundSuppressWindow: true,
            heardHz: concertE5,
            accentedClickHz: beepA6,
            unaccentedClickHz: beepE6));

        // After the beep, the same correct pitch is the player's F#, not the beep.
        Assert.True(WaitingCountInLogic.ShouldAcceptFirstNoteToEndCountIn(
            evaluateCorrect: true,
            countInActive: true,
            currentNoteIndex: 0,
            withinSelfSoundSuppressWindow: false,
            heardHz: concertE5,
            accentedClickHz: beepA6,
            unaccentedClickHz: beepE6));

        var session = new NoteSessionService
        {
            Instrument = "bb-clarinet",
            Tolerance = 15,
            Tune = "Practice Tune",
        };
        session.NotesToDraw.Add(new NoteInfo
        {
            Name = "F#5",
            Midi = 78,
            TargetFreq = NoteSessionService.MidiToFreqPublic(78),
            DurationBeats = 0.5,
            StartBeat = 1,
        });
        session.FeedbackViewModels.Add(new FeedbackItem(0, 0, 0, false));
        var heard = session.Evaluate(concertE5);
        Assert.True(heard.correct);
        var domains = session.DescribePitchDomains(session.NotesToDraw[0], concertE5);
        Assert.Equal("F#5", domains.ExpectedWrittenPitch);
        Assert.Equal("E5", domains.ExpectedConcertPitch);
        Assert.NotEqual(beepE6, NoteSessionService.MidiToFreqPublic(domains.ExpectedConcertMidi));
    }

    private static GeneratedNote ToGradedNote(MusicNote note)
    {
        var identity = SavedTuneStore.WithPitchIdentity(new GeneratedNote
        {
            MidiNumber = note.MidiNumber,
            SpelledName = note.SpelledName,
            Duration = note.Duration,
        });
        var (key, scale) = ("C", "Major");
        var (midi, name) = NoteSessionService.ResolveTargetPitch(identity, key, scale);
        Assert.Equal(78, midi);
        Assert.Equal("F#5", name);
        return identity;
    }

    private static void AddGraded(NoteSessionService session, GeneratedNote note)
    {
        var (midi, name) = NoteSessionService.ResolveTargetPitch(note, "C", "Major");
        int index = session.NotesToDraw.Count;
        session.NotesToDraw.Add(new NoteInfo
        {
            Name = name,
            Midi = midi,
            TargetFreq = NoteSessionService.MidiToFreqPublic(midi),
            Duration = note.Duration,
            DurationBeats = note.Duration == NoteDuration.Eighth ? 0.5 : 1,
            StartBeat = index == 0 ? 1 : 1.5,
            GateBeatsAfterPrevious = 0,
        });
        session.FeedbackViewModels.Add(new FeedbackItem(index, 0, 0, false));
    }
}
