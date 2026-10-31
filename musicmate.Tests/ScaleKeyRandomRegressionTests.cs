using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Former DEBUG in-app ScaleKeyRandomTests.RunSelfChecks — keep as xUnit only.
/// </summary>
[Collection("SessionPreferences")]
public class ScaleKeyRandomRegressionTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public ScaleKeyRandomRegressionTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Fact]
    public void RandomMode_GoUsuallyChangesScaleOrKey()
    {
        var session = new NoteSessionService
        {
            ChildLevel = 42,
            ScaleSelectionMode = ScaleSelectionMode.Random,
            Key = "C",
            SelectedScale = "Major",
        };
        session.SelectedScale = "Major";
        session.PrepareEffectiveScaleForGeneration(0);

        bool anyChange = false;
        for (int seed = 1; seed <= 20; seed++)
        {
            session.PrepareFreshScaleAndKeyForGeneration("GoButton", repeatSame: false, seed);
            if (session.EffectiveScale != "Major" || session.Key != "C")
                anyChange = true;
        }

        Assert.True(anyChange);
    }

    [Fact]
    public void NamedMode_GoKeepsUserScaleAndKey()
    {
        var session = new NoteSessionService
        {
            ChildLevel = 42,
            ScaleSelectionMode = ScaleSelectionMode.Named,
            SelectedScale = "Major",
            Tune = "Selected Scale",
            IsRandomMode = false,
            Key = "C",
        };
        session.SelectedScale = "Major";
        session.PrepareEffectiveScaleForGeneration(0);

        session.PrepareFreshScaleAndKeyForGeneration("GoButton", repeatSame: false, 99);
        Assert.Equal("Major", session.EffectiveScale);
        Assert.Equal("C", session.Key);
    }

    [Fact]
    public void RepeatSame_PreservesScaleAndKey()
    {
        var session = new NoteSessionService
        {
            ChildLevel = 42,
            ScaleSelectionMode = ScaleSelectionMode.Random,
            Key = "C",
            SelectedScale = "Major",
        };
        session.SelectedScale = "Major";
        session.PrepareEffectiveScaleForGeneration(0);

        session.PrepareFreshScaleAndKeyForGeneration("AutoStart", repeatSame: true, 7);
        Assert.Equal("Major", session.EffectiveScale);
        Assert.Equal("C", session.Key);
    }

    [Fact]
    public void Level1_RandomPicks_StayInPool()
    {
        var session = new NoteSessionService
        {
            ChildLevel = 1,
            ScaleSelectionMode = ScaleSelectionMode.Random,
            Key = "C",
        };

        var allowedScales = ChildLevelProgression.GetAllowedScalesForLevel(1);
        var allowedKeys = ChildLevelProgression.GetAllowedKeys(1);

        for (int seed = 0; seed < 30; seed++)
        {
            session.PrepareFreshScaleAndKeyForGeneration("GoButton", repeatSame: false, seed + 500);
            Assert.Contains(session.EffectiveScale, allowedScales);
            Assert.Contains(session.Key, allowedKeys);
        }
    }
}
