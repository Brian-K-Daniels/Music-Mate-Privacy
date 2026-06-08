namespace musicmate.Services
{
    /// <summary>
    /// Staged child-level curriculum (1–100).  Each 10-level band mainly introduces
    /// one new idea so players are not hit with new keys, scales, rhythms, and
    /// syncopation all at once.
    /// </summary>
    internal static class ChildLevelProgression
    {
        // Keys ordered easy → hard (accidentals in signature).
        private static readonly string[] KeysByDifficulty =
            ["C", "G", "F", "D", "Bb", "A", "Eb", "E", "Ab", "B", "Db", "F#"];

        private static readonly string[] AllScales =
        [
            "Major", "Natural Minor", "Harmonic Minor", "Melodic Minor",
            "Dorian", "Phrygian", "Lydian", "Mixolydian", "Locrian",
            "Major Pentatonic", "Minor Pentatonic", "Blues", "Chromatic"
        ];

        /// <summary>Human-readable band label for Child Home.</summary>
        public static string GetStageLabel(int level)
        {
            level = Math.Clamp(level, 1, 100);
            return ((level - 1) / 10) switch
            {
                0 => "Beginner",
                1 => "New keys",
                2 => "Rests",
                3 => "Minor & 16ths",
                4 => "Syncopation",
                5 => "Modes & blues",
                6 => "Full syncopation",
                _ => "Mastery"
            };
        }

        /// <summary>One-line description of what this level band is teaching.</summary>
        public static string GetMainFocus(int level)
        {
            level = Math.Clamp(level, 1, 100);
            return ((level - 1) / 10) switch
            {
                0 => "More notes, C major, quarter notes",
                1 => "More keys, longer note values",
                2 => "Rests and natural minor",
                3 => "Minor scales and sixteenth notes",
                4 => "Simple off-beat rhythms",
                5 => "Modes and blues sounds",
                6 => "Full syncopation",
                _ => "Wider range, speed, and chromatic notes"
            };
        }

        public static string KeyForLevel(int level)
        {
            level = Math.Clamp(level, 1, 100);
            int maxIndex = MaxKeyIndex(level);
            int index = KeyIndexWithinBand(level, maxIndex);
            return KeysByDifficulty[index];
        }

        public static string ScaleForLevel(int level)
        {
            level = Math.Clamp(level, 1, 100);
            return level switch
            {
                <= 20 => "Major",
                <= 25 => "Natural Minor",
                <= 30 => "Natural Minor",
                <= 35 => "Harmonic Minor",
                <= 40 => "Melodic Minor",
                <= 42 => "Major",
                <= 44 => "Natural Minor",
                <= 46 => "Harmonic Minor",
                <= 48 => "Melodic Minor",
                <= 50 => "Major Pentatonic",
                <= 52 => "Dorian",
                <= 54 => "Mixolydian",
                <= 56 => "Blues",
                <= 58 => "Minor Pentatonic",
                <= 60 => "Blues",
                <= 62 => "Phrygian",
                <= 64 => "Lydian",
                <= 66 => "Locrian",
                <= 70 => "Locrian",
                <= 85 => AllScales[(level - 71) % 8],          // rotate common types
                <= 95 => AllScales[(level * 3) % AllScales.Length],
                _     => AllScales[(level * 7 + 11) % AllScales.Length]
            };
        }

        public static int NoteCountForLevel(int level)
        {
            level = Math.Clamp(level, 1, 100);
            int band = (level - 1) / 10;
            int pos  = (level - 1) % 10;
            double t = pos / 9.0;
            return band switch
            {
                0 => (int)Math.Round(Lerp(4, 8, t)),    // 3–5 → 5–8 (avg pitched slots)
                1 => (int)Math.Round(Lerp(6, 12, t)),
                2 => (int)Math.Round(Lerp(8, 14, t)),
                3 => (int)Math.Round(Lerp(10, 16, t)),
                4 => (int)Math.Round(Lerp(12, 16, t)),
                5 => (int)Math.Round(Lerp(14, 18, t)),
                6 => (int)Math.Round(Lerp(16, 20, t)),
                _ => (int)Math.Round(Lerp(18, 28, t + (band - 7) * 0.15))
            };
        }

        public static (string Lo, string Hi) NoteRangeForLevel(int level)
        {
            level = Math.Clamp(level, 1, 100);
            // Keep range narrow while players learn notes, keys, and rests.
            if (level <= 20) return ("C4", "C5");
            if (level <= 30) return ("B3", "D5");
            if (level <= 40) return ("A3", "E5");
            if (level <= 55) return ("A3", "G5");
            if (level <= 70) return ("G3", "A5");
            return ("E3", "C6");
        }

        public static string SmallestNoteForLevel(int level)
        {
            level = Math.Clamp(level, 1, 100);
            if (level <= 15) return "Quarter";
            if (level <= 35) return "Eighth";
            return "Sixteenth";
        }

        /// <summary>0 = quarters only; higher values add longer/shorter note values.</summary>
        public static int RhythmVarietyPercentForLevel(int level)
        {
            level = Math.Clamp(level, 1, 100);
            if (level <= 10) return 0;
            if (level <= 15) return 25;
            if (level <= 20) return 35;
            if (level <= 30) return 40;
            if (level <= 40) return 50;
            if (level <= 50) return 55;
            if (level <= 60) return 60;
            if (level <= 70) return 70;
            if (level <= 85) return 80;
            return 95;
        }

        /// <summary>Rest probability per slot (0 until rests are introduced at 21).</summary>
        public static int RestChancePercentForLevel(int level)
        {
            level = Math.Clamp(level, 1, 100);
            if (level <= 20) return 0;
            if (level <= 30) return 14;
            if (level <= 50) return 18;
            if (level <= 70) return 22;
            return 28;
        }

        public static string SyncopationForLevel(int level)
        {
            level = Math.Clamp(level, 1, 100);
            if (level <= 40) return "None";
            if (level <= 60) return "Simple";
            if (level <= 65) return "Simple";
            return "Full";
        }

        public static int AccidentalPercentForLevel(int level)
        {
            level = Math.Clamp(level, 1, 100);
            if (level <= 30) return 0;
            if (level <= 40) return 5;
            if (level <= 50) return 10;
            if (level <= 60) return 12;
            if (level <= 70) return 15;
            if (level <= 80) return 20;
            if (level <= 90) return 25;
            return 30;
        }

        public static int MaxIntervalForLevel(int level)
        {
            level = Math.Clamp(level, 1, 100);
            if (level <= 10) return 2;
            if (level <= 20) return 3;
            if (level <= 40) return 4;
            if (level <= 60) return 5;
            if (level <= 80) return 6;
            return 8;
        }

        public static int MeasureBatchSizeForLevel(int level, int targetNoteCount)
        {
            // Roughly 3–4 pitched slots per 4/4 measure depending on rhythm.
            int measures = (int)Math.Ceiling(targetNoteCount / 3.5);
            return Math.Clamp(measures, 1, level <= 10 ? 2 : level <= 30 ? 4 : 8);
        }

        private static int MaxKeyIndex(int level)
        {
            if (level <= 10) return 0;
            if (level <= 20) return 4;   // C, G, F, D, Bb
            if (level <= 30) return 5;   // + A
            if (level <= 40) return 7;   // + Eb, E
            if (level <= 50) return 8;   // + Ab
            if (level <= 60) return 9;   // + B
            if (level <= 70) return 11;  // + Db, F#
            return KeysByDifficulty.Length - 1;
        }

        /// <summary>Within the current band, pick the newest key unlocked so far.</summary>
        private static int KeyIndexWithinBand(int level, int maxIndex)
        {
            int bandStart = ((level - 1) / 10) * 10 + 1;
            int prevMax   = MaxKeyIndex(bandStart - 1);
            int newKeys   = maxIndex - prevMax;
            if (newKeys <= 0) return maxIndex;

            int posInBand = level - bandStart;
            int added     = newKeys == 0 ? 0 : (int)Math.Round(newKeys * posInBand / 9.0);
            return Math.Clamp(prevMax + added, 0, maxIndex);
        }

        private static double Lerp(double a, double b, double t)
            => a + (b - a) * Math.Clamp(t, 0, 1);
    }
}
