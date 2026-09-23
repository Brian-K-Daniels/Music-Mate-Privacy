using musicmate.Models;

namespace musicmate.Services;

/// <summary>
/// Ensures Music-page autoplay uses exactly the musical events assigned to displayed staves.
/// Count-in / metronome clicks are not part of this sequence.
/// </summary>
public static class DisplayedPlaybackSync
{
    public readonly record struct MusicalEventIdentity(
        string SpelledName,
        int MidiNumber,
        NoteDuration Duration,
        double BeatDuration);

    public sealed class DisplayedPlaybackAudit
    {
        public required int GeneratedPitchedCount { get; init; }
        public required int UpperPitchedCount { get; init; }
        public required int LowerPitchedCount { get; init; }
        public required int UnplacedPitchedCount { get; init; }
        public required int DisplayedPitchedCount { get; init; }
        public required int PlaybackPitchedCount { get; init; }
        public required IReadOnlyList<(int Index, MusicalEventIdentity Event)> PlaybackOnlyEvents { get; init; }

        public bool IsSynchronized =>
            PlaybackOnlyEvents.Count == 0
            && DisplayedPitchedCount == PlaybackPitchedCount;

        public string FormatStageDump(
            IReadOnlyList<GeneratedNote> generated,
            IReadOnlyList<GeneratedNote> upper,
            IReadOnlyList<GeneratedNote> lower,
            IReadOnlyList<GeneratedNote> playback)
        {
            var lines = new List<string>
            {
                FormatList("Generated", generated),
                FormatList("Upper staff visible", upper),
                FormatList("Lower staff visible", lower),
                FormatList("Playback sequence", playback),
                $"totals: generated={GeneratedPitchedCount} upper={UpperPitchedCount} lower={LowerPitchedCount} " +
                $"unplaced={UnplacedPitchedCount} displayed={DisplayedPitchedCount} playback={PlaybackPitchedCount}",
            };

            if (PlaybackOnlyEvents.Count > 0)
            {
                lines.Add("playback-only events:");
                foreach (var (index, ev) in PlaybackOnlyEvents)
                    lines.Add($"  {index} {ev.SpelledName}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatList(string label, IReadOnlyList<GeneratedNote> notes)
        {
            var lines = new List<string> { label + ":" };
            int pitchIdx = 0;
            for (int i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                if (n.IsRest)
                    continue;
                lines.Add($"  {pitchIdx} {n.SpelledName}");
                pitchIdx++;
            }

            lines.Add($"  count={pitchIdx}");
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>Rhythm order for displayed upper then lower staff (placed measures only).</summary>
    public static List<GeneratedNote> BuildDisplayedRhythmSequence(
        IReadOnlyList<GeneratedNote> upperNotes,
        IReadOnlyList<GeneratedNote> lowerNotes)
        => upperNotes.Concat(lowerNotes).ToList();

    public static IReadOnlyList<MusicalEventIdentity> ExtractPitchedMusicalEvents(
        IEnumerable<GeneratedNote> sequence)
        => sequence
            .Where(n => !n.IsRest)
            .Select(ToIdentity)
            .ToList();

    public static DisplayedPlaybackAudit AuditStaffAssignment(
        IReadOnlyList<GeneratedNote> generatedPage,
        IReadOnlyList<GeneratedNote> upperNotes,
        IReadOnlyList<GeneratedNote> lowerNotes,
        IReadOnlyList<GeneratedNote> unplacedNotes,
        IReadOnlyList<GeneratedNote>? playbackSequence = null)
    {
        playbackSequence ??= BuildDisplayedRhythmSequence(upperNotes, lowerNotes);

        var displayed = ExtractPitchedMusicalEvents(
            BuildDisplayedRhythmSequence(upperNotes, lowerNotes));
        var playback = ExtractPitchedMusicalEvents(playbackSequence);

        var playbackOnly = new List<(int, MusicalEventIdentity)>();
        int shared = Math.Min(displayed.Count, playback.Count);
        for (int i = 0; i < shared; i++)
        {
            if (!displayed[i].Equals(playback[i]))
                playbackOnly.Add((i, playback[i]));
        }

        for (int i = shared; i < playback.Count; i++)
            playbackOnly.Add((i, playback[i]));

        return new DisplayedPlaybackAudit
        {
            GeneratedPitchedCount = generatedPage.Count(n => !n.IsRest),
            UpperPitchedCount = upperNotes.Count(n => !n.IsRest),
            LowerPitchedCount = lowerNotes.Count(n => !n.IsRest),
            UnplacedPitchedCount = unplacedNotes.Count(n => !n.IsRest),
            DisplayedPitchedCount = displayed.Count,
            PlaybackPitchedCount = playback.Count,
            PlaybackOnlyEvents = playbackOnly,
        };
    }

    public static void AssertSynchronized(DisplayedPlaybackAudit audit, string context)
    {
        if (audit.IsSynchronized)
            return;

        throw new InvalidOperationException(
            $"{context}: playback/display mismatch — displayed={audit.DisplayedPitchedCount} " +
            $"playback={audit.PlaybackPitchedCount} playback-only={audit.PlaybackOnlyEvents.Count}");
    }

    private static MusicalEventIdentity ToIdentity(GeneratedNote note)
        => new(note.SpelledName, note.MidiNumber, note.Duration, note.BeatDuration);
}
