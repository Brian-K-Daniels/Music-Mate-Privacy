namespace musicmate.Models;

/// <summary>Per-attempt pitch/timing/overall result for one note slot or rest.</summary>
public readonly struct NoteAttemptOutcome
{
    public bool IsRest { get; init; }
    public string ExpectedWrittenNoteName { get; init; }
    public string? ActualDetectedNoteName { get; init; }
    public string? ExpectedDuration { get; init; }
    public double? ExpectedBeat { get; init; }
    public double? ExpectedStartMs { get; init; }
    public double? ActualDetectedMs { get; init; }
    public double? TimingErrorMs { get; init; }
    public double? TimingToleranceMs { get; init; }
    public bool PitchCorrect { get; init; }
    public bool? TimingCorrect { get; init; }
    public bool OverallCorrect { get; init; }
    public string WrongReason { get; init; }
    public int PitchErrorCents { get; init; }
    public int MidiNumber { get; init; }
}
