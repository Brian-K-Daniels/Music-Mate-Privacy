using musicmate.Models;

namespace musicmate.Services
{
    /// <summary>
    /// Builds staff display notes for Interval Ear Training from the exact written
    /// MIDI pitches that were sounded (no re-picking / approximation).
    /// </summary>
    public static class IntervalEarTrainingNotation
    {
        /// <summary>
        /// Two quarter notes in one measure, spelled for <paramref name="key"/>/<paramref name="scale"/>.
        /// </summary>
        public static List<GeneratedNote> BuildDisplayNotes(
            IntervalEarTrainingLogic.IntervalPitches pitches,
            string key,
            string scale)
        {
            var first = CreateNote(
                pitches.StartWrittenMidi, key, scale, prevMidi: -1,
                measureIndex: 0, beatPosition: 0);
            var second = CreateNote(
                pitches.EndWrittenMidi, key, scale, prevMidi: pitches.StartWrittenMidi,
                measureIndex: 0, beatPosition: 1);
            return new List<GeneratedNote> { first, second };
        }

        /// <summary>One measure spanning the two quarter notes (2 beats).</summary>
        public static List<double> BuildBarBeats()
            => new() { 2.0 };

        private static GeneratedNote CreateNote(
            int midi,
            string key,
            string scale,
            int prevMidi,
            int measureIndex,
            double beatPosition)
        {
            bool preferFlats = KeySignatureRules.KeySignatureUsesFlats(key, scale);
            string spelledName = EnsureSpellingMatchesMidi(
                NoteSessionService.SpellWrittenPitch(midi, key, scale, prevMidi),
                midi,
                preferFlats);

            char letter = char.ToUpperInvariant(spelledName[0]);
            int octave = NoteSessionService.ParseOctaveFromSpelledName(spelledName);
            var (accidental, finalSpelledName) = NoteSessionService.ResolveAccidentalAndSpelling(
                spelledName, midi, letter, octave, key, scale);

            return new GeneratedNote
            {
                MidiNumber = midi,
                Letter = letter,
                Octave = octave,
                Accidental = accidental,
                SpelledName = finalSpelledName,
                TargetFrequency = 440.0 * Math.Pow(2.0, (midi - 69) / 12.0),
                Duration = NoteDuration.Quarter,
                IsRest = false,
                MeasureIndex = measureIndex,
                BeatPosition = beatPosition,
                IsPlayedCorrectly = false,
            };
        }

        private static string EnsureSpellingMatchesMidi(string spelledName, int midi, bool preferFlats)
        {
            if (string.IsNullOrWhiteSpace(spelledName)
                || spelledName.Contains("##", StringComparison.Ordinal)
                || spelledName.Contains("bb", StringComparison.Ordinal)
                || NoteSessionService.NoteNameToMidi(spelledName) != midi)
            {
                return NoteSessionService.MidiToNoteName(midi, preferFlats);
            }

            return spelledName;
        }
    }
}
