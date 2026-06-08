namespace musicmate.Services
{
    // ──────────────────────────────────────────────────────────────────────────────
    // PracticeDifficultySettings
    //
    // Holds the full set of session parameters that a difficulty level can control.
    // ──────────────────────────────────────────────────────────────────────────────
    public record PracticeDifficultySettings
    {
        public int AccidentalPercent { get; init; }
        public string LowestNote { get; init; } = "C4";
        public string HighestNote { get; init; } = "C5";
        public string V2SmallestNote { get; init; } = "Quarter";
        public string V2RhythmMode { get; init; } = "Simple";
        public string V2Syncopation { get; init; } = "None";
        public bool UseRandomMode { get; init; } = true;
        public string ForceKey { get; init; } = "C";
        public string SuggestedKey { get; init; } = "C";
        public string SuggestedScale { get; init; } = "Major";
        public int SuggestedNoteCount { get; init; }
        public int MaxMelodicIntervalSemitones { get; init; }

        /// <summary>0–100.  Drives half/eighth/sixteenth variety in V2 generation.</summary>
        public int RhythmVarietyPercent { get; init; }

        /// <summary>0–100.  Per-slot rest probability (0 until level 21).</summary>
        public int RestChancePercent { get; init; }

        /// <summary>Measures generated per V2 batch for child sessions.</summary>
        public int MeasureBatchSize { get; init; } = 8;

        public string StageLabel { get; init; } = "Beginner";
        public string MainFocus { get; init; } = "";
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // DifficultyLevelMapper
    //
    // Converts level 1–100 into session parameters using staged bands defined in
    // <see cref="ChildLevelProgression"/> (one main new idea per 10-level group).
    // ──────────────────────────────────────────────────────────────────────────────
    public static class DifficultyLevelMapper
    {
        public static PracticeDifficultySettings GetSettingsForLevel(int level, string instrumentKey = "C")
        {
            level = Math.Clamp(level, 1, 100);

            var range     = ChildLevelProgression.NoteRangeForLevel(level);
            var key       = ChildLevelProgression.KeyForLevel(level);
            var scale     = ChildLevelProgression.ScaleForLevel(level);
            var noteCount = ChildLevelProgression.NoteCountForLevel(level);
            int variety   = ChildLevelProgression.RhythmVarietyPercentForLevel(level);

            return new PracticeDifficultySettings
            {
                AccidentalPercent           = ChildLevelProgression.AccidentalPercentForLevel(level),
                LowestNote                  = range.Lo,
                HighestNote                 = range.Hi,
                V2SmallestNote              = ChildLevelProgression.SmallestNoteForLevel(level),
                V2RhythmMode                = variety > 0 ? "Mixed" : "Simple",
                V2Syncopation               = ChildLevelProgression.SyncopationForLevel(level),
                UseRandomMode               = true,
                ForceKey                    = key,
                SuggestedKey                = key,
                SuggestedScale              = scale,
                SuggestedNoteCount          = noteCount,
                MaxMelodicIntervalSemitones = ChildLevelProgression.MaxIntervalForLevel(level),
                RhythmVarietyPercent        = variety,
                RestChancePercent           = ChildLevelProgression.RestChancePercentForLevel(level),
                MeasureBatchSize            = ChildLevelProgression.MeasureBatchSizeForLevel(level, noteCount),
                StageLabel                  = ChildLevelProgression.GetStageLabel(level),
                MainFocus                   = ChildLevelProgression.GetMainFocus(level),
            };
        }

        public static string GetStageLabel(int level)
            => ChildLevelProgression.GetStageLabel(level);

        public static string GetMainFocus(int level)
            => ChildLevelProgression.GetMainFocus(level);

        public static void ApplyToSession(PracticeDifficultySettings settings, NoteSessionService session, bool forceClassicMode = true)
        {
            if (forceClassicMode)
                session.StaffDisplayMode = StaffDisplayMode.Classic;

            if (settings.UseRandomMode)
                session.IsRandomMode = true;

            session.Key = settings.ForceKey;
            session.SelectedScale = settings.SuggestedScale;
            session.AccidentalPercent = settings.AccidentalPercent;

            var whiteKeys = session.WhiteKeyNoteNames;
            if (Array.IndexOf(whiteKeys, settings.LowestNote) >= 0 &&
                Array.IndexOf(whiteKeys, settings.HighestNote) >= 0 &&
                NoteSessionService.NoteNameToMidi(settings.LowestNote) <
                NoteSessionService.NoteNameToMidi(settings.HighestNote))
            {
                session.LowestNote  = settings.LowestNote;
                session.HighestNote = settings.HighestNote;
            }

            session.V2SmallestNote = settings.V2SmallestNote;
            session.V2RhythmMode   = settings.V2RhythmMode;
            session.V2Syncopation  = settings.V2Syncopation;
            session.MaxMelodicIntervalSemitones = settings.MaxMelodicIntervalSemitones;

            session.ChildMeasureBatchSize   = settings.MeasureBatchSize;
            session.V2RhythmVarietyPercent  = settings.RhythmVarietyPercent;
            session.V2RestChancePercent     = settings.RestChancePercent;
        }
    }
}
