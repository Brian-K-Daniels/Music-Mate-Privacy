namespace musicmate.Services;

/// <summary>
/// Go must not present Stop / the current-note playing color until capture has
/// delivered a buffer. A failed or cancelled startup must not keep that chrome.
/// </summary>
public static class ListeningStartupGate
{
    public static bool ShouldPresentPlayingChrome(bool startupActive, bool bufferConfirmed)
        => !startupActive || bufferConfirmed;

    /// <summary>
    /// A late failure may roll back only the startup generation that is still in progress.
    /// A newer Go, or a session that already confirmed listening, is left alone.
    /// </summary>
    public static bool ShouldAbortStartup(
        bool startupActive,
        bool chromeShown,
        int expectedGeneration,
        int currentGeneration)
        => startupActive
           && !chromeShown
           && expectedGeneration == currentGeneration
           && expectedGeneration != 0;
}
