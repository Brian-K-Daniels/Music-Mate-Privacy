using System.ComponentModel;
using Microsoft.Maui.Graphics;
using System.Collections.ObjectModel;
using Microsoft.Maui.Storage;
using System.Linq;
using musicmate.Services;

namespace musicmate.ViewModels
{
    public class SettingsPageViewModel : INotifyPropertyChanged
    {
        // ── Factory defaults ──────────────────────────────────────────────────
        public static string DefaultInstrument
        {
            get
            {
                var opts = NoteSessionService.InstrumentOptions;
                if (opts != null && opts.Length > 0)
                {
                    // Prefer an instrument explicitly named "C" when available, otherwise use the first option.
                    var preferC = opts.FirstOrDefault(i => i == "C");
                    if (!string.IsNullOrEmpty(preferC))
                        return preferC;
                    return opts[0];
                }
                return "C";
            }
        }
        public const string DefaultKey               = "C";
        public const string DefaultTune             = "Major";
        public const string DefaultLowestNote       = "C4";
        public const string DefaultHighestNote      = "F5";
        public const int    DefaultPlaybackBpm      = 100;
        public const int    DefaultAccidentalPct    = 30;
        public const int    DefaultCorrectThreshold = 95;
        public const int    DefaultMinCorrectCount  = 6;
        public const int    DefaultOmitMsAvg        = 400;
        public const bool   DefaultAutoStart        = false;
        public const bool   DefaultCollectNote      = true;
        public const bool   DefaultCollectSession   = true;
        public const int    DefaultMaxSessionDbMb   = 50;

        private Color _panelBackgroundColor = Color.FromArgb(Preferences.Get("musicmate.PanelBackgroundColor", Colors.White.ToHex()));
        private readonly NoteSessionService? _session;
        private readonly ThemeService? _theme;

        public SettingsPageViewModel()
        {
            _session = ServiceHelper.GetService<NoteSessionService>();
            _theme = ServiceHelper.GetService<ThemeService>();
            if (_session != null)
                _session.PropertyChanged += Session_PropertyChanged;
            if (_theme != null)
                _theme.PropertyChanged += Theme_PropertyChanged;
        }

        private void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Forward session property changes so UI bound to view-model updates
            switch (e.PropertyName)
            {
                case nameof(NoteSessionService.SelectedScale):
                    OnPropertyChanged(nameof(SelectedScale)); break;
                case nameof(NoteSessionService.LowestNote):
                    OnPropertyChanged(nameof(LowestNote)); break;
                case nameof(NoteSessionService.HighestNote):
                    OnPropertyChanged(nameof(HighestNote)); break;
                case nameof(NoteSessionService.AccidentalPercent):
                    OnPropertyChanged(nameof(AccidentalPercent)); break;
                case nameof(NoteSessionService.PlaybackBpm):
                    OnPropertyChanged(nameof(PlaybackBpm)); break;
                case nameof(NoteSessionService.CorrectThreshold):
                    OnPropertyChanged(nameof(CorrectThreshold)); break;
                case nameof(NoteSessionService.MinCorrectCount):
                    OnPropertyChanged(nameof(MinCorrectCount)); break;
                case nameof(NoteSessionService.AutoStart):
                    OnPropertyChanged(nameof(AutoStart)); break;
                case nameof(NoteSessionService.OmitMsAvgThreshold):
                    OnPropertyChanged(nameof(OmitMsAvgThreshold)); break;
                case nameof(NoteSessionService.WhiteKeyNoteNames):
                    OnPropertyChanged(nameof(WhiteKeyNoteNames)); break;
                case nameof(NoteSessionService.AvailableScalesForBinding):
                    OnPropertyChanged(nameof(AvailableScalesForBinding)); break;
                default:
                    break;
            }
        }

