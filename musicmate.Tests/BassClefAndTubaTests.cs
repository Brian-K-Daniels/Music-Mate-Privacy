using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Bass clef is a pitch-to-staff mapping. It does not transpose.
/// Tuba is concert pitch (offset 0) in bass clef.
/// </summary>
[Collection("SessionPreferences")]
public class BassClefAndTubaTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public BassClefAndTubaTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    // Bass staff, steps below the middle line D3.
    // Lines bottom→top: G2 +4, B2 +2, D3 0, F3 −2, A3 −4.
    [Theory]
    [InlineData('F', 2, 5)]  // space below the staff
    [InlineData('A', 2, 3)]  // bottom space
    [InlineData('C', 3, 1)]  // second space
    [InlineData('F', 3, -2)] // second line from the top
    [InlineData('A', 3, -4)] // top line
    [InlineData('C', 4, -6)] // one ledger line above the staff
    public void BassStaffPosition_MapsWrittenPitch(char letter, int octave, int stepsBelowMiddle)
    {
        Assert.Equal(stepsBelowMiddle, ClefStaffPosition.StepsBelowMiddle(Clef.Bass, letter, octave));
        Assert.Equal(stepsBelowMiddle, StaffDrawable.GetStaffSteps(Clef.Bass, letter, octave));
        Assert.NotEqual(
            StaffDrawable.GetDiatonicStepsFromB4(letter, octave),
            StaffDrawable.GetStaffSteps(Clef.Bass, letter, octave));
    }

    [Fact]
    public void MiddleC_SitsOneLedgerLineAboveBassStaff()
    {
        int topLine = ClefStaffPosition.StepsBelowMiddle(Clef.Bass, 'A', 3);
        int middleC = ClefStaffPosition.StepsBelowMiddle(Clef.Bass, 'C', 4);
        Assert.Equal(-4, topLine);
        Assert.Equal(-6, middleC);
        // One staff space is two diatonic steps. C4 is that far above the top line.
        Assert.Equal(2, topLine - middleC);
    }

    [Theory]
    [InlineData("G", false, 1)]
    [InlineData("D", false, 2)]
    [InlineData("A", false, 3)]
    [InlineData("F", true, 1)]
    [InlineData("Bb", true, 2)]
    [InlineData("Eb", true, 3)]
    [InlineData("Cb", true, 7)]
    public void BassKeySignature_UsesBassStaffPositions(string majorKey, bool flats, int count)
    {
        Assert.Equal(flats ? -count : count, KeySignatureRules.GetSignedAccidentalCountForMajorKey(majorKey));

        var bass = ClefStaffPosition.KeySignaturePositions(Clef.Bass, flats);
        var treble = ClefStaffPosition.KeySignaturePositions(Clef.Treble, flats);
        char[] letters = flats
            ? KeySignatureRules.FlatLetterOrder
            : KeySignatureRules.SharpLetterOrder;

        Assert.Equal(7, bass.Count);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(letters[i], bass[i].Letter);
            Assert.Equal(treble[i].Letter, bass[i].Letter);
            Assert.Equal(treble[i].Octave - 2, bass[i].Octave);
            Assert.NotEqual(
                ClefStaffPosition.StepsBelowMiddle(Clef.Treble, treble[i].Letter, treble[i].Octave),
                ClefStaffPosition.StepsBelowMiddle(Clef.Bass, bass[i].Letter, bass[i].Octave));
        }

        if (majorKey == "G")
            Assert.Equal(('F', 3), bass[0]);
        if (majorKey == "Cb")
            Assert.Equal(('F', 2), bass[6]);
    }

    [Fact]
    public void BassKeySignature_IsNotTheTreblePositionTable()
    {
        var bassSharps = ClefStaffPosition.KeySignaturePositions(Clef.Bass, flats: false);
        var trebleSharps = ClefStaffPosition.KeySignaturePositions(Clef.Treble, flats: false);
        Assert.Equal(
            new[] { ('F', 3), ('C', 3), ('G', 3), ('D', 3), ('A', 2), ('E', 3), ('B', 2) },
            bassSharps);
        Assert.NotEqual(trebleSharps, bassSharps);

        var bassFlats = ClefStaffPosition.KeySignaturePositions(Clef.Bass, flats: true);
        Assert.Equal(
            new[] { ('B', 2), ('E', 3), ('A', 2), ('D', 3), ('G', 2), ('C', 3), ('F', 2) },
            bassFlats);
    }

    [Fact]
    public void TrebleKeySignature_PositionsStayUnchanged()
    {
        Assert.Equal(
            new[] { ('F', 5), ('C', 5), ('G', 5), ('D', 5), ('A', 4), ('E', 5), ('B', 4) },
            ClefStaffPosition.KeySignaturePositions(Clef.Treble, flats: false));
        Assert.Equal(
            new[] { ('B', 4), ('E', 5), ('A', 4), ('D', 5), ('G', 4), ('C', 5), ('F', 4) },
            ClefStaffPosition.KeySignaturePositions(Clef.Treble, flats: true));
        Assert.Equal(6, StaffDrawable.GetDiatonicStepsFromB4('C', 4));
    }

    [Fact]
    public void Tuba_IsConcertPitchBassClef()
    {
        var tuba = InstrumentCatalog.Resolve("Tuba");
        Assert.Equal("tuba", tuba.Id);
        Assert.Equal("Tuba", tuba.DisplayName);
        Assert.Equal("C", tuba.InstrumentKey);
        Assert.Equal(0, tuba.TransposeOffset);
        Assert.Equal(Clef.Bass, tuba.DefaultClef);
        Assert.Equal("E2", tuba.PracticalLowestNote);
        Assert.Equal("C4", tuba.PracticalHighestNote);
        Assert.NotEqual("concert-pitch", tuba.Id);

        var concert = InstrumentCatalog.Resolve("concert-pitch");
        Assert.Equal(Clef.Treble, concert.DefaultClef);
        Assert.DoesNotContain("Tuba", concert.Aliases);
    }

    [Fact]
    public void Trombone_IsConcertPitch_AndCanUseEitherClef()
    {
        var trombone = InstrumentCatalog.Resolve("Trombone");
        Assert.Equal("trombone", trombone.Id);
        Assert.Equal(0, trombone.TransposeOffset);
        Assert.Equal(Clef.Treble, trombone.DefaultClef);
        Assert.True(trombone.CanToggleNotationClef);
        Assert.Contains(Clef.Bass, trombone.NotationClefs);
        Assert.NotEqual("concert-pitch", trombone.Id);
    }

    [Fact]
    public void TubaWrittenC_IsConcertC_AtAbout130Hz()
    {
        int written = NoteSessionService.NoteNameToMidi("C3");
        Assert.Equal(48, written);

        var tuba = InstrumentCatalog.Resolve("tuba");
        int concert = TunerReferenceNoteCatalog.ToConcertMidi(written, tuba.TransposeOffset);
        Assert.Equal(written, concert);

        double hz = TunerReferenceNoteCatalog.ConcertFrequencyHz(written, tuba.TransposeOffset);
        Assert.Equal(NoteSessionService.MidiToFreqPublic(written), hz, precision: 6);
        Assert.InRange(hz, 130.80, 130.82);

        double trumpetWrittenC = TunerReferenceNoteCatalog.ConcertFrequencyHz(written, transposeOffset: -2);
        Assert.NotEqual(hz, trumpetWrittenC);
    }

    [Fact]
    public void ChangingClef_DoesNotChangeSoundingPitch()
    {
        int written = NoteSessionService.NoteNameToMidi("C3");
        double concertHz = TunerReferenceNoteCatalog.ConcertFrequencyHz(written, transposeOffset: 0);
        double tubaHz = TunerReferenceNoteCatalog.ConcertFrequencyHz(
            written, InstrumentCatalog.Resolve("tuba").TransposeOffset);

        Assert.Equal(concertHz, tubaHz);
        Assert.NotEqual(
            ClefStaffPosition.StepsBelowMiddle(Clef.Treble, 'C', 3),
            ClefStaffPosition.StepsBelowMiddle(Clef.Bass, 'C', 3));
    }

    [Fact]
    public void TubaTuner_UsesWrittenConcertPitch()
    {
        var profile = TunerReferenceNoteCatalog.GetEffectiveInstrumentProfile("Tuba");
        Assert.Equal("tuba", profile.Id);
        Assert.Equal(0, TunerReferenceNoteCatalog.GetEffectiveTransposeOffset("Tuba"));
        Assert.Equal("Tuba", TunerReferenceNoteCatalog.GetTunerInstrumentPickerDisplayName("Tuba"));
        Assert.Equal(Clef.Bass, profile.DefaultClef);

        int c3 = NoteSessionService.NoteNameToMidi("C3");
        var choice = TunerReferenceNoteCatalog.BuildChoices(profile, preferFlatsForStaff: false)
            .Single(c => c.WrittenMidi == c3);
        Assert.Equal("C3", choice.StaffSpellingAscii);
        Assert.InRange(
            TunerReferenceNoteCatalog.ConcertFrequencyHz(choice.WrittenMidi, profile.TransposeOffset),
            130.80, 130.82);

        var midis = TunerReferenceNoteCatalog.GetPracticalWrittenMidis(profile);
        Assert.Equal(NoteSessionService.NoteNameToMidi("E2"), midis.Min());
        Assert.Equal(NoteSessionService.NoteNameToMidi("C4"), midis.Max());
    }

    [Fact]
    public void TubaScale_StaysInBassRange_AndSoundsAsWritten()
    {
        var gen = new MusicSequenceGenerator
        {
            Key = "C",
            Scale = "Major",
            LowestNote = "E2",
            HighestNote = "C4",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 8,
            RhythmVarietyPercent = 0,
            SmallestDuration = NoteDuration.Quarter,
            RestChancePercent = 0,
            AccidentalPercent = 0,
            UseScaleOrder = true,
            ChildLevel = 0,
            RandomSeed = 1,
        };
        var notes = MusicSequenceGenerator.Flatten(gen.GenerateSequence())
            .Where(n => !n.IsRest)
            .ToList();

        Assert.NotEmpty(notes);
        int low = NoteSessionService.NoteNameToMidi("E2");
        int high = NoteSessionService.NoteNameToMidi("C4");
        Assert.Contains(notes, n => n.Letter == 'C' && n.Octave == 3);
        Assert.Contains(notes, n => n.Letter == 'C' && n.Octave == 4);
        foreach (var note in notes)
        {
            Assert.InRange(note.MidiNumber, low, high);
            Assert.Equal(Accidental.None, note.Accidental);
            Assert.Equal(note.MidiNumber, NoteSessionService.NoteNameToMidi($"{note.Letter}{note.Octave}"));
            Assert.Equal(note.MidiNumber, TunerReferenceNoteCatalog.ToConcertMidi(note.MidiNumber, 0));
            Assert.Equal(
                ClefStaffPosition.StepsBelowMiddle(Clef.Bass, note.Letter, note.Octave),
                StaffDrawable.GetStaffSteps(Clef.Bass, note.Letter, note.Octave));
        }
    }

    [Fact]
    public void TubaArpeggio_IsConcertMajorTriadOnBassStaff()
    {
        var notes = new ArpeggioSequenceBuilder
        {
            Key = "C",
            Scale = "Major",
            LowestNote = "E2",
            HighestNote = "C4",
        }.Build(ArpeggioCatalog.MajorTriad, "C3");

        var pitched = notes.Where(n => !n.IsRest).ToList();
        Assert.NotEmpty(pitched);
        Assert.Contains(pitched, n => n.Letter == 'C' && n.Octave == 3);
        Assert.Contains(pitched, n => n.Letter == 'E' && n.Octave == 3);
        Assert.Contains(pitched, n => n.Letter == 'G' && n.Octave == 3);
        Assert.Contains(pitched, n => n.Letter == 'C' && n.Octave == 4);

        foreach (var note in pitched)
        {
            Assert.InRange(note.MidiNumber, NoteSessionService.NoteNameToMidi("E2"), NoteSessionService.NoteNameToMidi("C4"));
            Assert.Equal(note.MidiNumber, NoteSessionService.NoteNameToMidi($"{note.Letter}{note.Octave}"));
            Assert.Equal(note.MidiNumber, TunerReferenceNoteCatalog.ToConcertMidi(note.MidiNumber, 0));
            Assert.Equal(1, ClefStaffPosition.StepsBelowMiddle(Clef.Bass, 'C', 3));
        }
    }

    [Fact]
    public void SelectingTuba_FromBbInstrument_ClearsTranspositionAndUsesBassClef()
    {
        var session = NewSession();
        session.Instrument = "bb-trumpet";
        Assert.Equal(-2, session.InstrumentTransposeOffset);
        Assert.Equal(Clef.Treble, session.NotationClef);
        Assert.Equal("Bb", session.InstrumentKey);
        string trumpetConcert = session.GetConcertKey();
        Assert.NotEqual("C", trumpetConcert);
        Assert.Equal(PitchClass(NoteSessionService.NoteNameToMidi("C4") - 2), PitchClass(NoteSessionService.NoteNameToMidi(trumpetConcert + "4")));

        session.Instrument = "Tuba";

        AssertTuba(session);
        Assert.Equal("C", session.Key);
        Assert.Equal("C", session.GetConcertKey());
    }

    [Fact]
    public void SelectingTuba_FromEbInstrument_ClearsTranspositionAndUsesBassClef()
    {
        var session = NewSession();
        session.Instrument = "eb-alto-sax";
        Assert.Equal(-9, session.InstrumentTransposeOffset);
        Assert.Equal(Clef.Treble, session.NotationClef);
        Assert.Equal("Eb", session.InstrumentKey);
        string saxConcert = session.GetConcertKey();
        Assert.NotEqual("C", saxConcert);
        Assert.Equal(PitchClass(NoteSessionService.NoteNameToMidi("C4") - 9), PitchClass(NoteSessionService.NoteNameToMidi(saxConcert + "4")));

        session.Instrument = "tuba";

        AssertTuba(session);
        Assert.Equal("C", session.GetConcertKey());
    }

    [Fact]
    public void LeavingTuba_ForBbInstrument_RestoresTrebleAndTransposition()
    {
        var session = NewSession();
        session.Instrument = "tuba";
        AssertTuba(session);

        session.Instrument = "bb-clarinet";

        var clarinet = InstrumentCatalog.Resolve("bb-clarinet");
        Assert.Equal("bb-clarinet", session.Instrument);
        Assert.Equal(-2, session.InstrumentTransposeOffset);
        Assert.Equal(Clef.Treble, session.NotationClef);
        Assert.Equal("Bb", session.InstrumentKey);
        Assert.Equal(PitchClass(NoteSessionService.NoteNameToMidi("C4") - 2), PitchClass(NoteSessionService.NoteNameToMidi(session.GetConcertKey() + "4")));
        Assert.Equal("C", session.Key);
        AssertRange(session, clarinet);
    }

    [Fact]
    public void LeavingTuba_ForEbInstrument_RestoresTrebleAndTransposition()
    {
        var session = NewSession();
        session.Instrument = "tuba";
        AssertTuba(session);

        session.Instrument = "eb-alto-sax";

        var sax = InstrumentCatalog.Resolve("eb-alto-sax");
        Assert.Equal(-9, session.InstrumentTransposeOffset);
        Assert.Equal(Clef.Treble, session.NotationClef);
        Assert.Equal("Eb", session.InstrumentKey);
        Assert.Equal(PitchClass(NoteSessionService.NoteNameToMidi("C4") - 9), PitchClass(NoteSessionService.NoteNameToMidi(session.GetConcertKey() + "4")));
        AssertRange(session, sax);
    }

    [Fact]
    public void LeavingTuba_ForConcertPitch_RestoresTrebleClefAndRange()
    {
        var session = NewSession();
        session.Instrument = "tuba";
        AssertTuba(session);

        session.Instrument = "concert-pitch";

        var concert = InstrumentCatalog.Resolve("concert-pitch");
        Assert.Equal(0, session.InstrumentTransposeOffset);
        Assert.Equal(Clef.Treble, session.NotationClef);
        Assert.Equal("C", session.InstrumentKey);
        Assert.Equal("C", session.GetConcertKey());
        Assert.Equal("G3", session.LowestNote);
        Assert.Equal("C6", session.HighestNote);
        AssertRange(session, concert);
        Assert.NotEqual(NoteSessionService.NoteNameToMidi("E2"), NoteSessionService.NoteNameToMidi(session.LowestNote));
    }

    private NoteSessionService NewSession()
    {
        var session = new NoteSessionService { ChildLevel = 0, Key = "C" };
        session.ClearNoteRangeCustomization();
        return session;
    }

    private static void AssertTuba(NoteSessionService session)
    {
        Assert.Equal("tuba", session.Instrument);
        Assert.Equal("Tuba", session.InstrumentDisplayName);
        Assert.Equal("C", session.InstrumentKey);
        Assert.Equal(0, session.InstrumentTransposeOffset);
        Assert.Equal(Clef.Bass, session.NotationClef);
        Assert.Equal("E2", session.LowestNote);
        Assert.Equal("C4", session.HighestNote);
    }

    private static int PitchClass(int midi) => ((midi % 12) + 12) % 12;

    private static void AssertRange(NoteSessionService session, InstrumentProfile profile)
    {
        Assert.Equal(
            NoteSessionService.NoteNameToMidi(profile.PracticalLowestNote),
            NoteSessionService.NoteNameToMidi(session.LowestNote));
        Assert.Equal(
            NoteSessionService.NoteNameToMidi(profile.PracticalHighestNote),
            NoteSessionService.NoteNameToMidi(session.HighestNote));
    }
}
