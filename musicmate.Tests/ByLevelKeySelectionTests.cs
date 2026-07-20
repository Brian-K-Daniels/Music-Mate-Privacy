using musicmate.Services;

namespace musicmate.Tests;

public class ByLevelKeySelectionTests
{
    [Fact]
    public void Level1_PermitsOnlyIntendedBeginnerKeys()
    {
        var majorKeys = KeyDifficultyRules.GetAllowedKeyNamesForScale(1, "Major");
        Assert.Equal(new HashSet<string>(StringComparer.Ordinal) { "C" }, majorKeys);

        var minorKeys = KeyDifficultyRules.GetAllowedKeyNamesForScale(1, "Natural Minor");
        Assert.Equal(new HashSet<string>(StringComparer.Ordinal) { "A" }, minorKeys);

        for (int seed = 0; seed < 80; seed++)
        {
            Assert.Equal("C", KeyDifficultyRules.PickBalancedKeyForSignature("Major", 1, new Random(seed)));
            Assert.Equal("A", KeyDifficultyRules.PickBalancedKeyForSignature("Natural Minor", 1, new Random(seed)));
        }
    }

    [Fact]
    public void Level21_CannotProduceSixSharpsOrSixFlats()
    {
        const int level = 21;
        Assert.Equal(2, KeyDifficultyRules.GetMaxKeySignatureDifficulty(level));
        Assert.False(KeyDifficultyRules.IsKeyAllowedAtLevel("F#", "Major", level));
        Assert.False(KeyDifficultyRules.IsKeyAllowedAtLevel("Gb", "Major", level));

        foreach (string scale in new[] { "Major", "Natural Minor", "Major Pentatonic" })
        {
            for (int seed = 0; seed < 200; seed++)
            {
                string key = KeyDifficultyRules.PickBalancedKeyForSignature(scale, level, new Random(seed));
                int accidentals = KeySignatureRules.GetAccidentalCount(key, scale);
                Assert.True(
                    accidentals <= 2,
                    $"L{level} {scale} picked {key} with {accidentals} accidentals");
                Assert.NotEqual("F#", key);
                Assert.NotEqual("Gb", key);
                Assert.NotEqual("C#", key);
                Assert.NotEqual("Cb", key);
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(21)]
    [InlineData(35)]
    [InlineData(50)]
    [InlineData(65)]
    [InlineData(70)]
    [InlineData(71)]
    [InlineData(100)]
    public void EveryLevel_OnlyProducesKeysAtOrBelowConfiguredMinimumLevel(int level)
    {
        foreach (string scale in new[] { "Major", "Natural Minor" })
        {
            var permitted = KeyDifficultyRules.GetWeightedKeysForScale(level, scale);
            Assert.All(permitted, option =>
            {
                Assert.True(
                    KeyDifficultyRules.GetMinimumLevelForKey(option.Key, scale) <= level,
                    $"{option.Key} {scale} min level exceeds L{level}");
                Assert.True(KeyDifficultyRules.IsKeyAllowedAtLevel(option.Key, scale, level));
            });

            for (int seed = 0; seed < 40; seed++)
            {
                string key = KeyDifficultyRules.PickBalancedKeyForSignature(scale, level, new Random(seed));
                Assert.True(KeyDifficultyRules.GetMinimumLevelForKey(key, scale) <= level);
                Assert.True(KeyDifficultyRules.IsKeyAllowedAtLevel(key, scale, level));
            }
        }
    }

    [Fact]
    public void SharpFlatBalancing_NeverOverridesLevelRestriction()
    {
        // At L21 the sharp bucket exists (G, D) but must never reach F# (6 sharps).
        int sawSharp = 0;
        for (int seed = 0; seed < 300; seed++)
        {
            string key = KeyDifficultyRules.PickBalancedKeyForSignature("Major", 21, new Random(seed));
            Assert.True(KeyDifficultyRules.IsKeyAllowedAtLevel(key, "Major", 21));
            Assert.True(KeySignatureRules.GetAccidentalCount(key, "Major") <= 2);
            if (KeySignatureRules.GetSignedAccidentalCount(key, "Major") > 0)
                sawSharp++;
        }

        Assert.True(sawSharp > 0, "Expected some sharp keys within the L21 cap");
    }

    [Theory]
    [InlineData("C#", "Major")]
    [InlineData("Cb", "Major")]
    [InlineData("A#", "Natural Minor")]
    [InlineData("Ab", "Natural Minor")]
    public void DifficultySevenKeys_UnavailableUntilConfiguredHighLevelThreshold(string key, string scale)
    {
        Assert.Equal(7, KeyDifficultyRules.GetKeySignatureDifficulty(key, scale));
        Assert.Equal(71, KeyDifficultyRules.GetMinimumLevelForKey(key, scale));

        for (int level = 1; level <= 70; level++)
            Assert.False(KeyDifficultyRules.IsKeyAllowedAtLevel(key, scale, level));

        Assert.True(KeyDifficultyRules.IsKeyAllowedAtLevel(key, scale, 71));
        Assert.DoesNotContain(
            KeyDifficultyRules.GetWeightedKeysForScale(70, scale),
            o => o.Key == key);
        Assert.Contains(
            KeyDifficultyRules.GetWeightedKeysForScale(71, scale),
            o => o.Key == key);
    }

    [Theory]
    [InlineData("F#", "Major", 66)]
    [InlineData("Gb", "Major", 66)]
    public void SixAccidentalEnharmonics_ShareConfiguredMinimumLevel(
        string key, string scale, int expectedMinLevel)
    {
        Assert.Equal(6, KeyDifficultyRules.GetKeySignatureDifficulty(key, scale));
        Assert.Equal(expectedMinLevel, KeyDifficultyRules.GetMinimumLevelForKey(key, scale));
        Assert.False(KeyDifficultyRules.IsKeyAllowedAtLevel(key, scale, expectedMinLevel - 1));
        Assert.True(KeyDifficultyRules.IsKeyAllowedAtLevel(key, scale, expectedMinLevel));
    }

    [Fact]
    public void EnsureKeyAllowedAtLevel_RejectsIllegalKeyAndReplacesWithPermitted()
    {
        string replacement = KeyDifficultyRules.EnsureKeyAllowedAtLevel(
            "F#", "Major", level: 21, new Random(1));

        Assert.NotEqual("F#", replacement);
        Assert.True(KeyDifficultyRules.IsKeyAllowedAtLevel(replacement, "Major", 21));
        Assert.True(KeySignatureRules.GetAccidentalCount(replacement, "Major") <= 2);
    }

    [Fact]
    public void PickBalancedKeyForSignature_OnlyUsesPrefilteredPermittedList()
    {
        const int level = 21;
        var permitted = KeyDifficultyRules.GetAllowedKeyNamesForScale(level, "Major");

        for (int seed = 0; seed < 100; seed++)
        {
            string key = KeyDifficultyRules.PickBalancedKeyForSignature("Major", level, new Random(seed));
            Assert.Contains(key, permitted);
        }
    }

    [Fact]
    public void BbInstrumentArpeggio_Level21_NeverWritesSixSharpKey()
    {
        const int level = 21;
        const int bbOffset = -2;

        var candidateRoots = ChildLevelProgression.GetAllowedKeys(level);
        foreach (string root in candidateRoots)
        {
            string written = NoteSessionService.ResolveArpeggioWrittenKeySignature(
                ArpeggioCatalog.MajorTriad, $"{root}4", bbOffset);
            if (!KeyDifficultyRules.IsKeyAllowedAtLevel(written, "Major", level))
                continue;

            Assert.True(
                KeySignatureRules.GetAccidentalCount(written, "Major") <= 2,
                $"Root {root} → written {written} exceeds L{level}");
            Assert.NotEqual("F#", written);
            Assert.NotEqual("Gb", written);
        }

        // Concert E is in the L21 union pool (via minor) but must not be chosen for Bb
        // major arpeggios because written F# is illegal at this level.
        string eWritten = NoteSessionService.ResolveArpeggioWrittenKeySignature(
            ArpeggioCatalog.MajorTriad, "E4", bbOffset);
        Assert.Equal("F#", eWritten);
        Assert.False(KeyDifficultyRules.IsKeyAllowedAtLevel(eWritten, "Major", level));
    }
}
