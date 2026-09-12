using System.Globalization;
using System.Text;
using musicmate.Models;

namespace musicmate.ViewModels;

/// <summary>
/// Display row for a persisted note attempt. Kept outside DEBUG-only pages so unit tests
/// can verify detail formatting in Release builds too.
/// </summary>
public sealed class NoteAttemptRowViewModel
{
    public NoteAttemptRowViewModel(NoteAttempt attempt)
    {
        AttemptId = attempt.AttemptId;
        Header = $"#{attempt.AttemptId}  {attempt.DateTime:u}  sess={Truncate(attempt.SessionId, 8)}";
        Detail = BuildDetail(attempt);
        IsWrong = !attempt.OverallCorrect;
    }

    public int AttemptId { get; }
    public string Header { get; }
    public string Detail { get; }
    public bool IsWrong { get; }

    private static string BuildDetail(NoteAttempt a)
    {
        var sb = new StringBuilder();
        sb.Append(a.IsRest ? "REST" : a.ExpectedWrittenNoteName);
        if (!string.IsNullOrEmpty(a.ExpectedDuration))
            sb.Append(' ').Append(a.ExpectedDuration);
        sb.Append(" → ").Append(string.IsNullOrEmpty(a.ActualDetectedNoteName) ? "-" : a.ActualDetectedNoteName);
        sb.Append(" | P=").Append(a.PitchCorrect ? "ok" : "no");
        sb.Append(" T=").Append(a.TimingCorrect switch
        {
            true => "ok",
            false => "no",
            null => "-",
        });
        sb.Append(" O=").Append(a.OverallCorrect ? "ok" : "no");

        bool hadEarlyCandidate = NoteAttemptTimingDiagnostics.IsHadEarlyCandidateReason(a.WrongReason);
        if (!string.IsNullOrEmpty(a.WrongReason) && !hadEarlyCandidate)
            sb.Append(" | ").Append(a.WrongReason);
        if (hadEarlyCandidate)
            sb.Append(" | hadEarlyCandidate");

        string? onset = NoteAttemptTimingDiagnostics.ClassifyAcceptedOnset(a.TimingErrorMs);
        if (onset is not null)
            sb.Append(" | onset=").Append(onset);

        if (a.PitchErrorCents != 0)
            sb.Append(" | ").Append(a.PitchErrorCents).Append('¢');
        if (a.TimingErrorMs.HasValue)
            sb.Append(" | Δ").Append(a.TimingErrorMs.Value.ToString("F0", CultureInfo.InvariantCulture)).Append("ms");
        if (a.ExpectedStartMs.HasValue || a.ActualDetectedMs.HasValue)
        {
            sb.Append(" | t=");
            sb.Append(a.ExpectedStartMs?.ToString("F0", CultureInfo.InvariantCulture) ?? "-");
            sb.Append('/');
            sb.Append(a.ActualDetectedMs?.ToString("F0", CultureInfo.InvariantCulture) ?? "-");
        }

        return sb.ToString();
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s))
            return "-";
        return s.Length <= max ? s : s[..max];
    }
}
