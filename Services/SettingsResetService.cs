#nullable enable
using musicmate.Diagnostics;
using System.Text.Json;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using musicmate.ViewModels;

namespace musicmate.Services
{
    /// <summary>
    /// Factory reset and user-defined custom default settings.
    /// </summary>
    public sealed class SettingsResetService
    {
        private const string CustomDefaultsJsonKey = "musicmate.CustomDefaults.Json";
        private const string CustomDefaultsExistsKey = "musicmate.CustomDefaults.Exists";
        private const string ActiveDefaultsKey = "musicmate.ActiveDefaults";

        private readonly NoteSessionService _session;
        private readonly ThemeService _theme;

        public SettingsResetService(NoteSessionService session, ThemeService theme)
        {
            _session = session;
            _theme = theme;
        }

        public bool HasCustomDefaults => Preferences.Get(CustomDefaultsExistsKey, false);

        public ActiveDefaultsSet ActiveDefaults => Preferences.Get(ActiveDefaultsKey, ActiveDefaultsSet.Factory.ToString()) switch
        {
            nameof(ActiveDefaultsSet.Custom) => ActiveDefaultsSet.Custom,
            _ => ActiveDefaultsSet.Factory,
        };

        public void SetActiveDefaults(ActiveDefaultsSet set)
            => Preferences.Set(ActiveDefaultsKey, set.ToString());

        /// <summary>Resets all settings to factory defaults (same behavior as the former Settings page button).</summary>
        public void ResetToFactoryDefaults()
        {
            DebugLog.WriteLine("[ResetOptionsTest] ResetToFactoryDefaults called.");
            _session.Tempo = SettingsPageViewModel.DefaultTempo;
            _session.AccidentalPercent = SettingsPageViewModel.DefaultAccidentalPct;
            _session.CorrectThreshold = SettingsPageViewModel.DefaultCorrectThreshold;
            _session.MinCorrectCount = SettingsPageViewModel.DefaultMinCorrectCount;
            Preferences.Set("musicmate.OmitMsAvgThreshold", SettingsPageViewModel.DefaultOmitMsAvg);
            _session.OmitMsAvgThreshold = SettingsPageViewModel.DefaultOmitMsAvg;
            _session.AutoStart = SettingsPageViewModel.DefaultAutoStart;
            _session.MasteredMethod = "% Correct";
            _session.StreakCrit = 3;
            Preferences.Default.Set("CollectNoteStats", SettingsPageViewModel.DefaultCollectNote);
            Preferences.Default.Set("CollectSessionStats", SettingsPageViewModel.DefaultCollectSession);
            Preferences.Default.Set("MaxSessionDbSizeMb", SettingsPageViewModel.DefaultMaxSessionDbMb);

            _session.SelectedScale = SettingsPageViewModel.DefaultTune;  // "Major" — correct for scale
            _session.Instrument = SettingsPageViewModel.DefaultInstrument;
            _session.Key = SettingsPageViewModel.DefaultKey;
            // [ResetOptionsTest] Tune is a mode tag ("Selected Scale"), not a scale name.
            _session.Tune = "Selected Scale";
            DebugLog.WriteLine($"[ResetOptionsTest] After factory reset: Tune={_session.Tune} Scale={_session.SelectedScale} Key={_session.Key} AccPct={_session.AccidentalPercent}");
            _session.ApplyAutomaticInstrumentRange(fullReset: true);
            _session.ResetAdvancedDetectionDefaults();
            _session.ResetPracticeCompositionDefaults();

            LevelUpService.ResetCriteriaToDefaults();
            _theme.ResetAllToFactoryDefaults();
            SetActiveDefaults(ActiveDefaultsSet.Factory);
        }

        /// <summary>Saves the currently active settings as the user's custom defaults.</summary>
        public void SaveCustomDefaultsFromCurrent()
        {
            DebugLog.WriteLine($"[ResetOptionsTest] SaveCustomDefaults: Tune={_session.Tune} Scale={_session.SelectedScale} Key={_session.Key}");
            var snapshot = CaptureCurrentSnapshot();
            var json = JsonSerializer.Serialize(snapshot);
            Preferences.Set(CustomDefaultsJsonKey, json);
            Preferences.Set(CustomDefaultsExistsKey, true);
        }

