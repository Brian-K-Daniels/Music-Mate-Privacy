namespace musicmate.Services;

/// <summary>
/// Orders one Music Count-In ahead of listening. Clicks use
/// <see cref="WaitingCountInPlayer"/>; this only decides when that loop may
/// start, when listening may follow, and when a pitch is the player's note.
/// </summary>
public sealed class CountInSessionGate
{
    public int Generation { get; private set; }
    public int ActiveSequences { get; private set; }
    public bool CountInRunning { get; private set; }
    public bool ClicksSounded { get; private set; }
    public bool ListeningStarted { get; private set; }
    public bool FirstNoteIsTarget { get; private set; }
    public bool FirstNoteHeard { get; private set; }
    public int NoteIndex { get; private set; }
    public int ClickCount { get; private set; }

    public static bool ShouldRunCountIn(bool countInEnabled, bool tuner, int noteCount)
        => countInEnabled && !tuner && noteCount > 0;

    /// <summary>
    /// GO, automatic listening, or a repeated session. Refuses when a sequence
    /// is already running so two count-ins cannot overlap.
    /// </summary>
    public bool TryBegin(bool countInEnabled, bool tuner, int noteCount)
    {
        if (ActiveSequences != 0 || CountInRunning)
            return false;

        Generation++;
        ClickCount = 0;
        ClicksSounded = false;
        ListeningStarted = false;
        FirstNoteIsTarget = false;
        FirstNoteHeard = false;
        NoteIndex = 0;

        if (!ShouldRunCountIn(countInEnabled, tuner, noteCount))
        {
            CountInRunning = false;
            ListeningStarted = !tuner;
            FirstNoteIsTarget = ListeningStarted;
            return false;
        }

        ActiveSequences = 1;
        CountInRunning = true;
        return true;
    }

    /// <summary>A count-in click has been issued. Listening is still closed.</summary>
    public void OnClickSounded()
    {
        if (!CountInRunning)
            return;

        ClickCount++;
        ClicksSounded = true;
    }

    /// <summary>Listening opens only after at least one click has sounded.</summary>
    public bool MayBeginListening()
        => CountInRunning && ClicksSounded && !ListeningStarted;

    public void MarkListeningStarted()
    {
        if (!MayBeginListening())
            return;

        ListeningStarted = true;
        FirstNoteIsTarget = true;
        NoteIndex = 0;
        FirstNoteHeard = false;
    }

    /// <summary>Stop, navigation, or a newer session. Cancels the current sequence.</summary>
    public void Stop()
    {
        ActiveSequences = 0;
        CountInRunning = false;
        ClicksSounded = false;
        ListeningStarted = false;
        FirstNoteIsTarget = false;
        ClickCount = 0;
        Generation++;
    }

    /// <summary>
    /// A count-in tone is not the first played note. A real note is heard only
    /// after listening has started, and it does not move the target earlier.
    /// </summary>
    public bool TryAcceptHeardPitch(bool correct, bool nearCountInTone)
    {
        if (!ListeningStarted || !CountInRunning || NoteIndex != 0)
            return false;
        if (nearCountInTone || !correct)
            return false;

        FirstNoteHeard = true;
        CountInRunning = false;
        ActiveSequences = 0;
        return true;
    }
}
