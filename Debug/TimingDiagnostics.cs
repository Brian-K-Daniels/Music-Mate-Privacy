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
        ConductorDecision,
    }

    private readonly struct PendingEntry
    {
        public PendingKind Kind { get; init; }
        public TimingWrongPayload TimingWrong { get; init; }
        public RestTimingWrongPayload RestTimingWrong { get; init; }
        public ConductorTimingDecisionPayload ConductorDecision { get; init; }
    }

    private static readonly ConcurrentQueue<PendingEntry> _pending = new();
    private static int _timingWrongCount;
    private static int _restTimingWrongCount;
    private static int _conductorDecisionCount;

    public static void ResetSession()
    {
        while (_pending.TryDequeue(out _)) { }
        _timingWrongCount = 0;
        _restTimingWrongCount = 0;
        _conductorDecisionCount = 0;
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

    public static void EnqueueConductorDecision(in ConductorTimingDecisionPayload payload)
    {
        if (!EnableTimingDiagnostics)
            return;

        Interlocked.Increment(ref _conductorDecisionCount);
        _pending.Enqueue(new PendingEntry
        {
            Kind = PendingKind.ConductorDecision,
            ConductorDecision = payload,
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
                case PendingKind.ConductorDecision:
                    {
                        var p = entry.ConductorDecision;
                        Debug.WriteLine(
                            $"[ConductorTiming] bpm={p.Bpm} secPerBeat={p.SecondsPerBeat:F4} " +
                            $"conductorStartMs={p.ConductorStartMs:F0} idx={p.NoteIndex} " +
                            $"beat={p.BeatPosition:F2} expectedMs={p.ExpectedOnsetMs:F0} " +
                            $"actualMs={p.ActualOnsetMs:F0} errMs={p.TimingErrorMs:F0} " +
                            $"earlyTolMs={p.EarlyToleranceMs:F0} lateTolMs={p.LateToleranceMs:F0} " +
                            $"detMidi={p.DetectedMidi} expMidi={p.ExpectedMidi} " +
                            $"heard={p.DetectedName} expected={p.ExpectedName} " +
                            $"pitchOk={p.PitchAccepted} timingOk={p.TimingAccepted} " +
                            $"reason={p.AdvanceReason}");
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
            $"[TimingSummary] timingWrongCount={_timingWrongCount} " +
            $"restTimingWrongCount={_restTimingWrongCount} " +
            $"conductorDecisionCount={_conductorDecisionCount}");
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

public readonly struct ConductorTimingDecisionPayload
{
    public int Bpm { get; init; }
    public double SecondsPerBeat { get; init; }
    public double ConductorStartMs { get; init; }
    public int NoteIndex { get; init; }
    public double BeatPosition { get; init; }
    public double ExpectedOnsetMs { get; init; }
    public double ActualOnsetMs { get; init; }
    public double TimingErrorMs { get; init; }
    public double EarlyToleranceMs { get; init; }
    public double LateToleranceMs { get; init; }
    public int DetectedMidi { get; init; }
    public int ExpectedMidi { get; init; }
    public string DetectedName { get; init; }
    public string ExpectedName { get; init; }
    public bool PitchAccepted { get; init; }
    public bool TimingAccepted { get; init; }
    public string AdvanceReason { get; init; }
}
