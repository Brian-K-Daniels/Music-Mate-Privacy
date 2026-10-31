using musicmate.Services;

namespace musicmate.Tests;

public class RepeatSessionStartupTests
{
    [Fact]
    public void RepeatOn_AutoStartOn_NextSessionListens()
    {
        var startup = new RepeatSessionStartup();
        Assert.True(RepeatSessionStartup.ShouldAutoStartListening(true, true, "Major"));

        startup.StopPreviousListener();
        Assert.True(startup.TryBeginAutoStart(countInEnabled: true, conductorCuesEnabled: true));
        startup.CompleteAutoStart(pipelineArmed: true);

        Assert.True(startup.Listening);
        Assert.False(startup.WaitingForGo);
        Assert.Equal(1, startup.ActiveListeners);
        Assert.Equal(1, startup.StartCount);
        Assert.Equal(0, startup.CurrentNoteIndex);
        Assert.True(startup.FirstNoteIsActiveTarget);
    }

    [Fact]
    public void RepeatOn_AutoStartOff_WaitsForGo()
    {
        var startup = new RepeatSessionStartup();
        Assert.False(RepeatSessionStartup.ShouldAutoStartListening(true, false, "Major"));

        startup.StopPreviousListener();
        Assert.True(startup.WaitForGo(conductorCuesEnabled: true));

        Assert.False(startup.Listening);
        Assert.True(startup.WaitingForGo);
        Assert.Equal(0, startup.ActiveListeners);
        Assert.Equal(0, startup.StartCount);
        Assert.Equal(0, startup.CurrentNoteIndex);
        Assert.True(startup.ConductorCuesEnabled);
    }

    [Fact]
    public void RepeatedSessions_DoNotCreateDuplicateListeners()
    {
        var startup = new RepeatSessionStartup();
        startup.StopPreviousListener();
        Assert.True(startup.TryBeginAutoStart(countInEnabled: false, conductorCuesEnabled: false));
        startup.CompleteAutoStart(pipelineArmed: true);

        Assert.False(startup.TryBeginAutoStart(countInEnabled: false, conductorCuesEnabled: false));
        Assert.Equal(1, startup.ActiveListeners);
        Assert.Equal(1, startup.StartCount);
    }

    [Fact]
    public void PreviousListener_IsStoppedBeforeTheNextBegins()
    {
        var startup = new RepeatSessionStartup();
        startup.StopPreviousListener();
        Assert.True(startup.TryBeginAutoStart(countInEnabled: false, conductorCuesEnabled: false));
        startup.CompleteAutoStart(pipelineArmed: true);
        int stopsBeforeNext = startup.StopCount;

        Assert.False(startup.TryBeginAutoStart(countInEnabled: false, conductorCuesEnabled: false));

        startup.StopPreviousListener();
        Assert.True(startup.StopCount > stopsBeforeNext);
        Assert.Equal(0, startup.ActiveListeners);
        Assert.False(startup.Listening);

        Assert.True(startup.TryBeginAutoStart(countInEnabled: false, conductorCuesEnabled: false));
        startup.CompleteAutoStart(pipelineArmed: true);
        Assert.Equal(1, startup.ActiveListeners);
        Assert.Equal(2, startup.StartCount);
    }

    [Fact]
    public void RepeatedSession_DoesNotSkipFirstNote()
    {
        var startup = new RepeatSessionStartup();
        startup.StopPreviousListener();
        Assert.True(startup.TryBeginAutoStart(countInEnabled: true, conductorCuesEnabled: true));
        startup.CompleteAutoStart(pipelineArmed: true);

        Assert.Equal(0, startup.CurrentNoteIndex);
        Assert.True(startup.FirstNoteIsActiveTarget);
    }

    [Fact]
    public void CountInAndConductorCues_StillApplyWithoutConsumingTheFirstNote()
    {
        var startup = new RepeatSessionStartup();
        startup.StopPreviousListener();
        Assert.True(startup.TryBeginAutoStart(countInEnabled: true, conductorCuesEnabled: true));
        startup.CompleteAutoStart(pipelineArmed: true);

        startup.OnCountInClick();
        startup.OnCountInClick();

        Assert.True(startup.CountInArmed);
        Assert.True(startup.ConductorCuesEnabled);
        Assert.False(startup.CountInAdvancedFirstNote);
        Assert.Equal(0, startup.CurrentNoteIndex);
        Assert.True(startup.Listening);
    }

    [Fact]
    public void Tuner_DoesNotAutoStartFromRepeat()
    {
        Assert.False(RepeatSessionStartup.ShouldAutoStartListening(true, true, "Tuner"));
    }
}
