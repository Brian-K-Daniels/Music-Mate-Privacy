using musicmate.Models;

namespace musicmate.Services
{
    public static class InstrumentCatalog
    {
        public static IReadOnlyList<InstrumentProfile> All { get; } =
        [
            new(
                Id: "concert-pitch",
                DisplayName: "Concert Pitch",
                InstrumentKey: "C",
                LegacyInstrumentValue: "C, Flute Oboe Bassoon Trumpet Trombone Euphoneum Tuba Piano",
                TransposeOffset: 0,
                PracticalLowestNote: "G3",
                PracticalHighestNote: "C6",
                Aliases:
                [
                    "C",
                    "C, Flute Oboe Bassoon Trumpet Trombone Euphoneum Tuba Piano",
                    "Flute", "Oboe", "Bassoon", "Trombone", "Euphonium", "Tuba", "Piano"
                ]),
            new(
                Id: "bb-clarinet",
                DisplayName: "Bb Clarinet",
                InstrumentKey: "Bb",
                LegacyInstrumentValue: "Bb,            Clarinet, Soprano Sax, Trumpet",
                TransposeOffset: -2,
                PracticalLowestNote: "E3",
                PracticalHighestNote: "G6",
                Aliases: ["Bb", "Bb Clarinet", "Bb,            Clarinet, Soprano Sax, Trumpet"]),
            new(
                Id: "bb-trumpet",
                DisplayName: "Bb Trumpet",
                InstrumentKey: "Bb",
                LegacyInstrumentValue: "Bb,            Clarinet, Soprano Sax, Trumpet",
                TransposeOffset: -2,
                PracticalLowestNote: "F#3",
                PracticalHighestNote: "C6",
                Aliases: ["Bb Trumpet", "Trumpet"]),
            new(
                Id: "soprano-sax",
                DisplayName: "Soprano Saxophone",
                InstrumentKey: "Bb",
                LegacyInstrumentValue: "Bb,            Clarinet, Soprano Sax, Trumpet",
                TransposeOffset: -2,
                PracticalLowestNote: "Ab3",
                PracticalHighestNote: "F6",
                Aliases: ["Soprano Sax", "Soprano Saxophone"]),
            new(
                Id: "a-clarinet",
                DisplayName: "A Clarinet",
                InstrumentKey: "A",
                LegacyInstrumentValue: "A,             Clarinet",
                TransposeOffset: -3,
                PracticalLowestNote: "E3",
                PracticalHighestNote: "G6",
                Aliases: ["A", "A Clarinet", "A,             Clarinet"]),
            new(
                Id: "f-horn",
                DisplayName: "F Horn",
                InstrumentKey: "F",
                LegacyInstrumentValue: "F,             English Horn, French Horn",
                TransposeOffset: -7,
                PracticalLowestNote: "F3",
                PracticalHighestNote: "G6",
                Aliases: ["F", "F Horn", "French Horn", "English Horn", "F,             English Horn, French Horn"]),
            new(
                Id: "eb-alto-sax",
                DisplayName: "Eb Alto Saxophone",
                InstrumentKey: "Eb",
                LegacyInstrumentValue: "Eb,            Alto Clarinet, Alto Sax",
                TransposeOffset: -9,
                PracticalLowestNote: "Bb3",
                PracticalHighestNote: "F6",
                Aliases: ["Eb Alto Sax", "Eb Alto Saxophone", "Alto Sax", "Eb,            Alto Clarinet, Alto Sax"]),
            new(
                Id: "eb-clarinet",
                DisplayName: "Eb Clarinet",
                InstrumentKey: "Eb",
                LegacyInstrumentValue: "Eb,            Clarinet",
                TransposeOffset: 3,
                PracticalLowestNote: "E3",
                PracticalHighestNote: "G6",
                Aliases: ["Eb Clarinet", "Eb,            Clarinet"]),
            new(
                Id: "piccolo",
                DisplayName: "Piccolo",
                InstrumentKey: "C + 1 octave",
                LegacyInstrumentValue: "C + 1 octave,  Piccolo",
                TransposeOffset: 12,
                PracticalLowestNote: "D4",
                PracticalHighestNote: "C7",
                Aliases: ["Piccolo", "C + 1 octave", "C + 1 octave,  Piccolo"]),
            new(
                Id: "glockenspiel",
                DisplayName: "Glockenspiel",
                InstrumentKey: "C + 2 octaves",
                LegacyInstrumentValue: "C + 2 octaves, Glockenspiel",
                TransposeOffset: 24,
                PracticalLowestNote: "F4",
                PracticalHighestNote: "C8",
                Aliases: ["Glockenspiel", "C + 2 octaves", "C + 2 octaves, Glockenspiel"]),
            new(
                Id: "tenor-sax",
                DisplayName: "Tenor Saxophone",
                InstrumentKey: "Bb - 1 octave",
                LegacyInstrumentValue: "Bb - 1 octave, Tenor Sax, Bass Clarinet",
                TransposeOffset: -14,
                PracticalLowestNote: "Ab3",
                PracticalHighestNote: "F6",
                Aliases: ["Tenor Sax", "Tenor Saxophone", "Bb - 1 octave", "Bb - 1 octave, Tenor Sax, Bass Clarinet"]),
            new(
                Id: "baritone-sax",
                DisplayName: "Baritone Saxophone",
                InstrumentKey: "Eb - 1 octave",
                LegacyInstrumentValue: "Eb - 1 octave, Baritone Sax",
                TransposeOffset: -21,
                PracticalLowestNote: "Bb3",
                PracticalHighestNote: "F6",
                Aliases: ["Baritone Sax", "Baritone Saxophone", "Eb - 1 octave", "Eb - 1 octave, Baritone Sax"]),
            new(
                Id: "double-bass",
                DisplayName: "Double Bass",
                InstrumentKey: "C - 1 octave",
                LegacyInstrumentValue: "C - 1 octave,  Double Bass, Contrabassoon",
                TransposeOffset: -12,
                PracticalLowestNote: "E2",
                PracticalHighestNote: "G4",
                Aliases: ["Double Bass", "Contrabassoon", "C - 1 octave", "C - 1 octave,  Double Bass, Contrabassoon"]),
            // Concert-pitch voice categories (transposition key C, offset 0).
            new(
                Id: "voice-soprano",
                DisplayName: "Voice – Soprano",
                InstrumentKey: "C",
                LegacyInstrumentValue: "Voice – Soprano",
                TransposeOffset: 0,
                PracticalLowestNote: "C4",
                PracticalHighestNote: "C6",
                Aliases: ["Voice – Soprano", "Voice - Soprano", "Soprano", "voice-soprano"]),
            new(
                Id: "voice-mezzo-soprano",
                DisplayName: "Voice – Mezzo-soprano",
                InstrumentKey: "C",
                LegacyInstrumentValue: "Voice – Mezzo-soprano",
                TransposeOffset: 0,
                PracticalLowestNote: "A3",
                PracticalHighestNote: "A5",
                Aliases: ["Voice – Mezzo-soprano", "Voice - Mezzo-soprano", "Mezzo-soprano", "Mezzo", "voice-mezzo-soprano"]),
            new(
                Id: "voice-contralto",
                DisplayName: "Voice – Contralto",
                InstrumentKey: "C",
                LegacyInstrumentValue: "Voice – Contralto",
                TransposeOffset: 0,
                PracticalLowestNote: "F3",
                PracticalHighestNote: "F5",
                Aliases: ["Voice – Contralto", "Voice - Contralto", "Contralto", "Alto", "voice-contralto"]),
            new(
                Id: "voice-countertenor",
                DisplayName: "Voice – Countertenor",
                InstrumentKey: "C",
                LegacyInstrumentValue: "Voice – Countertenor",
                TransposeOffset: 0,
                PracticalLowestNote: "G3",
                PracticalHighestNote: "E5",
                Aliases: ["Voice – Countertenor", "Voice - Countertenor", "Countertenor", "voice-countertenor"]),
            new(
                Id: "voice-tenor",
                DisplayName: "Voice – Tenor",
                InstrumentKey: "C",
                LegacyInstrumentValue: "Voice – Tenor",
                TransposeOffset: 0,
                PracticalLowestNote: "C3",
                PracticalHighestNote: "C5",
                Aliases: ["Voice – Tenor", "Voice - Tenor", "Tenor", "voice-tenor"]),
            new(
                Id: "voice-baritone",
                DisplayName: "Voice – Baritone",
                InstrumentKey: "C",
                LegacyInstrumentValue: "Voice – Baritone",
                TransposeOffset: 0,
                PracticalLowestNote: "A2",
                PracticalHighestNote: "A4",
                Aliases: ["Voice – Baritone", "Voice - Baritone", "Baritone", "voice-baritone"]),
            new(
                Id: "voice-bass-baritone",
                DisplayName: "Voice – Bass-baritone",
                InstrumentKey: "C",
                LegacyInstrumentValue: "Voice – Bass-baritone",
                TransposeOffset: 0,
                PracticalLowestNote: "F2",
                PracticalHighestNote: "F4",
                Aliases: ["Voice – Bass-baritone", "Voice - Bass-baritone", "Bass-baritone", "voice-bass-baritone"]),
            new(
                Id: "voice-bass",
                DisplayName: "Voice – Bass",
                InstrumentKey: "C",
                LegacyInstrumentValue: "Voice – Bass",
                TransposeOffset: 0,
                PracticalLowestNote: "E2",
                PracticalHighestNote: "E4",
                Aliases: ["Voice – Bass", "Voice - Bass", "Bass", "voice-bass"])
        ];

