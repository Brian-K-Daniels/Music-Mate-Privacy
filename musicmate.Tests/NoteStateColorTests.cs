using Microsoft.Maui.Graphics;
using musicmate.Drawables;
using musicmate.Services;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class NoteStateColorTests : IDisposable
{
    private readonly Dictionary<string, object?> _sessionStore = new();
    private readonly Dictionary<string, string> _themeStore = new();

    public NoteStateColorTests()
    {
        SessionPreferences.TestStore = _sessionStore;
        ThemeService.TestStore = _themeStore;
        _sessionStore.Clear();
        _themeStore.Clear();
    }

    public void Dispose()
    {
        SessionPreferences.TestStore = null;
        ThemeService.TestStore = null;
    }

    [Fact]
    public void EachNoteState_MapsToItsOwnColor()
    {
        var theme = LoadedTheme();
        theme.SetColor(AppColorTarget.NoteUnplayed, Color.FromArgb("#111111"));
        theme.SetColor(AppColorTarget.NoteToBePlayed, Color.FromArgb("#2222EE"));
        theme.SetColor(AppColorTarget.NotePlayed, Color.FromArgb("#22AA44"));
        theme.SetColor(AppColorTarget.NoteWrong, Color.FromArgb("#CC2222"));

        var staff = new StaffDrawable(new NoteSessionService(), theme);

        Assert.Equal(AppColorTarget.NoteUnplayed, NoteStateColors.TargetFor(StaffNoteState.Pending));
        Assert.Equal(AppColorTarget.NoteToBePlayed, NoteStateColors.TargetFor(StaffNoteState.Current));
        Assert.Equal(AppColorTarget.NotePlayed, NoteStateColors.TargetFor(StaffNoteState.Correct));
        Assert.Equal(AppColorTarget.NoteWrong, NoteStateColors.TargetFor(StaffNoteState.Wrong));

        Assert.Equal("#111111", staff.GetConfiguredNoteColor(StaffNoteState.Pending).ToHex());
        Assert.Equal("#2222EE", staff.GetConfiguredNoteColor(StaffNoteState.Current).ToHex());
        Assert.Equal("#22AA44", staff.GetConfiguredNoteColor(StaffNoteState.Correct).ToHex());
        Assert.Equal("#CC2222", staff.GetConfiguredNoteColor(StaffNoteState.Wrong).ToHex());

        Assert.Equal("Note unplayed", ThemeService.GetDisplayName(AppColorTarget.NoteUnplayed));
        Assert.Equal("Note to be played", ThemeService.GetDisplayName(AppColorTarget.NoteToBePlayed));
        Assert.Equal("Note played", ThemeService.GetDisplayName(AppColorTarget.NotePlayed));
        Assert.Equal("Note wrong", ThemeService.GetDisplayName(AppColorTarget.NoteWrong));
    }

    [Fact]
    public void NoteToBePlayed_FactoryDefaultIsBlue()
    {
        var theme = LoadedTheme();
        var current = theme.GetFactoryDefaultColor(AppColorTarget.NoteToBePlayed);

        Assert.Equal("#007BFF", current.ToHex());
        Assert.Equal("#007BFF", theme.NoteToBePlayedColor.ToHex());
        Assert.True(current.Blue > current.Red);
        Assert.True(current.Blue > current.Green);

        Assert.Equal("#000000", theme.GetFactoryDefaultColor(AppColorTarget.NoteUnplayed).ToHex());
        Assert.Equal("#22AA44", theme.GetFactoryDefaultColor(AppColorTarget.NotePlayed).ToHex());
        Assert.Equal("#CC2222", theme.GetFactoryDefaultColor(AppColorTarget.NoteWrong).ToHex());
    }

    [Fact]
    public void CustomNoteColors_PersistAcrossReload()
    {
        var theme = LoadedTheme();
        theme.SetColor(AppColorTarget.NoteUnplayed, Color.FromArgb("#010101"));
        theme.SetColor(AppColorTarget.NoteToBePlayed, Color.FromArgb("#123456"));
        theme.SetColor(AppColorTarget.NotePlayed, Color.FromArgb("#00AA00"));
        theme.SetColor(AppColorTarget.NoteWrong, Color.FromArgb("#AA0000"));

        var reloaded = new ThemeService();
        reloaded.LoadFromPreferences();

        Assert.Equal("#010101", reloaded.NoteUnplayedColor.ToHex());
        Assert.Equal("#123456", reloaded.NoteToBePlayedColor.ToHex());
        Assert.Equal("#00AA00", reloaded.NotePlayedColor.ToHex());
        Assert.Equal("#AA0000", reloaded.NoteWrongColor.ToHex());
    }

    [Fact]
    public void FactoryReset_RestoresNoteColorDefaults()
    {
        var session = new NoteSessionService();
        var theme = LoadedTheme();
        theme.SetColor(AppColorTarget.NoteUnplayed, Color.FromArgb("#010101"));
        theme.SetColor(AppColorTarget.NoteToBePlayed, Color.FromArgb("#123456"));
        theme.SetColor(AppColorTarget.NotePlayed, Color.FromArgb("#00AA00"));
        theme.SetColor(AppColorTarget.NoteWrong, Color.FromArgb("#AA0000"));

        var reset = new SettingsResetService(session, theme);
        reset.ResetToFactoryDefaults();

        Assert.Equal("#000000", theme.NoteUnplayedColor.ToHex());
        Assert.Equal("#007BFF", theme.NoteToBePlayedColor.ToHex());
        Assert.Equal("#22AA44", theme.NotePlayedColor.ToHex());
        Assert.Equal("#CC2222", theme.NoteWrongColor.ToHex());

        var reloaded = new ThemeService();
        reloaded.LoadFromPreferences();
        Assert.Equal("#007BFF", reloaded.NoteToBePlayedColor.ToHex());
        Assert.Equal("#000000", reloaded.NoteUnplayedColor.ToHex());
    }

    [Fact]
    public void StateTransitions_ChangeTheNoteColor()
    {
        var theme = LoadedTheme();
        var staff = new StaffDrawable(new NoteSessionService(), theme);
        var noneCorrect = Array.Empty<int>();
        var noFeedback = new Dictionary<int, (int Wrong, int Cents)>();

        var beforeCurrent = StaffNoteStateResolver.Resolve(2, 1, true, noneCorrect, noFeedback);
        var nowCurrent = StaffNoteStateResolver.Resolve(2, 2, true, noneCorrect, noFeedback);
        Assert.Equal(StaffNoteState.Pending, beforeCurrent);
        Assert.Equal(StaffNoteState.Current, nowCurrent);
        Assert.NotEqual(
            staff.GetConfiguredNoteColor(beforeCurrent).ToHex(),
            staff.GetConfiguredNoteColor(nowCurrent).ToHex());

        var played = StaffNoteStateResolver.Resolve(2, 3, true, new[] { 2 }, noFeedback);
        Assert.Equal(StaffNoteState.Correct, played);
        Assert.Equal("#22AA44", staff.GetConfiguredNoteColor(played).ToHex());

        var wrongFeedback = new Dictionary<int, (int Wrong, int Cents)> { [2] = (1, 15) };
        var wrong = StaffNoteStateResolver.Resolve(2, 2, true, noneCorrect, wrongFeedback);
        Assert.Equal(StaffNoteState.Wrong, wrong);
        Assert.Equal("#CC2222", staff.GetConfiguredNoteColor(wrong).ToHex());
        Assert.NotEqual(
            staff.GetConfiguredNoteColor(StaffNoteState.Current).ToHex(),
            staff.GetConfiguredNoteColor(wrong).ToHex());
    }

    [Fact]
    public void ChangingInstrumentLevelKeyOrTune_DoesNotChangeSavedNoteColors()
    {
        var theme = LoadedTheme();
        theme.SetColor(AppColorTarget.NoteUnplayed, Color.FromArgb("#010101"));
        theme.SetColor(AppColorTarget.NoteToBePlayed, Color.FromArgb("#123456"));
        theme.SetColor(AppColorTarget.NotePlayed, Color.FromArgb("#00AA00"));
        theme.SetColor(AppColorTarget.NoteWrong, Color.FromArgb("#AA0000"));
        var saved = SnapshotNoteColors();

        var session = new NoteSessionService
        {
            Instrument = "Piano",
            ChildLevel = 12,
            Key = "G",
            Tune = "Major Scale",
        };
        session.Instrument = "Tuba";
        session.ChildLevel = 40;
        session.Key = "D";
        session.Tune = "Chromatic";

        Assert.Equal(saved, SnapshotNoteColors());

        var reloaded = new ThemeService();
        reloaded.LoadFromPreferences();
        Assert.Equal("#010101", reloaded.NoteUnplayedColor.ToHex());
        Assert.Equal("#123456", reloaded.NoteToBePlayedColor.ToHex());
        Assert.Equal("#00AA00", reloaded.NotePlayedColor.ToHex());
        Assert.Equal("#AA0000", reloaded.NoteWrongColor.ToHex());
    }

    private ThemeService LoadedTheme()
    {
        var theme = new ThemeService();
        theme.LoadFromPreferences();
        return theme;
    }

    private Dictionary<string, string> SnapshotNoteColors()
    {
        var snapshot = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var target in new[]
                 {
                     AppColorTarget.NoteUnplayed,
                     AppColorTarget.NoteToBePlayed,
                     AppColorTarget.NotePlayed,
                     AppColorTarget.NoteWrong,
                 })
        {
            string key = ThemeService.ColorPreferencePrefix + target;
            snapshot[key] = _themeStore[key];
        }

        return snapshot;
    }
}
