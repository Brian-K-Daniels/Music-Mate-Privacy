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
}
