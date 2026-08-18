using System.Security.Cryptography;
using System.Text;
using musicmate.Models;

namespace musicmate.Services
{
    /// <summary>
    /// Deterministic identity of generated musical content (not object identity, not title).
    /// </summary>
    public static class GeneratedTuneSignature
    {
        public static string FromGeneratedNotes(IEnumerable<GeneratedNote>? notes)
        {
            if (notes == null)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var n in notes)
            {
                if (sb.Length > 0)
                    sb.Append(';');

                if (n.IsRest)
                {
                    sb.Append("R|").Append(n.Duration).Append('|')
                        .Append(n.MeasureIndex ?? -1).Append('|')
                        .Append((n.BeatPosition ?? 0).ToString("0.###"));
                }
                else
                {
                    sb.Append(n.MidiNumber).Append('|')
                        .Append(n.SpelledName).Append('|')
                        .Append(n.Duration).Append('|')
                        .Append(n.Accidental).Append('|')
                        .Append(n.MeasureIndex ?? -1).Append('|')
                        .Append((n.BeatPosition ?? 0).ToString("0.###"));
                }
            }

            if (sb.Length == 0)
                return string.Empty;

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToHexString(hash);
        }

        public static string FromStaffNotes(
            IEnumerable<GeneratedNote>? upper,
            IEnumerable<GeneratedNote>? lower)
        {
            var combined = Enumerable.Empty<GeneratedNote>();
            if (upper != null)
                combined = combined.Concat(upper);
            if (lower != null)
                combined = combined.Concat(lower);
            return FromGeneratedNotes(combined);
        }
    }
}
