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
    public void PickBalancedKeyForSignature_FallsBackToFlatWhenSharpBucketEmpty()
    {
        string key = ChildLevelProgression.PickBalancedKeyForSignature(
            "Natural Minor", level: 18, new Random(42));

        Assert.True(KeySignatureRules.KeySignatureUsesFlats(key, "Natural Minor"));
    }

    [Fact]
    public void PickBalancedKeyForSignature_CanPickSharpKeysAtLowMajorLevel()
    {
        bool sawSharp = false;
        for (int seed = 0; seed < 100; seed++)
        {
            string key = ChildLevelProgression.PickBalancedKeyForSignature("Major", level: 1, new Random(seed));
            if (KeySignatureRules.GetSignedAccidentalCount(key, "Major") > 0)
                sawSharp = true;
        }

        Assert.True(sawSharp);
    }
}
