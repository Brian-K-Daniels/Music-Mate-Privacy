namespace musicmate.Services
{
    // ──────────────────────────────────────────────────────────────────────────────
    // PracticeDifficultySettings
    //
    // Holds the full set of session parameters that a difficulty level can control.
    // Only the "safe" subset is currently wired into NoteSessionService (see
    // DifficultyLevelMapper.ApplyToSession).  The remaining fields are present so
    // future work has a clear place to connect them.
    // ──────────────────────────────────────────────────────────────────────────────
    public record PracticeDifficultySettings
    {
        // ── Connected to NoteSessionService ────────────────────────────────────

        /// <summary>Metronome / playback tempo in beats per minute (30–400).</summary>
        public int PlaybackBpm { get; init; }

        /// <summary>
        /// Percentage of scale notes that may be replaced with chromatic accidentals (0–100).
        /// Only applied when the user has premium access.
        /// </summary>
        public int AccidentalPercent { get; init; }

        /// <summary>Lowest written note in the exercise range, e.g. "C4".</summary>
        public string LowestNote { get; init; } = "C4";

        /// <summary>Highest written note in the exercise range, e.g. "C5".</summary>
        public string HighestNote { get; init; } = "C5";

        /// <summary>
        /// Smallest note duration allowed in V2 rhythm generation.
        /// One of: "Quarter", "Eighth", "Sixteenth".
        /// </summary>
        public string V2SmallestNote { get; init; } = "Quarter";

        /// <summary>
        /// Rhythm variety mode for V2 generation.
        /// "Simple" = predominantly quarter notes; "Mixed" = full range up to V2SmallestNote.
        /// </summary>
        public string V2RhythmMode { get; init; } = "Simple";

        // ── Connected: mode and key ────────────────────────────────────────────

        /// <summary>
        /// When true, <see cref="DifficultyLevelMapper.ApplyToSession"/> sets
        /// <c>session.IsRandomMode = true</c> so LowestNote/HighestNote actually
        /// affect note generation.  BuildScaleSequence ignores the range; only
        /// BuildRandomSequenceAsync uses it.  Always true for child-home levels.
        /// </summary>
        public bool UseRandomMode { get; init; } = true;

        /// <summary>
        /// Key that IS applied by <see cref="DifficultyLevelMapper.ApplyToSession"/>.
        /// Child-home users never visit WhatToPlayPage so overriding is intentional.
        /// </summary>
        public string ForceKey { get; init; } = "C";

        // ── Future / informational fields ────────────────────────────────────────

        /// <summary>
        /// Informational copy of <see cref="ForceKey"/> for future UI display.
        /// </summary>
        public string SuggestedKey { get; init; } = "C";

        /// <summary>
        /// Suggested scale for this level (e.g. "Major", "Harmonic Minor").
        /// TODO: Apply to NoteSessionService.SelectedScale under the same guard as SuggestedKey.
        /// </summary>
        public string SuggestedScale { get; init; } = "Major";

        /// <summary>
        /// Target number of notes per exercise (random / scale modes).
        /// TODO: NoteSessionService does not yet have a NoteCount cap; add one and wire this in.
        /// </summary>
        public int SuggestedNoteCount { get; init; }

        /// <summary>
        /// Maximum melodic interval (in semitones) allowed between consecutive notes.
        /// TODO: Thread into BuildRandomSequenceAsync interval-weight table once a per-session
        ///       max-interval cap is added to NoteSessionService.
        /// </summary>
        public int MaxMelodicIntervalSemitones { get; init; }
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // DifficultyLevelMapper
    //
    // Converts a level in 1–100 into a PracticeDifficultySettings instance.
    //
    // Design principles:
    //   • Uses formulas and broad level bands — not a hard-coded 100-row table.
    //   • All numeric ranges use linear interpolation within each band via Lerp().
    //   • Conservative defaults: beginners get slow tempo, narrow range, no accidentals.
    //   • The instrument key is NOT touched here; the child home page already handles
    //     instrument selection independently.
    // ──────────────────────────────────────────────────────────────────────────────
    public static class DifficultyLevelMapper
    {
        // ── Level band boundaries ───────────────────────────────────────────────
        //   1–10   Beginner
        //  11–25   Easy
        //  26–40   Lower Intermediate
        //  41–60   Intermediate
        //  61–80   Advanced
        //  81–100  Expert

        // ── BPM range ───────────────────────────────────────────────────────────
        // Level 1 → 50 BPM (slow enough for beginners to find each note)
        // Level 100 → 160 BPM (demanding but achievable)
        private const int BpmAtLevel1   = 50;
        private const int BpmAtLevel100 = 160;

        // ── Note-range milestones (written pitch) ────────────────────────────────
        // Range widens progressively so beginners work on a single octave.
        // Each entry: (startLevel, loMidi, hiMidi, loName, hiName)
        // MIDI: C4=60, B3=59, A3=57, G3=55, E3=52, D5=74, E5=76, G5=79, A5=81, C6=84
        //
        // Level  1 → C4–C5  (1 octave, 12 semitones) — very narrow for beginners
        // Level 26 → A3–E5  (19 semitones)            — clearly wider for level 32
        // …progressing to E3–C6 at level 81+
        private static readonly (int StartLevel, int LoMidi, int HiMidi, string LoName, string HiName)[] RangeMilestones =
        [
            (  1, 60, 72, "C4", "C5"),   // 1 octave
            ( 11, 59, 74, "B3", "D5"),   // ~14 semitones
            ( 26, 57, 76, "A3", "E5"),   // ~19 semitones  ← level 32 lands here
            ( 41, 57, 79, "A3", "G5"),   // 22 semitones
            ( 61, 55, 81, "G3", "A5"),   // ~26 semitones
            ( 81, 52, 84, "E3", "C6"),   // ~32 semitones
        ];

        // ── Accidental % milestones ─────────────────────────────────────────────
        // Levels 1–25: 0 %; rises to 25 % at level 100.
        // Only effective when the user has premium access (NoteSessionService guards this).
        private const int AccidentalStartLevel = 26;
        private const int AccidentalMaxPercent = 25;

        // ── Key progression (suggested, not forced) ──────────────────────────────
        // Keys ordered by number of accidentals (easy → hard).
        private static readonly string[] KeysByDifficulty =
            ["C", "G", "F", "D", "Bb", "A", "Eb", "E", "Ab", "B", "Db", "F#"];

        // ── Public API ──────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a <see cref="PracticeDifficultySettings"/> instance for the given
        /// level (clamped to 1–100).  Pass the short instrument key (e.g. "Bb", "C")
        /// for future instrument-aware range adjustments.
        /// </summary>
        public static PracticeDifficultySettings GetSettingsForLevel(int level, string instrumentKey = "C")
        {
            level = Math.Clamp(level, 1, 100);

            var range      = CalcNoteRange(level);
            var concertKey = CalcSuggestedKey(level);

            // session.Key is the written key as read by the player.  For a Bb instrument
            // reading key-of-C on the page there are no accidentals — that IS the easiest
            // key for a beginner regardless of instrument transposition.  The sounding
            // concert pitch is handled automatically by GetInstrumentTransposeOffset()
            // inside Evaluate() / MapPitch(), so we must NOT shift the key here.
            var forceKey = concertKey;

            return new PracticeDifficultySettings
            {
                PlaybackBpm            = CalcBpm(level),
                AccidentalPercent      = CalcAccidentalPercent(level),
                LowestNote             = range.Lo,
                HighestNote            = range.Hi,
                V2SmallestNote         = CalcV2SmallestNote(level),
                V2RhythmMode           = level <= 40 ? "Simple" : "Mixed",
                UseRandomMode          = true,
                ForceKey               = forceKey,

                // Informational / future
                SuggestedKey           = forceKey,
                SuggestedScale         = "Major",   // TODO: expand scale progression later
                SuggestedNoteCount     = CalcNoteCount(level),
                MaxMelodicIntervalSemitones = CalcMaxInterval(level),
            };
        }

        /// <summary>
        /// Applies the safe subset of <paramref name="settings"/> to
        /// <paramref name="session"/>.  Key and scale are intentionally NOT applied
        /// here so they do not override the user's manual selection on WhatToPlayPage.
        /// </summary>
        public static void ApplyToSession(PracticeDifficultySettings settings, NoteSessionService session, bool forceClassicMode = true)
        {
            // ── Staff display mode ────────────────────────────────────────────────
            // Child-home sessions must always run in Classic mode so that the
            // SessionCompletedAsync handler reaches the stat-save and banner path.
            // V2 and V3 modes have their own early-return branches that skip
            // session completion entirely, making them unsuitable for child sessions.
            if (forceClassicMode)
                session.StaffDisplayMode = StaffDisplayMode.Classic;

            // ── Random mode ──────────────────────────────────────────────────────
            // CRITICAL: BuildScaleSequence ignores LowestNote/HighestNote entirely.
            // Only BuildRandomSequenceAsync uses the range.  Force random mode so
            // the range settings actually control what notes appear on screen.
            if (settings.UseRandomMode)
                session.IsRandomMode = true;

            // ── Key ──────────────────────────────────────────────────────────────
            // Child-home users never visit WhatToPlayPage, so we own the key here.
            // ForceKey progresses gently from "C" (level 1) through "G", "F", etc.
            session.Key = settings.ForceKey;

            // ── BPM ──────────────────────────────────────────────────────────────
            session.PlaybackBpm = settings.PlaybackBpm;

            // ── Accidental % ─────────────────────────────────────────────────────
            // Only honoured by NoteSessionService when the user has premium access.
            session.AccidentalPercent = settings.AccidentalPercent;

            // ── Note range ───────────────────────────────────────────────────────
            // Validate before setting: both names must exist in the white-key list
            // so WhatToPlayPage pickers won't show an invalid selection.
            var whiteKeys = session.WhiteKeyNoteNames;
            if (Array.IndexOf(whiteKeys, settings.LowestNote)  >= 0 &&
                Array.IndexOf(whiteKeys, settings.HighestNote) >= 0 &&
                NoteSessionService.NoteNameToMidi(settings.LowestNote) <
                NoteSessionService.NoteNameToMidi(settings.HighestNote))
            {
                session.LowestNote  = settings.LowestNote;
                session.HighestNote = settings.HighestNote;
            }

            // ── V2 rhythm ────────────────────────────────────────────────────────
            session.V2SmallestNote = settings.V2SmallestNote;
            session.V2RhythmMode   = settings.V2RhythmMode;

            // TODO: Apply SuggestedScale to session.SelectedScale once the child home
            //       page has a scale selector (keeping "Major" for now).

            // TODO: Apply SuggestedNoteCount once NoteSessionService has a NoteCount cap.

            // TODO: Apply MaxMelodicIntervalSemitones once BuildRandomSequenceAsync
            //       supports a per-session max-interval parameter.
        }

        // ── Private formula helpers ─────────────────────────────────────────────

        /// <summary>Linear BPM interpolation across the full 1–100 range.</summary>
        private static int CalcBpm(int level) =>
            (int)Math.Round(Lerp(BpmAtLevel1, BpmAtLevel100, (level - 1) / 99.0));

        /// <summary>
        /// Accidentals stay at 0 % until level 26, then rise linearly to
        /// <see cref="AccidentalMaxPercent"/> at level 100.
        /// </summary>
        private static int CalcAccidentalPercent(int level)
        {
            if (level < AccidentalStartLevel) return 0;
            return (int)Math.Round(
                Lerp(0, AccidentalMaxPercent, (level - AccidentalStartLevel) / (100.0 - AccidentalStartLevel)));
        }

        /// <summary>
        /// Picks the appropriate note-range milestone band and interpolates between
        /// MIDI numbers so the range widens smoothly across bands.
        /// </summary>
        private static (string Lo, string Hi) CalcNoteRange(int level)
        {
            var milestones = RangeMilestones;
            for (int i = milestones.Length - 1; i >= 0; i--)
            {
                if (level >= milestones[i].StartLevel)
                {
                    if (i < milestones.Length - 1)
                    {
                        var cur  = milestones[i];
                        var next = milestones[i + 1];
                        double t = (double)(level - cur.StartLevel) / (next.StartLevel - cur.StartLevel);
                        int midiLo = (int)Math.Round(Lerp(cur.LoMidi, next.LoMidi, t));
                        int midiHi = (int)Math.Round(Lerp(cur.HiMidi, next.HiMidi, t));
                        return (WhiteKeyNameForMidi(midiLo, preferLower: true),
                                WhiteKeyNameForMidi(midiHi, preferLower: false));
                    }
                    return (milestones[i].LoName, milestones[i].HiName);
                }
            }
            return (milestones[0].LoName, milestones[0].HiName);
        }

        private static string CalcV2SmallestNote(int level) => level switch
        {
            <= 25 => "Quarter",
            <= 60 => "Eighth",
            _     => "Sixteenth",
        };

        /// <summary>
        /// Suggested key: steps through keys by number of accidentals as level rises.
        /// Progression is intentionally slow so beginners stay on easy keys for a long time:
        ///   1–25  → C  (no accidentals)
        ///  26–50  → G  (1 sharp)
        ///  51–65  → F  (1 flat)
        ///  66–80  → D  (2 sharps)
        ///  81–90  → Bb (2 flats)
        ///  91–100 → A  (3 sharps)
        /// </summary>
        private static string CalcSuggestedKey(int level) => level switch
        {
            <= 25  => "C",
            <= 50  => "G",
            <= 65  => "F",
            <= 80  => "D",
            <= 90  => "Bb",
            _      => "A",
        };

        /// <summary>
        /// Suggested number of notes per exercise: starts at 6 (one of each scale degree
        /// in a narrow range) and grows to ~20 at level 100.
        /// TODO: wire into session once a NoteCount property is added.
        /// </summary>
        private static int CalcNoteCount(int level) =>
            (int)Math.Round(Lerp(6, 20, (level - 1) / 99.0));

        /// <summary>
        /// Maximum melodic interval in semitones: starts at a 4th (5 semitones) for
        /// beginners and opens to a 12th (19 semitones) for experts.
        /// TODO: wire into BuildRandomSequenceAsync once a per-session cap exists.
        /// </summary>
        private static int CalcMaxInterval(int level) =>
            (int)Math.Round(Lerp(5, 19, (level - 1) / 99.0));

        // ── Utility ─────────────────────────────────────────────────────────────

        private static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0, 1);

        // White-key note names ordered by MIDI number (C0 .. C8 natural notes only)
        private static readonly string[] AllWhiteKeys = Enumerable.Range(21, 88)
            .Select(m => NoteSessionService.MidiToNoteName(m, false))
            .Where(n => !n.Contains('#') && !n.Contains('b'))
            .ToArray();

        /// <summary>
        /// Finds the nearest white-key note name for a given MIDI number.
        /// If the MIDI lands on a black key, steps down (preferLower=true) or up to find white key.
        /// </summary>
        private static string WhiteKeyNameForMidi(int midi, bool preferLower)
        {
            // Try exact match
            var candidate = NoteSessionService.MidiToNoteName(midi, false);
            if (!candidate.Contains('#'))
                return EnsureInWhiteKeyList(candidate);

            // Step toward nearest white key
            int step = preferLower ? -1 : 1;
            for (int delta = 1; delta <= 2; delta++)
            {
                var n = NoteSessionService.MidiToNoteName(midi + step * delta, false);
                if (!n.Contains('#'))
                    return EnsureInWhiteKeyList(n);
            }
            // Fallback
            return preferLower ? "C4" : "C5";
        }

        private static string EnsureInWhiteKeyList(string name) =>
            AllWhiteKeys.Contains(name) ? name : "C4";
    }
}
