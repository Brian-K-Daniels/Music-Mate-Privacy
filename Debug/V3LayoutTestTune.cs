using System.Diagnostics;
using System.Linq;
using System.Text;
using musicmate.Models;
using Microsoft.Maui.Storage;

namespace musicmate.V3LayoutDebug
{
    /// <summary>
    /// TEMP DEBUG: fixed 4/4 tune for repeatable V3 staff layout testing.
    /// Remove this file and related hooks when layout debugging is done.
    /// </summary>
    public static class V3LayoutTestTune
    {
        public const string PreferenceKey = "Debug.UseFixedV3TestTune";

        public static bool IsEnabled =>
            Preferences.Default.Get(PreferenceKey, false);

        public static void SetEnabled(bool enabled) =>
            Preferences.Default.Set(PreferenceKey, enabled);

        /// <summary>
        /// Eight 4/4 measures (4 upper + 4 lower when split at midpoint).
        /// Every measure totals exactly 4.0 beats.
        /// </summary>
        public static PracticeTune Create()
        {
            var tune = new PracticeTune("V3 Layout Test Tune", TimeSignature.FourFour);

            // ── Upper staff (measures 0–3) ─────────────────────────────────────
            var m1 = tune.AppendMeasure();
            m1.AddNote(N(60, "C4", NoteDuration.Quarter));
            m1.AddNote(N(66, "F#4", NoteDuration.Quarter));
            m1.AddNote(N(67, "G4", NoteDuration.Quarter));
            m1.AddNote(N(69, "A4", NoteDuration.Quarter));

            var m2 = tune.AppendMeasure();
            m2.AddNote(N(60, "C4", NoteDuration.Eighth));
            m2.AddNote(N(62, "D4", NoteDuration.Eighth));
            m2.AddNote(N(64, "E4", NoteDuration.Eighth));
            m2.AddNote(N(65, "F4", NoteDuration.Eighth));
            m2.AddNote(N(67, "G4", NoteDuration.Eighth));
            m2.AddNote(N(69, "A4", NoteDuration.Eighth));
            m2.AddNote(N(71, "B4", NoteDuration.Eighth));
            m2.AddNote(N(72, "C5", NoteDuration.Eighth));

            var m3 = tune.AppendMeasure();
            m3.AddNote(MusicNote.Rest(NoteDuration.Quarter));
            m3.AddNote(N(70, "Bb4", NoteDuration.Quarter));
            m3.AddNote(N(69, "A4", NoteDuration.Eighth));
            m3.AddNote(N(67, "G4", NoteDuration.Eighth));
            m3.AddNote(N(65, "F4", NoteDuration.Eighth));
            m3.AddNote(N(64, "E4", NoteDuration.Eighth));

            var m4 = tune.AppendMeasure();
            m4.AddNote(N(60, "C4", NoteDuration.Sixteenth));
            m4.AddNote(N(62, "D4", NoteDuration.Sixteenth));
            m4.AddNote(N(64, "E4", NoteDuration.Sixteenth));
            m4.AddNote(N(65, "F4", NoteDuration.Sixteenth));
            m4.AddNote(N(67, "G4", NoteDuration.Sixteenth));
            m4.AddNote(N(69, "A4", NoteDuration.Sixteenth));
            m4.AddNote(N(71, "B4", NoteDuration.Sixteenth));
            m4.AddNote(N(72, "C5", NoteDuration.Sixteenth));
            m4.AddNote(N(74, "D5", NoteDuration.Quarter));
            m4.AddNote(N(76, "E5", NoteDuration.Quarter));

            // ── Lower staff (measures 4–7) ─────────────────────────────────────
            var m5 = tune.AppendMeasure();
            m5.AddNote(N(67, "G4", NoteDuration.Quarter));
            m5.AddNote(N(69, "A4", NoteDuration.Quarter));
            m5.AddNote(N(71, "B4", NoteDuration.Quarter));
            m5.AddNote(N(72, "C5", NoteDuration.Quarter));

            var m6 = tune.AppendMeasure();
            m6.AddNote(N(67, "G4", NoteDuration.Eighth));
            m6.AddNote(N(68, "Ab4", NoteDuration.Eighth));
            m6.AddNote(N(71, "B4", NoteDuration.Eighth));
            m6.AddNote(N(72, "C5", NoteDuration.Eighth));
            m6.AddNote(N(74, "D5", NoteDuration.Eighth));
            m6.AddNote(N(72, "C5", NoteDuration.Eighth));
            m6.AddNote(N(71, "B4", NoteDuration.Eighth));
            m6.AddNote(N(69, "A4", NoteDuration.Eighth));

            var m7 = tune.AppendMeasure();
            m7.AddNote(MusicNote.Rest(NoteDuration.Quarter));
            m7.AddNote(N(67, "G4", NoteDuration.Sixteenth));
            m7.AddNote(N(69, "A4", NoteDuration.Sixteenth));
            m7.AddNote(N(71, "B4", NoteDuration.Sixteenth));
            m7.AddNote(N(72, "C5", NoteDuration.Sixteenth));
            m7.AddNote(N(74, "D5", NoteDuration.Sixteenth));
            m7.AddNote(N(76, "E5", NoteDuration.Sixteenth));
            m7.AddNote(N(77, "F5", NoteDuration.Sixteenth));
            m7.AddNote(N(79, "G5", NoteDuration.Sixteenth));
            m7.AddNote(N(72, "C5", NoteDuration.Eighth));
            m7.AddNote(N(71, "B4", NoteDuration.Eighth));

            var m8 = tune.AppendMeasure();
            m8.AddNote(N(72, "C5", NoteDuration.Half));
            m8.AddNote(MusicNote.Rest(NoteDuration.Quarter));
            m8.AddNote(N(67, "G4", NoteDuration.Quarter));

            return tune;
        }

        public static string BuildLogText(PracticeTune tune)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[V3TestTune] \"{tune.Title}\" {tune.TimeSignature} — {tune.Measures.Count} measures");
            for (int mi = 0; mi < tune.Measures.Count; mi++)
            {
                var m = tune.Measures[mi];
                double beats = m.Notes.Sum(n => n.Duration.ToBeatValue());
                sb.AppendLine($"  M{mi + 1}: {beats:F2} beats ({m.Notes.Count} slots)");
                int slot = 0;
                foreach (var n in m.Notes)
                    sb.AppendLine($"    [{slot++}] {n}");
            }
            return sb.ToString();
        }

        public static void LogContents(PracticeTune tune)
        {
            var text = BuildLogText(tune);
            Debug.WriteLine(text);
            Utilities.Utils.Log(text);
        }

        private static MusicNote N(int midi, string name, NoteDuration duration)
            => new(midi, name, duration);
    }
}
