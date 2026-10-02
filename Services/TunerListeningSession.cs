namespace musicmate.Services;

/// <summary>
/// Tuner microphone startup. "Listening starts when music appears" also applies
/// when the Tuner display appears. One start at a time; a failure leaves Go able
/// to request another real capture attempt.
/// </summary>
public sealed class TunerListeningSession
{
    public const string ListeningMessage = "Listening…";
    public const string MicrophoneFailedMessage = "Microphone did not start — tap Go to retry.";

    private bool _starting;

    public bool IsStarting => _starting;
    public bool IsListening { get; private set; }
    public int Generation { get; private set; }
    public int StartAttempts { get; private set; }
    public int ActiveSessions { get; private set; }
    public string? StatusMessage { get; private set; }

    public bool ShouldAutoStartOnAppear(
        bool listeningStartsWhenPageAppears,
        bool isCapturing,
        bool referenceTonePlaying,
        bool pitchPlaying)
        => listeningStartsWhenPageAppears
           && !isCapturing
           && !_starting
           && !referenceTonePlaying
           && !pitchPlaying;

    /// <summary>Go while capture is actually running stops it. A failed or idle Go starts again.</summary>
    public bool ShouldStopOnGo(bool isCapturing)
        => IsListening && isCapturing && !_starting;

    /// <summary>
    /// Marks one new capture attempt. False when a start is already in flight or capture is live,
    /// so OnAppearing and Go cannot open two sessions.
    /// </summary>
    public bool TryBeginStart(bool isCapturing)
    {
        if (_starting || isCapturing)
            return false;

        _starting = true;
        Generation++;
        StartAttempts++;
        ActiveSessions = 0;
        IsListening = false;
        return true;
    }

    public void CompleteStart(int generation, bool captureIsRunning)
    {
        if (generation != Generation)
            return;

        _starting = false;
        if (captureIsRunning)
        {
            ActiveSessions = 1;
            IsListening = true;
            StatusMessage = ListeningMessage;
            return;
        }

        ActiveSessions = 0;
        IsListening = false;
        StatusMessage = MicrophoneFailedMessage;
    }

    public void MarkStopped()
    {
        _starting = false;
        IsListening = false;
        ActiveSessions = 0;
    }

    /// <summary>Invalidates an in-flight start and drops the live capture count.</summary>
    public void Leave()
    {
        Generation++;
        _starting = false;
        IsListening = false;
        ActiveSessions = 0;
    }
}
