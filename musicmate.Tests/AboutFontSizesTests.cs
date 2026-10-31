using musicmate.ViewModels;

namespace musicmate.Tests;

public class AboutFontSizesTests
{
    [Fact]
    public void Range_IsEvenStepsFrom6Through24()
    {
        Assert.Equal(6, AboutFontSizes.Min);
        Assert.Equal(24, AboutFontSizes.Max);
        Assert.Equal(2, AboutFontSizes.Step);
        Assert.Equal(
            new double[] { 6, 8, 10, 12, 14, 16, 18, 20, 22, 24 },
            AboutFontSizes.All);
    }

    [Fact]
    public void Default_Is12_AndPreviousMax18StillPresent()
    {
        Assert.Equal(12, AboutFontSizes.Default);
        Assert.True(AboutFontSizes.Contains(18));
        Assert.True(AboutFontSizes.Contains(24));
        Assert.False(AboutFontSizes.Contains(25));
        Assert.False(AboutFontSizes.Contains(7));
    }

    [Fact]
    public void ClampOrDefault_KeepsValid_AndFallsBackForUnknown()
    {
        Assert.Equal(20, AboutFontSizes.ClampOrDefault(20));
        Assert.Equal(12, AboutFontSizes.ClampOrDefault(19));
    }

    [Fact]
    public void PreferenceKey_Unchanged()
        => Assert.Equal("About_FontSize", AboutFontSizes.PreferenceKey);
}
