#if DEBUG
using System.Diagnostics;

namespace musicmate.Diagnostics;

/// <summary>Temporary beat-timing trace for metronome / count-in scheduler tuning.</summary>
public static class MetronomeBeatDiagnostics
{
    public static bool Enabled { get; set; } = true;

    public static void LogBeat(
        int measureNumber,
        int beatNumber,
        double intendedMs,
        double schedulerMs,
        double schedulerErrorMs,
        bool accented)
    {
        if (!Enabled)
            return;

        Debug.WriteLine(
            $"[Metronome] m={measureNumber} b={beatNumber} " +
            $"target={intendedMs:F1}ms sched={schedulerMs:F1}ms schedErr={schedulerErrorMs:+0.0;-0.0;0.0}ms " +
            $"accent={(accented ? 1 : 0)}");
    }

    public static void LogAudioTrigger(
        int measureNumber,
        int beatNumber,
        double intendedMs,
        double schedulerMs,
        double dispatchLatencyMs,
        bool accented)
    {
        if (!Enabled)
            return;

        Debug.WriteLine(
            $"[Metronome] m={measureNumber} b={beatNumber} " +
            $"target={intendedMs:F1}ms sched={schedulerMs:F1}ms " +
            $"dispatchLatency={dispatchLatencyMs:+0.0;-0.0;0.0}ms accent={(accented ? 1 : 0)}");
    }
}
#endif