        public static string[] DisplayNames => All.Select(profile => profile.DisplayName).ToArray();

        public static InstrumentProfile Default => All[0];

        /// <summary>
        /// Index in <see cref="DisplayNames"/> / <see cref="NoteSessionService.InstrumentOptions"/>
        /// for a stored id, display name, alias, or instrument key.
        /// </summary>
        public static int IndexOfOption(string? value)
        {
            var display = Resolve(value).DisplayName;
            return Array.IndexOf(DisplayNames, display);
        }

        public static InstrumentProfile Resolve(string? value)
        {
            var raw = value?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(raw))
                return Default;

            var exact = All.FirstOrDefault(profile =>
                string.Equals(profile.DisplayName, raw, StringComparison.Ordinal)
                || string.Equals(profile.LegacyInstrumentValue, raw, StringComparison.Ordinal)
                || string.Equals(profile.Id, raw, StringComparison.Ordinal));
            if (exact != null)
                return exact;

            var alias = All.FirstOrDefault(profile =>
                profile.Aliases.Any(a => string.Equals(a, raw, StringComparison.OrdinalIgnoreCase)));
            if (alias != null)
                return alias;

            var shortKey = raw.Split(',')[0].Trim();
            return All.FirstOrDefault(profile =>
                string.Equals(profile.InstrumentKey, shortKey, StringComparison.OrdinalIgnoreCase)
                || profile.Aliases.Any(a => string.Equals(a, shortKey, StringComparison.OrdinalIgnoreCase)))
                ?? Default;
        }

