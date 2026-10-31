using musicmate.Services;

namespace musicmate.Tests;

public class CompositionLevelWeightTests
{
    [Theory]
    [InlineData(1, 0.0)]
    [InlineData(10, 0.0)]
    [InlineData(11, 0.25)]
    [InlineData(15, 0.25)]
    [InlineData(25, 0.25)]
    [InlineData(26, 0.60)]
    [InlineData(50, 0.60)]
    [InlineData(51, 1.0)]
    [InlineData(54, 1.0)]
    [InlineData(100, 1.0)]
    public void GetTuneWeightFactor_MatchesLevelBands(int level, double expected)
    {
        Assert.Equal(expected, CompositionLevelWeights.GetTuneWeightFactor(level));
    }

    [Fact]
    public void ComputeEffectiveWeights_Level1ByLevel_ZeroTunes()
    {
        var weights = CompositionLevelWeights.ComputeEffectiveWeights(
            pcTunes: 100,
            pcRandom: 0,
            pcScales: 0,
            pcArpeggios: 0,
            level: 1,
            applyByLevelTuneRestriction: true,
            hasEligibleTunes: true,
            hasEligibleArpeggios: false);

        Assert.Equal(0, weights.Tunes);
        Assert.Equal(100, weights.Random + weights.Scales + weights.Arpeggios);
    }

    [Fact]
    public void ComputeEffectiveWeights_Level15ByLevel_ReducesTuneShare()
    {
        var level15 = CompositionLevelWeights.ComputeEffectiveWeights(
            80, 10, 5, 5,
            level: 15,
            applyByLevelTuneRestriction: true,
            hasEligibleTunes: true,
            hasEligibleArpeggios: true);

        var level54 = CompositionLevelWeights.ComputeEffectiveWeights(
            80, 10, 5, 5,
            level: 54,
            applyByLevelTuneRestriction: true,
            hasEligibleTunes: true,
            hasEligibleArpeggios: true);

        Assert.True(level15.Tunes < level54.Tunes);
        Assert.True(level15.Tunes > 0);
        Assert.Equal(80, level54.Tunes);
    }

    [Fact]
    public void ComputeEffectiveWeights_Level54ByLevel_UsesFullTuneSlider()
    {
        var weights = CompositionLevelWeights.ComputeEffectiveWeights(
            pcTunes: 20,
            pcRandom: 60,
            pcScales: 20,
            pcArpeggios: 0,
            level: 54,
            applyByLevelTuneRestriction: true,
            hasEligibleTunes: true,
            hasEligibleArpeggios: false);

        Assert.Equal(20, weights.Tunes);
        Assert.Equal(60, weights.Random);
        Assert.Equal(20, weights.Scales);
        Assert.Equal(0, weights.Arpeggios);
    }

    [Fact]
    public void ComputeEffectiveWeights_NormalizesToOneHundred()
    {
        var weights = CompositionLevelWeights.ComputeEffectiveWeights(
            pcTunes: 20,
            pcRandom: 60,
            pcScales: 15,
            pcArpeggios: 5,
            level: 15,
            applyByLevelTuneRestriction: true,
            hasEligibleTunes: true,
            hasEligibleArpeggios: true);

        Assert.Equal(100, weights.Tunes + weights.Random + weights.Scales + weights.Arpeggios);
    }

    [Fact]
    public void PickExerciseKindFromWeights_Level1ByLevelEffectiveWeights_NeverChoosesTunes()
    {
        var weights = CompositionLevelWeights.ComputeEffectiveWeights(
            pcTunes: 100,
            pcRandom: 0,
            pcScales: 0,
            pcArpeggios: 0,
            level: 1,
            applyByLevelTuneRestriction: true,
            hasEligibleTunes: true,
            hasEligibleArpeggios: false);

        for (int seed = 0; seed < 500; seed++)
        {
            var kind = PracticeCompositionSelector.PickExerciseKindFromWeights(
                weights.Tunes,
                weights.Random,
                weights.Scales,
                weights.Arpeggios,
                hasEligibleTunes: true,
                new Random(seed));

            Assert.NotEqual(PracticeCompositionSelector.ExerciseKind.Tune, kind);
        }
    }

    [Fact]
    public void PickExerciseKindFromWeights_Level15ByLevel_CanChooseTunesAtReducedRate()
    {
        var level15 = CompositionLevelWeights.ComputeEffectiveWeights(
            80, 10, 5, 5, 15, true, true, true);
        var level54 = CompositionLevelWeights.ComputeEffectiveWeights(
            80, 10, 5, 5, 54, true, true, true);

        int hits15 = 0;
        int hits54 = 0;
        for (int seed = 0; seed < 2000; seed++)
        {
            if (PracticeCompositionSelector.PickExerciseKindFromWeights(
                    level15.Tunes, level15.Random, level15.Scales, level15.Arpeggios,
                    true, new Random(seed))
                == PracticeCompositionSelector.ExerciseKind.Tune)
                hits15++;

            if (PracticeCompositionSelector.PickExerciseKindFromWeights(
                    level54.Tunes, level54.Random, level54.Scales, level54.Arpeggios,
                    true, new Random(seed + 10_000))
                == PracticeCompositionSelector.ExerciseKind.Tune)
                hits54++;
        }

        Assert.True(hits15 > 0);
        Assert.True(hits54 > hits15);
    }

    [Fact]
    public void PickExerciseKindFromWeights_ZeroWeightCategoriesAreNotSelected()
    {
        var weights = CompositionLevelWeights.ComputeEffectiveWeights(
            pcTunes: 0,
            pcRandom: 100,
            pcScales: 0,
            pcArpeggios: 0,
            level: 54,
            applyByLevelTuneRestriction: true,
            hasEligibleTunes: true,
            hasEligibleArpeggios: false);

        for (int seed = 0; seed < 200; seed++)
        {
            var kind = PracticeCompositionSelector.PickExerciseKindFromWeights(
                weights.Tunes,
                weights.Random,
                weights.Scales,
                weights.Arpeggios,
                hasEligibleTunes: true,
                new Random(seed));

            Assert.Equal(PracticeCompositionSelector.ExerciseKind.Random, kind);
        }
    }

    [Fact]
    public void ComputeEffectiveWeights_SkipsByLevelRestrictionWhenNotByLevelMode()
    {
        var withoutRestriction = CompositionLevelWeights.ComputeEffectiveWeights(
            pcTunes: 20,
            pcRandom: 60,
            pcScales: 20,
            pcArpeggios: 0,
            level: 1,
            applyByLevelTuneRestriction: false,
            hasEligibleTunes: true,
            hasEligibleArpeggios: false);

        Assert.Equal(20, withoutRestriction.Tunes);
    }
}
