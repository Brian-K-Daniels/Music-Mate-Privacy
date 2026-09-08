using musicmate.Services;

namespace musicmate.Tests;

public class BalancedKeySelectionTests
{
    [Fact]
    public void PickBalancedKeyForSignature_OnlyUsesAllowedKeysAtLevel()
    {
        const int level = 1;
        var allowed = ChildLevelProgression.GetAllowedKeys(level);

        for (int seed = 0; seed < 50; seed++)
        {
            string key = ChildLevelProgression.PickBalancedKeyForSignature("Major", level, new Random(seed));
            Assert.Contains(key, allowed);
            Assert.Equal("C", key);
        }
    }

    [Fact]
    public void PickBalancedKeyForSignature_ApproximatelyBalancedWhenBothBucketsExist()
    {
        const int level = 80;
        const string scale = "Major";
        int flatCount = 0;
        int sharpCount = 0;

        for (int seed = 0; seed < 200; seed++)
        {
            string key = ChildLevelProgression.PickBalancedKeyForSignature(scale, level, new Random(seed));
            if (KeySignatureRules.KeySignatureUsesFlats(key, scale))
                flatCount++;
            else if (KeySignatureRules.GetSignedAccidentalCount(key, scale) > 0)
                sharpCount++;
        }

        Assert.True(flatCount > 50, $"Expected many flat signatures, got {flatCount}");
        Assert.True(sharpCount > 50, $"Expected many sharp signatures, got {sharpCount}");
    }

    [Fact]
    public void PickBalancedKeyForSignature_CanPickFlatMinorKeysAtMidLevel()
    {
        bool sawFlat = false;
        for (int seed = 0; seed < 200; seed++)
        {
            string key = ChildLevelProgression.PickBalancedKeyForSignature(
                "Natural Minor", level: 25, new Random(seed));
            if (KeySignatureRules.KeySignatureUsesFlats(key, "Natural Minor"))
                sawFlat = true;
        }

        Assert.True(sawFlat);
    }

    [Fact]
    public void PickBalancedKeyForSignature_Level1MajorNeverPicksSharpKeys()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            string key = ChildLevelProgression.PickBalancedKeyForSignature("Major", level: 1, new Random(seed));
            Assert.Equal(0, KeySignatureRules.GetAccidentalCount(key, "Major"));
        }
    }

    [Fact]
    public void L1_NaturalMinor_FallsBackToNaturalWhenAccidentalsEmpty()
    {
        for (int seed = 0; seed < 40; seed++)
        {
            string key = ChildLevelProgression.PickBalancedKeyForSignature(
                "Natural Minor", level: 1, new Random(seed));
            Assert.Equal("A", key);
            Assert.Equal(0, KeySignatureRules.GetAccidentalCount(key, "Natural Minor"));
        }
    }

    [Fact]
    public void L18_NaturalMinor_UsesBothFlatAndSharpBuckets()
    {
        bool sawFlat = false;
        bool sawSharp = false;
        for (int seed = 0; seed < 80; seed++)
        {
            string key = ChildLevelProgression.PickBalancedKeyForSignature(
                "Natural Minor", level: 18, new Random(seed));
            Assert.True(KeyDifficultyRules.IsKeyAllowedAtLevel(key, "Natural Minor", 18));
            Assert.True(KeyDifficultyRules.GetKeySignatureDifficulty(key, "Natural Minor") <= 1);
            if (KeySignatureRules.KeySignatureUsesFlats(key, "Natural Minor"))
                sawFlat = true;
            else if (KeySignatureRules.GetSignedAccidentalCount(key, "Natural Minor") > 0)
                sawSharp = true;
        }

        Assert.True(sawFlat, "Expected at least one flat-signature minor key at L18");
        Assert.True(sawSharp, "Expected at least one sharp-signature minor key at L18");
    }
}
