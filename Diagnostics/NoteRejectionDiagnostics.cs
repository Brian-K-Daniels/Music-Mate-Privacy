using System.Globalization;
using musicmate.Diagnostics;

namespace musicmate.Services;

/// <summary>
/// DEBUG diagnostic lines when the player identifies the expected pitch but the note is not accepted.
/// </summary>
public static class NoteRejectionDiagnostics
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, DateTime> LastLoggedUtc = new();
    private const int MinRepeatIntervalMs = 100;

    public sealed record TimingInfo(
        double SessionMs,
        double? ExpectedStartMs,
        double? TimingErrorMs,
        double? EarlyToleranceMs,
        double? LateToleranceMs,
        string Classification);

    public static void Log(
        string expected,
        string heard,
        int cents,
        int noteIndex,
        string reason,
        TimingInfo? timing = null,
        float? rms = null,
        float? pitchConfidence = null,
        string? debounceState = null,
        string? extra = null)
    {
        string key = $"{noteIndex}|{reason}|{heard}|{cents}";
        var now = DateTime.UtcNow;
        lock (Gate)
        {
            if (LastLoggedUtc.TryGetValue(key, out var last)
                && (now - last).TotalMilliseconds < MinRepeatIntervalMs)
                return;
            LastLoggedUtc[key] = now;
        }

        var parts = new List<string>
        {
            $"[NoteRejected] Expected={expected}",
            $"Heard={heard}",
            $"Cents={cents}",
            $"Index={noteIndex}",
        };

        if (timing is not null)
        {
            parts.Add($"SessionMs={timing.SessionMs.ToString("F0", CultureInfo.InvariantCulture)}");
            if (timing.ExpectedStartMs.HasValue)
                parts.Add($"ExpectedStartMs={timing.ExpectedStartMs.Value.ToString("F0", CultureInfo.InvariantCulture)}");
            if (timing.TimingErrorMs.HasValue)
                parts.Add($"Timing={timing.TimingErrorMs.Value.ToString("F0", CultureInfo.InvariantCulture)}ms");
            if (timing.EarlyToleranceMs.HasValue)
                parts.Add($"TolEarly={timing.EarlyToleranceMs.Value.ToString("F0", CultureInfo.InvariantCulture)}ms");
            if (timing.LateToleranceMs.HasValue)
                parts.Add($"TolLate={timing.LateToleranceMs.Value.ToString("F0", CultureInfo.InvariantCulture)}ms");
            if (!string.IsNullOrWhiteSpace(timing.Classification))
                parts.Add($"Window={timing.Classification}");
        }

        if (rms.HasValue)
            parts.Add($"RMS={rms.Value.ToString("F4", CultureInfo.InvariantCulture)}");
        if (pitchConfidence.HasValue)
            parts.Add($"Confidence={pitchConfidence.Value.ToString("F2", CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(debounceState))
            parts.Add($"Debounce={debounceState}");
        if (!string.IsNullOrWhiteSpace(extra))
            parts.Add(extra);

        parts.Add($"Reason={reason}");
        DebugLog.WriteLine(DebugLogCategory.MusicPageFlow, string.Join(' ', parts));
    }

    internal static void ResetForTests()
    {
        lock (Gate)
            LastLoggedUtc.Clear();
    }
}
