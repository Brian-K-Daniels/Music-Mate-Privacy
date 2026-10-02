namespace musicmate.Services;

/// <summary>
/// Back navigation must not open Tuner, including through shell history.
/// When it would, the Music page opens on Other = Assortment by Level.
/// </summary>
public static class BackNavigationTunerPolicy
{
    public const string MusicPageTarget = "//MusicPage";

    public static bool WouldOpenTuner(
        string? sessionTune,
        bool explicitTunerOpenPending,
        string? navigationTarget)
    {
        if (IsTunerEntryTarget(navigationTarget))
            return true;

        if (!PlayModePickerOptions.IsTunerMode(sessionTune) && !explicitTunerOpenPending)
            return false;

        // A pop with no resolved target can return to the Tuner visit still on the stack.
        if (string.IsNullOrWhiteSpace(navigationTarget))
            return true;

        return IsMusicPageTarget(navigationTarget);
    }

    /// <summary>
    /// When <paramref name="navigationTarget"/> would show Tuner, clear the hamburger
    /// mark and select Assortment by Level. Returns true when that replacement happened.
    /// </summary>
    public static bool ApplyIfBackWouldOpenTuner(
        NoteSessionService session,
        string? navigationTarget,
        Action<string>? persistSelectedTune = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!WouldOpenTuner(session.Tune, PlayModePickerOptions.IsExplicitTunerOpenPending, navigationTarget))
            return false;

        PlayModePickerOptions.ClearExplicitTunerOpen();
        PlayModePickerOptions.ApplyOtherSelection(
            session,
            NoteSessionService.ScaleSelectionByLevel,
            persistSelectedTune);
        return true;
    }

    /// <summary>
    /// Title-bar back arrows that navigate to Music. No-ops when services are not ready.
    /// </summary>
    public static void ApplyBackArrowToMusicPage()
    {
        var session = ServiceHelper.GetService<NoteSessionService>();
        if (session == null)
            return;

        ApplyIfBackWouldOpenTuner(session, MusicPageTarget);
    }

    public static bool IsTunerEntryTarget(string? navigationTarget)
        => Contains(navigationTarget, "TunerEntry");

    public static bool IsMusicPageTarget(string? navigationTarget)
        => Contains(navigationTarget, "MusicPage");

    private static bool Contains(string? navigationTarget, string token)
        => !string.IsNullOrWhiteSpace(navigationTarget)
           && navigationTarget.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
}
