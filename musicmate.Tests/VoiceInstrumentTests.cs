using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class VoiceInstrumentTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public static TheoryData<string, string, string, string> VoiceCategories { get; } = new()
    {
        { "voice-soprano", "Voice – Soprano", "C4", "C6" },
        { "voice-mezzo-soprano", "Voice – Mezzo-soprano", "A3", "A5" },
        { "voice-contralto", "Voice – Contralto", "F3", "F5" },
        { "voice-countertenor", "Voice – Countertenor", "G3", "E5" },
        { "voice-tenor", "Voice – Tenor", "C3", "C5" },
        { "voice-baritone", "Voice – Baritone", "A2", "A4" },
        { "voice-bass-baritone", "Voice – Bass-baritone", "F2", "F4" },
        { "voice-bass", "Voice – Bass", "E2", "E4" },
    };

    public VoiceInstrumentTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Theory]
    [MemberData(nameof(VoiceCategories))]
    public void Voice_UsesConcertPitchKeyC_AndConfiguredRange(
        string id, string displayName, string low, string high)
    {
        var profile = InstrumentCatalog.Resolve(id);
        Assert.Equal(id, profile.Id);
        Assert.Equal(displayName, profile.DisplayName);
        Assert.Equal("C", profile.InstrumentKey);
        Assert.Equal(0, profile.TransposeOffset);
        Assert.Equal(low, profile.PracticalLowestNote);
        Assert.Equal(high, profile.PracticalHighestNote);

        var byDisplay = InstrumentCatalog.Resolve(displayName);
        Assert.Equal(id, byDisplay.Id);

        var (autoLow, autoHigh) = InstrumentCatalog.GetAutomaticRange(profile, level: 0);
        Assert.Equal(low, autoLow);
        Assert.Equal(high, autoHigh);
    }

    [Fact]
    public void AllVoiceCategories_AppearInInstrumentPickerList()
    {
        foreach (var row in VoiceCategories)
        {
            string displayName = (string)row[1];
            Assert.Contains(displayName, InstrumentCatalog.DisplayNames);
        }
    }

    [Theory]
    [MemberData(nameof(VoiceCategories))]
    public void SelectingVoice_SetsSessionRangeToConfiguredLimits(
        string id, string displayName, string low, string high)
    {
        _ = displayName;
        var session = new NoteSessionService { ChildLevel = 0 };
        session.ClearNoteRangeCustomization();
        session.Instrument = id;

        Assert.Equal(id, session.Instrument);
        Assert.Equal(0, session.InstrumentTransposeOffset);
        Assert.Equal("C", session.InstrumentKey);
        Assert.Equal(low, session.LowestNote);
        Assert.Equal(high, session.HighestNote);
    }

    [Fact]
    public void ChangingVoice_PreservesCustomRange_WhenInsideNewLimits()
    {
        var session = new NoteSessionService { ChildLevel = 0 };
        session.ClearNoteRangeCustomization();
        session.Instrument = "voice-soprano";
        session.LowestNote = "D4";
        session.HighestNote = "G5";
        Assert.True(session.NoteRangeCustomized);

        session.Instrument = "voice-mezzo-soprano"; // A3–A5

        Assert.Equal("D4", session.LowestNote);
        Assert.Equal("G5", session.HighestNote);
        Assert.True(session.NoteRangeCustomized);
    }

    [Fact]
    public void ChangingVoice_ClampsCustomRange_WhenOutsideNewLimits()
    {
        var session = new NoteSessionService { ChildLevel = 0 };
        session.ClearNoteRangeCustomization();
        session.Instrument = "voice-soprano"; // C4–C6
        // Change away from defaults so NoteRangeCustomized becomes true.
        session.LowestNote = "D4";
        session.HighestNote = "B5";
        Assert.True(session.NoteRangeCustomized);

        session.Instrument = "voice-bass"; // E2–E4

        Assert.Equal("D4", session.LowestNote); // still inside E2–E4
        Assert.Equal("E4", session.HighestNote); // B5 clamped down to E4
    }

    [Fact]
    public void ChangingVoice_ClampsLowEnd_WhenBelowNewLimits()
    {
        var session = new NoteSessionService { ChildLevel = 0 };
        session.ClearNoteRangeCustomization();
        session.Instrument = "voice-bass";
        session.LowestNote = "F2";
        session.HighestNote = "D4";
        Assert.True(session.NoteRangeCustomized);

        session.Instrument = "voice-soprano"; // C4–C6

        Assert.Equal("C4", session.LowestNote); // F2 clamped up
        Assert.Equal("D4", session.HighestNote); // still inside
    }

    [Theory]
    [MemberData(nameof(VoiceCategories))]
    public void GeneratedNotes_StayInsideSelectedVoiceRange(
        string id, string displayName, string low, string high)
    {
        _ = displayName;
        int minMidi = NoteSessionService.NoteNameToMidi(low);
        int maxMidi = NoteSessionService.NoteNameToMidi(high);

        var gen = new MusicSequenceGenerator
        {
            Key = "C",
            Scale = "Major",
            LowestNote = low,
            HighestNote = high,
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 8,
            RhythmVarietyPercent = 40,
            SmallestDuration = NoteDuration.Eighth,
            RestChancePercent = 10,
            UseScaleOrder = false,
            UseMotifPhrases = true,
            AccidentalPercent = 0,
            RandomSeed = id.GetHashCode(),
        };

        var pitched = MusicSequenceGenerator.Flatten(gen.GenerateSequence())
            .Where(n => !n.IsRest)
            .ToList();

        Assert.NotEmpty(pitched);
        Assert.All(pitched, n =>
        {
            Assert.InRange(n.MidiNumber, minMidi, maxMidi);
        });
    }

    [Theory]
    [MemberData(nameof(VoiceCategories))]
    public void AvailableInstrumentMidis_MatchConfiguredVoiceRange(
        string id, string displayName, string low, string high)
    {
        _ = displayName;
        var session = new NoteSessionService { ChildLevel = 0 };
        session.ClearNoteRangeCustomization();
        session.Instrument = id;

        int minMidi = NoteSessionService.NoteNameToMidi(low);
        int maxMidi = NoteSessionService.NoteNameToMidi(high);
        var midis = session.AvailableInstrumentMidis;

        Assert.Equal(minMidi, midis.Min());
        Assert.Equal(maxMidi, midis.Max());
        Assert.Equal(maxMidi - minMidi + 1, midis.Count);
    }

    [Fact]
    public void NoteRangePicker_IncludesEveryInstrumentPracticalExtreme()
    {
        var picker = InstrumentCatalog.BuildNoteRangePickerNames();
        var pickerMidis = picker
            .Select(NoteSessionService.NoteNameToMidi)
            .Where(m => m >= 0)
            .ToHashSet();

        foreach (var profile in InstrumentCatalog.All)
        {
            int low = NoteSessionService.NoteNameToMidi(profile.PracticalLowestNote);
            int high = NoteSessionService.NoteNameToMidi(profile.PracticalHighestNote);
            Assert.True(
                pickerMidis.Contains(low),
                $"{profile.DisplayName} low {profile.PracticalLowestNote} missing from picker");
            Assert.True(
                pickerMidis.Contains(high),
                $"{profile.DisplayName} high {profile.PracticalHighestNote} missing from picker");
        }

        var (boundLow, boundHigh) = InstrumentCatalog.GetCatalogPracticalMidiBounds();
        Assert.Equal(
            InstrumentCatalog.All.Min(p => NoteSessionService.NoteNameToMidi(p.PracticalLowestNote)),
            boundLow);
        Assert.Equal(
            InstrumentCatalog.All.Max(p => NoteSessionService.NoteNameToMidi(p.PracticalHighestNote)),
            boundHigh);
        Assert.Contains(boundHigh, pickerMidis);
        Assert.Contains(boundLow, pickerMidis);
    }

    [Fact]
    public void VoiceBass_AtLevel100_UsesFullPracticalRange_E2ToE4()
    {
        var session = new NoteSessionService();
        session.ClearNoteRangeCustomization();
        session.Instrument = "voice-bass";
        session.ChildLevel = 100;

        Assert.Equal("E2", session.LowestNote);
        Assert.Equal("E4", session.HighestNote);

        var (autoLow, autoHigh) = InstrumentCatalog.GetAutomaticRange(
            InstrumentCatalog.Resolve("voice-bass"), level: 100);
        Assert.Equal("E2", autoLow);
        Assert.Equal("E4", autoHigh);
    }

    [Theory]
    [InlineData(1, "C4", "E4")]   // beginner low ∩ bass practical high
    [InlineData(100, "E2", "E4")] // top level uses full voice-bass practical range
    public void VoiceBass_LevelIntersectsPracticalRange(
        int level, string expectedLow, string expectedHigh)
    {
        var (low, high) = InstrumentCatalog.GetAutomaticRange(
            InstrumentCatalog.Resolve("voice-bass"), level);
        Assert.Equal(expectedLow, low);
        Assert.Equal(expectedHigh, high);
    }
}
