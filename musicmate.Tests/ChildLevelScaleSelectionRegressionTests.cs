using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Former DEBUG in-app ChildLevelScaleSelectionTests.RunSelfChecks — keep as xUnit only.
/// </summary>
[Collection("SessionPreferences")]
public class ChildLevelScaleSelectionRegressionTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public ChildLevelScaleSelectionRegressionTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Fact]
    public void Level1_RandomPool_IsMajorOnly()
    {
        var allowed = ChildLevelProgression.GetAllowedScalesForLevel(1);
        Assert.Contains("Major", allowed);
        Assert.Contains("Major Pentatonic", allowed);

        var randomPool = ChildLevelProgression.GetProfile(1).ScalePool;
        Assert.Single(randomPool);
        Assert.Equal("Major", randomPool[0].Scale);

        for (int i = 0; i < 50; i++)
            Assert.Equal("Major", ChildLevelProgression.PickWeightedRandomScale(1, new Random(i)));
    }

    [Fact]
    public void Level5_Random_FavorsMajor()
    {
        int major = 0;
        for (int i = 0; i < 100; i++)
        {
            if (ChildLevelProgression.PickWeightedRandomScale(5, new Random(i + 50)) == "Major")
                major++;
        }

        Assert.True(major >= 80, $"Expected Major to dominate at L5, got {major}/100");
    }

    [Fact]
    public void Level10_Pentatonic_MoreCommonThanLevel5()
    {
        int pentatonic5 = CountPentatonicPicks(5, 200, seedOffset: 300);
        int pentatonic10 = CountPentatonicPicks(10, 200, seedOffset: 600);
        Assert.True(pentatonic10 > pentatonic5,
            $"L10 pentatonic ({pentatonic10}) should exceed L5 ({pentatonic5})");
    }

    [Fact]
    public void Level10_Random_OnlyFromAllowed()
    {
        var allowed = ChildLevelProgression.GetAllowedScalesForLevel(10);
        Assert.Contains("Major Pentatonic", allowed);
        Assert.Contains("Major", allowed);
        Assert.Equal(2, allowed.Count);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < 100; i++)
            seen.Add(ChildLevelProgression.PickWeightedRandomScale(10, new Random(i + 100)));
        Assert.True(seen.SetEquals(allowed));
    }

    [Fact]
    public void Level60_Pool_IncludesCoreExcludesModes()
    {
        var allowed = ChildLevelProgression.GetAllowedScalesForLevel(60);
        foreach (var scale in new[]
                 {
                     "Major", "Natural Minor", "Harmonic Minor", "Melodic Minor",
                     "Minor Pentatonic", "Blues"
                 })
            Assert.Contains(scale, allowed);

        foreach (var scale in new[] { "Dorian", "Lydian", "Phrygian", "Locrian", "Enigmatic" })
            Assert.DoesNotContain(scale, allowed);
    }

    [Fact]
    public void Level70_HasMixolydian_NotDorianOrLydian()
    {
        var allowed = ChildLevelProgression.GetAllowedScalesForLevel(70);
        Assert.Contains("Mixolydian", allowed);
        Assert.DoesNotContain("Dorian", allowed);
        Assert.DoesNotContain("Lydian", allowed);
    }

    [Fact]
    public void Level80_HasDorian()
        => Assert.Contains("Dorian", ChildLevelProgression.GetAllowedScalesForLevel(80));

    [Fact]
    public void Level90_HasLydianPhrygian_NotEnigmatic()
    {
        var allowed = ChildLevelProgression.GetAllowedScalesForLevel(90);
        Assert.Contains("Lydian", allowed);
        Assert.Contains("Phrygian", allowed);
        if (NoteSessionService.AvailableScales.Contains("Enigmatic"))
            Assert.DoesNotContain("Enigmatic", allowed);
    }

    [Fact]
    public void Level95_AllowsAllNonChromaticScales()
    {
        var allowed = ChildLevelProgression.GetAllowedScalesForLevel(95);
        foreach (var scale in NoteSessionService.AvailableScales)
        {
            if (scale == "Chromatic")
                continue;
            Assert.Contains(scale, allowed);
        }
    }

    [Fact]
    public void Locrian_AllowedAt95_NotAt10()
    {
        Assert.True(ChildLevelProgression.IsScaleAllowedAtLevel(95, "Locrian"));
        Assert.False(ChildLevelProgression.IsScaleAllowedAtLevel(10, "Locrian"));
        Assert.Equal("Major", ChildLevelProgression.GetDefaultScaleForLevel(10));
    }

    private static int CountPentatonicPicks(int level, int trials, int seedOffset)
    {
        int count = 0;
        for (int i = 0; i < trials; i++)
        {
            if (ChildLevelProgression.PickWeightedRandomScale(level, new Random(i + seedOffset))
                == "Major Pentatonic")
                count++;
        }

        return count;
    }
}
