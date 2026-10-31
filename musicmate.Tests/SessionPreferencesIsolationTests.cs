using musicmate.Services;

namespace musicmate.Tests;

public class SessionPreferencesIsolationTests
{
    [Fact]
    public void TestStore_DoesNotLeakAcrossThreads()
    {
        var store = new Dictionary<string, object?>
        {
            ["musicmate.Tune"] = PlayModePickerOptions.Tuner,
        };
        SessionPreferences.TestStore = store;
        try
        {
            string? otherTune = null;
            bool otherSawThisStore = true;

            TestExecutionIsolation.RunOnFreshThread(() =>
            {
                otherSawThisStore = ReferenceEquals(SessionPreferences.TestStore, store);
                var other = new Dictionary<string, object?>
                {
                    ["musicmate.Tune"] = "Practice Tune",
                };
                SessionPreferences.TestStore = other;
                otherTune = SessionPreferences.Get("musicmate.Tune", "missing");
            });

            Assert.False(otherSawThisStore);
            Assert.Equal("Practice Tune", otherTune);
            Assert.Same(store, SessionPreferences.TestStore);
            Assert.Equal(PlayModePickerOptions.Tuner, SessionPreferences.Get("musicmate.Tune", "missing"));

            var session = new NoteSessionService();
            Assert.Equal(PlayModePickerOptions.Tuner, session.Tune);
        }
        finally
        {
            SessionPreferences.TestStore = null;
        }
    }

    [Fact]
    public void AppRestart_PersistedTunerTune_StaysIsolatedFromParallelPracticeTuneStore()
    {
        PlayModePickerOptions.ClearExplicitTunerOpen();
        var store = new Dictionary<string, object?>
        {
            ["musicmate.Tune"] = PlayModePickerOptions.Tuner,
        };
        string? saved = "Major";
        SessionPreferences.TestStore = store;
        try
        {
            TestExecutionIsolation.RunOnFreshThread(() =>
            {
                SessionPreferences.TestStore = new Dictionary<string, object?>
                {
                    ["musicmate.Tune"] = "Practice Tune",
                };
                _ = new NoteSessionService();
            });

            var session = new NoteSessionService();
            Assert.Equal(PlayModePickerOptions.Tuner, session.Tune);

            bool showTuner = PlayModePickerOptions.PrepareMusicPageForNavigation(
                session, () => saved, value => saved = value);

            Assert.False(showTuner);
            Assert.NotEqual(PlayModePickerOptions.Tuner, session.Tune);

            var restarted = new NoteSessionService();
            Assert.NotEqual(PlayModePickerOptions.Tuner, restarted.Tune);
        }
        finally
        {
            SessionPreferences.TestStore = null;
            PlayModePickerOptions.ClearExplicitTunerOpen();
        }
    }
}
