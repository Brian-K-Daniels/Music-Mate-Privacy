using System.ComponentModel;
using System.IO;
using musicmate.Services;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Graphics;

namespace musicmate.ViewModels
{
    public class AdvancedPageViewModel : INotifyPropertyChanged
    {
        // --- Actual values from the current session ---
        public int CurrentSessionNotesCount => _session.NotesToDraw?.Count ?? 0;
        public double CurrentSessionPitchAccuracyPercent
        {
            get
            {
                var (_, _, percent) = _session.GetSessionCorrectWrongTotals();
                return double.IsNaN(percent) ? 0.0 : percent;
            }
        }

        /// <summary>Null when fewer than 3 timed onsets were recorded.</summary>
        public double? CurrentSessionTimingAccuracyPercent => _session.GetTimingAccuracyPercent();

        public double CurrentSessionOverallAccuracyPercent => CurrentSessionPitchAccuracyPercent;
        public int CurrentSessionCount => 1; // Placeholder: set to 1, or expose actual session count if tracked
        private readonly ThemeService _themeService;
        private readonly NoteSessionService _session;

        public AdvancedPageViewModel(ThemeService themeService, NoteSessionService session)
        {
            _themeService = themeService;
            _session = session;

            // Listen for theme changes
            _themeService.PropertyChanged += Theme_PropertyChanged;
            _themeService.ThemeColorsChanged += (_, _) => RefreshThemeColorBindings();

            // Forward session property changes so the view model updates when other pages modify session settings
            _session.PropertyChanged += (s, e) =>
            {
                // Map session property name to view model properties
                switch (e.PropertyName)
                {
                    case nameof(NoteSessionService.Tolerance):
                        OnPropertyChanged(nameof(Tolerance));
                        break;
                    case nameof(NoteSessionService.RmsThreshold):
                        OnPropertyChanged(nameof(RmsThreshold));
                        break;
                    case nameof(NoteSessionService.CooldownMs):
                        OnPropertyChanged(nameof(CooldownMs));
                        break;
                    case nameof(NoteSessionService.PitchOffsetCents):
                        OnPropertyChanged(nameof(PitchOffsetCents));
                        break;
                    case nameof(NoteSessionService.AudioBufferSize):
                        OnPropertyChanged(nameof(AudioBufferSize));
                        break;
                    case nameof(NoteSessionService.PitchWindowSize):
                        OnPropertyChanged(nameof(PitchWindowSize));
                        break;
                    case nameof(NoteSessionService.MinFrequency):
                        OnPropertyChanged(nameof(MinFrequency));
                        break;
                    case nameof(NoteSessionService.MaxFrequency):
                        OnPropertyChanged(nameof(MaxFrequency));
                        break;
                    case nameof(NoteSessionService.SmoothingWindowSize):
                        OnPropertyChanged(nameof(SmoothingWindowSize));
                        break;
                    case nameof(NoteSessionService.PitchConfidenceThreshold):
                        OnPropertyChanged(nameof(PitchConfidenceThreshold));
                        break;
                    case nameof(NoteSessionService.WrongDebounceMs):
                        OnPropertyChanged(nameof(WrongDebounceMs));
                        break;
                    case nameof(NoteSessionService.PcTunes):
                    case nameof(NoteSessionService.PcRandom):
                    case nameof(NoteSessionService.PcScales):
                    case nameof(NoteSessionService.PcArpeggios):
                        SyncCompositionDraftsFromSession();
                        break;
                    case nameof(NoteSessionService.TimingStatsDisplay):
                    case nameof(NoteSessionService.SessionCompleted):
                        RefreshCurrentSessionMetrics();
                        break;
                    default:
                        break;
                }
            };
        }

        public void RefreshCurrentSessionMetrics()
        {
            OnPropertyChanged(nameof(CurrentSessionNotesCount));
            OnPropertyChanged(nameof(CurrentSessionPitchAccuracyPercent));
            OnPropertyChanged(nameof(CurrentSessionTimingAccuracyPercent));
            OnPropertyChanged(nameof(CurrentSessionOverallAccuracyPercent));
        }

        private void Theme_PropertyChanged(object? sender, PropertyChangedEventArgs e)
            => RefreshThemeColorBindings();

        private void RefreshThemeColorBindings()
        {
            OnPropertyChanged(nameof(PanelBackgroundColor));
            OnPropertyChanged(nameof(SecondPanelBackgroundColor));
            OnPropertyChanged(nameof(ContrastingTextColor));
            OnPropertyChanged(nameof(TextColor));
            OnPropertyChanged(nameof(HeadingTextColor));
            OnPropertyChanged(nameof(SliderColor));
            OnPropertyChanged(nameof(ButtonBackgroundColor));
            OnPropertyChanged(nameof(ButtonTextColor));
            OnPropertyChanged(nameof(PickerBackgroundColor));
            OnPropertyChanged(nameof(PickerTextColor));
            OnPropertyChanged(nameof(PickerBorderColor));
            OnPropertyChanged(nameof(EntryBackgroundColor));
            OnPropertyChanged(nameof(EntryTextColor));
        }