        private void Theme_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ThemeService.PanelBackgroundColor))
            {
                OnPropertyChanged(nameof(PanelBackgroundColor));
                OnPropertyChanged(nameof(ContrastingTextColor));
            }
        }

        public Color PanelBackgroundColor
        {
            get => _theme?.PanelBackgroundColor ?? _panelBackgroundColor;
            set
            {
                if ((_theme?.PanelBackgroundColor ?? _panelBackgroundColor) != value)
                {
                    if (_theme != null)
                    {
                        _theme.PanelBackgroundColor = value;
                    }
                    else
                    {
                        _panelBackgroundColor = value;
                        Preferences.Set("musicmate.PanelBackgroundColor", value.ToHex());
                        OnPropertyChanged(nameof(PanelBackgroundColor));
                        OnPropertyChanged(nameof(ContrastingTextColor));
                    }
                }
            }
        }
        public Color ContrastingTextColor
        {
            get
            {
                double luminance = 0.299 * PanelBackgroundColor.Red + 0.587 * PanelBackgroundColor.Green + 0.114 * PanelBackgroundColor.Blue;
                return luminance > 0.5 ? Colors.Black : Colors.White;
            }
        }

        // Picker and slider properties
        public string[] AvailableScalesForBinding => _session?.AvailableScalesForBinding ?? new[]
        {
            "Major",  "Harmonic Minor", "Melodic Minor", "Natural Minor", "Dorian", "Phrygian",
            "Lydian", "Mixolydian", "Locrian", "Major Pentatonic", "Minor Pentatonic", "Blues"
        };
        public string[] WhiteKeyNoteNames => (_session?.WhiteKeyNoteNames ??
            Enumerable.Range(21, 88)
                .Select(midi => MidiToNoteName(midi, false))
                .Where(name => !name.Contains('#') && !name.Contains('b'))
                .ToArray()).Reverse().ToArray();

        private string _selectedScale = Preferences.Get("musicmate.SelectedScale", "Major");
        public string SelectedScale
        {
            get => _session?.SelectedScale ?? _selectedScale;
            set
            {
                if (( _session?.SelectedScale ?? _selectedScale) == value || string.IsNullOrWhiteSpace(value)) return;
                if (_session != null)
                {
                    _session.SelectedScale = value;
                    OnPropertyChanged(nameof(SelectedScale));
                }
                else
                {
                    _selectedScale = value;
                    Preferences.Set("musicmate.SelectedScale", _selectedScale);
                    OnPropertyChanged(nameof(SelectedScale));
                }
            }
        }

        private string _lowestNote = Preferences.Get("musicmate.LowestNote", "A0");
        public string LowestNote
        {
            get => _session?.LowestNote ?? _lowestNote;
            set
            {
                if ((_session?.LowestNote ?? _lowestNote) != value)
                {
                    if (_session != null)
                    {
                        _session.LowestNote = value;
                        OnPropertyChanged(nameof(LowestNote));
                    }
                    else
                    {
                        _lowestNote = value;
                        Preferences.Set("musicmate.LowestNote", _lowestNote);
                        OnPropertyChanged(nameof(LowestNote));
                    }
                }
            }
        }

        private string _highestNote = Preferences.Get("musicmate.HighestNote", "C8");
        public string HighestNote
        {
            get => _session?.HighestNote ?? _highestNote;
            set
            {
                if ((_session?.HighestNote ?? _highestNote) != value)
                {
                    if (_session != null)
                    {
                        _session.HighestNote = value;
                        OnPropertyChanged(nameof(HighestNote));
                    }
                    else
                    {
                        _highestNote = value;
                        Preferences.Set("musicmate.HighestNote", _highestNote);
                        OnPropertyChanged(nameof(HighestNote));
                    }
                }
            }
        }

        private int _accidentalPercent = Preferences.Get("musicmate.AccidentalPercent", 0);
        public int AccidentalPercent
        {
            get => _session?.AccidentalPercent ?? _accidentalPercent;
            set
            {
                if ((_session?.AccidentalPercent ?? _accidentalPercent) != value)
                {
                    if (_session != null)
                    {
                        _session.AccidentalPercent = value;
                        OnPropertyChanged(nameof(AccidentalPercent));
                    }
                    else
                    {
                        _accidentalPercent = value;
                        Preferences.Set("musicmate.AccidentalPercent", value);
                        OnPropertyChanged(nameof(AccidentalPercent));
                    }
                }
            }
        }

        private int _playbackBpm = Preferences.Get("musicmate.PlaybackBpm", 100);
        public int PlaybackBpm
        {
            get => _session?.PlaybackBpm ?? _playbackBpm;
            set
            {
                var clamped = Math.Clamp(value, 30, 400);
                if ((_session?.PlaybackBpm ?? _playbackBpm) == clamped) return;
                if (_session != null
                )
                {
                    _session.PlaybackBpm = clamped;
                    OnPropertyChanged(nameof(PlaybackBpm));
                }
                else
                {
                    _playbackBpm = clamped;
                    Preferences.Set("musicmate.PlaybackBpm", _playbackBpm);
                    OnPropertyChanged(nameof(PlaybackBpm));
                }
            }
        }

        private int _correctThreshold = Preferences.Get("musicmate.CorrectThreshold", 0);
        public int CorrectThreshold
        {
            get => _session?.CorrectThreshold ?? _correctThreshold;
            set
            {
                var clamped = Math.Clamp(value, 0, 100);
                if ((_session?.CorrectThreshold ?? _correctThreshold) == clamped) return;
                if (_session != null)
                {
                    _session.CorrectThreshold = clamped;
                    OnPropertyChanged(nameof(CorrectThreshold));
                }
                else
                {
                    _correctThreshold = clamped;
                    Preferences.Set("musicmate.CorrectThreshold", _correctThreshold);
                    OnPropertyChanged(nameof(CorrectThreshold));
                }
            }
        }

        private int _minCorrectCount = Preferences.Get("musicmate.MinCorrectCount", 3);
        public int MinCorrectCount
        {
            get => _session?.MinCorrectCount ?? _minCorrectCount;
            set
            {
                var clamped = Math.Clamp(value, 1, 20);
                if ((_session?.MinCorrectCount ?? _minCorrectCount) == clamped) return;
                if (_session != null)
                {
                    _session.MinCorrectCount = clamped;
                    OnPropertyChanged(nameof(MinCorrectCount));
                }
                else
                {
                    _minCorrectCount = clamped;
                    Preferences.Set("musicmate.MinCorrectCount", clamped);
                    OnPropertyChanged(nameof(MinCorrectCount));
                }
            }
        }

        private bool _autoStart = Preferences.Get("musicmate.AutoStart", false);
        public bool AutoStart
        {
            get => _session?.AutoStart ?? _autoStart;
            set
            {
                if ((_session?.AutoStart ?? _autoStart) == value) return;
                if (_session != null)
                {
                    _session.AutoStart = value;
                    OnPropertyChanged(nameof(AutoStart));
                }
                else
                {
                    _autoStart = value;
                    Preferences.Set("musicmate.AutoStart", value);
                    OnPropertyChanged(nameof(AutoStart));
                }
            }
        }

        private int _omitMsAvgThreshold = Preferences.Get("musicmate.OmitMsAvgThreshold", 0);
        public int OmitMsAvgThreshold
        {
            get => _omitMsAvgThreshold;
            set
            {
                var clamped = Math.Clamp(value, 0, 5000);
                if (_omitMsAvgThreshold == clamped) return;
                _omitMsAvgThreshold = clamped;
                Preferences.Set("musicmate.OmitMsAvgThreshold", clamped);
                OnPropertyChanged(nameof(OmitMsAvgThreshold));
            }
        }

        // ── Statistics Collection ─────────────────────────────────────────────
        const string KeyCollectNote    = "CollectNoteStats";
        const string KeyCollectSession = "CollectSessionStats";
        const string KeyMaxSessionDbMb = "MaxSessionDbSizeMb";

        private bool _collectNoteStats = Preferences.Default.Get("CollectNoteStats", true);
        public bool CollectNoteStats
        {
            get => _collectNoteStats;
            set
            {
                if (_collectNoteStats == value) return;
                _collectNoteStats = value;
                Preferences.Default.Set(KeyCollectNote, value);
                OnPropertyChanged(nameof(CollectNoteStats));
            }
        }

        private bool _collectSessionStats = Preferences.Default.Get("CollectSessionStats", true);
        public bool CollectSessionStats
        {
            get => _collectSessionStats;
            set
            {
                if (_collectSessionStats == value) return;
                _collectSessionStats = value;
                Preferences.Default.Set(KeyCollectSession, value);
                OnPropertyChanged(nameof(CollectSessionStats));
            }
        }


        private double _maxSessionDbSizeMb = Preferences.Default.Get("MaxSessionDbSizeMb", 50);
        public double MaxSessionDbSizeMb
        {
            get => _maxSessionDbSizeMb;
            set
            {
                if (_maxSessionDbSizeMb == value) return;
                _maxSessionDbSizeMb = value;
                Preferences.Default.Set(KeyMaxSessionDbMb, (int)value);
                OnPropertyChanged(nameof(MaxSessionDbSizeMb));
                OnPropertyChanged(nameof(MaxSessionDbSizeDisplay));
            }
        }
        public string MaxSessionDbSizeDisplay => $"Max Session DB size: {(int)MaxSessionDbSizeMb} MB";

        // ── Storage info (read-only, refreshed on page appear) ────────────────
        private double _sessionDbSizeMb;
        public double SessionDbSizeMb
        {
            get => _sessionDbSizeMb;
            private set { _sessionDbSizeMb = value; OnPropertyChanged(nameof(SessionDbSizeMb)); OnPropertyChanged(nameof(MemoryUsageDisplay)); }
        }

        private double _availableStorageMb = 500;
        public double AvailableStorageMb
        {
            get => _availableStorageMb;
            private set { _availableStorageMb = value; OnPropertyChanged(nameof(AvailableStorageMb)); OnPropertyChanged(nameof(MemoryUsageDisplay)); }
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

                // Main database file
                if (File.Exists(dbPath))
                    totalSize += new System.IO.FileInfo(dbPath).Length;

                // Include WAL file (Write-Ahead Log) for SQLite databases
                var walPath = dbPath + "-wal";
                if (File.Exists(walPath))
                    totalSize += new System.IO.FileInfo(walPath).Length;

                // Include SHM file (Shared Memory) for SQLite databases
                var shmPath = dbPath + "-shm";
                if (File.Exists(shmPath))
                    totalSize += new System.IO.FileInfo(shmPath).Length;

                SessionDbSizeMb = totalSize / 1_048_576.0;

                var drive = new System.IO.DriveInfo(FileSystem.AppDataDirectory);
                var freeMb = drive.AvailableFreeSpace / 1_048_576.0;
                AvailableStorageMb = freeMb;

                // Clamp the slider value so it doesn't exceed the new maximum
                if (_maxSessionDbSizeMb > freeMb)
                    MaxSessionDbSizeMb = freeMb;
            }
            catch { }
        }

        /// <summary>Resets all settings to their factory defaults.</summary>
        public void ResetToDefaults()
        {
            LowestNote          = DefaultLowestNote;
            HighestNote         = DefaultHighestNote;
            PlaybackBpm         = DefaultPlaybackBpm;
            AccidentalPercent   = DefaultAccidentalPct;
            CorrectThreshold    = DefaultCorrectThreshold;
            MinCorrectCount     = DefaultMinCorrectCount;
            OmitMsAvgThreshold  = DefaultOmitMsAvg;
            AutoStart           = DefaultAutoStart;
            CollectNoteStats    = DefaultCollectNote;
            CollectSessionStats = DefaultCollectSession;
            MaxSessionDbSizeMb  = DefaultMaxSessionDbMb;
            // Musical defaults: reset scale/tune, instrument and key via session
            SelectedScale = DefaultTune;
            if (_session != null)
            {
                _session.Instrument = DefaultInstrument;
                _session.Key = DefaultKey;
                _session.Tune = DefaultTune;
            }
            else
            {
                Preferences.Set("musicmate.Instrument", DefaultInstrument);
                Preferences.Set("musicmate.Key", DefaultKey);
                Preferences.Set("musicmate.Tune", DefaultTune);
            }
        }

        // Helper for MIDI to note name
        private static string MidiToNoteName(int midi, bool preferSharps)
        {
            string[] namesSharps = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            string[] namesFlats  = { "C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B" };
            int octave = (midi / 12) - 1;
            int pc = midi % 12;
            string name = preferSharps ? namesSharps[pc] : namesFlats[pc];
            return $"{name}{octave}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
