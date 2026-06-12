using musicmate.Models;

namespace musicmate.Services;

/// <summary>
/// Builds per-pitched-note rhythm metadata from a displayed sequence (rests included)
/// for sustain/rest earliest-start gating.
/// </summary>
public static class RhythmStartGate
{
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
