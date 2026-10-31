using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

public class RhythmStartGateTests
{
    [Fact]
    public void RestGateBeats_ConsecutiveQuarters_HasNoRestGap()
    {
        var slots = RhythmStartGate.BuildSlots(new[]
        {
            Note(60),
            Note(62),
        });

        Assert.Equal(1, slots[1].GateBeatsAfterPrevious);
        Assert.Equal(0, RhythmStartGate.RestGateBeatsAfterPrevious(
            slots[1].GateBeatsAfterPrevious, slots[0].DurationBeats));
        Assert.False(RhythmStartGate.HasRestGapAfter(
            slots[1].GateBeatsAfterPrevious, slots[0].DurationBeats));
    }

    [Fact]
    public void RestGateBeats_QuarterThenRestThenQuarter_HasRestGap()
    {
        var slots = RhythmStartGate.BuildSlots(new[]
        {
            Note(60),
            Rest(),
            Note(62),
        });

        Assert.Equal(2, slots[1].GateBeatsAfterPrevious);
        Assert.Equal(1, RhythmStartGate.RestGateBeatsAfterPrevious(
            slots[1].GateBeatsAfterPrevious, slots[0].DurationBeats));
        Assert.True(RhythmStartGate.HasRestGapAfter(
            slots[1].GateBeatsAfterPrevious, slots[0].DurationBeats));
    }

    private static GeneratedNote Note(int midi)
        => new()
        {
            MidiNumber = midi,
            Letter = 'C',
            Octave = 4,
            SpelledName = "C4",
            TargetFrequency = 261.63,
            Duration = NoteDuration.Quarter,
            IsRest = false,
        };

    private static GeneratedNote Rest()
        => GeneratedNote.Rest(NoteDuration.Quarter);
}
