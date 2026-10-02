using musicmate.Services;
using musicmate.ViewModels;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class SessionColumnDefinitionsViewModelTests : IDisposable
{
    static SessionColumnDefinitionsViewModelTests()
    {
        SQLitePCL.Batteries.Init();
    }

    private readonly Dictionary<string, object?> _sessionStore = new();
    private readonly Dictionary<string, string> _themeStore = new();
    private readonly NoteDatabase _noteDatabase;
    private readonly SessionDatabase _sessionDatabase;
    private readonly NoteAttemptDatabase _attemptDatabase;
    private readonly NoteSessionService _session;
    private readonly ThemeService _themeService;
    private readonly StatisticsCacheService _statisticsCache;
    private readonly NoteMasteryService _noteMasteryService;
    private readonly NoteStatisticsViewModel _viewModel;

    public SessionColumnDefinitionsViewModelTests()
    {
        SessionPreferences.TestStore = _sessionStore;
        ThemeService.TestStore = _themeStore;
        _sessionStore.Clear();
        _themeStore.Clear();

        var noteDbPath = Path.Combine(Path.GetTempPath(), $"mm_note_{Guid.NewGuid():N}.db3");
        var sessionDbPath = Path.Combine(Path.GetTempPath(), $"mm_session_{Guid.NewGuid():N}.db3");
        var attemptDbPath = Path.Combine(Path.GetTempPath(), $"mm_attempt_{Guid.NewGuid():N}.db3");

        _noteDatabase = new NoteDatabase(noteDbPath);
        _sessionDatabase = new SessionDatabase(sessionDbPath);
        _attemptDatabase = new NoteAttemptDatabase(attemptDbPath);
        _session = new NoteSessionService();
        _themeService = new ThemeService();
        _themeService.LoadFromPreferences();
        _statisticsCache = new StatisticsCacheService();
        _noteMasteryService = new NoteMasteryService(_noteDatabase, _attemptDatabase, _session);
        _viewModel = new NoteStatisticsViewModel(
            _noteDatabase,
            _sessionDatabase,
            _themeService,
            _session,
            _statisticsCache,
            _noteMasteryService);

        SeedSessionStats();
    }

    public void Dispose()
    {
        SessionPreferences.TestStore = null;
        ThemeService.TestStore = null;
    }

    private void SeedSessionStats()
    {
        _viewModel.SessionStats.Clear();
        _viewModel.SessionStats.Add(new SessionStat
        {
            Id = 1,
            Dt = new DateTime(2026, 3, 1, 10, 30, 0),
            Level = 2,
            Pc = 88.5,
            Pch = 88.5,
            Tmg = 72.0,
            Ovrl = 80.3,
            PitchRightCount = 10,
            PitchWrongCount = 2,
            Key = "C",
            What = "Maj",
            Rand = true,
            AccPct = 15,
            Hi = "G4",
            Lo = "C4",
            Instrument = "Piano"
        });
        _viewModel.SessionStats.Add(new SessionStat
        {
            Id = 2,
            Dt = new DateTime(2026, 3, 2, 11, 0, 0),
            Level = 3,
            Pc = 91.0,
            Pch = 91.0,
            Tmg = 0.0,
            Ovrl = 91.0,
            PitchRightCount = 12,
            PitchWrongCount = 1,
            Key = "G",
            What = "Nat Min",
            Rand = false,
            AccPct = 0,
            Hi = "D5",
            Lo = "G3",
            Instrument = "Flute"
        });
    }

    private static string CaptureSessionSnapshot(NoteStatisticsViewModel viewModel)
        => string.Join("|", viewModel.SessionStats.Select(s =>
            $"{s.Id};{s.Dt.Ticks};{s.Level};{s.Pc};{s.Pch};{s.Tmg};{s.Ovrl};" +
            $"{s.PitchRightCount};{s.PitchWrongCount};{s.Key};{s.What};{s.Rand};" +
            $"{s.AccPct};{s.Hi};{s.Lo};{s.Instrument}"));

    public static IEnumerable<object[]> ColumnKeys =>
        SessionTableColumnDefinitions.All.Select(d => new object[] { d.ColumnKey });

    [Theory]
    [MemberData(nameof(ColumnKeys))]
    public void ShowSessionColumnDefinitions_OpensPopupForEveryHeading(string columnKey)
    {
        _viewModel.CloseSessionColumnDefinitionsCommand.Execute(null);

        _viewModel.ShowSessionColumnDefinitionsCommand.Execute(columnKey);

        Assert.True(_viewModel.IsSessionColumnDefinitionsVisible);
        Assert.Equal(columnKey, _viewModel.SessionColumnDefinitionsHighlightKey);
    }

    [Theory]
    [MemberData(nameof(ColumnKeys))]
    public void ShowSessionColumnDefinitions_ListsAllHeadingsAndDefinitions(string columnKey)
    {
        _viewModel.ShowSessionColumnDefinitionsCommand.Execute(columnKey);

        Assert.Equal(SessionTableColumnDefinitions.All.Count, _viewModel.SessionColumnDefinitionRows.Count);
        Assert.All(_viewModel.SessionColumnDefinitionRows, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Heading));
            Assert.False(string.IsNullOrWhiteSpace(row.Definition));
        });

        foreach (var expected in SessionTableColumnDefinitions.All)
        {
            Assert.Contains(
                _viewModel.SessionColumnDefinitionRows,
                row => row.ColumnKey == expected.ColumnKey
                       && row.Heading == expected.Heading
                       && row.Definition == expected.Definition);
        }
    }

    [Theory]
    [MemberData(nameof(ColumnKeys))]
    public void ShowSessionColumnDefinitions_HighlightsTappedHeadingOnly(string columnKey)
    {
        _viewModel.ShowSessionColumnDefinitionsCommand.Execute(columnKey);

        Assert.Single(
            _viewModel.SessionColumnDefinitionRows,
            row => row.IsHighlighted);
        Assert.Equal(
            columnKey,
            _viewModel.SessionColumnDefinitionRows.Single(row => row.IsHighlighted).ColumnKey);
    }

    [Fact]
    public void CloseSessionColumnDefinitions_DismissesPopup()
    {
        _viewModel.ShowSessionColumnDefinitionsCommand.Execute("Date");
        Assert.True(_viewModel.IsSessionColumnDefinitionsVisible);

        _viewModel.CloseSessionColumnDefinitionsCommand.Execute(null);

        Assert.False(_viewModel.IsSessionColumnDefinitionsVisible);
    }

    [Theory]
    [MemberData(nameof(ColumnKeys))]
    public void OpeningAndClosingDefinitions_DoesNotAlterSessionStats(string columnKey)
    {
        var before = CaptureSessionSnapshot(_viewModel);
        var countBefore = _viewModel.SessionStats.Count;

        _viewModel.ShowSessionColumnDefinitionsCommand.Execute(columnKey);
        _viewModel.CloseSessionColumnDefinitionsCommand.Execute(null);

        Assert.Equal(countBefore, _viewModel.SessionStats.Count);
        Assert.Equal(before, CaptureSessionSnapshot(_viewModel));
    }

    [Fact]
    public void ShowSessionColumnDefinitions_IgnoresUnknownColumnKey()
    {
        _viewModel.ShowSessionColumnDefinitionsCommand.Execute("Unknown");

        Assert.False(_viewModel.IsSessionColumnDefinitionsVisible);
        Assert.Empty(_viewModel.SessionColumnDefinitionRows);
    }
}
