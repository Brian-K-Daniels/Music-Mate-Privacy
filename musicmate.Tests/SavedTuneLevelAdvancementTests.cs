using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Saved Practice Tunes must not advance Child Level or accumulate in SessionResult
/// (the level-up progress pool). SessionStat and NoteAttempts still record normally.
/// </summary>
[Collection("SessionPreferences")]
public class SavedTuneLevelAdvancementTests : IDisposable
{
    static SavedTuneLevelAdvancementTests()
    {
        SQLitePCL.Batteries.Init();
    }

    private readonly Dictionary<string, object?> _sessionStore = new();
    private readonly string _sessionDbPath;
    private readonly string _resultDbPath;
    private readonly string _attemptDbPath;
    private readonly SessionDatabase _sessionDb;
    private readonly SessionResultDatabase _resultDb;
    private readonly NoteAttemptDatabase _attemptDb;

    public SavedTuneLevelAdvancementTests()
    {
        SessionPreferences.TestStore = _sessionStore;
        _sessionStore.Clear();

        // Make one qualifying by-level session enough to level-up when it should.
        SessionPreferences.Set("LevelUp.SessionCount", 1);
        SessionPreferences.Set("LevelUp.MinPitchPct", 1.0);
        SessionPreferences.Set("LevelUp.MinTimingPct", 1.0);
        SessionPreferences.Set("LevelUp.MinOverallPct", 1.0);
        SessionPreferences.Set("LevelUp.MinNotes", 1);
        SessionPreferences.Set("LevelUp.MinOverallAccuracyFloor", 1.0);
        SessionPreferences.Set("ChildPractice.Level", 1);
        LevelUpService.MarkCountSinceNow();

        _sessionDbPath = Path.Combine(Path.GetTempPath(), $"mm_saved_lvl_sess_{Guid.NewGuid():N}.db3");
        _resultDbPath = Path.Combine(Path.GetTempPath(), $"mm_saved_lvl_res_{Guid.NewGuid():N}.db3");
        _attemptDbPath = Path.Combine(Path.GetTempPath(), $"mm_saved_lvl_att_{Guid.NewGuid():N}.db3");
        _sessionDb = new SessionDatabase(_sessionDbPath);
        _resultDb = new SessionResultDatabase(_resultDbPath);
        _attemptDb = new NoteAttemptDatabase(_attemptDbPath);
        _sessionDb.InitializeAsync().GetAwaiter().GetResult();
        _resultDb.InitializeAsync().GetAwaiter().GetResult();
        _attemptDb.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        SessionPreferences.TestStore = null;
        try { if (File.Exists(_sessionDbPath)) File.Delete(_sessionDbPath); } catch { }
        try { if (File.Exists(_resultDbPath)) File.Delete(_resultDbPath); } catch { }
        try { if (File.Exists(_attemptDbPath)) File.Delete(_attemptDbPath); } catch { }
    }

    [Fact]
    public void CountsTowardLevelAdvancement_FalseForSavedTune_TrueForGenerated()
    {
        var saved = CreateCompletedSession(childLevel: 5);
        saved.SelectPracticeTune(BuildSavedPracticeTune("Saved Tune 1"));
        Assert.False(LevelUpService.CountsTowardLevelAdvancement(saved));
        Assert.True(LevelUpService.IsSavedTuneSession(saved));

        var generated = CreateCompletedSession(childLevel: 5);
        generated.Tune = "Selected Scale";
        Assert.True(LevelUpService.CountsTowardLevelAdvancement(generated));
        Assert.False(LevelUpService.IsSavedTuneSession(generated));

        var library = CreateCompletedSession(childLevel: 5);
        library.SelectPracticeTune(new PracticeTune("Mary Had a Little Lamb", TimeSignature.FourFour, "C"));
        Assert.True(LevelUpService.CountsTowardLevelAdvancement(library));
    }

    [Fact]
    public async Task CompletingSavedTune_DoesNotAdvanceLevel()
    {
        var session = CreateCompletedSession(childLevel: 1);
        session.SelectPracticeTune(BuildSavedPracticeTune("Saved Tune 1"));
        MarkAllNotesCorrect(session);

        var outcome = await PracticeSessionPersistence.SaveSessionStatAsync(
            session, _sessionDb, _resultDb, collectSessionStats: true, maxSessionDbSizeBytes: 10_000_000);

        Assert.Null(outcome.NewChildLevel);
        Assert.Equal(1, session.ChildLevel);
        Assert.Equal(1, SessionPreferences.Get("ChildPractice.Level", 0));
        Assert.Empty(await _resultDb.GetAllAsync());
    }

