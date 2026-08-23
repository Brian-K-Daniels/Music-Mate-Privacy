using musicmate.Services;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class ApplyLevelSettingsTests : IDisposable
{
    private const string ChildLevelPrefKey = "ChildPractice.Level";
    private readonly Dictionary<string, object?> _store = new();

    public ApplyLevelSettingsTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose()
    {
        SessionPreferences.TestStore = null;
    }

    private static NoteSessionService CreateSessionAtLevel(int level)
    {
        var session = new NoteSessionService
        {
            ScaleSelectionMode = ScaleSelectionMode.ByLevel,
        };
        DifficultyLevelMapper.ApplyLevelSettings(level, session, preserveUserPracticeSettings: false);
        return session;
    }

    [Fact]
    public void ApplyLevelSettings_UpdatesChildLevelUsedByGenerator()
    {
        var session = CreateSessionAtLevel(10);
        Assert.Equal(10, session.ChildLevel);

        DifficultyLevelMapper.ApplyLevelSettings(36, session);

        Assert.Equal(36, session.ChildLevel);
    }

    [Fact]
    public void ApplyLevelSettings_Level36_EnablesSixteenthNotes()
    {
        Assert.Equal("Sixteenth", ChildLevelProgression.SmallestNoteForLevel(36));

        var session = CreateSessionAtLevel(10);
        Assert.Equal("Quarter", session.SmallestRhythmNote);

        DifficultyLevelMapper.ApplyLevelSettings(36, session);

        Assert.Equal("Sixteenth", session.SmallestRhythmNote);
        Assert.Equal("Mixed", session.RhythmMode);
    }

    [Fact]
    public void ApplyLevelSettings_LevelDown_RemovesIneligibleFeatures()
    {
        var session = CreateSessionAtLevel(50);
        Assert.Equal("Sixteenth", session.SmallestRhythmNote);
        Assert.Equal("Simple", session.SyncopationSetting);
        Assert.True(session.AccidentalPercent > 0);

        DifficultyLevelMapper.ApplyLevelSettings(10, session);

        Assert.Equal(10, session.ChildLevel);
        Assert.Equal("Quarter", session.SmallestRhythmNote);
        Assert.Equal("Simple", session.RhythmMode);
        Assert.Equal("None", session.SyncopationSetting);
        Assert.Equal(0, session.AccidentalPercent);
        Assert.Equal(0, session.PracticeRestChancePercent);
        Assert.Equal(
            ChildLevelProgression.MaxIntervalForLevel(10),
            session.MaxMelodicIntervalSemitones);
    }

    [Fact]
    public void ApplyLevelSettings_UpdatesAllLevelDependentSettings()
    {
        var session = CreateSessionAtLevel(5);
        int priorTempo = session.Tempo;
        string priorMeter = session.MeterTimeSignature;

        DifficultyLevelMapper.ApplyLevelSettings(45, session);

        Assert.Equal(ChildLevelProgression.SmallestNoteForLevel(45), session.SmallestRhythmNote);
        Assert.Equal(ChildLevelProgression.SyncopationForLevel(45), session.SyncopationSetting);
        Assert.Equal(ChildLevelProgression.AccidentalPercentForLevel(45), session.AccidentalPercent);
        Assert.Equal(ChildLevelProgression.RestChancePercentForLevel(45), session.PracticeRestChancePercent);
        Assert.Equal(ChildLevelProgression.MaxIntervalForLevel(45), session.MaxMelodicIntervalSemitones);
        Assert.Equal(
            ChildLevelProgression.MeasureBatchSizeForLevel(45, ChildLevelProgression.NoteCountForLevel(45)),
            session.ChildMeasureBatchSize);
        Assert.Equal(
            ChildLevelProgression.RhythmVarietyPercentForLevel(45) > 0 ? "Mixed" : "Simple",
            session.RhythmMode);

        // Tempo / meter are intentionally independent of level.
        Assert.Equal(priorTempo, session.Tempo);
        Assert.Equal(priorMeter, session.MeterTimeSignature);
    }

    [Fact]
    public void ApplyLevelSettings_RaisesPropertyChangeNotifications()
    {
        var session = CreateSessionAtLevel(10);
        var changed = new HashSet<string>(StringComparer.Ordinal);
        session.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.PropertyName))
                changed.Add(e.PropertyName);
        };

        DifficultyLevelMapper.ApplyLevelSettings(50, session);

        Assert.Contains(nameof(NoteSessionService.ChildLevel), changed);
        Assert.Contains(nameof(NoteSessionService.SmallestRhythmNote), changed);
        Assert.Contains(nameof(NoteSessionService.SyncopationSetting), changed);
        Assert.Contains(nameof(NoteSessionService.AccidentalPercent), changed);
    }

    [Fact]
    public void ApplyLevelSettings_WithPersistedLevel_MatchesMusicPageContract()
    {
        var session = CreateSessionAtLevel(12);
        const int newLevel = 36;

        // Same order as MusicPage.ApplyChildLevelAndRefreshAsync: persist, then apply.
        SessionPreferences.Set(ChildLevelPrefKey, newLevel);
        DifficultyLevelMapper.ApplyLevelSettings(newLevel, session);

        Assert.Equal(newLevel, session.ChildLevel);
        Assert.Equal(newLevel, SessionPreferences.Get(ChildLevelPrefKey, 0));
        Assert.Equal("Sixteenth", session.SmallestRhythmNote);
    }

    [Fact]
    public void ApplyLevelSettings_DoesNotLeavePreviousLevelRhythmCached()
    {
        var session = CreateSessionAtLevel(40);
        Assert.Equal("Sixteenth", session.SmallestRhythmNote);

        DifficultyLevelMapper.ApplyLevelSettings(15, session);
        Assert.Equal("Quarter", session.SmallestRhythmNote);

        DifficultyLevelMapper.ApplyLevelSettings(40, session);
        Assert.Equal("Sixteenth", session.SmallestRhythmNote);
        Assert.Equal(ChildLevelProgression.SyncopationForLevel(40), session.SyncopationSetting);
    }

    [Fact]
    public void StalePreferenceRead_MustNotOverrideAppliedLevel()
    {
        // Reproduces the former Music-page bug: prefs still hold the old level while
        // ApplyLevelSettings has already advanced the session.
        var session = CreateSessionAtLevel(20);
        SessionPreferences.Set(ChildLevelPrefKey, 20);

        DifficultyLevelMapper.ApplyLevelSettings(36, session);
        Assert.Equal(36, session.ChildLevel);

        int staleFromPrefs = Math.Clamp(SessionPreferences.Get(ChildLevelPrefKey, session.ChildLevel), 1, 100);
        Assert.Equal(20, staleFromPrefs);

        // Correct source of truth is the session after ApplyLevelSettings.
        Assert.Equal(36, session.ChildLevel);
        Assert.Equal("Sixteenth", session.SmallestRhythmNote);
    }

    [Fact]
    public void ApplyLevelSettings_PreservesCustomizedPracticeSettingsWhenRequested()
    {
        var session = CreateSessionAtLevel(20);
        session.SmallestRhythmNote = "Eighth";
        session.AccidentalPercent = 7;
        session.MarkChildPracticeSettingsCustomized();

        DifficultyLevelMapper.ApplyLevelSettings(40, session, preserveUserPracticeSettings: true);

        Assert.Equal(40, session.ChildLevel);
        Assert.Equal("Eighth", session.SmallestRhythmNote);
        Assert.Equal(7, session.AccidentalPercent);
        Assert.Equal(ChildLevelProgression.MaxIntervalForLevel(40), session.MaxMelodicIntervalSemitones);
    }

    [Fact]
    public void ResolveSessionSettings_Level36_SixteenthsEligible()
    {
        var settings = DifficultyLevelMapper.ResolveSessionSettings(36, new Random(1));
        Assert.Equal("Sixteenth", settings.SmallestRhythmNote);
    }

    [Fact]
    public void ApplyLevelDerivedSettings_ResetsStaleNarrowRangeWhenNotCustomized()
    {
        // WhatToPlay → Music used to skip level-derived range apply when ChildLevel
        // was already set, leaving a stale narrow range and a tiny first scale walk.
        var session = new NoteSessionService
        {
            ChildLevel = 40,
            ScaleSelectionMode = ScaleSelectionMode.Named,
            SelectedScale = "Major",
            Key = "F",
        };
        session.ClearNoteRangeCustomization();
        session.LowestNote = "F3";
        session.HighestNote = "A3";
        session.ClearNoteRangeCustomization();

        DifficultyLevelMapper.ApplyLevelDerivedSettings(40, session);

        int lo = NoteSessionService.NoteNameToMidi(session.LowestNote);
        int hi = NoteSessionService.NoteNameToMidi(session.HighestNote);
        Assert.True(hi - lo >= 12, $"Expected at least one octave after level apply, got {session.LowestNote}-{session.HighestNote}");
        Assert.False(session.NoteRangeCustomized);
    }

    [Fact]
    public void ApplyLevelDerivedSettings_ShrinksRangeOnLevelDownWhenNotCustomized()
    {
        var session = new NoteSessionService { ChildLevel = 80 };
        session.ClearNoteRangeCustomization();
        DifficultyLevelMapper.ApplyLevelDerivedSettings(80, session);
        int wideSpan = NoteSessionService.NoteNameToMidi(session.HighestNote)
            - NoteSessionService.NoteNameToMidi(session.LowestNote);

        DifficultyLevelMapper.ApplyLevelDerivedSettings(5, session);
        int narrowSpan = NoteSessionService.NoteNameToMidi(session.HighestNote)
            - NoteSessionService.NoteNameToMidi(session.LowestNote);

        Assert.True(narrowSpan < wideSpan,
            $"Level-down should shrink automatic range ({session.LowestNote}-{session.HighestNote})");
    }
}
