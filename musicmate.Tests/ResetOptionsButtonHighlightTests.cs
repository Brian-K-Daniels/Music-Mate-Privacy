using musicmate.Services;
using musicmate.ViewModels;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class ResetOptionsButtonHighlightTests : IDisposable
{
    private readonly Dictionary<string, object?> _sessionStore = new();
    private readonly Dictionary<string, string> _themeStore = new();
    private readonly NoteSessionService _session;
    private readonly ThemeService _theme;
    private readonly SettingsResetService _reset;

    public ResetOptionsButtonHighlightTests()
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

    [Fact]
    public void CurrentEqualsFactory_FactoryActive_CustomInactive()
    {
        var state = _reset.EvaluateDefaultsButtonHighlightState();

        Assert.True(state.FactoryActive);
        Assert.False(state.CustomActive);
    }

    [Fact]
    public void CurrentEqualsCustomButNotFactory_CustomActive_FactoryInactive()
    {
        _session.Key = "G";
        _reset.SaveCustomDefaultsFromCurrent();

        var state = _reset.EvaluateDefaultsButtonHighlightState();

        Assert.False(state.FactoryActive);
        Assert.True(state.CustomActive);
        Assert.True(state.SaveCustomActive);
    }

    [Fact]
    public void CurrentMatchesNeither_BothDefaultsButtonsInactive()
    {
        _session.Key = "G";
        _reset.SaveCustomDefaultsFromCurrent();
        _session.Key = "D";

        var state = _reset.EvaluateDefaultsButtonHighlightState();

        Assert.False(state.FactoryActive);
        Assert.False(state.CustomActive);
        Assert.False(state.SaveCustomActive);
    }

    [Fact]
    public void CurrentEqualsFactoryAndCustom_FactoryTakesPrecedence()
    {
        _reset.SaveCustomDefaultsFromCurrent();

        var state = _reset.EvaluateDefaultsButtonHighlightState();

        Assert.True(state.FactoryActive);
        Assert.False(state.CustomActive);
        Assert.True(state.SaveCustomActive);
    }

    [Fact]
    public void ChangingOneSettingFromCustom_RemovesCustomMatchingIndication()
    {
        _session.Key = "G";
        _reset.SaveCustomDefaultsFromCurrent();
        Assert.True(_reset.EvaluateDefaultsButtonHighlightState().SaveCustomActive);

        _session.Tempo = SettingsPageViewModel.DefaultTempo + 3;

        var state = _reset.EvaluateDefaultsButtonHighlightState();
        Assert.False(state.CustomActive);
        Assert.False(state.SaveCustomActive);
    }

    [Fact]
    public void SavingCurrentAsCustom_EstablishesCustomMatchingUnlessFactoryPrecedenceApplies()
    {
        _session.Key = "G";
        _reset.SaveCustomDefaultsFromCurrent();

        var customState = _reset.EvaluateDefaultsButtonHighlightState();
        Assert.True(customState.SaveCustomActive);
        Assert.True(customState.CustomActive);
        Assert.False(customState.FactoryActive);

        _reset.ResetToFactoryDefaults();
        _reset.SaveCustomDefaultsFromCurrent();

        var factoryCustomState = _reset.EvaluateDefaultsButtonHighlightState();
        Assert.True(factoryCustomState.SaveCustomActive);
        Assert.True(factoryCustomState.FactoryActive);
        Assert.False(factoryCustomState.CustomActive);
    }

    [Fact]
    public void ReopeningPage_RecalculatesFromActualSettings_NotLastTappedButton()
    {
        _session.Key = "G";
        _reset.SaveCustomDefaultsFromCurrent();
        _reset.SetActiveDefaults(ActiveDefaultsSet.Custom);

        var reopened = new SettingsResetService(_session, _theme);
        var state = reopened.EvaluateDefaultsButtonHighlightState();

        Assert.False(state.FactoryActive);
        Assert.True(state.CustomActive);
        Assert.True(state.SaveCustomActive);
    }

    [Fact]
    public void RestoreCustomDefaults_RefreshesHighlightFromSnapshotComparison()
    {
        _session.Key = "G";
        _reset.SaveCustomDefaultsFromCurrent();
        _session.Key = "D";
        Assert.False(_reset.EvaluateDefaultsButtonHighlightState().CustomActive);

        _reset.RestoreCustomDefaults();

        var state = _reset.EvaluateDefaultsButtonHighlightState();
        Assert.False(state.FactoryActive);
        Assert.True(state.CustomActive);
        Assert.True(state.SaveCustomActive);
    }

    [Fact]
    public void NeverBothFactoryAndCustomActive()
    {
        for (int i = 0; i < 20; i++)
        {
            _session.Key = i % 2 == 0 ? "G" : SettingsPageViewModel.DefaultKey;
            _session.Tempo = SettingsPageViewModel.DefaultTempo + (i % 3);
            if (i % 4 == 0)
                _reset.SaveCustomDefaultsFromCurrent();

            var state = _reset.EvaluateDefaultsButtonHighlightState();
            Assert.False(state.FactoryActive && state.CustomActive);
        }
    }
}
