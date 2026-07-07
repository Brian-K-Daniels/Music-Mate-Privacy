using Microsoft.Maui.Graphics;
using musicmate.Services;

namespace musicmate.Tests;

public class ThemeServiceTests : IDisposable
{
    private readonly Dictionary<string, string> _store = new();

    public ThemeServiceTests()
    {
        ThemeService.TestStore = _store;
        _store.Clear();
    }

    public void Dispose()
    {
        ThemeService.TestStore = null;
    }

    [Fact]
    public void GetColor_ReturnsFactoryDefault_WhenNoPreferenceSaved()
    {
        var theme = new ThemeService();
        theme.LoadFromPreferences();

        Assert.Equal("#FFFFFF", theme.GetColor(AppColorTarget.PanelBackground).ToHex());
        Assert.Equal("#8B4513", theme.GetColor(AppColorTarget.Slider).ToHex());
    }

    [Fact]
    public void SetColor_PersistsAndReloads()
    {
        var theme = new ThemeService();
        theme.SetColor(AppColorTarget.ButtonBackground, Color.FromArgb("#112233"));

        var reloaded = new ThemeService();
        reloaded.LoadFromPreferences();

        Assert.Equal("#112233", reloaded.GetColor(AppColorTarget.ButtonBackground).ToHex());
    }

    [Fact]
    public void ResetAllToFactoryDefaults_RestoresEveryTarget()
    {
        var theme = new ThemeService();
        theme.SetColor(AppColorTarget.PanelBackground, Colors.Red);
        theme.SetColor(AppColorTarget.Text, Colors.Blue);
        theme.SetColor(AppColorTarget.PickerBorder, Colors.Green);

        theme.ResetAllToFactoryDefaults();

        foreach (var target in ThemeService.AllColorTargets)
            Assert.Equal(theme.GetFactoryDefaultColor(target).ToHex(), theme.GetColor(target).ToHex());
    }

    [Fact]
    public void SetColor_SubstitutesUnreadableText_OnButtonText()
    {
        var theme = new ThemeService();
        theme.SetColor(AppColorTarget.ButtonBackground, Color.FromArgb("#FFFFFF"));
        theme.SetColor(AppColorTarget.ButtonText, Color.FromArgb("#EEEEEE"));

        Assert.Equal(Colors.Black.ToHex(), theme.GetColor(AppColorTarget.ButtonText).ToHex());
        Assert.Equal(Colors.Black.ToHex(), theme.ButtonTextColor.ToHex());
    }

    [Fact]
    public void GetContrastingTextColor_ReturnsBlackOrWhite_ByLuminance()
    {
        Assert.Equal(Colors.Black, ThemeColorContrast.GetContrastingTextColor(Colors.White));
        Assert.Equal(Colors.White, ThemeColorContrast.GetContrastingTextColor(Colors.Black));
    }

    [Fact]
    public void ResolveReadableText_KeepsReadablePair()
    {
        var result = ThemeColorContrast.ResolveReadableText(Colors.Black, Colors.White);
        Assert.Equal(Colors.Black, result);
    }

    [Fact]
    public void ShellChromeForeground_StaysReadable_WhenMainBackgroundDiffersFromPanel()
    {
        var theme = new ThemeService();
        theme.SetColor(AppColorTarget.MainBackground, Color.FromArgb("#1f1f1f"));
        theme.SetColor(AppColorTarget.PanelBackground, Color.FromArgb("#FFFFFF"));
        theme.SetColor(AppColorTarget.Text, Color.FromArgb("#000000"));

        Assert.Equal("#FFFFFF", theme.ShellChromeBackgroundColor.ToHex());
        Assert.Equal(Colors.Black.ToHex(), theme.ShellChromeForegroundColor.ToHex());
        Assert.NotEqual(theme.MainBackgroundColor.ToHex(), theme.ShellChromeBackgroundColor.ToHex());
    }

    [Fact]
    public void ShellChromeForeground_FlipsToWhite_OnDarkPanelBackground()
    {
        var theme = new ThemeService();
        theme.SetColor(AppColorTarget.PanelBackground, Color.FromArgb("#1f1f1f"));
        theme.SetColor(AppColorTarget.Text, Color.FromArgb("#000000"));

        Assert.Equal(Colors.White.ToHex(), theme.ShellChromeForegroundColor.ToHex());
    }

    [Fact]
    public void LoadFromPreferences_MigratesLegacyStaffPanelColor()
    {
        _store["StaffPanelColor"] = "#AABBCC";

        var theme = new ThemeService();
        theme.LoadFromPreferences();

        Assert.Equal("#AABBCC", theme.GetColor(AppColorTarget.PanelBackground).ToHex());
    }
}
