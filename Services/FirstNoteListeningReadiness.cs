namespace musicmate.Services;

/// <summary>
/// The first note must not be graded until capture is running and enough audio
/// has arrived to fill one pitch-analysis window. Staff highlight follows the
/// same gate so the player is not invited to play into a microphone that is
/// still starting.
/// </summary>
public sealed class FirstNoteListeningReadiness
{
    public bool ListeningActive { get; private set; }
    public bool GradingArmed { get; private set; }
    public int AudioBuffersReceived { get; private set; }
    public int SamplesReceived { get; private set; }
    public int SamplesRequired { get; private set; } = 4096;

    /// <summary>True once capture is up and one analysis window of audio has arrived.</summary>
    public bool ShouldGradePitch => ListeningActive && GradingArmed;

    /// <summary>
    /// Tuner does not use this gate. Music grades only after <see cref="ShouldGradePitch"/>.
    /// </summary>
    public bool MayGradeMusicPitch(bool isTuner) => isTuner || ShouldGradePitch;

    /// <summary>
    /// The current-note color is the "play this now" signal.
    /// Before listening starts, an idle preview may still mark the opening note.
    /// Once a listening session has begun, the color waits until grading is armed.
    /// </summary>
    public static bool ShouldPaintCurrentNote(
        bool showCurrentNoteHighlight, bool gradingArmed, bool listeningSession)
        => showCurrentNoteHighlight && (!listeningSession || gradingArmed);

    public void BeginSession(int pitchWindowSamples)
    {
        ListeningActive = false;
        GradingArmed = false;
        AudioBuffersReceived = 0;
        SamplesReceived = 0;
        SamplesRequired = Math.Max(1, pitchWindowSamples);
    }

    public void MarkCaptureRunning()
    {
        ListeningActive = true;
        if (SamplesReceived >= SamplesRequired)
            GradingArmed = true;
    }

    public void MarkCaptureStopped()
    {
        ListeningActive = false;
    }

    /// <summary>Returns true on the buffer that first arms grading.</summary>
    public bool OnAudioBuffer(int sampleCount)
    {
        if (!ListeningActive)
            return false;

        AudioBuffersReceived++;
        if (GradingArmed)
            return false;

        SamplesReceived += Math.Max(0, sampleCount);
        if (SamplesReceived < SamplesRequired)
            return false;

        GradingArmed = true;
        return true;
    }
}

/// <summary>
/// One line for the first note: written vs concert, what the microphone heard,
/// and why that detection was not accepted.
/// </summary>
public readonly record struct FirstNotePitchDiagnostic(
    int CurrentNoteIndex,
    string ExpectedWrittenPitch,
    string ExpectedConcertPitch,
    string HeardConcertPitch,
    string HeardWrittenPitch,
    bool ListeningActive,
    int AudioBuffersReceived,
    float PitchConfidence,
    string RejectionReason)
{
    public static FirstNotePitchDiagnostic FromDomains(
        int currentNoteIndex,
        PitchDomains domains,
        bool listeningActive,
        int audioBuffersReceived,
        float pitchConfidence,
        string rejectionReason)
        => new(
            currentNoteIndex,
            domains.ExpectedWrittenPitch,
            domains.ExpectedConcertPitch,
            domains.HeardConcertPitch,
            domains.HeardWrittenPitch,
            listeningActive,
            audioBuffersReceived,
            pitchConfidence,
            rejectionReason);

    public string Format()
        => "[FirstNote] "
           + $"index={CurrentNoteIndex} "
           + $"expectedWritten={ExpectedWrittenPitch} "
           + $"expectedConcert={ExpectedConcertPitch} "
           + $"heardConcert={HeardConcertPitch} "
           + $"heardWritten={HeardWrittenPitch} "
           + $"listening={(ListeningActive ? "yes" : "no")} "
           + $"buffers={AudioBuffersReceived} "
           + $"confidence={PitchConfidence.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)} "
           + $"reason={RejectionReason}";
}