        public Color PanelBackgroundColor => _themeService.PanelBackgroundColor;
        public Color SecondPanelBackgroundColor => _themeService.SecondPanelBackgroundColor;
        public Color ContrastingTextColor => _themeService.ContrastingTextColor;
        public Color TextColor => _themeService.TextColor;
        public Color HeadingTextColor => _themeService.HeadingTextColor;
        public Color SliderColor => _themeService.SliderColor;
        public Color ButtonBackgroundColor => _themeService.ButtonBackgroundColor;
        public Color ButtonTextColor => _themeService.ButtonTextColor;
        public Color PickerBackgroundColor => _themeService.PickerBackgroundColor;
        public Color PickerTextColor => _themeService.PickerTextColor;
        public Color PickerBorderColor => _themeService.PickerBorderColor;
        public Color EntryBackgroundColor => _themeService.EntryBackgroundColor;
        public Color EntryTextColor => _themeService.EntryTextColor;

        public int AccidentalPercent
        {
            get => _session.AccidentalPercent;
            set
            {
                _session.AccidentalPercent = value;
                if (_session.ChildLevel > 0)
                    _session.MarkChildPracticeSettingsCustomized();
                OnPropertyChanged(nameof(AccidentalPercent));
            }
        }
        public int AudioBufferSize
        {
            get => _session.AudioBufferSize;
            set { _session.AudioBufferSize = value; OnPropertyChanged(nameof(AudioBufferSize)); }
        }

        public int PitchWindowSize
        {
            get => _session.PitchWindowSize;
            set { _session.PitchWindowSize = value; OnPropertyChanged(nameof(PitchWindowSize)); }
        }

        public int MinFrequency
        {
            get => _session.MinFrequency;
            set { _session.MinFrequency = value; OnPropertyChanged(nameof(MinFrequency)); }
        }

        public int MaxFrequency
        {
            get => _session.MaxFrequency;
            set { _session.MaxFrequency = value; OnPropertyChanged(nameof(MaxFrequency)); }
        }

        public int SmoothingWindowSize
        {
            get => _session.SmoothingWindowSize;
            set { _session.SmoothingWindowSize = value; OnPropertyChanged(nameof(SmoothingWindowSize)); }
        }

        public double PitchConfidenceThreshold
        {
            get => _session.PitchConfidenceThreshold;
            set { _session.PitchConfidenceThreshold = value; OnPropertyChanged(nameof(PitchConfidenceThreshold)); }
        }

        public int Tolerance
        {
            get => _session.Tolerance;
            set { _session.Tolerance = value; OnPropertyChanged(nameof(Tolerance)); }
        }

        public float RmsThreshold
        {
            get => _session.RmsThreshold;
            set { _session.RmsThreshold = value; OnPropertyChanged(nameof(RmsThreshold)); }
        }

        public int CooldownMs
        {
            get => _session.CooldownMs;
            set { _session.CooldownMs = value; OnPropertyChanged(nameof(CooldownMs)); }
        }

        public int WrongDebounceMs
        {
            get => _session.WrongDebounceMs;
            set { _session.WrongDebounceMs = value; OnPropertyChanged(nameof(WrongDebounceMs)); }
        }

        public double PitchOffsetCents
        {
            get => _session.PitchOffsetCents;
            set { _session.PitchOffsetCents = value; OnPropertyChanged(nameof(PitchOffsetCents)); }
        }

        public double RepeatDelaySeconds
        {
            get => SessionPreferences.Get("RepeatDelaySeconds", 2.0);
            set
            {
                SessionPreferences.Set("RepeatDelaySeconds", value);
                OnPropertyChanged(nameof(RepeatDelaySeconds));
                NotifyFactoryDefaultsMayHaveChanged();
            }
        }

        // LevelUp Criteria Properties
        public int SessionCount
        {
            get => SessionPreferences.Get("LevelUp.SessionCount", LevelUpService.DefaultSessionCount);
            set
            {
                SessionPreferences.Set("LevelUp.SessionCount", value);
                OnPropertyChanged(nameof(SessionCount));
                NotifyFactoryDefaultsMayHaveChanged();
            }
        }
        public double MinPitchAccuracyPercent
        {
            get => SessionPreferences.Get("LevelUp.MinPitchPct", LevelUpService.DefaultMinPitchAccuracyPercent);
            set
            {
                SessionPreferences.Set("LevelUp.MinPitchPct", value);
                OnPropertyChanged(nameof(MinPitchAccuracyPercent));
                NotifyFactoryDefaultsMayHaveChanged();
            }
        }
        public double MinTimingAccuracyPercent
        {
            get => SessionPreferences.Get("LevelUp.MinTimingPct", LevelUpService.DefaultMinTimingAccuracyPercent);
            set
            {
                SessionPreferences.Set("LevelUp.MinTimingPct", value);
                OnPropertyChanged(nameof(MinTimingAccuracyPercent));
                NotifyFactoryDefaultsMayHaveChanged();
            }
        }
        public double MinOverallAccuracyPercent
        {
            get => SessionPreferences.Get("LevelUp.MinOverallPct", LevelUpService.DefaultMinOverallAccuracyPercent);
            set
            {
                SessionPreferences.Set("LevelUp.MinOverallPct", value);
                OnPropertyChanged(nameof(MinOverallAccuracyPercent));
                NotifyFactoryDefaultsMayHaveChanged();
            }
        }
        public int MinNotesPerSession
        {
            get => SessionPreferences.Get("LevelUp.MinNotes", LevelUpService.DefaultMinNotesPerSession);
            set
            {
                SessionPreferences.Set("LevelUp.MinNotes", value);
                OnPropertyChanged(nameof(MinNotesPerSession));
                NotifyFactoryDefaultsMayHaveChanged();
            }
        }

