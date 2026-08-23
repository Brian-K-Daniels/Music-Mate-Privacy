using musicmate.Services;

namespace musicmate.Tests;

public class ByLevelScaleSelectionTests
{
    [Fact]
    public void Level24_AllowedPool_IncludesMajorAndNaturalMinor()
    {
        var allowed = ChildLevelProgression.GetAllowedScalesForLevel(24);
        Assert.Contains("Major", allowed);
        Assert.Contains("Natural Minor", allowed);
    }

    [Fact]
    public void Level24_DefaultScale_IsNaturalMinor()
    {
        Assert.Equal("Natural Minor", ChildLevelProgression.GetDefaultScaleForLevel(24));
    }

    [Fact]
    public void ResolveScaleForFreshGeneration_Level24ScaleExercise_IncludesMajor()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int seed = 0; seed < 100; seed++)
        {
            seen.Add(NoteSessionService.ResolveScaleForFreshGeneration(
                ScaleSelectionMode.ByLevel,
                tuneMode: "Selected Scale",
                isRandomMode: false,
                level: 24,
                new Random(seed)));
        }

        Assert.Contains("Major", seen);
        Assert.Contains("Natural Minor", seen);
    }

    [Fact]
    public void HasMultipleScaleWalkIdentities_FalseAtBeginnerPentatonicOnlyLevels()
    {
        Assert.False(ChildLevelProgression.HasMultipleScaleWalkIdentities(1));
        Assert.False(ChildLevelProgression.HasMultipleScaleWalkIdentities(5));
        Assert.True(ChildLevelProgression.HasMultipleScaleWalkIdentities(6));
    }

    [Fact]
    public void ResolveScaleForFreshGeneration_Level24RandomExercise_IncludesMajor()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int seed = 0; seed < 100; seed++)
        {
            seen.Add(NoteSessionService.ResolveScaleForFreshGeneration(
                ScaleSelectionMode.ByLevel,
                tuneMode: "Selected Scale",
                isRandomMode: true,
                level: 24,
                new Random(seed)));
        }

        Assert.Contains("Major", seen);
    }

    [Fact]
    public void ResolveScaleForFreshGeneration_Level24PracticeTune_KeepsLevelDefault()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            string scale = NoteSessionService.ResolveScaleForFreshGeneration(
                ScaleSelectionMode.ByLevel,
                tuneMode: "Practice Tune",
                isRandomMode: false,
                level: 24,
                new Random(seed));

            Assert.Equal("Natural Minor", scale);
        }
    }

    [Fact]
    public void ShouldPickFreshScaleFromLevelPool_OnlyForScaleAndRandomCategories()
    {
        Assert.True(NoteSessionService.ShouldPickFreshScaleFromLevelPool("Selected Scale", isRandomMode: false));
        Assert.True(NoteSessionService.ShouldPickFreshScaleFromLevelPool("Selected Scale", isRandomMode: true));
        Assert.False(NoteSessionService.ShouldPickFreshScaleFromLevelPool("Practice Tune", isRandomMode: false));
        Assert.False(NoteSessionService.ShouldPickFreshScaleFromLevelPool("Arpeggio", isRandomMode: false));
    }
}
