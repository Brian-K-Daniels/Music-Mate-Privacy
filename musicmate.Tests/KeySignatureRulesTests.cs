using musicmate.Services;

namespace musicmate.Tests;

public class KeySignatureRulesTests
{
    [Theory]
    [InlineData("C", "Major", 0, false)]
    [InlineData("G", "Major", 1, false)]
    [InlineData("D", "Major", 2, false)]
    [InlineData("A", "Major", 3, false)]
    [InlineData("E", "Major", 4, false)]
    [InlineData("B", "Major", 5, false)]
    [InlineData("F#", "Major", 6, false)]
    [InlineData("C#", "Major", 7, false)]
    [InlineData("F", "Major", 1, true)]
    [InlineData("Bb", "Major", 2, true)]
    [InlineData("Eb", "Major", 3, true)]
    [InlineData("Ab", "Major", 4, true)]
    [InlineData("Db", "Major", 5, true)]
    [InlineData("Gb", "Major", 6, true)]
    [InlineData("Cb", "Major", 7, true)]
    public void GetAccidentalCount_MajorKeys(string key, string scale, int expectedCount, bool expectedFlats)
    {
        Assert.Equal(expectedCount, KeySignatureRules.GetAccidentalCount(key, scale));
        Assert.Equal(expectedFlats, KeySignatureRules.KeySignatureUsesFlats(key, scale));
    }

    [Theory]
    [InlineData("A", "Natural Minor", 0, false)]
    [InlineData("D", "Natural Minor", 1, true)]
    [InlineData("E", "Natural Minor", 1, false)]
    [InlineData("G", "Natural Minor", 2, true)]
    [InlineData("C", "Natural Minor", 3, true)]
    [InlineData("F#", "Natural Minor", 3, false)]
    [InlineData("B", "Harmonic Minor", 2, false)]
    [InlineData("D", "Dorian", 2, false)]
    public void GetAccidentalCount_RelativeAndModalKeys(string key, string scale, int expectedCount, bool expectedFlats)
    {
        Assert.Equal(expectedCount, KeySignatureRules.GetAccidentalCount(key, scale));
        if (expectedCount > 0)
            Assert.Equal(expectedFlats, KeySignatureRules.KeySignatureUsesFlats(key, scale));
    }

    [Theory]
    [InlineData("Bb", "Major", 'B', "b")]
    [InlineData("Bb", "Major", 'E', "b")]
    [InlineData("Bb", "Major", 'F', null)]
    [InlineData("F#", "Natural Minor", 'F', "#")]
    [InlineData("F#", "Natural Minor", 'C', "#")]
    [InlineData("F#", "Natural Minor", 'G', "#")]
    [InlineData("F#", "Natural Minor", 'D', null)]
    public void GetSignatureAccidentalForLetter(string key, string scale, char letter, string? expected)
    {
        Assert.Equal(expected, KeySignatureRules.GetSignatureAccidentalForLetter(letter, key, scale));
    }

    [Theory]
    [InlineData("Natural Minor")]
    [InlineData("Harmonic Minor")]
    [InlineData("Melodic Minor")]
    [InlineData("Aeolian")]
    public void ScaleUsesRelativeMajorKeySignature_MinorFamily(string scale)
    {
        Assert.True(KeySignatureRules.ScaleUsesRelativeMajorKeySignature(scale));
    }

    [Theory]
    [InlineData("Major")]
    [InlineData("Dorian")]
    [InlineData("Mixolydian")]
    public void ScaleUsesRelativeMajorKeySignature_OtherScales(string scale)
    {
        Assert.False(KeySignatureRules.ScaleUsesRelativeMajorKeySignature(scale));
    }

    [Fact]
    public void RelativeMajorOf_MapsMinorRootsToRelativeMajor()
    {
        Assert.Equal("Eb", KeySignatureRules.RelativeMajorOf("C"));
        Assert.Equal("Bb", KeySignatureRules.RelativeMajorOf("G"));
        Assert.Equal("G", KeySignatureRules.RelativeMajorOf("E"));
    }
}