        private static void NotifyFactoryDefaultsMayHaveChanged()
            => ServiceHelper.GetService<SettingsResetService>()?.NotifySettingsChanged();


        public int PcTunesDraft
        {
            get => _pcTunesDraft;
            set { _pcTunesDraft = value; OnPropertyChanged(nameof(PcTunesDraft)); }
        }

        public int PcRandomDraft
        {
            get => _pcRandomDraft;
            set { _pcRandomDraft = value; OnPropertyChanged(nameof(PcRandomDraft)); }
        }

        public int PcScalesDraft
        {
            get => _pcScalesDraft;
            set { _pcScalesDraft = value; OnPropertyChanged(nameof(PcScalesDraft)); }
        }

        public int PcArpeggiosDraft
        {
            get => _pcArpeggiosDraft;
            set { _pcArpeggiosDraft = value; OnPropertyChanged(nameof(PcArpeggiosDraft)); }
        }

        private int _pcTunesDraft;
        private int _pcRandomDraft;
        private int _pcScalesDraft;
        private int _pcArpeggiosDraft;

        public void LoadCompositionPercentsFromSession() => SyncCompositionDraftsFromSession();

        public void CommitCompositionPercent(int changedIndex, int newValue)
        {
            var current = new[]
            {
                _session.PcTunes, _session.PcRandom, _session.PcScales, _session.PcArpeggios
            };
            var redistributed = NoteSessionService.RedistributePracticeComposition(
                current, changedIndex, newValue);
            _session.SetPracticeCompositionPercents(
                redistributed[0], redistributed[1], redistributed[2], redistributed[3]);
            SyncCompositionDraftsFromSession();
        }

        private double _maxSessionDbSizeMb = Preferences.Default.Get("MaxSessionDbSizeMb", 50);
        public double MaxSessionDbSizeMb
        {
            get => _maxSessionDbSizeMb;
            set
            {
                if (Math.Abs(_maxSessionDbSizeMb - value) < 0.001) return;
                _maxSessionDbSizeMb = value;
                Preferences.Default.Set("MaxSessionDbSizeMb", (int)value);
                OnPropertyChanged(nameof(MaxSessionDbSizeMb));
            }
        }

        private double _sessionDbSizeMb;
        public double SessionDbSizeMb
        {
            get => _sessionDbSizeMb;
            private set
            {
                _sessionDbSizeMb = value;
                OnPropertyChanged(nameof(SessionDbSizeMb));
                OnPropertyChanged(nameof(MemoryUsageDisplay));
            }
        }

        private double _availableStorageMb = 500;
        public double AvailableStorageMb
        {
            get => _availableStorageMb;
            private set
            {
                _availableStorageMb = value;
                OnPropertyChanged(nameof(AvailableStorageMb));
                OnPropertyChanged(nameof(MemoryUsageDisplay));
            }
        }

        public string MemoryUsageDisplay =>
            $"Memory usage:   Session Database {SessionDbSizeMb:F4} MB   Available {AvailableStorageMb:N0} MB";

        public void RefreshStorageInfo()
        {
            try
            {
                var sessionDb = ServiceHelper.GetService<SessionDatabase>();
                var dbPath = sessionDb?.DatabasePath ?? string.Empty;

                long totalSize = 0;
                if (File.Exists(dbPath))
                    totalSize += new FileInfo(dbPath).Length;

                var walPath = dbPath + "-wal";
                if (File.Exists(walPath))
                    totalSize += new FileInfo(walPath).Length;

                var shmPath = dbPath + "-shm";
                if (File.Exists(shmPath))
                    totalSize += new FileInfo(shmPath).Length;

                SessionDbSizeMb = totalSize / 1_048_576.0;

                var drive = new DriveInfo(FileSystem.AppDataDirectory);
                var freeMb = drive.AvailableFreeSpace / 1_048_576.0;
                AvailableStorageMb = freeMb;

                if (_maxSessionDbSizeMb > freeMb)
                    MaxSessionDbSizeMb = freeMb;
            }
            catch { }
        }

        private void SyncCompositionDraftsFromSession()
        {
            PcTunesDraft = _session.PcTunes;
            PcRandomDraft = _session.PcRandom;
            PcScalesDraft = _session.PcScales;
            PcArpeggiosDraft = _session.PcArpeggios;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
