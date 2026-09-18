using musicmate.Models;

namespace musicmate.Services;

/// <summary>
/// Conductor-anchored note onset timing: expected times are fixed to the session/conductor
/// start and Music BPM. Live acceptance uses this whenever practice notes are active
/// (visual conductor cues are optional via <see cref="NoteSessionService.ShowConductorCues"/>).
/// </summary>
public static class ConductorOnsetTiming
{
    /// <summary>
    /// Allowed earliness as a fraction of one beat at the session tempo.
    /// ~30% of a beat matches comfortable human timing at slow practice tempos.
    /// </summary>
    public const double EarlyToleranceBeats = 0.30;

    /// <summary>
    /// Late scoring window as a fraction of one beat. Kept at least as wide as early
    /// so a slightly late correct pitch still counts as on-time; beyond this the note
    /// is timing-wrong but the session still advances so the player can continue.
    /// </summary>
    public const double LateToleranceBeats = 0.50;

    /// <summary>Floor so very fast tempos do not shrink the window below playability.</summary>
    public const double MinToleranceMs = 100.0;

    /// <summary>Ceiling so very slow tempos do not open a multi-second accept window.</summary>
    public const double MaxToleranceMs = 1200.0;

    /// <summary>Beat fractions and clamps for a timing window profile (Normal vs Easy).</summary>
    public readonly record struct TimingWindowProfile(
        double EarlyBeats,
        double LateBeats,
        double MinMs,
        double MaxMs);

    /// <summary>Factory / normal-mode conductor timing window.</summary>
    public static TimingWindowProfile NormalProfile { get; } =
        new(EarlyToleranceBeats, LateToleranceBeats, MinToleranceMs, MaxToleranceMs);

    public static double MsPerBeat(int bpm)
    {
        int safeBpm = Math.Max(1, bpm);
        return 60000.0 / safeBpm;
    }

    public static double SecondsPerBeat(int bpm) => MsPerBeat(bpm) / 1000.0;

    public static double EarlyToleranceMs(int bpm)
        => EarlyToleranceMs(bpm, NormalProfile);

    public static double LateToleranceMs(int bpm)
        => LateToleranceMs(bpm, NormalProfile);

    public static double EarlyToleranceMs(int bpm, TimingWindowProfile profile)
        => ClampToleranceMs(profile.EarlyBeats * MsPerBeat(bpm), profile);

    public static double LateToleranceMs(int bpm, TimingWindowProfile profile)
        => ClampToleranceMs(profile.LateBeats * MsPerBeat(bpm), profile);

    public static double ClampToleranceMs(double toleranceMs)
        => ClampToleranceMs(toleranceMs, NormalProfile);

    public static double ClampToleranceMs(double toleranceMs, TimingWindowProfile profile)
        => Math.Clamp(toleranceMs, profile.MinMs, profile.MaxMs);

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

    public static double EarliestAcceptableMs(double expectedMs, double earlyToleranceMs)
        => expectedMs - earlyToleranceMs;

    public static double LatestAcceptableMs(double expectedMs, double lateToleranceMs)
        => expectedMs + lateToleranceMs;

    /// <summary>True when musical time has moved past the note's late-scoring window.</summary>
    public static bool IsWindowExpired(double actualMs, double expectedMs, double lateToleranceMs)
        => actualMs > expectedMs + lateToleranceMs;

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
