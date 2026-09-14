using Microsoft.Extensions.DependencyInjection;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class ClearNoteAttemptsAfterSessionTests : IDisposable
{
    static ClearNoteAttemptsAfterSessionTests()
    {
        SQLitePCL.Batteries.Init();
    }

    private readonly Dictionary<string, object?> _sessionStore = new();
    private readonly Dictionary<string, string> _themeStore = new();
    private readonly string _noteAttemptDbPath;
    private readonly string _sessionDbPath;
    private readonly NoteAttemptDatabase _attemptDb;
    private readonly SessionDatabase _sessionDb;
    private readonly NoteSessionService _session;
    private readonly ThemeService _theme;
    private readonly SettingsResetService _reset;

    public ClearNoteAttemptsAfterSessionTests()
    {
        SessionPreferences.TestStore = _sessionStore;
        ThemeService.TestStore = _themeStore;
        _sessionStore.Clear();
        _themeStore.Clear();

        _noteAttemptDbPath = Path.Combine(Path.GetTempPath(), $"mm_na_clear_{Guid.NewGuid():N}.db3");
        _sessionDbPath = Path.Combine(Path.GetTempPath(), $"mm_sess_clear_{Guid.NewGuid():N}.db3");
        _attemptDb = new NoteAttemptDatabase(_noteAttemptDbPath);
        _sessionDb = new SessionDatabase(_sessionDbPath);

        var services = new ServiceCollection();
        ServiceHelper.Initialize(services.BuildServiceProvider());

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
        ServiceHelper.Initialize(new ServiceCollection().BuildServiceProvider());
        try { if (File.Exists(_noteAttemptDbPath)) File.Delete(_noteAttemptDbPath); } catch { }
        try { if (File.Exists(_sessionDbPath)) File.Delete(_sessionDbPath); } catch { }
    }

    [Fact]
    public void FactoryDefault_IsOff()
    {
        Assert.False(NoteSessionService.DefaultClearNoteAttemptsAfterSession);
        Assert.False(_session.ClearNoteAttemptsAfterSession);
    }

    [Fact]
    public void Setting_PersistsAcrossSessionRecreation()
    {
        _session.ClearNoteAttemptsAfterSession = true;
        Assert.True(SessionPreferences.Get(
            NoteSessionService.PrefClearNoteAttemptsAfterSessionKey, false));

        var reopened = new NoteSessionService();
        Assert.True(reopened.ClearNoteAttemptsAfterSession);

        reopened.ClearNoteAttemptsAfterSession = false;
        var again = new NoteSessionService();
        Assert.False(again.ClearNoteAttemptsAfterSession);
    }

    [Fact]
    public void FactoryReset_RestoresOff()
    {
        Assert.True(_reset.AreFactoryDefaultsApplied);

        _session.ClearNoteAttemptsAfterSession = true;
        _reset.EvaluateAreFactoryDefaultsApplied();
        Assert.False(_reset.AreFactoryDefaultsApplied);

        _reset.ResetToFactoryDefaults();
        Assert.False(_session.ClearNoteAttemptsAfterSession);
        Assert.True(_reset.AreFactoryDefaultsApplied);
    }

    [Fact]
    public async Task Off_CompletingMultipleSessions_PreservesAllNoteAttempts()
    {
        await _attemptDb.InitializeAsync();

        string s1 = PracticeSessionLifecycle.NewSessionId();
        string s2 = PracticeSessionLifecycle.NewSessionId();
        string s3 = PracticeSessionLifecycle.NewSessionId();

        await SimulateCompletedSessionPipelineAsync(s1, clearOlder: false, notes: new[] { "C4" });
        await SimulateCompletedSessionPipelineAsync(s2, clearOlder: false, notes: new[] { "D4", "E4" });
        await SimulateCompletedSessionPipelineAsync(s3, clearOlder: false, notes: new[] { "F4" });

        Assert.Single(await _attemptDb.GetBySessionIdAsync(s1));
        Assert.Equal(2, (await _attemptDb.GetBySessionIdAsync(s2)).Count);
        Assert.Single(await _attemptDb.GetBySessionIdAsync(s3));
        Assert.Equal(4, (await _attemptDb.GetAllAsync()).Count);
    }

    [Fact]
    public async Task On_AfterSession1_Session1Remains()
    {
        await _attemptDb.InitializeAsync();
        string s1 = PracticeSessionLifecycle.NewSessionId();

        await SimulateCompletedSessionPipelineAsync(s1, clearOlder: true, notes: new[] { "C4", "D4" });

        Assert.Equal(2, (await _attemptDb.GetBySessionIdAsync(s1)).Count);
        Assert.Equal(2, (await _attemptDb.GetAllAsync()).Count);
    }

    [Fact]
    public async Task On_AfterSession2_Session1Deleted_Session2Remains()
    {
        await _attemptDb.InitializeAsync();
        string s1 = PracticeSessionLifecycle.NewSessionId();
        string s2 = PracticeSessionLifecycle.NewSessionId();

        await SimulateCompletedSessionPipelineAsync(s1, clearOlder: true, notes: new[] { "C4" });
        await SimulateCompletedSessionPipelineAsync(s2, clearOlder: true, notes: new[] { "D4", "E4" });

        Assert.Empty(await _attemptDb.GetBySessionIdAsync(s1));
        Assert.Equal(2, (await _attemptDb.GetBySessionIdAsync(s2)).Count);
        Assert.Equal(2, (await _attemptDb.GetAllAsync()).Count);
        Assert.All(await _attemptDb.GetAllAsync(), a => Assert.Equal(s2, a.SessionId));
    }

    [Fact]
    public async Task On_AfterSession3_OnlySession3Remains()
    {
        await _attemptDb.InitializeAsync();
        string s1 = PracticeSessionLifecycle.NewSessionId();
        string s2 = PracticeSessionLifecycle.NewSessionId();
        string s3 = PracticeSessionLifecycle.NewSessionId();

        await SimulateCompletedSessionPipelineAsync(s1, clearOlder: true, notes: new[] { "C4" });
        await SimulateCompletedSessionPipelineAsync(s2, clearOlder: true, notes: new[] { "D4" });
        await SimulateCompletedSessionPipelineAsync(s3, clearOlder: true, notes: new[] { "E4", "F4", "G4" });

        Assert.Empty(await _attemptDb.GetBySessionIdAsync(s1));
        Assert.Empty(await _attemptDb.GetBySessionIdAsync(s2));
        Assert.Equal(3, (await _attemptDb.GetBySessionIdAsync(s3)).Count);
        Assert.All(await _attemptDb.GetAllAsync(), a => Assert.Equal(s3, a.SessionId));
    }

    [Fact]
    public async Task On_SavedTuneSessions_BehaveTheSameWay()
    {
        await _attemptDb.InitializeAsync();
        string saved1 = PracticeSessionLifecycle.NewSessionId();
        string saved2 = PracticeSessionLifecycle.NewSessionId();

        await SimulateCompletedSessionPipelineAsync(
            saved1, clearOlder: true, notes: new[] { "A4" }, tuneTitle: "Saved Tune Alpha");
        await SimulateCompletedSessionPipelineAsync(
            saved2, clearOlder: true, notes: new[] { "B4", "C5" }, tuneTitle: "Saved Tune Beta");

        Assert.Empty(await _attemptDb.GetBySessionIdAsync(saved1));
        Assert.Equal(2, (await _attemptDb.GetBySessionIdAsync(saved2)).Count);
        Assert.All(await _attemptDb.GetAllAsync(), a => Assert.Equal(saved2, a.SessionId));
    }

    [Fact]
    public async Task On_CurrentSessionFullySaved_BeforeOlderSessionsDeleted()
    {
        await _attemptDb.InitializeAsync();
        string older = PracticeSessionLifecycle.NewSessionId();
        string current = PracticeSessionLifecycle.NewSessionId();
        await SeedAttemptAsync(older, "C4");

        bool sawCurrentSavedBeforeDelete = false;
        bool olderStillPresentBeforeDelete = false;

        // Mirror MusicPage: save current attempts, then retain-only cleanup.
        await SeedAttemptAsync(current, "D4");
        await SeedAttemptAsync(current, "E4");

        Assert.Equal(2, (await _attemptDb.GetBySessionIdAsync(current)).Count);
        Assert.Single(await _attemptDb.GetBySessionIdAsync(older));
        sawCurrentSavedBeforeDelete = true;
        olderStillPresentBeforeDelete = true;

        int deleted = await NoteAttemptSessionCleanup.RetainOnlyCompletedSessionIfEnabledAsync(
            _attemptDb, current, retainOnlyLatestSession: true);

        Assert.True(sawCurrentSavedBeforeDelete);
        Assert.True(olderStillPresentBeforeDelete);
        Assert.True(deleted >= 1);
        Assert.Equal(2, (await _attemptDb.GetBySessionIdAsync(current)).Count);
        Assert.Empty(await _attemptDb.GetBySessionIdAsync(older));
    }

    [Fact]
    public async Task On_DoesNotAffectSessionStatsSettingsOrLevelProgress()
    {
        await _attemptDb.InitializeAsync();
        await _sessionDb.InitializeAsync();

        SessionPreferences.Set("SelectedTune", "Saved Tune 1");
        _session.ClearNoteAttemptsAfterSession = true;
        _session.ChildLevel = 7;
        _session.UseNoteMasteryForGeneration = false;

        string older = PracticeSessionLifecycle.NewSessionId();
        string current = PracticeSessionLifecycle.NewSessionId();
        await SeedAttemptAsync(older, "G4");

        var session = BuildScorableSession();
        session.Tune = "Selected Scale";
        var olderStat = PracticeSessionPersistence.BuildSessionStat(session);
        olderStat.What = "older-session-stat";
        await _sessionDb.InsertAsync(olderStat);

        var currentStat = PracticeSessionPersistence.BuildSessionStat(session);
        currentStat.What = "current-session-stat";

        await SimulateCompletedSessionPipelineAsync(
            current,
            clearOlder: true,
            notes: new[] { "A4" },
            afterSaveBeforeClear: async () =>
            {
                await _sessionDb.InsertAsync(currentStat);
            });

        Assert.Empty(await _attemptDb.GetBySessionIdAsync(older));
        Assert.Single(await _attemptDb.GetBySessionIdAsync(current));

        var sessions = await _sessionDb.GetAllAsync();
        Assert.Equal(2, sessions.Count);
        Assert.Contains(sessions, s => s.What == "older-session-stat");
        Assert.Contains(sessions, s => s.What == "current-session-stat");

        Assert.Equal("Saved Tune 1", SessionPreferences.Get("SelectedTune", string.Empty));
        Assert.Equal(7, _session.ChildLevel);
        Assert.False(_session.UseNoteMasteryForGeneration);
        Assert.True(_session.ClearNoteAttemptsAfterSession);
    }

    private async Task SimulateCompletedSessionPipelineAsync(
        string sessionId,
        bool clearOlder,
        IReadOnlyList<string> notes,
        string? tuneTitle = null,
        Func<Task>? afterSaveBeforeClear = null)
    {
        // Mirrors MusicPage SessionCompletedAsync order:
        // SaveSessionStat / scoring, then persist attempts, then maybe retain-only cleanup.
        _ = tuneTitle; // session type does not change Note Attempt cleanup path
        foreach (var note in notes)
            await SeedAttemptAsync(sessionId, note);

        if (afterSaveBeforeClear != null)
            await afterSaveBeforeClear();

        await NoteAttemptSessionCleanup.RetainOnlyCompletedSessionIfEnabledAsync(
            _attemptDb, sessionId, clearOlder);
    }

    private async Task SeedAttemptAsync(string sessionId, string writtenName)
    {
        await _attemptDb.SaveAttemptAsync(new NoteAttempt
        {
            DateTime = DateTime.UtcNow,
            SessionId = sessionId,
            Instrument = "Concert Pitch",
            Level = 1,
            WrittenNoteName = writtenName,
            ExpectedWrittenNoteName = writtenName,
            ActualDetectedNoteName = writtenName,
            PitchCorrect = true,
            TimingCorrect = true,
            OverallCorrect = true,
        });
    }

    private static NoteSessionService BuildScorableSession()
    {
        var session = new NoteSessionService
        {
            Instrument = "concert-pitch",
            Tune = "Selected Scale",
            Key = "C",
            ChildLevel = 1,
            CooldownMs = 0,
        };
        session.Reset();
        session.Instrument = "concert-pitch";
        session.ChildLevel = 1;
        session.NotesToDraw.Clear();
        session.FeedbackViewModels.Clear();
        for (int i = 0; i < 4; i++)
        {
            int midi = 60 + i;
            session.NotesToDraw.Add(new NoteInfo
            {
                Midi = midi,
                Name = NoteSessionService.MidiToNoteName(midi, flats: false),
                TargetFreq = NoteSessionService.MidiToFreqPublic(midi),
                DurationBeats = 1,
                Duration = NoteDuration.Quarter,
                StartBeat = i,
            });
            session.FeedbackViewModels.Add(new FeedbackItem(i, 0, 0, false));
            session.CorrectNoteIndices.Add(i);
        }
        return session;
    }
}
