namespace musicmate.Services
{
    /// <summary>
    /// Pure music-theory rules for key signatures: circle-of-fifths counts,
    /// relative-major mapping for minor-family scales, and which pitch-classes
    /// are altered by the signature.
    /// </summary>
    public static class KeySignatureRules
    {
        public static readonly char[] FlatLetterOrder = { 'B', 'E', 'A', 'D', 'G', 'C', 'F' };
        public static readonly char[] SharpLetterOrder = { 'F', 'C', 'G', 'D', 'A', 'E', 'B' };

        /// <summary>
        /// Scales whose displayed key signature follows the relative major of the root.
        /// Modes and other scales use the root key name directly (existing behavior).
        /// </summary>
        public static bool ScaleUsesRelativeMajorKeySignature(string scale)
            => scale is "Natural Minor" or "Aeolian" or "Harmonic Minor"
                or "Melodic Minor" or "Jazz Melodic Minor";

        /// <summary>Maps a minor-family root to its relative major key name.</summary>
        public static string RelativeMajorOf(string minorKey) => minorKey switch
        {
            "A" => "C",
            "E" => "G",
            "B" => "D",
            "F#" => "A",
            "C#" => "E",
            "G#" => "B",
            "D#" => "F#",
            "D" => "F",
            "G" => "Bb",
            "C" => "Eb",
            "F" => "Ab",
            "Bb" => "Db",
            "Eb" => "Gb",
            _ => minorKey
        };

        public static string MajorKeyForSignature(string key, string scale)
            => ScaleUsesRelativeMajorKeySignature(scale) ? RelativeMajorOf(key) : key;

        public static bool IsFlatMajorKey(string majorKey)
            => majorKey is "F" or "Bb" or "Eb" or "Ab" or "Db" or "Gb" or "Cb";

        /// <summary>Flat key names used for note spelling when not scale-aware.</summary>
        public static bool IsFlatKeyName(string key)
            => IsFlatMajorKey(key);

        /// <summary>
        /// Signed circle-of-fifths count: positive = sharps, negative = flats, zero = C major / A minor.
        /// </summary>
        public static int GetSignedAccidentalCountForMajorKey(string majorKey) => majorKey switch
        {
            "C" => 0,
            "G" => 1,
            "D" => 2,
            "A" => 3,
            "E" => 4,
            "B" => 5,
            "F#" => 6,
            "C#" => 7,
            "F" => -1,
            "Bb" => -2,
            "Eb" => -3,
            "Ab" => -4,
            "Db" => -5,
            "Gb" => -6,
            "Cb" => -7,
            _ => 0
        };

        /// <summary>Signed accidental count for the displayed key signature.</summary>
        public static int GetSignedAccidentalCount(string key, string scale)
            => GetSignedAccidentalCountForMajorKey(MajorKeyForSignature(key, scale));

        /// <summary>Absolute number of sharps or flats shown in the key signature (0–7).</summary>
        public static int GetAccidentalCount(string key, string scale)
            => Math.Abs(GetSignedAccidentalCount(key, scale));

        public static bool KeySignatureUsesFlats(string key, string scale)
            => GetSignedAccidentalCount(key, scale) < 0;

        /// <summary>Returns "#", "b", or null when <paramref name="letter"/> is in the signature.</summary>
        public static string? GetSignatureAccidentalForLetter(char letter, string key, string scale)
        {
            int signed = GetSignedAccidentalCount(key, scale);
            if (signed == 0) return null;

            if (signed > 0)
                return SharpLetterOrder.Take(signed).Contains(letter) ? "#" : null;

            return FlatLetterOrder.Take(Math.Abs(signed)).Contains(letter) ? "b" : null;
        }

        /// <summary>Returns "#", "b", or null from a signed circle-of-fifths count.</summary>
        public static string? GetSignatureAccidentalForLetter(char letter, int signedAccidentalCount)
        {
            if (signedAccidentalCount == 0) return null;

            if (signedAccidentalCount > 0)
                return SharpLetterOrder.Take(signedAccidentalCount).Contains(letter) ? "#" : null;

            return FlatLetterOrder.Take(Math.Abs(signedAccidentalCount)).Contains(letter) ? "b" : null;
        }

        public static bool IsLetterInKeySignature(char letter, string key, string scale)
            => GetSignatureAccidentalForLetter(letter, key, scale) is not null;

#if DEBUG
        private static int _debugSelfTestsRun;

