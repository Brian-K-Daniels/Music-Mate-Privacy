using musicmate.Models;

namespace musicmate.Services
{
    /// <summary>
    /// Written pitch plus clef → vertical staff position, and the staff positions
    /// of key-signature accidentals. Independent of instrument transposition.
    /// </summary>
    public static class ClefStaffPosition
    {
        /// <summary>Treble middle line is B4. Bass middle line is D3.</summary>
        public static (char Letter, int Octave) MiddleLine(Clef clef)
            => clef == Clef.Bass ? ('D', 3) : ('B', 4);

        /// <summary>
        /// Diatonic steps below (+) or above (−) the clef's middle line.
        /// Bass: F3 (second line from the top) is −2; C4 (one ledger above) is −6.
        /// </summary>
        public static int StepsBelowMiddle(Clef clef, char letter, int octave)
        {
            var (midLetter, midOctave) = MiddleLine(clef);
            return DiatonicIndex(midLetter, midOctave) - DiatonicIndex(letter, octave);
        }

        /// <summary>
        /// Key-signature accidental positions in circle-of-fifths order.
        /// The pitch classes match treble and bass; the octaves do not.
        /// </summary>
        public static IReadOnlyList<(char Letter, int Octave)> KeySignaturePositions(Clef clef, bool flats)
        {
            if (clef == Clef.Bass)
            {
                return flats
                    ? BassFlats
                    : BassSharps;
            }

            return flats ? TrebleFlats : TrebleSharps;
        }

        // Treble: F5 C5 G5 D5 A4 E5 B4.
        private static readonly (char Letter, int Octave)[] TrebleSharps =
        {
            ('F', 5), ('C', 5), ('G', 5), ('D', 5), ('A', 4), ('E', 5), ('B', 4)
        };

        // Treble: B4 E5 A4 D5 G4 C5 F4.
        private static readonly (char Letter, int Octave)[] TrebleFlats =
        {
            ('B', 4), ('E', 5), ('A', 4), ('D', 5), ('G', 4), ('C', 5), ('F', 4)
        };

        // Bass staff lines (bottom→top) G2 B2 D3 F3 A3.
        // Sharps sit on F3 C3 G3 D3 A2 E3 B2 — not the treble octaves.
        private static readonly (char Letter, int Octave)[] BassSharps =
        {
            ('F', 3), ('C', 3), ('G', 3), ('D', 3), ('A', 2), ('E', 3), ('B', 2)
        };

        // Flats sit on B2 E3 A2 D3 G2 C3 F2. F2 is the space below the staff.
        private static readonly (char Letter, int Octave)[] BassFlats =
        {
            ('B', 2), ('E', 3), ('A', 2), ('D', 3), ('G', 2), ('C', 3), ('F', 2)
        };

        private static int DiatonicIndex(char letter, int octave)
        {
            int noteVal = char.ToUpperInvariant(letter) switch
            {
                'C' => 0,
                'D' => 1,
                'E' => 2,
                'F' => 3,
                'G' => 4,
                'A' => 5,
                'B' => 6,
                _ => 0
            };
            return noteVal + octave * 7;
        }
    }
}