        /// <summary>Restores settings from the saved custom defaults.</summary>
        public void RestoreCustomDefaults()
        {
            DebugLog.WriteLine("[ResetOptionsTest] RestoreCustomDefaults called.");
            if (!HasCustomDefaults)
                return;

            var json = Preferences.Get(CustomDefaultsJsonKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return;

            var snapshot = JsonSerializer.Deserialize<AppSettingsSnapshot>(json);
            if (snapshot == null)
                return;

            ApplySnapshot(snapshot);
            SetActiveDefaults(ActiveDefaultsSet.Custom);
        }

        private AppSettingsSnapshot CaptureCurrentSnapshot()
        {
            return new AppSettingsSnapshot
            {
                StaffPanelColorHex = _theme.PanelBackgroundColor.ToHex(),
                SelectedScale = _session.SelectedScale,
                LowestNote = _session.LowestNote,
                HighestNote = _session.HighestNote,
                AccidentalPercent = _session.AccidentalPercent,
                Tempo = _session.Tempo,
                PlaybackBpm = _session.Tempo,
                MusicBpm = _session.Tempo,
                CorrectThreshold = _session.CorrectThreshold,
                MinCorrectCount = _session.MinCorrectCount,
                OmitMsAvgThreshold = Preferences.Get("musicmate.OmitMsAvgThreshold", _session.OmitMsAvgThreshold),
                AutoStart = _session.AutoStart,
                MasteredMethod = _session.MasteredMethod,
                StreakCrit = _session.StreakCrit,
                MeterTimeSignature = _session.MeterTimeSignature,
                SmallestRhythmNote = _session.SmallestRhythmNote,
                RhythmMode = _session.RhythmMode,
                SyncopationSetting = _session.SyncopationSetting,
                NoteNameDisplay = _session.NoteNameDisplay,
                CollectNoteStats = Preferences.Default.Get("CollectNoteStats", true),
                CollectSessionStats = Preferences.Default.Get("CollectSessionStats", true),
                MaxSessionDbSizeMb = Preferences.Default.Get("MaxSessionDbSizeMb", 50),
                MaxAttemptsPerNote = Preferences.Default.Get("MaxAttemptsPerNote", 100),
                Instrument = _session.Instrument,
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
                RepeatDelaySeconds = Preferences.Default.Get("RepeatDelaySeconds", 2.0),
                LevelUpSessionCount = Preferences.Default.Get("LevelUp.SessionCount", LevelUpService.DefaultSessionCount),
                LevelUpMinPitchPct = Preferences.Default.Get("LevelUp.MinPitchPct", LevelUpService.DefaultMinPitchAccuracyPercent),
                LevelUpMinTimingPct = Preferences.Default.Get("LevelUp.MinTimingPct", LevelUpService.DefaultMinTimingAccuracyPercent),
                LevelUpMinOverallPct = Preferences.Default.Get("LevelUp.MinOverallPct", LevelUpService.DefaultMinOverallAccuracyPercent),
                LevelUpMinNotes = Preferences.Default.Get("LevelUp.MinNotes", LevelUpService.DefaultMinNotesPerSession),
            };
        }

        private void ApplySnapshot(AppSettingsSnapshot snapshot)
        {
            if (!string.IsNullOrWhiteSpace(snapshot.StaffPanelColorHex))
                _theme.SetColor(AppColorTarget.PanelBackground, Color.FromArgb(snapshot.StaffPanelColorHex));

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
            _session.MeterTimeSignature = snapshot.MeterTimeSignature;
            _session.SmallestRhythmNote = snapshot.SmallestRhythmNote;
            _session.RhythmMode = snapshot.RhythmMode;
            _session.SyncopationSetting = snapshot.SyncopationSetting;
            _session.NoteNameDisplay = snapshot.NoteNameDisplay;

            Preferences.Default.Set("CollectNoteStats", snapshot.CollectNoteStats);
            Preferences.Default.Set("CollectSessionStats", snapshot.CollectSessionStats);
            Preferences.Default.Set("MaxSessionDbSizeMb", snapshot.MaxSessionDbSizeMb);
            Preferences.Default.Set("MaxAttemptsPerNote", snapshot.MaxAttemptsPerNote);

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

            Preferences.Default.Set("RepeatDelaySeconds", snapshot.RepeatDelaySeconds);
            Preferences.Default.Set("LevelUp.SessionCount", snapshot.LevelUpSessionCount);
            Preferences.Default.Set("LevelUp.MinPitchPct", snapshot.LevelUpMinPitchPct);
            Preferences.Default.Set("LevelUp.MinTimingPct", snapshot.LevelUpMinTimingPct);
            Preferences.Default.Set("LevelUp.MinOverallPct", snapshot.LevelUpMinOverallPct);
            Preferences.Default.Set("LevelUp.MinNotes", snapshot.LevelUpMinNotes);
        }

        private sealed class AppSettingsSnapshot
        {
            public string StaffPanelColorHex { get; init; } = "#FFFFFF";
            public string SelectedScale { get; init; } = "Major";
            public string LowestNote { get; init; } = "C4";
            public string HighestNote { get; init; } = "F5";
            public int AccidentalPercent { get; init; }
            public int Tempo { get; init; } = 100;
            public int PlaybackBpm { get; init; } = 100;
            public int MusicBpm { get; init; } = 100;
            public int CorrectThreshold { get; init; }
            public int MinCorrectCount { get; init; } = 3;
            public int OmitMsAvgThreshold { get; init; }
            public bool AutoStart { get; init; }
            public string MasteredMethod { get; init; } = "% Correct";
            public int StreakCrit { get; init; } = 3;
            public string MeterTimeSignature { get; init; } = "4/4";
            public string SmallestRhythmNote { get; init; } = "Quarter";
            public string RhythmMode { get; init; } = "Simple";
            public string SyncopationSetting { get; init; } = "None";
            public string NoteNameDisplay { get; init; } = "Current only";
            public bool CollectNoteStats { get; init; } = true;
            public bool CollectSessionStats { get; init; } = true;
            public int MaxSessionDbSizeMb { get; init; } = 50;
            public int MaxAttemptsPerNote { get; init; } = 100;
            public string Instrument { get; init; } = "C";
            public string Key { get; init; } = "C";
            public string Tune { get; init; } = "Major";
            public int Tolerance { get; init; }
            public double PitchOffsetCents { get; init; }
            public float RmsThreshold { get; init; }
            public int CooldownMs { get; init; }
            public int WrongDebounceMs { get; init; }
            public int PcTunes { get; init; }
            public int PcRandom { get; init; }
            public int PcScales { get; init; }
            public int PcArpeggios { get; init; }
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
        }
    }

    public enum ActiveDefaultsSet
    {
        Factory,
        Custom
    }
}
