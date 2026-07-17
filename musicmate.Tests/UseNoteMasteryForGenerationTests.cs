using Microsoft.Extensions.DependencyInjection;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class UseNoteMasteryForGenerationTests : IDisposable
{
    static UseNoteMasteryForGenerationTests()
    {
        SQLitePCL.Batteries.Init();
    }

    private readonly Dictionary<string, object?> _sessionStore = new();
    private readonly Dictionary<string, string> _themeStore = new();
    private readonly string _noteDbPath;
    private readonly NoteDatabase _noteDatabase;
    private readonly NoteSessionService _session;
    private readonly ThemeService _theme;
    private readonly SettingsResetService _reset;

    public UseNoteMasteryForGenerationTests()
    {
        SessionPreferences.TestStore = _sessionStore;
        ThemeService.TestStore = _themeStore;
        _sessionStore.Clear();
        _themeStore.Clear();

        _noteDbPath = Path.Combine(Path.GetTempPath(), $"mm_mastery_gen_{Guid.NewGuid():N}.db3");
        _noteDatabase = new NoteDatabase(_noteDbPath);

        var services = new ServiceCollection();
        services.AddSingleton(_noteDatabase);
        ServiceHelper.Initialize(services.BuildServiceProvider());

        _session = new NoteSessionService();
        _theme = new ThemeService();
        _theme.LoadFromPreferences();
        _reset = new SettingsResetService(_session, _theme);
        _reset.ResetToFactoryDefaults();
    }

    public void Dispose()
    {
        SessionPreferences.TestStore = null;
        ThemeService.TestStore = null;
        ServiceHelper.Initialize(new ServiceCollection().BuildServiceProvider());
        try { if (File.Exists(_noteDbPath)) File.Delete(_noteDbPath); } catch { }
    }

    [Fact]
    public void FactoryDefault_IsOn()
    {
        Assert.True(MasteryPreferenceDefaults.UseNoteMasteryForGeneration);
        Assert.True(_session.UseNoteMasteryForGeneration);
    }

    [Fact]
    public void Setting_PersistsAcrossSessionRecreation()
    {
        _session.UseNoteMasteryForGeneration = false;
        Assert.False(SessionPreferences.Get("musicmate.UseNoteMasteryForGeneration", true));

        var reopened = new NoteSessionService();
        Assert.False(reopened.UseNoteMasteryForGeneration);

        reopened.UseNoteMasteryForGeneration = true;
        var again = new NoteSessionService();
        Assert.True(again.UseNoteMasteryForGeneration);
    }

    [Fact]
    public void TurningOff_MakesAreFactoryDefaultsAppliedFalse_AndFactoryResetRestoresOn()
    {
        Assert.True(_reset.AreFactoryDefaultsApplied);

        _session.UseNoteMasteryForGeneration = false;
        _reset.EvaluateAreFactoryDefaultsApplied();
        Assert.False(_reset.AreFactoryDefaultsApplied);

        _reset.ResetToFactoryDefaults();
        Assert.True(_session.UseNoteMasteryForGeneration);
        Assert.True(_reset.AreFactoryDefaultsApplied);
    }

    [Fact]
    public void CustomDefaults_PreserveAndRestoreSetting()
    {
        _session.UseNoteMasteryForGeneration = false;
        _reset.SaveCustomDefaultsFromCurrent();

        _reset.ResetToFactoryDefaults();
        Assert.True(_session.UseNoteMasteryForGeneration);

        _reset.RestoreCustomDefaults();
        Assert.False(_session.UseNoteMasteryForGeneration);
    }

    [Fact]
    public async Task GetMasteredMidiNumbers_RespectsSwitch_WithoutErasingStats()
    {
        await _noteDatabase.InitializeAsync();
        await _noteDatabase.InsertOrReplaceAsync(CreateMasteredStat("C4"));
        await _noteDatabase.InsertOrReplaceAsync(CreateUnmasteredStat("D4"));

        _session.MasteredMethod = "Streak";
        _session.StreakCrit = 3;
        _session.UseNoteMasteryForGeneration = true;

        var on = await _session.GetMasteredMidiNumbersAsync();
        int c4 = NoteSessionService.NoteNameToMidi("C4");
        Assert.Contains(c4, on);
        Assert.DoesNotContain(NoteSessionService.NoteNameToMidi("D4"), on);

        _session.UseNoteMasteryForGeneration = false;
        var off = await _session.GetMasteredMidiNumbersAsync();
        Assert.Empty(off);

        var stillThere = await _noteDatabase.GetByWrittenNameAsync("C4");
        Assert.NotNull(stillThere);
        Assert.Equal(5, stillThere!.Streak);

        _session.UseNoteMasteryForGeneration = true;
        var restored = await _session.GetMasteredMidiNumbersAsync();
        Assert.Contains(c4, restored);
    }

    [Fact]
    public async Task FilterNotesExcludingMastered_IgnoresMasteryWhenOff()
    {
        await _noteDatabase.InitializeAsync();
        await _noteDatabase.InsertOrReplaceAsync(CreateMasteredStat("E4"));
        await _noteDatabase.InsertOrReplaceAsync(CreateUnmasteredStat("F4"));

        _session.MasteredMethod = "Streak";
        _session.StreakCrit = 3;

        var notes = new List<NoteInfo>
        {
            new() { Name = "E4" },
            new() { Name = "F4" },
            new() { Name = "G4" },
        };

        _session.UseNoteMasteryForGeneration = true;
        var filteredOn = await PracticeSessionLifecycle.FilterNotesExcludingMasteredAsync(
            notes, _session, _noteDatabase);
        Assert.DoesNotContain(filteredOn, n => n.Name == "E4");
        Assert.Contains(filteredOn, n => n.Name == "F4");

        _session.UseNoteMasteryForGeneration = false;
        var filteredOff = await PracticeSessionLifecycle.FilterNotesExcludingMasteredAsync(
            notes, _session, _noteDatabase);
        Assert.Equal(3, filteredOff.Count);
        Assert.Contains(filteredOff, n => n.Name == "E4");
    }

    [Fact]
    public void Generation_WithMasteryOff_IgnoresDifferentExclusionHistories_SameSeed()
    {
        const int seed = 4242;
        var historyA = new HashSet<int>
        {
            NoteSessionService.NoteNameToMidi("C4"),
            NoteSessionService.NoteNameToMidi("E4"),
        };
        var historyB = new HashSet<int>
        {
            NoteSessionService.NoteNameToMidi("D4"),
            NoteSessionService.NoteNameToMidi("G4"),
            NoteSessionService.NoteNameToMidi("A4"),
        };

        // When mastery use is OFF, callers pass an empty exclusion set (GetMasteredMidiNumbersAsync).
        var pitchesA = FlattenPitches(CreateRandomGenerator(seed, new HashSet<int>()));
        var pitchesB = FlattenPitches(CreateRandomGenerator(seed, new HashSet<int>()));
        Assert.Equal(pitchesA, pitchesB);

        // Different histories would have produced different exclusions when ON — prove they differ.
        var onA = FlattenPitches(CreateRandomGenerator(seed, historyA));
        var onB = FlattenPitches(CreateRandomGenerator(seed, historyB));
        Assert.NotEqual(onA, onB);

        // Empty exclusions (OFF) must not match a run that excluded mastered notes (ON).
        Assert.NotEqual(pitchesA, onA);
    }

    [Fact]
    public void Generation_WithMasteryOff_TreatsMasteredAndUnmasteredEqually()
    {
        const int seed = 991;
        int masteredMidi = NoteSessionService.NoteNameToMidi("C4");
        int unmasteredMidi = NoteSessionService.NoteNameToMidi("D4");

        var withEmpty = CreateRandomGenerator(seed, new HashSet<int>());
        var measures = withEmpty.GenerateSequence();
        var flat = MusicSequenceGenerator.Flatten(measures)
            .Where(n => !n.IsRest)
            .Select(n => n.MidiNumber)
            .ToList();

        Assert.Contains(masteredMidi, flat);
        Assert.Contains(unmasteredMidi, flat);

        // Same seed with only C4 excluded (ON-style) must omit C4 when enough pool remains.
        var excluded = FlattenPitches(CreateRandomGenerator(seed, new HashSet<int> { masteredMidi }));
        Assert.DoesNotContain(masteredMidi, excluded);
        Assert.Contains(unmasteredMidi, excluded);
    }

    private static MusicSequenceGenerator CreateRandomGenerator(int seed, HashSet<int> excluded)
        => new()
        {
            Key = "C",
            Scale = "Major",
            LowestNote = "C4",
            HighestNote = "C5",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 8,
            RhythmVarietyPercent = 0,
            SmallestDuration = NoteDuration.Quarter,
            UseScaleOrder = false,
            AccidentalPercent = 0,
            RestChancePercent = 0,
            ExcludedMidiNumbers = excluded,
            RandomSeed = seed,
        };

    private static List<int> FlattenPitches(MusicSequenceGenerator gen)
        => MusicSequenceGenerator.Flatten(gen.GenerateSequence())
            .Where(n => !n.IsRest)
            .Select(n => n.MidiNumber)
            .ToList();

    private static NoteStat CreateMasteredStat(string writtenName)
        => new()
        {
            WrittenName = writtenName,
            Streak = 5,
            Correct = 10,
            Wrong = 0,
            OverallCorrectCount = 10,
            OverallWrongCount = 0,
            PitchCorrectCount = 10,
            PitchWrongCount = 0,
            Mastered = 1,
        };

    private static NoteStat CreateUnmasteredStat(string writtenName)
        => new()
        {
            WrittenName = writtenName,
            Streak = 0,
            Correct = 1,
            Wrong = 4,
            OverallCorrectCount = 1,
            OverallWrongCount = 4,
            PitchCorrectCount = 1,
            PitchWrongCount = 4,
            Mastered = 0,
        };
}
