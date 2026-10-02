namespace musicmate.Services;

/// <summary>
/// Pure helpers for Music-page inactivity idle (battery). Does not kill the process —
/// callers stop mic/timers and leave the page showing Go for a normal restart.
/// </summary>
public static class InactivityIdleLogic
{
    /// <summary>Minutes of genuine inactivity before entering idle. Change in one place.</summary>
    public const int InactivityTimeoutMinutes = 10;

    public static TimeSpan Timeout { get; } = TimeSpan.FromMinutes(InactivityTimeoutMinutes);

    /// <summary>
    /// True when battery-draining work is active and neither intentional cue/playback
    /// nor recent activity (taps / detected playing) has occurred within <see cref="Timeout"/>.
    /// </summary>
    public static bool ShouldEnterIdle(
        DateTime utcNow,
        DateTime lastActivityUtc,
        bool hasBatteryDrainingWork,
        bool intentionalCueOrPlaybackActive)
    {
        if (!hasBatteryDrainingWork)
            return false;
        if (intentionalCueOrPlaybackActive)
            return false;
        return utcNow - lastActivityUtc >= Timeout;
    }
}
