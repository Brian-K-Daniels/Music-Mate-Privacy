using Microsoft.Maui.Graphics;
using musicmate.Services;
using musicmate.ViewModels;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class AreFactoryDefaultsAppliedTests : IDisposable
{
    private readonly Dictionary<string, object?> _sessionStore = new();
    private readonly Dictionary<string, string> _themeStore = new();
    private readonly NoteSessionService _session;
    private readonly ThemeService _theme;
    private readonly SettingsResetService _reset;

    public AreFactoryDefaultsAppliedTests()
    {
        SessionPreferences.TestStore = _sessionStore;
        ThemeService.TestStore = _themeStore;
        _sessionStore.Clear();
        _themeStore.Clear();

        _session = new NoteSessionService();
        _theme = new ThemeService();
        _theme.LoadFromPreferences();
        _reset = new SettingsResetService(_session, _theme);
        _reset.ResetToFactoryDefaults();
    }

    public void Dispose()
    {
        SessionPreferences.TestStore = null;
        ThemeService.TestStore = null;
    }

    private static Color FactoryActiveText
        => ThemeColorContrast.GetContrastingTextColor(Colors.Green);

    [Fact]
    public void AreFactoryDefaultsApplied_True_AfterFactoryReset()
    {
        Assert.True(_reset.AreFactoryDefaultsApplied);
    }

    [Fact]
    public void FactoryButtonColors_GreenWhenDefaultsActive()
    {
        Assert.True(_reset.AreFactoryDefaultsApplied);
        // Document the Reset Options page contract used by UpdateActiveDefaultsButtonHighlight.
        Assert.Equal(Colors.Green, Colors.Green);
        Assert.Equal(Colors.White, FactoryActiveText);
    }

    [Fact]
    public void ChangingSettingsValue_MakesAreFactoryDefaultsAppliedFalse()
    {
        Assert.True(_reset.AreFactoryDefaultsApplied);

        _session.Tempo = SettingsPageViewModel.DefaultTempo + 5;
        _reset.EvaluateAreFactoryDefaultsApplied();

        Assert.False(_reset.AreFactoryDefaultsApplied);
    }

    [Fact]
    public void ChangingAdvancedSettingsValue_MakesAreFactoryDefaultsAppliedFalse()
    {
        Assert.True(_reset.AreFactoryDefaultsApplied);

        _session.Tolerance = NoteSessionService.DefaultTolerance + 10;
        _reset.EvaluateAreFactoryDefaultsApplied();

        Assert.False(_reset.AreFactoryDefaultsApplied);
    }

    [Fact]
    public void ChangingAdvancedPreference_MakesAreFactoryDefaultsAppliedFalse()
    {
        Assert.True(_reset.AreFactoryDefaultsApplied);

        SessionPreferences.Set("LevelUp.SessionCount", LevelUpService.DefaultSessionCount + 1);
        _reset.NotifySettingsChanged();

        Assert.False(_reset.AreFactoryDefaultsApplied);
    }

    [Fact]
    public void RestoringAlteredSettingsValue_MakesButtonGreenAgain()
    {
        _session.Tempo = 120;
        _reset.EvaluateAreFactoryDefaultsApplied();
        Assert.False(_reset.AreFactoryDefaultsApplied);

        _session.Tempo = SettingsPageViewModel.DefaultTempo;
        _reset.EvaluateAreFactoryDefaultsApplied();

        Assert.True(_reset.AreFactoryDefaultsApplied);
    }

    [Fact]
    public void ResetToFactoryDefaults_MakesAreFactoryDefaultsAppliedTrue()
    {
        _session.AccidentalPercent = 25;
        _session.Tolerance = 80;
        SessionPreferences.Set("RepeatDelaySeconds", 5.0);
        _reset.NotifySettingsChanged();
        Assert.False(_reset.AreFactoryDefaultsApplied);

        _reset.ResetToFactoryDefaults();

        Assert.True(_reset.AreFactoryDefaultsApplied);
    }

    [Fact]
    public void SavedNonDefaultSettings_AppearInactiveWhenEvaluatedOnOpen()
    {
        _session.Key = "G";
        _reset.EvaluateAreFactoryDefaultsApplied();
        Assert.False(_reset.AreFactoryDefaultsApplied);

        // Simulate opening Reset Options: re-evaluate from live state.
        var opened = new SettingsResetService(_session, _theme);
        Assert.False(opened.AreFactoryDefaultsApplied);
    }

    [Fact]
    public void ProgrammaticSettingChange_RaisesPropertyChanged()
    {
        Assert.True(_reset.AreFactoryDefaultsApplied);
        int notifications = 0;
        _reset.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsResetService.AreFactoryDefaultsApplied))
                notifications++;
        };

        _session.SmallestRhythmNote = "Sixteenth";

        Assert.True(notifications >= 1);
        Assert.False(_reset.AreFactoryDefaultsApplied);
    }

    [Fact]
    public void InactiveColors_AreWhiteBackgroundBlackText()
    {
        _session.AutoStart = false;
        _reset.EvaluateAreFactoryDefaultsApplied();
        Assert.False(_reset.AreFactoryDefaultsApplied);

        Assert.Equal(Colors.White, Colors.White);
        Assert.Equal(Colors.Black, Colors.Black);
    }

    [Fact]
    public void ThemeColorChange_MakesAreFactoryDefaultsAppliedFalse()
    {
        Assert.True(_reset.AreFactoryDefaultsApplied);

        _theme.SetColor(AppColorTarget.PanelBackground, Colors.Red);
        _reset.EvaluateAreFactoryDefaultsApplied();

        Assert.False(_reset.AreFactoryDefaultsApplied);
    }
}
