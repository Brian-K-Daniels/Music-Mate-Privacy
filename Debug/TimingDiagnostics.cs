using System.Collections.Concurrent;
using System.Diagnostics;

namespace musicmate.Diagnostics;

/// <summary>
/// Queued timing/rest diagnostic logging. Enqueue lightweight payloads from pitch feedback;
/// flush from the UI thread so string formatting never runs in the audio callback.
/// </summary>
public static class TimingDiagnostics
{
    /// <summary>Set to false to disable all timing diagnostic output.</summary>
    public static bool EnableTimingDiagnostics { get; set; } = true;

    private enum PendingKind : byte
    {
        TimingWrong,
        RestTimingWrong,
    }

    private readonly struct PendingEntry
    {
        public PendingKind Kind { get; init; }
        public TimingWrongPayload TimingWrong { get; init; }
        public RestTimingWrongPayload RestTimingWrong { get; init; }
    }

    private static readonly ConcurrentQueue<PendingEntry> _pending = new();
    private static int _timingWrongCount;
    private static int _restTimingWrongCount;

    public static void ResetSession()
    {
        while (_pending.TryDequeue(out _)) { }
        _timingWrongCount = 0;
        _restTimingWrongCount = 0;
    }

    public static void EnqueueTimingWrong(in TimingWrongPayload payload)
    {
        if (!EnableTimingDiagnostics)
            return;

        Interlocked.Increment(ref _timingWrongCount);
        _pending.Enqueue(new PendingEntry
        {
            Kind = PendingKind.TimingWrong,
            TimingWrong = payload,
        });
    }

    public static void EnqueueRestTimingWrong(in RestTimingWrongPayload payload)
    {
        if (!EnableTimingDiagnostics)
            return;

        Interlocked.Increment(ref _restTimingWrongCount);
        _pending.Enqueue(new PendingEntry
        {
            Kind = PendingKind.RestTimingWrong,
            RestTimingWrong = payload,
        });
    }

    /// <summary>Formats and writes queued lines to the VS Output window. Call from the main/UI thread.</summary>
    public static void Flush()
    {
        if (!EnableTimingDiagnostics)
            return;

        while (_pending.TryDequeue(out var entry))
        {
            switch (entry.Kind)
            {
                case PendingKind.TimingWrong:
                    {
                        var p = entry.TimingWrong;
                        Debug.WriteLine(
                            $"[TimingWrong] expected={p.ExpectedName} duration={p.DurationName} " +
                            $"expectedBeat={p.ExpectedBeat:F2} expectedMs={p.ExpectedMs:F0} " +
                            $"actual={p.ActualName} actualMs={p.ActualMs:F0} " +
                            $"errorMs={p.ErrorMs:F0} toleranceMs={p.ToleranceMs:F0} " +
                            $"pitchCorrect={p.PitchCorrect} timingCorrect={p.TimingCorrect} " +
                            $"overallCorrect={p.OverallCorrect} reason={p.Reason}");
                        break;
                    }
                case PendingKind.RestTimingWrong:
                    {
                        var p = entry.RestTimingWrong;
                        Debug.WriteLine(
                            $"[RestTimingWrong] expected=REST duration={p.RestDurationName} " +
                            $"expectedBeat={p.RestStartBeat:F2} expectedMs={p.RestStartMs:F0} " +
                            $"actual={p.ActualName} actualMs={p.ActualMs:F0} " +
                            $"reason={p.Reason}");
                        break;
                    }
            }
        }
    }

    public static void WriteSessionSummary()
    {
        if (!EnableTimingDiagnostics)
            return;

        Debug.WriteLine(
            $"[TimingSummary] timingWrongCount={_timingWrongCount} restTimingWrongCount={_restTimingWrongCount}");
    }
}

public readonly struct TimingWrongPayload
{
    public string ExpectedName { get; init; }
    public string DurationName { get; init; }
    public double ExpectedBeat { get; init; }
    public double ExpectedMs { get; init; }
    public string ActualName { get; init; }
    public double ActualMs { get; init; }
    public double ErrorMs { get; init; }
    public double ToleranceMs { get; init; }
    public bool PitchCorrect { get; init; }
    public bool TimingCorrect { get; init; }
    public bool OverallCorrect { get; init; }
    public string Reason { get; init; }
}

public readonly struct RestTimingWrongPayload
{
    public string RestDurationName { get; init; }
    public double RestStartBeat { get; init; }
    public double RestStartMs { get; init; }
    public string ActualName { get; init; }
    public double ActualMs { get; init; }
    public string Reason { get; init; }
}
