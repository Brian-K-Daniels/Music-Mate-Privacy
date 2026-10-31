using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

public class NoteMasteryStatusColorsTests
{
    [Theory]
    [InlineData(NoteMasteryState.Mastered)]
    [InlineData(NoteMasteryState.Improving)]
    [InlineData(NoteMasteryState.NeedsPractice)]
    [InlineData(NoteMasteryState.NotYetAttempted)]
    public void ForState_IsReadable_OnLightAndDarkPanels(NoteMasteryState state)
    {
        var light = Colors.White;
        var dark = Color.FromArgb("#121212");

        var onLight = NoteMasteryStatusColors.ForState(state, light);
        var onDark = NoteMasteryStatusColors.ForState(state, dark);

        Assert.True(ThemeColorContrast.HasReadableContrast(onLight, light));
        Assert.True(ThemeColorContrast.HasReadableContrast(onDark, dark));
        Assert.NotEqual(onLight.ToHex(), onDark.ToHex());
    }

    [Fact]
    public void ForState_UsesDistinctHues_ForPracticeStatuses()
    {
        var bg = Colors.White;
        var mastered = NoteMasteryStatusColors.ForState(NoteMasteryState.Mastered, bg);
        var improving = NoteMasteryStatusColors.ForState(NoteMasteryState.Improving, bg);
        var practice = NoteMasteryStatusColors.ForState(NoteMasteryState.NeedsPractice, bg);

        Assert.NotEqual(mastered.ToHex(), improving.ToHex());
        Assert.NotEqual(mastered.ToHex(), practice.ToHex());
        Assert.NotEqual(improving.ToHex(), practice.ToHex());
    }

    [Fact]
    public void Description_ExplainsEachStatusWord()
    {
        Assert.Contains("mastery", NoteMasteryStateLabels.Description(NoteMasteryState.Mastered), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("progress", NoteMasteryStateLabels.Description(NoteMasteryState.Improving), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("practice", NoteMasteryStateLabels.Description(NoteMasteryState.NeedsPractice), StringComparison.OrdinalIgnoreCase);
    }
}
