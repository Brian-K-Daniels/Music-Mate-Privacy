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
                Aliases: ["Double Bass", "Contrabassoon", "C - 1 octave", "C - 1 octave,  Double Bass, Contrabassoon"])
        ];

        public static string[] DisplayNames => All.Select(profile => profile.DisplayName).ToArray();

        public static InstrumentProfile Default => All[0];

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
            level = Math.Clamp(level <= 0 ? 100 : level, 1, 100);
            int practicalLow = NoteSessionService.NoteNameToMidi(profile.PracticalLowestNote);
            int practicalHigh = NoteSessionService.NoteNameToMidi(profile.PracticalHighestNote);
            if (practicalLow < 0 || practicalHigh <= practicalLow)
                return Array.Empty<int>();

            var (spanLow, spanHigh) = GetLevelWindow(profile, level);
            int low = Math.Max(practicalLow, spanLow);
            int high = Math.Min(practicalHigh, spanHigh);
            if (high < low)
            {
                low = practicalLow;
                high = practicalHigh;
            }

            return Enumerable.Range(low, high - low + 1).ToArray();
        }

        public static (string Lowest, string Highest) GetAutomaticRange(InstrumentProfile profile, int level)
        {
            var notes = BuildAvailableMidiSet(profile, level);
            if (notes.Count == 0)
                return (profile.PracticalLowestNote, profile.PracticalHighestNote);

            return (
                NoteSessionService.MidiToNoteName(notes.Min(), PreferFlats(profile.InstrumentKey)),
                NoteSessionService.MidiToNoteName(notes.Max(), PreferFlats(profile.InstrumentKey)));
        }

        private static (int Low, int High) GetLevelWindow(InstrumentProfile profile, int level)
        {
            var anchor = NoteSessionService.NoteNameToMidi(AnchorFor(profile));
            int halfSpan = level switch
            {
                <= 5 => 3,
                <= 20 => 6,
                <= 35 => 9,
                <= 50 => 14,
                <= 75 => 20,
                _ => 48
            };

            return (anchor - halfSpan, anchor + halfSpan);
        }

        private static string AnchorFor(InstrumentProfile profile)
            => profile.Id switch
            {
                "glockenspiel" => "C6",
                "piccolo" => "D5",
                "double-bass" => "E3",
                _ => "C4"
            };

        private static bool PreferFlats(string key)
            => key.Contains('b') || key is "F" or "Bb" or "Eb" or "Ab" or "Db" or "Gb" or "Cb";
    }
}
