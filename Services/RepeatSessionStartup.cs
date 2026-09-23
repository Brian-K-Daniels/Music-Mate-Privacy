namespace musicmate.Services;

/// <summary>
/// Repeat must finish the previous listener before the next Music session begins.
/// Auto-start uses one new listener; otherwise the new staff waits for GO.
/// A count-in click is not the first played note.
/// </summary>
public sealed class RepeatSessionStartup
{
    private bool _stoppedSinceLastStart = true;

    public bool Listening { get; private set; }
    public bool WaitingForGo { get; private set; }
    public int ActiveListeners { get; private set; }
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public int CurrentNoteIndex { get; private set; }
    public bool CountInArmed { get; private set; }
    public bool ConductorCuesEnabled { get; private set; }
    public bool CountInAdvancedFirstNote { get; private set; }

    public static bool ShouldAutoStartListening(bool autoRepeat, bool autoStart, string? tune)
        => PracticeSessionLifecycle.ShouldAutoRepeat(autoRepeat, tune ?? string.Empty) && autoStart;

    /// <summary>Drops the finished session's listener. Required before the next begin.</summary>
    public void StopPreviousListener()
    {
        ActiveListeners = 0;
        Listening = false;
        WaitingForGo = false;
        CountInArmed = false;
        CountInAdvancedFirstNote = false;
        _stoppedSinceLastStart = true;
        StopCount++;
    }

    /// <summary>
    /// Claims exactly one listener for the repeated session. False when the previous
    /// listener was not stopped, or a listener is already active.
    /// </summary>
    public bool TryBeginAutoStart(bool countInEnabled, bool conductorCuesEnabled)
    {
        if (!_stoppedSinceLastStart || ActiveListeners != 0)
            return false;

        _stoppedSinceLastStart = false;
        ActiveListeners = 1;
        StartCount++;
        Listening = true;
        WaitingForGo = false;
        CurrentNoteIndex = 0;
        CountInArmed = countInEnabled;
        ConductorCuesEnabled = conductorCuesEnabled;
        CountInAdvancedFirstNote = false;
        return true;
    }

    /// <summary>The real start routine did not leave a live pipeline. Release the claim.</summary>
    public void CompleteAutoStart(bool pipelineArmed)
    {
        if (pipelineArmed)
        {
            ActiveListeners = 1;
            Listening = true;
            WaitingForGo = false;
            CurrentNoteIndex = 0;
            return;
        }

        ActiveListeners = 0;
        Listening = false;
        WaitingForGo = true;
        CountInArmed = false;
    }

    /// <summary>Next staff is ready, but listening waits for GO.</summary>
    public bool WaitForGo(bool conductorCuesEnabled)
    {
        if (!_stoppedSinceLastStart || ActiveListeners != 0)
            return false;

        _stoppedSinceLastStart = false;
        ActiveListeners = 0;
        Listening = false;
        WaitingForGo = true;
        CurrentNoteIndex = 0;
        CountInArmed = false;
        ConductorCuesEnabled = conductorCuesEnabled;
        CountInAdvancedFirstNote = false;
        return true;
    }

    /// <summary>Count-in clicks must not move off the first note.</summary>
    public void OnCountInClick()
    {
        if (!CountInArmed)
            return;

        if (CurrentNoteIndex != 0)
            CountInAdvancedFirstNote = true;
    }

    public bool FirstNoteIsActiveTarget
        => CurrentNoteIndex == 0 && (Listening || WaitingForGo);
}
