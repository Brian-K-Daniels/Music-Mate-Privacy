namespace musicmate.Services;

/// <summary>
/// Pure helpers for the Music-page tempo nudge control (outer / inner / current / inner / outer).
/// Step sizes depend on the current BPM. Authoritative BPM remains <see cref="NoteSessionService.Tempo"/>.
/// </summary>
public static class TempoControlLogic
{
    /// <summary>Outer and inner absolute step sizes for a given BPM.</summary>
    public readonly record struct TempoStepSizes(int Outer, int Inner);

    /// <summary>
    /// Step sizes by current BPM band (clamped to session tempo range first):
    /// 30–59 → 10/2; 60–71 → 10/3; 72–119 → 20/4; 120–143 → 30/6; 144–239 → 40/8; 240–252 → 60/12.
    /// </summary>
    public static TempoStepSizes GetStepSizes(int currentBpm)
    {
        int bpm = Math.Clamp(currentBpm, NoteSessionService.MinTempo, NoteSessionService.MaxTempo);
        if (bpm <= 59) return new TempoStepSizes(10, 2);
        if (bpm <= 71) return new TempoStepSizes(10, 3);
        if (bpm <= 119) return new TempoStepSizes(20, 4);
        if (bpm <= 143) return new TempoStepSizes(30, 6);
        if (bpm <= 239) return new TempoStepSizes(40, 8);
        return new TempoStepSizes(60, 12);
    }

    /// <summary>Five deltas for the strip: −outer, −inner, 0, +inner, +outer.</summary>
    public static int[] GetDeltaChoices(int currentBpm)
    {
        var steps = GetStepSizes(currentBpm);
        return [-steps.Outer, -steps.Inner, 0, steps.Inner, steps.Outer];
    }

    /// <summary>Applies a BPM delta and clamps to the session tempo range (no wrap-around).</summary>
    public static int ApplyDelta(int currentBpm, int delta)
        => Math.Clamp(
            currentBpm + delta,
            NoteSessionService.MinTempo,
            NoteSessionService.MaxTempo);

    /// <summary>True when applying <paramref name="delta"/> would change the clamped tempo.</summary>
    public static bool WouldChange(int currentBpm, int delta)
        => ApplyDelta(currentBpm, delta) != Math.Clamp(
            currentBpm,
            NoteSessionService.MinTempo,
            NoteSessionService.MaxTempo);

    /// <summary>Label for a nudge button: absolute BPM for the center (0) slot.</summary>
    public static string FormatButtonLabel(int currentBpm, int delta)
    {
        if (delta == 0)
        {
            return Math.Clamp(currentBpm, NoteSessionService.MinTempo, NoteSessionService.MaxTempo)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return delta > 0 ? $"+{delta}" : $"−{Math.Abs(delta)}";
    }
}
