using System.Text;
using musicmate.Models;

namespace musicmate.Services
{
    /// <summary>
    /// Builds plain <see cref="GeneratedNote"/> sequences from arpeggio catalog data.
    /// This does not participate in Random generation yet.
    /// </summary>
    public sealed class ArpeggioSequenceBuilder
    {
        public string Key { get; init; } = "C";
        public string Scale { get; init; } = "Major";
        public string LowestNote { get; init; } = "C4";
        public string HighestNote { get; init; } = "C6";
        public NoteDuration Duration { get; init; } = NoteDuration.Quarter;
        public int StartMeasureIndex { get; init; } = 0;
        public double StartBeatPosition { get; init; } = 0.0;

        public List<GeneratedNote> Build(
            ArpeggioPattern pattern,
            string rootNote,
            bool descendingAfterAscending = true)
        {
            if (pattern.SemitoneIntervals.Count == 0)
                return new List<GeneratedNote>();

            int rootMidi = NoteSessionService.NoteNameToMidi(rootNote);
            int minMidi = NoteSessionService.NoteNameToMidi(LowestNote);
            int maxMidi = NoteSessionService.NoteNameToMidi(HighestNote);
            if (rootMidi < 0 || minMidi < 0 || maxMidi < 0 || minMidi > maxMidi)
                return new List<GeneratedNote>();

            var ascending = pattern.SemitoneIntervals
                .Select(interval => rootMidi + interval)
                .Where(midi => midi >= minMidi && midi <= maxMidi)
                .Distinct()
                .OrderBy(midi => midi)
                .ToList();

            if (ascending.Count == 0)
                return new List<GeneratedNote>();

            var sequence = new List<int>(ascending);
            if (descendingAfterAscending && ascending.Count > 1)
            {
                for (int i = ascending.Count - 2; i >= 0; i--)
                    sequence.Add(ascending[i]);
            }

            var notes = new List<GeneratedNote>(sequence.Count);
            double beat = StartBeatPosition;
            for (int i = 0; i < sequence.Count; i++)
            {
                notes.Add(CreateNote(sequence[i], Duration, StartMeasureIndex, beat));
                beat += Duration.ToBeatValue();
            }

            return notes;
        }

        public List<GeneratedNote> Build(
            ArpeggioPattern pattern,
            int rootMidi,
            bool descendingAfterAscending = true)
        {
            bool preferFlats = PreferFlats(Key);
            return Build(pattern, NoteSessionService.MidiToNoteName(rootMidi, preferFlats),
                descendingAfterAscending);
        }

        public static string BuildDebugVerificationReport()
        {
            var sb = new StringBuilder();
            AppendCase(
                sb,
                title: "C major root",
                builder: new ArpeggioSequenceBuilder
                {
                    Key = "C",
                    Scale = "Major",
                    LowestNote = "C4",
                    HighestNote = "C5"
                },
                pattern: ArpeggioCatalog.MajorTriad,
                rootNote: "C4");

            AppendCase(
                sb,
                title: "A minor root",
                builder: new ArpeggioSequenceBuilder
                {
                    Key = "A",
                    Scale = "Natural Minor",
                    LowestNote = "A3",
                    HighestNote = "A4"
                },
                pattern: ArpeggioCatalog.MinorTriad,
                rootNote: "A3");

            AppendCase(
                sb,
                title: "Bb major root",
                builder: new ArpeggioSequenceBuilder
                {
                    Key = "Bb",
                    Scale = "Major",
                    LowestNote = "Bb3",
                    HighestNote = "Bb4"
                },
                pattern: ArpeggioCatalog.MajorTriad,
                rootNote: "Bb3");

            AppendCase(
                sb,
                title: "Range clipping",
                builder: new ArpeggioSequenceBuilder
                {
                    Key = "C",
                    Scale = "Major",
                    LowestNote = "E4",
                    HighestNote = "G4"
                },
                pattern: ArpeggioCatalog.MajorTriad,
                rootNote: "C4");

            return sb.ToString();
        }

        private GeneratedNote CreateNote(
            int midi,
            NoteDuration duration,
            int measureIndex,
            double beatPosition)
        {
            bool preferFlats = PreferFlats(Key);
            string spelledName = NoteSessionService.MidiToNoteName(midi, preferFlats);
            char letter = char.ToUpperInvariant(spelledName[0]);
            int octave = int.TryParse(spelledName[^1].ToString(), out var parsedOctave)
                ? parsedOctave
                : 4;

            var (accidental, finalSpelledName) = NoteSessionService.ResolveAccidentalAndSpelling(
                spelledName, midi, letter, octave, Key, Scale);

            return new GeneratedNote
            {
                MidiNumber = midi,
                Letter = letter,
                Octave = octave,
                Accidental = accidental,
                SpelledName = finalSpelledName,
                TargetFrequency = NoteSessionService.MidiToFreqPublic(midi),
                Duration = duration,
                IsRest = false,
                MeasureIndex = measureIndex,
                BeatPosition = beatPosition,
                IsPlayedCorrectly = false
            };
        }

        private static void AppendCase(
            StringBuilder sb,
            string title,
            ArpeggioSequenceBuilder builder,
            ArpeggioPattern pattern,
            string rootNote)
        {
            var notes = builder.Build(pattern, rootNote);
            sb.Append(title)
                .Append(": ")
                .AppendJoin(" ", notes.Select(note => note.SpelledName))
                .AppendLine();
        }

        private static bool PreferFlats(string key)
            => key is "F" or "Bb" or "Eb" or "Ab" or "Db" or "Gb" or "Cb";
    }
}
