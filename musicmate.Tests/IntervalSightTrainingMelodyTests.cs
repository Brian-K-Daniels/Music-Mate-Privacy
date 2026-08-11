using musicmate.Services;

namespace musicmate.Tests;

public class IntervalSightTrainingMelodyTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(10, 2)]
    [InlineData(20, 3)]
    [InlineData(40, 4)]
    [InlineData(60, 6)]
    [InlineData(80, 8)]
    [InlineData(90, 10)]
    [InlineData(95, 12)]
    [InlineData(100, 12)]
    public void MaxSightIntervalForLevel_OpensTowardOctave(int level, int expectedMax)
    {
        Assert.Equal(expectedMax, IntervalSightTrainingMelody.MaxSightIntervalForLevel(level));
    }

    [Fact]
    public void MaxSightIntervalForLevel_DoesNotChangeMusicCurriculumCap()
    {
        // Music progression stays capped at 8 at top levels; Sight alone opens to 12.
        Assert.Equal(8, ChildLevelProgression.MaxIntervalForLevel(100));
        Assert.Equal(12, IntervalSightTrainingMelody.MaxSightIntervalForLevel(100));
    }

    [Fact]
    public void CreateIntervalPool_IsMagnitudesOnly()
    {
        var pool = IntervalSightTrainingMelody.CreateIntervalPool(2);
        Assert.Equal(new[] { 0, 1, 2 }, pool);
    }

    [Fact]
    public void PickIntervals_AllMagnitudesUnique()
    {
        var rng = new Random(42);
        int maxAbs = 3;
        int poolSize = IntervalSightTrainingMelody.CreateIntervalPool(maxAbs).Count;
        var deltas = IntervalSightTrainingMelody.PickIntervalsWithoutRepetition(
            startMidi: 64,
            lowMidi: 60,
            highMidi: 72,
            stepCount: poolSize,
            maxAbsSemitones: maxAbs,
            rng);

        Assert.Equal(poolSize, deltas.Count);
        Assert.Equal(poolSize, deltas.Select(Math.Abs).Distinct().Count());
        Assert.True(IntervalSightTrainingMelody.HasNoRepeatsUntilPoolExhausted(deltas, maxAbs));
    }

    [Fact]
    public void PickIntervals_ExcludesFirstMagnitudeWhenRequested()
    {
        var rng = new Random(9);
        var deltas = IntervalSightTrainingMelody.PickIntervalsWithoutRepetition(
            startMidi: 66,
            lowMidi: 60,
            highMidi: 72,
            stepCount: 3,
            maxAbsSemitones: 3,
            rng,
            excludeFirstMagnitude: 2);

        Assert.NotEmpty(deltas);
        Assert.NotEqual(2, Math.Abs(deltas[0]));
    }

    [Fact]
    public void BuildMidiChain_StaysConsistentWithDeltas()
    {
        var deltas = new List<int> { 2, -1, 0, 4 };
        var midis = IntervalSightTrainingMelody.BuildMidiChain(60, deltas);
        Assert.Equal(new[] { 60, 62, 61, 61, 65 }, midis);
    }

    [Fact]
    public void BuildTrainingMidiChain_CoversWideMagnitudesInWideRange()
    {
        var rng = new Random(7);
        var midis = IntervalSightTrainingMelody.BuildTrainingMidiChain(
            lowMidi: 40,
            highMidi: 84,
            stepCount: 13,
            maxAbsSemitones: 12,
            rng);

        Assert.Equal(14, midis.Count);
        var mags = new List<int>();
        for (int i = 1; i < midis.Count; i++)
            mags.Add(Math.Abs(midis[i] - midis[i - 1]));

        Assert.Equal(13, mags.Distinct().Count());
        Assert.Contains(mags, m => m >= 9);
        Assert.All(mags, m => Assert.InRange(m, 0, 12));
    }

    [Fact]
    public void Generate_ProducesUniqueMagnitudes_NoRests_AndLevelKey()
    {
        var session = new NoteSessionService
        {
            Key = "C",
            SelectedScale = "Major",
            LowestNote = "C4",
            HighestNote = "C5",
            MaxMelodicIntervalSemitones = 4,
            ChildLevel = 20,
            AccidentalPercent = 15,
            PracticeRestChancePercent = 40,
        };

        int maxAbs = IntervalSightTrainingMelody.MaxSightIntervalForLevel(20);
        var exercise = IntervalSightTrainingSequenceBuilder.Generate(
            session, measureCount: null, randomSeed: 123, excludeFirstMagnitude: null);

        Assert.NotEmpty(exercise.Notes);
        Assert.DoesNotContain(exercise.Notes, n => n.IsRest);
        Assert.False(string.IsNullOrWhiteSpace(exercise.Key));
        Assert.False(string.IsNullOrWhiteSpace(exercise.Scale));
        Assert.Equal(maxAbs, exercise.MaxAbsoluteSemitones);

        Assert.True(IntervalSightTrainingMelody.TryValidateUniqueMagnitudes(
            exercise.Notes, maxAbs, excludeFirstMagnitude: null, out var mags));
        Assert.NotEmpty(mags);
        Assert.All(mags, m => Assert.InRange(m, 0, maxAbs));
    }

    [Fact]
    public void Generate_UsesChildLevelRange_NotMusicPagePrefs()
    {
        // Narrow Music prefs must not shrink Sight Training range.
        var session = new NoteSessionService
        {
            ChildLevel = 100,
            LowestNote = "C4",
            HighestNote = "C5",
            AccidentalPercent = 20,
            Key = "C",
            SelectedScale = "Major",
        };

        var exercise = IntervalSightTrainingSequenceBuilder.Generate(
            session, measureCount: null, randomSeed: 404, excludeFirstMagnitude: null);

        int low = exercise.Notes.Min(n => n.MidiNumber);
        int high = exercise.Notes.Max(n => n.MidiNumber);
        // Level 100 Child range is E2–C8; a C4–C5-only tune cannot span much — expect wider use.
        Assert.True(high - low >= 12,
            $"Expected wider-than-octave span from Child Level range, got {low}-{high}");
        Assert.Equal(12, exercise.MaxAbsoluteSemitones);
    }

    [Fact]
    public void Generate_HighLevel_ExercisesLargeMagnitudes()
    {
        var session = new NoteSessionService
        {
            ChildLevel = 100,
            AccidentalPercent = 25,
            Key = "C",
            SelectedScale = "Major",
        };

        var seen = new HashSet<int>();
        for (int i = 0; i < 40; i++)
        {
            var exercise = IntervalSightTrainingSequenceBuilder.Generate(
                session, measureCount: null, randomSeed: 1000 + i, excludeFirstMagnitude: null);
            foreach (int m in IntervalSightTrainingMelody.CollectMagnitudes(exercise.Notes))
                seen.Add(m);
        }

        Assert.Contains(9, seen);
        Assert.Contains(10, seen);
        Assert.Contains(11, seen);
        Assert.Contains(12, seen);
        Assert.True(seen.Count >= 10, $"Expected broad magnitude coverage, got {{{string.Join(",", seen.OrderBy(x => x))}}}");
    }

    [Fact]
    public void Generate_ExcludesPreviousFinalMagnitudeOnFirstInterval()
    {
        var session = new NoteSessionService
        {
            Key = "C",
            SelectedScale = "Major",
            ChildLevel = 35,
            AccidentalPercent = 10,
        };

        int maxAbs = IntervalSightTrainingMelody.MaxSightIntervalForLevel(35);
        var exercise = IntervalSightTrainingSequenceBuilder.Generate(
            session, measureCount: null, randomSeed: 55, excludeFirstMagnitude: 3);

        Assert.True(IntervalSightTrainingMelody.TryValidateUniqueMagnitudes(
            exercise.Notes, maxAbs, excludeFirstMagnitude: 3, out var mags));
        Assert.NotEqual(3, mags[0]);
    }

    [Fact]
    public void Generate_ChildLevelOverride_DoesNotRequireSessionChildLevel()
    {
        var session = new NoteSessionService
        {
            ChildLevel = 1,
            AccidentalPercent = 0,
            Key = "C",
            SelectedScale = "Major",
        };

        var exercise = IntervalSightTrainingSequenceBuilder.Generate(
            session,
            measureCount: null,
            randomSeed: 77,
            excludeFirstMagnitude: null,
            childLevelOverride: 100);

        Assert.Equal(12, exercise.MaxAbsoluteSemitones);
        Assert.True(IntervalSightTrainingMelody.TryValidateUniqueMagnitudes(
            exercise.Notes, 12, null, out var mags));
        Assert.Contains(mags, m => m >= 8);
    }
}
