using Microsoft.Maui.Storage;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using musicmate.Diagnostics;
using musicmate.Models;
using musicmate.Utilities;

namespace musicmate.Services
{
    public record FeedbackItem(int Index, int WrongAttempts, int CentsDeviation, bool IsCorrect)
    {
        public string CentsText => $"{CentsDeviation:+0;-0;0}";
        public Color BoxColor => IsCorrect ? Colors.Green : Colors.DarkRed;
    }

    // Replace the NoteInfo class in Services\NoteSessionService.cs with this (remove PlaybackHighlightIndex from NoteInfo)
    public class NoteInfo
    {
        public int Midi { get; set; }
        public string Name { get; set; } = "";
        public double TargetFreq { get; set; }
        public float X { get; set; }
        /// <summary>When true this slot is a rest — it is skipped during capture evaluation and drawn as a rest symbol.</summary>
        public bool IsRest { get; set; }
        /// <summary>Rhythmic duration for practice-tune notes. Null in random/scale/tuner modes.</summary>
        public NoteDuration? Duration { get; set; }
        /// <summary>Absolute beat where this pitched note begins (rhythm gate).</summary>
        public double StartBeat { get; set; }
        /// <summary>Written duration in beats (rhythm gate).</summary>
        public double DurationBeats { get; set; }
        /// <summary>
        /// Beats from previous pitched note's start to this note's start
        /// (= prior note duration + intervening rests). Zero for the first pitched note.
        /// </summary>
        public double GateBeatsAfterPrevious { get; set; }
        // Returns all enharmonic names for this note (including itself)
        public IEnumerable<string> EnharmonicNames
        {
            get
            {
                var midis = musicmate.Services.NoteSessionService.GetEnharmonicMidis(Midi);
                foreach (var midi in midis)
                {
                    yield return musicmate.Services.NoteSessionService.MidiToNoteName(midi, false);
                    yield return musicmate.Services.NoteSessionService.MidiToNoteName(midi, true);
                }
            }
        }
    }

    /// <summary>Per-note session aggregates flushed to <see cref="NoteStat"/> at session end.</summary>
    public struct SessionNoteAggregate
    {
        public int PitchCorrect;
        public int PitchWrong;
        public int TimingCorrect;
        public int TimingWrong;
        public int OverallCorrect;
        public int OverallWrong;
        public double TotalMs;
        public int MsCount;
    }

    public partial class NoteSessionService : INotifyPropertyChanged
    {
        public bool IsDirty { get; set; } = true;  //  2026.07.09 1104  

