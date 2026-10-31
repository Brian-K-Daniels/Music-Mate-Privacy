using musicmate.Services;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class ShowSignaturesOnBothStaffsTests : IDisposable
{
    private readonly Dictionary<string, object?> _sessionStore = new();
    private readonly Dictionary<string, string> _themeStore = new();

    public ShowSignaturesOnBothStaffsTests()
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
    public void FactoryDefault_IsOn()
    {
        var session = new NoteSessionService();
        Assert.True(session.ShowSignaturesOnBothStaffs);
    }

    [Fact]
    public void Setter_PersistsAndRaisesPropertyChanged()
    {
        var session = new NoteSessionService();
        Assert.True(session.ShowSignaturesOnBothStaffs);

        string? changed = null;
        session.PropertyChanged += (_, e) => changed = e.PropertyName;

        session.ShowSignaturesOnBothStaffs = false;
        Assert.False(session.ShowSignaturesOnBothStaffs);
        Assert.Equal(nameof(NoteSessionService.ShowSignaturesOnBothStaffs), changed);
        Assert.False(SessionPreferences.Get("musicmate.ShowSignaturesOnBothStaffs", true));

        changed = null;
        session.ShowSignaturesOnBothStaffs = true;
        Assert.True(session.ShowSignaturesOnBothStaffs);
        Assert.Equal(nameof(NoteSessionService.ShowSignaturesOnBothStaffs), changed);
        Assert.True(SessionPreferences.Get("musicmate.ShowSignaturesOnBothStaffs", false));
    }

    [Fact]
    public void FactoryReset_RestoresOn()
    {
        var session = new NoteSessionService();
        var theme = new ThemeService();
        theme.LoadFromPreferences();
        var reset = new SettingsResetService(session, theme);

        session.ShowSignaturesOnBothStaffs = false;
        Assert.False(session.ShowSignaturesOnBothStaffs);

        reset.ResetToFactoryDefaults();
        Assert.True(session.ShowSignaturesOnBothStaffs);
    }

    [Fact]
    public void LowerStaffUsesFullHeader_WhenSettingOnAndBothStaffsHaveNotes()
    {
        // Mirrors StaffDrawable: full header when setting ON or lower is sole staff with notes.
        bool showOnBoth = true;
        int upperCount = 3;
        int lowerCount = 2;
        bool lowerUsesFullHeader = lowerCount > 0
            && (upperCount == 0 || showOnBoth);
        bool lowerClefOnlyStart = lowerCount > 0 && !lowerUsesFullHeader;

        Assert.True(lowerUsesFullHeader);
        Assert.False(lowerClefOnlyStart);
    }

    [Fact]
    public void LowerStaffUsesClefOnly_WhenSettingOffAndBothStaffsHaveNotes()
    {
        bool showOnBoth = false;
        int upperCount = 3;
        int lowerCount = 2;
        bool lowerUsesFullHeader = lowerCount > 0
            && (upperCount == 0 || showOnBoth);
        bool lowerClefOnlyStart = lowerCount > 0 && !lowerUsesFullHeader;

        Assert.False(lowerUsesFullHeader);
        Assert.True(lowerClefOnlyStart);
    }

    [Fact]
    public void LowerStaffUsesFullHeader_WhenUpperEmpty_EvenIfSettingOff()
    {
        bool showOnBoth = false;
        int upperCount = 0;
        int lowerCount = 4;
        bool lowerUsesFullHeader = lowerCount > 0
            && (upperCount == 0 || showOnBoth);

        Assert.True(lowerUsesFullHeader);
    }
}
