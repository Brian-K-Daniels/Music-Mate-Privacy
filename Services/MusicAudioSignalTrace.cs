namespace musicmate.Services;

/// <summary>
/// Status text for an active Music listening session.
/// </summary>
public static class MusicAudioSignalTrace
{
    public const string ListeningPausedMessage = "Listening paused.";
    public const string StoppedListeningMessage = "Stopped listening.";
    public const string ListeningMessage = "Listening…";

    /// <summary>
    /// An active Music session must not keep the idle "Stopped listening." label.
    /// Returns the replacement, or null when the current text should stay.
    /// </summary>
    public static string? StatusWhileMusicCaptureActive(string? rawStatus, bool listeningSessionActive)
    {
        if (!listeningSessionActive)
            return null;
        if (string.Equals(rawStatus, StoppedListeningMessage, StringComparison.Ordinal))
            return ListeningMessage;
        return null;
    }

    /// <summary>
    /// Count-in restores its baseline after two seconds. A leftover stop label
    /// would come back while the microphone is still open.
    /// </summary>
    public static string CountInRestoreBaseline(string? rawStatus)
    {
        if (string.Equals(rawStatus, StoppedListeningMessage, StringComparison.Ordinal)
            || string.Equals(rawStatus, ListeningPausedMessage, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(rawStatus))
            return ListeningMessage;
        return rawStatus!;
    }

    /// <summary>Listening is only honest while the recorder is still delivering buffers.</summary>
    public static bool UiSaysListeningWhileCaptureStopped(bool uiSaysListening, bool isCapturing)
        => uiSaysListening && !isCapturing;

    /// <summary>
    /// Quiet air before the player starts must not switch the detector off.
    /// The conductor clock is not armed until the first correct note, so that
    /// silence is not a musical pause.
    /// </summary>
    public static bool SilenceBeforeFirstNoteDisablesDetector(bool listeningClockArmed)
        => false;
}