        private static readonly HashSet<string> FreeScales = new() { "Major", "Harmonic Minor" };
        private static readonly HashSet<string> FreeKeys = new() { "C", "F", "Bb", "G", "D" };
        public NoteSessionService()
        {
            Instrument = _instrument;
            if (!NoteRangeCustomized)
                ApplyAutomaticInstrumentRange(fullReset: true);
            else
                NotifyNoteRangeDerivedPropertiesChanged();

            StatusService.Instance.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(StatusService.IsPremiumUser) && !StatusService.Instance.IsPremiumUser)
                    RevertToFreeDefaults();
            };
        }
        private void RevertToFreeDefaults()
        {
            if (!FreeScales.Contains(SelectedScale))
                SelectedScale = "Major";
            if (!FreeKeys.Contains(Key))
                Key = "C";

            if (!NoteRangeCustomized)
                ApplyAutomaticInstrumentRange(fullReset: true);
            else
                NotifyNoteRangeDerivedPropertiesChanged();
        }
        public int SampleRate { get; set; } = 44100;        public int BufferSize { get; set; } = 4096;
        // Add this property to NoteSessionService (near other public properties)  //  2026.04.07 1216  
        private int? _playbackHighlightIndex = null;
        public int? PlaybackHighlightIndex
        {
            get => _playbackHighlightIndex;
            set
            {
                if (_playbackHighlightIndex != value)
                {
                    _playbackHighlightIndex = value;
                    OnPropertyChanged(nameof(PlaybackHighlightIndex));
                }
            }
        }

        // ── Rhythm / staff generation settings ─────────────────────────────────
        private const string PrefMeterTimeSignatureKey = "musicmate.TimeSignature";
        private const string PrefSmallestRhythmNoteKey = "musicmate.SmallestNote";
        private const string PrefRhythmModeKey = "musicmate.RhythmMode";
        private const string PrefSyncopationSettingKey = "musicmate.Syncopation";
        private const string PrefNoteNameDisplayKey = "musicmate.NoteNameDisplay";
        private const string PrefShowConductorCuesKey = "musicmate.ShowConductorCues";
        private string _meterTimeSignature = SessionPreferences.Get(PrefMeterTimeSignatureKey, "4/4");
        private string _smallestRhythmNote = SessionPreferences.Get(PrefSmallestRhythmNoteKey, "Quarter");
        private string _rhythmMode = SessionPreferences.Get(PrefRhythmModeKey, "Simple");
        private string _syncopationSetting = SessionPreferences.Get(PrefSyncopationSettingKey, "None");
        private string _noteNameDisplay = SessionPreferences.Get(PrefNoteNameDisplayKey, "Current only");
        private bool _showConductorCues = SessionPreferences.Get(PrefShowConductorCuesKey, false);

        /// <summary>
        /// Time signature for rhythm generation.
        /// Persisted value is the display string: "4/4", "3/4", or "2/4".
        /// </summary>
        public string MeterTimeSignature
        {
            get => _meterTimeSignature;
            set
            {
                if (_meterTimeSignature == value) return;
                _meterTimeSignature = value;
                SessionPreferences.Set(PrefMeterTimeSignatureKey, value);
                OnPropertyChanged(nameof(MeterTimeSignature));
            }
        }
        /// <summary>
        /// Time signature drawn on the staff.  Built-in practice tunes use their own
        /// meter; generated sequences use <see cref="MeterTimeSignature"/>.
        /// </summary>
        public string GetDisplayTimeSignature()
        {
            if (Tune == "Practice Tune" && CurrentTune != null)
                return CurrentTune.TimeSignature.ToString();
            return MeterTimeSignature ?? "4/4";
        }
        /// <summary>Quarter-note beats per measure for layout validation.</summary>
        public double GetDisplayMeasureBeats()
        {
            if (Tune == "Practice Tune" && CurrentTune != null)
                return CurrentTune.TimeSignature.TotalBeats;

            var parts = (MeterTimeSignature ?? "4/4").Split('/');
            return parts.Length == 2 && int.TryParse(parts[0], out int beats) ? beats : 4.0;
        }
        /// <summary>
        /// Smallest note value allowed in rhythm generation.
        /// Persisted value is the display string: "Quarter", "Eighth", or "Sixteenth".
        /// </summary>
        public string SmallestRhythmNote
        {
            get => _smallestRhythmNote;
            set
            {
                if (_smallestRhythmNote == value) return;
                _smallestRhythmNote = value;
                SessionPreferences.Set(PrefSmallestRhythmNoteKey, value);
                OnPropertyChanged(nameof(SmallestRhythmNote));
            }
        }
        /// <summary>
        /// Rhythm variety mode for generation.
        /// "Simple" uses only quarter notes (and half/whole occasionally).
        /// "Mixed" allows the full range of durations up to <see cref="SmallestRhythmNote"/>.
        /// </summary>
        public string RhythmMode
        {
            get => _rhythmMode;
            set
            {
                if (_rhythmMode == value) return;
                _rhythmMode = value;
                SessionPreferences.Set(PrefRhythmModeKey, value);
                OnPropertyChanged(nameof(RhythmMode));
            }
        }
        /// <summary>
        /// Syncopation level for rhythm generation.
        /// "None" = on-beat sequential fill; "Simple" = mild off-beat accents;
        /// "Full" = stronger syncopated motifs.
        /// </summary>
        public string SyncopationSetting
        {
            get => _syncopationSetting;
            set
            {
                if (_syncopationSetting == value) return;
                _syncopationSetting = value;
                SessionPreferences.Set(PrefSyncopationSettingKey, value);
                OnPropertyChanged(nameof(SyncopationSetting));
            }
        }
        /// <summary>
        /// Controls when note names are shown above/below noteheads on the staff.
        /// Values: "Current only", "All notes", "Off".
        /// </summary>
        public string NoteNameDisplay
        {
            get => _noteNameDisplay;
            set
            {
                if (_noteNameDisplay == value) return;
                _noteNameDisplay = value;
                SessionPreferences.Set(PrefNoteNameDisplayKey, value);
                OnPropertyChanged(nameof(NoteNameDisplay));
            }
        }

        /// <summary>When true, red conductor arrows mark conducted beat starts above the staff.</summary>
        public bool ShowConductorCues
        {
            get => _showConductorCues;
            set
            {
                if (_showConductorCues == value) return;
                _showConductorCues = value;
                SessionPreferences.Set(PrefShowConductorCuesKey, value);
                OnPropertyChanged(nameof(ShowConductorCues));
            }
        }
        public int AccidentalPercent
        {
            get => _accidentalPercent;
            set
            {
                if (_accidentalPercent != value)
                {
                    _accidentalPercent = value;
                    SessionPreferences.Set(PrefAccidentalPercentKey, value);
                    OnPropertyChanged(nameof(AccidentalPercent));
                }
            }
        }
        /// <summary>
        /// Maximum melodic interval in semitones allowed between consecutive notes.
        /// Set by <see cref="DifficultyLevelMapper.ApplyToSession"/> to enforce
        /// level-based interval limits in random-mode generation.
        /// 0 = no limit (open melodic range).
        /// </summary>
        private int _maxMelodicIntervalSemitones = 0;
        public int MaxMelodicIntervalSemitones
        {
            get => _maxMelodicIntervalSemitones;
            set
            {
                if (_maxMelodicIntervalSemitones != value)
                {
                    _maxMelodicIntervalSemitones = value;
                    OnPropertyChanged(nameof(MaxMelodicIntervalSemitones));
                }
            }
        }

        /// <summary>Child-Practice measure batch size; 0 = use MusicPage default.</summary>
        public int ChildMeasureBatchSize { get; set; }
        /// <summary>Explicit rhythm variety (0–100); -1 = derive from <see cref="RhythmMode"/>.</summary>
        public int RhythmVarietyPercent { get; set; } = -1;
        /// <summary>Per-slot rest chance (0–100); -1 = legacy rest logic in generator.</summary>
        public int PracticeRestChancePercent { get; set; } = -1;
        public string[] WhiteKeyNoteNames { get; } =
            Enumerable.Range(21, 88) // MIDI 21 (A0) to 108 (C8)
                .Select(midi => MidiToNoteName(midi, false))
                .Where(name => !name.Contains('#') && !name.Contains('b'))
                .ToArray();
        public event Func<Task>? SessionCompletedAsync;
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private const string PrefInstrumentKey = "musicmate.Instrument";
        private const string PrefKeySignatureKey = "musicmate.Key";
        private const string PrefSelectedScaleKey = "musicmate.SelectedScale";
        private const string PrefScaleSelectionModeKey = "musicmate.ScaleSelectionMode";
        private const string PrefTuneKey = "musicmate.Tune";
        private const string PrefSelectedArpeggioIdKey = "musicmate.SelectedArpeggioId";
        private const string PrefSelectedArpeggioRootKey = "musicmate.SelectedArpeggioRoot";
        private const string PrefSelectedArpeggioDisplayKey = "musicmate.SelectedArpeggioDisplay";
        private const string PrefPlaybackBpmKey = "musicmate.PlaybackBpm";
        private const string PrefMusicBpmKey = "musicmate.MusicBpm";
        private const string PrefPcTunesKey = "musicmate.PcTunes";
        private const string PrefPcRandomKey = "musicmate.PcRandom";
        private const string PrefPcScalesKey = "musicmate.PcScales";
        private const string PrefPcArpeggiosKey = "musicmate.PcArpeggios";
        private const string PrefPitchMethodKey = "musicmate.PitchMethod";
        private const string PrefToleranceKey = "musicmate.Tolerance";
        private const string PrefAutoStartKey = "musicmate.AutoStart";
        //private const string PrefShowConductorCuesKey = "musicmate.ShowConductorCues";
        private const string PrefAutoRepeatKey = "musicmate.AutoRepeat";
        private const string PrefRepeatSameTuneKey = "musicmate.RepeatSameTune";
        private const string PrefAccidentalPercentKey = "musicmate.AccidentalPercent";
        private const string PrefCorrectThresholdKey = "musicmate.CorrectThreshold";
        private const string PrefPitchOffsetCentsKey = "musicmate.PitchOffsetCents";

        // Advanced detection settings keys
        private const string PrefAudioBufferSizeKey = "musicmate.AudioBufferSize";
        private const string PrefPitchWindowSizeKey = "musicmate.PitchWindowSize";
        private const string PrefMinFrequencyKey = "musicmate.MinFrequency";
        private const string PrefMaxFrequencyKey = "musicmate.MaxFrequency";
        private const string PrefSmoothingWindowSizeKey = "musicmate.SmoothingWindowSize";
        private const string PrefPitchConfidenceThresholdKey = "musicmate.PitchConfidenceThreshold";

        // Backing fields with persisted defaults
        private int _audioBufferSize = SessionPreferences.Get(PrefAudioBufferSizeKey, 1024);
        private bool _autoStart = SessionPreferences.Get(PrefAutoStartKey, true);
        private bool _autoRepeat = SessionPreferences.Get(PrefAutoRepeatKey, false);
        private bool _repeatSameTune = SessionPreferences.Get(PrefRepeatSameTuneKey, false);
        private int _pitchWindowSize = SessionPreferences.Get(PrefPitchWindowSizeKey, 4096);
        private string _highestNote = SessionPreferences.Get("musicmate.HighestNote", "C6") ?? "C6";
        private string _lowestNote = SessionPreferences.Get("musicmate.LowestNote", "E3") ?? "E3";
        private int _minFrequency = SessionPreferences.Get(PrefMinFrequencyKey, 60);
        private int _maxFrequency = SessionPreferences.Get(PrefMaxFrequencyKey, 8000);
        private int _smoothingWindowSize = SessionPreferences.Get(PrefSmoothingWindowSizeKey, 3);
        private double _pitchConfidenceThreshold = SessionPreferences.Get(PrefPitchConfidenceThresholdKey, 0.5);
        private string _randomSelectedNotesDisplay = string.Empty;
        public int AudioBufferSize
        {
            get => _audioBufferSize;
            set
            {
                if (_audioBufferSize != value)
                {
                    _audioBufferSize = value;
                    SessionPreferences.Set(PrefAudioBufferSizeKey, value);
                    OnPropertyChanged(nameof(AudioBufferSize));
                }
            }
        }

        public int PitchWindowSize
        {
            get => _pitchWindowSize;
            set
            {
                if (_pitchWindowSize != value)
                {
                    _pitchWindowSize = value;
                    SessionPreferences.Set(PrefPitchWindowSizeKey, value);
                    OnPropertyChanged(nameof(PitchWindowSize));
                }
            }
        }
        public int MinFrequency
        {
            get => _minFrequency;
            set
            {
                if (_minFrequency != value)
                {
                    _minFrequency = value;
                    SessionPreferences.Set(PrefMinFrequencyKey, value);
                    OnPropertyChanged(nameof(MinFrequency));
                }
            }
        }
        public int MaxFrequency
        {
            get => _maxFrequency;
            set
            {
                if (_maxFrequency != value)
                {
                    _maxFrequency = value;
                    SessionPreferences.Set(PrefMaxFrequencyKey, value);
                    OnPropertyChanged(nameof(MaxFrequency));
                }
            }
        }
        public int SmoothingWindowSize
        {
            get => _smoothingWindowSize;
            set
            {
                if (_smoothingWindowSize != value)
                {
                    _smoothingWindowSize = value;
                    SessionPreferences.Set(PrefSmoothingWindowSizeKey, value);
                    OnPropertyChanged(nameof(SmoothingWindowSize));
                }
            }
        }
        public double PitchConfidenceThreshold
        {
            get => _pitchConfidenceThreshold;
            set
            {
                if (Math.Abs(_pitchConfidenceThreshold - value) > 0.0001)
                {
                    _pitchConfidenceThreshold = value;
                    SessionPreferences.Set(PrefPitchConfidenceThresholdKey, value);
                    OnPropertyChanged(nameof(PitchConfidenceThreshold));
                }
            }
        }
        private string _instrument = SessionPreferences.Get(PrefInstrumentKey, "Bb");
        private string _key = SessionPreferences.Get(PrefKeySignatureKey, "C");
        private string? _keyBeforePracticeTune;
        private string _selectedScale = SessionPreferences.Get(PrefSelectedScaleKey, "Major");
        private ScaleSelectionMode _scaleSelectionMode = ParseScaleSelectionMode(
            SessionPreferences.Get(PrefScaleSelectionModeKey, nameof(ScaleSelectionMode.ByLevel)));
        private string? _tune = SessionPreferences.Get(PrefTuneKey, "Selected Scale");
        private string _selectedArpeggioId = SessionPreferences.Get(PrefSelectedArpeggioIdKey, "major-triad");
        private string _selectedArpeggioRoot = SessionPreferences.Get(PrefSelectedArpeggioRootKey, "C4");
        private string _selectedArpeggioDisplay = SessionPreferences.Get(PrefSelectedArpeggioDisplayKey, "C major triad");
        private int _childLevel;
        private int _tempo = LoadUnifiedTempo();
        private int _tolerance = SessionPreferences.Get(PrefToleranceKey, DefaultTolerance);
        public const int MinTempo = 30;
        public const int MaxTempo = 150;  //  2026.07.09 1658  reduce from 200 to 150 for better usability after testing play by phone.
        public const int DefaultTempo = 100;
        public const int DefaultTolerance = 50;
        public const int DefaultMusicBpm = DefaultTempo;
        public const int DefaultPlaybackBpm = DefaultTempo;

        private static int LoadUnifiedTempo()
        {
            int music = SessionPreferences.Get(PrefMusicBpmKey, DefaultTempo);
            int playback = SessionPreferences.Get(PrefPlaybackBpmKey, DefaultTempo);
            int tempo = Math.Clamp(music, MinTempo, MaxTempo);
            if (playback != tempo)
                SessionPreferences.Set(PrefPlaybackBpmKey, tempo);
            return tempo;
        }
        public const int DefaultPcTunes = 20;
        public const int DefaultPcRandom = 60;
        public const int DefaultPcScales = 20;
        public const int DefaultPcArpeggios = 0;
        /// <summary>Scale used for key-signature notation on built-in practice tunes (all major).</summary>
        public const string PracticeTuneKeySignatureScale = "Major";
        private int _pcTunes = SessionPreferences.Get(PrefPcTunesKey, DefaultPcTunes);
        private int _pcRandom = SessionPreferences.Get(PrefPcRandomKey, DefaultPcRandom);
        private int _pcScales = SessionPreferences.Get(PrefPcScalesKey, DefaultPcScales);
        private int _pcArpeggios = SessionPreferences.Get(PrefPcArpeggiosKey, DefaultPcArpeggios);
        private int _accidentalPercent = SessionPreferences.Get(PrefAccidentalPercentKey, 0);
        private int _correctThreshold = SessionPreferences.Get(PrefCorrectThresholdKey, MasteryPreferenceDefaults.CorrectThreshold);
        private double _pitchOffsetCents = SessionPreferences.Get(PrefPitchOffsetCentsKey, DefaultPitchOffsetCents);
        public const double DefaultPitchOffsetCents = 0.0;
        public double PitchOffsetCents
        {
            get => _pitchOffsetCents;
            set
            {
                if (Math.Abs(_pitchOffsetCents - value) > 0.01)
                {
                    _pitchOffsetCents = value;
                    SessionPreferences.Set(PrefPitchOffsetCentsKey, value);
                    OnPropertyChanged(nameof(PitchOffsetCents));
                }
            }
        }
        /// <summary>
        /// Milliseconds to debounce wrong-count increments. Exposed for binding in Advanced settings.
        /// Persisted to preferences key <see cref="PrefWrongDebounceMsKey"/>.
        /// </summary>
        public int WrongDebounceMs
        {
            get => _wrongDebounceMs;
            set
            {
                var clamped = Math.Max(0, value);
                if (_wrongDebounceMs == clamped) return;
                _wrongDebounceMs = clamped;
                SessionPreferences.Set(PrefWrongDebounceMsKey, _wrongDebounceMs);
                OnPropertyChanged(nameof(WrongDebounceMs));
            }
        }
        private readonly Dictionary<string, SessionNoteAggregate> _sessionNoteStats = new();
        private readonly List<NoteAttemptOutcome> _sessionAttemptOutcomes = new();
        private int _sessionRestCorrect;
        private int _sessionRestWrong;
        private DateTime? _lastCorrectNoteUtc;
        private const string PrefWrongDebounceMsKey = "musicmate.WrongDebounceMs";
        public const int DefaultDebounceMs = 300;
        private int _wrongDebounceMs = SessionPreferences.Get(PrefWrongDebounceMsKey, DefaultDebounceMs);
        // Track last wrong timestamp per note index to debounce rapid wrong increments
        private readonly Dictionary<int, DateTime> _lastWrongTimePerIndex = new();
        // Track last wrong timestamp per written name for session stats debouncing
        private readonly Dictionary<string, DateTime> _lastRandomWrongUtc = new();

        private const string PrefMasteredMethodKey = "musicmate.MasteredMethod";
        private const string PrefStreakCritKey = "musicmate.StreakCrit";
        private const string PrefUseNoteMasteryForGenerationKey = "musicmate.UseNoteMasteryForGeneration";
        private string _masteredMethod = SessionPreferences.Get(PrefMasteredMethodKey, MasteryPreferenceDefaults.MasteredMethod);
        private int _streakCrit = SessionPreferences.Get(PrefStreakCritKey, MasteryPreferenceDefaults.StreakCrit);
        private bool _useNoteMasteryForGeneration = SessionPreferences.Get(
            PrefUseNoteMasteryForGenerationKey, MasteryPreferenceDefaults.UseNoteMasteryForGeneration);

        /// <summary>
        /// When true (factory default), Random / Repeat-Same generation may omit mastered
        /// notes and bias toward weaker ones. When false, generation ignores mastery data
        /// entirely without changing stored NoteStat / NoteAttempt records.
        /// </summary>
        public bool UseNoteMasteryForGeneration
        {
            get => _useNoteMasteryForGeneration;
            set
            {
                if (_useNoteMasteryForGeneration == value) return;
                _useNoteMasteryForGeneration = value;
                SessionPreferences.Set(PrefUseNoteMasteryForGenerationKey, value);
                OnPropertyChanged(nameof(UseNoteMasteryForGeneration));
            }
        }

        /// <summary>
        /// Temporary written-note emphasis from Note Mastery (not persisted).
        /// Does not change the user's What to Play mode.
        /// </summary>
        private string? _temporaryEmphasizedWrittenNote;
        private int _temporaryEmphasizedSelectionPercent =
            NoteMasteryPreferenceDefaults.EmphasizedNoteSelectionPercent;
        private bool _pendingMasteryPracticeNavigation;

        public string? TemporaryEmphasizedWrittenNote
        {
            get => _temporaryEmphasizedWrittenNote;
            private set
            {
                if (_temporaryEmphasizedWrittenNote == value) return;
                _temporaryEmphasizedWrittenNote = value;
                OnPropertyChanged(nameof(TemporaryEmphasizedWrittenNote));
                OnPropertyChanged(nameof(HasTemporaryNoteEmphasis));
                OnPropertyChanged(nameof(TemporaryNoteEmphasisBannerText));
            }
        }

        public int TemporaryEmphasizedSelectionPercent
        {
            get => _temporaryEmphasizedSelectionPercent;
            private set => _temporaryEmphasizedSelectionPercent = Math.Clamp(value, 1, 95);
        }

        public bool HasTemporaryNoteEmphasis =>
            !string.IsNullOrWhiteSpace(TemporaryEmphasizedWrittenNote);

        public string TemporaryNoteEmphasisBannerText =>
            HasTemporaryNoteEmphasis
                ? $"Emphasizing {TemporaryEmphasizedWrittenNote}"
                : string.Empty;

        /// <summary>
        /// When true, MusicPage should regenerate with temporary note focus after navigation
        /// from Note Mastery, then clear this flag.
        /// </summary>
        public bool PendingMasteryPracticeNavigation
        {
            get => _pendingMasteryPracticeNavigation;
            set
            {
                if (_pendingMasteryPracticeNavigation == value) return;
                _pendingMasteryPracticeNavigation = value;
                OnPropertyChanged(nameof(PendingMasteryPracticeNavigation));
            }
        }

        public void BeginEmphasizedNotePractice(string writtenNoteName, int selectionPercent)
        {
            TemporaryEmphasizedWrittenNote = writtenNoteName?.Trim();
            TemporaryEmphasizedSelectionPercent = selectionPercent;
            PendingMasteryPracticeNavigation = true;
            IsDirty = true;
            DebugLog.WriteLine(
                DebugLogCategory.Statistics,
                $"[NoteMastery] Emphasize note={TemporaryEmphasizedWrittenNote} weight={TemporaryEmphasizedSelectionPercent}%");
            OnPropertyChanged(nameof(TemporaryEmphasizedSelectionPercent));
        }

        public void BeginPracticeThisNote(string writtenNoteName)
            => BeginEmphasizedNotePractice(
                writtenNoteName, NoteMasteryPreferenceDefaults.PracticeThisNoteSelectionPercent);

        public void ClearTemporaryNoteEmphasis(string reason = "cleared")
        {
            if (!HasTemporaryNoteEmphasis && !PendingMasteryPracticeNavigation)
                return;

            DebugLog.WriteLine(
                DebugLogCategory.Statistics,
                $"[NoteMastery] Clear emphasis note={TemporaryEmphasizedWrittenNote} reason={reason}");
            TemporaryEmphasizedWrittenNote = null;
            TemporaryEmphasizedSelectionPercent =
                NoteMasteryPreferenceDefaults.EmphasizedNoteSelectionPercent;
            PendingMasteryPracticeNavigation = false;
            IsDirty = true;
            OnPropertyChanged(nameof(TemporaryEmphasizedSelectionPercent));
        }

        public int? GetTemporaryEmphasizedMidi()
        {
            if (!HasTemporaryNoteEmphasis)
                return null;
            int midi = NoteNameToMidi(TemporaryEmphasizedWrittenNote!);
            return midi > 0 ? midi : null;
        }

        public string MasteredMethod
        {
            get => _masteredMethod;
            set
            {
                if (_masteredMethod != value)
                {
                    _masteredMethod = value;
                    SessionPreferences.Set(PrefMasteredMethodKey, value);
                    OnPropertyChanged(nameof(MasteredMethod));
                    NotifyMasterySettingsChanged();
                }
            }
        }
        public int StreakCrit
        {
            get => _streakCrit;
            set
            {
                var clamped = Math.Clamp(value, 1, 50);
                if (_streakCrit != clamped)
                {
                    _streakCrit = clamped;
                    SessionPreferences.Set(PrefStreakCritKey, clamped);
                    OnPropertyChanged(nameof(StreakCrit));
                    NotifyMasterySettingsChanged();
                }
            }
        }
        // Per-session streak tracking: written name → current consecutive correct count
        private readonly Dictionary<string, int> _sessionStreaks = new();
        /// <summary>True when timing accuracy affects overall correctness and mastery.</summary>
        public bool IsTimingActiveForMastery()
            => MasteryEvaluator.TimingAffectsMastery(ChildLevel);
        /// <summary>
        /// When timing is not active for the current level, overall follows pitch only.
        /// When timing is active, both pitch and timing must pass.
        /// </summary>
        public bool ComputeOverallCorrect(bool pitchCorrect, bool? timingCorrect)
        {
            if (!pitchCorrect)
                return false;
            if (!IsTimingActiveForMastery())
                return true;
            return timingCorrect == true;
        }      
        public void RecordAttemptOutcome(in NoteAttemptOutcome outcome)
        {
            _sessionAttemptOutcomes.Add(outcome);

            if (outcome.IsRest)
            {
                if (outcome.OverallCorrect)
                    _sessionRestCorrect++;
                else
                    _sessionRestWrong++;
                return;
            }

            var writtenName = outcome.ExpectedWrittenNoteName;
            if (string.IsNullOrEmpty(writtenName))
                return;

            if (!_sessionNoteStats.TryGetValue(writtenName, out var agg))
                agg = default;

            if (outcome.PitchCorrect)
                agg.PitchCorrect++;
            else
                agg.PitchWrong++;

            if (outcome.TimingCorrect == true)
                agg.TimingCorrect++;
            else if (outcome.TimingCorrect == false)
                agg.TimingWrong++;

            if (outcome.OverallCorrect)
                agg.OverallCorrect++;
            else
                agg.OverallWrong++;

            var now = DateTime.UtcNow;
            if (outcome.OverallCorrect)
            {
                if (_lastCorrectNoteUtc.HasValue)
                {
                    agg.TotalMs += (now - _lastCorrectNoteUtc.Value).TotalMilliseconds;
                    agg.MsCount++;
                }
                _lastCorrectNoteUtc = now;
                _lastRandomWrongUtc.Remove(writtenName);
                _sessionStreaks[writtenName] = _sessionStreaks.GetValueOrDefault(writtenName, 0) + 1;
            }
            else
            {
                _sessionStreaks[writtenName] = 0;
                if (!_lastRandomWrongUtc.TryGetValue(writtenName, out var lastWrong)
                    || (now - lastWrong).TotalMilliseconds >= _wrongDebounceMs)
                {
                    _lastRandomWrongUtc[writtenName] = now;
                }
            }

            _sessionNoteStats[writtenName] = agg;
        }
        public Dictionary<string, SessionNoteAggregate> GetAndClearSessionNoteStats()
        {
            var copy = new Dictionary<string, SessionNoteAggregate>(_sessionNoteStats);
            _sessionNoteStats.Clear();
            return copy;
        }
        public IReadOnlyList<NoteAttemptOutcome> GetSessionAttemptOutcomes()
            => _sessionAttemptOutcomes;
        public (int PitchRight, int PitchWrong, int TimingRight, int TimingWrong,
            int OverallRight, int OverallWrong, int RestRight, int RestWrong) GetSessionSummaryCounts()
        {
            int pitchRight = 0, pitchWrong = 0, timingRight = 0, timingWrong = 0;
            int overallRight = 0, overallWrong = 0;
            foreach (var outcome in _sessionAttemptOutcomes)
            {
                if (outcome.IsRest)
                    continue;
                if (outcome.PitchCorrect) pitchRight++; else pitchWrong++;
                if (outcome.TimingCorrect == true) timingRight++;
                else if (outcome.TimingCorrect == false) timingWrong++;
                if (outcome.OverallCorrect) overallRight++; else overallWrong++;
            }

            return (pitchRight, pitchWrong, timingRight, timingWrong,
                overallRight, overallWrong, _sessionRestCorrect, _sessionRestWrong);
        }
        public void ClearSessionAttemptOutcomes()
        {
            _sessionAttemptOutcomes.Clear();
            _sessionRestCorrect = 0;
            _sessionRestWrong = 0;
        }        
        public Dictionary<string, int> GetSessionStreaks()
        {
            return new Dictionary<string, int>(_sessionStreaks);
        }

        private static void NotifyMasterySettingsChanged()
        {
            ServiceHelper.GetService<StatisticsCacheService>()?.InvalidateNoteStats();
            ServiceHelper.GetService<NoteMasteryService>()?.Invalidate();
        }
        /// <summary>
        /// Returns the set of written-pitch MIDI numbers that the player has mastered,
        /// using the same criteria as v1 Random mode mastery filtering.
        /// Used by v2 sequence generation to exclude mastered notes.
        /// Returns an empty set when <see cref="UseNoteMasteryForGeneration"/> is false.
        /// </summary>
        public async Task<HashSet<int>> GetMasteredMidiNumbersAsync()
        {
            var result = new HashSet<int>();
            if (!UseNoteMasteryForGeneration)
                return result;

            try
            {
                var db = ServiceHelper.GetService<NoteDatabase>();
                if (db == null) return result;
                await db.InitializeAsync();

                var statsList = await db.GetAllAsync();
                foreach (var stat in statsList)
                {
                    if (!MasteryEvaluator.IsFullyMastered(stat, this)) continue;

                    int midi = NoteNameToMidi(stat.WrittenName);
                    if (midi > 0) result.Add(midi);
                }
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[Session] GetMasteredMidiNumbersAsync ERROR: {ex}");
            }
            return result;
        }
        public (double sumCorrects, double sumWrongs, double adjustedPercentCorrect) GetSessionCorrectWrongTotals()
        {
            double sumCorrects = CorrectNoteIndices.Count;
            double sumWrongs = NoteFeedbacks.Values.Sum(v => v.Wrong);
            double total = sumCorrects + sumWrongs;
            double rpc = total > 0 ? 100 * sumCorrects / total : 0; // Raw Percent Correct
            double apc;                                                 // Adjusted Percent Correct
            //double pcc = 40;                                          // Percent Correct Correction // ADJUST AS NECESSARY
            //if (rpc >= pcc)
            //{
            //    apc = 100;
            //}
            //else
            //{
            //   apc = 100.0 * rpc / pcc;
            //}  //  2026.03.20 1853  
            apc = rpc;
            return (sumCorrects, sumWrongs, apc);
        }

        private static AccidentalPreference GetPreferenceForScale(string key, string scale)
        {
            if (scale == "Enigmatic")
            {
                return AccidentalPreference.Sharps; // example override
            }
            return KeyUsesFlats(key) ? AccidentalPreference.Flats : AccidentalPreference.Sharps;
        }
        private static string[] BuildLetterAwareScale(
          string tonicWithOctave,
          int[] semitonesAscending,
          int[] semitonesDescending,
          AccidentalPreference prefAsc,
          AccidentalPreference prefDesc,
          HashSet<int> flats,
          HashSet<int> sharps)
        {
            var s = tonicWithOctave.Trim();
            var tonicLetter = char.ToUpperInvariant(s[0]);
            var octave = int.Parse(s[^1].ToString());
            var tonicIdx = Array.IndexOf(Letters, tonicLetter);
            var tonicMidi = NoteNameToMidi(s);

            var ascending = SpellDegrees(tonicLetter, octave, tonicIdx, tonicMidi, semitonesAscending, prefAsc, flats, sharps);

            string[] descending;
            if (semitonesDescending.Length == 0)
            {
                descending = ascending.Length > 1
                    ? ascending.Reverse().ToArray()
                    : Array.Empty<string>();
            }
            else
            {
                descending = SpellDescendingDegrees(tonicLetter, octave, tonicIdx, tonicMidi, semitonesDescending, prefDesc, flats, sharps);
            }

            return ascending.Concat(descending.Skip(1)).ToArray();
        }
        // Helper: Get the pitch class sequence for the selected scale
        private static string[] BuildScaleDegrees(string key, string selectedScale)
        {
            // For C Major: ["C", "D", "E", "F", "G", "A", "B"]
            // You can expand this for other scales as needed
            // This example assumes major scale
            // For a more general solution, use your scale-building logic to get the pitch classes
            var scale = BuildScaleSequence(key, selectedScale);
            return scale.Select(n => new string(n.TakeWhile(c => !char.IsDigit(c)).ToArray())).Distinct().ToArray();
        }
        public string LowestNote
        {
            get => string.IsNullOrWhiteSpace(_lowestNote) ? "E3" : _lowestNote;
            set => SetLowestNote(value);
        }
        public string HighestNote
        {
            get => string.IsNullOrWhiteSpace(_highestNote) ? "C6" : _highestNote;
            set => SetHighestNote(value);
        }

        private void SetLowestNote(string? value, bool fromUser = true)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "E3" : value;
            if (_lowestNote == normalized) return;

            _lowestNote = normalized;
            SessionPreferences.Set("musicmate.LowestNote", _lowestNote);
            if (fromUser && !_suppressNoteRangeCustomization)
                NoteRangeCustomized = true;
            OnPropertyChanged(nameof(LowestNote));
            OnPropertyChanged(nameof(AutomaticNoteRangeDisplay));
            if (IsRandomMode)
                _ = UpdateRandomSelectedNotesDisplayAsync();
        }

        private void SetHighestNote(string? value, bool fromUser = true)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "C6" : value;
            if (_highestNote == normalized) return;

            _highestNote = normalized;
            SessionPreferences.Set("musicmate.HighestNote", _highestNote);
            if (fromUser && !_suppressNoteRangeCustomization)
                NoteRangeCustomized = true;
            OnPropertyChanged(nameof(HighestNote));
            OnPropertyChanged(nameof(AutomaticNoteRangeDisplay));
            if (IsRandomMode)
                _ = UpdateRandomSelectedNotesDisplayAsync();
        }
        public int CorrectThreshold
        {
            get => _correctThreshold;
            set
            {
                var clamped = Math.Clamp(value, 0, 100);
                if (_correctThreshold == clamped)
                {
                    return;
                }
                _correctThreshold = clamped;
                SessionPreferences.Set(PrefCorrectThresholdKey, _correctThreshold);
                OnPropertyChanged(nameof(CorrectThreshold));
                NotifyMasterySettingsChanged();
            }
        }
        public static string[] InstrumentOptions => InstrumentCatalog.DisplayNames;
        /// <summary>
        /// Maps a stored instrument value (short key like "Bb" or a full InstrumentOptions entry)
        /// to the canonical InstrumentOptions string.
        /// </summary>
        public static string NormalizeInstrumentOption(string? value)
            => InstrumentCatalog.Resolve(value).Id;
        private int GetInstrumentTransposeOffset()
            => CurrentInstrumentProfile.TransposeOffset;
        public int InstrumentTransposeOffset => GetInstrumentTransposeOffset();
        public InstrumentProfile CurrentInstrumentProfile => InstrumentCatalog.Resolve(_instrument);
        public string InstrumentDisplayName => CurrentInstrumentProfile.DisplayName;
        public string InstrumentKey => CurrentInstrumentProfile.InstrumentKey;
        public string AutomaticNoteRangeDisplay => $"{LowestNote} - {HighestNote}";
        public IReadOnlyList<int> AvailableInstrumentMidis
            => InstrumentCatalog.BuildAvailableMidiSet(CurrentInstrumentProfile, ChildLevel);
        public IReadOnlyList<string> AvailableInstrumentNoteNames
            => AvailableInstrumentMidis
                .Select(midi => MidiToNoteName(midi, KeyUsesFlats(Key)))
                .ToArray();
        public void ApplyAutomaticInstrumentRange(int? levelOverride = null, bool fullReset = false)
        {
            int level = levelOverride ?? ChildLevel;
            var (autoLowest, autoHighest) = InstrumentCatalog.GetAutomaticRange(CurrentInstrumentProfile, level);
            string autoLow = string.IsNullOrWhiteSpace(autoLowest) ? "E3" : autoLowest;
            string autoHigh = string.IsNullOrWhiteSpace(autoHighest) ? "C6" : autoHighest;

            if (fullReset)
                ClearNoteRangeCustomization();

            string newLow;
            string newHigh;
            if (fullReset)
            {
                newLow = autoLow;
                newHigh = autoHigh;
            }
            else
            {
                int autoLowMidi = NoteNameToMidi(autoLow);
                int autoHighMidi = NoteNameToMidi(autoHigh);
                int curLowMidi = NoteNameToMidi(LowestNote);
                int curHighMidi = NoteNameToMidi(HighestNote);
                bool preferFlats = KeyUsesFlats(Key);

                int newLowMidi = curLowMidi < autoLowMidi ? curLowMidi : autoLowMidi;
                int newHighMidi = curHighMidi > autoHighMidi ? curHighMidi : autoHighMidi;
                if (newLowMidi > newHighMidi)
                {
                    newLowMidi = autoLowMidi;
                    newHighMidi = autoHighMidi;
                }

                newLow = MidiToNoteName(newLowMidi, preferFlats);
                newHigh = MidiToNoteName(newHighMidi, preferFlats);
            }

            _suppressNoteRangeCustomization = true;
            try
            {
                SetLowestNote(newLow, fromUser: false);
                SetHighestNote(newHigh, fromUser: false);
            }
            finally
            {
                _suppressNoteRangeCustomization = false;
            }

            NotifyNoteRangeDerivedPropertiesChanged();
        }

        private void NotifyNoteRangeDerivedPropertiesChanged()
        {
            OnPropertyChanged(nameof(AutomaticNoteRangeDisplay));
            OnPropertyChanged(nameof(AvailableInstrumentMidis));
            OnPropertyChanged(nameof(AvailableInstrumentNoteNames));
        }
        /// <summary>
        /// Ensures random-mode generation has an interval cap and instrument range.
        /// Uses <see cref="ChildLevel"/> when set; otherwise falls back to the saved
        /// ChildPractice level preference; otherwise applies a modest adult default cap.
        /// </summary>
        public void EnsureRandomModeGenerationSettings()
        {
            if (!IsRandomMode)
                return;

            int level = ChildLevel;
            if (level <= 0)
                level = SessionPreferences.Get("ChildPractice.Level", 0);

            if (level > 0)
                DifficultyLevelMapper.ApplyLevelDerivedSettings(level, this);
            else if (MaxMelodicIntervalSemitones <= 0)
                MaxMelodicIntervalSemitones = 7;
        }
        public string TimingStatsDisplay =>
            _timingAccuracyPercent.HasValue
                ? $"Timing: {_timingAccuracyPercent.Value:F1}%"
                : "Timing: N/A";
        private void NotifyTimingStatsChanged()
        {
            OnPropertyChanged(nameof(TimingStatsDisplay));
            OnPropertyChanged(nameof(DetectedBpm));
        }
        private static string[] SpellDescendingDegrees(
            char tonicLetter,
            int startOctave,
            int tonicIdx,
            int tonicMidi,
            int[] semitones,
            AccidentalPreference pref,
            HashSet<int> flats,
            HashSet<int> sharps)
        {
            var result = new List<string>(semitones.Length);

            for (int i = 0; i < semitones.Length; i++)
            {
                var letterIdx = (tonicIdx - i) % 7;
                if (letterIdx < 0)
                    letterIdx += 7;

                var degLetter = Letters[letterIdx];
                var targetMidi = tonicMidi + semitones[i];

                // ✅ same fix here
                var noteName = SpellNote(degLetter, targetMidi);

                result.Add(noteName);
            }

            return result.ToArray();
        }
       private static string[] SpellDegrees(
            char tonicLetter,
            int octave,
            int tonicIdx,
            int tonicMidi,
            int[] semitones,
             AccidentalPreference pref,
             HashSet<int> flats,
            HashSet<int> sharps)
        {
            var result = new List<string>(semitones.Length);

            for (int i = 0; i < semitones.Length; i++)
            {
                var degLetter = Letters[(tonicIdx + i) % 7];
                var targetMidi = tonicMidi + semitones[i];

                // ✅ THIS is the fix:
                var noteName = SpellNote(degLetter, targetMidi);

                result.Add(noteName);
            }

            return result.ToArray();
        }
        private const string PrefNoteRangeCustomizedKey = "musicmate.NoteRangeCustomized";
        private bool _noteRangeCustomized = SessionPreferences.Get(PrefNoteRangeCustomizedKey, false);
        private bool _suppressNoteRangeCustomization;

        /// <summary>When true, <see cref="LowestNote"/> / <see cref="HighestNote"/> were set manually and are not fully auto-managed.</summary>
        public bool NoteRangeCustomized
        {
            get => _noteRangeCustomized;
            private set
            {
                if (_noteRangeCustomized == value) return;
                _noteRangeCustomized = value;
                SessionPreferences.Set(PrefNoteRangeCustomizedKey, value);
                OnPropertyChanged(nameof(NoteRangeCustomized));
            }
        }

        public void ClearNoteRangeCustomization() => NoteRangeCustomized = false;

        private const string PrefMinCorrectCountKey = "musicmate.MinCorrectCount";
        private int _minCorrectCount = SessionPreferences.Get(PrefMinCorrectCountKey, MasteryPreferenceDefaults.MinCorrectCount);
        private const string PrefOmitMsAvgThresholdKey = "musicmate.OmitMsAvgThreshold";
        private int _omitMsAvgThreshold = SessionPreferences.Get(PrefOmitMsAvgThresholdKey, MasteryPreferenceDefaults.OmitMsAvgThreshold);
        public int OmitMsAvgThreshold
        {
            get => _omitMsAvgThreshold;
            set
            {
                var clamped = Math.Clamp(value, 0, 5000);
                if (_omitMsAvgThreshold != clamped)
                {
                    _omitMsAvgThreshold = clamped;
                    SessionPreferences.Set(PrefOmitMsAvgThresholdKey, clamped);
                    OnPropertyChanged(nameof(OmitMsAvgThreshold));
                    NotifyMasterySettingsChanged();
                }
            }
        }
        public int MinCorrectCount
        {
            get => _minCorrectCount;
            set
            {
                var clamped = Math.Clamp(value, 1, 20);
                if (_minCorrectCount != clamped)
                {
                    _minCorrectCount = clamped;
                    SessionPreferences.Set(PrefMinCorrectCountKey, clamped);
                    OnPropertyChanged(nameof(MinCorrectCount));
                    NotifyMasterySettingsChanged();
                }
            }
        }
        private async Task UpdateRandomSelectedNotesDisplayAsync()
        {
            if (IsRandomMode)
            {
                var notes = await BuildRandomSequenceAsync();
                RandomSelectedNotesDisplay = notes.Length > 0
                    ? $"Random Notes: {string.Join(", ", notes)}"
                    : "Random Notes: (none)";
            }
            else
            {
                RandomSelectedNotesDisplay = string.Empty;
            }
        }
        public string RandomSelectedNotesDisplay
        {
            get => _randomSelectedNotesDisplay;
            private set
            {
                if (_randomSelectedNotesDisplay != value)
                {
                    _randomSelectedNotesDisplay = value;
                    OnPropertyChanged(nameof(RandomSelectedNotesDisplay));
                }
            }
        }
        public string Instrument
        {
            get => _instrument;
            set
            {
                var normalized = NormalizeInstrumentOption(value);
                if (_instrument == normalized) return;
                _instrument = normalized;
                SessionPreferences.Set(PrefInstrumentKey, _instrument);
                ApplyAutomaticInstrumentRange(fullReset: true);
                OnPropertyChanged(nameof(Instrument));
                OnPropertyChanged(nameof(InstrumentDisplayName));
                OnPropertyChanged(nameof(InstrumentKey));
                OnPropertyChanged(nameof(InstrumentTransposeOffset));
            }
        }

        /// <summary>
        /// The child difficulty level (1–100) selected on HomePage before this session
        /// started.  0 means the session was started from the standard practice pages, not
        /// from HomePage, and no child-level SessionResult should be recorded.
        ///
        /// Not persisted here — HomePage owns persistence via Preferences("ChildPractice.Level").
        /// </summary>
        public int ChildLevel
        {
            get => _childLevel;
            set
            {
                var clamped = Math.Clamp(value, 0, 100);
                if (_childLevel == clamped) return;
                _childLevel = clamped;
                ApplyAutomaticInstrumentRange(fullReset: false);
                OnPropertyChanged(nameof(ChildLevel));
                NotifyMasterySettingsChanged();
            }
        }

        /// <summary>
        /// When true, accidental, rhythm, key, and scale were changed by the user during a child
        /// session and should not be overwritten until the child level changes.
        /// </summary>
        public bool ChildPracticeSettingsCustomized { get; private set; }
        /// <summary>Marks user-owned child session settings for the current child level.</summary>
        public void MarkChildPracticeSettingsCustomized()
        {
            if (ChildLevel <= 0)
                return;
            ChildPracticeSettingsCustomized = true;
        }
        /// <summary>Clears the child practice override flag (level defaults will apply again).</summary>
        public void ClearChildPracticeSettingsCustomization()
            => ChildPracticeSettingsCustomized = false;
        public string Key
        {
            get => _key;
            set
            {
                if (_key == value) return;
                _key = value;
                SessionPreferences.Set(PrefKeySignatureKey, _key);
                OnPropertyChanged(nameof(Key));
                OnPropertyChanged(nameof(EffectiveScaleDisplay));
            }
        }
        public string SelectedScale
        {
            get => _selectedScale;
            set
            {
                if (_selectedScale == value || string.IsNullOrWhiteSpace(value)) return;
                _selectedScale = value;
                SessionPreferences.Set(PrefSelectedScaleKey, _selectedScale);
                OnPropertyChanged(nameof(SelectedScale));
                if (ScaleSelectionMode == ScaleSelectionMode.Named && !IsRandomMode)
                    SetEffectiveScale(_selectedScale);
            }
        }

        private string _effectiveScale = string.Empty;

        /// <summary>
        /// The scale used for the current generated tune. In random mode this is chosen once
        /// per generation and remains stable for that tune.
        /// </summary>
        public string EffectiveScale
        {
            get => string.IsNullOrWhiteSpace(_effectiveScale) ? SelectedScale : _effectiveScale;
            private set => SetEffectiveScale(value);
        }

        /// <summary>Scale passed to note generation.</summary>
        public string GenerationScale => EffectiveScale;

        /// <summary>Label for the active scale selection.</summary>
        public string EffectiveScaleDisplay => ScaleSelectionMode switch
        {
            ScaleSelectionMode.ByLevel => $"{Key} {EffectiveScale} (By Level)",
            ScaleSelectionMode.Random when IsRandomMode => $"Random — {Key} {EffectiveScale}",
            ScaleSelectionMode.Random => $"Random — {Key} {EffectiveScale}",
            _ when IsRandomMode => $"Random — {Key} {EffectiveScale}",
            _ => $"{Key} {SelectedScale}"
        };

        private void SetEffectiveScale(string scale)
        {
            if (string.IsNullOrWhiteSpace(scale))
                scale = SelectedScale;
            if (_effectiveScale == scale)
            {
                OnPropertyChanged(nameof(EffectiveScaleDisplay));
                OnPropertyChanged(nameof(GenerationScale));
                return;
            }
            _effectiveScale = scale;
            OnPropertyChanged(nameof(EffectiveScale));
            OnPropertyChanged(nameof(EffectiveScaleDisplay));
            OnPropertyChanged(nameof(GenerationScale));
        }

        /// <summary>
        /// Locks the effective scale for one generated tune. Call once before each new generation.
        /// Does not re-randomize scale or key; use <see cref="PrepareFreshScaleAndKeyForGeneration"/>
        /// before regeneration when fresh material is required.
        /// </summary>
        public void PrepareEffectiveScaleForGeneration(int generationSeed)
        {
            int level = ResolvePracticeLevel();
            bool weightedRandom = false;
            string? resetReason = null;
            string activeScale;

            switch (ScaleSelectionMode)
            {
                case ScaleSelectionMode.ByLevel:
                    activeScale = level > 0
                        ? (!string.IsNullOrWhiteSpace(_effectiveScale)
                            ? _effectiveScale
                            : ChildLevelProgression.GetDefaultScaleForLevel(level))
                        : SelectedScale;
                    SetEffectiveScale(activeScale);
                    break;

                case ScaleSelectionMode.Random:
                    activeScale = !string.IsNullOrWhiteSpace(_effectiveScale)
                        ? _effectiveScale
                        : SelectedScale;
                    weightedRandom = level > 0;
                    SetEffectiveScale(activeScale);
                    break;

                default:
                    activeScale = SelectedScale;
                    if (level > 0 && !ChildLevelProgression.IsScaleAllowedAtLevel(level, activeScale))
                    {
                        ScaleSelectionMode = ScaleSelectionMode.ByLevel;
                        activeScale = ChildLevelProgression.GetDefaultScaleForLevel(level);
                        SelectedScale = activeScale;
                        resetReason = "SelectedScaleNotAllowed";
                    }
                    SetEffectiveScale(activeScale);
                    break;
            }

            LogScaleLevel(level, activeScale, weightedRandom, resetReason);
        }

        /// <summary>
        /// Chooses a fresh scale (when mode is Random or By Level) and key before note generation.
        /// Skipped when <paramref name="repeatSame"/> is true.
        /// </summary>
        public void PrepareFreshScaleAndKeyForGeneration(string trigger, bool repeatSame, int generationSeed)
        {
            int level = ResolvePracticeLevel();
            int keyPoolLevel = level > 0 ? level : 100;
            string oldScale = EffectiveScale;
            string oldKey = Key;
            string scaleModeLabel = GetScaleSelectionModeLogLabel();
            string allowedScales = level > 0
                ? string.Join(",", ChildLevelProgression.GetAllowedScalesForLevel(level))
                : "n/a";
            string allowedKeys = string.Join(",", ChildLevelProgression.GetAllowedKeys(keyPoolLevel));

            if (repeatSame)
            {
                LogScaleKeyRandom(trigger, repeatSame, level, scaleModeLabel, oldScale, oldKey,
                    oldScale, oldKey, allowedScales, allowedKeys, scaleChanged: false, keyChanged: false);
                return;
            }

            var rng = new Random(generationSeed);
            string newScale = oldScale;
            bool scaleChanged = false;

            switch (ScaleSelectionMode)
            {
                case ScaleSelectionMode.ByLevel:
                    if (level > 0)
                    {
                        newScale = ResolveScaleForFreshGeneration(
                            ScaleSelectionMode.ByLevel, Tune, IsRandomMode, level, rng);
                        if (!string.Equals(SelectedScale, newScale, StringComparison.Ordinal))
                            SelectedScale = newScale;
                        SetEffectiveScale(newScale);
                        scaleChanged = !string.Equals(oldScale, newScale, StringComparison.Ordinal);
                    }
                    break;

                case ScaleSelectionMode.Random:
                    if (level > 0)
                    {
                        newScale = ChildLevelProgression.PickWeightedRandomScale(level, rng);
                        SelectedScale = newScale;
                        SetEffectiveScale(newScale);
                        scaleChanged = !string.Equals(oldScale, newScale, StringComparison.Ordinal);
                    }
                    break;

                default:
                    newScale = SelectedScale;
                    if (level > 0 && !ChildLevelProgression.IsScaleAllowedAtLevel(level, newScale))
                    {
                        ScaleSelectionMode = ScaleSelectionMode.ByLevel;
                        newScale = ChildLevelProgression.GetDefaultScaleForLevel(level);
                        SelectedScale = newScale;
                        scaleChanged = true;
                    }
                    else
                    {
                        scaleChanged = !string.Equals(oldScale, newScale, StringComparison.Ordinal);
                    }
                    SetEffectiveScale(newScale);
                    break;
            }

            string newKey = ResolveKeyForFreshGeneration(
                Tune ?? string.Empty, CurrentTune, newScale, keyPoolLevel, rng, preservedKey: oldKey);
            bool keyChanged = !string.Equals(oldKey, newKey, StringComparison.Ordinal);
            if (keyChanged)
                Key = newKey;

            LogScaleKeyRandom(trigger, repeatSame, level, scaleModeLabel, oldScale, oldKey,
                newScale, newKey, allowedScales, allowedKeys, scaleChanged, keyChanged);
        }

        /// <summary>
        /// Key for fresh generation. Practice tunes keep their authored key instead of
        /// the By Level session key pool. Arpeggios keep the written key derived from the
        /// selected concert root (instrument transposition already applied).
        /// </summary>
        internal static string ResolveKeyForFreshGeneration(
            string tuneMode,
            PracticeTune? currentTune,
            string scale,
            int keyPoolLevel,
            Random rng,
            string? preservedKey = null)
        {
            if (tuneMode == "Practice Tune" && !string.IsNullOrWhiteSpace(currentTune?.Key))
                return currentTune.Key;
            if (string.Equals(tuneMode, "Arpeggio", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(preservedKey))
                return preservedKey;
            return ChildLevelProgression.PickBalancedKeyForSignature(scale, keyPoolLevel, rng);
        }

        /// <summary>
        /// Scale for fresh By Level generation. Scale and Random composition exercises
        /// draw from the level pool (e.g. Major + Natural Minor at L24); tunes/arpeggios
        /// keep the level default for display only.
        /// </summary>
        internal static string ResolveScaleForFreshGeneration(
            ScaleSelectionMode mode,
            string? tuneMode,
            bool isRandomMode,
            int level,
            Random rng)
        {
            if (mode != ScaleSelectionMode.ByLevel || level <= 0)
                return ChildLevelProgression.GetDefaultScaleForLevel(Math.Max(level, 1));

            if (ShouldPickFreshScaleFromLevelPool(tuneMode, isRandomMode))
                return ChildLevelProgression.PickWeightedRandomScale(level, rng);

            return ChildLevelProgression.GetDefaultScaleForLevel(level);
        }

        internal static bool ShouldPickFreshScaleFromLevelPool(string? tuneMode, bool isRandomMode)
        {
            if (string.Equals(tuneMode, "Practice Tune", StringComparison.Ordinal))
                return false;
            if (string.Equals(tuneMode, "Arpeggio", StringComparison.Ordinal))
                return false;
            // Selected Scale: composition Scale (ordered) or Random categories.
            return string.Equals(tuneMode, "Selected Scale", StringComparison.Ordinal)
                   || isRandomMode;
        }

        /// <summary>Written key and scale for a practice tune's fixed key signature.</summary>
        public static (string Key, string Scale) ResolvePracticeTuneNotation(PracticeTune tune)
            => string.IsNullOrWhiteSpace(tune.Key)
                ? ("C", PracticeTuneKeySignatureScale)
                : (tune.Key, PracticeTuneKeySignatureScale);

        /// <summary>
        /// Key and scale for note spelling and pitch evaluation.
        /// Practice tunes use their authored key, not the By Level session key.
        /// Arpeggios use Major so leftover SelectedScale (e.g. Natural Minor) does not
        /// remap the written key through relative-major rules (D + Natural Minor → F).
        /// </summary>
        public (string Key, string Scale) GetNotationKeyAndScale()
        {
            if (Tune == "Practice Tune"
                && CurrentTune != null
                && !string.IsNullOrWhiteSpace(CurrentTune.Key))
                return ResolvePracticeTuneNotation(CurrentTune);
            if (Tune == "Arpeggio")
                return (Key, "Major");
            return (Key, SelectedScale);
        }

        private string GetScaleSelectionModeLogLabel()
            => ScaleSelectionMode switch
            {
                ScaleSelectionMode.ByLevel => "ByLevel",
                ScaleSelectionMode.Random => "Random",
                _ => SelectedScale ?? "Named"
            };

        private static void LogScaleKeyRandom(
            string trigger,
            bool repeatSame,
            int level,
            string scaleMode,
            string oldScale,
            string oldKey,
            string newScale,
            string newKey,
            string allowedScales,
            string allowedKeys,
            bool scaleChanged,
            bool keyChanged)
        {
#if DEBUG
            if (trigger is not ("GoButton" or "AutoStart"))
                return;
            DebugLog.WriteLine(
                $"[ScaleKeyRandom] Trigger={trigger} RepeatSame={repeatSame} Level={level} " +
                $"ScaleMode={scaleMode} OldScale={oldScale} OldKey={oldKey} " +
                $"NewScale={newScale} NewKey={newKey} ScaleChanged={scaleChanged} KeyChanged={keyChanged} " +
                $"AllowedScales={allowedScales} AllowedKeys={allowedKeys} OK");
#endif
        }

        /// <summary>Applies Part 6 rules when the child level changes.</summary>
        public void ApplyScaleSelectionOnLevelChange(int level, Random? rng = null)
        {
            level = Math.Clamp(level, 1, 100);
            string? resetReason = null;
            bool weightedRandom = false;
            string activeScale;

            switch (ScaleSelectionMode)
            {
                case ScaleSelectionMode.ByLevel:
                    activeScale = ChildLevelProgression.GetDefaultScaleForLevel(level);
                    SelectedScale = activeScale;
                    SetEffectiveScale(activeScale);
                    break;

                case ScaleSelectionMode.Random:
                    activeScale = ChildLevelProgression.PickWeightedRandomScale(level, rng ?? Random.Shared);
                    weightedRandom = true;
                    SelectedScale = activeScale;
                    SetEffectiveScale(activeScale);
                    break;

                default:
                    if (ChildLevelProgression.IsScaleAllowedAtLevel(level, SelectedScale))
                    {
                        activeScale = SelectedScale;
                        SetEffectiveScale(activeScale);
                    }
                    else
                    {
                        ScaleSelectionMode = ScaleSelectionMode.ByLevel;
                        activeScale = ChildLevelProgression.GetDefaultScaleForLevel(level);
                        SelectedScale = activeScale;
                        SetEffectiveScale(activeScale);
                        resetReason = "SelectedScaleNotAllowed";
                    }
                    break;
            }

            LogScaleLevel(level, activeScale, weightedRandom, resetReason);
        }

        public bool TryApplyScalePickerSelection(string selection, out string? rejectionReason)
        {
            rejectionReason = null;
            int level = ResolvePracticeLevel();

            if (selection == ScaleSelectionByLevel)
            {
                ScaleSelectionMode = ScaleSelectionMode.ByLevel;
                if (level > 0)
                {
                    SelectedScale = ChildLevelProgression.GetDefaultScaleForLevel(level);
                    SetEffectiveScale(SelectedScale);
                }
                LogScaleLevel(level, EffectiveScale, weightedRandom: false, resetReason: null);
                return true;
            }

            if (selection == ScaleSelectionRandom)
            {
                ScaleSelectionMode = ScaleSelectionMode.Random;
                if (level > 0)
                {
                    var picked = ChildLevelProgression.PickWeightedRandomScale(level, Random.Shared);
                    SelectedScale = picked;
                    SetEffectiveScale(picked);
                    LogScaleLevel(level, picked, weightedRandom: true, resetReason: null);
                }
                return true;
            }

            if (!IsNamedScaleOption(selection))
            {
                rejectionReason = "UnknownScaleOption";
                return false;
            }

            if (level > 0 && !ChildLevelProgression.IsScaleAllowedAtLevel(level, selection))
            {
                ScaleSelectionMode = ScaleSelectionMode.ByLevel;
                var fallback = ChildLevelProgression.GetDefaultScaleForLevel(level);
                SelectedScale = fallback;
                SetEffectiveScale(fallback);
                rejectionReason = "SelectedScaleNotAllowed";
                LogScaleLevel(level, fallback, weightedRandom: false, resetReason: rejectionReason);
                return false;
            }

            ScaleSelectionMode = ScaleSelectionMode.Named;
            SelectedScale = selection;
            SetEffectiveScale(selection);
            LogScaleLevel(level, selection, weightedRandom: false, resetReason: null);
            return true;
        }

        private int ResolvePracticeLevel()
        {
            if (ChildLevel > 0)
                return ChildLevel;
            return SessionPreferences.Get("ChildPractice.Level", 0);
        }

        private void LogScaleLevel(int level, string activeScale, bool weightedRandom, string? resetReason)
        {
#if DEBUG
            var allowed = level > 0
                ? string.Join(",", ChildLevelProgression.GetAllowedScalesForLevel(level))
                : "n/a";
            var allowedCheck = level <= 0
                || ChildLevelProgression.IsScaleAllowedAtLevel(level, activeScale);
            var selection = ScaleSelectionMode switch
            {
                ScaleSelectionMode.ByLevel => "ByLevel",
                ScaleSelectionMode.Random => "Random",
                _ => SelectedScale
            };
            var reset = resetReason == null
                ? string.Empty
                : $" SelectionResetTo=ByLevel Reason={resetReason}";
            DebugLog.WriteLine(
                $"[ScaleLevel] Level={level} Selection={selection} ActiveScale={activeScale} " +
                $"Allowed={allowed} AllowedCheck={allowedCheck} WeightedRandom={weightedRandom}{reset} OK");
#endif
        }

        private static ScaleSelectionMode ParseScaleSelectionMode(string? raw)
            => Enum.TryParse<ScaleSelectionMode>(raw, out var mode)
                ? mode
                : ScaleSelectionMode.ByLevel;
        /// <summary>Tempo (BPM) for score marking, rhythm gates, and phone autoplay.</summary>
        public int Tempo
        {
            get => _tempo;
            set => ApplyTempo(value);
        }

        /// <summary>Alias for <see cref="Tempo"/> (score marking).</summary>
        public int MusicBpm
        {
            get => _tempo;
            set => ApplyTempo(value);
        }

        /// <summary>Alias for <see cref="Tempo"/> (phone autoplay).</summary>
        public int PlaybackBpm
        {
            get => _tempo;
            set => ApplyTempo(value);
        }

        private void ApplyTempo(int value)
        {
            var clamped = Math.Clamp(value, MinTempo, MaxTempo);
            if (_tempo == clamped)
                return;

            _tempo = clamped;
            SessionPreferences.Set(PrefMusicBpmKey, _tempo);
            SessionPreferences.Set(PrefPlaybackBpmKey, _tempo);
            OnPropertyChanged(nameof(Tempo));
            OnPropertyChanged(nameof(MusicBpm));
            OnPropertyChanged(nameof(PlaybackBpm));
        }
        /// <summary>
        /// Pool weights (sum 100): practice tunes, random, scales, arpeggios.
        /// Used by <see cref="PracticeCompositionSelector"/> for Child / By Level / mixed practice.
        /// </summary>
        public int PcTunes
        {
            get => _pcTunes;
            set => SetSinglePracticeCompositionPercent(ref _pcTunes, PrefPcTunesKey, value, nameof(PcTunes));
        }
        public int PcRandom
        {
            get => _pcRandom;
            set => SetSinglePracticeCompositionPercent(ref _pcRandom, PrefPcRandomKey, value, nameof(PcRandom));
        }
        public int PcScales
        {
            get => _pcScales;
            set => SetSinglePracticeCompositionPercent(ref _pcScales, PrefPcScalesKey, value, nameof(PcScales));
        }
        public int PcArpeggios
        {
            get => _pcArpeggios;
            set => SetSinglePracticeCompositionPercent(ref _pcArpeggios, PrefPcArpeggiosKey, value, nameof(PcArpeggios));
        }
        public void SetPracticeCompositionPercents(int tunes, int random, int scales, int arpeggios)
        {
            tunes = Math.Clamp(tunes, 0, 100);
            random = Math.Clamp(random, 0, 100);
            scales = Math.Clamp(scales, 0, 100);
            arpeggios = Math.Clamp(arpeggios, 0, 100);
            if (tunes + random + scales + arpeggios != 100)
                return;

            _pcTunes = tunes;
            _pcRandom = random;
            _pcScales = scales;
            _pcArpeggios = arpeggios;
            SessionPreferences.Set(PrefPcTunesKey, _pcTunes);
            SessionPreferences.Set(PrefPcRandomKey, _pcRandom);
            SessionPreferences.Set(PrefPcScalesKey, _pcScales);
            SessionPreferences.Set(PrefPcArpeggiosKey, _pcArpeggios);
            OnPropertyChanged(nameof(PcTunes));
            OnPropertyChanged(nameof(PcRandom));
            OnPropertyChanged(nameof(PcScales));
            OnPropertyChanged(nameof(PcArpeggios));
        }
        /// <summary>
        /// Redistributes the three unchanged categories so all four values sum to 100,
        /// preserving their relative proportions.
        /// </summary>
        public static int[] RedistributePracticeComposition(int[] current, int changedIndex, int newValue)
        {
            if (current.Length != 4)
                throw new ArgumentException("Expected four composition percentages.", nameof(current));
            if (changedIndex < 0 || changedIndex > 3)
                throw new ArgumentOutOfRangeException(nameof(changedIndex));

            newValue = Math.Clamp(newValue, 0, 100);
            var result = new int[4];
            result[changedIndex] = newValue;

            int remainder = 100 - newValue;
            var otherIndices = new int[3];
            int o = 0;
            for (int i = 0; i < 4; i++)
                if (i != changedIndex)
                    otherIndices[o++] = i;

            if (remainder <= 0)
            {
                for (int i = 0; i < 3; i++)
                    result[otherIndices[i]] = 0;
                return result;
            }

            int sumOthers = otherIndices.Sum(i => current[i]);
            if (sumOthers == 0)
            {
                int each = remainder / 3;
                int extra = remainder % 3;
                for (int j = 0; j < 3; j++)
                    result[otherIndices[j]] = each + (j < extra ? 1 : 0);
                return result;
            }

            int assigned = 0;
            for (int j = 0; j < 2; j++)
            {
                result[otherIndices[j]] = (int)Math.Round(
                    remainder * (current[otherIndices[j]] / (double)sumOthers));
                assigned += result[otherIndices[j]];
            }

            result[otherIndices[2]] = remainder - assigned;
            return result;
        }
                public void ResetPracticeCompositionDefaults()
        {
            SetPracticeCompositionPercents(
                DefaultPcTunes, DefaultPcRandom, DefaultPcScales, DefaultPcArpeggios);
        }
        private void SetSinglePracticeCompositionPercent(
            ref int field, string prefKey, int value, string propertyName)
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (field == clamped)
                return;
            field = clamped;
            SessionPreferences.Set(prefKey, field);
            OnPropertyChanged(propertyName);
        }
        public int Tolerance
        {
            get => _tolerance;
            set
            {
                var clamped = Math.Clamp(value, 0, 100);
                if (_tolerance == clamped)
                {
                    return;
                }
                _tolerance = clamped;
                SessionPreferences.Set(PrefToleranceKey, _tolerance);
                OnPropertyChanged(nameof(Tolerance));
            }
        }
        public bool AutoStart
        {
            get => _autoStart;
            set
            {
                if (_autoStart == value)
                {
                    return;
                }
                _autoStart = value;
                SessionPreferences.Set(PrefAutoStartKey, value);
                OnPropertyChanged(nameof(AutoStart));
            }
        }
        //public bool ShowConductorCues
        //{
        //    get => _showConductorCues;
        //    set
        //    {
        //        if (_showConductorCues == value)
        //        {
        //            return;
        //        }
        //        _showConductorCues = value;
        //        SessionPreferences.Set(PrefShowConductorCuesKey, value);
        //        OnPropertyChanged(nameof(ShowConductorCues));
        //    }
        //}
        public bool AutoRepeat
        {
            get => _autoRepeat;
            set
            {
                if (_autoRepeat == value)
                {
                    return;
                }
                _autoRepeat = value;
                SessionPreferences.Set(PrefAutoRepeatKey, value);
                OnPropertyChanged(nameof(AutoRepeat));
            }
        }
        public bool RepeatSameTune
        {
            get => _repeatSameTune;
            set
            {
                if (_repeatSameTune == value)
                {
                    return;
                }
                _repeatSameTune = value;
                SessionPreferences.Set(PrefRepeatSameTuneKey, value);
                OnPropertyChanged(nameof(RepeatSameTune));
            }
        }

        private float _rmsThreshold = 0.025f;
        public const float DefaultRmsThreshold = 0.025f;
        public float RmsThreshold
        {
            get => _rmsThreshold;
            set
            {
                var clamped = Math.Clamp(value, 0f, 0.1f);
                if (Math.Abs(_rmsThreshold - clamped) < 0.0001f)
                {
                    return;
                }
                _rmsThreshold = clamped;
                OnPropertyChanged(nameof(RmsThreshold));
            }
        }
        private int _cooldownMs = 50;
        public const int DefaultCooldownMs = 50;
        public int CooldownMs
        {
            get => _cooldownMs;
            set
            {
                // Limit cooldown to 300ms to support fast-tempo playback scenarios
                var clamped = Math.Clamp(value, 0, 300);
                if (_cooldownMs == clamped)
                {
                    return;
                }
                _cooldownMs = clamped;
                OnPropertyChanged(nameof(CooldownMs));
            }
        }
        /// <summary>Restores advanced pitch-detection settings to factory defaults.</summary>
        public void ResetAdvancedDetectionDefaults()
        {
            Tolerance = DefaultTolerance;
            RmsThreshold = DefaultRmsThreshold;
            CooldownMs = DefaultCooldownMs;
            PitchOffsetCents = DefaultPitchOffsetCents;
        }
        public ObservableCollection<FeedbackItem> FeedbackViewModels { get; } = new();
        public readonly List<NoteInfo> NotesToDraw = new();
        public int CurrentNoteIndex { get; private set; }
        public readonly HashSet<int> CorrectNoteIndices = new();
        /// <summary>
        /// The practice tune currently loaded into <see cref="NotesToDraw"/>.
        /// Null when the active mode is not "Practice Tune".
        /// </summary>
        public PracticeTune? CurrentTune { get; private set; }
        /// <summary>
        /// X-positions (in the same coordinate space as <see cref="NoteInfo.X"/>) at which
        /// bar lines should be drawn between measures.  Populated by
        /// <see cref="GenerateNotesAsync"/> when a <see cref="PracticeTune"/> is loaded.
        /// Empty for all other modes.
        /// </summary>
        public readonly List<float> MeasureBarXPositions = new();

        /// <summary>
        /// X-positions of rest slots in the current practice tune.
        /// Each entry holds the source X (same space as <see cref="NoteInfo.X"/>)
        /// for use by the drawing layer to render rest symbols.
        /// Empty for all other modes.
        /// </summary>
        public readonly List<float> RestXPositions = new();
        /// <summary>Duration for each rest in <see cref="RestXPositions"/> (parallel list).</summary>
        public readonly List<NoteDuration> RestDurations = new();
        public readonly Dictionary<int, (int Wrong, int Cents)> NoteFeedbacks = new();
        public DateTime IgnoreAudioUntilUtc { get; private set; } = DateTime.MinValue;
        private int? _lockedPitchClassAfterAdvance;
        /// <summary>
        /// When two consecutive practice-tune notes share the same pitch class, require
        /// a silence gap between them so the sustained audio from the first note cannot
        /// immediately trigger the second.
        /// </summary>
        private bool _requireSilenceBeforeNote;
        // Sustain/rest earliest-start gate (uses MusicBpm as written tempo)
        private bool _rhythmStartGateEnabled;
        private int _rhythmGateMusicBpm;
        private double _rhythmGateUntilMs;
        private double _rhythmGateStartMs;
        private int _rhythmGateAcceptedIdx = -1;
        private double _rhythmGatePriorDurationMs;
        private double _lastRestViolationLogMs = double.NegativeInfinity;
        private enum AccidentalPreference { Auto, Sharps, Flats }
        public const string ScaleSelectionByLevel = "By Level";
        public const string ScaleSelectionRandom = "Random";

        public static readonly string[] AvailableScales = new[]
        {
            "Major",  "Harmonic Minor", "Melodic Minor", "Natural Minor", "Dorian", "Phrygian",
            "Lydian", "Mixolydian", "Locrian", "Major Pentatonic", "Minor Pentatonic", "Blues",
            "Enigmatic", "Chromatic"
        };

        /// <summary>Scale picker items: named scales only (By Level and Random live under Other).</summary>
        public static string[] ScalePickerOptions { get; } = AvailableScales.ToArray();

        public static bool IsNamedScaleOption(string? option)
            => !string.IsNullOrWhiteSpace(option)
               && option != ScaleSelectionByLevel
               && option != ScaleSelectionRandom
               && AvailableScales.Contains(option, StringComparer.Ordinal);

        public ScaleSelectionMode ScaleSelectionMode
        {
            get => _scaleSelectionMode;
            set
            {
                if (_scaleSelectionMode == value)
                    return;
                _scaleSelectionMode = value;
                SessionPreferences.Set(PrefScaleSelectionModeKey, value.ToString());
                OnPropertyChanged(nameof(ScaleSelectionMode));
                OnPropertyChanged(nameof(EffectiveScaleDisplay));
            }
        }

        public string ScaleSelectionDisplay => ScaleSelectionMode switch
        {
            ScaleSelectionMode.ByLevel => ScaleSelectionByLevel,
            ScaleSelectionMode.Random => ScaleSelectionRandom,
            _ => SelectedScale
        };
        public string[] AvailableScalesForBinding => AvailableScales;
        private static readonly char[] Letters = ['A', 'B', 'C', 'D', 'E', 'F', 'G'];
        private static readonly int[] HarmonicMinorUp = new[] { 0, 2, 3, 5, 7, 8, 11, 12 };
        private static readonly int[] NaturalMinorUp = new[] { 0, 2, 3, 5, 7, 8, 10, 12 };
        private static readonly int[] HarmonicMajorUp = new[] { 0, 2, 4, 5, 7, 8, 11, 12 };
        private static readonly int[] PhrygianDominantUp = new[] { 0, 1, 4, 5, 7, 8, 10, 12 };
        private static readonly int[] DoubleHarmonicUp = new[] { 0, 1, 4, 5, 7, 8, 11, 12 };
        private static readonly int[] NeapolitanMinorUp = new[] { 0, 1, 3, 5, 7, 8, 11, 12 };
        private static readonly int[] NeapolitanMajorUp = new[] { 0, 1, 3, 5, 7, 9, 11, 12 };
        // Timing: onset-based linear regression (least-squares fit)
        private readonly Stopwatch _sessionStopwatch = new();
        private readonly List<(double OnsetMs, double ExpectedBeat)> _onsetData = new();
        private double? _timingAccuracyPercent;
        /// <summary>Detected tempo (BPM) from the user's performance this session.</summary>
        private int? _detectedBpm;
        /// <summary>
        /// Detected tempo in beats per minute from the user's playing this session.
        /// Null until <see cref="FinalizeSessionStats"/> runs or when detection is unavailable.
        /// </summary>
        public int? DetectedBpm => _detectedBpm;
        public string? Tune
        {
            get => _tune;
            set
            {
                if (value != null &&_tune != value)
                {
                    var leavingPracticeTune = _tune == "Practice Tune" && value != "Practice Tune";
                    _tune = value;
                    SessionPreferences.Set(PrefTuneKey, value);
                    if (leavingPracticeTune && _keyBeforePracticeTune != null)
                    {
                        Key = _keyBeforePracticeTune;
                        _keyBeforePracticeTune = null;
#if DEBUG
                        DebugLog.WriteLine($"[PickerTest] LeavePracticeTune: restored Key={Key} Concert={GetConcertKey()}");
#endif
                    }
                    OnPropertyChanged(nameof(Tune));
                }
            }
        }
        public string SelectedArpeggioId
        {
            get => _selectedArpeggioId;
            set
            {
                if (string.IsNullOrWhiteSpace(value) || _selectedArpeggioId == value) return;
                _selectedArpeggioId = value;
                SessionPreferences.Set(PrefSelectedArpeggioIdKey, _selectedArpeggioId);
                OnPropertyChanged(nameof(SelectedArpeggioId));
            }
        }
        public string SelectedArpeggioRoot
        {
            get => _selectedArpeggioRoot;
            set
            {
                if (string.IsNullOrWhiteSpace(value) || _selectedArpeggioRoot == value) return;
                _selectedArpeggioRoot = value;
                SessionPreferences.Set(PrefSelectedArpeggioRootKey, _selectedArpeggioRoot);
                OnPropertyChanged(nameof(SelectedArpeggioRoot));
            }
        }
        public string SelectedArpeggioDisplay
        {
            get => _selectedArpeggioDisplay;
            set
            {
                if (string.IsNullOrWhiteSpace(value) || _selectedArpeggioDisplay == value) return;
                _selectedArpeggioDisplay = value;
                SessionPreferences.Set(PrefSelectedArpeggioDisplayKey, _selectedArpeggioDisplay);
                OnPropertyChanged(nameof(SelectedArpeggioDisplay));
            }
        }
        public void SelectArpeggio(ArpeggioPattern pattern, string rootNote, string displayName)
        {
            SelectedArpeggioId = pattern.Id;
            SelectedArpeggioRoot = rootNote;
            SelectedArpeggioDisplay = displayName;
            Tune = "Arpeggio";
        }
        private readonly Queue<double> _pitchMedianHistory = new();
        public double SmoothPitch(double freq)
        {
            if (SmoothingWindowSize <= 1)
                return freq;

            _pitchMedianHistory.Enqueue(freq);
            while (_pitchMedianHistory.Count > SmoothingWindowSize)
                _pitchMedianHistory.Dequeue();

            if (_pitchMedianHistory.Count < SmoothingWindowSize)
                return freq;

            var sorted = _pitchMedianHistory.Order().ToArray();
            return sorted[sorted.Length / 2];
        }
        /// <summary>Restores key, scale, and staff layout saved with Repeat Same.</summary>
        public void RestoreRepeatSameGenerationContext(
            string key,
            string selectedScale,
            string effectiveScale,
            ScaleSelectionMode scaleMode,
            bool isRandomMode,
            string tune)
        {
            Key = key;
            SelectedScale = selectedScale;
            ScaleSelectionMode = scaleMode;
            IsRandomMode = isRandomMode;
            Tune = tune;
            SetEffectiveScale(effectiveScale);
        }

        public void Reset()
        {
            SessionCompleted = false;
            FeedbackViewModels.Clear();
            NotesToDraw.Clear();
            CorrectNoteIndices.Clear();
            NoteFeedbacks.Clear();
            CurrentNoteIndex = 0;
            IgnoreAudioUntilUtc = DateTime.MinValue;
            _lockedPitchClassAfterAdvance = null;
            _requireSilenceBeforeNote = false;
            _rhythmStartGateEnabled = false;
            _rhythmGateMusicBpm = 0;
            _rhythmGateUntilMs = 0;
            _rhythmGateStartMs = 0;
            _rhythmGateAcceptedIdx = -1;
            _rhythmGatePriorDurationMs = 0;
            _lastRestViolationLogMs = double.NegativeInfinity;
            _pitchMedianHistory.Clear();
            TimingDiagnostics.ResetSession();

            // Clear timing data and stats
            _onsetData.Clear();
            _timingAccuracyPercent = null;
            _detectedBpm = null;
            _lastCorrectNoteUtc = null;
            OmitMsAvgThreshold = SessionPreferences.Get(PrefOmitMsAvgThresholdKey, MasteryPreferenceDefaults.OmitMsAvgThreshold);
            _lastWrongTimePerIndex.Clear();
            _lastRandomWrongUtc.Clear();
            _sessionNoteStats.Clear();
            ClearSessionAttemptOutcomes();
            _sessionStreaks.Clear();
            _tunerPrevWrittenMidi = null;// reset direction tracking for next session
            // ensure persisted value is reloaded
            _wrongDebounceMs = SessionPreferences.Get(PrefWrongDebounceMsKey, DefaultDebounceMs);
            _sessionStopwatch.Reset();
        }
        /// <summary>
        /// Starts the session clock. Call when the microphone is live (after tune setup).
        /// </summary>
        public void StartListeningClock()
        {
            _sessionStopwatch.Restart();
        }
        /// <summary>
        /// Enables sustain/rest earliest-start gating using <see cref="MusicBpm"/>.
        /// </summary>
        public void ConfigureRhythmStartGates()
        {
            _rhythmGateUntilMs = 0;
            if (NotesToDraw.Count == 0)
            {
                _rhythmStartGateEnabled = false;
                _rhythmGateMusicBpm = 0;
                return;
            }

            _rhythmGateMusicBpm = Math.Clamp(MusicBpm, MinTempo, MaxTempo);
            _rhythmStartGateEnabled = false;
            for (int i = 1; i < NotesToDraw.Count; i++)
            {
                if (RhythmStartGate.HasRestGapAfter(
                        NotesToDraw[i].GateBeatsAfterPrevious,
                        NotesToDraw[i - 1].DurationBeats))
                {
                    _rhythmStartGateEnabled = true;
                    break;
                }
            }
        }
        private double GetSessionElapsedMs()
            => _sessionStopwatch.Elapsed.TotalMilliseconds;
        private double BeatToGateMs(double beats)
            => beats * 60000.0 / _rhythmGateMusicBpm;
        private void ArmRhythmGateAfterAdvance(int acceptedIdx)
        {
            if (!_rhythmStartGateEnabled)
                return;

            int nextIdx = acceptedIdx + 1;
            if (nextIdx >= NotesToDraw.Count)
            {
                _rhythmGateUntilMs = 0;
                return;
            }

            double gateBeats = NotesToDraw[nextIdx].GateBeatsAfterPrevious;
            double priorDurationBeats = NotesToDraw[acceptedIdx].DurationBeats;
            double restBeats = RhythmStartGate.RestGateBeatsAfterPrevious(gateBeats, priorDurationBeats);
            if (restBeats <= 0)
            {
                _rhythmGateUntilMs = 0;
                return;
            }

            double nowMs = GetSessionElapsedMs();
            _rhythmGateStartMs = nowMs;
            _rhythmGateAcceptedIdx = acceptedIdx;
            _rhythmGatePriorDurationMs = 0;
            _rhythmGateUntilMs = nowMs + BeatToGateMs(restBeats);
            _lastRestViolationLogMs = double.NegativeInfinity;
        }
        private bool IsRhythmGateBlocking()
            => _rhythmStartGateEnabled && _rhythmGateUntilMs > 0
               && GetSessionElapsedMs() < _rhythmGateUntilMs;
        private void ClearRhythmGateIfExpired()
        {
            if (_rhythmGateUntilMs > 0 && GetSessionElapsedMs() >= _rhythmGateUntilMs)
                _rhythmGateUntilMs = 0;
        }
        private bool TryMarkDebouncedWrong(int idx, (int Wrong, int Cents) curFeedback, int cents)
        {
            var nowTrailing = DateTime.UtcNow;
            if (_lastWrongTimePerIndex.TryGetValue(idx, out var lastTrailing)
                && (nowTrailing - lastTrailing).TotalMilliseconds < _wrongDebounceMs)
                return false;

            _lastWrongTimePerIndex[idx] = nowTrailing;
            var updated = (Wrong: curFeedback.Wrong + 1, Cents: cents);
            NoteFeedbacks[idx] = updated;
            FeedbackViewModels[idx] = new FeedbackItem(idx, updated.Wrong, updated.Cents, false);
            return true;
        }
        private static string FormatDurationName(NoteDuration? duration)
            => duration?.ToString() ?? "Quarter";
        private static string BeatsToDurationLabel(double beats)
        {
            if (beats >= 3.5) return NoteDuration.Whole.ToString();
            if (beats >= 1.5) return NoteDuration.Half.ToString();
            if (beats >= 0.75) return NoteDuration.Quarter.ToString();
            if (beats >= 0.35) return NoteDuration.Eighth.ToString();
            return NoteDuration.Sixteenth.ToString();
        }
        private NoteAttemptOutcome BuildNoteOutcome(
            NoteInfo targetNote,
            string heardNote,
            int cents,
            bool pitchCorrect,
            bool? timingCorrect,
            string reason,
            double? actualMs = null,
            double? expectedStartMs = null,
            double? timingErrorMs = null,
            double timingToleranceMs = 0)
        {
            return new NoteAttemptOutcome
            {
                ExpectedWrittenNoteName = targetNote.Name,
                ActualDetectedNoteName = heardNote is "-" or "" ? null : heardNote,
                IsRest = targetNote.IsRest,
                ExpectedDuration = FormatDurationName(targetNote.Duration),
                ExpectedBeat = targetNote.StartBeat,
                ExpectedStartMs = expectedStartMs,
                ActualDetectedMs = actualMs,
                TimingErrorMs = timingErrorMs,
                TimingToleranceMs = timingToleranceMs,
                PitchCorrect = pitchCorrect,
                TimingCorrect = timingCorrect,
                OverallCorrect = ComputeOverallCorrect(pitchCorrect, timingCorrect),
                WrongReason = reason,
                PitchErrorCents = cents,
                MidiNumber = targetNote.Midi,
            };
        }
        private void RecordRestViolation(string heardNote, double actualMs)
        {
            if (_rhythmGateAcceptedIdx < 0 || _rhythmGateAcceptedIdx >= NotesToDraw.Count)
                return;

            const double restLogDebounceMs = 250;
            if (actualMs - _lastRestViolationLogMs < restLogDebounceMs)
                return;
            _lastRestViolationLogMs = actualMs;

            var prior = NotesToDraw[_rhythmGateAcceptedIdx];
            double restStartBeat = prior.StartBeat + prior.DurationBeats;
            int nextIdx = Math.Min(_rhythmGateAcceptedIdx + 1, NotesToDraw.Count - 1);
            double gateBeats = NotesToDraw[nextIdx].GateBeatsAfterPrevious;
            double restBeats = Math.Max(0, gateBeats - prior.DurationBeats);
            double restStartMs = _rhythmGateStartMs + _rhythmGatePriorDurationMs;

            RecordAttemptOutcome(new NoteAttemptOutcome
            {
                IsRest = true,
                ExpectedWrittenNoteName = "REST",
                ExpectedDuration = BeatsToDurationLabel(restBeats),
                ExpectedBeat = restStartBeat,
                ExpectedStartMs = restStartMs,
                ActualDetectedNoteName = heardNote is "-" or "" ? null : heardNote,
                ActualDetectedMs = actualMs,
                PitchCorrect = false,
                TimingCorrect = false,
                OverallCorrect = false,
                WrongReason = "SoundDuringRest",
            });

            TimingDiagnostics.EnqueueRestTimingWrong(new RestTimingWrongPayload
            {
                RestDurationName = BeatsToDurationLabel(restBeats),
                RestStartBeat = restStartBeat,
                RestStartMs = restStartMs,
                ActualName = heardNote,
                ActualMs = actualMs,
                Reason = "SoundDuringRest",
            });
        }
        private void TryEnqueueTimingWrong(
            NoteInfo targetNote,
            string heardNote,
            double actualMs,
            string reason,
            bool pitchCorrect,
            bool timingCorrect)
        {
            double expectedMs = _rhythmGateUntilMs;
            double errorMs = actualMs - expectedMs;
            bool overallCorrect = ComputeOverallCorrect(pitchCorrect, timingCorrect);

            TimingDiagnostics.EnqueueTimingWrong(new TimingWrongPayload
            {
                ExpectedName = targetNote.Name,
                DurationName = FormatDurationName(targetNote.Duration),
                ExpectedBeat = targetNote.StartBeat,
                ExpectedMs = expectedMs,
                ActualName = heardNote,
                ActualMs = actualMs,
                ErrorMs = errorMs,
                ToleranceMs = 0,
                PitchCorrect = pitchCorrect,
                TimingCorrect = timingCorrect,
                OverallCorrect = overallCorrect,
                Reason = reason,
            });
        }
        /// <summary>
        /// Stop the current session gracefully: mark completed, stop timing,
        /// and clear any short-term ignore state so the app can perform cleanup.
        /// Safe to call from any thread.
        /// </summary>
        public void StopSession()
        {
            try
            {
                SessionCompleted = true;
                IgnoreAudioUntilUtc = DateTime.MinValue;
                if (_sessionStopwatch.IsRunning)
                {
                    _sessionStopwatch.Stop();
                }

                TimingDiagnostics.Flush();
                TimingDiagnostics.WriteSessionSummary();
            }
            catch
            {
                // Swallow exceptions to keep stop operation best-effort
            }
        }
        /// <summary>
        /// Records the onset time and expected beat position for the note that was just
        /// played correctly. Called from UpdateFeedbackForCurrent when a note advances.
        /// </summary>
        private void RecordOnsetIfNeeded(int noteIndex)
        {
            if (!_sessionStopwatch.IsRunning || noteIndex >= NotesToDraw.Count)
                return;

            double onsetMs = _sessionStopwatch.Elapsed.TotalMilliseconds;
            double expectedBeat = CalculateExpectedBeatPosition(noteIndex);
            _onsetData.Add((onsetMs, expectedBeat));
        }

        /// <summary>
        /// Calculates the expected beat position for a note based on the sum of
        /// all note durations (including rests) up to that index.
        /// Whole note = 4 beats, Half = 2, Quarter = 1, Eighth = 0.5, Sixteenth = 0.25.
        /// </summary>
        private double CalculateExpectedBeatPosition(int noteIndex)
        {
            double beatPosition = 0.0;
            for (int i = 0; i < noteIndex && i < NotesToDraw.Count; i++)
            {
                var note = NotesToDraw[i];
                if (note.Duration.HasValue)
                    beatPosition += note.Duration.Value.ToBeatValue();
                else
                    beatPosition += 1.0; // Default to quarter note for scale/random mode
            }
            return beatPosition;
        }
        /// <summary>
        /// Computes timing accuracy using least-squares linear regression.
        /// Fits ActualOnsetTimeMs = StartOffsetMs + MsPerBeat * ExpectedBeatStart
        /// and scores each note based on its timing error relative to adaptive thresholds.
        /// </summary>
        public void FinalizeSessionStats()
        {
            _detectedBpm = ComputeDetectedBpmFromOnsets();

            // Need at least 3 notes for meaningful linear regression
            if (_onsetData.Count < 3)
            {
                _timingAccuracyPercent = null;
                NotifyTimingStatsChanged();
                TimingDiagnostics.Flush();
                TimingDiagnostics.WriteSessionSummary();
                return;
            }

            // Check for zero variance in expected beats (would cause divide-by-zero)
            var beatValues = _onsetData.Select(d => d.ExpectedBeat).ToArray();
            if (beatValues.Distinct().Count() < 2)
            {
                _timingAccuracyPercent = null;
                NotifyTimingStatsChanged();
                TimingDiagnostics.Flush();
                TimingDiagnostics.WriteSessionSummary();
                return;
            }

            // Least-squares linear regression: y = mx + b
            // y = ActualOnsetMs, x = ExpectedBeat
            int n = _onsetData.Count;
            double sumX = _onsetData.Sum(d => d.ExpectedBeat);
            double sumY = _onsetData.Sum(d => d.OnsetMs);
            double sumXY = _onsetData.Sum(d => d.ExpectedBeat * d.OnsetMs);
            double sumX2 = _onsetData.Sum(d => d.ExpectedBeat * d.ExpectedBeat);

            // Slope (MsPerBeat) and intercept (StartOffsetMs)
            double msPerBeat = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
            double startOffsetMs = (sumY - msPerBeat * sumX) / n;

            // Compute timing score for each note
            var noteScores = new List<double>();
            double sixteenthMs = msPerBeat * 0.25;
            double goodThresholdMs = sixteenthMs * 0.25;
            double badThresholdMs = sixteenthMs * 1.00;

            foreach (var (onsetMs, expectedBeat) in _onsetData)
            {
                double expectedFittedMs = startOffsetMs + msPerBeat * expectedBeat;
                double timingErrorMs = Math.Abs(onsetMs - expectedFittedMs);

                double noteScore;
                if (timingErrorMs <= goodThresholdMs)
                    noteScore = 100.0;
                else if (timingErrorMs >= badThresholdMs)
                    noteScore = 0.0;
                else
                    noteScore = 100.0 * (1.0 - (timingErrorMs - goodThresholdMs) / (badThresholdMs - goodThresholdMs));

                noteScores.Add(noteScore);
            }

            _timingAccuracyPercent = noteScores.Average();
            NotifyTimingStatsChanged();
            TimingDiagnostics.Flush();
            TimingDiagnostics.WriteSessionSummary();
        }
        /// <summary>
        /// Returns timing accuracy percentage from least-squares onset fitting.
        /// Null when fewer than 3 notes were played or expected beats have no variance.
        /// </summary>
        public double? GetTimingAccuracyPercent() => _timingAccuracyPercent;
        /// <summary>
        /// Detected tempo (beats per minute) from consecutive user onsets, with IQR outlier removal.
        /// Each interval uses written beat spacing: BPM = 60000 × Δbeats / Δms.
        /// Returns null when fewer than 2 onsets or no valid intervals remain after filtering.
        /// </summary>
        public int? GetDetectedBpm() => ComputeDetectedBpmFromOnsets();
        private int? ComputeDetectedBpmFromOnsets()
        {
            if (_onsetData.Count < 2)
                return null;

            var sorted = _onsetData.OrderBy(d => d.ExpectedBeat).ToList();
            var bpms = new List<double>();

            for (int i = 1; i < sorted.Count; i++)
            {
                double beatDelta = sorted[i].ExpectedBeat - sorted[i - 1].ExpectedBeat;
                double msDelta = sorted[i].OnsetMs - sorted[i - 1].OnsetMs;
                if (beatDelta <= 1e-6 || msDelta < 50)
                    continue;

                double bpm = 60000.0 * beatDelta / msDelta;
                if (bpm >= 30 && bpm <= 250)
                    bpms.Add(bpm);
            }

            if (bpms.Count == 0)
                return null;

            var filtered = FilterOutliersIqr(bpms);
            if (filtered.Count == 0)
                return null;

            return (int)Math.Round(filtered.Average());
        }
        private static List<double> FilterOutliersIqr(List<double> values)
        {
            if (values.Count < 4)
                return values;

            var sorted = values.OrderBy(v => v).ToArray();
            double q1 = Percentile(sorted, 0.25);
            double q3 = Percentile(sorted, 0.75);
            double iqr = q3 - q1;
            double lo = q1 - 1.5 * iqr;
            double hi = q3 + 1.5 * iqr;
            return values.Where(v => v >= lo && v <= hi).ToList();
        }
        private static double Percentile(double[] sorted, double p)
        {
            double pos = p * (sorted.Length - 1);
            int lo = (int)Math.Floor(pos);
            int hi = (int)Math.Ceiling(pos);
            if (lo == hi)
                return sorted[lo];
            return sorted[lo] + (pos - lo) * (sorted[hi] - sorted[lo]);
        }
        public bool UpdateFeedbackForCurrent(double freq, (bool correct, int cents) result)
        {

            if (Tune == null)
            {
                StatusService.Instance.StatusMessage = "UpdateFeedbackForCurrent: Tune is null";
                return false;
            }

            if (NotesToDraw.Count == 0 || freq <= 0)
            {
                return false;
            }

            string heardNote = "-";
            if (freq > 0)
            {
                var midi = (int)Math.Round(69 + 12 * Math.Log(freq / 440.0, 2));
                var detMidiWrit = midi - GetInstrumentTransposeOffset();
                heardNote = MidiToNoteName(detMidiWrit, KeyUsesFlats(Key));
            }
            string expectedNote = (CurrentNoteIndex < NotesToDraw.Count)
                ? ResolveWrittenEvaluationName(NotesToDraw[CurrentNoteIndex])
                : "-";

            // Only allow the current note in the sequence to be marked correct
            int idx = CurrentNoteIndex;
            if (idx >= NotesToDraw.Count)
                return false;

            // Bounds check to prevent race condition when collection is modified from another thread
            if (idx < 0 || idx >= FeedbackViewModels.Count)
            {
                Utils.Log($"[Feedback] Index {idx} out of bounds for FeedbackViewModels (Count={FeedbackViewModels.Count}). Session may have been reset.");
                return false;
            }

            var targetNote = NotesToDraw[idx];
            var expectedWrittenMidi = ResolveWrittenEvaluationMidi(targetNote);
            var detectedMidi = (int)Math.Round(69 + 12 * Math.Log(freq / 440.0, 2));
            var detMidiWritten = detectedMidi - GetInstrumentTransposeOffset();
            var detectedPcWritten = Mod12(detMidiWritten);

            // After a note advances, ignore tail detections that still match
            // the previous note's pitch class — they are residual audio, not
            // genuine wrong answers for the new target note.
            // Exception: if the current target has the same pitch class (consecutive
            // identical notes in Random mode), allow through — the cooldown already
            // debounces residual audio.
            if (_lockedPitchClassAfterAdvance.HasValue)
            {
                if (detectedPcWritten == _lockedPitchClassAfterAdvance.Value
                    && Mod12(expectedWrittenMidi) != _lockedPitchClassAfterAdvance.Value)
                {
                    return false;
                }
                _lockedPitchClassAfterAdvance = null;
            }

            ClearRhythmGateIfExpired();

            var curFeedback = NoteFeedbacks.TryGetValue(idx, out var v2) ? v2 : (Wrong: 0, Cents: 0);

            // Sustain/rest gate: block N+1 until prior note duration + rests have elapsed.
            if (IsRhythmGateBlocking())
            {
                double actualMs = GetSessionElapsedMs();
                double elapsedInGate = actualMs - _rhythmGateStartMs;
                bool inRestPhase = elapsedInGate >= _rhythmGatePriorDurationMs;

                if (inRestPhase)
                    RecordRestViolation(heardNote, actualMs);

                StatusService.Instance.StatusMessage =
                    $"Expected: {expectedNote}, Heard: {heardNote}, {result.cents}¢ (too early), Notes: {NotesToDraw.Count}";

                if (Mod12(expectedWrittenMidi) == detectedPcWritten)
                {
                    string reason = inRestPhase ? "Early" : "EarlyDuringSustain";
                    if (TryMarkDebouncedWrong(idx, curFeedback, result.cents))
                    {
                        var outcome = BuildNoteOutcome(
                            targetNote, heardNote, result.cents,
                            pitchCorrect: true, timingCorrect: false, reason: reason,
                            actualMs: actualMs, expectedStartMs: _rhythmGateUntilMs,
                            timingErrorMs: actualMs - _rhythmGateUntilMs);
                        RecordAttemptOutcome(outcome);
                        TryEnqueueTimingWrong(targetNote, heardNote, actualMs, reason,
                            pitchCorrect: true, timingCorrect: false);
                        return true;
                    }
                    return false;
                }

                return false;
            }

            // If a silence gap is required (consecutive same-pitch notes), block until
            // silence clears the flag via NotifySilence().
            if (_requireSilenceBeforeNote)
                return false;

            StatusService.Instance.StatusMessage = $"Expected: {expectedNote}, Heard: {heardNote}, {result.cents}¢, Notes: {NotesToDraw.Count}";

            // Only match if the detected pitch class matches the current note's pitch class
            if (Mod12(expectedWrittenMidi) != detectedPcWritten)
            {
                if (TryMarkDebouncedWrong(idx, curFeedback, result.cents))
                {
                    RecordAttemptOutcome(BuildNoteOutcome(
                        targetNote, heardNote, result.cents,
                        pitchCorrect: false, timingCorrect: true, reason: "WrongPitch",
                        actualMs: GetSessionElapsedMs()));
                    return true;
                }
                return false;
            }
            if (result.correct)
            {
                // Timing: record onset time and expected beat position
                RecordOnsetIfNeeded(idx);

                RecordAttemptOutcome(BuildNoteOutcome(
                    targetNote, heardNote, result.cents,
                    pitchCorrect: true, timingCorrect: true, reason: string.Empty,
                    actualMs: GetSessionElapsedMs(), expectedStartMs: targetNote.StartBeat > 0
                        ? null : GetSessionElapsedMs()));

                // Update feedback: update cents only on correct
                CorrectNoteIndices.Add(idx);
                FeedbackViewModels[idx] = new FeedbackItem(idx, curFeedback.Wrong, result.cents, true);

                // Lock advancement and move to next note
                _lockedPitchClassAfterAdvance = detectedPcWritten;

                // Clear smoothing history so the next note starts with fresh data
                _pitchMedianHistory.Clear();

                // Ignore further audio for a short period to debounce
                IgnoreAudioUntilUtc = DateTime.UtcNow.AddMilliseconds(CooldownMs);

                // Clear wrong-debounce for this index on correct
                _lastWrongTimePerIndex.Remove(idx);

                // Move CurrentNoteIndex to the next note (in order)
                CurrentNoteIndex = idx + 1;
                if (CurrentNoteIndex >= NotesToDraw.Count)
                {
                    CurrentNoteIndex = NotesToDraw.Count; // Stay at the end
                    _requireSilenceBeforeNote = false;
                    _rhythmGateUntilMs = 0;
                    FinalizeSessionStats();
                    _ = SessionCompletedAsync?.Invoke();
                }
                else
                {
                    ArmRhythmGateAfterAdvance(idx);
                    if (Tune == "Practice Tune"
                             && Mod12(ResolveWrittenEvaluationMidi(NotesToDraw[CurrentNoteIndex])) == detectedPcWritten)
                    {
                        // Next note has the same pitch class — require a silence gap so the
                        // sustained audio from this note cannot auto-trigger the next one.
                        _requireSilenceBeforeNote = true;
                    }
                }
                return true;
            }

            // Update feedback for incorrect attempt: only increment wrong, do not update cents
            curFeedback = (Wrong: curFeedback.Wrong + 1, Cents: curFeedback.Cents);
            NoteFeedbacks[idx] = curFeedback;
            FeedbackViewModels[idx] = new FeedbackItem(idx, curFeedback.Wrong, curFeedback.Cents, false);
            RecordAttemptOutcome(BuildNoteOutcome(
                targetNote, heardNote, result.cents,
                pitchCorrect: false, timingCorrect: true, reason: "WrongPitch",
                actualMs: GetSessionElapsedMs()));
            return true;
        }
        private async Task<string[]> BuildRandomSequenceAsync()
        {
            // Composition-driven exercise types are applied in PracticeCompositionSelector
            // before generation; this path handles random melodic note lists.
            var availableNotes = BuildAvailableNotesForCurrentInstrumentAndScale();

            if (availableNotes.Count < 2)
                return availableNotes.ToArray();

            if (UseNoteMasteryForGeneration)
            {
                // Get stats from database asynchronously
                var db = ServiceHelper.GetService<NoteDatabase>();
                if (db == null)
                {
                    Utils.Log("NoteDatabase service is not registered.");
                    return Array.Empty<string>();
                }
                await db.InitializeAsync();

                var statsList = await db.GetAllAsync();
                var stats = (statsList ?? Enumerable.Empty<NoteStat>())
                    .Where(s => !string.IsNullOrWhiteSpace(s.WrittenName))
                    .GroupBy(s => s.WrittenName)
                    .ToDictionary(g => g.Key, g => g.First());

                // Exclude mastered notes based on MasteredMethod
                availableNotes = availableNotes
                    .Where(note =>
                    {
                        if (!stats.TryGetValue(note, out var stat)) return true;
                        return !MasteryEvaluator.IsFullyMastered(stat, this);
                    })
                    .ToList();

                var fullPool = BuildAvailableNotesForCurrentInstrumentAndScale();

                // When mastery leaves too few candidates, keep the full pool so generation
                // does not collapse into a repeating two-note pattern (e.g. G–A–G–A).
                if (availableNotes.Count < MusicSequenceGenerator.MinPitchPoolAfterMasteryExclusion
                    && fullPool.Count >= 2)
                {
                    availableNotes = fullPool;
                }
                // If filtering removed all notes or left only one, fall back to weakest notes.
                else if (availableNotes.Count < 2)
                {
                    if (fullPool.Count >= 2)
                    {
                        availableNotes = fullPool
                            .OrderBy(note =>
                            {
                                if (stats.TryGetValue(note, out var s))
                                    return MasteredMethod == "Streak" ? s.Streak : (int)s.PercentOverallCorrect;
                                return 0;
                            })
                            .Take(Math.Max(2, fullPool.Count / 2))
                            .ToList();
                    }
                    else
                    {
                        availableNotes = fullPool;
                    }
                }
            }

            // Remove enharmonic boundary notes that would be out of range when respelled
            // (e.g. Cb4 if lowest is C4, or B#5 if highest is B5)
            string lowestNote = string.IsNullOrWhiteSpace(LowestNote) ? "E3" : LowestNote;
            string highestNote = string.IsNullOrWhiteSpace(HighestNote) ? "C6" : HighestNote;

            string flatOfLowest = "";
            if (lowestNote.Length > 1 && !lowestNote.Contains("#") && !lowestNote.Contains("b"))
                flatOfLowest = lowestNote[0] + "b" + lowestNote.Substring(1);

            string sharpOfHighest = "";
            if (highestNote.Length > 1 && !highestNote.Contains("#") && !highestNote.Contains("b"))
                sharpOfHighest = highestNote[0] + "#" + highestNote.Substring(1);

            availableNotes = availableNotes
                .Where(n => n != flatOfLowest && n != sharpOfHighest)
                .ToList();

            if (availableNotes.Count < 2)
                return availableNotes.ToArray();

            // Interval-weighted random ordering
            // Intervals 1–7 (index distance) get descending weights; farther notes fall back to unweighted pick
            var intervalWeights = new Dictionary<int, int>
            {
                [1] = 100, // 2nd
                [2] = 80,  // 3rd
                [3] = 60,  // 4th
                [4] = 40,  // 5th
                [5] = 20,  // 6th
                [6] = 10,  // 7th
                [7] = 5    // 8th (octave)
            };

            var rand = new Random();
            var result = new List<string>();
            var unused = Enumerable.Range(0, availableNotes.Count).ToList();

            int currentIdx = unused[rand.Next(unused.Count)];
            result.Add(availableNotes[currentIdx]);
            unused.Remove(currentIdx);

            while (unused.Count > 0)
            {
                // Build weighted candidates excluding any that are the same pitch class as the previous note
                int prevPc = NoteNameToMidi(result[^1]) % 12;
                var candidates = new List<(int idx, int weight)>();
                foreach (var nextIdx in unused)
                {
                    int interval = Math.Abs(nextIdx - currentIdx);
                    if (interval == 0) continue;
                    // Reject if same pitch class as previous note (catches octave duplicates)
                    if (NoteNameToMidi(availableNotes[nextIdx]) % 12 == prevPc) continue;
                    if (intervalWeights.TryGetValue(interval, out int weight))
                        candidates.Add((nextIdx, weight));
                }

                // Fallback: all unused notes that are not the same pitch class as previous
                var nonRepeatUnused = unused
                    .Where(i => NoteNameToMidi(availableNotes[i]) % 12 != prevPc)
                    .ToList();

                int chosenIdx;
                if (candidates.Count > 0)
                {
                    int totalWeight = candidates.Sum(c => c.weight);
                    int pick = rand.Next(totalWeight);
                    int acc = 0;
                    chosenIdx = candidates[0].idx;
                    foreach (var (idx, weight) in candidates)
                    {
                        acc += weight;
                        if (pick < acc)
                        {
                            chosenIdx = idx;
                            break;
                        }
                    }
                }
                else if (nonRepeatUnused.Count > 0)
                {
                    // No interval-weighted candidate found; pick any non-repeating note
                    chosenIdx = nonRepeatUnused[rand.Next(nonRepeatUnused.Count)];
                }
                else
                {
                    // Only one note remains and it is the same pitch — allow it (safe fallback)
                    chosenIdx = unused[rand.Next(unused.Count)];
                }

                result.Add(availableNotes[chosenIdx]);
                unused.Remove(chosenIdx);
                currentIdx = chosenIdx;
            }

            // --- Accidental logic ---
            // Build all non-scale MIDIs within the generated instrument note set as the accidental pool.
            var scaleMidis = new HashSet<int>(availableNotes.Select(n => NoteNameToMidi(n)));

            // Collect every chromatic pitch in range that is NOT a scale tone.
            var accidentalPool = new List<string>();
            if (StatusService.Instance.IsPremiumUser && AccidentalPercent > 0)
            {
                bool useFlats = KeyUsesFlats(Key);
                foreach (int midi in AvailableInstrumentMidis)
                {
                    if (scaleMidis.Contains(midi)) continue;
                    accidentalPool.Add(MidiToNoteName(midi, useFlats));
                }
            }

            if (StatusService.Instance.IsPremiumUser && AccidentalPercent > 0 && accidentalPool.Count > 0 && result.Count > 0)
            {
                int count = (int)Math.Round(result.Count * AccidentalPercent / 100.0);
                var indices = Enumerable.Range(0, result.Count).OrderBy(_ => rand.Next()).Take(count).ToList();

                foreach (int i in indices)
                {
                    // Determine which accidental notes would not repeat the adjacent notes (by pitch class)
                    int prevPc2 = i > 0 ? NoteNameToMidi(result[i - 1]) % 12 : -1;
                    int nextPc2 = i < result.Count - 1 ? NoteNameToMidi(result[i + 1]) % 12 : -1;

                    var validAccidentals = accidentalPool
                        .Where(n =>
                        {
                            int pc = NoteNameToMidi(n) % 12;
                            return pc != prevPc2 && pc != nextPc2;
                        })
                        .ToList();

                    if (validAccidentals.Count == 0)
                        validAccidentals = accidentalPool; // fallback: allow any accidental note

                    result[i] = validAccidentals[rand.Next(validAccidentals.Count)];
                }
            }

            // Final safety pass: enforce no-adjacent-repeat by pitch class.
            // Run up to two passes so a fix at position i doesn't create a new conflict at i+1.
            if (result.Count >= 2)
            {
                var allPool = availableNotes
                    .Concat(StatusService.Instance.IsPremiumUser && AccidentalPercent > 0 ? accidentalPool : Enumerable.Empty<string>())
                    .ToList();
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int i = 1; i < result.Count; i++)
                    {
                        if (NoteNameToMidi(result[i]) % 12 == NoteNameToMidi(result[i - 1]) % 12)
                        {
                            int prevPc3 = NoteNameToMidi(result[i - 1]) % 12;
                            int nextPc3 = i < result.Count - 1 ? NoteNameToMidi(result[i + 1]) % 12 : -1;
                            var options = allPool
                                .Where(n =>
                                {
                                    int pc = NoteNameToMidi(n) % 12;
                                    return pc != prevPc3 && pc != nextPc3;
                                })
                                .ToList();
                            if (options.Count > 0)
                                result[i] = options[rand.Next(options.Count)];
                        }
                    }
                }
            }

            return result.ToArray();
        }
        private List<string> BuildAvailableNotesForCurrentInstrumentAndScale()
        {
            var scaleDegrees = BuildScaleDegrees(Key, GenerationScale);
            return AvailableInstrumentMidis
                .Select(midi => MidiToNoteName(midi, KeyUsesFlats(Key)))
                .Where(noteName => noteName.Length > 0 && scaleDegrees
                    .Contains(noteName.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9')))
                .Distinct()
                .ToList();
        }
        /// <summary>
        /// Builds normal <see cref="GeneratedNote"/> objects from the arpeggio catalog
        /// without wiring arpeggios into Random weighting.
        /// </summary>
        public List<GeneratedNote> BuildArpeggioNotes(
            ArpeggioPattern? pattern = null,
            string? rootNote = null,
            bool descendingAfterAscending = true)
        {
            pattern ??= ArpeggioCatalog.MajorTriad;
            // SelectedArpeggioRoot is the concert-pitch name from the picker; Key is already written.
            string writtenRootNote;
            if (string.IsNullOrWhiteSpace(rootNote))
            {
                writtenRootNote = $"{Key}4";
            }
            else
            {
                writtenRootNote = ToWrittenNoteName(rootNote.Trim());
            }

            var builder = new ArpeggioSequenceBuilder
            {
                Key = Key,
                Scale = "Major",
                LowestNote = LowestNote,
                HighestNote = HighestNote,
                Duration = NoteDuration.Quarter
            };

            var notes = builder.Build(pattern, writtenRootNote, descendingAfterAscending);
            DebugLog.WriteLine(
                $"[Arpeggio] {pattern.DisplayName} root={rootNote ?? "(Key)"} writtenRoot={writtenRootNote} " +
                $"range={LowestNote}-{HighestNote}: {string.Join(" ", notes.Select(n => n.SpelledName))}");
            return notes;
        }

        /// <summary>
        /// Loads an arpeggio through the existing listen/play session state.  The returned
        /// notes are the displayed rhythm order; callers assign them to the staff drawable.
        /// </summary>
        public Task<List<GeneratedNote>> LoadArpeggioAsync(
            ArpeggioPattern? pattern = null,
            string? rootNote = null,
            bool descendingAfterAscending = true)
        {
            var previewNotes = BuildArpeggioNotes(pattern, rootNote, descendingAfterAscending);

            Reset();
            CurrentTune = null;

            var rhythmSlots = RhythmStartGate.BuildSlots(previewNotes);
            int sessionIdx = 0;
            int pitchIdx = 0;
            var (noteKey, noteScale) = GetNotationKeyAndScale();
            foreach (var note in previewNotes)
            {
                if (note.IsRest)
                    continue;

                var slot = rhythmSlots[pitchIdx++];
                var (midi, name) = ResolveTargetPitch(note, noteKey, noteScale);

                NotesToDraw.Add(new NoteInfo
                {
                    Midi = midi,
                    Name = name,
                    TargetFreq = 440.0 * Math.Pow(2.0, (midi - 69) / 12.0),
                    X = 0f,
                    Duration = note.Duration,
                    StartBeat = slot.StartBeat,
                    DurationBeats = slot.DurationBeats,
                    GateBeatsAfterPrevious = slot.GateBeatsAfterPrevious
                });
                FeedbackViewModels.Add(new FeedbackItem(sessionIdx++, 0, 0, false));
            }

            ConfigureRhythmStartGates();
            DebugLog.WriteLine(
                $"[Arpeggio] Loaded {NotesToDraw.Count} playable notes into session state.");

            return Task.FromResult(previewNotes);
        }
#if DEBUG
        public List<GeneratedNote> BuildArpeggioPreviewNotes(
            ArpeggioPattern? pattern = null,
            string? rootNote = null,
            bool descendingAfterAscending = true)
            => BuildArpeggioNotes(pattern, rootNote, descendingAfterAscending);
#endif
        public bool ShouldIgnoreAudio(DateTime utcNow)
        {
            return utcNow < IgnoreAudioUntilUtc;
        }
        /// <summary>
        /// Called by the audio pipeline when RMS drops below the silence threshold.
        /// Clears the consecutive-same-pitch silence requirement so the next note
        /// can be matched as soon as the player plays it.
        /// </summary>
        public void NotifySilence()
        {
            _requireSilenceBeforeNote = false;
            ClearRhythmGateIfExpired();
        }
        public (string WrittenName, int CentsDeviation) MapPitch(double freq)
        {
            if (freq <= 0)
            {
                return ("-", 0);
            }

            var midi = (int)Math.Round(69 + 12 * Math.Log(freq / 440.0, 2));
            var detMidiWritten = ApplyInstrumentTranspose(midi);
            var name = MidiToNoteName(detMidiWritten, KeyUsesFlats(Key));
            var nearestFreq = MidiToFreq(midi);
            var cents = (int)Math.Round(1200 * Math.Log(freq / nearestFreq, 2));
            return (name, cents);
        }

        // Tuner: last detected written name and cents
        private string? _tunerLastNoteName;
        public string? TunerLastNoteName
        {
            get => _tunerLastNoteName;
            private set
            {
                if (_tunerLastNoteName != value)
                {
                    _tunerLastNoteName = value;
                    OnPropertyChanged(nameof(TunerLastNoteName));
                    OnPropertyChanged(nameof(TunerDisplay));
                }
            }
        }

        private int _tunerLastCents;
        public int TunerLastCents
        {
            get => _tunerLastCents;
            private set
            {
                if (_tunerLastCents != value)
                {
                    _tunerLastCents = value;
                    OnPropertyChanged(nameof(TunerLastCents));
                    OnPropertyChanged(nameof(TunerDisplay));
                }
            }
        }

        // Tuner: last detected frequencies
        private double _tunerLastDetectedFreq;
        public double TunerLastDetectedFreq
        {
            get => _tunerLastDetectedFreq;
            private set
            {
                if (Math.Abs(_tunerLastDetectedFreq - value) > 0.0001)
                {
                    _tunerLastDetectedFreq = value;
                    OnPropertyChanged(nameof(TunerLastDetectedFreq));
                }
            }
        }

        private double _tunerLastNearestFreq;
        private int? _tunerPrevWrittenMidi = null;   // direction-based enharmonic spelling
        public double TunerLastNearestFreq
        {
            get => _tunerLastNearestFreq;
            private set
            {
                if (Math.Abs(_tunerLastNearestFreq - value) > 0.0001)
                {
                    _tunerLastNearestFreq = value;
                    OnPropertyChanged(nameof(TunerLastNearestFreq));
                }
            }
        }

        public void ClearTunerDetection()
        {
            _tunerLastNoteName = null;
            _tunerLastCents = 0;
            _tunerLastDetectedFreq = 0;
            _tunerLastNearestFreq = 0;
            _tunerPrevWrittenMidi = null;
            OnPropertyChanged(nameof(TunerLastNoteName));
            OnPropertyChanged(nameof(TunerLastCents));
            OnPropertyChanged(nameof(TunerDisplay));
            OnPropertyChanged(nameof(TunerLastDetectedFreq));
            OnPropertyChanged(nameof(TunerLastNearestFreq));
        }

        public static GeneratedNote? TryBuildGeneratedNoteFromSpelledName(string? spelledName)
        {
            if (string.IsNullOrWhiteSpace(spelledName))
                return null;

            var raw = spelledName.Trim();
            try
            {
                char letter = char.ToUpperInvariant(raw[0]);
                int octave = ParseOctaveFromSpelledName(raw);
                int midi = NoteNameToMidi(raw);
                Accidental acc = Accidental.None;
                if (raw.Contains("##"))
                    acc = Accidental.DoubleSharp;
                else if (raw.Contains("bb"))
                    acc = Accidental.DoubleFlat;
                else if (raw.Contains('#'))
                    acc = Accidental.Sharp;
                else if (raw.Length > 1 && raw[1] == 'b')
                    acc = Accidental.Flat;

                return new GeneratedNote
                {
                    MidiNumber = midi,
                    Letter = letter,
                    Octave = octave,
                    Accidental = acc,
                    SpelledName = raw,
                    TargetFrequency = MidiToFreq(midi),
                    Duration = NoteDuration.Quarter,
                    IsRest = false,
                    MeasureIndex = 0,
                    BeatPosition = 0,
                };
            }
            catch
            {
                return null;
            }
        }

        public void UpdateTunerLastNote(double freq)
        {
            if (freq <= 0) return;
            try
            {
                var concertMidi = (int)Math.Round(69 + 12 * Math.Log(freq / 440.0, 2));
                var writtenMidi = ApplyInstrumentTranspose(concertMidi);
                var nearestFreq = MidiToFreq(concertMidi);
                var cents = (int)Math.Round(1200 * Math.Log(freq / nearestFreq, 2));

                // Direction-based enharmonic spelling:
                bool preferFlats;
                if (_tunerPrevWrittenMidi.HasValue && writtenMidi != _tunerPrevWrittenMidi.Value)
                    preferFlats = writtenMidi < _tunerPrevWrittenMidi.Value;
                else if (_tunerPrevWrittenMidi.HasValue)
                    preferFlats = TunerLastNoteName?.Contains('b') == true;
                else
                    preferFlats = KeyUsesFlats(Key);

                _tunerPrevWrittenMidi = writtenMidi;

                TunerLastNoteName = MidiToNoteName(writtenMidi, preferFlats);
                TunerLastCents = cents;
                TunerLastDetectedFreq = freq;
                TunerLastNearestFreq = nearestFreq;
            }
            catch
            {
                TunerLastNearestFreq = 0.0;
            }
        }
        public string TunerDisplay => TunerLastNoteName == null ? "-" : $"{TunerLastNoteName} {TunerLastCents:+ 0;- 0;0}¢";
        public async Task GenerateNotesAsync(double availableWidth)
        {
            NotesToDraw.Clear();
            FeedbackViewModels.Clear();
            CorrectNoteIndices.Clear();
            NoteFeedbacks.Clear();
            MeasureBarXPositions.Clear();
            RestXPositions.Clear();
            RestDurations.Clear();
            CurrentNoteIndex = 0;
            _lockedPitchClassAfterAdvance = null;
            IgnoreAudioUntilUtc = DateTime.MinValue;

            // ── Practice Tune mode ──────────────────────────────────────────────
            if (Tune == "Practice Tune")
            {
                var tune = CurrentTune ?? TuneLibrary.CMajorScale;
                CurrentTune = tune;
                var (noteKey, noteScale) = ResolvePracticeTuneNotation(tune);

                // Proportional spacing: each beat unit gets a fixed pixel width so that
                // half notes are twice as wide as quarters, whole notes four times as wide, etc.
                var noteHeadWidth = 24f;
                var beatUnit = tune.TimeSignature.BeatUnit;
                var beatUnitValue = beatUnit.ToBeatValue(); // e.g. 1.0 for quarter

                // Count total beat-units across the whole tune for layout sizing
                double totalBeats = 0;
                foreach (var m in tune.Measures)
                    foreach (var mn in m.Notes)
                        totalBeats += mn.Duration.ToBeatValue() / beatUnitValue;

                var usable = availableWidth > 0 ? (float)(availableWidth - 64) : noteHeadWidth * 3f * (float)totalBeats;
                // pixels per beat-unit
                var pixPerBeat = Math.Max(noteHeadWidth * 2f, usable / Math.Max(1, (float)totalBeats));
                var startX = 32f;

                double cursorBeats = 0;
                bool firstMeasure = true;
                foreach (var measure in tune.Measures)
                {
                    // Record bar-line X at the start of each measure except the first
                    if (!firstMeasure)
                    {
                        var barX = startX + (float)(cursorBeats * pixPerBeat) - pixPerBeat * 0.5f;
                        MeasureBarXPositions.Add(barX);
                    }
                    firstMeasure = false;

                    foreach (var mn in measure.Notes)
                    {
                        var beatVal = mn.Duration.ToBeatValue() / beatUnitValue;
                        var slotX = startX + (float)(cursorBeats * pixPerBeat);
                        if (mn.IsRest)
                        {
                            RestXPositions.Add(slotX);
                            RestDurations.Add(mn.Duration);
                        }
                        else
                        {
                            var adjustedMidi = ApplyKeySignatureToMidi(mn.SpelledName, mn.MidiNumber, noteKey, noteScale);
                            var rawName = mn.SpelledName.Trim();
                            char letter = char.ToUpperInvariant(rawName[0]);
                            int octave = ParseOctaveFromSpelledName(rawName);
                            var displayName = ResolveWrittenNoteName(
                                rawName, adjustedMidi, letter, octave, noteKey, noteScale);
                            var freq = MidiToFreq(adjustedMidi);
                            var noteIdx = NotesToDraw.Count;
                            NotesToDraw.Add(new NoteInfo
                            {
                                Midi = adjustedMidi,
                                Name = displayName,
                                TargetFreq = freq,
                                X = slotX,
                                Duration = mn.Duration
                            });
                            FeedbackViewModels.Add(new FeedbackItem(noteIdx, 0, 0, false));
                        }
                        cursorBeats += beatVal;
                    }
                }
                return;
            }

            // ── All other modes (unchanged) ─────────────────────────────────────
            CurrentTune = null;

            string[] sequence;
            if (IsRandomMode)
            {
                sequence = await BuildRandomSequenceAsync();
            }
            else if (Tune == "Selected Scale")
            {
                sequence = BuildScaleSequence(Key, SelectedScale);
            }
            else if (Tune == "Tuner")
            {
                sequence = Array.Empty<string>();
            }
            else
            {
                sequence = BuildScaleSequence(Key, SelectedScale);
            }

            var noteHeadWidthStd = 24f;
            var spacingStd = noteHeadWidthStd * 3f;
            if (availableWidth > 0 && sequence.Length > 0)
            {
                var usable = (float)(availableWidth - 64);
                spacingStd = Math.Max(noteHeadWidthStd * 2f, usable / Math.Max(1, sequence.Length));
            }

            var startXStd = 32f;
            for (var i = 0; i < sequence.Length; i++)
            {
                var midi = NoteNameToMidi(sequence[i]);
                var freq = MidiToFreq(midi);
                NotesToDraw.Add(new NoteInfo { Midi = midi, Name = sequence[i], TargetFreq = freq, X = startXStd + i * spacingStd });
                FeedbackViewModels.Add(new FeedbackItem(i, 0, 0, false));
            }
        }
        public string GetConcertKey()
        {
            return TransposeKey(Key, GetInstrumentTransposeOffset());
        }

        /// <summary>
        /// Converts a concert key name to the written key for an instrument
        /// (<c>written = TransposeKey(concert, -offset)</c>), inverse of <see cref="GetConcertKey"/>.
        /// </summary>
        public static string ToWrittenKey(string concertKey, int transposeOffset)
            => NormalizeKeyNameForSignature(TransposeKey(concertKey, -transposeOffset));

        /// <summary>Instance helper: concert key → written key for the active instrument.</summary>
        public string ToWrittenKey(string concertKey)
            => ToWrittenKey(concertKey, GetInstrumentTransposeOffset());

        /// <summary>
        /// Converts a concert note name (e.g. D4) to the written note for an instrument.
        /// Same shift as <see cref="ApplyInstrumentTranspose"/>.
        /// </summary>
        public static string ToWrittenNoteName(string concertNoteName, int transposeOffset)
        {
            if (string.IsNullOrWhiteSpace(concertNoteName))
                return concertNoteName;

            int concertMidi = NoteNameToMidi(concertNoteName.Trim());
            if (concertMidi < 0)
                return concertNoteName.Trim();

            int writtenMidi = concertMidi - transposeOffset;
            bool preferFlats = KeyUsesFlats(TrimNoteOctave(concertNoteName));
            return MidiToNoteName(writtenMidi, preferFlats);
        }

        /// <summary>Instance helper: concert note → written note for the active instrument.</summary>
        public string ToWrittenNoteName(string concertNoteName)
            => ToWrittenNoteName(concertNoteName, GetInstrumentTransposeOffset());

        /// <summary>
        /// Concert-pitch key signature implied by an arpeggio root + pattern
        /// (relative major for minor-family chords; otherwise the root).
        /// </summary>
        public static string ResolveArpeggioConcertKeySignature(ArpeggioPattern pattern, string concertRootNote)
        {
            var root = NormalizeKeyNameForSignature(TrimNoteOctave(concertRootNote));
            if (ArpeggioUsesMinorFamilyKeySignature(pattern))
                return KeySignatureRules.RelativeMajorOf(root);
            return root;
        }

        /// <summary>
        /// Written key signature for an arpeggio: concert key signature transposed for the instrument.
        /// Picker roots are concert pitch names; the staff shows written music.
        /// </summary>
        public static string ResolveArpeggioWrittenKeySignature(
            ArpeggioPattern pattern,
            string concertRootNote,
            int transposeOffset)
            => ToWrittenKey(ResolveArpeggioConcertKeySignature(pattern, concertRootNote), transposeOffset);

        /// <summary>Instance helper using the active instrument transpose offset.</summary>
        public string ResolveArpeggioWrittenKeySignature(ArpeggioPattern pattern, string concertRootNote)
            => ResolveArpeggioWrittenKeySignature(pattern, concertRootNote, GetInstrumentTransposeOffset());

        private static bool ArpeggioUsesMinorFamilyKeySignature(ArpeggioPattern pattern)
            => pattern.SemitoneIntervals.Contains(3) && !pattern.SemitoneIntervals.Contains(4);

        private static string TrimNoteOctave(string noteName)
            => new(noteName.TakeWhile(c => !char.IsDigit(c)).ToArray());

        private static string NormalizeKeyNameForSignature(string key) => key switch
        {
            "A#" => "Bb",
            "D#" => "Eb",
            "G#" => "Ab",
            "E#" => "F",
            "B#" => "C",
            "Fb" => "E",
            _ => key
        };
        private static string GetNoteName(int midi, AccidentalPreference pref)
        {
            var namesSharp = new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            var namesFlat = new[] { "C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B" };
            var pc = ((midi % 12) + 12) % 12;
            var oct = (midi / 12) - 1;
            var baseName = pref == AccidentalPreference.Flats ? namesFlat[pc] : namesSharp[pc];
            return $"{baseName}{oct}";
        }
        /// <summary>
        /// Written-pitch MIDI used for evaluation (what the player reads on the staff).
        /// Prefers <see cref="NoteInfo.Name"/> so transposing instruments stay aligned with the score.
        /// </summary>
        public static int ResolveWrittenEvaluationMidi(NoteInfo note)
        {
            if (!string.IsNullOrWhiteSpace(note.Name))
            {
                try
                {
                    return NoteNameToMidi(note.Name.Trim());
                }
                catch
                {
                    // fall through to stored midi
                }
            }

            return note.Midi;
        }

        /// <summary>Written note label for status display (matches staff / player expectation).</summary>
        public string ResolveWrittenEvaluationName(NoteInfo note)
            => ResolveWrittenNoteName(note);

        public (bool correct, int cents) Evaluate(double freq)
        {
            if (NotesToDraw.Count == 0 || CurrentNoteIndex >= NotesToDraw.Count || freq <= 0)
            {
                return (false, 0);
            }

            var target = NotesToDraw[CurrentNoteIndex];
            var expectedWrittenMidi = ResolveWrittenEvaluationMidi(target);
            var detMidi = (int)Math.Round(69 + 12 * Math.Log(freq / 440.0, 2));

            // Transpose detected MIDI to written pitch for the selected instrument
            var detMidiWritten = detMidi - GetInstrumentTransposeOffset();
            var detPcWritten = Mod12(detMidiWritten);

            // Compare to the written note's pitch class
            var expectedPc = Mod12(expectedWrittenMidi);
            var correctPc = detPcWritten == expectedPc;

            // Enharmonic check: allow E4 == Fb4, etc.
            bool enharmonicMatch = false;
            if (!correctPc)
            {
                // Get all enharmonic MIDI numbers for the target note
                var enharmonicMidis = GetEnharmonicMidis(expectedWrittenMidi);
                // enharmonicMatch = enharmonicMidis.Contains(detMidiWritten);
                enharmonicMatch = enharmonicMidis.Any(m => Mod12(m) == detPcWritten);  //  2026.03.06 1745  
            }

            // Utils.Log($"Evaluate: freq={freq:F2}, detMidi={detMidi}, detMidiWritten={detMidiWritten}, detPcWritten={detPcWritten}, targetMidi={target.Midi}, expectedPc={expectedPc}, correctPc={correctPc}, enharmonicMatch={enharmonicMatch}");

            // For cents, always use the concert pitch of the detected MIDI (not written MIDI)
            var nearestMidi = detMidi;
            var nearestFreq = MidiToFreq(nearestMidi);
            var cents = (int)Math.Round(1200 * Math.Log(freq / nearestFreq, 2));
            var withinTolerance = Math.Abs(cents) <= Tolerance;
            // Utils.Log($"Evaluate: nearestMidi={nearestMidi}, nearestFreq={nearestFreq:F2}, cents={cents}, withinTolerance={withinTolerance}");

            // Consider a detection correct only if pitch-class matches (or is enharmonic)
            // AND the cents deviation is within the configured tolerance.
            var isPitchClassMatch = (correctPc || enharmonicMatch);
            return (isPitchClassMatch && withinTolerance, cents);
        }
        // Returns all MIDI numbers that are enharmonic equivalents of the given MIDI (including itself)
        public static HashSet<int> GetEnharmonicMidis(int midi)
        {
            var set = new HashSet<int> { midi };
            // Try all possible note names for this midi and add their midi equivalents
            var names = new[]
            {
                MidiToNoteName(midi, false), // sharp
                MidiToNoteName(midi, true)   // flat
            };
            foreach (var name in names)
            {
                try
                {
                    set.Add(NoteNameToMidi(name));
                }
                catch { }
            }
            // Add common enharmonic equivalents manually
            // E.g., E4 <-> Fb4, B3 <-> Cb4, etc.
            var enharmonics = new Dictionary<string, string>
            {
                { "E", "Fb" }, { "Fb", "E" },
                { "B", "Cb" }, { "Cb", "B" },
                { "C", "B#" }, { "B#", "C" },
                { "F", "E#" }, { "E#", "F" },
                { "G#", "Ab" }, { "Ab", "G#" },
                { "A#", "Bb" }, { "Bb", "A#" },
                { "D#", "Eb" }, { "Eb", "D#" },
                { "C#", "Db" }, { "Db", "C#" },
            };
            var baseName = new string(MidiToNoteName(midi, false).TakeWhile(c => !char.IsDigit(c)).ToArray());
            var octave = new string(MidiToNoteName(midi, false).SkipWhile(c => !char.IsDigit(c)).ToArray());
            if (enharmonics.TryGetValue(baseName, out var enh))
            {
                var enhName = enh + octave;
                try { set.Add(NoteNameToMidi(enhName)); } catch { }
            }
            return set;
        }
        private static int Mod12(int midi)
        {
            return ((midi % 12) + 12) % 12;
        }
        public static int NoteNameToMidi(string note)
        {
            if (note != null)
            {
                var name = note.Trim();
                var octave = int.Parse(name[^1].ToString());
                var baseName = name[..^1];
                var pc = baseName switch
                {
                    "C" => 0,
                    "C#" => 1,
                    "Db" => 1,
                    "D" => 2,
                    "D#" => 3,
                    "Eb" => 3,
                    "E" => 4,
                    "E#" => 5,
                    "Fb" => 4,
                    "F" => 5,
                    "F#" => 6,
                    "Gb" => 6,
                    "G" => 7,
                    "G#" => 8,
                    "Ab" => 8,
                    "A" => 9,
                    "A#" => 10,
                    "Bb" => 10,
                    "B" => 11,
                    "B#" => 12,
                    "Cb" => -1,
                    _ => 0
                };
                return 12 * (octave + 1) + pc;
            }
            return -1;
        }
        private static double MidiToFreq(int midi)
        {
            return 440.0 * Math.Pow(2, (midi - 69) / 12.0);
        }
        public static double MidiToFreqPublic(int midi) => MidiToFreq(midi);
        public static string MidiToNoteName(int midi, bool flats)
        {
            var namesSharp = new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            var namesFlat = new[] { "C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B" };
            var pc = ((midi % 12) + 12) % 12;
            var oct = (midi / 12) - 1;
            return $"{(flats ? namesFlat : namesSharp)[pc]}{oct}";
        }

        /// <summary>
        /// Semitone intervals from the tonic (excluding the octave) for standard 7-note scales,
        /// or null for pentatonic/chromatic/non-standard scales. Used for letter-aware spelling
        /// so notes like E# appear instead of F♮ in F# major.
        /// </summary>
        public static int[]? GetSevenNoteScaleDegreeIntervals(string scale) => scale switch
        {
            "Major" or "Ionian" => new[] { 0, 2, 4, 5, 7, 9, 11 },
            "Natural Minor" or "Aeolian" => new[] { 0, 2, 3, 5, 7, 8, 10 },
            "Harmonic Minor" => new[] { 0, 2, 3, 5, 7, 8, 11 },
            "Melodic Minor" or "Jazz Melodic Minor" => new[] { 0, 2, 3, 5, 7, 9, 11 },
            "Dorian" => new[] { 0, 2, 3, 5, 7, 9, 10 },
            "Phrygian" => new[] { 0, 1, 3, 5, 7, 8, 10 },
            "Lydian" => new[] { 0, 2, 4, 6, 7, 9, 11 },
            "Mixolydian" => new[] { 0, 2, 4, 5, 7, 9, 10 },
            "Locrian" => new[] { 0, 1, 3, 5, 6, 8, 10 },
            "Harmonic Major" => new[] { 0, 2, 4, 5, 7, 8, 11 },
            "Phrygian Dominant" => new[] { 0, 1, 4, 5, 7, 8, 10 },
            "Double Harmonic" => new[] { 0, 1, 4, 5, 7, 8, 11 },
            _ => null
        };

        /// <summary>
        /// Chromatic pitch classes (0–11) belonging to <paramref name="key"/> / <paramref name="scale"/>.
        /// </summary>
        public static HashSet<int> GetScalePitchClasses(string key, string scale)
        {
            int[] intervals = GetSevenNoteScaleDegreeIntervals(scale)
                ?? GetNonSevenNoteScaleDegreeIntervals(scale);

            int tonicPc = ((NoteNameToMidi($"{key}4") % 12) + 12) % 12;
            var pcs = new HashSet<int>();
            foreach (var interval in intervals)
                pcs.Add((tonicPc + interval) % 12);
            return pcs;
        }

        /// <summary>Intervals for scales that are not letter-sequential 7-note spellings.</summary>
        private static int[] GetNonSevenNoteScaleDegreeIntervals(string scale) => scale switch
        {
            "Major Pentatonic" => new[] { 0, 2, 4, 7, 9 },
            "Minor Pentatonic" => new[] { 0, 3, 5, 7, 10 },
            "Blues" or "Minor Blues" => new[] { 0, 3, 5, 6, 7, 10 },
            "Major Blues" => new[] { 0, 2, 3, 4, 7, 9 },
            "Chromatic" => new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 },
            "Lydian Dominant" => new[] { 0, 2, 4, 6, 7, 9, 10 },
            "Hungarian Minor" => new[] { 0, 2, 3, 6, 7, 8, 11 },
            "Bebop" => new[] { 0, 2, 4, 5, 7, 9, 10, 11 },
            _ => new[] { 0, 2, 4, 5, 7, 9, 11 }
        };

        /// <summary>
        /// Spells a written MIDI pitch for <paramref name="key"/> / <paramref name="scale"/>
        /// using the same letter-aware rules as music generation.
        /// When <paramref name="prevMidi"/> is non-negative and the signature is empty,
        /// non-scale chromatics use melodic direction (ascending → sharp, descending → flat).
        /// Catalogs should omit <paramref name="prevMidi"/> for a deterministic spelling.
        /// </summary>
        public static string SpellWrittenPitch(int midi, string key, string scale, int prevMidi = -1)
        {
            if (string.IsNullOrWhiteSpace(key))
                key = "C";
            if (string.IsNullOrWhiteSpace(scale))
                scale = "Major";

            bool preferFlats = KeySignatureRules.KeySignatureUsesFlats(key, scale);

            if (!preferFlats
                && KeySignatureRules.GetSignedAccidentalCount(key, scale) == 0
                && prevMidi >= 0)
            {
                bool isChromatic = !GetScalePitchClasses(key, scale)
                    .Contains(((midi % 12) + 12) % 12);
                if (isChromatic)
                    preferFlats = midi < prevMidi;
            }

            var scaleDegreeIntervals = GetSevenNoteScaleDegreeIntervals(scale);
            if (scaleDegreeIntervals != null)
            {
                int pc = ((midi % 12) + 12) % 12;
                int tonicPc = ((NoteNameToMidi($"{key}4") % 12) + 12) % 12;
                int degree = -1;
                for (int i = 0; i < scaleDegreeIntervals.Length; i++)
                {
                    if (((tonicPc + scaleDegreeIntervals[i]) % 12) == pc)
                    {
                        degree = i;
                        break;
                    }
                }

                if (degree >= 0)
                {
                    char tonicLetter = char.ToUpperInvariant(key.Trim()[0]);
                    int tonicLetterIdx = Array.IndexOf(Letters, tonicLetter);
                    if (tonicLetterIdx < 0)
                        tonicLetterIdx = 0;
                    char degLetter = Letters[(tonicLetterIdx + degree) % 7];
                    return SpellNote(degLetter, midi);
                }
            }

            return MidiToNoteName(midi, preferFlats);
        }

        private static bool KeyUsesFlats(string key)
            => KeySignatureRules.IsFlatKeyName(key);
        private int ApplyInstrumentTranspose(int concertMidi)
        {
            return concertMidi - GetInstrumentTransposeOffset();
        }
        public static string TransposeKey(string key, int semitones)
        {
            var midi = NoteNameToMidi($"{key}4") + semitones;
            return MidiToNoteName(midi, KeyUsesFlats(key)).TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        }
        private static readonly int[] MajorUp = new[] { 0, 2, 4, 5, 7, 9, 11, 12 };
        private static string[] BuildMajorSpelled(string tonic, string key)
        {
            var (flats, sharps) = GetAccidentalSetsForScale(key, "Major");
            var pref = GetPreferenceForScale(key, "Major");
            return BuildLetterAwareScale(tonic, MajorUp, Array.Empty<int>(), pref, pref, flats, sharps);
        }
        private static string[] BuildHarmonicMinorSpelled(string tonic, string key)
        {
            var (flats, sharps) = GetAccidentalSetsForScale(key, "Harmonic Minor");
            var pref = GetPreferenceForScale(key, "Harmonic Minor");
            return BuildLetterAwareScale(tonic, HarmonicMinorUp, Array.Empty<int>(), pref, pref, flats, sharps);
        }
        private static string[] BuildMelodicMinorSpelled(string tonic, string key)
        {
            int[] up = new[] { 0, 2, 3, 5, 7, 9, 11, 12 };
            int[] down = new[] { 12, 10, 8, 7, 5, 3, 2, 0 };
            var (flats, sharps) = GetAccidentalSetsForScale(key, "Melodic Minor");
            var pref = GetPreferenceForScale(key, "Melodic Minor");
            return BuildLetterAwareScale(tonic, up, down, pref, pref, flats, sharps);
        }
        private static string[] BuildNaturalMinorSpelled(string tonic, string key)
        {
            var (flats, sharps) = GetAccidentalSetsForScale(key, "Natural Minor");
            var pref = GetPreferenceForScale(key, "Natural Minor");
            return BuildLetterAwareScale(tonic, NaturalMinorUp, Array.Empty<int>(), pref, pref, flats, sharps);
        }
        private static string[] BuildHarmonicMajorSpelled(string tonic, string key)
        {
            var (flats, sharps) = GetAccidentalSetsForScale(key, "Harmonic Major");
            var pref = GetPreferenceForScale(key, "Harmonic Major");
            return BuildLetterAwareScale(tonic, HarmonicMajorUp, Array.Empty<int>(), pref, pref, flats, sharps);
        }
        private static string[] BuildPhrygianDominantSpelled(string tonic, string key)
        {
            var (flats, sharps) = GetAccidentalSetsForScale(key, "Phrygian Dominant");
            var pref = GetPreferenceForScale(key, "Phrygian Dominant");
            return BuildLetterAwareScale(tonic, PhrygianDominantUp, Array.Empty<int>(), pref, pref, flats, sharps);
        }
        private static string[] BuildDoubleHarmonicSpelled(string tonic, string key)
        {
            var (flats, sharps) = GetAccidentalSetsForScale(key, "Double Harmonic");
            var pref = GetPreferenceForScale(key, "Double Harmonic");
            return BuildLetterAwareScale(tonic, DoubleHarmonicUp, Array.Empty<int>(), pref, pref, flats, sharps);
        }
        private static string[] BuildNeapolitanMinorSpelled(string tonic, string key)
        {
            var (flats, sharps) = GetAccidentalSetsForScale(key, "Neapolitan Minor");
            var pref = GetPreferenceForScale(key, "Neapolitan Minor");
            return BuildLetterAwareScale(tonic, NeapolitanMinorUp, Array.Empty<int>(), pref, pref, flats, sharps);
        }
        private static string[] BuildNeapolitanMajorSpelled(string tonic, string key)
        {
            var (flats, sharps) = GetAccidentalSetsForScale(key, "Neapolitan Major");
            var pref = GetPreferenceForScale(key, "Neapolitan Major");
            return BuildLetterAwareScale(tonic, NeapolitanMajorUp, Array.Empty<int>(), pref, pref, flats, sharps);
        }
        private static string[] BuildScaleSequence(string key, string selectedScale)
        {
            // Use octave 3 for keys above G (Ab, A, Bb, B) to keep the scale in a comfortable range
            var testMidi = NoteNameToMidi($"{key}4");
            var startOctave = testMidi > 67 ? 3 : 4; // 67 = G4
            var tonic = $"{key}{startOctave}";

            if (selectedScale == "Harmonic Minor")
            {
                return BuildHarmonicMinorSpelled(tonic, key);
            }

            if (selectedScale is "Melodic Minor" or "Jazz Melodic Minor")
            {
                return BuildMelodicMinorSpelled(tonic, key);
            }

            if (selectedScale is "Natural Minor" or "Aeolian")
            {
                return BuildNaturalMinorSpelled(tonic, key);
            }

            if (selectedScale == "Harmonic Major")
            {
                return BuildHarmonicMajorSpelled(tonic, key);
            }

            if (selectedScale == "Phrygian Dominant")
            {
                return BuildPhrygianDominantSpelled(tonic, key);
            }

            if (selectedScale == "Double Harmonic")
            {
                return BuildDoubleHarmonicSpelled(tonic, key);
            }

            if (selectedScale == "Neapolitan Minor")
            {
                return BuildNeapolitanMinorSpelled(tonic, key);
            }

            if (selectedScale == "Neapolitan Major")
            {
                return BuildNeapolitanMajorSpelled(tonic, key);
            }

            if (selectedScale is "Major" or "Ionian")
            {
                return BuildMajorSpelled(tonic, key);
            }
            if (selectedScale is "Blues" or "Minor Blues")
            {
                return BuildBluesSpelled(tonic, key);
            }

            var pref = GetPreferenceForScale(key, selectedScale);
            int[] up = selectedScale switch
            {
                "Natural Minor" or "Aeolian" => new[] { 0, 2, 3, 5, 7, 8, 10, 12 },
                "Dorian" => new[] { 0, 2, 3, 5, 7, 9, 10, 12 },
                "Phrygian" => new[] { 0, 1, 3, 5, 7, 8, 10, 12 },
                "Lydian" => new[] { 0, 2, 4, 6, 7, 9, 11, 12 },
                "Mixolydian" => new[] { 0, 2, 4, 5, 7, 9, 10, 12 },
                "Locrian" => new[] { 0, 1, 3, 5, 6, 8, 10, 12 },
                "Lydian Dominant" => new[] { 0, 2, 4, 6, 7, 9, 10, 12 },
                "Super Locrian" => new[] { 0, 1, 3, 4, 6, 8, 10, 12 },
                "Hungarian Minor" => new[] { 0, 2, 3, 6, 7, 8, 11, 12 },
                "Arabian" => new[] { 0, 2, 3, 5, 6, 8, 11, 12 },
                "Japanese" => new[] { 0, 1, 5, 7, 8, 12 },
                "Egyptian" => new[] { 0, 2, 5, 7, 10, 12 },
                "Major Pentatonic" => new[] { 0, 2, 4, 7, 9, 12 },
                "Minor Pentatonic" => new[] { 0, 3, 5, 7, 10, 12 },
                "Blues" => new[] { 0, 3, 5, 6, 7, 10, 12 },
                "Minor Blues" => new[] { 0, 3, 5, 6, 7, 10, 12 },
                "Major Blues" => new[] { 0, 2, 3, 4, 7, 9, 12 },
                "Altered" => new[] { 0, 1, 3, 4, 6, 8, 10, 12 },
                "Bebop" => new[] { 0, 2, 4, 5, 7, 9, 10, 11, 12 },
                "Symmetrical Whole-Tone" or "Whole-Tone" => new[] { 0, 2, 4, 6, 8, 10, 12 },
                "Symmetrical Diminished" or "Diminished" => new[] { 0, 1, 3, 4, 6, 7, 9, 10, 12 },
                "Chromatic" => Enumerable.Range(0, 13).ToArray(),
                _ => new[] { 0, 2, 4, 5, 7, 9, 11, 12 }
            };
            var startMidi = NoteNameToMidi(tonic);

            // Chromatic scale: sharps ascending, flats descending
            if (selectedScale == "Chromatic")
            {
                var descChr = up.Take(up.Length - 1).Reverse().ToArray();
                var ascending = up.Select(d => GetNoteName(startMidi + d, AccidentalPreference.Sharps));
                var descending = descChr.Select(d => GetNoteName(startMidi + d, AccidentalPreference.Flats));
                return ascending.Concat(descending).ToArray();
            }

            // All other scales: assign letters sequentially (A→B→C→D→E→F→G) from the tonic,
            // then derive the correct accidental by comparing each MIDI with the natural pitch
            // of the assigned letter.  Offsets are null for 7-note modes and whole-tone (sequential).
            int[]? letterOffsets = selectedScale switch
            {
                "Major Pentatonic" => new[] { 0, 1, 2, 4, 5 },
                "Minor Pentatonic" => new[] { 0, 2, 3, 4, 6 },
                "Major Blues" => new[] { 0, 1, 2, 2, 4, 5 },
                "Japanese" => new[] { 0, 1, 3, 4, 5 },
                "Egyptian" => new[] { 0, 1, 3, 4, 6 },
                "Bebop" => new[] { 0, 1, 2, 3, 4, 5, 6, 6 },
                "Symmetrical Diminished" or "Diminished" => new[] { 0, 1, 2, 2, 3, 4, 5, 6 },
                _ => null  // 7-note modes + whole-tone
            };
            return SpellSequential(tonic[0], startMidi, up, letterOffsets);
        }
        private static readonly Dictionary<(string, string), (HashSet<int>, HashSet<int>)> _accidentalCache = new();
        private static (HashSet<int> flats, HashSet<int> sharps) GetAccidentalSetsForScale(string key, string scale)
        {
            var cacheKey = (key, scale ?? "Major");
            if (_accidentalCache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var result = ComputeAccidentalSets(key, scale ?? "Major");
            _accidentalCache[cacheKey] = result;
            return result;
        }
        private static (HashSet<int>, HashSet<int>) ComputeAccidentalSets(string key, string scale)
        {
            var majorSharps = new Dictionary<string, int[]> { ["G"] = [6], ["D"] = [6, 1], ["A"] = [6, 1, 8], ["E"] = [6, 1, 8, 3], ["B"] = [6, 1, 8, 3, 10], ["F#"] = [6, 1, 8, 3, 10, 5], ["C#"] = [6, 1, 8, 3, 10, 5, 0] };
            var majorFlats = new Dictionary<string, int[]> { ["F"] = [10], ["Bb"] = [10, 3], ["Eb"] = [10, 3, 8], ["Ab"] = [10, 3, 8, 1], ["Db"] = [10, 3, 8, 1, 6], ["Gb"] = [10, 3, 8, 1, 6, 11], ["Cb"] = [10, 3, 8, 1, 6, 11, 4] };
            var minorFlats = new Dictionary<string, int[]> { ["D"] = [10], ["G"] = [10, 3], ["C"] = [10, 3, 8], ["F"] = [10, 3, 8, 1], ["Bb"] = [10, 3, 8, 1, 6], ["Eb"] = [10, 3, 8, 1, 6, 11], ["Ab"] = [10, 3, 8, 1, 6, 11, 4] };
            var minorSharps = new Dictionary<string, int[]> { ["E"] = [6], ["B"] = [6, 1], ["F#"] = [6, 1, 8], ["C#"] = [6, 1, 8, 3], ["G#"] = [6, 1, 8, 3, 10], ["D#"] = [6, 1, 8, 3, 10, 5], ["A#"] = [6, 1, 8, 3, 10, 5, 0] };

            var flats = new HashSet<int>();
            var sharps = new HashSet<int>();
            var s = scale.Trim();

            if (s is "Chromatic Scale" or "Symmetrical Whole-Tone" or "Whole-Tone" or "Symmetrical Diminished" or "Diminished")
            {
                return (flats, sharps);
            }

            if (s is "Natural Minor" or "Harmonic Minor" or "Melodic Minor" or "Aeolian" or "Minor Pentatonic" or "Jazz Melodic Minor")
            {
                if (minorFlats.TryGetValue(key, out var mf))
                {
                    foreach (var pc in mf)
                    {
                        flats.Add(pc);
                    }
                }

                if (minorSharps.TryGetValue(key, out var ms))
                {
                    foreach (var pc in ms)
                    {
                        sharps.Add(pc);
                    }
                }

                if (s == "Harmonic Minor")
                {
                    var ltPc = Mod12(NoteNameToMidi($"{key}4") + 11);
                    sharps.Add(ltPc);
                    flats.Remove(ltPc);
                }

                return (flats, sharps);
            }

            if (majorFlats.TryGetValue(key, out var majF))
            {
                foreach (var pc in majF)
                {
                    flats.Add(pc);
                }
            }

            if (majorSharps.TryGetValue(key, out var majS))
            {
                foreach (var pc in majS)
                {
                    sharps.Add(pc);
                }
            }

            return (flats, sharps);
        }
        private Color _appBackgroundColor = GetColorPreference("musicmate.AppBackgroundColor", Colors.White);
        public Color AppBackgroundColor
        {
            get => _appBackgroundColor;
            set
            {
                if (_appBackgroundColor != value)
                {
                    _appBackgroundColor = value;
                    SessionPreferences.Set("musicmate.AppBackgroundColor", value.ToArgbHex());
                    OnPropertyChanged(nameof(AppBackgroundColor));
                }
            }
        }
        private Color _panelBackgroundColor = GetColorPreference("musicmate.PanelBackgroundColor", Colors.White);
        public Color PanelBackgroundColor
        {
            get => _panelBackgroundColor;
            set
            {
                if (_panelBackgroundColor != value)
                {
                    _panelBackgroundColor = value;
                    SessionPreferences.Set("musicmate.PanelBackgroundColor", value.ToArgbHex());
                    OnPropertyChanged(nameof(PanelBackgroundColor));
                }
            }
        }
        private static Color GetColorPreference(string key, Color fallback)
        {
            var hex = SessionPreferences.Get(key, fallback.ToArgbHex());
            try
            {
                return Color.FromArgb(hex);
            }
            catch
            {
                return fallback;
            }
        }
        /// <summary>
        /// Sets <see cref="CurrentTune"/> and switches <see cref="Tune"/> to "Practice Tune"
        /// so the next call to <see cref="GenerateNotesAsync"/> will use the supplied tune.
        /// </summary>
        public void SelectPracticeTune(PracticeTune tune)
        {
            ArgumentNullException.ThrowIfNull(tune);

            if (!string.IsNullOrWhiteSpace(tune.Key))
            {
                if (Tune != "Practice Tune")
                    _keyBeforePracticeTune = Key;
                if (Key != tune.Key)
                    Key = tune.Key;
#if DEBUG
                DebugLog.WriteLine(
                    $"[PickerTest] PracticeTune/{tune.Title}: written Key={tune.Key} Concert={GetConcertKey()}");
#endif
            }

            CurrentTune = tune;
            Tune = "Practice Tune";
            OnPropertyChanged(nameof(CurrentTune));
        }
        private const string PrefIsRandomModeKey = "musicmate.IsRandomMode";
        private bool _isRandomMode = SessionPreferences.Get("musicmate.IsRandomMode", false);
        /// <summary>
        /// When true, note generation draws a random sequence from the current
        /// key/scale pool instead of playing the scale or practice tune in order.
        /// Persisted independently of the Tune picker selection.
        /// </summary>
        public bool IsRandomMode
        {
            get => _isRandomMode;
            set
            {
                if (_isRandomMode == value) return;
                _isRandomMode = value;
                SessionPreferences.Set(PrefIsRandomModeKey, value);
                OnPropertyChanged(nameof(IsRandomMode));
                OnPropertyChanged(nameof(EffectiveScaleDisplay));
                if (!value)
                    SetEffectiveScale(SelectedScale);
            }
        }
        private bool _sessionCompleted = true;
        public bool SessionCompleted
        {
            get => _sessionCompleted;
            set
            {
                if (_sessionCompleted != value)
                {
                    _sessionCompleted = value;
                    OnPropertyChanged(nameof(SessionCompleted));
                }
            }
        }
        /// <summary>
        /// Triggers session completion manually (e.g., after autoplay finishes).
        /// </summary>
        public async Task TriggerSessionCompletionAsync()
        {
            if (SessionCompletedAsync != null)
            {
                await SessionCompletedAsync.Invoke();
            }
        }
        /// <summary>Returns the natural (no-accidental) pitch-class 0–11 for a letter A–G.</summary>
        public static int NaturalPcForLetter(char letter) => letter switch
        {
            'C' => 0,
            'D' => 2,
            'E' => 4,
            'F' => 5,
            'G' => 7,
            'A' => 9,
            'B' => 11,
            _ => 0
        };
        /// <summary>
        /// Spells one MIDI note using the given letter, computing the correct octave and
        /// accidental by comparing <paramref name="targetMidi"/> with the nearest natural
        /// pitch of that letter.
        /// </summary>
        public static string SpellNote(char letter, int targetMidi)
        {
            var naturalPC = NaturalPcForLetter(letter);
            var letterOctave = (targetMidi - naturalPC) / 12;
            var naturalMidi = (letterOctave + 1) * 12 + naturalPC;
            var diff = targetMidi - naturalMidi;

            // Correct for octave boundary: natural is in an adjacent octave
            if (diff > 6) { diff -= 12; letterOctave++; }
            else if (diff < -6) { diff += 12; letterOctave--; }

            return diff switch
            {
                0 => $"{letter}{letterOctave}",
                1 => $"{letter}#{letterOctave}",
                -1 => $"{letter}b{letterOctave}",
                2 => $"{letter}##{letterOctave}",
                -2 => $"{letter}bb{letterOctave}",
                _ => throw new InvalidOperationException(
                $"Cannot spell MIDI {targetMidi} as letter {letter} within double accidental range.")
            };
        }
        /// <summary>
        /// Builds an ascending+descending scale sequence with correct letter-sequential spelling.
        /// Each degree is assigned the next letter in A–B–C–D–E–F–G order (wrapping) and the
        /// accidental is derived by comparing the target MIDI with the natural pitch of that letter.
        /// <para><paramref name="up"/> must include the octave (semitone 12) as its last element.</para>
        /// <para><paramref name="letterOffsets"/> maps each ascending degree (excluding the octave)
        /// to a letter-index offset from the tonic.  Pass <c>null</c> for 7-note (or 6-note
        /// whole-tone) scales to use sequential offsets 0, 1, 2, …</para>
        /// </summary>
        private static string[] SpellSequential(
            char tonicLetter,
            int tonicMidi,
            int[] up,
            int[]? letterOffsets)
        {
            var tonicIdx = Array.IndexOf(Letters, tonicLetter);
            var degreeCount = up.Length - 1; // unique degrees, not counting the octave repeat

            // Spell ascending (including the octave note at the end)
            var ascSpelled = new string[up.Length];
            for (var i = 0; i < up.Length; i++)
            {
                var offset = i < degreeCount
                    ? (letterOffsets != null ? letterOffsets[i] : i)
                    : 0; // octave repeats the tonic letter
                var letter = Letters[(tonicIdx + offset) % 7];
                ascSpelled[i] = SpellNote(letter, tonicMidi + up[i]);
            }

            // Descending mirrors ascending (minus octave), reversed — same spelling each direction
            var descSpelled = ascSpelled.Take(degreeCount).Reverse().ToArray();
            return ascSpelled.Concat(descSpelled).ToArray();
        }
        private static string[] BuildBluesSpelled(string tonic, string key)
        {
            int[] up = new[] { 0, 3, 5, 6, 7, 10, 12 };
            var tonicLetter = char.ToUpperInvariant(tonic[0]);
            var tonicIdx = Array.IndexOf(Letters, tonicLetter);
            var startMidi = NoteNameToMidi(tonic);

            // b3=offset2, P4=offset3, tritone=dynamic (offset 3 or 4), P5=offset4, b7=offset6
            int[] fixedOffsets = { 0, 2, 3, -1, 4, 6 }; // -1 = dynamic tritone

            var ascSpelled = new string[up.Length];
            for (int i = 0; i < up.Length - 1; i++)
            {
                int offset;
                if (fixedOffsets[i] == -1)
                {
                    // Pick offset 3 (augmented 4th) or offset 4 (diminished 5th) —
                    // whichever produces the simpler accidental. On a tie, prefer offset 4 (flat).
                    var targetMidi = startMidi + up[i];
                    int diff3 = ComputeSpellDiff(Letters[(tonicIdx + 3) % 7], targetMidi);
                    int diff4 = ComputeSpellDiff(Letters[(tonicIdx + 4) % 7], targetMidi);
                    offset = Math.Abs(diff3) < Math.Abs(diff4) ? 3 : 4;
                }
                else
                {
                    offset = fixedOffsets[i];
                }
                ascSpelled[i] = SpellNote(Letters[(tonicIdx + offset) % 7], startMidi + up[i]);
            }
            ascSpelled[up.Length - 1] = SpellNote(tonicLetter, startMidi + 12); // octave

            var descSpelled = ascSpelled.Take(up.Length - 1).Reverse().ToArray();
            return ascSpelled.Concat(descSpelled).ToArray();
        }
        private static int ComputeSpellDiff(char letter, int targetMidi)
        {
            var naturalPC = NaturalPcForLetter(letter);
            var letterOctave = (targetMidi - naturalPC) / 12;
            var naturalMidi = (letterOctave + 1) * 12 + naturalPC;
            var diff = targetMidi - naturalMidi;
            if (diff > 6) diff -= 12;
            else if (diff < -6) diff += 12;
            return diff;
        }
        /// <summary>
        /// Adjusts <paramref name="midi"/> for any key-signature accidental implied by
        /// <paramref name="key"/> when the note name has no explicit accidental.
        /// For example, "B4" in key F (one flat: B♭) returns midi - 1.
        /// Notes that already carry an explicit '#' or 'b' are returned unchanged.
        /// </summary>
        public static int ApplyKeySignatureToMidi(string noteName, int midi, string key, string scale)
        {
            if (string.IsNullOrWhiteSpace(noteName)) return midi;
            var raw = noteName.Trim();
            if (HasExplicitAccidentalInName(raw)) return midi;

            char letter = char.ToUpperInvariant(raw[0]);
            var sigAcc = GetSignatureAccidentalForLetter(letter, GetKeySignatureAccidentalCount(key, scale));
            return sigAcc switch
            {
                "#" => midi + 1,
                "b" => midi - 1,
                _ => midi
            };
        }
        /// <summary>
        /// Adds a key-signature sharp or flat to <paramref name="noteName"/> when the name
        /// has no explicit accidental and the letter is altered by the key signature.
        /// </summary>
        public static string ApplyKeySignatureToSpelledName(string noteName, string key, string scale)
        {
            if (string.IsNullOrWhiteSpace(noteName)) return noteName;
            var raw = noteName.Trim();
            if (HasExplicitAccidentalInName(raw)) return raw;

            char letter = char.ToUpperInvariant(raw[0]);
            string octave = GetOctaveSuffix(raw);
            var sigAcc = GetSignatureAccidentalForLetter(letter, GetKeySignatureAccidentalCount(key, scale));
            return sigAcc switch
            {
                "#" => $"{letter}#{octave}",
                "b" => $"{letter}b{octave}",
                _ => raw
            };
        }
        /// <summary>
        /// Resolves the written MIDI and evaluation label for a staff note, applying
        /// key-signature accidentals when the score omits them (e.g. G on the G line in E major → G#).
        /// </summary>
        public static (int Midi, string Name) ResolveTargetPitch(
            GeneratedNote note, string key, string scale)
        {
            var raw = note.SpelledName.Trim();
            char letter = note.Letter;
            int octave = note.Octave;
            int naturalMidi = NoteNameToMidi($"{letter}{octave}");

            if (note.Accidental == Accidental.Natural)
                return (naturalMidi, raw);

            if (HasExplicitAccidentalInName(raw))
                return (NoteNameToMidi(raw), raw);

            int keySigMidi = ApplyKeySignatureToMidi($"{letter}{octave}", naturalMidi, key, scale);
            if (keySigMidi != naturalMidi)
            {
                var spelled = ApplyKeySignatureToSpelledName($"{letter}{octave}", key, scale);
                return (NoteNameToMidi(spelled), spelled);
            }

            var name = ResolveWrittenNoteName(raw, note.MidiNumber, letter, octave, key, scale);
            return (NoteNameToMidi(name), name);
        }

        /// <summary>
        /// Returns the display/evaluation name for a written note, applying key-signature
        /// spelling when the score omits accidentals that the key signature implies.
        /// </summary>
        public static string ResolveWrittenNoteName(
            string spelledName, int midi, char letter, int octave, string key, string scale)
        {
            if (string.IsNullOrWhiteSpace(spelledName)) return spelledName;
            var raw = spelledName.Trim();
            if (HasExplicitAccidentalInName(raw)) return raw;

            int naturalMidi = NoteNameToMidi($"{letter}{octave}");
            int keySigMidi = ApplyKeySignatureToMidi(raw, naturalMidi, key, scale);

            if (midi == keySigMidi && keySigMidi != naturalMidi)
                return ApplyKeySignatureToSpelledName(raw, key, scale);

            if (midi == naturalMidi)
                return raw;

            return SpellNote(letter, midi);
        }
        /// <summary>
        /// Resolves body accidental and corrected spelling for a generated or imported note.
        /// </summary>
        public static (Accidental Accidental, string SpelledName) ResolveAccidentalAndSpelling(
            string spelledName, int midi, char letter, int octave, string key, string scale)
        {
            var raw = spelledName.Trim();
            if (HasExplicitAccidentalInName(raw))
            {
                Accidental acc = raw.Contains("##") ? Accidental.DoubleSharp
                    : raw.Contains("bb") ? Accidental.DoubleFlat
                    : raw.Contains('#') ? Accidental.Sharp
                    : Accidental.Flat;
                return (acc, raw);
            }

            int naturalMidi = NoteNameToMidi($"{letter}{octave}");
            int keySigMidi = ApplyKeySignatureToMidi(raw, naturalMidi, key, scale);

            if (midi == naturalMidi && keySigMidi != naturalMidi)
                return (Accidental.Natural, raw);

            if (midi == keySigMidi && keySigMidi != naturalMidi)
                return (Accidental.None, ApplyKeySignatureToSpelledName(raw, key, scale));

            return (Accidental.None, raw);
        }
        /// <summary>Circle-of-fifths count for the displayed key signature (scale-aware).</summary>
        public static int GetKeySignatureAccidentalCount(string key, string scale)
            => KeySignatureRules.GetSignedAccidentalCount(key, scale);

        public static string RelativeMajorForKeySignature(string minorKey)
            => KeySignatureRules.RelativeMajorOf(minorKey);
        public string ResolveWrittenNoteName(NoteInfo note)
        {
            var raw = note.Name?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(raw)) return raw;
            char letter = char.ToUpperInvariant(raw[0]);
            int octave = ParseOctaveFromSpelledName(raw);
            var (key, scale) = GetNotationKeyAndScale();
            return ResolveWrittenNoteName(raw, note.Midi, letter, octave, key, scale);
        }
        private static bool HasExplicitAccidentalInName(string raw)
            => raw.Contains("##") || raw.Contains("bb") || raw.Contains('#')
               || (raw.Length > 1 && raw[1] == 'b');
        private static string GetOctaveSuffix(string raw)
        {
            int end = raw.Length - 1;
            int start = end;
            while (start >= 0 && char.IsDigit(raw[start])) start--;
            return start < end ? raw.Substring(start + 1) : "4";
        }
        public static int ParseOctaveFromSpelledName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return 4;
            int end = raw.Length - 1;
            int start = end;
            while (start >= 0 && char.IsDigit(raw[start])) start--;
            return start < end && int.TryParse(raw.AsSpan(start + 1, end - start), out var o) ? o : 4;
        }

        private static string? GetSignatureAccidentalForLetter(char letter, int signatureCount)
            => KeySignatureRules.GetSignatureAccidentalForLetter(letter, signatureCount);
    }
}

