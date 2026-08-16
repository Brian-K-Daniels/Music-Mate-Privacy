#nullable enable
using System.ComponentModel;
using System.Text.Json;
using Microsoft.Maui.Graphics;
using musicmate.Diagnostics;
using musicmate.ViewModels;

namespace musicmate.Services
{
    /// <summary>
    /// Factory reset and user-defined custom default settings.
    /// Exposes <see cref="AreFactoryDefaultsApplied"/> by comparing live Settings / Advanced
    /// values to the defined factory defaults (not merely an "active defaults" flag).
    /// </summary>
    public sealed class SettingsResetService : INotifyPropertyChanged
    {
        private const string CustomDefaultsJsonKey = "musicmate.CustomDefaults.Json";
        private const string CustomDefaultsExistsKey = "musicmate.CustomDefaults.Exists";
        private const string ActiveDefaultsKey = "musicmate.ActiveDefaults";
        private const string CollectNoteStatsKey = "CollectNoteStats";
        private const string CollectSessionStatsKey = "CollectSessionStats";
        private const string MaxSessionDbSizeMbKey = "MaxSessionDbSizeMb";
        private const string MaxAttemptsPerNoteKey = "MaxAttemptsPerNote";
        private const string RepeatDelaySecondsKey = "RepeatDelaySeconds";
        private const string OmitMsAvgKey = "musicmate.OmitMsAvgThreshold";

        private readonly NoteSessionService _session;
        private readonly ThemeService _theme;
        private bool _areFactoryDefaultsApplied;
        private bool _suppressFactoryEvaluation;
        private bool _isEvaluating;

