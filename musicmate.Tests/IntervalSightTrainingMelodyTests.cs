using musicmate.Models;
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
        Assert.Contains(2, deltas.Select(Math.Abs));
    }

    [Fact]
    public void CreateShuffledCycle_IsPermutationOfPool_AndAvoidsFirstWhenPossible()
    {
        var rng = new Random(21);
        int maxAbs = 4;
        var cycle = IntervalSightTrainingMelody.CreateShuffledCycle(maxAbs, rng, avoidFirst: 0);
        Assert.True(IntervalSightTrainingMelody.IsCompleteCycle(cycle, maxAbs));
        Assert.NotEqual(0, cycle[0]);
        Assert.Contains(0, cycle);
    }

    [Fact]
    public void PickIntervals_DoesNotSkipShuffledMagnitude_WhenDirectionOrNewStartCanRealizeIt()
    {
        // Magnitude 12 cannot be realized from MIDI 60 in 60–66; the picker must
        // choose a new start rather than skip 12 and play 1 instead.
        var rng = new Random(3);
        var deltas = IntervalSightTrainingMelody.PickIntervalsWithoutRepetition(
            startMidi: 60,
            lowMidi: 48,
            highMidi: 72,
            stepCount: 13,
            maxAbsSemitones: 12,
            rng);

        Assert.Equal(13, deltas.Count);
        Assert.True(IntervalSightTrainingMelody.IsCompleteCycle(
            deltas.Select(Math.Abs).ToList(), 12));
    }

    [Fact]
    public void PickIntervals_TwoCycles_EachIsFullPermutation_AndNewCycleStartsIndependently()
    {
        var rng = new Random(101);
        int maxAbs = 5;
        int pool = maxAbs + 1;
        var deltas = IntervalSightTrainingMelody.PickIntervalsWithoutRepetition(
            startMidi: 64,
            lowMidi: 48,
            highMidi: 84,
            stepCount: pool * 2,
            maxAbsSemitones: maxAbs,
            rng);

        var mags = deltas.Select(Math.Abs).ToList();
        var cycles = IntervalSightTrainingMelody.SplitCompleteCycles(mags, maxAbs);
        Assert.Equal(2, cycles.Count);
        Assert.True(IntervalSightTrainingMelody.IsCompleteCycle(cycles[0], maxAbs));
        Assert.True(IntervalSightTrainingMelody.IsCompleteCycle(cycles[1], maxAbs));
        Assert.NotEqual(cycles[0][^1], cycles[1][0]);
        Assert.True(IntervalSightTrainingMelody.HasNoRepeatsUntilPoolExhausted(deltas, maxAbs));
        // Independent reshuffle: not required to differ, but first-of-cycle-2 must be a legal pool value.
        Assert.InRange(cycles[1][0], 0, maxAbs);
    }

    [Fact]
    public void BuildCycledMagnitudeSequence_BeginsNewShuffleAfterPoolExhausted()
    {
        var rng = new Random(8);
        int maxAbs = 3;
        int pool = maxAbs + 1;
        var seq = IntervalSightTrainingMelody.BuildCycledMagnitudeSequence(
            pool * 2, maxAbs, rng, excludeFirstMagnitude: null);

        var cycles = IntervalSightTrainingMelody.SplitCompleteCycles(seq, maxAbs);
        Assert.Equal(2, cycles.Count);
        Assert.Equal(pool, cycles[0].Distinct().Count());
        Assert.Equal(pool, cycles[1].Distinct().Count());
        Assert.NotEqual(cycles[0][^1], cycles[1][0]);
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

        Assert.True(IntervalSightTrainingMelody.TryValidateCompleteCycle(
            exercise.Notes, maxAbs, excludeFirstMagnitude: null, out var mags));
        Assert.Equal(maxAbs + 1, mags.Count);
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

        Assert.True(IntervalSightTrainingMelody.TryValidateCompleteCycle(
            exercise.Notes, maxAbs, excludeFirstMagnitude: 3, out var mags));
        Assert.NotEqual(3, mags[0]);
        Assert.Contains(3, mags);
    }

    [Fact]
    public void Generate_SuccessiveExercises_AreCompleteCycles_AndAvoidRepeatingBoundaryMagnitude()
    {
        var session = new NoteSessionService
        {
            ChildLevel = 40,
            AccidentalPercent = 10,
            Key = "C",
            SelectedScale = "Major",
        };

        int maxAbs = IntervalSightTrainingMelody.MaxSightIntervalForLevel(40);
        var first = IntervalSightTrainingSequenceBuilder.Generate(
            session, measureCount: null, randomSeed: 202, excludeFirstMagnitude: null);
        Assert.True(IntervalSightTrainingMelody.TryValidateCompleteCycle(
            first.Notes, maxAbs, null, out var cycle1));

        var second = IntervalSightTrainingSequenceBuilder.Generate(
            session, measureCount: null, randomSeed: 303,
            excludeFirstMagnitude: first.FinalIntervalMagnitude);
        Assert.True(IntervalSightTrainingMelody.TryValidateCompleteCycle(
            second.Notes, maxAbs, first.FinalIntervalMagnitude, out var cycle2));

        Assert.Equal(cycle1[^1], first.FinalIntervalMagnitude);
        Assert.NotEqual(first.FinalIntervalMagnitude, cycle2[0]);
        Assert.Equal(maxAbs + 1, cycle1.Count);
        Assert.Equal(maxAbs + 1, cycle2.Count);

        var logic = new IntervalSightTrainingLogic(first.Notes, first.Key, first.Scale);
        var tested = new List<int>();
        while (logic.HasCurrentPair)
        {
            int mag = logic.CurrentPair!.Value.AbsoluteSemitones;
            tested.Add(mag);
            Assert.True(logic.SubmitAnswer(mag));
        }

        Assert.True(logic.IsSessionComplete);
        Assert.True(IntervalSightTrainingMelody.IsCompleteCycle(tested, maxAbs));
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
        Assert.True(IntervalSightTrainingMelody.TryValidateCompleteCycle(
            exercise.Notes, 12, null, out var mags));
        Assert.Contains(mags, m => m >= 8);
    }

    [Fact]
    public void Generate_SpellingMatchesMidi_AndQuizUsesWrittenPitch()
    {
        var session = new NoteSessionService
        {
            ChildLevel = 26,
            AccidentalPercent = 40,
            Key = "C",
            SelectedScale = "Major",
        };

        for (int seed = 0; seed < 80; seed++)
        {
            var exercise = IntervalSightTrainingSequenceBuilder.Generate(
                session,
                measureCount: null,
                randomSeed: 2600 + seed,
                excludeFirstMagnitude: null,
                childLevelOverride: 26);

            Assert.True(exercise.Notes.Count >= 2, $"seed={seed}");
            foreach (var n in exercise.Notes.Where(n => !n.IsRest))
            {
                int written = IntervalSightTrainingLogic.SightSoundingMidi(
                    n, exercise.Key, exercise.Scale);
                Assert.True(
                    written == n.MidiNumber,
                    $"seed={seed} {exercise.Key} {exercise.Scale}: MIDI {n.MidiNumber} " +
                    $"spelled {n.Letter}{n.Accidental}{n.Octave} ({n.SpelledName}) sounds {written}");
            }

            var logic = new IntervalSightTrainingLogic(exercise.Notes, exercise.Key, exercise.Scale);
            var midiMags = IntervalSightTrainingMelody.CollectMagnitudes(exercise.Notes);
            var quizMags = new List<int>();
            while (logic.HasCurrentPair)
            {
                int mag = logic.CurrentPair!.Value.AbsoluteSemitones;
                quizMags.Add(mag);
                Assert.True(logic.SubmitAnswer(mag), $"seed={seed} mag={mag}");
            }

            Assert.Equal(midiMags, quizMags);
        }
    }

    [Fact]
    public void ApplyTrainingIntervals_DoesNotSpellDAsCSharp()
    {
        var notes = Enumerable.Range(0, 6)
            .Select(_ => new GeneratedNote
            {
                MidiNumber = 60,
                Letter = 'C',
                Octave = 4,
                Accidental = Accidental.None,
                SpelledName = "C4",
                Duration = NoteDuration.Quarter,
                IsRest = false,
                MeasureIndex = 0,
                BeatPosition = 0,
            })
            .ToList();

        IntervalSightTrainingSequenceBuilder.ApplyTrainingIntervals(
            notes,
            key: "G",
            scale: "Major",
            lowestNote: "C4",
            highestNote: "C5",
            maxAbsSemitones: 4,
            accidentalPercent: 50,
            randomSeed: 42);

        foreach (var n in notes.Where(n => !n.IsRest))
        {
            int written = IntervalSightTrainingLogic.SightSoundingMidi(n, "G", "Major");
            Assert.Equal(n.MidiNumber, written);
            if (n.MidiNumber % 12 == 2)
            {
                Assert.Equal('D', n.Letter);
                Assert.NotEqual(Accidental.Sharp, n.Accidental);
            }
        }
    }
}
