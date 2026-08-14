using musicmate.Services;

namespace musicmate.Tests;

public class IntervalEarTrainingButtonColorsTests
{
    [Theory]
    [InlineData(0, IntervalEarTrainingButtonColors.IntervalFamily.Unison)]
    [InlineData(1, IntervalEarTrainingButtonColors.IntervalFamily.Minor)]
    [InlineData(2, IntervalEarTrainingButtonColors.IntervalFamily.Major)]
    [InlineData(3, IntervalEarTrainingButtonColors.IntervalFamily.Minor)]
    [InlineData(4, IntervalEarTrainingButtonColors.IntervalFamily.Major)]
    [InlineData(5, IntervalEarTrainingButtonColors.IntervalFamily.Perfect)]
    [InlineData(6, IntervalEarTrainingButtonColors.IntervalFamily.Tritone)]
    [InlineData(7, IntervalEarTrainingButtonColors.IntervalFamily.Perfect)]
    [InlineData(8, IntervalEarTrainingButtonColors.IntervalFamily.Minor)]
    [InlineData(9, IntervalEarTrainingButtonColors.IntervalFamily.Major)]
    [InlineData(10, IntervalEarTrainingButtonColors.IntervalFamily.Minor)]
    [InlineData(11, IntervalEarTrainingButtonColors.IntervalFamily.Major)]
    [InlineData(12, IntervalEarTrainingButtonColors.IntervalFamily.Perfect)]
    public void GetFamily_MapsZeroThroughTwelve(int semitones, IntervalEarTrainingButtonColors.IntervalFamily expected)
        => Assert.Equal(expected, IntervalEarTrainingButtonColors.GetFamily(semitones));

    [Fact]
    public void FamilyBackground_MatchesSharedFamilyColors()
    {
        Assert.Equal(IntervalEarTrainingButtonColors.Unison, IntervalEarTrainingButtonColors.FamilyBackground(0));
        Assert.Equal(IntervalEarTrainingButtonColors.Minor, IntervalEarTrainingButtonColors.FamilyBackground(1));
        Assert.Equal(IntervalEarTrainingButtonColors.Minor, IntervalEarTrainingButtonColors.FamilyBackground(3));
        Assert.Equal(IntervalEarTrainingButtonColors.Minor, IntervalEarTrainingButtonColors.FamilyBackground(8));
        Assert.Equal(IntervalEarTrainingButtonColors.Minor, IntervalEarTrainingButtonColors.FamilyBackground(10));
        Assert.Equal(IntervalEarTrainingButtonColors.Major, IntervalEarTrainingButtonColors.FamilyBackground(2));
        Assert.Equal(IntervalEarTrainingButtonColors.Major, IntervalEarTrainingButtonColors.FamilyBackground(4));
        Assert.Equal(IntervalEarTrainingButtonColors.Major, IntervalEarTrainingButtonColors.FamilyBackground(9));
        Assert.Equal(IntervalEarTrainingButtonColors.Major, IntervalEarTrainingButtonColors.FamilyBackground(11));
        Assert.Equal(IntervalEarTrainingButtonColors.Perfect, IntervalEarTrainingButtonColors.FamilyBackground(5));
        Assert.Equal(IntervalEarTrainingButtonColors.Perfect, IntervalEarTrainingButtonColors.FamilyBackground(7));
        Assert.Equal(IntervalEarTrainingButtonColors.Perfect, IntervalEarTrainingButtonColors.FamilyBackground(12));
        Assert.Equal(IntervalEarTrainingButtonColors.Tritone, IntervalEarTrainingButtonColors.FamilyBackground(6));
    }

    [Fact]
    public void FeedbackColors_DifferFromAllFamilyColors()
    {
        var families = new[]
        {
            IntervalEarTrainingButtonColors.Unison,
            IntervalEarTrainingButtonColors.Minor,
            IntervalEarTrainingButtonColors.Major,
            IntervalEarTrainingButtonColors.Perfect,
            IntervalEarTrainingButtonColors.Tritone,
        };

        Assert.DoesNotContain(IntervalEarTrainingButtonColors.CorrectBackground, families);
        Assert.DoesNotContain(IntervalEarTrainingButtonColors.WrongBackground, families);
    }

    [Fact]
    public void EverySemitone_HasAFamilyBackground()
    {
        for (int s = 0; s <= 12; s++)
        {
            var color = IntervalEarTrainingButtonColors.FamilyBackground(s);
            Assert.NotEqual(default, color);
            Assert.InRange(color.Alpha, 0.99f, 1.01f);
        }
    }
}
