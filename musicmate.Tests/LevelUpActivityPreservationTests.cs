using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Level-up must advance ChildLevel and level-driven difficulty without switching
/// the user's chosen activity (Named scale, Assortment, arpeggio, etc.).
/// </summary>
[Collection("SessionPreferences")]
public class LevelUpActivityPreservationTests : IDisposable
{
    static LevelUpActivityPreservationTests()
    {
        SQLitePCL.Batteries.Init();
    }

    private readonly Dictionary<string, object?> _sessionStore = new();
    private readonly string _sessionDbPath;
    private readonly string _resultDbPath;
    private readonly SessionDatabase _sessionDb;
    private readonly SessionResultDatabase _resultDb;

    public LevelUpActivityPreservationTests()
    {
        SessionPreferences.TestStore = _sessionStore;
        _sessionStore.Clear();

        SessionPreferences.Set("LevelUp.SessionCount", 1);
        SessionPreferences.Set("LevelUp.MinPitchPct", 1.0);
        SessionPreferences.Set("LevelUp.MinTimingPct", 1.0);
        SessionPreferences.Set("LevelUp.MinOverallPct", 1.0);
        SessionPreferences.Set("LevelUp.MinNotes", 1);
        SessionPreferences.Set("LevelUp.MinOverallAccuracyFloor", 1.0);
        SessionPreferences.Set("ChildPractice.Level", 60);
        LevelUpService.MarkCountSinceNow();

        _sessionDbPath = Path.Combine(Path.GetTempPath(), $"mm_lvl_act_sess_{Guid.NewGuid():N}.db3");
        _resultDbPath = Path.Combine(Path.GetTempPath(), $"mm_lvl_act_res_{Guid.NewGuid():N}.db3");
        _sessionDb = new SessionDatabase(_sessionDbPath);
        _resultDb = new SessionResultDatabase(_resultDbPath);
        _sessionDb.InitializeAsync().GetAwaiter().GetResult();
        _resultDb.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        SessionPreferences.TestStore = null;
        try { if (File.Exists(_sessionDbPath)) File.Delete(_sessionDbPath); } catch { }
        try { if (File.Exists(_resultDbPath)) File.Delete(_resultDbPath); } catch { }
    }

    [Fact]
    public void ApplyLevelUp_KeepsBluesScaleNamed()
    {
        var session = CreateScaleSession(60, "Blues");
        DifficultyLevelMapper.ApplyLevelUpToSession(61, session, out var warning);

        Assert.Null(warning);
        Assert.Equal(ScaleSelectionMode.Named, session.ScaleSelectionMode);
        Assert.Equal("Blues", session.SelectedScale);
        Assert.Equal("Selected Scale", session.Tune);
        Assert.False(session.IsRandomMode);
        Assert.Equal(ChildLevelProgression.AccidentalPercentForLevel(61), session.AccidentalPercent);
    }

    [Fact]
    public void ApplyLevelUp_KeepsAnotherNamedScale()
    {
        var session = CreateScaleSession(70, "Mixolydian");
        DifficultyLevelMapper.ApplyLevelUpToSession(71, session, out var warning);

        Assert.Null(warning);
        Assert.Equal(ScaleSelectionMode.Named, session.ScaleSelectionMode);
        Assert.Equal("Mixolydian", session.SelectedScale);
        Assert.False(session.IsRandomMode);
        Assert.Equal("Selected Scale", session.Tune);
    }

    [Fact]
    public void ApplyLevelUp_KeepsAssortmentByLevel()
    {
        var session = CreateScaleSession(20, "Natural Minor");
        session.ScaleSelectionMode = ScaleSelectionMode.ByLevel;
        session.IsRandomMode = false;
        session.TryApplyScalePickerSelection(NoteSessionService.ScaleSelectionByLevel, out _);

        DifficultyLevelMapper.ApplyLevelUpToSession(21, session, out var warning);

        Assert.Null(warning);
        Assert.Equal(ScaleSelectionMode.ByLevel, session.ScaleSelectionMode);
        Assert.Equal("Selected Scale", session.Tune);
        Assert.False(session.IsRandomMode);
        Assert.Equal(
            ChildLevelProgression.GetDefaultScaleForLevel(21),
            session.SelectedScale);
    }

    [Fact]
    public void ApplyLevelUp_KeepsArpeggioActivity()
    {
        var session = CreateScaleSession(40, "Major");
        Assert.True(ArpeggioCatalog.TryResolveQuality("Major triad", out var pattern));
        session.ApplyArpeggioQuality(pattern);
        Assert.Equal("Arpeggio", session.Tune);

        DifficultyLevelMapper.ApplyLevelUpToSession(41, session, out var warning);

        Assert.Null(warning);
        Assert.Equal("Arpeggio", session.Tune);
        Assert.False(session.IsRandomMode);
        Assert.Equal(ScaleSelectionMode.Named, session.ScaleSelectionMode);
        Assert.Equal("Major", session.SelectedScale);
    }

