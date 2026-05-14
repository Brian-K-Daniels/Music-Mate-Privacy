namespace musicmate.Models
{
    /// <summary>
    /// A catalogue of simple built-in <see cref="PracticeTune"/> instances.
    /// <para>
    /// All tunes are constructed once and cached. They are pure data — no audio,
    /// no rendering, no session logic. Future steps will let the user pick a tune
    /// from this library to practice.
    /// </para>
    /// <para>
    /// MIDI reference: C4 (middle C) = 60. Each semitone up = +1.
    /// </para>
    /// </summary>
    public static class TuneLibrary
    {
        // ── cached instances ───────────────────────────────────────────────────

        private static PracticeTune? _cMajorScale;
        private static PracticeTune? _maryHadALittleLamb;
        private static PracticeTune? _odeToJoy;
        private static PracticeTune? _minuetG;

        // ── public accessors ──────────────────────────────────────────────────

        /// <summary>
        /// C Major scale, one octave up and back, all quarter notes, 4/4 time.
        /// Two measures up (C4–C5) and two measures down (B4–C4).
        /// </summary>
        public static PracticeTune CMajorScale => _cMajorScale ??= BuildCMajorScale();

        /// <summary>
        /// "Mary Had a Little Lamb" in C major, quarter notes, 4/4 time.
        /// </summary>
        public static PracticeTune MaryHadALittleLamb => _maryHadALittleLamb ??= BuildMaryHadALittleLamb();

        /// <summary>
        /// Beethoven "Ode to Joy" theme in C major, quarter and half notes, 4/4 time.
        /// </summary>
        public static PracticeTune OdeToJoy => _odeToJoy ??= BuildOdeToJoy();

        /// <summary>
        /// Minuet in G (Bach/Petzold), simplified to C major, mixed quarter and eighth notes, 3/4 time.
        /// </summary>
        public static PracticeTune MinuetG => _minuetG ??= BuildMinuetG();

        /// <summary>All built-in tunes in display order.</summary>
        public static IReadOnlyList<PracticeTune> All => new[]
        {
            MaryHadALittleLamb,
            OdeToJoy,
            MinuetG
        };

        // ── builders ──────────────────────────────────────────────────────────

        private static PracticeTune BuildCMajorScale()
        {
            //  C4  D4  E4  F4 | G4  A4  B4  C5 | B4  A4  G4  F4 | E4  D4  C4  (rest)
            var tune = new PracticeTune("C Major Scale", TimeSignature.FourFour);

            var m1 = tune.AppendMeasure();
            m1.AddNote(N(60, "C4", NoteDuration.Quarter));
            m1.AddNote(N(62, "D4", NoteDuration.Quarter));
            m1.AddNote(N(64, "E4", NoteDuration.Quarter));
            m1.AddNote(N(65, "F4", NoteDuration.Quarter));

            var m2 = tune.AppendMeasure();
            m2.AddNote(N(67, "G4", NoteDuration.Quarter));
            m2.AddNote(N(69, "A4", NoteDuration.Quarter));
            m2.AddNote(N(71, "B4", NoteDuration.Quarter));
            m2.AddNote(N(72, "C5", NoteDuration.Quarter));

            var m3 = tune.AppendMeasure();
            m3.AddNote(N(71, "B4", NoteDuration.Quarter));
            m3.AddNote(N(69, "A4", NoteDuration.Quarter));
            m3.AddNote(N(67, "G4", NoteDuration.Quarter));
            m3.AddNote(N(65, "F4", NoteDuration.Quarter));

            var m4 = tune.AppendMeasure();
            m4.AddNote(N(64, "E4", NoteDuration.Quarter));
            m4.AddNote(N(62, "D4", NoteDuration.Quarter));
            m4.AddNote(N(60, "C4", NoteDuration.Half));

            return tune;
        }

        private static PracticeTune BuildMaryHadALittleLamb()
        {
            //  E4  D4  C4  D4 | E4  E4  E4  — | D4  D4  D4  — | E4  G4  G4  —
            //  E4  D4  C4  D4 | E4  E4  E4  E4 | D4  D4  E4  D4 | C4  —   —   —
            var tune = new PracticeTune("Mary Had a Little Lamb", TimeSignature.FourFour);

            // bar 1:  E D C D
            var m1 = tune.AppendMeasure();
            m1.AddNote(N(64, "E4", NoteDuration.Quarter));
            m1.AddNote(N(62, "D4", NoteDuration.Quarter));
            m1.AddNote(N(60, "C4", NoteDuration.Quarter));
            m1.AddNote(N(62, "D4", NoteDuration.Quarter));

            // bar 2:  E E E (half rest represented as a held half note on E)
            var m2 = tune.AppendMeasure();
            m2.AddNote(N(64, "E4", NoteDuration.Quarter));
            m2.AddNote(N(64, "E4", NoteDuration.Quarter));
            m2.AddNote(N(64, "E4", NoteDuration.Half));

            // bar 3:  D D D (half rest)
            var m3 = tune.AppendMeasure();
            m3.AddNote(N(62, "D4", NoteDuration.Quarter));
            m3.AddNote(N(62, "D4", NoteDuration.Quarter));
            m3.AddNote(N(62, "D4", NoteDuration.Half));

            // bar 4:  E G G (half rest)
            var m4 = tune.AppendMeasure();
            m4.AddNote(N(64, "E4", NoteDuration.Quarter));
            m4.AddNote(N(67, "G4", NoteDuration.Quarter));
            m4.AddNote(N(67, "G4", NoteDuration.Half));

            // bar 5:  E D C D
            var m5 = tune.AppendMeasure();
            m5.AddNote(N(64, "E4", NoteDuration.Quarter));
            m5.AddNote(N(62, "D4", NoteDuration.Quarter));
            m5.AddNote(N(60, "C4", NoteDuration.Quarter));
            m5.AddNote(N(62, "D4", NoteDuration.Quarter));

            // bar 6:  E E E E
            var m6 = tune.AppendMeasure();
            m6.AddNote(N(64, "E4", NoteDuration.Quarter));
            m6.AddNote(N(64, "E4", NoteDuration.Quarter));
            m6.AddNote(N(64, "E4", NoteDuration.Quarter));
            m6.AddNote(N(64, "E4", NoteDuration.Quarter));

            // bar 7:  D D E D
            var m7 = tune.AppendMeasure();
            m7.AddNote(N(62, "D4", NoteDuration.Quarter));
            m7.AddNote(N(62, "D4", NoteDuration.Quarter));
            m7.AddNote(N(64, "E4", NoteDuration.Quarter));
            m7.AddNote(N(62, "D4", NoteDuration.Quarter));

            // bar 8:  C (whole)
            var m8 = tune.AppendMeasure();
            m8.AddNote(N(60, "C4", NoteDuration.Whole));

            return tune;
        }

        private static PracticeTune BuildOdeToJoy()
        {
            // Beethoven 9th, "Ode to Joy" theme, simplified to C major.
            // Original is in D major; transposed here to C major for easy reading.
            //
            // bar 1:  E4  E4  F4  G4
            // bar 2:  G4  F4  E4  D4
            // bar 3:  C4  C4  D4  E4
            // bar 4:  E4. D4  D4  —       (dotted quarter = 1.5 beats; simplified to E half, D quarter)
            // bar 5:  E4  E4  F4  G4
            // bar 6:  G4  F4  E4  D4
            // bar 7:  C4  C4  D4  E4
            // bar 8:  D4. C4  C4  —       (simplified to D half, C quarter, rest quarter → C half)

            var tune = new PracticeTune("Ode to Joy", TimeSignature.FourFour);

            var m1 = tune.AppendMeasure();
            m1.AddNote(N(64, "E4", NoteDuration.Quarter));
            m1.AddNote(N(64, "E4", NoteDuration.Quarter));
            m1.AddNote(N(65, "F4", NoteDuration.Quarter));
            m1.AddNote(N(67, "G4", NoteDuration.Quarter));

            var m2 = tune.AppendMeasure();
            m2.AddNote(N(67, "G4", NoteDuration.Quarter));
            m2.AddNote(N(65, "F4", NoteDuration.Quarter));
            m2.AddNote(N(64, "E4", NoteDuration.Quarter));
            m2.AddNote(N(62, "D4", NoteDuration.Quarter));

            var m3 = tune.AppendMeasure();
            m3.AddNote(N(60, "C4", NoteDuration.Quarter));
            m3.AddNote(N(60, "C4", NoteDuration.Quarter));
            m3.AddNote(N(62, "D4", NoteDuration.Quarter));
            m3.AddNote(N(64, "E4", NoteDuration.Quarter));

            var m4 = tune.AppendMeasure();
            m4.AddNote(N(64, "E4", NoteDuration.Half));    // dotted quarter simplified to half
            m4.AddNote(N(62, "D4", NoteDuration.Half));

            var m5 = tune.AppendMeasure();
            m5.AddNote(N(64, "E4", NoteDuration.Quarter));
            m5.AddNote(N(64, "E4", NoteDuration.Quarter));
            m5.AddNote(N(65, "F4", NoteDuration.Quarter));
            m5.AddNote(N(67, "G4", NoteDuration.Quarter));

            var m6 = tune.AppendMeasure();
            m6.AddNote(N(67, "G4", NoteDuration.Quarter));
            m6.AddNote(N(65, "F4", NoteDuration.Quarter));
            m6.AddNote(N(64, "E4", NoteDuration.Quarter));
            m6.AddNote(N(62, "D4", NoteDuration.Quarter));

            var m7 = tune.AppendMeasure();
            m7.AddNote(N(60, "C4", NoteDuration.Quarter));
            m7.AddNote(N(60, "C4", NoteDuration.Quarter));
            m7.AddNote(N(62, "D4", NoteDuration.Quarter));
            m7.AddNote(N(64, "E4", NoteDuration.Quarter));

            var m8 = tune.AppendMeasure();
            m8.AddNote(N(62, "D4", NoteDuration.Half));    // dotted quarter simplified to half
            m8.AddNote(N(60, "C4", NoteDuration.Half));

            return tune;
        }

        private static PracticeTune BuildMinuetG()
        {
            // Minuet in G (Bach/Petzold), simplified to C major for readability.
            // 3/4 time: each bar has 3 quarter-note beats.
            // Mix of quarter notes and pairs of eighth notes to show rhythmic variety.
            //
            // bar 1:  D4(q)  G4(q)  A4(q)
            // bar 2:  B4(q)  C5(q)  D5(q)
            // bar 3:  E5(e)F5(e)  E5(e)D5(e)  C5(q)
            // bar 4:  B4(q)  G4(q)  G4(q)
            // bar 5:  C5(q)  C5(q)  D5(q)
            // bar 6:  E5(q)  C5(q)  C5(q)
            // bar 7:  D5(e)C5(e)  B4(e)A4(e)  G4(q)
            // bar 8:  G4(h)  (quarter rest)

            var ts = TimeSignature.ThreeFour;
            var tune = new PracticeTune("Minuet in G (simplified)", ts);

            var m1 = tune.AppendMeasure();
            m1.AddNote(N(62, "D4", NoteDuration.Quarter));
            m1.AddNote(N(67, "G4", NoteDuration.Quarter));
            m1.AddNote(N(69, "A4", NoteDuration.Quarter));

            var m2 = tune.AppendMeasure();
            m2.AddNote(N(71, "B4", NoteDuration.Quarter));
            m2.AddNote(N(72, "C5", NoteDuration.Quarter));
            m2.AddNote(N(74, "D5", NoteDuration.Quarter));

            var m3 = tune.AppendMeasure();
            m3.AddNote(N(76, "E5", NoteDuration.Eighth));
            m3.AddNote(N(77, "F5", NoteDuration.Eighth));
            m3.AddNote(N(76, "E5", NoteDuration.Eighth));
            m3.AddNote(N(74, "D5", NoteDuration.Eighth));
            m3.AddNote(N(72, "C5", NoteDuration.Quarter));

            var m4 = tune.AppendMeasure();
            m4.AddNote(N(71, "B4", NoteDuration.Quarter));
            m4.AddNote(N(67, "G4", NoteDuration.Quarter));
            m4.AddNote(N(67, "G4", NoteDuration.Quarter));

            var m5 = tune.AppendMeasure();
            m5.AddNote(N(72, "C5", NoteDuration.Quarter));
            m5.AddNote(N(72, "C5", NoteDuration.Quarter));
            m5.AddNote(N(74, "D5", NoteDuration.Quarter));

            var m6 = tune.AppendMeasure();
            m6.AddNote(N(76, "E5", NoteDuration.Quarter));
            m6.AddNote(N(72, "C5", NoteDuration.Quarter));
            m6.AddNote(N(72, "C5", NoteDuration.Quarter));

            var m7 = tune.AppendMeasure();
            m7.AddNote(N(74, "D5", NoteDuration.Eighth));
            m7.AddNote(N(72, "C5", NoteDuration.Eighth));
            m7.AddNote(N(71, "B4", NoteDuration.Eighth));
            m7.AddNote(N(69, "A4", NoteDuration.Eighth));
            m7.AddNote(N(67, "G4", NoteDuration.Quarter));

            var m8 = tune.AppendMeasure();
            m8.AddNote(N(67, "G4", NoteDuration.Half));
            m8.AddNote(MusicNote.Rest(NoteDuration.Quarter));

            return tune;
        }

        // ── private helpers ───────────────────────────────────────────────────

        private static MusicNote N(int midi, string name, NoteDuration duration)
            => new MusicNote(midi, name, duration);
    }
}
