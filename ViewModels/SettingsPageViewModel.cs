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
                    var preferC = opts.FirstOrDefault(i => i == "Concert Pitch");
                    if (!string.IsNullOrEmpty(preferC))
                        return preferC;
                    return opts[0];
                }
                return "C";
            }
        }
        public const string DefaultKey = "C";
        public const string DefaultTune = "Major";
        public const string DefaultLowestNote = "C4";
        public const string DefaultHighestNote = "F5";
        public const int DefaultTempo = NoteSessionService.DefaultTempo;
        public const int DefaultMusicBpm = DefaultTempo;
        public const int DefaultPlaybackBpm = DefaultTempo;
        // Aligned with NoteSessionService Preferences default (0) so factory reset
        // and a fresh install produce the same initial accidental percentage.
        public const int DefaultAccidentalPct = 0;
        public const int DefaultCorrectThreshold = MasteryPreferenceDefaults.CorrectThreshold;
        public const int DefaultMinCorrectCount = MasteryPreferenceDefaults.MinCorrectCount;
        public const int DefaultOmitMsAvg = MasteryPreferenceDefaults.OmitMsAvgThreshold;
        public const bool DefaultAutoStart = true;
        public const bool DefaultCollectNote = true;
        public const bool DefaultCollectSession = true;
        public const int DefaultMaxSessionDbMb = 50;

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
            {
                _theme.PropertyChanged += Theme_PropertyChanged;
                _theme.ThemeColorsChanged += (_, _) => RefreshThemeColorBindings();
            }
        }

        /// <summary>
        /// User moved an accidental, rhythm, key, or scale control during a child session.
        /// </summary>
        private void MarkChildPracticeOverrideIfNeeded(bool rhythmModeChanged = false)
        {
            if (_session == null || _session.ChildLevel <= 0)
                return;

            if (rhythmModeChanged)
                _session.RhythmVarietyPercent = -1;

            _session.MarkChildPracticeSettingsCustomized();
        }

        private void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Forward session property changes so UI bound to view-model updates
            switch (e.PropertyName)
            {
                case nameof(NoteSessionService.SelectedScale):
                    OnPropertyChanged(nameof(SelectedScale)); break;
                case nameof(NoteSessionService.LowestNote):
                    OnPropertyChanged(nameof(LowestNote));
                    OnPropertyChanged(nameof(AutomaticNoteRangeDisplay));
                    break;
                case nameof(NoteSessionService.HighestNote):
                    OnPropertyChanged(nameof(HighestNote));
                    OnPropertyChanged(nameof(AutomaticNoteRangeDisplay));
                    break;
                case nameof(NoteSessionService.AccidentalPercent):
                    OnPropertyChanged(nameof(AccidentalPercent)); break;
                case nameof(NoteSessionService.Tempo):
                case nameof(NoteSessionService.PlaybackBpm):
                case nameof(NoteSessionService.MusicBpm):
                    OnPropertyChanged(nameof(Tempo)); break;
                case nameof(NoteSessionService.CorrectThreshold):
                    OnPropertyChanged(nameof(CorrectThreshold));
                    OnPropertyChanged(nameof(OmitSliderValue));
                    break;
                case nameof(NoteSessionService.MinCorrectCount):
                    OnPropertyChanged(nameof(MinCorrectCount)); break;
                case nameof(NoteSessionService.AutoStart):
                    OnPropertyChanged(nameof(AutoStart)); break;
                //case nameof(NoteSessionService.ShowConductorCues):
                //    OnPropertyChanged(nameof(ShowConductorCues)); break;
                case nameof(NoteSessionService.OmitMsAvgThreshold):
                    OnPropertyChanged(nameof(OmitMsAvgThreshold)); break;
                case nameof(NoteSessionService.MasteredMethod):
                    OnPropertyChanged(nameof(MasteredMethod));
                    OnPropertyChanged(nameof(OmitSliderLabel));
                    OnPropertyChanged(nameof(OmitSliderMin));
                    OnPropertyChanged(nameof(OmitSliderMax));
                    OnPropertyChanged(nameof(OmitSliderValue));
                    OnPropertyChanged(nameof(IsPercentCorrectMethod));
                    break;
                case nameof(NoteSessionService.StreakCrit):
                    OnPropertyChanged(nameof(StreakCrit));
                    OnPropertyChanged(nameof(OmitSliderValue));
                    break;
                case nameof(NoteSessionService.MeterTimeSignature):
                    OnPropertyChanged(nameof(MeterTimeSignature)); break;
                case nameof(NoteSessionService.SmallestRhythmNote):
                    OnPropertyChanged(nameof(SmallestRhythmNote)); break;
                case nameof(NoteSessionService.RhythmMode):
                    OnPropertyChanged(nameof(RhythmMode)); break;
                case nameof(NoteSessionService.SyncopationSetting):
                    OnPropertyChanged(nameof(SyncopationSetting)); break;
                case nameof(NoteSessionService.NoteNameDisplay):
                    OnPropertyChanged(nameof(NoteNameDisplay)); break;
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
            RefreshThemeColorBindings();
        }

        public void RefreshThemeColorBindings()
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

        public Color PanelBackgroundColor => _theme?.PanelBackgroundColor ?? _panelBackgroundColor;
        public Color SecondPanelBackgroundColor => _theme?.SecondPanelBackgroundColor ?? Color.FromArgb("#F8F8FF");
        public Color TextColor => _theme?.TextColor ?? Colors.Black;
        public Color HeadingTextColor => _theme?.HeadingTextColor ?? Color.FromArgb("#8B4513");
        public Color SliderColor => _theme?.SliderColor ?? Color.FromArgb("#8B4513");
        public Color ButtonBackgroundColor => _theme?.ButtonBackgroundColor ?? Color.FromArgb("#E7FFCC");
        public Color ButtonTextColor => _theme?.ButtonTextColor ?? Colors.White;
        public Color PickerBackgroundColor => _theme?.PickerBackgroundColor ?? Colors.White;
        public Color PickerTextColor => _theme?.PickerTextColor ?? Colors.Black;
        public Color PickerBorderColor => _theme?.PickerBorderColor ?? Color.FromArgb("#8B4513");
        public Color EntryBackgroundColor => _theme?.EntryBackgroundColor ?? Colors.White;
        public Color EntryTextColor => _theme?.EntryTextColor ?? Colors.Black;
        public Color ContrastingTextColor => _theme?.ContrastingTextColor ?? ThemeColorContrast.GetContrastingTextColor(PanelBackgroundColor);

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
                if ((_session?.SelectedScale ?? _selectedScale) == value || string.IsNullOrWhiteSpace(value)) return;
                if (_session != null)
                {
                    _session.SelectedScale = value;
                    MarkChildPracticeOverrideIfNeeded();
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

        public string AutomaticNoteRangeDisplay => _session?.AutomaticNoteRangeDisplay ?? $"{LowestNote} - {HighestNote}";

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
                        MarkChildPracticeOverrideIfNeeded();
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

        public int MinTempo => NoteSessionService.MinTempo;
        public int MaxTempo => NoteSessionService.MaxTempo;

        private int _tempo = Preferences.Get("musicmate.MusicBpm", DefaultTempo);
        public int Tempo
        {
            get => _session?.Tempo ?? _tempo;
            set
            {
                var clamped = Math.Clamp(value, MinTempo, MaxTempo);
                if ((_session?.Tempo ?? _tempo) == clamped) return;
                if (_session != null)
                {
                    _session.Tempo = clamped;
                    OnPropertyChanged(nameof(Tempo));
                }
                else
                {
                    _tempo = clamped;
                    Preferences.Set("musicmate.MusicBpm", _tempo);
                    Preferences.Set("musicmate.PlaybackBpm", _tempo);
                    OnPropertyChanged(nameof(Tempo));
                }
            }
        }

        private int _correctThreshold = Preferences.Get("musicmate.CorrectThreshold", MasteryPreferenceDefaults.CorrectThreshold);
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
                    OnPropertyChanged(nameof(OmitSliderValue));
                }
                else
                {
                    _correctThreshold = clamped;
                    Preferences.Set("musicmate.CorrectThreshold", _correctThreshold);
                    OnPropertyChanged(nameof(CorrectThreshold));
                    OnPropertyChanged(nameof(OmitSliderValue));
                }
            }
        }

        private int _minCorrectCount = Preferences.Get("musicmate.MinCorrectCount", MasteryPreferenceDefaults.MinCorrectCount);
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

        private int _omitMsAvgThreshold = Preferences.Get("musicmate.OmitMsAvgThreshold", MasteryPreferenceDefaults.OmitMsAvgThreshold);
        public int OmitMsAvgThreshold
        {
            get => _session?.OmitMsAvgThreshold ?? _omitMsAvgThreshold;
            set
            {
                var clamped = Math.Clamp(value, 0, 5000);
                if ((_session?.OmitMsAvgThreshold ?? _omitMsAvgThreshold) == clamped) return;
                if (_session != null)
                {
                    _session.OmitMsAvgThreshold = clamped;
                    OnPropertyChanged(nameof(OmitMsAvgThreshold));
                }
                else
                {
                    _omitMsAvgThreshold = clamped;
                    Preferences.Set("musicmate.OmitMsAvgThreshold", clamped);
                    OnPropertyChanged(nameof(OmitMsAvgThreshold));
                }
            }
        }

        public List<string> MasteredMethodOptions { get; } = new() { "% Correct", "Streak" };

        private string _masteredMethod = Preferences.Get("musicmate.MasteredMethod", MasteryPreferenceDefaults.MasteredMethod);
        public string MasteredMethod
        {
            get => _session?.MasteredMethod ?? _masteredMethod;
            set
            {
                if ((_session?.MasteredMethod ?? _masteredMethod) == value) return;
                if (_session != null)
                {
                    _session.MasteredMethod = value;
                    OnPropertyChanged(nameof(MasteredMethod));
                }
                else
                {
                    _masteredMethod = value;
                    Preferences.Set("musicmate.MasteredMethod", value);
                    OnPropertyChanged(nameof(MasteredMethod));
                }
                OnPropertyChanged(nameof(OmitSliderLabel));
                OnPropertyChanged(nameof(OmitSliderMin));
                OnPropertyChanged(nameof(OmitSliderMax));
                OnPropertyChanged(nameof(OmitSliderValue));
                OnPropertyChanged(nameof(IsPercentCorrectMethod));
            }
        }

        public bool IsPercentCorrectMethod => MasteredMethod != "Streak";

        public string OmitSliderLabel => MasteredMethod == "Streak" ? "Streak" : "Omit ≥ % correct";
        public double OmitSliderMin => MasteredMethod == "Streak" ? 1 : 0;
        public double OmitSliderMax => MasteredMethod == "Streak" ? 50 : 100;
        public double OmitSliderValue
        {
            get => MasteredMethod == "Streak" ? StreakCrit : CorrectThreshold;
            set
            {
                if (MasteredMethod == "Streak")
                    StreakCrit = (int)value;
                else
                    CorrectThreshold = (int)value;
            }
        }

        private int _streakCrit = Preferences.Get("musicmate.StreakCrit", MasteryPreferenceDefaults.StreakCrit);
        public int StreakCrit
        {
            get => _session?.StreakCrit ?? _streakCrit;
            set
            {
                var clamped = Math.Clamp(value, 1, 50);
                if ((_session?.StreakCrit ?? _streakCrit) == clamped) return;
                if (_session != null)
                {
                    _session.StreakCrit = clamped;
                    OnPropertyChanged(nameof(StreakCrit));
                }
                else
                {
                    _streakCrit = clamped;
                    Preferences.Set("musicmate.StreakCrit", clamped);
                    OnPropertyChanged(nameof(StreakCrit));
                }
                OnPropertyChanged(nameof(OmitSliderValue));
            }
        }

        // ── Rhythm Settings ─────────────────────────────────────────────────────

        public List<string> MeterTimeSignatureOptions { get; } = new() { "4/4", "3/4", "2/4" };
        public List<string> SmallestRhythmNoteOptions { get; } = new() { "Quarter", "Eighth", "Sixteenth" };
        public List<string> RhythmModeOptions { get; } = new() { "Simple", "Mixed" };
        public List<string> SyncopationSettingOptions { get; } = new() { "None", "Simple", "Full" };
        public List<string> NoteNameDisplayOptions { get; } = new() { "Current only", "All notes", "Off" };

        private string _meterTimeSignature = Preferences.Get("musicmate.TimeSignature", "4/4");
        public string MeterTimeSignature
        {
            get => _session?.MeterTimeSignature ?? _meterTimeSignature;
            set
            {
                if ((_session?.MeterTimeSignature ?? _meterTimeSignature) == value) return;
                if (_session != null)
                {
                    _session.MeterTimeSignature = value;
                    MarkChildPracticeOverrideIfNeeded();
                    OnPropertyChanged(nameof(MeterTimeSignature));
                }
                else
                {
                    _meterTimeSignature = value;
                    Preferences.Set("musicmate.TimeSignature", value);
                    OnPropertyChanged(nameof(MeterTimeSignature));
                }
            }
        }

        private string _smallestRhythmNote = Preferences.Get("musicmate.SmallestNote", "Quarter");
        public string SmallestRhythmNote
        {
            get => _session?.SmallestRhythmNote ?? _smallestRhythmNote;
            set
            {
                if ((_session?.SmallestRhythmNote ?? _smallestRhythmNote) == value) return;
                if (_session != null)
                {
                    _session.SmallestRhythmNote = value;
                    MarkChildPracticeOverrideIfNeeded();
                    OnPropertyChanged(nameof(SmallestRhythmNote));
                }
                else
                {
                    _smallestRhythmNote = value;
                    Preferences.Set("musicmate.SmallestNote", value);
                    OnPropertyChanged(nameof(SmallestRhythmNote));
                }
            }
        }

        private string _rhythmMode = Preferences.Get("musicmate.RhythmMode", "Simple");
        public string RhythmMode
        {
            get => _session?.RhythmMode ?? _rhythmMode;
            set
            {
                if ((_session?.RhythmMode ?? _rhythmMode) == value) return;
                if (_session != null)
                {
                    _session.RhythmMode = value;
                    MarkChildPracticeOverrideIfNeeded(rhythmModeChanged: true);
                    OnPropertyChanged(nameof(RhythmMode));
                }
                else
                {
                    _rhythmMode = value;
                    Preferences.Set("musicmate.RhythmMode", value);
                    OnPropertyChanged(nameof(RhythmMode));
                }
            }
        }

        private string _syncopationSetting = Preferences.Get("musicmate.Syncopation", "None");
        public string SyncopationSetting
        {
            get => _session?.SyncopationSetting ?? _syncopationSetting;
            set
            {
                if ((_session?.SyncopationSetting ?? _syncopationSetting) == value) return;
                if (_session != null)
                {
                    _session.SyncopationSetting = value;
                    MarkChildPracticeOverrideIfNeeded();
                    OnPropertyChanged(nameof(SyncopationSetting));
                }
                else
                {
                    _syncopationSetting = value;
                    Preferences.Set("musicmate.Syncopation", value);
                    OnPropertyChanged(nameof(SyncopationSetting));
                }
            }
        }

        private string _noteNameDisplay = Preferences.Get("musicmate.NoteNameDisplay", "Current only");
        private bool _showConductorCues = Preferences.Get("musicmate.ShowConductorCues", false);
        public string NoteNameDisplay
        {
            get => _session?.NoteNameDisplay ?? _noteNameDisplay;
            set
            {
                if ((_session?.NoteNameDisplay ?? _noteNameDisplay) == value) return;
                if (_session != null)
                {
                    _session.NoteNameDisplay = value;
                    MarkChildPracticeOverrideIfNeeded();
                    OnPropertyChanged(nameof(NoteNameDisplay));
                }
                else
                {
                    _noteNameDisplay = value;
                    Preferences.Set("musicmate.NoteNameDisplay", value);
                    OnPropertyChanged(nameof(NoteNameDisplay));
                }
            }
        }

        public bool ShowConductorCues
        {
            get => _session?.ShowConductorCues ?? _showConductorCues;
            set
            {
                if ((_session?.ShowConductorCues ?? _showConductorCues) == value) return;
                if (_session != null)
                {
                    _session.ShowConductorCues = value;
                    OnPropertyChanged(nameof(ShowConductorCues));
                }
                else
                {
                    _showConductorCues = value;
                    Preferences.Set("musicmate.ShowConductorCues", value);
                    OnPropertyChanged(nameof(ShowConductorCues));
                }
            }
        }

        // ── Statistics Collection ─────────────────────────────────────────────
        const string KeyCollectNote = "CollectNoteStats";
        const string KeyCollectSession = "CollectSessionStats";

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


        // ── Rolling per-note attempt history limit ────────────────────────────
        // Persisted as "MaxAttemptsPerNote" in app preferences.
        // Default = 100.  Grouping key = WrittenNoteName + Instrument.
        private int _maxAttemptsPerNote = Preferences.Default.Get("MaxAttemptsPerNote", 100);
        public int MaxAttemptsPerNote
        {
            get => _maxAttemptsPerNote;
            set
            {
                var clamped = Math.Clamp(value, 0, 1000);
                if (_maxAttemptsPerNote == clamped) return;
                _maxAttemptsPerNote = clamped;
                Preferences.Default.Set("MaxAttemptsPerNote", clamped);
                CollectNoteStats = clamped > 0;
                OnPropertyChanged(nameof(MaxAttemptsPerNote));
            }
        }

        /// <summary>Resets all settings to their factory defaults.</summary>
        public void ResetToDefaults()
        {
            var resetService = ServiceHelper.GetService<SettingsResetService>();
            if (resetService != null)
            {
                resetService.ResetToFactoryDefaults();
                return;
            }

            // Fallback when service locator is unavailable (e.g. design-time).
            Tempo = DefaultTempo;
            AccidentalPercent = DefaultAccidentalPct;
            CorrectThreshold = DefaultCorrectThreshold;
            MinCorrectCount = DefaultMinCorrectCount;
            OmitMsAvgThreshold = DefaultOmitMsAvg;
            AutoStart = DefaultAutoStart;
            MasteredMethod = "% Correct";
            StreakCrit = MasteryPreferenceDefaults.StreakCrit;
            CollectNoteStats = DefaultCollectNote;
            CollectSessionStats = DefaultCollectSession;
            MaxAttemptsPerNote = 100;
            SelectedScale = DefaultTune;  // DefaultTune = "Major" is correct for SelectedScale
            if (_session != null)
            {
                _session.Instrument = DefaultInstrument;
                _session.Key = DefaultKey;
                // [ResetOptionsTest] Tune must be a mode name ("Selected Scale"), not a scale name.
                _session.Tune = "Selected Scale";
                _session.ApplyAutomaticInstrumentRange(fullReset: true);
                _session.ResetAdvancedDetectionDefaults();
                _session.ResetPracticeCompositionDefaults();
            }
            LevelUpService.ResetCriteriaToDefaults();
        }

        // Helper for MIDI to note name
        private static string MidiToNoteName(int midi, bool preferSharps)
        {
            string[] namesSharps = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            string[] namesFlats = { "C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B" };
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