        public static IReadOnlyList<int> BuildAvailableMidiSet(InstrumentProfile profile, int level)
        {
            int practicalLow = NoteSessionService.NoteNameToMidi(profile.PracticalLowestNote);
            int practicalHigh = NoteSessionService.NoteNameToMidi(profile.PracticalHighestNote);
            if (practicalLow < 0 || practicalHigh <= practicalLow)
                return Array.Empty<int>();

            // No child level: full practical instrument range (matches GetAutomaticRange).
            if (level <= 0)
                return Enumerable.Range(practicalLow, practicalHigh - practicalLow + 1).ToArray();

            level = Math.Clamp(level, 1, 100);

            var (levelLo, levelHi) = ChildLevelProgression.NoteRangeForLevel(level);
            int low = Math.Max(practicalLow, NoteSessionService.NoteNameToMidi(levelLo));
            int high = Math.Min(practicalHigh, NoteSessionService.NoteNameToMidi(levelHi));
            if (high < low)
            {
                low = practicalLow;
                high = practicalHigh;
            }

            return Enumerable.Range(low, high - low + 1).ToArray();
        }

        public static (string Lowest, string Highest) GetAutomaticRange(InstrumentProfile profile, int level)
        {
            if (level <= 0)
                return (profile.PracticalLowestNote, profile.PracticalHighestNote);

            var notes = BuildAvailableMidiSet(profile, level);
            if (notes.Count == 0)
                return (profile.PracticalLowestNote, profile.PracticalHighestNote);

            return (
                NoteSessionService.MidiToNoteName(notes.Min(), PreferFlats(profile.InstrumentKey)),
                NoteSessionService.MidiToNoteName(notes.Max(), PreferFlats(profile.InstrumentKey)));
        }

