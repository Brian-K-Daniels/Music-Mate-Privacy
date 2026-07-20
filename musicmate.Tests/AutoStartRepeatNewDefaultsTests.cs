using musicmate.Services;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class AutoStartRepeatNewDefaultsTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public AutoStartRepeatNewDefaultsTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose()
    {
        SessionPreferences.TestStore = null;
    }

    [Fact]
    public void FreshSession_DefaultsToAutoStartAndRepeatNew()
    {
        var session = new NoteSessionService();

        Assert.True(session.AutoStart);
        Assert.True(session.AutoRepeat);
        Assert.False(session.RepeatSameTune);
    }

    [Fact]
    public void EnableAutoStartWithRepeatNew_TurnsOnListeningAndRepeatNew()
    {
        var session = new NoteSessionService
        {
            AutoStart = false,
            AutoRepeat = false,
            RepeatSameTune = true,
        };

        session.EnableAutoStartWithRepeatNew();

        Assert.True(session.AutoStart);
        Assert.True(session.AutoRepeat);
        Assert.False(session.RepeatSameTune);
    }
}