    [Fact]
    public async Task RepeatingSavedTune_DoesNotAccumulateTowardLevelAdvancement()
    {
        for (int i = 0; i < 5; i++)
        {
            var session = CreateCompletedSession(childLevel: 1);
            session.SelectPracticeTune(BuildSavedPracticeTune("Saved Tune 2"));
            MarkAllNotesCorrect(session);

            var outcome = await PracticeSessionPersistence.SaveSessionStatAsync(
                session, _sessionDb, _resultDb, collectSessionStats: true, maxSessionDbSizeBytes: 10_000_000);

            Assert.Null(outcome.NewChildLevel);
            Assert.Equal(1, session.ChildLevel);
        }

        Assert.Empty(await _resultDb.GetAllAsync());
        Assert.Equal(5, (await _sessionDb.GetAllAsync()).Count);
        Assert.Equal(1, SessionPreferences.Get("ChildPractice.Level", 0));
    }

    [Fact]
    public async Task ByLevelExercise_StillAdvancesProgressAsBefore()
    {
        var session = CreateCompletedSession(childLevel: 1);
        session.Tune = "Selected Scale";
        MarkAllNotesCorrect(session);

        var outcome = await PracticeSessionPersistence.SaveSessionStatAsync(
            session, _sessionDb, _resultDb, collectSessionStats: true, maxSessionDbSizeBytes: 10_000_000);

        Assert.Equal(2, outcome.NewChildLevel);
        Assert.Equal(2, session.ChildLevel);
        Assert.Equal(2, SessionPreferences.Get("ChildPractice.Level", 0));
        Assert.Single(await _resultDb.GetAllAsync());
    }

    [Fact]
    public async Task SavedTune_StillRecordsSessionStatAndNoteAttempts()
    {
        var session = CreateCompletedSession(childLevel: 3);
        session.SelectPracticeTune(BuildSavedPracticeTune("Saved Tune 3"));
        MarkAllNotesCorrect(session);

        var outcome = await PracticeSessionPersistence.SaveSessionStatAsync(
            session, _sessionDb, _resultDb, collectSessionStats: true, maxSessionDbSizeBytes: 10_000_000);

        Assert.False(outcome.Skipped);
        Assert.Equal("saved-tune", outcome.SkipReason);
        Assert.Null(outcome.NewChildLevel);

        var stats = await _sessionDb.GetAllAsync();
        Assert.Single(stats);
        Assert.Equal("Practice Tune", stats[0].Tune);
        Assert.Equal(3, stats[0].Level);

        // Note-attempt history is independent of level-up; still writable after a saved-tune save.
        await _attemptDb.SaveAttemptAsync(new NoteAttempt
        {
            SessionId = "saved-tune-session",
            ExpectedWrittenNoteName = "C4",
            WrittenNoteName = "C4",
            ActualDetectedNoteName = "C4",
            Instrument = session.InstrumentDisplayName,
            PitchCorrect = true,
            TimingCorrect = true,
            OverallCorrect = true,
            Level = session.ChildLevel,
        });

        var attempts = await _attemptDb.GetAllAsync();
        Assert.Single(attempts);
        Assert.Equal("C4", attempts[0].WrittenNoteName);
        Assert.Empty(await _resultDb.GetAllAsync());
    }

    private static PracticeTune BuildSavedPracticeTune(string title)
    {
        var tune = new PracticeTune(title, TimeSignature.FourFour, "C");
        var measure = tune.AppendMeasure();
        measure.AddNote(new MusicNote(60, "C4", NoteDuration.Whole));
        return tune;
    }

    private static NoteSessionService CreateCompletedSession(int childLevel)
    {
        int[] midis = [60, 62, 64, 65];
        var session = new NoteSessionService
        {
            Instrument = "concert-pitch",
            Tune = "Selected Scale",
            CooldownMs = 0,
            Tempo = 60,
            ChildLevel = childLevel,
            SampleRate = 44100,
            PitchWindowSize = 4096,
        };
        session.Reset();
        session.Instrument = "concert-pitch";
        session.ChildLevel = childLevel;
        session.CooldownMs = 0;
        session.Tempo = 60;
        session.MeterTimeSignature = "4/4";
        session.NotesToDraw.Clear();
        session.FeedbackViewModels.Clear();
        for (int i = 0; i < midis.Length; i++)
        {
            int midi = midis[i];
            session.NotesToDraw.Add(new NoteInfo
            {
                Midi = midi,
                Name = NoteSessionService.MidiToNoteName(midi, flats: false),
                TargetFreq = NoteSessionService.MidiToFreqPublic(midi),
                DurationBeats = 1,
                Duration = NoteDuration.Quarter,
                StartBeat = i,
                GateBeatsAfterPrevious = i == 0 ? 0 : 1,
            });
            session.FeedbackViewModels.Add(new FeedbackItem(i, 0, 0, false));
        }

        return session;
    }

    private static void MarkAllNotesCorrect(NoteSessionService session)
    {
        session.CorrectNoteIndices.Clear();
        for (int i = 0; i < session.NotesToDraw.Count; i++)
        {
            if (session.NotesToDraw[i].IsRest)
                continue;
            session.CorrectNoteIndices.Add(i);
            session.NoteFeedbacks[i] = (Wrong: 0, Cents: 0);
            session.FeedbackViewModels[i] = new FeedbackItem(i, 0, 0, true);
        }
    }
}
