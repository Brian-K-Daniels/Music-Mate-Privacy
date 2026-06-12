using musicmate.Models;

namespace musicmate.Services;

/// <summary>
/// Maps a pitched note index in <see cref="NoteSessionService.NotesToDraw"/> to its
/// absolute beat position in the displayed rhythm (rests included).
/// </summary>
public readonly record struct RhythmTimingSlot(int PitchIndex, double StartBeat, double DurationBeats);

/// <summary>
/// Random listening tempo selection and rhythm-slot construction for child levels 50+.
/// Levels 50–60 are pitch-only; start/end timing is scored from level 61.
/// </summary>
public static class TimingSensitivity
{
    /// <summary>Metronome clicks played before the session clock starts (level 61+).</summary>
    public const int CountInBeats = 2;

    /// <summary>Child levels that participate in the listening-tempo feature band (50+).</summary>
    public static bool UsesListeningTempo(int childLevel) => childLevel >= 50;

    /// <summary>Child levels where start/end timing is scored.</summary>
    public static bool RequiresTimingJudgment(int childLevel) => childLevel >= 61;

    /// <summary>Alias for <see cref="RequiresTimingJudgment"/>.</summary>
    public static bool IsActive(int childLevel) => RequiresTimingJudgment(childLevel);

    public static int PickListeningTempo(Random rng, int level)
    {
        int[] choices = level switch
        {
            >= 80 and <= 100 => new[] { 90, 100, 120, 140 },
            >= 70 and <= 79  => new[] { 60, 80, 90, 100 },
            >= 61 and <= 69  => new[] { 40, 60, 80, 90 },
            _                => Array.Empty<int>()
        };
        return choices.Length == 0 ? 0 : choices[rng.Next(choices.Length)];
    }

    /// <summary>
    /// Walks the displayed rhythm in order, advancing the beat cursor for rests and
    /// emitting one slot per pitched note.
    /// </summary>
    public static (List<RhythmTimingSlot> Slots, double EndBeat) BuildSlots(
        IEnumerable<GeneratedNote> rhythmOrder,
        double beatOffset = 0,
        int startPitchIndex = 0)
    {
        var slots = new List<RhythmTimingSlot>();
        double beat = beatOffset;
        int pitchIdx = startPitchIndex;

        foreach (var n in rhythmOrder)
        {
            double dur = n.BeatDuration;
            if (!n.IsRest)
            {
                slots.Add(new RhythmTimingSlot(pitchIdx, beat, dur));
                pitchIdx++;
            }
            beat += dur;
        }

        return (slots, beat);
    }
}