        /// <summary>DEBUG self-tests for key-signature theory rules.</summary>
        public static void RunDebugSelfTests()
        {
            if (Interlocked.CompareExchange(ref _debugSelfTestsRun, 1, 0) != 0)
                return;

            Utilities.DebugTestLog.Write("[KeySigTest] OK | KeySignatureRules self-test START");

            var keySigTests = new (string Key, string Scale, int Count, bool Flats, string Desc)[]
            {
                ("C",  "Major",         0, false, "C major – no accidentals"),
                ("G",  "Major",         1, false, "G major – 1 sharp (F#)"),
                ("D",  "Major",         2, false, "D major – 2 sharps (F#, C#)"),
                ("A",  "Major",         3, false, "A major – 3 sharps (F#, C#, G#)"),
                ("E",  "Major",         4, false, "E major – 4 sharps (F#, C#, G#, D#)"),
                ("B",  "Major",         5, false, "B major – 5 sharps"),
                ("F#", "Major",         6, false, "F# major – 6 sharps"),
                ("C#", "Major",         7, false, "C# major – 7 sharps"),
                ("F",  "Major",         1, true,  "F major – 1 flat (Bb)"),
                ("Bb", "Major",         2, true,  "Bb major – 2 flats (Bb, Eb)"),
                ("Eb", "Major",         3, true,  "Eb major – 3 flats"),
                ("Ab", "Major",         4, true,  "Ab major – 4 flats (Bb, Eb, Ab, Db)"),
                ("Db", "Major",         5, true,  "Db major – 5 flats"),
                ("Gb", "Major",         6, true,  "Gb major – 6 flats"),
                ("Cb", "Major",         7, true,  "Cb major – 7 flats"),
                ("A",  "Natural Minor", 0, false, "A natural minor = C major sig (0 acc)"),
                ("D",  "Natural Minor", 1, true,  "D natural minor = F major sig (1 flat)"),
                ("E",  "Natural Minor", 1, false, "E natural minor = G major sig (1 sharp)"),
                ("G",  "Natural Minor", 2, true,  "G natural minor = Bb major sig (2 flats)"),
                ("C",  "Natural Minor", 3, true,  "C natural minor = Eb major sig (3 flats)"),
                ("F#", "Natural Minor", 3, false, "F# natural minor = A major sig (F#, C#, G#)"),
                ("B",  "Harmonic Minor", 2, false, "B harmonic minor = D major sig (2 sharps)"),
                ("D",  "Dorian",         2, false, "D Dorian uses D major sig (modal, not relative)"),
            };

            foreach (var t in keySigTests)
            {
                int count = GetAccidentalCount(t.Key, t.Scale);
                bool flats = KeySignatureUsesFlats(t.Key, t.Scale);
                bool ok = count == t.Count && (count == 0 || flats == t.Flats);
                string result = ok ? "OK" : $"FAIL: expected count={t.Count} flats={t.Flats}, got count={count} flats={flats}";
                Utilities.DebugTestLog.Write($"[KeySigTest] {result} | {t.Desc}");
            }

            var letterTests = new (string Key, string Scale, char Letter, string? Expected, string Desc)[]
            {
                ("Bb", "Major", 'B', "b", "Bb major: B is flat in key sig"),
                ("Bb", "Major", 'E', "b", "Bb major: E is flat in key sig"),
                ("Bb", "Major", 'F', null, "Bb major: F is natural in key sig"),
                ("F#", "Natural Minor", 'F', "#", "F# natural minor: F# in key sig"),
                ("F#", "Natural Minor", 'C', "#", "F# natural minor: C# in key sig"),
                ("F#", "Natural Minor", 'G', "#", "F# natural minor: G# in key sig"),
                ("F#", "Natural Minor", 'D', null, "F# natural minor: D is natural in key sig"),
            };

            foreach (var t in letterTests)
            {
                string? sigAcc = GetSignatureAccidentalForLetter(t.Letter, t.Key, t.Scale);
                bool ok = sigAcc == t.Expected;
                string result = ok ? "OK" : $"FAIL: expected {t.Expected ?? "null"}, got {sigAcc ?? "null"}";
                Utilities.DebugTestLog.Write($"[KeySigTest] {result} | {t.Desc}");
            }

            Utilities.DebugTestLog.Write("[KeySigTest] OK | KeySignatureRules self-test END");
        }
#else
        public static void RunDebugSelfTests() { }
#endif
    }
}