        /// <summary>
        /// Practical MIDI bounds covering every instrument/voice in the catalog.
        /// </summary>
        public static (int LowMidi, int HighMidi) GetCatalogPracticalMidiBounds()
        {
            int low = int.MaxValue;
            int high = int.MinValue;
            foreach (var profile in All)
            {
                int lo = NoteSessionService.NoteNameToMidi(profile.PracticalLowestNote);
                int hi = NoteSessionService.NoteNameToMidi(profile.PracticalHighestNote);
                if (lo >= 0) low = Math.Min(low, lo);
                if (hi >= 0) high = Math.Max(high, hi);
            }

            if (low > high)
                return (NoteSessionService.NoteNameToMidi("A0"), NoteSessionService.NoteNameToMidi("C8"));

            return (low, high);
        }

        /// <summary>
        /// Note names for low/high pickers: white keys across the catalog union, plus every
        /// instrument/voice practical endpoint so no configured extreme is missing.
        /// Ordered high→low for picker display.
        /// </summary>
        public static string[] BuildNoteRangePickerNames(bool preferFlats = false)
        {
            var (lowMidi, highMidi) = GetCatalogPracticalMidiBounds();
            var byMidi = new SortedDictionary<int, string>(Comparer<int>.Create((a, b) => b.CompareTo(a)));

            for (int midi = lowMidi; midi <= highMidi; midi++)
            {
                string name = NoteSessionService.MidiToNoteName(midi, preferFlats);
                if (name.Contains('#') || name.Contains('b'))
                    continue;
                byMidi[midi] = name;
            }

            foreach (var profile in All)
            {
                AddEndpoint(byMidi, profile.PracticalLowestNote, preferFlats);
                AddEndpoint(byMidi, profile.PracticalHighestNote, preferFlats);
            }

            return byMidi.Values.ToArray();
        }

        private static void AddEndpoint(
            SortedDictionary<int, string> byMidi, string noteName, bool preferFlats)
        {
            int midi = NoteSessionService.NoteNameToMidi(noteName);
            if (midi < 0)
                return;
            // Keep the catalog spelling when it differs from the default enharmonic.
            byMidi[midi] = string.IsNullOrWhiteSpace(noteName)
                ? NoteSessionService.MidiToNoteName(midi, preferFlats)
                : noteName;
        }

        private static bool PreferFlats(string key)
            => key.Contains('b') || key is "F" or "Bb" or "Eb" or "Ab" or "Db" or "Gb" or "Cb";
    }
}
