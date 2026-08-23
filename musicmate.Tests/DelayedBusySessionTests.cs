using musicmate.Services;

namespace musicmate.Tests;

public class DelayedBusySessionTests
{
    [Fact]
    public void ShowDelay_IsOneSecond()
    {
        Assert.Equal(1000, DelayedBusySession.DefaultShowDelayMs);
        Assert.Equal(1000, NavigationBusyService.ShowDelayMs);
    }

    [Fact]
    public void BeginThenEnd_InvalidatesPendingShowGeneration()
    {
        var busy = new DelayedBusySession();
        int gen = busy.Begin(out bool ownsTimer);
        Assert.True(ownsTimer);
        Assert.True(busy.IsBusy);
        Assert.True(busy.IsCurrent(gen));

        Assert.True(busy.End(out int after));
        Assert.False(busy.IsBusy);
        Assert.False(busy.IsCurrent(gen));
        Assert.NotEqual(gen, after);
    }

    [Fact]
    public void NestedBegin_DoesNotStartSecondTimer_AndKeepsOriginalGeneration()
    {
        var busy = new DelayedBusySession();
        int outer = busy.Begin(out bool ownsOuter);
        int inner = busy.Begin(out bool ownsInner);

        Assert.True(ownsOuter);
        Assert.False(ownsInner);
        Assert.Equal(outer, inner);
        Assert.True(busy.IsCurrent(outer));

        Assert.False(busy.End(out _));
        Assert.True(busy.IsBusy);
        Assert.True(busy.IsCurrent(outer));

        Assert.True(busy.End(out _));
        Assert.False(busy.IsBusy);
        Assert.False(busy.IsCurrent(outer));
    }

    [Fact]
    public void Reset_ClearsDepthAndInvalidatesOldGeneration()
    {
        var busy = new DelayedBusySession();
        int gen = busy.Begin(out _);
        busy.Begin(out _);
        int afterReset = busy.Reset();

        Assert.False(busy.IsBusy);
        Assert.False(busy.IsCurrent(gen));
        Assert.NotEqual(gen, afterReset);
    }

    [Fact]
    public void RapidBeginEndBegin_OldDelayedShowMustNotWin()
    {
        var busy = new DelayedBusySession();
        int first = busy.Begin(out _);
        busy.End(out _);
        int second = busy.Begin(out bool ownsSecond);

        Assert.True(ownsSecond);
        Assert.False(busy.IsCurrent(first));
        Assert.True(busy.IsCurrent(second));
    }

    [Fact]
    public void EndWhenIdle_DoesNotThrowAndStaysIdle()
    {
        var busy = new DelayedBusySession();
        Assert.True(busy.End(out _));
        Assert.False(busy.IsBusy);
    }
}
