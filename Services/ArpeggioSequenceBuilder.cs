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
            if (rootMidi < 0)
                return new List<GeneratedNote>();

            var chordIntervals = BuildCoreChordIntervals(pattern);
            rootMidi = FitRootForFullChord(rootMidi, chordIntervals, minMidi, maxMidi);

            int root = rootMidi + chordIntervals[0];
            int third = rootMidi + chordIntervals[1];
            int fifth = rootMidi + chordIntervals[2];
            int octave = rootMidi + chordIntervals[3];

            var exercise = new (int MeasureOffset, double Beat, int Midi, NoteDuration Duration)[]
            {
                // M1: root, third, fifth, octave
                (0, 0.0, root, NoteDuration.Quarter),
                (0, 1.0, third, NoteDuration.Quarter),
                (0, 2.0, fifth, NoteDuration.Quarter),
                (0, 3.0, octave, NoteDuration.Quarter),

                // M2: octave, fifth, third, root
                (1, 0.0, octave, NoteDuration.Quarter),
                (1, 1.0, fifth, NoteDuration.Quarter),
                (1, 2.0, third, NoteDuration.Quarter),
                (1, 3.0, root, NoteDuration.Quarter),

                // M3: third, fifth, octave, fifth
                (2, 0.0, third, NoteDuration.Quarter),
                (2, 1.0, fifth, NoteDuration.Quarter),
                (2, 2.0, octave, NoteDuration.Quarter),
                (2, 3.0, fifth, NoteDuration.Quarter),

                // M4: third, root, root as a half note
                (3, 0.0, third, NoteDuration.Quarter),
                (3, 1.0, root, NoteDuration.Quarter),
                (3, 2.0, root, NoteDuration.Half),

                // M5: root, fifth, third, fifth
                (4, 0.0, root, NoteDuration.Quarter),
                (4, 1.0, fifth, NoteDuration.Quarter),
                (4, 2.0, third, NoteDuration.Quarter),
                (4, 3.0, fifth, NoteDuration.Quarter),

                // M6: octave, fifth, third, root
                (5, 0.0, octave, NoteDuration.Quarter),
                (5, 1.0, fifth, NoteDuration.Quarter),
                (5, 2.0, third, NoteDuration.Quarter),
                (5, 3.0, root, NoteDuration.Quarter),

                // M7: root, third, fifth, third
                (6, 0.0, root, NoteDuration.Quarter),
                (6, 1.0, third, NoteDuration.Quarter),
                (6, 2.0, fifth, NoteDuration.Quarter),
                (6, 3.0, third, NoteDuration.Quarter),

                // M8: root held for the full measure as two half-note slots.
                (7, 0.0, root, NoteDuration.Half),
                (7, 2.0, root, NoteDuration.Half)
            };

            return exercise
                .Select(slot => CreateNote(
                    slot.Midi,
                    slot.Duration,
                    StartMeasureIndex + slot.MeasureOffset,
                    StartBeatPosition + slot.MeasureOffset * 4.0 + slot.Beat))
                .ToList();
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

        private static int[] BuildCoreChordIntervals(ArpeggioPattern pattern)
        {
            var unique = pattern.SemitoneIntervals
                .Distinct()
                .OrderBy(interval => interval)
                .ToArray();

            int root = unique.FirstOrDefault();
            int third = FindInterval(unique, interval => interval is 3 or 4, fallback: 4);
            int fifth = FindInterval(unique, interval => interval is 6 or 7 or 8, fallback: 7);
            int octave = FindInterval(unique, interval => interval >= 12, fallback: 12);

            return new[] { root, third, fifth, octave };
        }

        private static int FindInterval(IEnumerable<int> intervals, Func<int, bool> predicate, int fallback)
        {
            foreach (var interval in intervals)
            {
                if (predicate(interval))
                    return interval;
            }

            return fallback;
        }

        private static int FitRootForFullChord(int rootMidi, IReadOnlyList<int> intervals, int minMidi, int maxMidi)
        {
            if (minMidi < 0 || maxMidi < 0 || minMidi > maxMidi)
                return rootMidi;

            int lowestOffset = intervals.Min();
            int highestOffset = intervals.Max();
            int candidate = rootMidi;

            while (candidate + lowestOffset < minMidi)
                candidate += 12;

            while (candidate + highestOffset > maxMidi && candidate - 12 + lowestOffset >= minMidi)
                candidate -= 12;

            return candidate;
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