        public SettingsResetService(NoteSessionService session, ThemeService theme)
        {
            _session = session;
            _theme = theme;

            _session.PropertyChanged += (_, _) => EvaluateAreFactoryDefaultsApplied();
            _theme.PropertyChanged += (_, _) => EvaluateAreFactoryDefaultsApplied();
            _theme.ThemeColorsChanged += (_, _) => EvaluateAreFactoryDefaultsApplied();

            // Initial evaluation after construction (settings already loaded into session/theme).
            EvaluateAreFactoryDefaultsApplied();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool HasCustomDefaults => SessionPreferences.Get(CustomDefaultsExistsKey, false);

        public ActiveDefaultsSet ActiveDefaults => SessionPreferences.Get(ActiveDefaultsKey, ActiveDefaultsSet.Factory.ToString()) switch
        {
            nameof(ActiveDefaultsSet.Custom) => ActiveDefaultsSet.Custom,
            _ => ActiveDefaultsSet.Factory,
        };

        /// <summary>
        /// True when every Settings / Advanced setting matches its factory default value.
        /// </summary>
        public bool AreFactoryDefaultsApplied
        {
            get => _areFactoryDefaultsApplied;
            private set
            {
                if (_areFactoryDefaultsApplied == value)
                    return;
                _areFactoryDefaultsApplied = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AreFactoryDefaultsApplied)));
            }
        }

        public void SetActiveDefaults(ActiveDefaultsSet set)
            => SessionPreferences.Set(ActiveDefaultsKey, set.ToString());

        /// <summary>
        /// Call after preference-only Settings / Advanced changes that do not raise
        /// <see cref="NoteSessionService.PropertyChanged"/>.
        /// </summary>
        public void NotifySettingsChanged()
            => EvaluateAreFactoryDefaultsApplied();

        /// <summary>Resets all Settings / Advanced values to factory defaults.</summary>
        public void ResetToFactoryDefaults()
        {
            DebugLog.WriteLine("[ResetOptionsTest] ResetToFactoryDefaults called.");
            _suppressFactoryEvaluation = true;
            try
            {
                ApplyFactoryDefaultsToLiveSettings();
                SetActiveDefaults(ActiveDefaultsSet.Factory);
            }
            finally
            {
                _suppressFactoryEvaluation = false;
            }

            EvaluateAreFactoryDefaultsApplied();
            DebugLog.WriteLine(
                $"[ResetOptionsTest] After factory reset: Tune={_session.Tune} Scale={_session.SelectedScale} " +
                $"Key={_session.Key} AccPct={_session.AccidentalPercent} AreFactory={AreFactoryDefaultsApplied}");
        }

        /// <summary>
        /// Restores a brand-new-install experience: clears progress databases and caches,
        /// resets level to 1, and restores all user settings. Does <b>not</b> revoke Premium.
        /// </summary>
        public async Task PerformFullFactoryResetAsync(
            NoteDatabase? noteDatabase = null,
            SessionDatabase? sessionDatabase = null,
            SessionResultDatabase? sessionResultDatabase = null,
            NoteAttemptDatabase? noteAttemptDatabase = null,
            StatisticsCacheService? statisticsCache = null)
        {
            DebugLog.WriteLine("[FactoryReset] Full factory reset starting (Premium preserved).");

            await ClearProgressDatabasesAsync(
                noteDatabase, sessionDatabase, sessionResultDatabase, noteAttemptDatabase);

            statisticsCache?.InvalidateNoteStats();
            statisticsCache?.InvalidateSessionStats();
            ServiceHelper.GetService<NoteMasteryService>()?.Invalidate();

            // Level first so factory note-range resolution uses ChildLevel 1.
            Preferences.Default.Set("ChildPractice.Level", 1);
            _session.ChildLevel = 1;
            SessionPreferences.Set(IntervalSightTrainingLogic.LevelPreferenceKey,
                IntervalSightTrainingLogic.DefaultLevel);
            _session.ClearChildPracticeSettingsCustomization();
            _session.ClearTemporaryNoteEmphasis("factory-reset");

            // Fresh install has no level-up counting window yet.
            Preferences.Default.Remove("LevelUp.CountSinceUtc");

            // Ephemeral UI / What-to-Play selections a new install would not have.
            Preferences.Default.Remove("SelectedTune");
            Preferences.Default.Remove("musicmate.SelectedStatsDb");
            ClearSavedCustomDefaults();

            // User-saved practice tunes are not part of a fresh install.
            ServiceHelper.GetService<SavedTuneStore>()?.ClearAll();

            ResetToFactoryDefaults();

            DebugLog.WriteLine(
                $"[FactoryReset] Complete. Level={_session.ChildLevel} Premium={StatusService.Instance.IsPremiumUser}");
        }

        /// <summary>Removes saved custom-default snapshots (not present on a fresh install).</summary>
        public void ClearSavedCustomDefaults()
        {
            SessionPreferences.Remove(CustomDefaultsJsonKey);
            SessionPreferences.Remove(CustomDefaultsExistsKey);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasCustomDefaults)));
        }

        private static async Task ClearProgressDatabasesAsync(
            NoteDatabase? noteDatabase,
            SessionDatabase? sessionDatabase,
            SessionResultDatabase? sessionResultDatabase,
            NoteAttemptDatabase? noteAttemptDatabase)
        {
            if (noteDatabase != null)
            {
                await noteDatabase.InitializeAsync();
                await noteDatabase.ClearAllAsync();
            }

            if (sessionDatabase != null)
            {
                await sessionDatabase.InitializeAsync();
                await sessionDatabase.ClearAllAsync();
            }

            if (sessionResultDatabase != null)
            {
                await sessionResultDatabase.InitializeAsync();
                await sessionResultDatabase.ClearAllAsync();
            }

            if (noteAttemptDatabase != null)
            {
                await noteAttemptDatabase.InitializeAsync();
                await noteAttemptDatabase.ClearAllAsync();
            }
        }

        /// <summary>Saves the currently active settings as the user's custom defaults.</summary>
        public void SaveCustomDefaultsFromCurrent()
        {
            DebugLog.WriteLine($"[ResetOptionsTest] SaveCustomDefaults: Tune={_session.Tune} Scale={_session.SelectedScale} Key={_session.Key}");
            var snapshot = CaptureCurrentSnapshot();
            var json = JsonSerializer.Serialize(snapshot);
            SessionPreferences.Set(CustomDefaultsJsonKey, json);
            SessionPreferences.Set(CustomDefaultsExistsKey, true);
        }

        /// <summary>Restores settings from the saved custom defaults.</summary>
        public void RestoreCustomDefaults()
        {
            DebugLog.WriteLine("[ResetOptionsTest] RestoreCustomDefaults called.");
            if (!HasCustomDefaults)
                return;

            var json = SessionPreferences.Get(CustomDefaultsJsonKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return;

            var snapshot = JsonSerializer.Deserialize<AppSettingsSnapshot>(json);
            if (snapshot == null)
                return;

            _suppressFactoryEvaluation = true;
            try
            {
                ApplySnapshot(snapshot);
                SetActiveDefaults(ActiveDefaultsSet.Custom);
            }
            finally
            {
                _suppressFactoryEvaluation = false;
            }

            EvaluateAreFactoryDefaultsApplied();
        }

        public void EvaluateAreFactoryDefaultsApplied()
        {
            if (_suppressFactoryEvaluation || _isEvaluating)
                return;

            _isEvaluating = true;
            try
            {
                AreFactoryDefaultsApplied = MatchesFactoryDefaults(CaptureCurrentSnapshot());
            }
            finally
            {
                _isEvaluating = false;
            }
        }

        /// <summary>Test / diagnostics: build the factory snapshot for the current instrument level.</summary>
        internal AppSettingsSnapshot CreateFactoryDefaultsSnapshot()
            => BuildFactoryDefaultsSnapshot();

        /// <summary>Test / diagnostics: capture live settings.</summary>
        internal AppSettingsSnapshot CaptureCurrentSnapshotForTests()
            => CaptureCurrentSnapshot();

        private void ApplyFactoryDefaultsToLiveSettings()
        {
            _session.Tempo = SettingsPageViewModel.DefaultTempo;
            _session.AccidentalPercent = SettingsPageViewModel.DefaultAccidentalPct;
            _session.CorrectThreshold = SettingsPageViewModel.DefaultCorrectThreshold;
            _session.MinCorrectCount = SettingsPageViewModel.DefaultMinCorrectCount;
            SessionPreferences.Set(OmitMsAvgKey, SettingsPageViewModel.DefaultOmitMsAvg);
            _session.OmitMsAvgThreshold = SettingsPageViewModel.DefaultOmitMsAvg;
            _session.AutoStart = SettingsPageViewModel.DefaultAutoStart;
            _session.AutoRepeat = SettingsPageViewModel.DefaultAutoRepeat;
            _session.RepeatSameTune = SettingsPageViewModel.DefaultRepeatSameTune;
            _session.MasteredMethod = MasteryPreferenceDefaults.MasteredMethod;
            _session.StreakCrit = MasteryPreferenceDefaults.StreakCrit;
            _session.UseNoteMasteryForGeneration = MasteryPreferenceDefaults.UseNoteMasteryForGeneration;
            _session.ShowConductorCues = false;
            _session.ShowSignaturesOnBothStaffs = true;
            _session.NoteNameDisplay = "Current only";
            _session.MeterTimeSignature = "4/4";
            _session.SmallestRhythmNote = "Quarter";
            _session.RhythmMode = "Simple";
            _session.SyncopationSetting = "None";

            SessionPreferences.Set(CollectNoteStatsKey, SettingsPageViewModel.DefaultCollectNote);
            SessionPreferences.Set(CollectSessionStatsKey, SettingsPageViewModel.DefaultCollectSession);
            SessionPreferences.Set(MaxSessionDbSizeMbKey, SettingsPageViewModel.DefaultMaxSessionDbMb);
            SessionPreferences.Set(MaxAttemptsPerNoteKey, 100);
            SessionPreferences.Set(AboutPageViewModel.FontSizePreferenceKey, 14.0);
            SessionPreferences.Set(RepeatDelaySecondsKey, 2.0);
            SessionPreferences.Set(
                IntervalEarTrainingLogic.NoteDurationPreferenceKey,
                IntervalEarTrainingLogic.DefaultNoteDurationMs);
            SessionPreferences.Set(
                IntervalEarTrainingLogic.DirectionPreferenceKey,
                IntervalEarTrainingLogic.DefaultDirectionMode.ToString());
            WaitingCountInSettings.ResetToFactoryDefaults();

            _session.SelectedScale = SettingsPageViewModel.DefaultTune;
            _session.Instrument = SettingsPageViewModel.DefaultInstrument;
            _session.Key = SettingsPageViewModel.DefaultKey;
            _session.Tune = "Selected Scale";
            _session.ScaleSelectionMode = ScaleSelectionMode.ByLevel;
            _session.IsRandomMode = false;
            SessionPreferences.Remove("musicmate.SelectedArpeggioId");
            SessionPreferences.Remove("musicmate.SelectedArpeggioRoot");
            SessionPreferences.Remove("musicmate.SelectedArpeggioDisplay");
            _session.ApplyAutomaticInstrumentRange(fullReset: true);
            _session.ResetAdvancedDetectionDefaults();
            _session.WrongDebounceMs = NoteSessionService.DefaultDebounceMs;
            _session.ResetPracticeCompositionDefaults();
            _session.AudioBufferSize = 1024;
            _session.PitchWindowSize = 4096;
            _session.MinFrequency = 60;
            _session.MaxFrequency = 8000;
            _session.SmoothingWindowSize = 3;
            _session.PitchConfidenceThreshold = 0.5;

            LevelUpService.ResetCriteriaToDefaults();
            _theme.ResetAllToFactoryDefaults();
        }

        private AppSettingsSnapshot CaptureCurrentSnapshot()
        {
            var (autoLow, autoHigh) = ResolveFactoryNoteRange();
            _ = autoLow;
            _ = autoHigh;

            return new AppSettingsSnapshot
            {
                SelectedScale = _session.SelectedScale,
                LowestNote = _session.LowestNote,
                HighestNote = _session.HighestNote,
                AccidentalPercent = _session.AccidentalPercent,
                Tempo = _session.Tempo,
                PlaybackBpm = _session.Tempo,
                MusicBpm = _session.Tempo,
                CorrectThreshold = _session.CorrectThreshold,
                MinCorrectCount = _session.MinCorrectCount,
                OmitMsAvgThreshold = SessionPreferences.Get(OmitMsAvgKey, _session.OmitMsAvgThreshold),
                AutoStart = _session.AutoStart,
                MasteredMethod = _session.MasteredMethod,
                StreakCrit = _session.StreakCrit,
                UseNoteMasteryForGeneration = _session.UseNoteMasteryForGeneration,
                MeterTimeSignature = _session.MeterTimeSignature,
                SmallestRhythmNote = _session.SmallestRhythmNote,
                RhythmMode = _session.RhythmMode,
                SyncopationSetting = _session.SyncopationSetting,
                NoteNameDisplay = _session.NoteNameDisplay,
                ShowConductorCues = _session.ShowConductorCues,
                ShowSignaturesOnBothStaffs = _session.ShowSignaturesOnBothStaffs,
                AboutFontSize = SessionPreferences.Get(AboutPageViewModel.FontSizePreferenceKey, 14.0),
                CollectNoteStats = SessionPreferences.Get(CollectNoteStatsKey, true),
                CollectSessionStats = SessionPreferences.Get(CollectSessionStatsKey, true),
                MaxSessionDbSizeMb = SessionPreferences.Get(MaxSessionDbSizeMbKey, 50),
                MaxAttemptsPerNote = SessionPreferences.Get(MaxAttemptsPerNoteKey, 100),
                Instrument = NoteSessionService.NormalizeInstrumentOption(_session.Instrument),
                Key = _session.Key,
                Tune = _session.Tune ?? "Selected Scale",
                Tolerance = _session.Tolerance,
                PitchOffsetCents = _session.PitchOffsetCents,
                RmsThreshold = _session.RmsThreshold,
                CooldownMs = _session.CooldownMs,
                WrongDebounceMs = _session.WrongDebounceMs,
                PcTunes = _session.PcTunes,
                PcRandom = _session.PcRandom,
                PcScales = _session.PcScales,
                PcArpeggios = _session.PcArpeggios,
                AudioBufferSize = _session.AudioBufferSize,
                PitchWindowSize = _session.PitchWindowSize,
                MinFrequency = _session.MinFrequency,
                MaxFrequency = _session.MaxFrequency,
                SmoothingWindowSize = _session.SmoothingWindowSize,
                PitchConfidenceThreshold = _session.PitchConfidenceThreshold,
                RepeatDelaySeconds = SessionPreferences.Get(RepeatDelaySecondsKey, 2.0),
                LevelUpSessionCount = SessionPreferences.Get("LevelUp.SessionCount", LevelUpService.DefaultSessionCount),
                LevelUpMinPitchPct = SessionPreferences.Get("LevelUp.MinPitchPct", LevelUpService.DefaultMinPitchAccuracyPercent),
                LevelUpMinTimingPct = SessionPreferences.Get("LevelUp.MinTimingPct", LevelUpService.DefaultMinTimingAccuracyPercent),
                LevelUpMinOverallPct = SessionPreferences.Get("LevelUp.MinOverallPct", LevelUpService.DefaultMinOverallAccuracyPercent),
                LevelUpMinNotes = SessionPreferences.Get("LevelUp.MinNotes", LevelUpService.DefaultMinNotesPerSession),
                ThemeColorsHex = CaptureThemeColors(),
            };
        }

        private AppSettingsSnapshot BuildFactoryDefaultsSnapshot()
        {
            var (low, high) = ResolveFactoryNoteRange();
            return new AppSettingsSnapshot
            {
                SelectedScale = SettingsPageViewModel.DefaultTune,
                LowestNote = low,
                HighestNote = high,
                AccidentalPercent = SettingsPageViewModel.DefaultAccidentalPct,
                Tempo = SettingsPageViewModel.DefaultTempo,
                PlaybackBpm = SettingsPageViewModel.DefaultTempo,
                MusicBpm = SettingsPageViewModel.DefaultTempo,
                CorrectThreshold = SettingsPageViewModel.DefaultCorrectThreshold,
                MinCorrectCount = SettingsPageViewModel.DefaultMinCorrectCount,
                OmitMsAvgThreshold = SettingsPageViewModel.DefaultOmitMsAvg,
                AutoStart = SettingsPageViewModel.DefaultAutoStart,
                MasteredMethod = MasteryPreferenceDefaults.MasteredMethod,
                StreakCrit = MasteryPreferenceDefaults.StreakCrit,
                UseNoteMasteryForGeneration = MasteryPreferenceDefaults.UseNoteMasteryForGeneration,
                MeterTimeSignature = "4/4",
                SmallestRhythmNote = "Quarter",
                RhythmMode = "Simple",
                SyncopationSetting = "None",
                NoteNameDisplay = "Current only",
                ShowConductorCues = false,
                ShowSignaturesOnBothStaffs = true,
                AboutFontSize = 14.0,
                CollectNoteStats = SettingsPageViewModel.DefaultCollectNote,
                CollectSessionStats = SettingsPageViewModel.DefaultCollectSession,
                MaxSessionDbSizeMb = SettingsPageViewModel.DefaultMaxSessionDbMb,
                MaxAttemptsPerNote = 100,
                Instrument = NoteSessionService.NormalizeInstrumentOption(SettingsPageViewModel.DefaultInstrument),
                Key = SettingsPageViewModel.DefaultKey,
                Tune = "Selected Scale",
                Tolerance = NoteSessionService.DefaultTolerance,
                PitchOffsetCents = NoteSessionService.DefaultPitchOffsetCents,
                RmsThreshold = NoteSessionService.DefaultRmsThreshold,
                CooldownMs = NoteSessionService.DefaultCooldownMs,
                WrongDebounceMs = NoteSessionService.DefaultDebounceMs,
                PcTunes = NoteSessionService.DefaultPcTunes,
                PcRandom = NoteSessionService.DefaultPcRandom,
                PcScales = NoteSessionService.DefaultPcScales,
                PcArpeggios = NoteSessionService.DefaultPcArpeggios,
                AudioBufferSize = 1024,
                PitchWindowSize = 4096,
                MinFrequency = 60,
                MaxFrequency = 8000,
                SmoothingWindowSize = 3,
                PitchConfidenceThreshold = 0.5,
                RepeatDelaySeconds = 2.0,
                LevelUpSessionCount = LevelUpService.DefaultSessionCount,
                LevelUpMinPitchPct = LevelUpService.DefaultMinPitchAccuracyPercent,
                LevelUpMinTimingPct = LevelUpService.DefaultMinTimingAccuracyPercent,
                LevelUpMinOverallPct = LevelUpService.DefaultMinOverallAccuracyPercent,
                LevelUpMinNotes = LevelUpService.DefaultMinNotesPerSession,
                ThemeColorsHex = CaptureFactoryThemeColors(),
            };
        }

        private (string Low, string High) ResolveFactoryNoteRange()
        {
            var profile = InstrumentCatalog.Resolve(SettingsPageViewModel.DefaultInstrument);
            int level = _session.ChildLevel > 0 ? _session.ChildLevel : 0;
            return InstrumentCatalog.GetAutomaticRange(profile, level);
        }

        private Dictionary<string, string> CaptureThemeColors()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (AppColorTarget target in Enum.GetValues<AppColorTarget>())
                map[target.ToString()] = NormalizeHex(_theme.GetColor(target).ToHex());
            return map;
        }

        private Dictionary<string, string> CaptureFactoryThemeColors()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (AppColorTarget target in Enum.GetValues<AppColorTarget>())
                map[target.ToString()] = NormalizeHex(_theme.GetFactoryDefaultColor(target).ToHex());
            return map;
        }

        private bool MatchesFactoryDefaults(AppSettingsSnapshot current)
        {
            var factory = BuildFactoryDefaultsSnapshot();
            return current.EqualsFactory(factory);
        }

        private static string NormalizeHex(string hex)
            => (hex ?? string.Empty).Trim().ToUpperInvariant();

        private void ApplySnapshot(AppSettingsSnapshot snapshot)
        {
            if (snapshot.ThemeColorsHex != null)
            {
                foreach (var pair in snapshot.ThemeColorsHex)
                {
                    if (Enum.TryParse<AppColorTarget>(pair.Key, out var target))
                        _theme.SetColor(target, Color.FromArgb(pair.Value));
                }
            }
            else if (!string.IsNullOrWhiteSpace(snapshot.StaffPanelColorHex))
            {
                _theme.SetColor(AppColorTarget.PanelBackground, Color.FromArgb(snapshot.StaffPanelColorHex));
            }

            _session.SelectedScale = snapshot.SelectedScale;
            _session.LowestNote = snapshot.LowestNote;
            _session.HighestNote = snapshot.HighestNote;
            _session.AccidentalPercent = snapshot.AccidentalPercent;
            _session.Tempo = snapshot.Tempo > 0 ? snapshot.Tempo : snapshot.MusicBpm;
            _session.CorrectThreshold = snapshot.CorrectThreshold;
            _session.MinCorrectCount = snapshot.MinCorrectCount;
            _session.OmitMsAvgThreshold = snapshot.OmitMsAvgThreshold;
            _session.AutoStart = snapshot.AutoStart;
            _session.MasteredMethod = snapshot.MasteredMethod;
            _session.StreakCrit = snapshot.StreakCrit;
            _session.UseNoteMasteryForGeneration = snapshot.UseNoteMasteryForGeneration;
            _session.MeterTimeSignature = snapshot.MeterTimeSignature;
            _session.SmallestRhythmNote = snapshot.SmallestRhythmNote;
            _session.RhythmMode = snapshot.RhythmMode;
            _session.SyncopationSetting = snapshot.SyncopationSetting;
            _session.NoteNameDisplay = snapshot.NoteNameDisplay;
            _session.ShowConductorCues = snapshot.ShowConductorCues;
            _session.ShowSignaturesOnBothStaffs = snapshot.ShowSignaturesOnBothStaffs;

            SessionPreferences.Set(CollectNoteStatsKey, snapshot.CollectNoteStats);
            SessionPreferences.Set(CollectSessionStatsKey, snapshot.CollectSessionStats);
            SessionPreferences.Set(MaxSessionDbSizeMbKey, snapshot.MaxSessionDbSizeMb);
            SessionPreferences.Set(MaxAttemptsPerNoteKey, snapshot.MaxAttemptsPerNote);
            SessionPreferences.Set(AboutPageViewModel.FontSizePreferenceKey, snapshot.AboutFontSize);

            _session.Instrument = snapshot.Instrument;
            _session.Key = snapshot.Key;
            _session.Tune = snapshot.Tune;
            _session.Tolerance = snapshot.Tolerance;
            _session.PitchOffsetCents = snapshot.PitchOffsetCents;
            _session.RmsThreshold = snapshot.RmsThreshold;
            _session.CooldownMs = snapshot.CooldownMs;
            _session.WrongDebounceMs = snapshot.WrongDebounceMs;
            _session.SetPracticeCompositionPercents(
                snapshot.PcTunes, snapshot.PcRandom, snapshot.PcScales, snapshot.PcArpeggios);

            _session.AudioBufferSize = snapshot.AudioBufferSize;
            _session.PitchWindowSize = snapshot.PitchWindowSize;
            _session.MinFrequency = snapshot.MinFrequency;
            _session.MaxFrequency = snapshot.MaxFrequency;
            _session.SmoothingWindowSize = snapshot.SmoothingWindowSize;
            _session.PitchConfidenceThreshold = snapshot.PitchConfidenceThreshold;

            SessionPreferences.Set(RepeatDelaySecondsKey, snapshot.RepeatDelaySeconds);
            SessionPreferences.Set("LevelUp.SessionCount", snapshot.LevelUpSessionCount);
            SessionPreferences.Set("LevelUp.MinPitchPct", snapshot.LevelUpMinPitchPct);
            SessionPreferences.Set("LevelUp.MinTimingPct", snapshot.LevelUpMinTimingPct);
            SessionPreferences.Set("LevelUp.MinOverallPct", snapshot.LevelUpMinOverallPct);
            SessionPreferences.Set("LevelUp.MinNotes", snapshot.LevelUpMinNotes);
        }

        internal sealed class AppSettingsSnapshot
        {
            public string StaffPanelColorHex { get; init; } = "#CFFACD";  //  2026.07.19 1604  
            public Dictionary<string, string>? ThemeColorsHex { get; init; }
            public string SelectedScale { get; init; } = "Major";
            public string LowestNote { get; init; } = "C4";
            public string HighestNote { get; init; } = "F5";
            public int AccidentalPercent { get; init; }
            public int Tempo { get; init; } = 100;
            public int PlaybackBpm { get; init; } = 100;
            public int MusicBpm { get; init; } = 100;
            public int CorrectThreshold { get; init; } = MasteryPreferenceDefaults.CorrectThreshold;
            public int MinCorrectCount { get; init; } = MasteryPreferenceDefaults.MinCorrectCount;
            public int OmitMsAvgThreshold { get; init; } = MasteryPreferenceDefaults.OmitMsAvgThreshold;
            public bool AutoStart { get; init; } = true;
            public string MasteredMethod { get; init; } = MasteryPreferenceDefaults.MasteredMethod;
            public int StreakCrit { get; init; } = MasteryPreferenceDefaults.StreakCrit;
            public bool UseNoteMasteryForGeneration { get; init; } = MasteryPreferenceDefaults.UseNoteMasteryForGeneration;
            public string MeterTimeSignature { get; init; } = "4/4";
            public string SmallestRhythmNote { get; init; } = "Quarter";
            public string RhythmMode { get; init; } = "Simple";
            public string SyncopationSetting { get; init; } = "None";
            public string NoteNameDisplay { get; init; } = "Current only";
            public bool ShowConductorCues { get; init; }
            public bool ShowSignaturesOnBothStaffs { get; init; } = true;
            public double AboutFontSize { get; init; } = 12;
            public bool CollectNoteStats { get; init; } = true;
            public bool CollectSessionStats { get; init; } = true;
            public int MaxSessionDbSizeMb { get; init; } = 50;
            public int MaxAttemptsPerNote { get; init; } = 100;
            public string Instrument { get; init; } = "bb-clarinet";
            public string Key { get; init; } = "C";
            public string Tune { get; init; } = "Selected Scale";
            public int Tolerance { get; init; } = NoteSessionService.DefaultTolerance;
            public double PitchOffsetCents { get; init; }
            public float RmsThreshold { get; init; } = NoteSessionService.DefaultRmsThreshold;
            public int CooldownMs { get; init; } = NoteSessionService.DefaultCooldownMs;
            public int WrongDebounceMs { get; init; } = NoteSessionService.DefaultDebounceMs;
            public int PcTunes { get; init; } = NoteSessionService.DefaultPcTunes;
            public int PcRandom { get; init; } = NoteSessionService.DefaultPcRandom;
            public int PcScales { get; init; } = NoteSessionService.DefaultPcScales;
            public int PcArpeggios { get; init; } = NoteSessionService.DefaultPcArpeggios;
            public int AudioBufferSize { get; init; } = 1024;
            public int PitchWindowSize { get; init; } = 4096;
            public int MinFrequency { get; init; } = 60;
            public int MaxFrequency { get; init; } = 8000;
            public int SmoothingWindowSize { get; init; } = 3;
            public double PitchConfidenceThreshold { get; init; } = 0.5;
            public double RepeatDelaySeconds { get; init; } = 2.0;
            public int LevelUpSessionCount { get; init; } = LevelUpService.DefaultSessionCount;
            public double LevelUpMinPitchPct { get; init; } = LevelUpService.DefaultMinPitchAccuracyPercent;
            public double LevelUpMinTimingPct { get; init; } = LevelUpService.DefaultMinTimingAccuracyPercent;
            public double LevelUpMinOverallPct { get; init; } = LevelUpService.DefaultMinOverallAccuracyPercent;
            public int LevelUpMinNotes { get; init; } = LevelUpService.DefaultMinNotesPerSession;

            public bool EqualsFactory(AppSettingsSnapshot other)
            {
                if (other == null) return false;
                return StringEq(SelectedScale, other.SelectedScale)
                    && StringEq(LowestNote, other.LowestNote)
                    && StringEq(HighestNote, other.HighestNote)
                    && AccidentalPercent == other.AccidentalPercent
                    && Tempo == other.Tempo
                    && CorrectThreshold == other.CorrectThreshold
                    && MinCorrectCount == other.MinCorrectCount
                    && OmitMsAvgThreshold == other.OmitMsAvgThreshold
                    && AutoStart == other.AutoStart
                    && StringEq(MasteredMethod, other.MasteredMethod)
                    && StreakCrit == other.StreakCrit
                    && UseNoteMasteryForGeneration == other.UseNoteMasteryForGeneration
                    && StringEq(MeterTimeSignature, other.MeterTimeSignature)
                    && StringEq(SmallestRhythmNote, other.SmallestRhythmNote)
                    && StringEq(RhythmMode, other.RhythmMode)
                    && StringEq(SyncopationSetting, other.SyncopationSetting)
                    && StringEq(NoteNameDisplay, other.NoteNameDisplay)
                    && ShowConductorCues == other.ShowConductorCues
                    && ShowSignaturesOnBothStaffs == other.ShowSignaturesOnBothStaffs
                    && NearlyEqual(AboutFontSize, other.AboutFontSize)
                    && CollectNoteStats == other.CollectNoteStats
                    && CollectSessionStats == other.CollectSessionStats
                    && MaxSessionDbSizeMb == other.MaxSessionDbSizeMb
                    && MaxAttemptsPerNote == other.MaxAttemptsPerNote
                    && StringEq(Instrument, other.Instrument)
                    && StringEq(Key, other.Key)
                    && StringEq(Tune, other.Tune)
                    && Tolerance == other.Tolerance
                    && NearlyEqual(PitchOffsetCents, other.PitchOffsetCents)
                    && NearlyEqual(RmsThreshold, other.RmsThreshold)
                    && CooldownMs == other.CooldownMs
                    && WrongDebounceMs == other.WrongDebounceMs
                    && PcTunes == other.PcTunes
                    && PcRandom == other.PcRandom
                    && PcScales == other.PcScales
                    && PcArpeggios == other.PcArpeggios
                    && AudioBufferSize == other.AudioBufferSize
                    && PitchWindowSize == other.PitchWindowSize
                    && MinFrequency == other.MinFrequency
                    && MaxFrequency == other.MaxFrequency
                    && SmoothingWindowSize == other.SmoothingWindowSize
                    && NearlyEqual(PitchConfidenceThreshold, other.PitchConfidenceThreshold)
                    && NearlyEqual(RepeatDelaySeconds, other.RepeatDelaySeconds)
                    && LevelUpSessionCount == other.LevelUpSessionCount
                    && NearlyEqual(LevelUpMinPitchPct, other.LevelUpMinPitchPct)
                    && NearlyEqual(LevelUpMinTimingPct, other.LevelUpMinTimingPct)
                    && NearlyEqual(LevelUpMinOverallPct, other.LevelUpMinOverallPct)
                    && LevelUpMinNotes == other.LevelUpMinNotes
                    && ThemeColorsMatch(ThemeColorsHex, other.ThemeColorsHex);
            }

            private static bool StringEq(string? a, string? b)
                => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

            private static bool NearlyEqual(double a, double b)
                => Math.Abs(a - b) < 0.0001;

            private static bool NearlyEqual(float a, float b)
                => Math.Abs(a - b) < 0.0001f;

            private static bool ThemeColorsMatch(Dictionary<string, string>? a, Dictionary<string, string>? b)
            {
                if (a == null || b == null) return a == b;
                if (a.Count != b.Count) return false;
                foreach (var pair in a)
                {
                    if (!b.TryGetValue(pair.Key, out var other))
                        return false;
                    if (!string.Equals(
                            Normalize(pair.Value), Normalize(other), StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                return true;
            }

            private static string Normalize(string hex) => (hex ?? string.Empty).Trim();
        }
    }

    public enum ActiveDefaultsSet
    {
        Factory,
        Custom
    }
}
