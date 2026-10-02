using musicmate.Services;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class IntervalSightTrainingLevelTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public IntervalSightTrainingLevelTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Fact]
    public void LoadPersistedLevel_DefaultsToOne_NotMusicChildLevel()
    {
        SessionPreferences.Set("ChildPractice.Level", 12);
        Assert.Equal(1, IntervalSightTrainingLogic.LoadPersistedLevel());
        Assert.Equal(
            IntervalSightTrainingLogic.DefaultLevel,
            SessionPreferences.Get(IntervalSightTrainingLogic.LevelPreferenceKey, IntervalSightTrainingLogic.DefaultLevel));
    }

    [Fact]
    public void PersistLevel_DoesNotChangeMusicChildPracticeLevel()
    {
        SessionPreferences.Set("ChildPractice.Level", 12);
        IntervalSightTrainingLogic.PersistLevel(4);

        Assert.Equal(4, IntervalSightTrainingLogic.LoadPersistedLevel());
        Assert.Equal(12, SessionPreferences.Get("ChildPractice.Level", 1));
    }

    [Fact]
    public void MusicChildPracticeLevel_DoesNotChangeSightTrainingLevel()
    {
        IntervalSightTrainingLogic.PersistLevel(4);
        SessionPreferences.Set("ChildPractice.Level", 12);
        SessionPreferences.Set("ChildPractice.Level", 40);

        Assert.Equal(4, IntervalSightTrainingLogic.LoadPersistedLevel());
    }

    [Fact]
    public void GenerateWithoutOverride_UsesPersistedSightLevel_NotSessionChildLevel()
    {
        SessionPreferences.Set("ChildPractice.Level", 12);
        IntervalSightTrainingLogic.PersistLevel(4);

        var session = new NoteSessionService
        {
            ChildLevel = 12,
            LowestNote = "C4",
            HighestNote = "C6",
            AccidentalPercent = 0,
            Key = "C",
            SelectedScale = "Major",
        };

        var exercise = IntervalSightTrainingSequenceBuilder.Generate(
            session, measureCount: null, randomSeed: 11, excludeFirstMagnitude: null);

        Assert.Equal(
            IntervalSightTrainingMelody.MaxSightIntervalForLevel(4),
            exercise.MaxAbsoluteSemitones);
        Assert.Equal(12, session.ChildLevel);
    }

    [Fact]
    public void PreferenceKey_IsDistinctFromMusicLevel()
    {
        Assert.Equal("musicmate.SightTrainingLevel", IntervalSightTrainingLogic.LevelPreferenceKey);
        Assert.NotEqual("ChildPractice.Level", IntervalSightTrainingLogic.LevelPreferenceKey);
    }

    [Fact]
    public void ChangingLevel_ImmediatelyChangesAllowedIntervalSetAndExercise()
    {
        // Models OnLevelSliderValueChanged → StartNewSessionAsync(childLevelOverride: newLevel).
        var session = new NoteSessionService
        {
            ChildLevel = 12,
            LowestNote = "C4",
            HighestNote = "C6",
            AccidentalPercent = 0,
            Key = "C",
            SelectedScale = "Major",
        };

        const int levelLow = 1;
        const int levelHigh = 100;
        int maxLow = IntervalSightTrainingMelody.MaxSightIntervalForLevel(levelLow);
        int maxHigh = IntervalSightTrainingMelody.MaxSightIntervalForLevel(levelHigh);
        Assert.True(maxHigh > maxLow);

        var before = IntervalSightTrainingSequenceBuilder.Generate(
            session, measureCount: null, randomSeed: 42, childLevelOverride: levelLow);
        Assert.Equal(maxLow, before.MaxAbsoluteSemitones);
        Assert.All(
            IntervalSightTrainingMelody.CollectMagnitudes(before.Notes),
            m => Assert.InRange(m, 0, maxLow));

        // Immediate level change: regenerate with the new override (same path as page load / New).
        IntervalSightTrainingLogic.PersistLevel(levelHigh);
        var after = IntervalSightTrainingSequenceBuilder.Generate(
            session, measureCount: null, randomSeed: 42, childLevelOverride: levelHigh);

        Assert.Equal(maxHigh, after.MaxAbsoluteSemitones);
        Assert.Equal(levelHigh, IntervalSightTrainingLogic.LoadPersistedLevel());
        Assert.All(
            IntervalSightTrainingMelody.CollectMagnitudes(after.Notes),
            m => Assert.InRange(m, 0, maxHigh));
        Assert.Contains(
            IntervalSightTrainingMelody.CollectMagnitudes(after.Notes),
            m => m > maxLow);
        // Session Music level / instrument settings are not mutated by Sight Level.
        Assert.Equal(12, session.ChildLevel);
    }

    [Theory]
    [InlineData(1, 11)]
    [InlineData(20, 41)]
    [InlineData(60, 100)]
    public void ChangingLevelAcrossBands_ChangesMaxAbsoluteSemitones(int fromLevel, int toLevel)
    {
        var session = new NoteSessionService
        {
            ChildLevel = 1,
            LowestNote = "C4",
            HighestNote = "C5",
            AccidentalPercent = 0,
        };

        var from = IntervalSightTrainingSequenceBuilder.Generate(
            session, randomSeed: 7, childLevelOverride: fromLevel);
        var to = IntervalSightTrainingSequenceBuilder.Generate(
            session, randomSeed: 7, childLevelOverride: toLevel);

        Assert.Equal(
            IntervalSightTrainingMelody.MaxSightIntervalForLevel(fromLevel),
            from.MaxAbsoluteSemitones);
        Assert.Equal(
            IntervalSightTrainingMelody.MaxSightIntervalForLevel(toLevel),
            to.MaxAbsoluteSemitones);
        Assert.NotEqual(from.MaxAbsoluteSemitones, to.MaxAbsoluteSemitones);
    }
}
