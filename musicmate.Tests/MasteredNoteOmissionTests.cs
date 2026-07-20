using Microsoft.Extensions.DependencyInjection;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class MasteredNoteOmissionTests : IDisposable
{
    static MasteredNoteOmissionTests()
    {
        SQLitePCL.Batteries.Init();
    }

    private readonly Dictionary<string, object?> _sessionStore = new();
    private readonly string _noteDbPath;
    private readonly NoteDatabase _noteDatabase;
    private readonly NoteSessionService _session;

    public MasteredNoteOmissionTests()
    {
        SessionPreferences.TestStore = _sessionStore;
        _sessionStore.Clear();

        _noteDbPath = Path.Combine(Path.GetTempPath(), $"mm_omit_{Guid.NewGuid():N}.db3");
        _noteDatabase = new NoteDatabase(_noteDbPath);

        var services = new ServiceCollection();
        services.AddSingleton(_noteDatabase);
        ServiceHelper.Initialize(services.BuildServiceProvider());

        _session = new NoteSessionService();
        _session.MasteredMethod = "Streak";
        _session.StreakCrit = 3;
        _session.UseNoteMasteryForGeneration = true;
        _session.Key = "C";
        _session.LowestNote = "C4";
        _session.HighestNote = "G4";
        _session.Instrument = "concert-pitch";
    }

    public void Dispose()
    {
        SessionPreferences.TestStore = null;
        ServiceHelper.Initialize(new ServiceCollection().BuildServiceProvider());
        try { if (File.Exists(_noteDbPath)) File.Delete(_noteDbPath); } catch { }
    }

    [Fact]
    public async Task Random_OmitsMasteredD4AndE4()
    {
        await SeedMasteredAsync("D4", "E4");
        var excluded = await _session.GetMasteredMidiNumbersAsync();
        int d4 = NoteSessionService.NoteNameToMidi("D4");
        int e4 = NoteSessionService.NoteNameToMidi("E4");
        Assert.Contains(d4, excluded);
        Assert.Contains(e4, excluded);

        var pitches = FlattenPitches(CreateRandomGenerator(seed: 42, excluded));
        Assert.DoesNotContain(d4, pitches);
        Assert.DoesNotContain(e4, pitches);
        Assert.Contains(NoteSessionService.NoteNameToMidi("C4"), pitches);
    }

    [Fact]
    public async Task ByLevelRandom_OmitsMasteredD4AndE4()
    {
        await SeedMasteredAsync("D4", "E4");
        var excluded = await _session.GetMasteredMidiNumbersAsync();

        var gen = CreateRandomGenerator(seed: 77, excluded, high: "C6");
        gen.ActivityType = "ByLevel-Random";
        var pitches = FlattenPitches(gen);

        Assert.DoesNotContain(NoteSessionService.NoteNameToMidi("D4"), pitches);
        Assert.DoesNotContain(NoteSessionService.NoteNameToMidi("E4"), pitches);
        Assert.NotEqual(
            MasteredNoteOmission.FallbackKind.AllowedMasteredAllEligibleMastered,
            gen.LastMasteryFallback);
    }

    [Fact]
    public async Task OctaveSpecific_D4Mastered_DoesNotOmitD5()
    {
        await SeedMasteredAsync("D4");
        _session.HighestNote = "C6";
        var excluded = await _session.GetMasteredMidiNumbersAsync();

        var gen = CreateRandomGenerator(seed: 11, excluded, high: "C6");
        var pitches = FlattenPitches(gen);

        Assert.DoesNotContain(NoteSessionService.NoteNameToMidi("D4"), pitches);
        // D5 must remain available in the pool / output when present in range.
        Assert.DoesNotContain(
            NoteSessionService.NoteNameToMidi("D4"),
            excluded.Where(m => m == NoteSessionService.NoteNameToMidi("D5")));
        Assert.DoesNotContain(NoteSessionService.NoteNameToMidi("D5"), excluded);
    }

    [Fact]
    public async Task Enharmonic_MasteredEb4_OmitsMidiSharedWithDSharp()
    {
        await SeedMasteredAsync("Eb4");
        var excluded = await _session.GetMasteredMidiNumbersAsync();
        int eb4 = NoteSessionService.NoteNameToMidi("Eb4");
        int ds4 = NoteSessionService.NoteNameToMidi("D#4");
        Assert.Equal(eb4, ds4);
        Assert.Contains(eb4, excluded);

        var pitches = FlattenPitches(CreateRandomGenerator(seed: 5, excluded, high: "C5"));
        Assert.DoesNotContain(eb4, pitches);
    }

    [Fact]
    public async Task OmissionOff_AllowsMasteredNotes()
    {
        await SeedMasteredAsync("D4", "E4");
        _session.UseNoteMasteryForGeneration = false;
        var excluded = await _session.GetMasteredMidiNumbersAsync();
        Assert.Empty(excluded);

        var pitches = FlattenPitches(CreateRandomGenerator(seed: 3, excluded));
        Assert.Contains(NoteSessionService.NoteNameToMidi("D4"), pitches);
        Assert.Contains(NoteSessionService.NoteNameToMidi("E4"), pitches);
    }

    [Fact]
    public void SmallUnmasteredPool_ReintroducesMinimumMasteredForDistinctPitches()
    {
        // C4–E4 major: C D E. Exclude D and E → only C remains; restore closest (D) for variety.
        int c4 = NoteSessionService.NoteNameToMidi("C4");
        int d4 = NoteSessionService.NoteNameToMidi("D4");
        int e4 = NoteSessionService.NoteNameToMidi("E4");
        var full = new List<int> { c4, d4, e4 };
        var excluded = new HashSet<int> { d4, e4 };

        var result = MasteredNoteOmission.Apply(full, excluded, minDistinctPitches: 2);
        Assert.Equal(
            MasteredNoteOmission.FallbackKind.RelaxedOmissionForDistinctPitches,
            result.Fallback);
        Assert.Contains(c4, result.Pool);
        Assert.Contains(d4, result.Pool);
        Assert.DoesNotContain(e4, result.Pool);
        Assert.Equal(new[] { d4 }, result.RestoredMidis.ToArray());

        var gen = CreateRandomGenerator(seed: 99, excluded, low: "C4", high: "E4");
        gen.ChildLevel = 10;
        gen.MinDistinctPitches = 2;
        var pitches = FlattenPitches(gen);
        Assert.True(MelodicVarietyRules.CountDistinctPitches(pitches) >= 2);
        Assert.DoesNotContain(e4, pitches);
    }

    [Fact]
    public void AllEligibleMastered_AllowsMasteredWithExplicitFallback()
    {
        int c4 = NoteSessionService.NoteNameToMidi("C4");
        int d4 = NoteSessionService.NoteNameToMidi("D4");
        var full = new List<int> { c4, d4 };
        var excluded = new HashSet<int> { c4, d4 };

        var result = MasteredNoteOmission.Apply(full, excluded);
        Assert.Equal(
            MasteredNoteOmission.FallbackKind.AllowedMasteredAllEligibleMastered,
            result.Fallback);
        Assert.Equal(full, result.Pool);
        Assert.Contains("All eligible", result.Reason);
    }

    [Fact]
    public void ScaleOrder_WithEmptyExclusions_KeepsRequiredDegrees()
    {
        var gen = new MusicSequenceGenerator
        {
            Key = "C",
            Scale = "Major",
            LowestNote = "C4",
            HighestNote = "C5",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 4,
            UseScaleOrder = true,
            RestChancePercent = 0,
            ExcludedMidiNumbers = new HashSet<int>(),
            RandomSeed = 1,
            ActivityType = "Scale",
        };

        var pitches = FlattenPitches(gen);
        Assert.Contains(NoteSessionService.NoteNameToMidi("D4"), pitches);
        Assert.Contains(NoteSessionService.NoteNameToMidi("E4"), pitches);
    }

    [Fact]
    public void Apply_KeepsEmphasizedEvenWhenMastered()
    {
        int d4 = NoteSessionService.NoteNameToMidi("D4");
        int e4 = NoteSessionService.NoteNameToMidi("E4");
        var full = new List<int> { d4, e4 };
        var result = MasteredNoteOmission.Apply(full, new HashSet<int> { d4 }, emphasizedMidi: d4);
        Assert.Contains(d4, result.Pool);
    }

    [Fact]
    public async Task FilterNotesExcludingMastered_UsesMidiIdentity()
    {
        await SeedMasteredAsync("Eb4");
        var notes = new List<NoteInfo>
        {
            new() { Name = "D#4", Midi = NoteSessionService.NoteNameToMidi("D#4") },
            new() { Name = "F4", Midi = NoteSessionService.NoteNameToMidi("F4") },
        };

        var filtered = await PracticeSessionLifecycle.FilterNotesExcludingMasteredAsync(
            notes, _session, _noteDatabase);
        Assert.DoesNotContain(filtered, n => n.Midi == NoteSessionService.NoteNameToMidi("Eb4"));
        Assert.Contains(filtered, n => n.Name == "F4");
    }

    private async Task SeedMasteredAsync(params string[] names)
    {
        await _noteDatabase.InitializeAsync();
        foreach (var name in names)
        {
            await _noteDatabase.InsertOrReplaceAsync(new NoteStat
            {
                WrittenName = name,
                Streak = 5,
                Correct = 10,
                Wrong = 0,
                OverallCorrectCount = 10,
                OverallWrongCount = 0,
                PitchCorrectCount = 10,
                PitchWrongCount = 0,
                Mastered = 1,
            });
        }
    }

    private static MusicSequenceGenerator CreateRandomGenerator(
        int seed,
        HashSet<int> excluded,
        string low = "C4",
        string high = "G4")
        => new()
        {
            Key = "C",
            Scale = "Major",
            LowestNote = low,
            HighestNote = high,
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 8,
            RhythmVarietyPercent = 0,
            SmallestDuration = NoteDuration.Quarter,
            UseScaleOrder = false,
            AccidentalPercent = 0,
            RestChancePercent = 0,
            MaxMelodicIntervalSemitones = 0,
            ExcludedMidiNumbers = excluded,
            RandomSeed = seed,
            ActivityType = "Random",
        };

    private static List<int> FlattenPitches(MusicSequenceGenerator gen)
        => MusicSequenceGenerator.Flatten(gen.GenerateSequence())
            .Where(n => !n.IsRest)
            .Select(n => n.MidiNumber)
            .ToList();
}
