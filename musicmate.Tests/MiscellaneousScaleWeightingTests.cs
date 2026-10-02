using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Assortment-by-level / Random (Miscellaneous) automatic scale weighting by child level.
/// </summary>
public class MiscellaneousScaleWeightingTests
{
    [Fact]
    public void Level1_RandomPool_IsMajorOnly()
    {
        var pool = ChildLevelProgression.GetProfile(1).ScalePool;
        Assert.Single(pool);
        Assert.Equal("Major", pool[0].Scale);
    }

    [Fact]
    public void Level1_RandomPicks_NeverChoosePentatonic()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            Assert.Equal("Major", ChildLevelProgression.PickWeightedRandomScale(1, new Random(seed)));
        }
    }

    [Fact]
    public void Level1_ManualPentatonicSelection_RemainsAllowed()
    {
        var allowed = ChildLevelProgression.GetAllowedScalesForLevel(1);
        Assert.Contains("Major Pentatonic", allowed);

        var session = new NoteSessionService { ChildLevel = 1 };
        Assert.True(session.TryApplyScalePickerSelection("Major Pentatonic", out _));
        Assert.Equal("Major Pentatonic", session.SelectedScale);
        Assert.Equal(ScaleSelectionMode.Named, session.ScaleSelectionMode);
    }

    [Fact]
    public void Level5_RandomPicks_FavorMajor()
    {
        int majorCount = CountScale(5, "Major", trials: 300);
        Assert.True(majorCount >= 240, $"Expected Major to dominate at L5, got {majorCount}/300");
    }

    [Fact]
    public void Level5_RandomPicks_CanStillChoosePentatonic()
    {
        Assert.True(CountScale(5, "Major Pentatonic", trials: 300) > 0);
    }

    [Fact]
    public void Level10_PentatonicMoreLikelyThanLevel5()
    {
        int pentatonic5 = CountScale(5, "Major Pentatonic", trials: 400);
        int pentatonic10 = CountScale(10, "Major Pentatonic", trials: 400);
        Assert.True(pentatonic10 > pentatonic5,
            $"L10 pentatonic picks ({pentatonic10}) should exceed L5 ({pentatonic5})");
    }

    [Fact]
    public void Level15_StillIncludesBothMajorAndPentatonic()
    {
        var allowed = ChildLevelProgression.GetAllowedScalesForLevel(15);
        Assert.Contains("Major", allowed);
        Assert.Contains("Major Pentatonic", allowed);
    }

    [Fact]
    public void Level16_NoLongerIncludesPentatonicInPool()
    {
        var allowed = ChildLevelProgression.GetAllowedScalesForLevel(16);
        Assert.Contains("Major", allowed);
        Assert.Contains("Natural Minor", allowed);
        Assert.DoesNotContain("Major Pentatonic", allowed);
    }

    [Fact]
    public void ResolveScaleForFreshGeneration_Level5RandomExercise_FavorsMajor()
    {
        int majorCount = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            string scale = NoteSessionService.ResolveScaleForFreshGeneration(
                ScaleSelectionMode.ByLevel,
                tuneMode: "Selected Scale",
                isRandomMode: true,
                level: 5,
                new Random(seed));
            if (scale == "Major")
                majorCount++;
        }

        Assert.True(majorCount >= 160, $"Random Miscellaneous at L5 should favor Major ({majorCount}/200)");
    }

    [Fact]
    public void NamedScaleSelection_UnaffectedByRandomWeightingAtLevel1()
    {
        var session = new NoteSessionService
        {
            ChildLevel = 1,
            ScaleSelectionMode = ScaleSelectionMode.Named,
            SelectedScale = "Major Pentatonic",
            Key = "C",
        };
        session.PrepareEffectiveScaleForGeneration(0);
        session.PrepareFreshScaleAndKeyForGeneration("GoButton", repeatSame: false, generationSeed: 42);
        Assert.Equal("Major Pentatonic", session.EffectiveScale);
    }

    [Fact]
    public void Level24_Progression_Unchanged()
    {
        var allowed = ChildLevelProgression.GetAllowedScalesForLevel(24);
        Assert.Equal(["Major", "Natural Minor"], allowed);
        Assert.Equal("Natural Minor", ChildLevelProgression.GetDefaultScaleForLevel(24));
    }

    private static int CountScale(int level, string scale, int trials)
    {
        int count = 0;
        for (int seed = 0; seed < trials; seed++)
        {
            if (ChildLevelProgression.PickWeightedRandomScale(level, new Random(seed + level * 1000))
                == scale)
            {
                count++;
            }
        }

        return count;
    }
}
