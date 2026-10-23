using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class GuitarBassGuitarTests : IDisposable
{
    private const int SampleRate = 44100;
    private const int WindowSize = 4096;
    private static readonly object McLeodLock = new();

    private readonly Dictionary<string, object?> _store = new();

    public GuitarBassGuitarTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Fact]
    public void BothInstruments_AppearInThePicker_WithWrittenRangesAndOctaveOffset()
    {
        Assert.Contains("Guitar", InstrumentCatalog.DisplayNames);
        Assert.Contains("Bass Guitar", InstrumentCatalog.DisplayNames);

        var guitar = InstrumentCatalog.Resolve("Guitar");
        Assert.Equal("guitar", guitar.Id);
        Assert.Equal("C - 1 octave", guitar.InstrumentKey);
        Assert.Equal(-12, guitar.TransposeOffset);
        Assert.Equal("E3", guitar.PracticalLowestNote);
        Assert.Equal("E7", guitar.PracticalHighestNote);
        Assert.Equal(Clef.Treble, guitar.DefaultClef);
        Assert.False(guitar.CanToggleNotationClef);
        Assert.Equal(new[] { Clef.Treble }, guitar.NotationClefs);

        var bass = InstrumentCatalog.Resolve("Bass Guitar");
        Assert.Equal("bass-guitar", bass.Id);
        Assert.Equal("C - 1 octave", bass.InstrumentKey);
        Assert.Equal(-12, bass.TransposeOffset);
        Assert.Equal("E2", bass.PracticalLowestNote);
        Assert.Equal("G5", bass.PracticalHighestNote);
        Assert.Equal(Clef.Bass, bass.DefaultClef);
        Assert.False(bass.CanToggleNotationClef);
        Assert.Equal(new[] { Clef.Bass }, bass.NotationClefs);

        Assert.Equal("guitar", InstrumentCatalog.Resolve("Acoustic Guitar").Id);
        Assert.Equal("guitar", InstrumentCatalog.Resolve("Electric Guitar").Id);
        Assert.Equal("bass-guitar", InstrumentCatalog.Resolve("Electric Bass").Id);
    }

    [Theory]
    [InlineData("guitar")]
    [InlineData("bass-guitar")]
    public void WrittenC4_SoundsC3(string instrumentId)
    {
        var profile = InstrumentCatalog.Resolve(instrumentId);
        int writtenC4 = NoteSessionService.NoteNameToMidi("C4");
        int soundingC3 = NoteSessionService.NoteNameToMidi("C3");
        Assert.Equal(60, writtenC4);
        Assert.Equal(48, soundingC3);
        Assert.Equal(-12, profile.TransposeOffset);
        Assert.Equal(soundingC3, TunerReferenceNoteCatalog.ToConcertMidi(writtenC4, profile.TransposeOffset));
        Assert.Equal(
            NoteSessionService.MidiToFreqPublic(soundingC3),
            TunerReferenceNoteCatalog.ConcertFrequencyHz(writtenC4, profile.TransposeOffset),
            precision: 6);
    }

    [Theory]
    [InlineData(0, "guitar", "E3", "E7")]
    [InlineData(0, "bass-guitar", "E2", "G5")]
    [InlineData(25, "guitar", "B3", "D5")]
    [InlineData(25, "bass-guitar", "B3", "D5")]
    [InlineData(100, "guitar", "E3", "E7")]
    [InlineData(100, "bass-guitar", "E2", "G5")]
    public void NoteGeneration_StaysInsideThePracticalCompass(int level, string instrumentId, string low, string high)
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
        Assert.Equal(NoteSessionService.NoteNameToMidi(low), session.AvailableInstrumentMidis.Min());
        Assert.Equal(NoteSessionService.NoteNameToMidi(high), session.AvailableInstrumentMidis.Max());
        Assert.Equal(instrumentId, _store["musicmate.Instrument"]);
    }

    [Fact]
    public void Guitar_UsesTrebleClef_BassGuitarUsesBassClef()
    {
        var guitar = new NoteSessionService { Instrument = "Guitar" };
        Assert.Equal(Clef.Treble, guitar.NotationClef);
        Assert.Equal("Treble clef.", guitar.NotationClefAccessibilityText);
        guitar.ToggleNotationClef();
        Assert.Equal(Clef.Treble, guitar.NotationClef);

        var bass = new NoteSessionService { Instrument = "Bass Guitar" };
        Assert.Equal(Clef.Bass, bass.NotationClef);
        Assert.Equal("Bass clef.", bass.NotationClefAccessibilityText);
        bass.ToggleNotationClef();
        Assert.Equal(Clef.Bass, bass.NotationClef);
    }

    [Fact]
    public void SelectingEitherInstrument_KeepsTheWrittenKey_AndAppliesOctaveOffsetOnce()
    {
        var session = new NoteSessionService { ChildLevel = 0, Key = "F" };
        session.ClearNoteRangeCustomization();

        session.Instrument = "guitar";
        Assert.Equal("F", session.Key);
        Assert.Equal("F", session.GetConcertKey());
        Assert.Equal(-12, session.InstrumentTransposeOffset);

        session.Instrument = "bass-guitar";
        Assert.Equal("F", session.Key);
        Assert.Equal("F", session.GetConcertKey());
        Assert.Equal(-12, session.InstrumentTransposeOffset);
        Assert.Equal(Clef.Bass, session.NotationClef);
    }

    [Theory]
    [InlineData("guitar")]
    [InlineData("bass-guitar")]
    public void SoundingPitch_MustMatchTheTransposedOctave_NotTheWrittenPitch(string instrumentId)
    {
        var session = new NoteSessionService { Instrument = instrumentId };
        int writtenC4 = NoteSessionService.NoteNameToMidi("C4");
        double soundingC3 = NoteSessionService.MidiToFreqPublic(writtenC4 - 12);
        double writtenC4Hz = NoteSessionService.MidiToFreqPublic(writtenC4);
        session.NotesToDraw.Add(new NoteInfo { Midi = writtenC4, Name = "C4", TargetFreq = soundingC3 });

        var domains = session.DescribePitchDomains(session.NotesToDraw[0], soundingC3);
        Assert.Equal(writtenC4, domains.ExpectedWrittenMidi);
        Assert.Equal(writtenC4 - 12, domains.ExpectedConcertMidi);
        Assert.Equal(-12, domains.ExpectedConcertMidi - domains.ExpectedWrittenMidi);

        var (correctSounding, _) = session.Evaluate(soundingC3);
        Assert.True(correctSounding);

        var (correctWrittenOctave, cents) = session.Evaluate(writtenC4Hz);
        Assert.False(correctWrittenOctave);
        Assert.True(Math.Abs(cents) > 50);
    }

    [Fact]
    public void SavedInstrumentSelection_IsRestored()
    {
        var session = new NoteSessionService { Instrument = "Acoustic Guitar" };
        Assert.Equal("guitar", session.Instrument);
        Assert.Equal("guitar", _store["musicmate.Instrument"]);

        session.Instrument = "Bass Guitar";
        Assert.Equal("bass-guitar", session.Instrument);
        Assert.Equal("bass-guitar", _store["musicmate.Instrument"]);

        var restoredBass = new NoteSessionService();
        Assert.Equal("bass-guitar", restoredBass.Instrument);
        Assert.Equal(Clef.Bass, restoredBass.NotationClef);
        Assert.Equal(-12, restoredBass.InstrumentTransposeOffset);

        _store["musicmate.Instrument"] = "guitar";
        var restoredGuitar = new NoteSessionService();
        Assert.Equal("guitar", restoredGuitar.Instrument);
        Assert.Equal(Clef.Treble, restoredGuitar.NotationClef);
        Assert.Equal(-12, restoredGuitar.InstrumentTransposeOffset);
    }

    [Fact]
    public void ExistingInstrumentsAndTranspositions_RemainUnchanged()
    {
        Assert.Equal(0, InstrumentCatalog.Resolve("concert-pitch").TransposeOffset);
        Assert.Equal(-2, InstrumentCatalog.Resolve("bb-clarinet").TransposeOffset);
        Assert.Equal(12, InstrumentCatalog.Resolve("piccolo").TransposeOffset);
        Assert.Equal(-12, InstrumentCatalog.Resolve("double-bass").TransposeOffset);
        Assert.Equal("E2", InstrumentCatalog.Resolve("double-bass").PracticalLowestNote);
        Assert.Equal("G4", InstrumentCatalog.Resolve("double-bass").PracticalHighestNote);
        Assert.Equal(0, InstrumentCatalog.Resolve("bassoon").TransposeOffset);
        Assert.Equal("Bb1", InstrumentCatalog.Resolve("bassoon").PracticalLowestNote);
        Assert.Equal(-12, InstrumentCatalog.Resolve("contrabassoon").TransposeOffset);
        Assert.Equal("voice-bass", InstrumentCatalog.Resolve("Bass").Id);
        Assert.Equal("E2", InstrumentCatalog.Resolve("voice-bass").PracticalLowestNote);
        Assert.Equal("E4", InstrumentCatalog.Resolve("voice-bass").PracticalHighestNote);
        Assert.NotEqual("bass-guitar", InstrumentCatalog.Resolve("Bass").Id);
        Assert.Equal("concert-pitch", InstrumentCatalog.Resolve("Piano").Id);
        Assert.Equal(
            WindowSize,
            PitchDetectionService.AnalysisWindowSize(
                InstrumentCatalog.Resolve("bassoon"), SampleRate, WindowSize));
    }

    [Fact]
    public void GuitarMinDetectionFrequency_StaysAtDefault_BassGuitarAllowsE1()
    {
        Assert.Equal(
            PitchDetectionService.DefaultMinFrequencyHz,
            PitchDetectionService.MinDetectionFrequencyHz(InstrumentCatalog.Resolve("guitar")));
        Assert.Equal(
            PitchDetectionService.DefaultMinFrequencyHz,
            PitchDetectionService.MinDetectionFrequencyHz(InstrumentCatalog.Resolve("bb-clarinet")));
        Assert.Equal(
            WindowSize,
            PitchDetectionService.AnalysisWindowSize(
                InstrumentCatalog.Resolve("guitar"), SampleRate, WindowSize));
        Assert.Equal(
            WindowSize,
            PitchDetectionService.AnalysisWindowSize(
                InstrumentCatalog.Resolve("bb-clarinet"), SampleRate, WindowSize));

        int bassMin = PitchDetectionService.MinDetectionFrequencyHz(InstrumentCatalog.Resolve("bass-guitar"));
        Assert.InRange(bassMin, PitchDetectionService.AbsoluteMinFrequencyHz, 40);

        double e1 = NoteSessionService.MidiToFreqPublic(NoteSessionService.NoteNameToMidi("E1"));
        Assert.InRange(e1, 41.0, 41.5);
        Assert.True(bassMin < e1);
    }

    [Fact]
    public void BassGuitarLowE1_IsDetectedWithInstrumentMinFrequency_NotWithDefault60Hz()
    {
        var session = new NoteSessionService { Instrument = "bass-guitar", SampleRate = SampleRate };
        Assert.Equal(WindowSize, session.PitchWindowSize);
        int minHz = session.PitchDetectionMinFrequencyHz;
        int window = session.PitchAnalysisWindowSize;
        Assert.True(window > WindowSize);

        var clarinet = new NoteSessionService { Instrument = "bb-clarinet", SampleRate = SampleRate };
        Assert.Equal(WindowSize, clarinet.PitchAnalysisWindowSize);
        Assert.Equal(PitchDetectionService.DefaultMinFrequencyHz, clarinet.PitchDetectionMinFrequencyHz);

        double e1 = NoteSessionService.MidiToFreqPublic(NoteSessionService.NoteNameToMidi("E1"));
        float[] bassBuffer = Sine(e1, window, SampleRate, 0.2f);
        float[] defaultWindow = Sine(e1, WindowSize, SampleRate, 0.2f);

        float withBass;
        float withDefaultFloor;
        float withDefaultWindow;
        lock (McLeodLock)
        {
            withBass = PitchDetectionService.DetectPitchMcLeod(bassBuffer, window, SampleRate, minHz);
            withDefaultFloor = PitchDetectionService.DetectPitchMcLeod(
                bassBuffer, window, SampleRate, PitchDetectionService.DefaultMinFrequencyHz);
            withDefaultWindow = PitchDetectionService.DetectPitchMcLeod(
                defaultWindow, WindowSize, SampleRate, minHz);
        }

        Assert.True(withBass > 0, $"McLeod returned {withBass} Hz (minHz={minHz}, window={window}, e1={e1:F2})");
        double cents = 1200 * Math.Log(withBass / e1, 2);
        Assert.InRange(cents, -50, 50);

        if (withDefaultFloor > 0)
        {
            double defaultCents = 1200 * Math.Log(withDefaultFloor / e1, 2);
            Assert.True(Math.Abs(defaultCents) > 50);
        }

        Assert.True(withDefaultWindow == 0 || Math.Abs(1200 * Math.Log(withDefaultWindow / e1, 2)) > 50);
    }

    private static float[] Sine(double hz, int length, int sampleRate, float amplitude)
    {
        var buf = new float[length];
        double step = 2 * Math.PI * hz / sampleRate;
        double phase = 0;
        for (int i = 0; i < length; i++)
        {
            buf[i] = amplitude * (float)Math.Sin(phase);
            phase += step;
        }
        return buf;
    }
}
