using musicmate.Models;

namespace musicmate.Services;

/// <summary>
/// Conductor-anchored note onset timing: expected times are fixed to the session/conductor
/// start and Music BPM. Used when <see cref="NoteSessionService.ShowConductorCues"/> is on.
/// </summary>
public static class ConductorOnsetTiming
{
    /// <summary>Allowed earliness in quarter-note beats (one sixteenth of a quarter).</summary>
    public const double EarlyToleranceBeats = 0.25;

    /// <summary>Late scoring window in quarter-note beats (does not block acceptance).</summary>
    public const double LateToleranceBeats = 0.50;

    public static double MsPerBeat(int bpm)
    {
        int safeBpm = Math.Max(1, bpm);
        return 60000.0 / safeBpm;
    }

    public static double SecondsPerBeat(int bpm) => MsPerBeat(bpm) / 1000.0;

    public static double EarlyToleranceMs(int bpm) => EarlyToleranceBeats * MsPerBeat(bpm);

    public static double LateToleranceMs(int bpm) => LateToleranceBeats * MsPerBeat(bpm);

    /// <summary>
    /// Absolute expected onset from the conductor timeline.
    /// <paramref name="conductorStartMs"/> is normally 0 (session clock restart).
    /// </summary>
    public static double ExpectedOnsetMs(double conductorStartMs, double beatPosition, int bpm)
        => conductorStartMs + beatPosition * MsPerBeat(bpm);

    public static bool IsTooEarly(double actualMs, double expectedMs, double earlyToleranceMs)
        => actualMs < expectedMs - earlyToleranceMs;

    public static bool IsWithinTimingWindow(
        double actualMs,
        double expectedMs,
        double earlyToleranceMs,
        double lateToleranceMs)
        => actualMs >= expectedMs - earlyToleranceMs
           && actualMs <= expectedMs + lateToleranceMs;

    /// <summary>
    /// Beat position of note <paramref name="noteIndex"/> on a conductor-absolute timeline.
    /// Prefer positive gate spacing (includes written rests); fall back to prior duration
    /// when gates are missing or negative (e.g. lower-staff BeatPosition reset).
    /// </summary>
    public static double GetAnchoredBeatPosition(
        IReadOnlyList<NoteInfo> notes,
        int noteIndex)
    {
        if (notes == null || notes.Count == 0 || noteIndex <= 0)
        {
            if (notes != null && notes.Count > 0 && noteIndex == 0)
                return Math.Max(0.0, notes[0].StartBeat);
            return 0.0;
        }

        noteIndex = Math.Min(noteIndex, notes.Count - 1);
        double beat = Math.Max(0.0, notes[0].StartBeat);

        for (int i = 1; i <= noteIndex; i++)
        {
            double gate = notes[i].GateBeatsAfterPrevious;
            if (gate > 1e-9)
            {
                beat += gate;
                continue;
            }

            double priorDur = notes[i - 1].DurationBeats;
            if (priorDur <= 1e-9)
                priorDur = notes[i - 1].Duration?.ToBeatValue() ?? 1.0;
            beat += priorDur;
        }

        return beat;
    }
}
