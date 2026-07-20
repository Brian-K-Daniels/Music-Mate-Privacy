using musicmate.Diagnostics;
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
        public string SmallestRhythmNote { get; init; } = "Quarter";
        public string RhythmMode { get; init; } = "Simple";
        public string SyncopationSetting { get; init; } = "None";
        public bool UseRandomMode { get; init; } = true;
        public string ForceKey { get; init; } = "C";
        public string SuggestedKey { get; init; } = "C";
        public string SuggestedScale { get; init; } = "Major";
        public int SuggestedNoteCount { get; init; }
        public int MaxMelodicIntervalSemitones { get; init; }

        /// <summary>0–100.  Drives half/eighth/sixteenth variety in rhythm generation.</summary>
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
        /// Applies all level-dependent session settings for a child-level change that should
        /// keep the current key/scale when still allowed (e.g. Music page Level +/-).
        /// Sets <see cref="NoteSessionService.ChildLevel"/> then runs the full apply path.
        /// </summary>
        public static PracticeDifficultySettings ApplyLevelSettings(
            int level,
            NoteSessionService session,
            bool preserveUserPracticeSettings = false)
        {
            level = Math.Clamp(level, 1, 100);
            session.ChildLevel = level;
            return ApplyLevelChangeToSession(level, session, preserveUserPracticeSettings);
        }

        /// <summary>
        /// Applies a programmatic child-level change: validates key/scale against the new
        /// level's pools (keeping current values when still allowed), then applies level settings.
        /// Does not randomly re-pick key or scale. Prefer <see cref="ApplyLevelSettings"/> when
        /// also assigning <see cref="NoteSessionService.ChildLevel"/>.
        /// </summary>
        public static PracticeDifficultySettings ApplyLevelChangeToSession(
            int level,
            NoteSessionService session,
            bool preserveUserPracticeSettings = false)
        {
            level = Math.Clamp(level, 1, 100);
            var profile = ChildLevelProgression.GetProfile(level);
            // Finalize scale first, then validate the key against that scale's permitted list.
            session.ApplyScaleSelectionOnLevelChange(level);
            string scale = session.SelectedScale;
            string key = ChildLevelProgression.ValidateKeyForLevel(level, scale, session.Key);
            bool preserve = preserveUserPracticeSettings && session.ChildPracticeSettingsCustomized;
            var settings = BuildSettings(level, profile, scale, key);
            ApplyToSession(settings, session, applyKeyAndScale: true, applyPracticeSettings: !preserve);
#if DEBUG
            DebugLog.WriteLine($"[ChildLevel] Level change L{level} → {key} {scale} preserve={preserve}");
#endif
            return settings;
        }

        /// <summary>
        /// Pick scale/key for this session and apply child-level settings to the session.
        /// When <paramref name="preserveUserPracticeSettings"/> is true and the user has
        /// customized settings, key/scale/rhythm/accidental values are kept; range and batch
        /// sizing still follow the level.
        /// </summary>
        public static PracticeDifficultySettings PickAndApplyToSession(
            int level,
            NoteSessionService session,
            Random? rng = null,
            bool preserveUserPracticeSettings = false)
        {
            level = Math.Clamp(level, 1, 100);
            var profile = ChildLevelProgression.GetProfile(level);
            bool preserve = preserveUserPracticeSettings && session.ChildPracticeSettingsCustomized;

            if (!preserve)
            {
                session.ScaleSelectionMode = ScaleSelectionMode.ByLevel;
                session.IsRandomMode = true;
                session.Tune = "Selected Scale";
            }

            // Finalize scale before picking a key so balancing never runs on a temporary scale.
            session.ApplyScaleSelectionOnLevelChange(level, rng);

            if (!preserve)
            {
                session.Key = ChildLevelProgression.PickBalancedKeyForSignature(
                    session.SelectedScale, level, rng);
            }
            else
            {
                session.Key = ChildLevelProgression.ValidateKeyForLevel(
                    level, session.SelectedScale, session.Key);
            }

            var settings = BuildSettings(level, profile, session.SelectedScale, session.Key);
            ApplyToSession(settings, session,
                applyKeyAndScale: false,
                applyPracticeSettings: !preserve);
#if DEBUG
            DebugLog.WriteLine($"[ChildLevel] Picked session settings: L{level} {session.Key} {session.SelectedScale} mode={session.ScaleSelectionMode} preserve={preserve}");
#endif
            return settings;
        }

        /// <summary>Diagnostic report of weighted pools for sample levels.</summary>
        public static string BuildDiagnosticReport(IEnumerable<int>? levels = null)
            => ChildLevelProgression.BuildDiagnosticReport(levels);

        /// <summary>
        /// Applies interval cap, measure batch size, and instrument range for a child level
        /// without re-picking key, scale, or rhythm settings.
        /// </summary>
        public static void ApplyLevelDerivedSettings(int level, NoteSessionService session)
        {
            level = Math.Clamp(level, 1, 100);
            session.MaxMelodicIntervalSemitones = ChildLevelProgression.MaxIntervalForLevel(level);
            session.ChildMeasureBatchSize = ChildLevelProgression.MeasureBatchSizeForLevel(
                level, ChildLevelProgression.NoteCountForLevel(level));
            session.ApplyAutomaticInstrumentRange(level);
        }

        public static void ApplyToSession(
            PracticeDifficultySettings settings,
            NoteSessionService session,
            bool applyKeyAndScale = true,
            bool applyPracticeSettings = true)
        {
            if (applyKeyAndScale)
            {
                if (settings.UseRandomMode)
                    session.IsRandomMode = true;

                session.Key = settings.ForceKey;
                if (session.ScaleSelectionMode == ScaleSelectionMode.Named)
                    session.SelectedScale = settings.SuggestedScale;
                session.Tune = "Selected Scale";
            }

            session.MaxMelodicIntervalSemitones = settings.MaxMelodicIntervalSemitones;
            session.ChildMeasureBatchSize = settings.MeasureBatchSize;
            // Reset to the level's automatic range unless the user customized note limits.
            session.ApplyAutomaticInstrumentRange(fullReset: !session.NoteRangeCustomized);

            if (applyPracticeSettings)
            {
                session.AccidentalPercent = settings.AccidentalPercent;
                session.SmallestRhythmNote = settings.SmallestRhythmNote;
                session.RhythmMode = settings.RhythmMode;
                session.SyncopationSetting = settings.SyncopationSetting;
                session.RhythmVarietyPercent = settings.RhythmVarietyPercent;
                session.PracticeRestChancePercent = settings.RestChancePercent;
            }

            if (applyKeyAndScale && applyPracticeSettings)
                session.ClearChildPracticeSettingsCustomization();
        }

        private static PracticeDifficultySettings BuildSettings(
            int level, ChildLevelDifficultyProfile profile, string scale, string key)
        {
            var noteCount = ChildLevelProgression.NoteCountForLevel(level);
            int variety = ChildLevelProgression.RhythmVarietyPercentForLevel(level);

            return new PracticeDifficultySettings
            {
                AccidentalPercent = profile.AccidentalPercent,
                LowestNote = profile.LowestNote,
                HighestNote = profile.HighestNote,
                SmallestRhythmNote = ChildLevelProgression.SmallestNoteForLevel(level),
                RhythmMode = variety > 0 ? "Mixed" : "Simple",
                SyncopationSetting = ChildLevelProgression.SyncopationForLevel(level),
                UseRandomMode = true,
                ForceKey = key,
                SuggestedKey = key,
                SuggestedScale = scale,
                SuggestedNoteCount = noteCount,
                MaxMelodicIntervalSemitones = ChildLevelProgression.MaxIntervalForLevel(level),
                RhythmVarietyPercent = variety,
                RestChancePercent = ChildLevelProgression.RestChancePercentForLevel(level),
                MeasureBatchSize = ChildLevelProgression.MeasureBatchSizeForLevel(level, noteCount),
                StageLabel = profile.StageLabel,
                MainFocus = profile.MainFocus,
            };
        }
    }
}
