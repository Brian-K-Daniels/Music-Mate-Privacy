using System.Diagnostics;
using musicmate.Models;

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
        public string V3SmallestNote { get; init; } = "Quarter";
        public string V3RhythmMode { get; init; } = "Simple";
        public string V3Syncopation { get; init; } = "None";
        public bool UseRandomMode { get; init; } = true;
        public string ForceKey { get; init; } = "C";
        public string SuggestedKey { get; init; } = "C";
        public string SuggestedScale { get; init; } = "Major";
        public int SuggestedNoteCount { get; init; }
        public int MaxMelodicIntervalSemitones { get; init; }

        /// <summary>0–100.  Drives half/eighth/sixteenth variety in V3 generation.</summary>
        public int RhythmVarietyPercent { get; init; }

        /// <summary>0–100.  Per-slot rest probability (0 until level 21).</summary>
        public int RestChancePercent { get; init; }

        /// <summary>Measures generated per batch for child sessions.</summary>
        public int MeasureBatchSize { get; init; } = 8;

        public string StageLabel { get; init; } = "Beginner";
        public string MainFocus { get; init; } = "";
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // DifficultyLevelMapper
    //
    // Converts level 1–100 into session parameters using staged bands defined in
    // <see cref="ChildLevelProgression"/>. Scale and key are chosen per session
    // from weighted pools.
    // ──────────────────────────────────────────────────────────────────────────────
    public static class DifficultyLevelMapper
    {
        /// <summary>Inspectable profile (pools + range) for a level without random picks.</summary>
        public static ChildLevelDifficultyProfile GetProfile(int level)
            => ChildLevelProgression.GetProfile(level);

        /// <summary>Inspectable arpeggio catalog availability for a level. Not used by Random generation yet.</summary>
        public static ArpeggioLevelAvailability GetArpeggioAvailability(int level)
            => ArpeggioCatalog.GetAvailabilityForLevel(level);

        /// <summary>
        /// Resolve a full session settings bundle, randomly picking scale and key from
        /// the level's weighted pools.
        /// </summary>
        public static PracticeDifficultySettings ResolveSessionSettings(int level, Random? rng = null)
        {
            level = Math.Clamp(level, 1, 100);
            var profile = ChildLevelProgression.GetProfile(level);
            var (scale, key) = ChildLevelProgression.PickScaleAndKey(profile, rng);
            return BuildSettings(level, profile, scale, key);
        }

        /// <summary>Backward-compatible alias for <see cref="ResolveSessionSettings"/>.</summary>
        public static PracticeDifficultySettings GetSettingsForLevel(int level, string instrumentKey = "C", Random? rng = null)
            => ResolveSessionSettings(level, rng);

        public static string GetStageLabel(int level)
            => ChildLevelProgression.GetStageLabel(level);

        public static string GetMainFocus(int level)
            => ChildLevelProgression.GetMainFocus(level);

        /// <summary>
        /// Pick scale/key for this session and apply child-level settings to the session.
        /// When <paramref name="preserveUserPracticeSettings"/> is true and the user has
        /// customized settings, key/scale/rhythm/accidental values are kept; range and batch
        /// sizing still follow the level.
        /// </summary>
        public static PracticeDifficultySettings PickAndApplyToSession(
            int level,
            NoteSessionService session,
            bool forceClassicMode = false,
            Random? rng = null,
            bool preserveUserPracticeSettings = false)
        {
            var settings = ResolveSessionSettings(level, rng);
            bool preserve = preserveUserPracticeSettings && session.ChildPracticeSettingsCustomized;
            ApplyToSession(settings, session, forceClassicMode,
                applyKeyAndScale: !preserve,
                applyPracticeSettings: !preserve);
#if DEBUG
            Debug.WriteLine($"[ChildLevel] Picked session settings: L{level} {settings.SuggestedKey} {settings.SuggestedScale} preserve={preserve}");
#endif
            return settings;
        }

        /// <summary>Diagnostic report of weighted pools for sample levels.</summary>
        public static string BuildDiagnosticReport(IEnumerable<int>? levels = null)
            => ChildLevelProgression.BuildDiagnosticReport(levels);

        public static void ApplyToSession(
            PracticeDifficultySettings settings,
            NoteSessionService session,
            bool forceClassicMode = true,
            bool applyKeyAndScale = true,
            bool applyPracticeSettings = true)
        {
            // V3-only: child levels and MainPage always use the two-staff display.
            session.StaffDisplayMode = StaffDisplayMode.V3;

            if (applyKeyAndScale)
            {
                if (settings.UseRandomMode)
                    session.IsRandomMode = true;

                session.Key = settings.ForceKey;
                session.SelectedScale = settings.SuggestedScale;
                session.Tune = "Selected Scale";
            }

            session.ApplyAutomaticInstrumentRange();

            session.MaxMelodicIntervalSemitones = settings.MaxMelodicIntervalSemitones;
            session.ChildMeasureBatchSize       = settings.MeasureBatchSize;

            if (applyPracticeSettings)
            {
                session.AccidentalPercent        = settings.AccidentalPercent;
                session.V3SmallestNote           = settings.V3SmallestNote;
                session.V3RhythmMode             = settings.V3RhythmMode;
                session.V3Syncopation            = settings.V3Syncopation;
                session.V3RhythmVarietyPercent   = settings.RhythmVarietyPercent;
                session.V3RestChancePercent      = settings.RestChancePercent;
            }

            if (applyKeyAndScale && applyPracticeSettings)
                session.ClearChildPracticeSettingsCustomization();
        }

        private static PracticeDifficultySettings BuildSettings(
            int level, ChildLevelDifficultyProfile profile, string scale, string key)
        {
            var noteCount = ChildLevelProgression.NoteCountForLevel(level);
            int variety   = ChildLevelProgression.RhythmVarietyPercentForLevel(level);

            return new PracticeDifficultySettings
            {
                AccidentalPercent           = profile.AccidentalPercent,
                LowestNote                  = profile.LowestNote,
                HighestNote                 = profile.HighestNote,
                V3SmallestNote              = ChildLevelProgression.SmallestNoteForLevel(level),
                V3RhythmMode                = variety > 0 ? "Mixed" : "Simple",
                V3Syncopation               = ChildLevelProgression.SyncopationForLevel(level),
                UseRandomMode               = true,
                ForceKey                    = key,
                SuggestedKey                = key,
                SuggestedScale              = scale,
                SuggestedNoteCount          = noteCount,
                MaxMelodicIntervalSemitones = ChildLevelProgression.MaxIntervalForLevel(level),
                RhythmVarietyPercent        = variety,
                RestChancePercent           = ChildLevelProgression.RestChancePercentForLevel(level),
                MeasureBatchSize            = ChildLevelProgression.MeasureBatchSizeForLevel(level, noteCount),
                StageLabel                  = profile.StageLabel,
                MainFocus                   = profile.MainFocus,
            };
        }
    }
}
