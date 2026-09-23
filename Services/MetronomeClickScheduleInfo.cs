using System.Diagnostics;

namespace musicmate.Services;

/// <summary>Optional scheduler context for metronome click diagnostics.</summary>
public readonly record struct MetronomeClickScheduleInfo(
    long BeatIndex,
    int MeasureNumber,
    int BeatNumber,
    double IntendedMs,
    double SchedulerMs,
    long SchedulerTick,
    long GridOriginTimestamp,
    int LoopId = 0,
    int Bpm = 0,
    string Source = "COUNTIN")
{
    public double ElapsedMsAt(long timestamp)
        => GridOriginTimestamp == 0
            ? SchedulerMs
            : (timestamp - GridOriginTimestamp) * 1000.0 / Stopwatch.Frequency;

    public string FormatBeatLine(double actualElapsedMs)
    {
        double error = actualElapsedMs - IntendedMs;
        return $"Beat {BeatIndex + 1} target={IntendedMs:F1} actual={actualElapsedMs:F1} error={error:+0.0;-0.0;0.0} ms";
    }

    public string FormatAudibleBeat(double timeMs, string path)
        => $"{Source} session={LoopId} beat={BeatIndex + 1} time={timeMs:F1} bpm={Bpm} path={path}";
}
