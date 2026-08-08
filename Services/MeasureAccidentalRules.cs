using musicmate.Models;

namespace musicmate.Services
{
    /// <summary>
    /// Standard within-measure accidental display rules shared by layout and rendering.
    /// </summary>
    public static class MeasureAccidentalRules
    {
        /// <summary>
        /// Whether a ♮ glyph should be printed for this letter+octave in the current measure.
        /// <paramref name="keySigAccidentalForLetter"/> is "#" or "b" when the key signature
        /// alters that letter; null when the letter is natural in the signature.
        /// <paramref name="priorInBar"/> is the last effective accidental for the same
        /// letter+octave earlier in this measure (null if none).
        /// </summary>
        public static bool ShouldDrawNatural(string? keySigAccidentalForLetter, Accidental? priorInBar)
        {
            // Same ♮ already active — carry without redrawing.
            if (priorInBar == Accidental.Natural)
                return false;

            // Cancel a key-signature flat/sharp.
            if (keySigAccidentalForLetter != null)
                return true;

            // Cancel a prior chromatic sharp/flat in this bar.
            if (priorInBar is Accidental.Sharp or Accidental.Flat
                or Accidental.DoubleSharp or Accidental.DoubleFlat)
                return true;

            return false;
        }
    }
}
