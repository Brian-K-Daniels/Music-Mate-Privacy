using musicmate.Models;

namespace musicmate.Services
{
    /// <summary>
    /// Written-pitch choices for the Tuner reference-tone (tuning fork) controls.
    /// Picker labels may show both enharmonic names; staff spelling is a single name.
    /// </summary>
    public sealed record TunerReferenceNoteChoice(
        int WrittenMidi,
        string PickerLabel,
        string StaffSpellingAscii);

    public static class TunerReferenceNoteCatalog
    {
        /// <summary>Persisted written MIDI for the Tuner reference-note selection.</summary>
        public const string PreferenceKeyWrittenMidi = "TunerReferenceWrittenMidi";

        /// <summary>
        /// Full practical written MIDI range for the instrument (ignores child-level narrowing).
        /// </summary>
        public static IReadOnlyList<int> GetPracticalWrittenMidis(InstrumentProfile profile)
            => InstrumentCatalog.BuildAvailableMidiSet(profile, level: 0);

        public static IReadOnlyList<TunerReferenceNoteChoice> BuildChoices(
            InstrumentProfile profile,
            bool preferFlatsForStaff)
        {
            var midis = GetPracticalWrittenMidis(profile);
            if (midis.Count == 0)
                return Array.Empty<TunerReferenceNoteChoice>();

            // Highest pitch at the top of the picker; lowest at the bottom.
            var list = new List<TunerReferenceNoteChoice>(midis.Count);
            for (int i = midis.Count - 1; i >= 0; i--)
            {
                int midi = midis[i];
                list.Add(new TunerReferenceNoteChoice(
                    midi,
                    FormatPickerLabel(midi),
                    NoteSessionService.MidiToNoteName(midi, preferFlatsForStaff)));
            }
            return list;
        }

        /// <summary>
        /// Dropdown item text (no "Written" prefix). Naturals are a single name;
        /// black keys show both enharmonic spellings with ♯ ♭.
        /// </summary>
        public static string FormatPickerLabel(int writtenMidi)
        {
            int pc = ((writtenMidi % 12) + 12) % 12;
            int oct = (writtenMidi / 12) - 1;
            return pc switch
            {
                0 => $"C{oct}",
                1 => $"C♯{oct} / D♭{oct}",
                2 => $"D{oct}",
                3 => $"D♯{oct} / E♭{oct}",
                4 => $"E{oct}",
                5 => $"F{oct}",
                6 => $"F♯{oct} / G♭{oct}",
                7 => $"G{oct}",
                8 => $"G♯{oct} / A♭{oct}",
                9 => $"A{oct}",
                10 => $"A♯{oct} / B♭{oct}",
                11 => $"B{oct}",
                _ => NoteSessionService.MidiToNoteName(writtenMidi, flats: false)
            };
        }

        /// <summary>Closed-picker display for the selected reference note, e.g. "Written C5".</summary>
        public static string FormatWrittenDisplayLabel(int writtenMidi, bool preferFlats)
        {
            string ascii = NoteSessionService.MidiToNoteName(writtenMidi, preferFlats);
            return "Written " + ToUnicodeAccidentals(ascii);
        }

        /// <summary>
        /// Closed-picker display while listening: both enharmonics (flat first) plus cents,
        /// e.g. "Written E♭5 / D♯5 + 9¢".
        /// </summary>
        public static string FormatHeardDisplayLabel(int writtenMidi, int cents)
        {
            string pair = FormatEnharmonicPairFlatFirst(writtenMidi);
            string centsText = cents switch
            {
                0 => "0¢",
                > 0 => $"+ {cents}¢",
                _ => $"- {Math.Abs(cents)}¢"
            };
            return $"Written {pair} {centsText}";
        }

        /// <summary>Flat spelling first, then sharp (matches Tuner heard readout examples).</summary>
        public static string FormatEnharmonicPairFlatFirst(int writtenMidi)
        {
            int pc = ((writtenMidi % 12) + 12) % 12;
            int oct = (writtenMidi / 12) - 1;
            return pc switch
            {
                0 => $"C{oct}",
                1 => $"D♭{oct} / C♯{oct}",
                2 => $"D{oct}",
                3 => $"E♭{oct} / D♯{oct}",
                4 => $"E{oct}",
                5 => $"F{oct}",
                6 => $"G♭{oct} / F♯{oct}",
                7 => $"G{oct}",
                8 => $"A♭{oct} / G♯{oct}",
                9 => $"A{oct}",
                10 => $"B♭{oct} / A♯{oct}",
                11 => $"B{oct}",
                _ => NoteSessionService.MidiToNoteName(writtenMidi, flats: true)
            };
        }

        public static string ToUnicodeAccidentals(string asciiNoteName)
        {
            if (string.IsNullOrEmpty(asciiNoteName))
                return asciiNoteName;
            return asciiNoteName
                .Replace("##", "𝄪", StringComparison.Ordinal)
                .Replace("bb", "𝄫", StringComparison.Ordinal)
                .Replace("#", "♯", StringComparison.Ordinal)
                .Replace("b", "♭", StringComparison.Ordinal);
        }

        /// <summary>
        /// Concert MIDI for a written MIDI using <see cref="InstrumentProfile.TransposeOffset"/>.
        /// Convention (negative offset = sounds lower than written):
        /// writtenMidi = concertMidi - offset, so concertMidi = writtenMidi + offset.
        /// Applied exactly once — do not add further octave or transpose shifts.
        /// </summary>
        public static int ToConcertMidi(int writtenMidi, int transposeOffset)
            => writtenMidi + transposeOffset;

        /// <summary>
        /// Concert frequency for a written MIDI using <see cref="InstrumentProfile.TransposeOffset"/>.
        /// </summary>
        public static double ConcertFrequencyHz(int writtenMidi, int transposeOffset)
            => NoteSessionService.MidiToFreqPublic(ToConcertMidi(writtenMidi, transposeOffset));

        /// <summary>
        /// Nearest MIDI in <paramref name="midis"/> (order-independent — picker choices are high→low).
        /// </summary>
        public static int ClampToRange(int writtenMidi, IReadOnlyList<int> midis)
        {
            if (midis.Count == 0)
                return writtenMidi;

            int best = midis[0];
            int bestDist = Math.Abs(writtenMidi - best);
            for (int i = 1; i < midis.Count; i++)
            {
                int dist = Math.Abs(writtenMidi - midis[i]);
                if (dist < bestDist)
                {
                    best = midis[i];
                    bestDist = dist;
                }
            }
            return best;
        }

        public static int DefaultMiddleMidi(IReadOnlyList<int> midis)
        {
            if (midis.Count == 0)
                return NoteSessionService.NoteNameToMidi("C4");

            // Prefer ascending span so "middle" is pitch-middle regardless of list order.
            int lo = midis[0];
            int hi = midis[0];
            for (int i = 1; i < midis.Count; i++)
            {
                if (midis[i] < lo) lo = midis[i];
                if (midis[i] > hi) hi = midis[i];
            }
            int target = (lo + hi) / 2;
            return ClampToRange(target, midis);
        }
    }
}
