using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

public class MelodicVarietyRulesTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(20, 2)]
    [InlineData(21, 3)]
    [InlineData(100, 3)]
    public void MinimumDistinctPitches_ByLevel(int level, int expected)
        => Assert.Equal(expected, MelodicVarietyRules.GetMinimumDistinctPitchesForLevel(level));

    [Fact]
    public void HasExcessiveConsecutiveIdentical_DetectsFourInARow()
    {
        Assert.False(MelodicVarietyRules.HasExcessiveConsecutiveIdentical([60, 60, 60]));
        Assert.True(MelodicVarietyRules.HasExcessiveConsecutiveIdentical([60, 60, 60, 60]));
        Assert.False(MelodicVarietyRules.HasExcessiveConsecutiveIdentical([60, 60, 60, 62, 62, 62]));
    }

    [Fact]
    public void DistinctSoundedPitches_IgnoresRests_AndRequiresTwoPitches()
    {
        var a4 = new GeneratedNote { MidiNumber = 69, SpelledName = "A4" };
        var a4Again = new GeneratedNote { MidiNumber = 69, SpelledName = "A4" };
        var c5 = new GeneratedNote { MidiNumber = 72, SpelledName = "C5" };
        var rest = new GeneratedNote { IsRest = true, MidiNumber = 0 };

        Assert.Equal(1, MelodicVarietyRules.CountDistinctSoundedPitches(
            new[] { a4, rest, a4Again }));
        Assert.False(MelodicVarietyRules.HasAtLeastTwoDistinctSoundedPitches(
            new[] { a4, rest, a4Again }, null));
        Assert.True(MelodicVarietyRules.HasAtLeastTwoDistinctSoundedPitches(
            new[] { a4, rest }, new[] { c5 }));
        Assert.False(MelodicVarietyRules.HasAtLeastTwoDistinctSoundedPitches(
            new[] { rest }, new[] { rest }));
    }

    [Fact]
    public void PickClosestMastered_PrefersNearestToUnmastered()
    {
        int c4 = NoteSessionService.NoteNameToMidi("C4");
        int d4 = NoteSessionService.NoteNameToMidi("D4");
        int g4 = NoteSessionService.NoteNameToMidi("G4");
        int pick = MelodicVarietyRules.PickClosestMasteredToUnmastered(
            masteredCandidates: [g4, d4],
            unmasteredPitches: [c4]);
        Assert.Equal(d4, pick);
    }
}

