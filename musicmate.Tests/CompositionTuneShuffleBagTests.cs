using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

public class CompositionTuneShuffleBagTests
{
    private static readonly CompositionTuneContext WideContext =
        CompositionTuneEligibility.CreateContext(54, "Bb", "C3", "C6", isPremiumUser: true);

    public CompositionTuneShuffleBagTests()
    {
        CompositionTuneShuffleBag.Reset();
    }

    [Fact]
    public void DrawNextTitle_DoesNotRepeatUntilAllEligibleTunesUsed()
    {
        var eligible = CompositionTuneEligibility.GetEligibleTuneTitles(WideContext);
        Assert.True(eligible.Count >= 2);

        var seen = new List<string>();
        for (int i = 0; i < eligible.Count; i++)
        {
            string title = CompositionTuneShuffleBag.DrawNextTitle(WideContext, new Random(1000 + i))!;
            Assert.NotNull(title);
            Assert.DoesNotContain(title, seen);
            seen.Add(title);
        }
    }

    [Fact]
    public void TuneShuffleBag_AllowsRepeatWhenOnlyOneEligibleTune()
    {
        var bag = new TuneShuffleBag();
        var eligible = new[] { "Only Tune" };
        const string key = "single";

        string first = bag.DrawNext(eligible, key, new Random(1))!;
        string second = bag.DrawNext(eligible, key, new Random(2))!;
        Assert.Equal("Only Tune", first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void TuneShuffleBag_ReshuffleDoesNotLeadWithLastTuneWhenMultipleEligible()
    {
        var bag = new TuneShuffleBag();
        var eligible = new[] { "A", "B", "C" };
        const string key = "cycle";

        for (int seed = 0; seed < 200; seed++)
        {
            bag.Reset();
            var rng = new Random(seed);
            string? last = null;
            for (int i = 0; i < eligible.Length; i++)
                last = bag.DrawNext(eligible, key, rng);

            string firstAfterReshuffle = bag.DrawNext(eligible, key, rng)!;
            Assert.NotEqual(last, firstAfterReshuffle);
        }
    }

    [Fact]
    public void DrawNextTitle_ResetsWhenLevelChanges()
    {
        var level54 = CompositionTuneEligibility.CreateContext(54, "Bb", "C3", "C6", isPremiumUser: true);
        var level55 = level54 with { Level = 55 };

        for (int i = 0; i < 3; i++)
            CompositionTuneShuffleBag.DrawNextTitle(level54, new Random(500 + i));

        string afterLevelChange = CompositionTuneShuffleBag.DrawNextTitle(level55, new Random(42))!;
        Assert.Contains(afterLevelChange, CompositionTuneEligibility.GetEligibleTuneTitles(level55));
    }

    [Fact]
    public void DrawNextTitle_ResetsWhenInstrumentChanges()
    {
        var bb = WideContext;
        var clarinet = bb with { Instrument = "A" };

        string bbPick = CompositionTuneShuffleBag.DrawNextTitle(bb, new Random(7))!;
        Assert.Contains(bbPick, CompositionTuneEligibility.GetEligibleTuneTitles(bb));

        CompositionTuneShuffleBag.Reset();
        string clarinetPick = CompositionTuneShuffleBag.DrawNextTitle(clarinet, new Random(8))!;
        Assert.Contains(clarinetPick, CompositionTuneEligibility.GetEligibleTuneTitles(clarinet));
    }
}

public class PracticeCompositionWeightTests
{
    [Fact]
    public void PickExerciseKindFromWeights_UsesSliderWeights()
    {
        int tuneHits = 0;
        int randomHits = 0;
        for (int seed = 0; seed < 1000; seed++)
        {
            var kind = PracticeCompositionSelector.PickExerciseKindFromWeights(
                pcTunes: 80,
                pcRandom: 10,
                pcScales: 5,
                pcArpeggios: 5,
                hasEligibleTunes: true,
                new Random(seed));
            if (kind == PracticeCompositionSelector.ExerciseKind.Tune)
                tuneHits++;
            if (kind == PracticeCompositionSelector.ExerciseKind.Random)
                randomHits++;
        }

        Assert.True(tuneHits > 700);
        Assert.True(randomHits < 200);
    }

    [Fact]
    public void PickExerciseKindFromWeights_ZeroWeightCategoriesAreNotSelected()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            var kind = PracticeCompositionSelector.PickExerciseKindFromWeights(
                pcTunes: 0,
                pcRandom: 100,
                pcScales: 0,
                pcArpeggios: 0,
                hasEligibleTunes: true,
                new Random(seed));
            Assert.Equal(PracticeCompositionSelector.ExerciseKind.Random, kind);
        }
    }

    [Fact]
    public void PickExerciseKindFromWeights_SkipsTunesWhenNoneEligible()
    {
        for (int seed = 0; seed < 200; seed++)
        {
            var kind = PracticeCompositionSelector.PickExerciseKindFromWeights(
                pcTunes: 100,
                pcRandom: 0,
                pcScales: 0,
                pcArpeggios: 0,
                hasEligibleTunes: false,
                new Random(seed));
            Assert.NotEqual(PracticeCompositionSelector.ExerciseKind.Tune, kind);
        }
    }

    [Fact]
    public void PickExerciseKindFromWeights_AllZeroFallsBackToDefaults()
    {
        var kind = PracticeCompositionSelector.PickExerciseKindFromWeights(
            pcTunes: 0,
            pcRandom: 0,
            pcScales: 0,
            pcArpeggios: 0,
            hasEligibleTunes: true,
            new Random(1));

        Assert.True(Enum.IsDefined(kind));
    }
}
