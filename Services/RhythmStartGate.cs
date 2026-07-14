using musicmate.Models;

namespace musicmate.Services;

/// <summary>
/// Builds per-pitched-note rhythm metadata from a displayed sequence (rests included)
/// for sustain/rest earliest-start gating.
/// </summary>
public static class RhythmStartGate
{
    /// <summary>
    /// Rest beats between pitched notes — gateBeats after previous minus prior note duration.
    /// Consecutive notes with no written rest return 0 (no rhythm gate).
    /// </summary>
    public static double RestGateBeatsAfterPrevious(double gateBeatsAfterPrevious, double priorDurationBeats)
        => Math.Max(0, gateBeatsAfterPrevious - priorDurationBeats);

    public static bool HasRestGapAfter(double gateBeatsAfterPrevious, double priorDurationBeats)
        => RestGateBeatsAfterPrevious(gateBeatsAfterPrevious, priorDurationBeats) > 0;

    /// <summary>
    /// Walks rhythm in display order; one entry per pitched note (rests skipped).
    /// <see cref="PitchRhythmSlot.GateBeatsAfterPrevious"/> is
    /// startBeat(i) − startBeat(i−1) = duration(i−1) + rests between i−1 and i.
    /// </summary>
    public static List<PitchRhythmSlot> BuildSlots(IEnumerable<GeneratedNote> rhythmOrder)
    {
        var slots = new List<PitchRhythmSlot>();
        double beat = 0;
        double? prevStartBeat = null;

        foreach (var n in rhythmOrder)
        {
            double dur = n.BeatDuration;
            if (n.IsRest)
            {
                beat += dur;
                continue;
            }

            double startBeat = n.BeatPosition ?? beat;
            double gateBeats = prevStartBeat.HasValue ? startBeat - prevStartBeat.Value : 0;
            slots.Add(new PitchRhythmSlot(startBeat, dur, gateBeats));
            prevStartBeat = startBeat;
            beat += dur;
        }

        return slots;
    }
}

public readonly record struct PitchRhythmSlot(double StartBeat, double DurationBeats, double GateBeatsAfterPrevious);
