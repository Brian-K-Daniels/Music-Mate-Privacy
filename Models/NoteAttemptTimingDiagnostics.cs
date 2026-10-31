namespace musicmate.Models;

/// <summary>
/// Shared labels for note-attempt timing diagnostics so DB rows, UI, and tests agree.
/// </summary>
public static class NoteAttemptTimingDiagnostics
{
    /// <summary>
    /// Persisted in <see cref="NoteAttempt.WrongReason"/> when an Early candidate for the
    /// same note slot was superseded by a later in-window accept (OverallCorrect remains true).
    /// </summary>
    public const string HadEarlyCandidateReason = "hadEarlyCandidate";

    /// <summary>Legacy value written before the rename; still recognized when reading.</summary>
    public const string LegacyWasEarlyReason = "wasEarly";

    public static bool IsHadEarlyCandidateReason(string? wrongReason)
        => string.Equals(wrongReason, HadEarlyCandidateReason, StringComparison.Ordinal)
           || string.Equals(wrongReason, LegacyWasEarlyReason, StringComparison.Ordinal);

    /// <summary>
    /// Classification of the finally accepted onset from
    /// <c>deltaMs = actualOnset - expectedOnset</c>.
    /// </summary>
    public static string ClassifyAcceptedOnset(double deltaMs)
    {
        if (deltaMs < 0)
            return "early";
        if (deltaMs > 0)
            return "late";
        return "onTime";
    }

    public static string? ClassifyAcceptedOnset(double? timingErrorMs)
        => timingErrorMs.HasValue ? ClassifyAcceptedOnset(timingErrorMs.Value) : null;
}
