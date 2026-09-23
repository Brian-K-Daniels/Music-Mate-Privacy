using musicmate.Services;

namespace musicmate.Tests;

public class KeyDifficultyRulesTests
{
    [Fact]
    public void Level1_AllowsOnlyCMajorAndAMinor()
    {
        Assert.True(KeyDifficultyRules.IsKeyAllowedAtLevel("C", "Major", 1));
        Assert.True(KeyDifficultyRules.IsKeyAllowedAtLevel("A", "Natural Minor", 1));

        Assert.False(KeyDifficultyRules.IsKeyAllowedAtLevel("G", "Major", 1));
        Assert.False(KeyDifficultyRules.IsKeyAllowedAtLevel("F", "Major", 1));
        Assert.False(KeyDifficultyRules.IsKeyAllowedAtLevel("E", "Natural Minor", 1));
        Assert.False(KeyDifficultyRules.IsKeyAllowedAtLevel("D", "Natural Minor", 1));
    }

    [Fact]
    public void Level1_ExcludesKeysWithOneOrMoreAccidentals()
    {
        foreach (var option in KeyDifficultyRules.GetMasterKeyFrequencyWeights())
        {
            if (KeySignatureRules.GetAccidentalCount(option.Key, "Major") > 0)
                Assert.False(KeyDifficultyRules.IsKeyAllowedAtLevel(option.Key, "Major", 1));
        }
    }

    [Theory]
    [InlineData("C#", "Major")]
    [InlineData("Cb", "Major")]
    [InlineData("A#", "Natural Minor")]
    [InlineData("Ab", "Natural Minor")]
    public void LevelsBelow71_ExcludeDifficultySevenKeys(string key, string scale)
    {
        Assert.Equal(7, KeyDifficultyRules.GetKeySignatureDifficulty(key, scale));
        Assert.False(KeyDifficultyRules.IsKeyAllowedAtLevel(key, scale, 70));
    }

    [Theory]
    [InlineData("C#", "Major")]
    [InlineData("Cb", "Major")]
    [InlineData("A#", "Natural Minor")]
    [InlineData("Ab", "Natural Minor")]
    public void Level71_AllowsDifficultySevenKeys(string key, string scale)
    {
        Assert.True(KeyDifficultyRules.IsKeyAllowedAtLevel(key, scale, 71));
    }

    [Theory]
    [InlineData("C", "Major", "A", "Natural Minor")]
    [InlineData("G", "Major", "E", "Natural Minor")]
    [InlineData("F", "Major", "D", "Natural Minor")]
    [InlineData("D", "Major", "B", "Natural Minor")]
    [InlineData("Bb", "Major", "G", "Natural Minor")]
    [InlineData("C#", "Major", "A#", "Natural Minor")]
    [InlineData("Cb", "Major", "Ab", "Natural Minor")]
    public void RelativeMajorMinorPairs_ShareKeySignatureDifficulty(
        string majorKey, string majorScale, string minorKey, string minorScale)
    {
        int majorDifficulty = KeyDifficultyRules.GetKeySignatureDifficulty(majorKey, majorScale);
        int minorDifficulty = KeyDifficultyRules.GetKeySignatureDifficulty(minorKey, minorScale);
        Assert.Equal(majorDifficulty, minorDifficulty);
    }

    [Fact]
    public void FrequencyWeighting_AppliedOnlyAfterLevelFiltering()
    {
        var level1MajorPool = KeyDifficultyRules.GetWeightedKeysForScale(1, "Major");
        Assert.All(level1MajorPool, o =>
            Assert.True(KeyDifficultyRules.IsKeyAllowedAtLevel(o.Key, "Major", 1)));
        Assert.DoesNotContain(level1MajorPool, o => o.Key == "G");
        Assert.Contains(level1MajorPool, o => o.Key == "C");

        var masterHasG = KeyDifficultyRules.GetMasterKeyFrequencyWeights()
            .Any(o => o.Key == "G" && o.Weight > 0);
        Assert.True(masterHasG);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(10, 0)]
    [InlineData(11, 1)]
    [InlineData(20, 1)]
    [InlineData(21, 2)]
    [InlineData(35, 2)]
    [InlineData(36, 3)]
    [InlineData(50, 3)]
    [InlineData(51, 4)]
    [InlineData(60, 4)]
    [InlineData(61, 5)]
    [InlineData(65, 5)]
    [InlineData(66, 6)]
    [InlineData(70, 6)]
    [InlineData(71, 7)]
    [InlineData(100, 7)]
    public void GetMaxKeySignatureDifficulty_MatchesLevelBands(int level, int expectedMax)
    {
        Assert.Equal(expectedMax, KeyDifficultyRules.GetMaxKeySignatureDifficulty(level));
    }

    [Fact]
    public void PickBalancedKeyForSignature_RespectsLevelLimitsAtLevel1()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            string key = KeyDifficultyRules.PickBalancedKeyForSignature("Major", 1, new Random(seed));
            Assert.Equal("C", key);
        }
    }
}