    [Fact]
    public void ApplyLevelUp_KeepsPracticeTuneActivity()
    {
        var session = CreateScaleSession(25, "Major");
        var tune = new PracticeTune("Mary Had a Little Lamb", TimeSignature.FourFour, "C");
        var measure = tune.AppendMeasure();
        measure.AddNote(new MusicNote(60, "C4", NoteDuration.Whole));
        session.SelectPracticeTune(tune);
        Assert.Equal("Practice Tune", session.Tune);

        DifficultyLevelMapper.ApplyLevelUpToSession(26, session, out var warning);

        Assert.Null(warning);
        Assert.Equal("Practice Tune", session.Tune);
        Assert.NotNull(session.CurrentTune);
        Assert.Equal("Mary Had a Little Lamb", session.CurrentTune!.Title);
    }

    [Fact]
    public void ApplyLevelUp_KeepsRhythmNoteTuneActivity()
    {
        var session = CreateScaleSession(15, "Major");
        session.Tune = PlayModePickerOptions.HalfThroughSixteenthNotes;
        session.IsRandomMode = false;
        session.ScaleSelectionMode = ScaleSelectionMode.Named;
        session.SelectedScale = "Major";

        DifficultyLevelMapper.ApplyLevelUpToSession(16, session, out var warning);

        Assert.Null(warning);
        Assert.Equal(PlayModePickerOptions.HalfThroughSixteenthNotes, session.Tune);
        Assert.Equal(ScaleSelectionMode.Named, session.ScaleSelectionMode);
        Assert.Equal("Major", session.SelectedScale);
    }

    [Fact]
    public void ApplyLevelUp_InvalidNamedScale_ExplainsWithoutSwitchingToAssortment()
    {
        var session = CreateScaleSession(10, "Locrian");
        Assert.False(ChildLevelProgression.IsScaleAllowedAtLevel(11, "Locrian"));

        DifficultyLevelMapper.ApplyLevelUpToSession(11, session, out var warning);

        Assert.NotNull(warning);
        Assert.Contains("Locrian", warning);
        Assert.Equal(ScaleSelectionMode.Named, session.ScaleSelectionMode);
        Assert.Equal("Locrian", session.SelectedScale);
        Assert.Equal("Selected Scale", session.Tune);
        Assert.False(session.IsRandomMode);
    }

    [Fact]
    public async Task CompletingBluesScale_AdvancesLevel_AndKeepsBlues()
    {
        var session = CreateCompletedSession(childLevel: 60);
        session.ScaleSelectionMode = ScaleSelectionMode.Named;
        session.SelectedScale = "Blues";
        session.IsRandomMode = false;
        session.Tune = "Selected Scale";
        session.Key = "C";
        MarkAllNotesCorrect(session);

        var outcome = await PracticeSessionPersistence.SaveSessionStatAsync(
            session, _sessionDb, _resultDb, collectSessionStats: true, maxSessionDbSizeBytes: 10_000_000);

        Assert.Equal(61, outcome.NewChildLevel);
        Assert.Equal(61, session.ChildLevel);
        Assert.Equal(61, SessionPreferences.Get("ChildPractice.Level", 0));
        Assert.Null(outcome.ActivityWarning);
        Assert.Equal(ScaleSelectionMode.Named, session.ScaleSelectionMode);
        Assert.Equal("Blues", session.SelectedScale);
        Assert.Equal("Selected Scale", session.Tune);
        Assert.False(session.IsRandomMode);
    }

    [Fact]
    public async Task CompletingAssortment_AdvancesLevel_AndKeepsAssortment()
    {
        var session = CreateCompletedSession(childLevel: 60);
        session.ScaleSelectionMode = ScaleSelectionMode.ByLevel;
        session.IsRandomMode = false;
        session.Tune = "Selected Scale";
        session.TryApplyScalePickerSelection(NoteSessionService.ScaleSelectionByLevel, out _);
        MarkAllNotesCorrect(session);

        var outcome = await PracticeSessionPersistence.SaveSessionStatAsync(
            session, _sessionDb, _resultDb, collectSessionStats: true, maxSessionDbSizeBytes: 10_000_000);

        Assert.Equal(61, outcome.NewChildLevel);
        Assert.Equal(61, session.ChildLevel);
        Assert.Equal(ScaleSelectionMode.ByLevel, session.ScaleSelectionMode);
        Assert.Equal("Selected Scale", session.Tune);
        Assert.Equal(
            ChildLevelProgression.GetDefaultScaleForLevel(61),
            session.SelectedScale);
    }

    [Fact]
    public void FormatSessionResultBanner_IncludesActivityWarningWhenPresent()
    {
        var stats = new PracticeSessionLifecycle.CompletionSummaryStats(8, 2, 80, 120);
        var text = PracticeSessionLifecycle.FormatSessionResultBanner(
            stats, newChildLevel: 18, activityWarning: "Locrian is not in the level 18 scale pool.");
        Assert.Contains("Level 18", text);
        Assert.Contains("Locrian", text);
    }

    private static NoteSessionService CreateScaleSession(int level, string namedScale)
    {
        var session = new NoteSessionService
        {
            Instrument = "concert-pitch",
            Tune = "Selected Scale",
            ChildLevel = level,
            Key = "C",
            ScaleSelectionMode = ScaleSelectionMode.Named,
            SelectedScale = namedScale,
            IsRandomMode = false,
            AccidentalPercent = ChildLevelProgression.AccidentalPercentForLevel(level),
        };
        session.TryApplyScalePickerSelection(namedScale, out _);
        session.IsRandomMode = false;
        session.Tune = "Selected Scale";
        return session;
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
