namespace musicmate.Services;

/// <summary>
/// Cross-platform hook for any screen touch / pointer down so inactivity idle can
/// treat taps as activity without stealing control gestures.
/// </summary>
public static class UserInteractionProbe
{
    public static event Action? InteractionDetected;

    public static void NotifyInteraction()
    {
        try { InteractionDetected?.Invoke(); }
        catch { /* best-effort */ }
    }
}
