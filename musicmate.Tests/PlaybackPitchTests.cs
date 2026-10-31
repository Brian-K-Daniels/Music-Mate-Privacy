using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class PlaybackPitchTests : IDisposable
{
    public PlaybackPitchTests() => SessionPreferences.TestStore = new Dictionary<string, object?>();

    public void Dispose() => SessionPreferences.TestStore = null;

    [Fact]
    public void ConcertPitch_PlaysWrittenC4AsC4()
    {
        var slot = Play("concert-pitch", "C4");
        Assert.Equal(0, InstrumentCatalog.Resolve("concert-pitch").TransposeOffset);
        AssertSamePitch(slot, "C4");
    }

    [Fact]
    public void Guitar_PlaysWrittenC4AsC3()
    {
        Assert.Equal(-12, InstrumentCatalog.Resolve("guitar").TransposeOffset);
        var slot = Play("guitar", "C4");
        AssertSamePitch(slot, "C3");
        AssertNotDoubleTransposed(slot, "C4", -12);
    }

    [Fact]
    public void BassGuitar_PlaysWrittenC4AsC3()
    {
        Assert.Equal(-12, InstrumentCatalog.Resolve("bass-guitar").TransposeOffset);
        AssertSamePitch(Play("bass-guitar", "C4"), "C3");
    }

    [Fact]
    public void BbClarinet_PlaysWrittenC4AsBb3()
    {
        Assert.Equal(-2, InstrumentCatalog.Resolve("bb-clarinet").TransposeOffset);
        var slot = Play("bb-clarinet", "C4");
        AssertSamePitch(slot, "Bb3");
        AssertNotDoubleTransposed(slot, "C4", -2);
    }

    [Fact]
    public void CMajorScale_TransposesEveryNoteOnce()
    {
        string[] written = ["C4", "D4", "E4", "F4", "G4", "A4", "B4", "C5"];
        string[] guitarSounding = ["C3", "D3", "E3", "F3", "G3", "A3", "B3", "C4"];
        var concert = PlayScale("concert-pitch", written);
        var guitar = PlayScale("guitar", written);

        Assert.Equal(written.Length, concert.Count);
        for (int i = 0; i < written.Length; i++)
        {
            AssertSamePitch(concert[i], written[i]);
            AssertSamePitch(guitar[i], guitarSounding[i]);
            Assert.Equal(concert[i].FrequencyHz / 2.0, guitar[i].FrequencyHz, precision: 4);
            Assert.Equal(written[i], NoteName(concert[i]));
        }
    }

    [Theory]
    [InlineData("concert-pitch", 0)]
    [InlineData("guitar", -12)]
    [InlineData("bass-guitar", -12)]
    [InlineData("double-bass", -12)]
    [InlineData("bb-clarinet", -2)]
    [InlineData("eb-clarinet", 3)]
    [InlineData("f-horn", -7)]
    [InlineData("piccolo", 12)]
    [InlineData("glockenspiel", 24)]
    public void EveryListedInstrument_UsesItsCatalogOffsetOnce(string instrumentId, int expectedOffset)
    {
        var profile = InstrumentCatalog.Resolve(instrumentId);
        Assert.Equal(instrumentId, profile.Id);
        Assert.Equal(expectedOffset, profile.TransposeOffset);

        var slot = Play(instrumentId, "C4");
        int written = NoteSessionService.NoteNameToMidi("C4");
        double once = TunerReferenceNoteCatalog.ConcertFrequencyHz(written, expectedOffset);
        Assert.Equal(once, slot.FrequencyHz, precision: 6);
        Assert.Equal(written + expectedOffset, NoteSessionService.FrequencyToConcertMidi(slot.FrequencyHz));
        if (expectedOffset != 0)
            AssertNotDoubleTransposed(slot, "C4", expectedOffset);
    }

    [Fact]
    public void ChangingInstrument_UpdatesPlaybackPitch_WithoutChangingTheWrittenNote()
    {
        var session = new NoteSessionService { Instrument = "concert-pitch" };
        int written = NoteSessionService.NoteNameToMidi("C4");
        session.NotesToDraw.Add(new NoteInfo
        {
            Midi = written,
            Name = "C4",
            TargetFreq = NoteSessionService.MidiToFreqPublic(written),
        });

        double concertHz = PlaybackOnsetSchedule.FromSessionNotes(
            session.NotesToDraw, 100, session.InstrumentTransposeOffset)[0].FrequencyHz;

        session.Instrument = "guitar";
        double guitarHz = PlaybackOnsetSchedule.FromSessionNotes(
            session.NotesToDraw, 100, session.InstrumentTransposeOffset)[0].FrequencyHz;

        Assert.Equal("C4", session.NotesToDraw[0].Name);
        Assert.Equal(written, session.NotesToDraw[0].Midi);
        Assert.Equal(NoteSessionService.MidiToFreqPublic(written), session.NotesToDraw[0].TargetFreq, precision: 6);
        Assert.Equal(concertHz / 2.0, guitarHz, precision: 4);
        Assert.Equal(-12, session.InstrumentTransposeOffset);
    }

    [Fact]
    public void ClefChange_DoesNotChangeTheSoundingPitch()
    {
        var session = new NoteSessionService { Instrument = "double-bass" };
        session.ToggleNotationClef();
        Assert.Equal(Clef.Bass, session.NotationClef);
        int written = NoteSessionService.NoteNameToMidi("G3");
        session.NotesToDraw.Add(new NoteInfo
        {
            Midi = written,
            Name = "G3",
            TargetFreq = NoteSessionService.MidiToFreqPublic(written),
        });
        double bassClefHz = PlaybackOnsetSchedule.FromSessionNotes(
            session.NotesToDraw, 90, session.InstrumentTransposeOffset)[0].FrequencyHz;

        session.ToggleNotationClef();
        Assert.Equal(Clef.Treble, session.NotationClef);
        double trebleClefHz = PlaybackOnsetSchedule.FromSessionNotes(
            session.NotesToDraw, 90, session.InstrumentTransposeOffset)[0].FrequencyHz;

        Assert.Equal(bassClefHz, trebleClefHz, precision: 6);
        Assert.Equal(-12, session.InstrumentTransposeOffset);
        Assert.Equal(written, session.NotesToDraw[0].Midi);
        Assert.Equal("G3", session.NotesToDraw[0].Name);
    }

    [Theory]
    [InlineData("concert-pitch")]
    [InlineData("guitar")]
    [InlineData("bass-guitar")]
    [InlineData("bb-clarinet")]
    [InlineData("piccolo")]
    public void PlaybackFrequency_MatchesTheMicrophoneExpectation(string instrumentId)
    {
        var session = new NoteSessionService { Instrument = instrumentId };
        int written = NoteSessionService.NoteNameToMidi("C4");
        var note = new NoteInfo
        {
            Midi = written,
            Name = "C4",
            TargetFreq = NoteSessionService.MidiToFreqPublic(written),
        };
        session.NotesToDraw.Add(note);

        var domains = session.DescribePitchDomains(note, freq: 0);
        var slot = PlaybackOnsetSchedule.FromSessionNotes(
            session.NotesToDraw, 100, session.InstrumentTransposeOffset)[0];

        Assert.Equal(written, domains.ExpectedWrittenMidi);
        Assert.Equal(written + session.InstrumentTransposeOffset, domains.ExpectedConcertMidi);
        Assert.Equal(domains.ExpectedConcertMidi, NoteSessionService.FrequencyToConcertMidi(slot.FrequencyHz));
        Assert.Equal(
            TunerReferenceNoteCatalog.ConcertFrequencyHz(domains.ExpectedWrittenMidi, session.InstrumentTransposeOffset),
            slot.FrequencyHz,
            precision: 6);
    }

    private static PlaybackSlot Play(string instrumentId, string writtenName)
        => PlayScale(instrumentId, [writtenName])[0];

    private static IReadOnlyList<PlaybackSlot> PlayScale(string instrumentId, IReadOnlyList<string> writtenNames)
    {
        int offset = InstrumentCatalog.Resolve(instrumentId).TransposeOffset;
        var notes = new List<GeneratedNote>();
        foreach (string name in writtenNames)
        {
            int midi = NoteSessionService.NoteNameToMidi(name);
            notes.Add(new GeneratedNote
            {
                MidiNumber = midi,
                SpelledName = name,
                Letter = name[0],
                Octave = name[^1] - '0',
                TargetFrequency = NoteSessionService.MidiToFreqPublic(midi),
                Duration = NoteDuration.Quarter,
            });
        }

        var slots = PlaybackOnsetSchedule.FromGenerated(notes, 100, offset);
        for (int i = 0; i < notes.Count; i++)
        {
            Assert.Equal(NoteSessionService.NoteNameToMidi(writtenNames[i]), notes[i].MidiNumber);
            Assert.Equal(writtenNames[i], notes[i].SpelledName);
            Assert.Equal(
                NoteSessionService.MidiToFreqPublic(notes[i].MidiNumber),
                notes[i].TargetFrequency,
                precision: 6);
        }

        return slots;
    }

    private static void AssertSamePitch(PlaybackSlot slot, string soundingName)
    {
        int sounding = NoteSessionService.NoteNameToMidi(soundingName);
        Assert.Equal(sounding, NoteSessionService.FrequencyToConcertMidi(slot.FrequencyHz));
        Assert.Equal(NoteSessionService.MidiToFreqPublic(sounding), slot.FrequencyHz, precision: 2);
    }

    private static void AssertNotDoubleTransposed(PlaybackSlot slot, string writtenName, int offset)
    {
        int written = NoteSessionService.NoteNameToMidi(writtenName);
        double twice = TunerReferenceNoteCatalog.ConcertFrequencyHz(written + offset, offset);
        Assert.NotEqual(twice, slot.FrequencyHz);
    }

    private static string NoteName(PlaybackSlot slot)
        => NoteSessionService.MidiToNoteName(NoteSessionService.FrequencyToConcertMidi(slot.FrequencyHz), flats: false);
}