[Collection("SessionPreferences")]
public class MelodicVarietyGenerationTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public MelodicVarietyGenerationTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Fact]
    public void OnlyOneUnmasteredEligible_ReintroducesClosestMastered()
    {
        int c4 = NoteSessionService.NoteNameToMidi("C4");
        int d4 = NoteSessionService.NoteNameToMidi("D4");
        int e4 = NoteSessionService.NoteNameToMidi("E4");
        var full = new List<int> { c4, d4, e4 };
        var excluded = new HashSet<int> { d4, e4 };

        var result = MasteredNoteOmission.Apply(full, excluded, minDistinctPitches: 2);

        Assert.Equal(
            MasteredNoteOmission.FallbackKind.RelaxedOmissionForDistinctPitches,
            result.Fallback);
        Assert.Single(result.RestoredMidis);
        Assert.Equal(d4, result.RestoredMidis[0]); // closest to remaining C4
        Assert.Contains(c4, result.Pool);
        Assert.Contains(d4, result.Pool);
        Assert.DoesNotContain(e4, result.Pool); // only minimum restored
        Assert.True(result.Pool.Distinct().Count() >= 2);
    }

    [Fact]
    public void TwoUnmasteredEligible_DoesNotReintroduceAtBeginner()
    {
        int c4 = NoteSessionService.NoteNameToMidi("C4");
        int d4 = NoteSessionService.NoteNameToMidi("D4");
        int e4 = NoteSessionService.NoteNameToMidi("E4");
        var full = new List<int> { c4, d4, e4 };
        var excluded = new HashSet<int> { e4 };

        var result = MasteredNoteOmission.Apply(full, excluded, minDistinctPitches: 2);

        Assert.NotEqual(
            MasteredNoteOmission.FallbackKind.RelaxedOmissionForDistinctPitches,
            result.Fallback);
        Assert.Empty(result.RestoredMidis);
        Assert.Equal(new[] { c4, d4 }, result.Pool.OrderBy(m => m).ToArray());
    }

    [Fact]
    public void Level21_RequiresThreeDistinct_ReintroducesWhenOnlyTwoUnmastered()
    {
        int c4 = NoteSessionService.NoteNameToMidi("C4");
        int d4 = NoteSessionService.NoteNameToMidi("D4");
        int e4 = NoteSessionService.NoteNameToMidi("E4");
        int f4 = NoteSessionService.NoteNameToMidi("F4");
        var full = new List<int> { c4, d4, e4, f4 };
        var excluded = new HashSet<int> { e4, f4 };

        var result = MasteredNoteOmission.ApplyForLevel(full, excluded, childLevel: 21);

        Assert.Equal(
            MasteredNoteOmission.FallbackKind.RelaxedOmissionForDistinctPitches,
            result.Fallback);
        Assert.True(result.Pool.Distinct().Count() >= 3);
        Assert.NotEmpty(result.RestoredMidis);
    }

    [Fact]
    public void GeneratedTune_WithOneUnmastered_HasAtLeastTwoDistinctPitches()
    {
        int c4 = NoteSessionService.NoteNameToMidi("C4");
        int d4 = NoteSessionService.NoteNameToMidi("D4");
        int e4 = NoteSessionService.NoteNameToMidi("E4");
        var excluded = new HashSet<int> { d4, e4 };

        var gen = CreateRandomGenerator(seed: 42, excluded, childLevel: 10, low: "C4", high: "E4");
        var pitches = FlattenPitches(gen);

        Assert.True(pitches.Count >= 2);
        Assert.True(
            MelodicVarietyRules.CountDistinctPitches(pitches) >= 2,
            $"Expected ≥2 distinct, got [{string.Join(',', pitches)}]");
        Assert.Equal(
            MasteredNoteOmission.FallbackKind.RelaxedOmissionForDistinctPitches,
            gen.LastMasteryFallback);
        Assert.Contains(c4, pitches);
    }

    [Fact]
    public void Level21Generation_HasAtLeastThreeDistinctPitches()
    {
        int c4 = NoteSessionService.NoteNameToMidi("C4");
        int d4 = NoteSessionService.NoteNameToMidi("D4");
        int e4 = NoteSessionService.NoteNameToMidi("E4");
        int f4 = NoteSessionService.NoteNameToMidi("F4");
        int g4 = NoteSessionService.NoteNameToMidi("G4");
        // Leave only C and D unmastered — must restore one more for L21.
        var excluded = new HashSet<int> { e4, f4, g4 };

        for (int seed = 0; seed < 12; seed++)
        {
            var gen = CreateRandomGenerator(seed, excluded, childLevel: 21, low: "C4", high: "G4");
            var pitches = FlattenPitches(gen);
            Assert.True(
                MelodicVarietyRules.CountDistinctPitches(pitches) >= 3,
                $"seed={seed} distinct={MelodicVarietyRules.CountDistinctPitches(pitches)} pitches=[{string.Join(',', pitches)}]");
            Assert.False(MelodicVarietyRules.HasExcessiveConsecutiveIdentical(pitches));
            Assert.Equal(
                MasteredNoteOmission.FallbackKind.RelaxedOmissionForDistinctPitches,
                gen.LastMasteryFallback);
            Assert.NotEmpty(gen.LastTemporarilyRestoredMidis);
            Assert.Contains(c4, pitches.Concat(gen.LastTemporarilyRestoredMidis));
            Assert.Contains(d4, pitches.Concat(gen.LastTemporarilyRestoredMidis));
        }
    }

    [Fact]
    public void RepeatNew_MultipleSeeds_NeverAllSamePitchWhenPoolAllowsVariety()
    {
        int d4 = NoteSessionService.NoteNameToMidi("D4");
        int e4 = NoteSessionService.NoteNameToMidi("E4");
        var excluded = new HashSet<int> { d4, e4 }; // C,F,G remain in C4–G4 major

        for (int seed = 0; seed < 20; seed++)
        {
            var gen = CreateRandomGenerator(seed + 100, excluded, childLevel: 18, low: "C4", high: "G4");
            var pitches = FlattenPitches(gen);
            int distinct = MelodicVarietyRules.CountDistinctPitches(pitches);
            Assert.True(distinct >= 2, $"seed={seed} all-same-pitch tune");
            Assert.False(
                pitches.Count > 0 && pitches.All(m => m == pitches[0]),
                $"seed={seed} every note identical");
        }
    }

    [Fact]
    public void RelaxingOmission_DoesNotChangeMasteredStatusInDatabase()
    {
        // Pure omission path — no DB writes. Restored midis are only in the generation pool.
        int c4 = NoteSessionService.NoteNameToMidi("C4");
        int d4 = NoteSessionService.NoteNameToMidi("D4");
        var excluded = new HashSet<int> { d4 };
        var beforeExcluded = excluded.ToHashSet();

        var result = MasteredNoteOmission.Apply(
            new[] { c4, d4 }, excluded, minDistinctPitches: 2);

        Assert.Equal(beforeExcluded, excluded);
        Assert.Contains(d4, result.RestoredMidis);
        // Exclusion set unchanged — mastery omission not permanently disabled.
        Assert.Contains(d4, excluded);
    }

    [Fact]
    public void SinglePlayablePitchInRange_FailureSafe_DoesNotLoopForever()
    {
        int c4 = NoteSessionService.NoteNameToMidi("C4");
        // Range is only C4; full pool has one pitch even without omission.
        var gen = new MusicSequenceGenerator
        {
            Key = "C",
            Scale = "Major",
            LowestNote = "C4",
            HighestNote = "C4",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 4,
            UseScaleOrder = false,
            RestChancePercent = 0,
            ExcludedMidiNumbers = new HashSet<int>(),
            ChildLevel = 21,
            MinDistinctPitches = 3,
            RandomSeed = 7,
            ActivityType = "Random",
        };

        var pitches = FlattenPitches(gen);
        Assert.NotEmpty(pitches);
        Assert.All(pitches, m => Assert.Equal(c4, m));
        Assert.Equal(1, MelodicVarietyRules.CountDistinctPitches(pitches));
    }

    private static MusicSequenceGenerator CreateRandomGenerator(
        int seed,
        HashSet<int> excluded,
        int childLevel,
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
            UseMotifPhrases = false,
            ExcludedMidiNumbers = excluded,
            ChildLevel = childLevel,
            MinDistinctPitches = MelodicVarietyRules.GetMinimumDistinctPitchesForLevel(childLevel),
            RandomSeed = seed,
            ActivityType = "ByLevel-Random",
        };

    private static List<int> FlattenPitches(MusicSequenceGenerator gen)
        => MusicSequenceGenerator.Flatten(gen.GenerateSequence())
            .Where(n => !n.IsRest)
            .Select(n => n.MidiNumber)
            .ToList();
}
