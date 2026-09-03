namespace musicmate.Services;

/// <summary>Optional scheduler context for metronome click diagnostics.</summary>
public readonly record struct MetronomeClickScheduleInfo(
    long BeatIndex,
    int MeasureNumber,
    int BeatNumber,
    double IntendedMs,
    double SchedulerMs,
    long SchedulerTick);
