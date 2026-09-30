using Microsoft.Maui.Graphics;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class ClefToggleTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public ClefToggleTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Fact]
    public void Euphonium_TogglesBassAndTreble_WithoutChangingPitchesOrTheTune()
    {
        var session = new NoteSessionService
        {
            Instrument = "Euphonium",
            Key = "F",
            Tune = "Major Scale",
            Tempo = 72,
            ChildLevel = 15,
        };
        session.NotesToDraw.Add(new NoteInfo
        {
            Midi = NoteSessionService.NoteNameToMidi("C4"),
            Name = "C4",
            TargetFreq = 261.63,
        });
        session.NotesToDraw.Add(new NoteInfo
        {
            Midi = NoteSessionService.NoteNameToMidi("E2"),
            Name = "E2",
            TargetFreq = 82.41,
        });

        Assert.Equal("euphonium", session.Instrument);
        Assert.Equal(Clef.Bass, session.NotationClef);
        Assert.True(session.CanToggleNotationClef);
        Assert.Equal("Bass clef. Tap to change to treble clef.", session.NotationClefAccessibilityText);
        Assert.Equal(0, session.InstrumentTransposeOffset);

        int writtenC4 = NoteSessionService.NoteNameToMidi("C4");
        double soundingHz = TunerReferenceNoteCatalog.ConcertFrequencyHz(writtenC4, session.InstrumentTransposeOffset);
        int bassSteps = ClefStaffPosition.StepsBelowMiddle(Clef.Bass, 'C', 4);

        session.ToggleNotationClef();

        Assert.Equal(Clef.Treble, session.NotationClef);
        Assert.Equal("Treble clef. Tap to change to bass clef.", session.NotationClefAccessibilityText);
        Assert.Equal(0, session.InstrumentTransposeOffset);
        Assert.Equal(soundingHz, TunerReferenceNoteCatalog.ConcertFrequencyHz(writtenC4, session.InstrumentTransposeOffset));
        Assert.NotEqual(bassSteps, ClefStaffPosition.StepsBelowMiddle(session.NotationClef, 'C', 4));
        Assert.Equal("F", session.Key);
        Assert.Equal("Major Scale", session.Tune);
        Assert.Equal(72, session.Tempo);
        Assert.Equal(15, session.ChildLevel);
        Assert.Equal(2, session.NotesToDraw.Count);
        Assert.Equal(writtenC4, session.NotesToDraw[0].Midi);
        Assert.Equal("C4", session.NotesToDraw[0].Name);
        Assert.Equal(261.63, session.NotesToDraw[0].TargetFreq);
        Assert.Equal(NoteSessionService.NoteNameToMidi("E2"), session.NotesToDraw[1].Midi);

        session.ToggleNotationClef();
        Assert.Equal(Clef.Bass, session.NotationClef);
        Assert.Equal(writtenC4, session.NotesToDraw[0].Midi);
    }

    [Fact]
    public void EuphoniumBassClef_StaysBassAcrossPlayStopAndReentry()
    {
        var session = new NoteSessionService { Instrument = "Euphonium" };
        session.ToggleNotationClef();
        session.ToggleNotationClef();
        Assert.Equal(Clef.Bass, session.NotationClef);
        int midi = NoteSessionService.NoteNameToMidi("C4");
        session.NotesToDraw.Add(new NoteInfo { Midi = midi, Name = "C4", TargetFreq = 261.63 });
        string key = session.Key;
        int tempo = session.Tempo;
        string? tune = session.Tune;

        string? saved = session.BeginPlaybackInstrumentOverride();
        Assert.Null(saved);
        Assert.Equal(Clef.Bass, session.NotationClef);
        Assert.Equal("euphonium", session.Instrument);
        Assert.Equal(0, session.InstrumentTransposeOffset);
        Assert.Equal(midi, session.NotesToDraw[0].Midi);
        Assert.Equal(key, session.Key);
        Assert.Equal(tempo, session.Tempo);
        Assert.Equal(tune, session.Tune);
        session.EndPlaybackInstrumentOverride(saved);
        Assert.Equal(Clef.Bass, session.NotationClef);

        saved = session.BeginPlaybackInstrumentOverride();
        Assert.Null(saved);
        Assert.Equal(Clef.Bass, session.NotationClef);
        session.EndPlaybackInstrumentOverride(saved);
        Assert.Equal(Clef.Bass, session.NotationClef);

        var reopened = new NoteSessionService { Instrument = "euphonium" };
        Assert.Equal(Clef.Bass, reopened.NotationClef);
        Assert.Equal("euphonium", reopened.Instrument);
    }

    [Fact]
    public void TransposingInstrument_StillUsesConcertPitchOnlyDuringPlayback()
    {
        var session = new NoteSessionService { Instrument = "bb-clarinet" };
        Assert.Equal(Clef.Treble, session.NotationClef);

        string? saved = session.BeginPlaybackInstrumentOverride();
        Assert.Equal("bb-clarinet", saved);
        Assert.Equal("concert-pitch", session.Instrument);
        Assert.Equal(Clef.Treble, session.NotationClef);

        session.EndPlaybackInstrumentOverride(saved);
        Assert.Equal("bb-clarinet", session.Instrument);
        Assert.Equal(Clef.Treble, session.NotationClef);
    }

    [Fact]
    public void ClefChoice_PersistsPerInstrument_AndSurvivesANewSession()
    {
        var session = new NoteSessionService { Instrument = "euphonium" };
        session.ToggleNotationClef();
        Assert.Equal(Clef.Treble, session.NotationClef);

        session.Instrument = "tuba";
        Assert.Equal(Clef.Bass, session.NotationClef);
        Assert.True(session.CanToggleNotationClef);
        session.ToggleNotationClef();
        Assert.Equal(Clef.Treble, session.NotationClef);

        session.Instrument = "bb-clarinet";
        Assert.Equal(Clef.Treble, session.NotationClef);
        Assert.False(session.CanToggleNotationClef);
        session.ToggleNotationClef();
        Assert.Equal(Clef.Treble, session.NotationClef);

        session.Instrument = "euphonium";
        Assert.Equal(Clef.Treble, session.NotationClef);

        var reopened = new NoteSessionService { Instrument = "euphonium" };
        Assert.Equal(Clef.Treble, reopened.NotationClef);
        Assert.Equal(
            "Treble",
            _store[NoteSessionService.NotationClefPreferenceKey("euphonium")]);
        Assert.Equal(
            "Treble",
            _store[NoteSessionService.NotationClefPreferenceKey("tuba")]);
        Assert.False(_store.ContainsKey(NoteSessionService.NotationClefPreferenceKey("bb-clarinet")));

        var tubaAgain = new NoteSessionService { Instrument = "tuba" };
        Assert.Equal(Clef.Treble, tubaAgain.NotationClef);
    }

    [Fact]
    public void SingleClefInstruments_DoNotToggle()
    {
        var clarinet = new NoteSessionService { Instrument = "bb-clarinet" };
        Assert.False(clarinet.CanToggleNotationClef);
        Assert.Equal("Treble clef.", clarinet.NotationClefAccessibilityText);
        clarinet.ToggleNotationClef();
        Assert.Equal(Clef.Treble, clarinet.NotationClef);

        var flute = new NoteSessionService { Instrument = "Flute" };
        Assert.Equal("concert-pitch", flute.Instrument);
        Assert.False(flute.CanToggleNotationClef);
        flute.ToggleNotationClef();
        Assert.Equal(Clef.Treble, flute.NotationClef);

        Assert.Equal("concert-pitch", InstrumentCatalog.Resolve("Bass Guitar").Id);
        Assert.False(InstrumentCatalog.Resolve("Bass Guitar").CanToggleNotationClef);
        Assert.Equal("concert-pitch", InstrumentCatalog.Resolve("Piano").Id);
        Assert.False(InstrumentCatalog.Resolve("Piano").CanToggleNotationClef);

        Assert.Equal("voice-baritone", InstrumentCatalog.Resolve("Baritone").Id);
        Assert.False(InstrumentCatalog.Resolve("Baritone").CanToggleNotationClef);

        var contrabassoon = InstrumentCatalog.Resolve("Contrabassoon");
        Assert.Equal("contrabassoon", contrabassoon.Id);
        Assert.Equal(Clef.Bass, contrabassoon.DefaultClef);
        Assert.False(contrabassoon.CanToggleNotationClef);
        Assert.DoesNotContain(Clef.Treble, contrabassoon.NotationClefs);
        Assert.Equal(-12, contrabassoon.TransposeOffset);

        var combinedLegacy = InstrumentCatalog.Resolve("C - 1 octave,  Double Bass, Contrabassoon");
        Assert.Equal("double-bass", combinedLegacy.Id);
    }

    public static TheoryData<string, string, Clef> ClefToggleInstruments { get; } = new()
    {
        { "Euphonium / Baritone Horn", "euphonium", Clef.Bass },
        { "Trombone", "trombone", Clef.Treble },
        { "Cello", "cello", Clef.Treble },
        { "Bassoon", "bassoon", Clef.Bass },
        { "Double Bass", "double-bass", Clef.Treble },
        { "Tuba", "tuba", Clef.Bass },
    };

    [Theory]
    [MemberData(nameof(ClefToggleInstruments))]
    public void ClefTap_TogglesBothWays_KeepsPitchAndMovesStaffPosition(
        string selection, string instrumentId, Clef defaultClef)
    {
        var session = new NoteSessionService { Instrument = selection, Key = "F" };
        Assert.Equal(instrumentId, session.Instrument);
        Assert.True(session.CanToggleNotationClef);
        Assert.Equal(defaultClef, session.NotationClef);
        Assert.Contains(defaultClef == Clef.Bass ? "Tap to change to treble" : "Tap to change to bass",
            session.NotationClefAccessibilityText);

        int offset = session.InstrumentTransposeOffset;
        int c4 = NoteSessionService.NoteNameToMidi("C4");
        int fSharp = NoteSessionService.NoteNameToMidi("F#4");
        double c4Hz = TunerReferenceNoteCatalog.ConcertFrequencyHz(c4, offset);
        session.NotesToDraw.Add(new NoteInfo { Midi = c4, Name = "C4", TargetFreq = c4Hz });
        session.NotesToDraw.Add(new NoteInfo { Midi = fSharp, Name = "F#4", TargetFreq = 369.99 });

        int trebleC4 = ClefStaffPosition.StepsBelowMiddle(Clef.Treble, 'C', 4);
        int bassC4 = ClefStaffPosition.StepsBelowMiddle(Clef.Bass, 'C', 4);
        int trebleFSharp = ClefStaffPosition.StepsBelowMiddle(Clef.Treble, 'F', 4);
        int bassFSharp = ClefStaffPosition.StepsBelowMiddle(Clef.Bass, 'F', 4);
        Assert.Equal(6, trebleC4);
        Assert.Equal(-6, bassC4);
        Assert.NotEqual(trebleFSharp, bassFSharp);
        Assert.NotEqual(
            ClefStaffPosition.KeySignaturePositions(Clef.Treble, flats: true)[0],
            ClefStaffPosition.KeySignaturePositions(Clef.Bass, flats: true)[0]);

        Clef other = defaultClef == Clef.Bass ? Clef.Treble : Clef.Bass;
        session.ToggleNotationClef();
        Assert.Equal(other, session.NotationClef);
        AssertPitchUnchanged(session, offset, c4, c4Hz, fSharp);
        Assert.Equal(other == Clef.Bass ? bassC4 : trebleC4,
            ClefStaffPosition.StepsBelowMiddle(session.NotationClef, 'C', 4));
        Assert.Equal(other == Clef.Bass ? bassFSharp : trebleFSharp,
            ClefStaffPosition.StepsBelowMiddle(session.NotationClef, 'F', 4));
        Assert.Equal("F", session.Key);

        session.ToggleNotationClef();
        Assert.Equal(defaultClef, session.NotationClef);
        AssertPitchUnchanged(session, offset, c4, c4Hz, fSharp);
        Assert.Equal(defaultClef == Clef.Bass ? bassC4 : trebleC4,
            ClefStaffPosition.StepsBelowMiddle(session.NotationClef, 'C', 4));

        var reopened = new NoteSessionService { Instrument = instrumentId };
        Assert.Equal(defaultClef, reopened.NotationClef);
        Assert.Equal(offset, reopened.InstrumentTransposeOffset);
    }

    [Theory]
    [InlineData("bb-clarinet")]
    [InlineData("Flute")]
    [InlineData("Piano")]
    [InlineData("Bass Guitar")]
    [InlineData("Baritone")]
    [InlineData("Contrabassoon")]
    [InlineData("F Horn")]
    public void OtherInstruments_IgnoreClefTap(string selection)
    {
        var session = new NoteSessionService { Instrument = selection };
        Assert.False(session.CanToggleNotationClef);
        Assert.DoesNotContain("Tap to change", session.NotationClefAccessibilityText);
        Clef before = session.NotationClef;
        int offset = session.InstrumentTransposeOffset;
        string id = session.Instrument;

        session.ToggleNotationClef();

        Assert.Equal(before, session.NotationClef);
        Assert.Equal(offset, session.InstrumentTransposeOffset);
        Assert.Equal(id, session.Instrument);
        Assert.False(_store.ContainsKey(NoteSessionService.NotationClefPreferenceKey(id)));
    }

    [Fact]
    public void DoubleBassClef_SurvivesPlaybackAndReturns()
    {
        var session = new NoteSessionService { Instrument = "Double Bass" };
        session.ToggleNotationClef();
        Assert.Equal(Clef.Bass, session.NotationClef);
        Assert.Equal(-12, session.InstrumentTransposeOffset);
        int written = NoteSessionService.NoteNameToMidi("G3");
        double hz = TunerReferenceNoteCatalog.ConcertFrequencyHz(written, -12);
        session.NotesToDraw.Add(new NoteInfo { Midi = written, Name = "G3", TargetFreq = hz });

        string? saved = session.BeginPlaybackInstrumentOverride();
        Assert.Equal("double-bass", saved);
        Assert.Equal("concert-pitch", session.Instrument);
        Assert.Equal(written, session.NotesToDraw[0].Midi);
        Assert.Equal("G3", session.NotesToDraw[0].Name);

        session.EndPlaybackInstrumentOverride(saved);
        Assert.Equal("double-bass", session.Instrument);
        Assert.Equal(Clef.Bass, session.NotationClef);
        Assert.Equal(-12, session.InstrumentTransposeOffset);
        Assert.Equal(written, session.NotesToDraw[0].Midi);
        Assert.Equal(hz, session.NotesToDraw[0].TargetFreq);
        Assert.Equal(-6, ClefStaffPosition.StepsBelowMiddle(Clef.Bass, 'C', 4));
        Assert.NotEqual(
            ClefStaffPosition.StepsBelowMiddle(Clef.Treble, 'G', 3),
            ClefStaffPosition.StepsBelowMiddle(session.NotationClef, 'G', 3));
    }

    private static void AssertPitchUnchanged(
        NoteSessionService session, int offset, int c4, double c4Hz, int fSharp)
    {
        Assert.Equal(offset, session.InstrumentTransposeOffset);
        Assert.Equal(c4Hz, TunerReferenceNoteCatalog.ConcertFrequencyHz(c4, session.InstrumentTransposeOffset));
        Assert.Equal(2, session.NotesToDraw.Count);
        Assert.Equal(c4, session.NotesToDraw[0].Midi);
        Assert.Equal("C4", session.NotesToDraw[0].Name);
        Assert.Equal(c4Hz, session.NotesToDraw[0].TargetFreq);
        Assert.Equal(fSharp, session.NotesToDraw[1].Midi);
        Assert.Equal("F#4", session.NotesToDraw[1].Name);
    }

    [Fact]
    public void ClefHitTarget_IsLargerThanTheGlyph_AndStopsBeforeTheTimeSignature()
    {
        var glyph = new RectF(8, 30, 22, 36);
        var timeSignature = new RectF(90, 28, 28, 40);
        RectF hit = ClefHitTargetLayout.Expand(glyph, timeSignature);

        Assert.True(hit.Width > glyph.Width);
        Assert.True(hit.Height > glyph.Height);
        Assert.True(hit.Width >= ClefHitTargetLayout.MinimumSize || hit.X + hit.Width <= timeSignature.X);
        Assert.True(hit.X <= glyph.X);
        Assert.True(hit.Y <= glyph.Y);
        Assert.True(hit.X + hit.Width >= glyph.X + glyph.Width);
        Assert.True(hit.Y + hit.Height >= glyph.Y + glyph.Height);
        Assert.True(hit.X + hit.Width <= timeSignature.X - 4f);
    }
}
