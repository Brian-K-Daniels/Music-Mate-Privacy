using musicmate.Services;

namespace musicmate.Tests;

public class InactivityIdleLogicTests
{
    [Fact]
    public void Timeout_IsTenMinutesConstant()
    {
        Assert.Equal(10, InactivityIdleLogic.InactivityTimeoutMinutes);
        Assert.Equal(TimeSpan.FromMinutes(10), InactivityIdleLogic.Timeout);
    }

    [Fact]
    public void ShouldEnterIdle_False_WhenNoBatteryWork()
    {
        var now = DateTime.UtcNow;
        Assert.False(InactivityIdleLogic.ShouldEnterIdle(
            now, now.AddMinutes(-30),
            hasBatteryDrainingWork: false,
            intentionalCueOrPlaybackActive: false));
    }

    [Fact]
    public void ShouldEnterIdle_False_WhenCueOrPlaybackActive()
    {
        var now = DateTime.UtcNow;
        Assert.False(InactivityIdleLogic.ShouldEnterIdle(
            now, now.AddMinutes(-30),
            hasBatteryDrainingWork: true,
            intentionalCueOrPlaybackActive: true));
    }

    [Fact]
    public void ShouldEnterIdle_False_WhenActivityRecent()
    {
        var now = DateTime.UtcNow;
        Assert.False(InactivityIdleLogic.ShouldEnterIdle(
            now, now.AddMinutes(-9),
            hasBatteryDrainingWork: true,
            intentionalCueOrPlaybackActive: false));
    }

    [Fact]
    public void ShouldEnterIdle_True_WhenQuietListeningPastTimeout()
    {
        var now = DateTime.UtcNow;
        Assert.True(InactivityIdleLogic.ShouldEnterIdle(
            now, now.AddMinutes(-10).AddSeconds(-1),
            hasBatteryDrainingWork: true,
            intentionalCueOrPlaybackActive: false));
    }
}
