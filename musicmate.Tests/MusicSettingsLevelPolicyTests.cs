using musicmate.Services;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class MusicSettingsLevelPolicyTests : IDisposable
{
    private readonly Dictionary<string, object?> _sessionStore = new();
    private readonly Dictionary<string, string> _themeStore = new();

    public MusicSettingsLevelPolicyTests()
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
    public void FactoryDefault_IsMayBeChangedByLevel()
    {
        var session = new NoteSessionService();
        Assert.Equal(
            NoteSessionService.MusicSettingsLevelPolicyMayBeChangedByLevel,
            session.MusicSettingsLevelPolicy);
        Assert.True(session.AllowsLevelToChangeMusicSettings);
    }

    [Fact]
    public void Setter_PersistsAndRaisesPropertyChanged()
    {
        var session = new NoteSessionService();
        var changed = new HashSet<string>(StringComparer.Ordinal);
        session.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.PropertyName))
                changed.Add(e.PropertyName);
        };

        session.MusicSettingsLevelPolicy = NoteSessionService.MusicSettingsLevelPolicyFixed;
        Assert.Equal(NoteSessionService.MusicSettingsLevelPolicyFixed, session.MusicSettingsLevelPolicy);
        Assert.False(session.AllowsLevelToChangeMusicSettings);
        Assert.Contains(nameof(NoteSessionService.MusicSettingsLevelPolicy), changed);
        Assert.Contains(nameof(NoteSessionService.AllowsLevelToChangeMusicSettings), changed);
        Assert.Equal(
            NoteSessionService.MusicSettingsLevelPolicyFixed,
            SessionPreferences.Get(
                "musicmate.MusicSettingsLevelPolicy",
                NoteSessionService.DefaultMusicSettingsLevelPolicy));

        changed.Clear();
        session.MusicSettingsLevelPolicy = NoteSessionService.MusicSettingsLevelPolicyMayBeChangedByLevel;
        Assert.True(session.AllowsLevelToChangeMusicSettings);
        Assert.Contains(nameof(NoteSessionService.MusicSettingsLevelPolicy), changed);
    }

    [Fact]
    public void Setter_PersistsAcrossNewSessionInstance()
    {
        var first = new NoteSessionService();
        first.MusicSettingsLevelPolicy = NoteSessionService.MusicSettingsLevelPolicyFixed;

        var second = new NoteSessionService();
        Assert.Equal(NoteSessionService.MusicSettingsLevelPolicyFixed, second.MusicSettingsLevelPolicy);
        Assert.False(second.AllowsLevelToChangeMusicSettings);
    }

    [Fact]
    public void FactoryReset_RestoresMayBeChangedByLevel()
    {
        var session = new NoteSessionService();
        var theme = new ThemeService();
        theme.LoadFromPreferences();
        var reset = new SettingsResetService(session, theme);

        session.MusicSettingsLevelPolicy = NoteSessionService.MusicSettingsLevelPolicyFixed;
        Assert.False(session.AllowsLevelToChangeMusicSettings);

        reset.ResetToFactoryDefaults();
        Assert.Equal(
            NoteSessionService.MusicSettingsLevelPolicyMayBeChangedByLevel,
            session.MusicSettingsLevelPolicy);
        Assert.True(session.AllowsLevelToChangeMusicSettings);
    }

    [Fact]
    public void ChangingToFixed_MakesAreFactoryDefaultsAppliedFalse()
    {
        var session = new NoteSessionService();
        var theme = new ThemeService();
        theme.LoadFromPreferences();
        var reset = new SettingsResetService(session, theme);
        reset.ResetToFactoryDefaults();
        Assert.True(reset.AreFactoryDefaultsApplied);

        session.MusicSettingsLevelPolicy = NoteSessionService.MusicSettingsLevelPolicyFixed;
        reset.EvaluateAreFactoryDefaultsApplied();
        Assert.False(reset.AreFactoryDefaultsApplied);
    }

    [Fact]
    public void Fixed_ApplyLevelSettings_DoesNotAlterMusicPageSettings()
    {
        var session = CreateSessionAtLevel(10);
        session.AccidentalPercent = 12;
        session.SmallestRhythmNote = "Eighth";
        session.RhythmMode = "Mixed";
        session.SyncopationSetting = "Simple";
        session.MeterTimeSignature = "3/4";
        session.ClearNoteRangeCustomization();
        session.LowestNote = "C4";
        session.HighestNote = "C5";
        session.ClearNoteRangeCustomization();
        session.MusicSettingsLevelPolicy = NoteSessionService.MusicSettingsLevelPolicyFixed;
        string policyBefore = session.MusicSettingsLevelPolicy;

        DifficultyLevelMapper.ApplyLevelSettings(45, session);

        Assert.Equal(45, session.ChildLevel);
        Assert.Equal(12, session.AccidentalPercent);
        Assert.Equal("Eighth", session.SmallestRhythmNote);
        Assert.Equal("Mixed", session.RhythmMode);
        Assert.Equal("Simple", session.SyncopationSetting);
        Assert.Equal("3/4", session.MeterTimeSignature);
        Assert.Equal("C4", session.LowestNote);
        Assert.Equal("C5", session.HighestNote);
        Assert.Equal(policyBefore, session.MusicSettingsLevelPolicy);
        // Non-Music-page derived settings still follow the level.
        Assert.Equal(
            ChildLevelProgression.MaxIntervalForLevel(45),
            session.MaxMelodicIntervalSemitones);
    }

    [Fact]
    public void MayBeChangedByLevel_ApplyLevelSettings_StillAppliesMusicRules()
    {
        var session = CreateSessionAtLevel(10);
        Assert.Equal("Quarter", session.SmallestRhythmNote);
        session.MusicSettingsLevelPolicy =
            NoteSessionService.MusicSettingsLevelPolicyMayBeChangedByLevel;

        DifficultyLevelMapper.ApplyLevelSettings(45, session);

        Assert.Equal(ChildLevelProgression.SmallestNoteForLevel(45), session.SmallestRhythmNote);
        Assert.Equal(ChildLevelProgression.SyncopationForLevel(45), session.SyncopationSetting);
        Assert.Equal(ChildLevelProgression.AccidentalPercentForLevel(45), session.AccidentalPercent);
        Assert.Equal(
            ChildLevelProgression.RhythmVarietyPercentForLevel(45) > 0 ? "Mixed" : "Simple",
            session.RhythmMode);
        Assert.Equal(
            NoteSessionService.MusicSettingsLevelPolicyMayBeChangedByLevel,
            session.MusicSettingsLevelPolicy);
    }

    [Fact]
    public void Fixed_PickAndApplyToSession_DoesNotAlterMusicPageSettings()
    {
        var session = CreateSessionAtLevel(5);
        session.AccidentalPercent = 3;
        session.SmallestRhythmNote = "Quarter";
        session.RhythmMode = "Simple";
        session.SyncopationSetting = "None";
        session.ClearNoteRangeCustomization();
        session.LowestNote = "D4";
        session.HighestNote = "A4";
        session.ClearNoteRangeCustomization();
        session.MusicSettingsLevelPolicy = NoteSessionService.MusicSettingsLevelPolicyFixed;

        DifficultyLevelMapper.PickAndApplyToSession(40, session, new Random(1));

        Assert.Equal(3, session.AccidentalPercent);
        Assert.Equal("Quarter", session.SmallestRhythmNote);
        Assert.Equal("Simple", session.RhythmMode);
        Assert.Equal("None", session.SyncopationSetting);
        Assert.Equal("D4", session.LowestNote);
        Assert.Equal("A4", session.HighestNote);
        Assert.Equal(
            NoteSessionService.MusicSettingsLevelPolicyFixed,
            session.MusicSettingsLevelPolicy);
    }

    [Fact]
    public void Fixed_ApplyLevelDerivedSettings_DoesNotMoveNoteRange()
    {
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
        session.MusicSettingsLevelPolicy = NoteSessionService.MusicSettingsLevelPolicyFixed;

        DifficultyLevelMapper.ApplyLevelDerivedSettings(40, session);

        Assert.Equal("F3", session.LowestNote);
        Assert.Equal("A3", session.HighestNote);
        Assert.Equal(
            ChildLevelProgression.MaxIntervalForLevel(40),
            session.MaxMelodicIntervalSemitones);
        Assert.Equal(
            NoteSessionService.MusicSettingsLevelPolicyFixed,
            session.MusicSettingsLevelPolicy);
    }

    [Fact]
    public void Fixed_ChildLevelSetter_DoesNotMoveNoteRange()
    {
        var session = new NoteSessionService();
        session.ClearNoteRangeCustomization();
        session.LowestNote = "E4";
        session.HighestNote = "G4";
        session.ClearNoteRangeCustomization();
        session.MusicSettingsLevelPolicy = NoteSessionService.MusicSettingsLevelPolicyFixed;

        session.ChildLevel = 50;

        Assert.Equal(50, session.ChildLevel);
        Assert.Equal("E4", session.LowestNote);
        Assert.Equal("G4", session.HighestNote);
        Assert.Equal(
            NoteSessionService.MusicSettingsLevelPolicyFixed,
            session.MusicSettingsLevelPolicy);
    }

    [Fact]
    public void LevelChange_NeverChangesMusicSettingsLevelPolicy()
    {
        var session = CreateSessionAtLevel(10);
        session.MusicSettingsLevelPolicy = NoteSessionService.MusicSettingsLevelPolicyFixed;
        DifficultyLevelMapper.ApplyLevelSettings(60, session);
        Assert.Equal(
            NoteSessionService.MusicSettingsLevelPolicyFixed,
            session.MusicSettingsLevelPolicy);

        session.MusicSettingsLevelPolicy =
            NoteSessionService.MusicSettingsLevelPolicyMayBeChangedByLevel;
        DifficultyLevelMapper.ApplyLevelSettings(20, session);
        Assert.Equal(
            NoteSessionService.MusicSettingsLevelPolicyMayBeChangedByLevel,
            session.MusicSettingsLevelPolicy);

        DifficultyLevelMapper.PickAndApplyToSession(55, session, new Random(2));
        Assert.Equal(
            NoteSessionService.MusicSettingsLevelPolicyMayBeChangedByLevel,
            session.MusicSettingsLevelPolicy);
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
}
