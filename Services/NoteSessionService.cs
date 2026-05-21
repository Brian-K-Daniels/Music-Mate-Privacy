using Microsoft.Maui.Storage;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using musicmate.Models;
using musicmate.Utilities;

namespace musicmate.Services
{
    /// <summary>
    /// Controls which staff renderer is active on the Home / V3 Home page.
    /// </summary>
    public enum StaffDisplayMode
    {
        /// <summary>Original single-note staff (StaffDrawable).</summary>
        Classic,
        /// <summary>V2 scrolling measure staff (V2MeasureDrawable).</summary>
        V2,
        /// <summary>V3 two-staff endless display (V3StaffDrawable — in development).</summary>
        V3
    }

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

    public partial class NoteSessionService : INotifyPropertyChanged
    {
        private static readonly HashSet<string> FreeScales = new() { "Major", "Harmonic Minor" };
        private static readonly HashSet<string> FreeKeys = new() { "C", "F", "Bb", "G", "D" };
        private const string FreeLowestNote = "C4";
        private const string FreeHighestNote = "F5";

        public NoteSessionService()
        {
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

            var notes = WhiteKeyNoteNames.ToList();
            int minIdx = notes.IndexOf(FreeLowestNote);
            int maxIdx = notes.IndexOf(FreeHighestNote);
            if (minIdx >= 0 && notes.IndexOf(LowestNote) is int loIdx && (loIdx < minIdx || loIdx > maxIdx))
                LowestNote = FreeLowestNote;
            if (maxIdx >= 0 && notes.IndexOf(HighestNote) is int hiIdx && (hiIdx < minIdx || hiIdx > maxIdx))
                HighestNote = FreeHighestNote;
        }

        public int SampleRate { get; set; } = 44100;
        public int BufferSize { get; set; } = 4096;

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

        // ── Display mode ─────────────────────────────────────────────────────────
        // StaffDisplayMode is the single source of truth for which staff renderer is
        // active.  Classic = original single-note view; V2 = v2 measure staff;
        // V3 = new two-staff endless display.
        //
        // V2StaffMode is kept as a computed shim so all existing call sites continue
        // to compile and behave correctly without modification.
        private const string PrefStaffDisplayModeKey = "musicmate.StaffDisplayMode";
        private const string PrefV2StaffModeKey      = "musicmate.V2StaffMode";
        private const string PrefV2TimeSignatureKey  = "musicmate.V2TimeSignature";
        private const string PrefV2SmallestNoteKey   = "musicmate.V2SmallestNote";
        private const string PrefV2RhythmModeKey     = "musicmate.V2RhythmMode";
        private const string PrefV2NoteNameDisplayKey = "musicmate.V2NoteNameDisplay";

        private StaffDisplayMode _staffDisplayMode = LoadStaffDisplayMode();

        private static StaffDisplayMode LoadStaffDisplayMode()
        {
            // Migrate from the legacy bool key on first run with the new enum key.
            if (Preferences.ContainsKey(PrefStaffDisplayModeKey))
            {
                var saved = Preferences.Get(PrefStaffDisplayModeKey, nameof(StaffDisplayMode.Classic));
                return Enum.TryParse<StaffDisplayMode>(saved, out var parsed) ? parsed : StaffDisplayMode.Classic;
            }
            // First run: promote the old V2StaffMode bool if it was ever set true.
            if (Preferences.Get(PrefV2StaffModeKey, false))
                return StaffDisplayMode.V2;
            return StaffDisplayMode.Classic;
        }

        /// <summary>
        /// Active staff display mode.  Changing this property persists the choice and
        /// raises <see cref="INotifyPropertyChanged"/> for both
        /// <see cref="StaffDisplayMode"/> and the legacy <see cref="V2StaffMode"/> shim.
        /// </summary>
        public StaffDisplayMode StaffDisplayMode
        {
            get => _staffDisplayMode;
            set
            {
                if (_staffDisplayMode == value) return;
                _staffDisplayMode = value;
                Preferences.Set(PrefStaffDisplayModeKey, value.ToString());
                // Keep the legacy bool key in sync so Settings pages that read it
                // directly via Preferences still reflect the right state.
                Preferences.Set(PrefV2StaffModeKey, value == StaffDisplayMode.V2);
                OnPropertyChanged(nameof(StaffDisplayMode));
                OnPropertyChanged(nameof(V2StaffMode));
                OnPropertyChanged(nameof(StaffDisplayModeDisplay));
            }
        }

        /// <summary>
        /// Legacy compatibility shim.  Backed by <see cref="StaffDisplayMode"/>.
        /// Existing code that reads or writes this bool continues to work unchanged.
        /// Setting it to <c>true</c> selects V2; <c>false</c> selects Classic.
        /// Code that needs V3 should set <see cref="StaffDisplayMode"/> directly.
        /// </summary>
        public bool V2StaffMode
        {
            get => _staffDisplayMode == StaffDisplayMode.V2;
            set => StaffDisplayMode = value ? StaffDisplayMode.V2 : StaffDisplayMode.Classic;
        }

        /// <summary>Human-readable label for the Settings picker.</summary>
        public string StaffDisplayModeDisplay
        {
            get => _staffDisplayMode switch
            {
                StaffDisplayMode.V2 => "V2 Rhythm",
                StaffDisplayMode.V3 => "V3 Two-Staff",
                _                   => "Classic"
            };
            set
            {
                var parsed = value switch
                {
                    "V2 Rhythm"    => StaffDisplayMode.V2,
                    "V3 Two-Staff" => StaffDisplayMode.V3,
                    _              => StaffDisplayMode.Classic
                };
                StaffDisplayMode = parsed;
                OnPropertyChanged(nameof(StaffDisplayModeDisplay));
            }
        }

        public static string[] StaffDisplayModeOptions { get; } =
            { "Classic", "V2 Rhythm", "V3 Two-Staff" };

        private string _v2TimeSignature = Preferences.Get(PrefV2TimeSignatureKey, "4/4");
        private string _v2SmallestNote  = Preferences.Get(PrefV2SmallestNoteKey,  "Quarter");
        private string _v2RhythmMode    = Preferences.Get(PrefV2RhythmModeKey,    "Simple");
        private string _v2NoteNameDisplay = Preferences.Get(PrefV2NoteNameDisplayKey, "Current only");

        /// <summary>
        /// Time signature for v2 rhythm generation.
        /// Persisted value is the display string: "4/4", "3/4", or "2/4".
        /// </summary>
        public string V2TimeSignature
        {
            get => _v2TimeSignature;
            set
            {
                if (_v2TimeSignature == value) return;
                _v2TimeSignature = value;
                Preferences.Set(PrefV2TimeSignatureKey, value);
                OnPropertyChanged(nameof(V2TimeSignature));
            }
        }

        /// <summary>
        /// Smallest note value allowed in v2 rhythm generation.
        /// Persisted value is the display string: "Quarter", "Eighth", or "Sixteenth".
        /// </summary>
        public string V2SmallestNote
        {
            get => _v2SmallestNote;
            set
            {
                if (_v2SmallestNote == value) return;
                _v2SmallestNote = value;
                Preferences.Set(PrefV2SmallestNoteKey, value);
                OnPropertyChanged(nameof(V2SmallestNote));
            }
        }

        /// <summary>
        /// Rhythm variety mode for v2 generation.
        /// "Simple" uses only quarter notes (and half/whole occasionally).
        /// "Mixed" allows the full range of durations up to <see cref="V2SmallestNote"/>.
        /// </summary>
        public string V2RhythmMode
        {
            get => _v2RhythmMode;
            set
            {
                if (_v2RhythmMode == value) return;
                _v2RhythmMode = value;
                Preferences.Set(PrefV2RhythmModeKey, value);
                OnPropertyChanged(nameof(V2RhythmMode));
            }
        }

        /// <summary>
        /// Controls when note names are shown above/below noteheads in the v2 staff.
        /// Values: "Current only", "All notes", "Off".
        /// </summary>
        public string V2NoteNameDisplay
        {
            get => _v2NoteNameDisplay;
            set
            {
                if (_v2NoteNameDisplay == value) return;
                _v2NoteNameDisplay = value;
                Preferences.Set(PrefV2NoteNameDisplayKey, value);
                OnPropertyChanged(nameof(V2NoteNameDisplay));
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
                    Preferences.Set(PrefAccidentalPercentKey, value);
                    OnPropertyChanged(nameof(AccidentalPercent));
                }
            }
        }
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
        private const string PrefTuneKey = "musicmate.Tune";
        private const string PrefPlaybackBpmKey = "musicmate.PlaybackBpm";
        private const string PrefPitchMethodKey = "musicmate.PitchMethod";
        private const string PrefToleranceKey = "musicmate.Tolerance";
        private const string PrefAutoStartKey = "musicmate.AutoStart";
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
        private int _audioBufferSize = Preferences.Get(PrefAudioBufferSizeKey, 1024);
        private bool _autoStart = Preferences.Get(PrefAutoStartKey, true);
        private int _pitchWindowSize = Preferences.Get(PrefPitchWindowSizeKey, 4096);
        private string _highestNote = Preferences.Get("musicmate.HighestNote", "C6");
        private string _lowestNote = Preferences.Get("musicmate.LowestNote", "E3");
        private int _minFrequency = Preferences.Get(PrefMinFrequencyKey, 60);
        private int _maxFrequency = Preferences.Get(PrefMaxFrequencyKey, 8000);
        private int _smoothingWindowSize = Preferences.Get(PrefSmoothingWindowSizeKey, 3);
        private double _pitchConfidenceThreshold = Preferences.Get(PrefPitchConfidenceThresholdKey, 0.5);

        private string _randomSelectedNotesDisplay = string.Empty;

        public int AudioBufferSize
        {
            get => _audioBufferSize;
            set
            {
                if (_audioBufferSize != value)
                {
                    _audioBufferSize = value;
                    Preferences.Set(PrefAudioBufferSizeKey, value);
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
                    Preferences.Set(PrefPitchWindowSizeKey, value);
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
                    Preferences.Set(PrefMinFrequencyKey, value);
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
                    Preferences.Set(PrefMaxFrequencyKey, value);
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
                    Preferences.Set(PrefSmoothingWindowSizeKey, value);
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
                    Preferences.Set(PrefPitchConfidenceThresholdKey, value);
                    OnPropertyChanged(nameof(PitchConfidenceThreshold));
                }
            }
        }

        private string _instrument = Preferences.Get(PrefInstrumentKey, "Bb");
        private string _key = Preferences.Get(PrefKeySignatureKey, "C");
        private string _selectedScale = Preferences.Get(PrefSelectedScaleKey, "Major");
        private string? _tune = Preferences.Get(PrefTuneKey, "Selected Scale");
        private int _playbackBpm = Preferences.Get(PrefPlaybackBpmKey, 100);
        private int _tolerance = Preferences.Get(PrefToleranceKey, 50);
        private int _accidentalPercent = Preferences.Get(PrefAccidentalPercentKey, 0);
        private int _correctThreshold = Preferences.Get(PrefCorrectThresholdKey, 50);
        private double _pitchOffsetCents = Preferences.Get(PrefPitchOffsetCentsKey, 0.0);

        public double PitchOffsetCents
        {
            get => _pitchOffsetCents;
            set
            {
                if (Math.Abs(_pitchOffsetCents - value) > 0.01)
                {
                    _pitchOffsetCents = value;
                    Preferences.Set(PrefPitchOffsetCentsKey, value);
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
                Preferences.Set(PrefWrongDebounceMsKey, _wrongDebounceMs);
                OnPropertyChanged(nameof(WrongDebounceMs));
            }
        }
        private readonly Dictionary<string, (int Correct, int Wrong, double TotalMs, int MsCount)> _randomSessionNoteStats = new();
        private DateTime? _lastCorrectNoteUtc;
        private const string PrefWrongDebounceMsKey = "musicmate.WrongDebounceMs";
        public const int DefaultDebounceMs = 300;
        private int _wrongDebounceMs = Preferences.Get(PrefWrongDebounceMsKey, DefaultDebounceMs);
        // Track last wrong timestamp per note index to debounce rapid wrong increments
        private readonly Dictionary<int, DateTime> _lastWrongTimePerIndex = new();
        // Track last wrong timestamp per written name for session stats debouncing
        private readonly Dictionary<string, DateTime> _lastRandomWrongUtc = new();

        private const string PrefMasteredMethodKey = "musicmate.MasteredMethod";
        private const string PrefStreakCritKey = "musicmate.StreakCrit";
        private string _masteredMethod = Preferences.Get(PrefMasteredMethodKey, "% Correct");
        private int _streakCrit = Preferences.Get(PrefStreakCritKey, 3);

        public string MasteredMethod
        {
            get => _masteredMethod;
            set
            {
                if (_masteredMethod != value)
                {
                    _masteredMethod = value;
                    Preferences.Set(PrefMasteredMethodKey, value);
                    OnPropertyChanged(nameof(MasteredMethod));
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
                    Preferences.Set(PrefStreakCritKey, clamped);
                    OnPropertyChanged(nameof(StreakCrit));
                }
            }
        }

        // Per-session streak tracking: written name → current consecutive correct count
        private readonly Dictionary<string, int> _sessionStreaks = new();

        public void RecordRandomSessionNoteResult(string writtenName, bool correct)
        {
            if (!_randomSessionNoteStats.TryGetValue(writtenName, out var stat))
                stat = (0, 0, 0.0, 0);

            var now = DateTime.UtcNow;

            if (correct)
            {
                Utils.Log($"[Stats] RecordRandomSessionNoteResult CORRECT for {writtenName} (before: C={stat.Correct}, W={stat.Wrong})");
                stat.Correct++;
                if (_lastCorrectNoteUtc.HasValue)
                {
                    var intervalMs = (now - _lastCorrectNoteUtc.Value).TotalMilliseconds;
                    stat.TotalMs += intervalMs;
                    stat.MsCount++;
                }
                _lastCorrectNoteUtc = now;
                // Clear any debounce for this written note so future wrongs are counted
                _lastRandomWrongUtc.Remove(writtenName);
                // Increment session streak
                _sessionStreaks[writtenName] = _sessionStreaks.GetValueOrDefault(writtenName, 0) + 1;
                Utils.Log($"[Stats] RecordRandomSessionNoteResult updated CORRECT for {writtenName} (after: C={stat.Correct}, W={stat.Wrong}, Streak={_sessionStreaks[writtenName]})");
            }
            else
            {
                // Debounce rapid wrong increments for the same writtenName
                if (_lastRandomWrongUtc.TryGetValue(writtenName, out var lastWrong) && (now - lastWrong).TotalMilliseconds < _wrongDebounceMs)
                {
                    Utils.Log($"[Stats] RecordRandomSessionNoteResult DEBOUNCED wrong for {writtenName} (last at {lastWrong:O})");
                    // skip increment
                }
                else
                {
                    Utils.Log($"[Stats] RecordRandomSessionNoteResult WRONG for {writtenName} (before: C={stat.Correct}, W={stat.Wrong})");
                    stat.Wrong++;
                    _lastRandomWrongUtc[writtenName] = now;
                    Utils.Log($"[Stats] RecordRandomSessionNoteResult updated WRONG for {writtenName} (after: C={stat.Correct}, W={stat.Wrong})");
                }
            }

            _randomSessionNoteStats[writtenName] = stat;
        }
        public Dictionary<string, (int Correct, int Wrong, double TotalMs, int MsCount)> GetAndClearRandomSessionNoteStats()
        {
            var copy = new Dictionary<string, (int Correct, int Wrong, double TotalMs, int MsCount)>(_randomSessionNoteStats);
            _randomSessionNoteStats.Clear();
            return copy;
        }

        public Dictionary<string, int> GetSessionStreaks()
        {
            return new Dictionary<string, int>(_sessionStreaks);
        }

        /// <summary>
        /// Returns the set of written-pitch MIDI numbers that the player has mastered,
        /// using the same criteria as v1 Random mode mastery filtering.
        /// Used by v2 sequence generation to exclude mastered notes.
        /// </summary>
        public async Task<HashSet<int>> GetMasteredMidiNumbersAsync()
        {
            var result = new HashSet<int>();
            try
            {
                var db = ServiceHelper.GetService<NoteDatabase>();
                if (db == null) return result;
                await db.InitializeAsync();

                var statsList = await db.GetAllAsync();
                foreach (var stat in statsList)
                {
                    bool mastered;
                    if (MasteredMethod == "Streak")
                    {
                        mastered = stat.Streak >= StreakCrit;
                    }
                    else
                    {
                        mastered = stat.PercentCorrect >= CorrectThreshold
                                   && stat.Correct >= MinCorrectCount;
                        if (mastered && OmitMsAvgThreshold > 0 && stat.MsCount > 0)
                            mastered = stat.MsAverage < OmitMsAvgThreshold;
                    }

                    if (!mastered) continue;

                    int midi = NoteNameToMidi(stat.WrittenName);
                    if (midi > 0) result.Add(midi);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Session] GetMasteredMidiNumbersAsync ERROR: {ex}");
            }
            return result;
        }
        public (double sumCorrects, double sumWrongs, double adjustedPercentCorrect) GetSessionCorrectWrongTotals()
        {
            double sumCorrects = CorrectNoteIndices.Count;
            double sumWrongs = NoteFeedbacks.Values.Sum(v => v.Wrong);
            double rpc = 100 * sumCorrects / (sumCorrects + sumWrongs); // Raw Percent Correct
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
            get => _lowestNote;
            set
            {
                if (_lowestNote != value)
                {
                    _lowestNote = value;
                    Preferences.Set("musicmate.LowestNote", _lowestNote);
                    OnPropertyChanged(nameof(LowestNote));
                    if (Tune == "Random")
                    {
                        _ = UpdateRandomSelectedNotesDisplayAsync();
                    }
                }
            }
        }

        public string HighestNote
        {
            get => _highestNote;
            set
            {
                if (_highestNote != value)
                {
                    _highestNote = value;
                    Preferences.Set("musicmate.HighestNote", _highestNote);
                    OnPropertyChanged(nameof(HighestNote));
                    if (Tune == "Random")
                    {
                        _ = UpdateRandomSelectedNotesDisplayAsync();
                    }
                }
            }
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
                Preferences.Set(PrefCorrectThresholdKey, _correctThreshold);
                OnPropertyChanged(nameof(CorrectThreshold));
            }
        }

        private static bool IsEnharmonicToAdjacent(string accidentalNote, List<string> sequence, int index)
        {
            int midi = NoteNameToMidi(accidentalNote);
            // Check previous note
            if (index > 0 && NoteNameToMidi(sequence[index - 1]) == midi)
                return true;
            // Check next note
            if (index < sequence.Count - 1 && NoteNameToMidi(sequence[index + 1]) == midi)
                return true;
            return false;
        }

        public static readonly string[] InstrumentOptions = 
        {
            "C + 2 octaves, Glockenspiel",                      // +24 semitones
            "C + 1 octave,  Piccolo",                           // +12 semitones
            "Eb,            Clarinet",                          // +3 semitones
            "C, Flute Oboe Bassoon Trumpet Trombone Euphoneum Tuba Piano", // 0 semitones
            "Bb,            Clarinet, Soprano Sax, Trumpet",    // -2 semitones
            "A,             Clarinet",                          // -3 semitones
            "F,             English Horn, French Horn",         // -7 semitones
            "Eb,            Alto Clarinet, Alto Sax",           // -9 semitones
            "C - 1 octave,  Double Bass, Contrabassoon",        // -12 semitones
            "Bb - 1 octave, Tenor Sax, Bass Clarinet",          // -14 semitones
            "Eb - 1 octave, Baritone Sax"                       // -21 semitones
        };

        private static readonly int[] InstrumentTransposeOffsets = new[]
        {
            24,  // C + 2 octaves, Glockenspiel
            12,  // C + 1 octave,  Piccolo
            3,   // Eb,            Clarinet
            0,   // C, Flute Oboe Bassoon Trumpet Trombone Euphoneum Tuba Piano
            -2,  // Bb,            Clarinet, Soprano Sax, Trumpet
            -3,  // A,             Clarinet
            -7,  // F,             English Horn, French Horn
            -9,  // Eb,            Alto Clarinet, Alto Sax
            -12, // C - 1 octave,  Double Bass, Contrabassoon
            -14, // Bb - 1 octave, Tenor Sax, Bass Clarinet
            -21  // Eb - 1 octave, Baritone Sax
        };

        private int GetInstrumentTransposeOffset()
        {
            int idx = Array.IndexOf(InstrumentOptions, Instrument);
            return (idx >= 0 && idx < InstrumentTransposeOffsets.Length) ? InstrumentTransposeOffsets[idx] : 0;
        }

        private static HashSet<int> GetUnadornedNoteMidis(IEnumerable<string> scaleNotes)
        {
            var set = new HashSet<int>();
            foreach (var note in scaleNotes)
            {
                var baseName = new string(note.TakeWhile(c => !char.IsDigit(c)).ToArray());
                if (!baseName.Contains('#') && !baseName.Contains('b'))
                {
                    set.Add(NoteNameToMidi(note));
                }
            }
            return set;
        }

        public string BpmStatsDisplay =>
            _meanBpm is not null && _stddevBpm is not null
                ? $"Mean BPM: {_meanBpm.Value:F1} (±{_stddevBpm.Value:F1})"
                : "BPM: N/A";

        private void NotifyBpmStatsChanged()
        {
            OnPropertyChanged(nameof(BpmStatsDisplay));
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
        private const string PrefMinCorrectCountKey = "musicmate.MinCorrectCount";
        private int _minCorrectCount = Preferences.Get(PrefMinCorrectCountKey, 3);

        private const string PrefOmitMsAvgThresholdKey = "musicmate.OmitMsAvgThreshold";
        private int _omitMsAvgThreshold = Preferences.Get(PrefOmitMsAvgThresholdKey, 500);
        public int OmitMsAvgThreshold
        {
            get => _omitMsAvgThreshold;
            set
            {
                var clamped = Math.Clamp(value, 0, 5000);
                if (_omitMsAvgThreshold != clamped)
                {
                    _omitMsAvgThreshold = clamped;
                    Preferences.Set(PrefOmitMsAvgThresholdKey, clamped);
                    OnPropertyChanged(nameof(OmitMsAvgThreshold));
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
                    Preferences.Set(PrefMinCorrectCountKey, clamped);
                    OnPropertyChanged(nameof(MinCorrectCount));
                }
            }
        }
        private async Task UpdateRandomSelectedNotesDisplayAsync()
        {
            if (Tune == "Random")
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
                if (_instrument == value) return;
                _instrument = value;
                Preferences.Set(PrefInstrumentKey, _instrument);
                OnPropertyChanged(nameof(Instrument));
            }
        }
        public string Key
        {
            get => _key;
            set
            {
                if (_key == value) return;
                _key = value;
                Preferences.Set(PrefKeySignatureKey, _key);
                OnPropertyChanged(nameof(Key));
            }
        }
        public string SelectedScale
        {
            get => _selectedScale;
            set
            {
                if (_selectedScale == value || string.IsNullOrWhiteSpace(value)) return;
                _selectedScale = value;
                Preferences.Set(PrefSelectedScaleKey, _selectedScale);
                OnPropertyChanged(nameof(SelectedScale));
            }
        }
        public int PlaybackBpm
        {
            get => _playbackBpm;
            set
            {
                var clamped = Math.Clamp(value, 30, 400);
                if (_playbackBpm == clamped)
                {
                    return;
                }
                _playbackBpm = clamped;
                Preferences.Set(PrefPlaybackBpmKey, _playbackBpm);
                OnPropertyChanged(nameof(PlaybackBpm));
            }
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
                Preferences.Set(PrefToleranceKey, _tolerance);
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
                Preferences.Set(PrefAutoStartKey, value);
                OnPropertyChanged(nameof(AutoStart)); 
            }
        }

        private float _rmsThreshold = 0.025f;
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
        public bool OneOctaveMode { get; set; } = true;
        public int NoteAdvanceIgnoreMs { get; set; } = 200;
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
        private enum AccidentalPreference    { Auto, Sharps, Flats }
        public static readonly string[] AvailableScales = new[]
        {
            "Major",  "Harmonic Minor", "Melodic Minor", "Natural Minor", "Dorian", "Phrygian",
            "Lydian", "Mixolydian", "Locrian", "Major Pentatonic", "Minor Pentatonic", "Blues",
            "Chromatic"
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

        // Timing and BPM stats
        private readonly Stopwatch _noteStopwatch = new();
        private readonly List<double> _intervalsSeconds = new();
        private double? _meanBpm;
        private double? _stddevBpm;
        public string? Tune
        {
            get => _tune;
            set
            {
                if (_tune != value)
                {
                    _tune = value;
                    Preferences.Set(PrefTuneKey, value);
                    OnPropertyChanged(nameof(Tune));
                }
            }
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
            _pitchMedianHistory.Clear();

            // Clear timing data and stats
            _intervalsSeconds.Clear();
            _meanBpm = null;
            _stddevBpm = null;
            _lastCorrectNoteUtc = null;
            OmitMsAvgThreshold = Preferences.Get(PrefOmitMsAvgThresholdKey, 500);
            _lastWrongTimePerIndex.Clear();
            _lastRandomWrongUtc.Clear();
            _randomSessionNoteStats.Clear();
            _sessionStreaks.Clear();
            _tunerPrevWrittenMidi = null;// reset direction tracking for next session
            // ensure persisted value is reloaded
            _wrongDebounceMs = Preferences.Get(PrefWrongDebounceMsKey, DefaultDebounceMs);
            _noteStopwatch.Reset();
            _noteStopwatch.Start();
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
                if (_noteStopwatch.IsRunning)
                {
                    _noteStopwatch.Stop();
                }
            }
            catch
            {
                // Swallow exceptions to keep stop operation best-effort
            }
        }
        public void RecordIntervalIfNeeded()
        {
          if (_noteStopwatch.IsRunning && CurrentNoteIndex > 0 && _intervalsSeconds.Count < NotesToDraw.Count - 1)
          {
            var elapsed = _noteStopwatch.Elapsed.TotalSeconds;
            _intervalsSeconds.Add(elapsed);
            _noteStopwatch.Restart();
          }
          else if (!_noteStopwatch.IsRunning)
          {
            _noteStopwatch.Restart();
          }
        }
        public void FinalizeSessionStats()
        {
            //Utils.Log("FinalizeSessionStats");
            if (_intervalsSeconds.Count < 1)
            {
                _meanBpm = null;
                _stddevBpm = null;
                NotifyBpmStatsChanged();
                return;
            }
            var bpms = _intervalsSeconds.Select(sec => sec > 0 ? 60.0 / sec : 0).Where(bpm => bpm > 0).ToArray();
            if (bpms.Length == 0)
            {
                _meanBpm = null;
                _stddevBpm = null;
                NotifyBpmStatsChanged();
                return;
            }
            var mean = bpms.Average();
            var stddev = Math.Sqrt(bpms.Select(bpm => Math.Pow(bpm - mean, 2)).Average());
            _meanBpm = mean;
            _stddevBpm = stddev;
            NotifyBpmStatsChanged();
        }
        public (double? MeanBpm, double? StdDevBpm) GetFinalBpmStats()
        {
            return (_meanBpm, _stddevBpm);
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
            string expectedNote = (CurrentNoteIndex < NotesToDraw.Count) ? NotesToDraw[CurrentNoteIndex].Name : "-";

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
                    && Mod12(targetNote.Midi) != _lockedPitchClassAfterAdvance.Value)
                {
                    return false;
                }
                _lockedPitchClassAfterAdvance = null;
            }

            // If a silence gap is required (consecutive same-pitch notes), block until
            // silence clears the flag via NotifySilence().
            if (_requireSilenceBeforeNote)
                return false;

            StatusService.Instance.StatusMessage = $"Expected: {expectedNote}, Heard: {heardNote}, {result.cents}¢, Notes: {NotesToDraw.Count}";

            var curFeedback = NoteFeedbacks.TryGetValue(idx, out var v2) ? v2 : (Wrong: 0, Cents: 0);

            // Only match if the detected pitch class matches the current note's pitch class
            if (Mod12(targetNote.Midi) != detectedPcWritten)
            {
                // Debounce trailing wrong increments (pitch class matched but out of tolerance)
                var nowTrailing = DateTime.UtcNow;
                if (_lastWrongTimePerIndex.TryGetValue(idx, out var lastTrailing)
                    && (nowTrailing - lastTrailing).TotalMilliseconds < _wrongDebounceMs)
                {
                    //Utils.Log($"[Feedback] Debounced trailing wrong for index={idx}, note={targetNote.Name}");
                    return false;
                }
                _lastWrongTimePerIndex[idx] = nowTrailing;

                // Update feedback for incorrect attempt: only increment wrong, do not update cents
                //Utils.Log($"[Feedback] Incrementing trailing wrong for index={idx}, note={targetNote.Name} (before={curFeedback.Wrong})");
                curFeedback = (Wrong: curFeedback.Wrong + 1, Cents: result.cents);
                NoteFeedbacks[idx] = curFeedback;
                FeedbackViewModels[idx] = new FeedbackItem(idx, curFeedback.Wrong, curFeedback.Cents, false);
                //Utils.Log($"[Feedback] Updated trailing wrong for index={idx}, note={targetNote.Name} (after={curFeedback.Wrong})");
                return true;
            } 
            if (result.correct)
            {
                // Timing: record interval (skip first note)
                RecordIntervalIfNeeded();

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
                    FinalizeSessionStats();
                    _ = SessionCompletedAsync?.Invoke();
                }
                else if (Tune == "Practice Tune"
                         && Mod12(NotesToDraw[CurrentNoteIndex].Midi) == detectedPcWritten)
                {
                    // Next note has the same pitch class — require a silence gap so the
                    // sustained audio from this note cannot auto-trigger the next one.
                    _requireSilenceBeforeNote = true;
                }
                return true;
            }

            // Update feedback for incorrect attempt: only increment wrong, do not update cents
            //Utils.Log($"[Feedback] Incrementing trailing wrong for index={idx}, note={targetNote.Name} (before={curFeedback.Wrong})");
            curFeedback = (Wrong: curFeedback.Wrong + 1, Cents: curFeedback.Cents);
            NoteFeedbacks[idx] = curFeedback;
            FeedbackViewModels[idx] = new FeedbackItem(idx, curFeedback.Wrong, curFeedback.Cents, false);
            //Utils.Log($"[Feedback] Updated trailing wrong for index={idx}, note={targetNote.Name} (after={curFeedback.Wrong})");
            return true;
        }
        private async Task<string[]> BuildRandomSequenceAsync()
        {
            // 1. Build all notes in the scale between LowestNote and HighestNote (inclusive)
            var availableNotes = new List<string>();
            for (int midi = NoteNameToMidi(LowestNote); midi <= NoteNameToMidi(HighestNote); midi++)
            {
                string noteName = MidiToNoteName(midi, KeyUsesFlats(Key));
                if (noteName.Length > 0 && BuildScaleDegrees(Key, SelectedScale)
                        .Contains(noteName.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9')))
                {
                    availableNotes.Add(noteName);
                }
            }

            availableNotes = availableNotes.Distinct().ToList();

            if (availableNotes.Count < 2)
                return availableNotes.ToArray();

            // Get stats from database asynchronously
            var db = ServiceHelper.GetService<NoteDatabase>();
            if (db == null)
            {
                Utils.Log("NoteDatabase service is not registered.");
                return Array.Empty<string>();
            }
            await db.InitializeAsync();

            var statsList = await db.GetAllAsync();
            var stats = statsList.ToDictionary(s => s.WrittenName, s => s);

            // Exclude mastered notes based on MasteredMethod
            availableNotes = availableNotes
                .Where(note =>
                {
                    if (stats.TryGetValue(note, out var stat))
                    {
                        if (MasteredMethod == "Streak")
                        {
                            if (stat.Streak >= StreakCrit)
                                return false;
                        }
                        else // % Correct
                        {
                            if (stat.PercentCorrect >= CorrectThreshold && stat.Correct >= MinCorrectCount)
                                return false;
                            if (OmitMsAvgThreshold > 0 && stat.MsCount > 0 && stat.MsAverage < OmitMsAvgThreshold)
                                return false;
                        }
                    }
                    return true;
                })
                .ToList();

            // If filtering removed all notes or left only one, keep what we have if possible,
            // otherwise fall back to the full unfiltered pool (all notes need more practice)
            if (availableNotes.Count < 2)
            {
                // Build the full note pool for this range/scale
                var fullPool = new List<string>();
                for (int midi = NoteNameToMidi(LowestNote); midi <= NoteNameToMidi(HighestNote); midi++)
                {
                    string noteName = MidiToNoteName(midi, KeyUsesFlats(Key));
                    if (noteName.Length > 0 && BuildScaleDegrees(Key, SelectedScale)
                            .Contains(noteName.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9')))
                    {
                        fullPool.Add(noteName);
                    }
                }
                fullPool = fullPool.Distinct().ToList();

                if (fullPool.Count >= 2)
                {
                    // Pick the least-mastered notes: sort by streak ascending (or percent correct)
                    // so we review the weakest notes rather than including fully mastered ones
                    availableNotes = fullPool
                        .OrderBy(note =>
                        {
                            if (stats.TryGetValue(note, out var s))
                                return MasteredMethod == "Streak" ? s.Streak : (int)s.PercentCorrect;
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

            // Remove enharmonic boundary notes that would be out of range when respelled
            // (e.g. Cb4 if lowest is C4, or B#5 if highest is B5)
            string lowestNote = LowestNote;
            string highestNote = HighestNote;

            string flatOfLowest = "";
            if (lowestNote.Length > 1 && !lowestNote.Contains("#") && !lowestNote.Contains("b"))
                flatOfLowest = lowestNote[0] + "b" + lowestNote.Substring(1);

            string sharpOfHighest = "";
            if (highestNote.Length > 1 && !highestNote.Contains("#") && !highestNote.Contains("b"))
                sharpOfHighest = highestNote[0] + "#" + highestNote.Substring(1);

            availableNotes = availableNotes
                .Where(n => n != flatOfLowest && n != sharpOfHighest)
                .ToList();

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
                var candidates = new List<(int idx, int weight)>();
                foreach (var nextIdx in unused)
                {
                    int interval = Math.Abs(nextIdx - currentIdx);
                    if (interval == 0) continue;
                    if (intervalWeights.TryGetValue(interval, out int weight))
                        candidates.Add((nextIdx, weight));
                }

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
                else
                {
                    chosenIdx = unused[rand.Next(unused.Count)];
                }

                result.Add(availableNotes[chosenIdx]);
                unused.Remove(chosenIdx);
                currentIdx = chosenIdx;
            }

            // --- Accidental logic ---
            // Use all scale-note MIDIs to detect enharmonic collisions
            var allScaleNoteMidis = new HashSet<int>(availableNotes.Select(n => NoteNameToMidi(n)));

            if (StatusService.Instance.IsPremiumUser && AccidentalPercent > 0 && result.Count > 0)
            {
                int count = (int)Math.Round(result.Count * AccidentalPercent / 100.0);
                var indices = Enumerable.Range(0, result.Count).OrderBy(_ => rand.Next()).Take(count).ToList();
                var (flatPcs, sharpPcs) = GetAccidentalSetsForScale(Key, SelectedScale);

                for (int i = 0; i < result.Count; i++)
                {
                    if (!indices.Contains(i))
                        continue;

                    var note = result[i];
                    var baseName = new string(note.TakeWhile(c => !char.IsDigit(c)).ToArray());
                    var octave = new string(note.SkipWhile(c => !char.IsDigit(c)).ToArray());
                    var letter = char.ToUpperInvariant(baseName[0]).ToString();

                    string candidateNote;
                    if (baseName.Contains('#'))
                    {
                        // Key-sig sharp tone → naturalize (displays as ♮)
                        candidateNote = letter + octave;
                    }
                    else if (baseName.Contains('b'))
                    {
                        // Key-sig flat tone → naturalize (displays as ♮)
                        candidateNote = letter + octave;
                    }
                    else
                    {
                        // Natural scale tone → add chromatic accidental away from key-sig tendency
                        var pc = Mod12(NoteNameToMidi(note));
                        string accidental;
                        if (flatPcs.Contains(pc))
                            accidental = "#";
                        else if (sharpPcs.Contains(pc))
                            accidental = "b";
                        else
                            accidental = rand.Next(2) == 0 ? "#" : "b";
                        candidateNote = letter + accidental + octave;
                    }

                    if (string.IsNullOrEmpty(candidateNote) || candidateNote == note)
                        continue;

                    int candidateMidi = NoteNameToMidi(candidateNote);
                    int minMidi = NoteNameToMidi(LowestNote);
                    int maxMidi = NoteNameToMidi(HighestNote);

                    // Reject if enharmonically identical to any scale tone, or out of range
                    if (allScaleNoteMidis.Contains(candidateMidi) || candidateMidi < minMidi || candidateMidi > maxMidi)
                        continue;

                    result[i] = candidateNote;
                }
            }

            return result.ToArray();
        }
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
                            var freq = MidiToFreq(mn.MidiNumber);
                            var noteIdx = NotesToDraw.Count;
                            NotesToDraw.Add(new NoteInfo
                            {
                                Midi       = mn.MidiNumber,
                                Name       = mn.SpelledName,
                                TargetFreq = freq,
                                X          = slotX,
                                Duration   = mn.Duration
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
            if (Tune == "Random")
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
        private static string GetNoteName(int midi, AccidentalPreference pref)
        {
            var namesSharp = new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            var namesFlat = new[] { "C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B" };
            var pc = ((midi % 12) + 12) % 12;
            var oct = (midi / 12) - 1;
            var baseName = pref == AccidentalPreference.Flats ? namesFlat[pc] : namesSharp[pc];
            return $"{baseName}{oct}";
        }
        public (bool correct, int cents) Evaluate(double freq)
        {
            if (NotesToDraw.Count == 0 || CurrentNoteIndex >= NotesToDraw.Count || freq <= 0)
            {
                return (false, 0);
            }

            var target = NotesToDraw[CurrentNoteIndex];
            var detMidi = (int)Math.Round(69 + 12 * Math.Log(freq / 440.0, 2));

            // Transpose detected MIDI to written pitch for the selected instrument
            var detMidiWritten = detMidi - GetInstrumentTransposeOffset();
            var detPcWritten = Mod12(detMidiWritten);

            // Compare to the written note's pitch class
            var expectedPc = Mod12(target.Midi);
            var correctPc = detPcWritten == expectedPc;

            // Enharmonic check: allow E4 == Fb4, etc.
            bool enharmonicMatch = false;
            if (!correctPc)
            {
                // Get all enharmonic MIDI numbers for the target note
                var enharmonicMidis = GetEnharmonicMidis(target.Midi);
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
            if(note != null)
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
        /// <summary>
        /// Returns the pitch-detection window size (in samples) that gives at least
        /// <paramref name="minPeriods"/> complete periods at <paramref name="targetFreq"/>.
        /// Result is rounded up to the next power of two and clamped to [512, PitchWindowSize]
        /// so the user-configured ceiling is respected.
        /// </summary>
        public int ComputeWindowSizeForFreq(double targetFreq, int minPeriods = 8)
        {
            if (targetFreq <= 0) return PitchWindowSize;
            int periodsNeeded = (int)Math.Ceiling(SampleRate / targetFreq * minPeriods);
            // Round up to next power of 2 for autocorrelation-friendly sizing
            int p = 512;
            while (p < periodsNeeded) p <<= 1;
            // User-configured PitchWindowSize is the ceiling; floor is 512
            return Math.Clamp(p, 512, PitchWindowSize);
        }
        public static string MidiToNoteName(int midi, bool flats)
        {
            var namesSharp = new[] { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
            var namesFlat = new[] { "C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B" };
            var pc = ((midi % 12) + 12) % 12;
            var oct = (midi / 12) - 1;
            return $"{(flats ? namesFlat : namesSharp)[pc]}{oct}";
        }
        private static bool KeyUsesFlats(string key)
        {
            return key is "F" or "Bb" or "Eb" or "Ab" or "Db" or "Gb" or "Cb";
        }
        private int ApplyInstrumentTranspose(int concertMidi)
        {
            return concertMidi - GetInstrumentTransposeOffset();
        }
        private static string TransposeKey(string key, int semitones)
        {
            var midi = NoteNameToMidi($"{key}4") + semitones;
            return MidiToNoteName(midi, KeyUsesFlats(key)).TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        }
        private static string[] RespellToAvoidConsecutiveSameLetter(string[] notes, bool preferFlats)
        {
            if (notes.Length == 0)
            {
                return Array.Empty<string>();
            }

            var result = new string[notes.Length];
            result[0] = notes[0];

            for (var i = 1; i < notes.Length; i++)
            {
                var prev = result[i - 1];
                var cur = notes[i];
                var prevLetter = char.ToUpperInvariant(prev[0]);
                var curLetter = char.ToUpperInvariant(cur[0]);

                if (prevLetter != curLetter)
                {
                    result[i] = cur;
                    continue;
                }

                var midi = NoteNameToMidi(cur);
                var flatsName = MidiToNoteName(midi, true);
                var sharpsName = MidiToNoteName(midi, false);
                var candidate = preferFlats ? flatsName : sharpsName;
                if (char.ToUpperInvariant(candidate[0]) == prevLetter)
                {
                    candidate = preferFlats ? sharpsName : flatsName;
                }

                result[i] = candidate;
            }

            return result;
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
                var ascending  = up.Select(d => GetNoteName(startMidi + d, AccidentalPreference.Sharps));
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
                    Preferences.Set("musicmate.AppBackgroundColor", value.ToArgbHex());
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
                    Preferences.Set("musicmate.PanelBackgroundColor", value.ToArgbHex());
                    OnPropertyChanged(nameof(PanelBackgroundColor));
                }
            }
        }
        private static Color GetColorPreference(string key, Color fallback)
        {
            var hex = Preferences.Get(key, fallback.ToArgbHex());
            try
            {
                return Color.FromArgb(hex);
            }
            catch
            {
                return fallback;
            }
        }
        public static Color GetHighContrastColor(Color background)
        {
            // Use luminance to determine contrast
            double luminance = 0.299 * background.Red + 0.587 * background.Green + 0.114 * background.Blue;
            return luminance > 0.5 ? Colors.Black : Colors.White;
        }

        public Color ContrastingTextColor
        {
            get
            {
                var bg = PanelBackgroundColor;
                double luminance = 0.299 * bg.Red + 0.587 * bg.Green + 0.114 * bg.Blue;
                return luminance > 0.5 ? Colors.Black : Colors.White;
            }
        }

        /// <summary>
        /// Sets <see cref="CurrentTune"/> and switches <see cref="Tune"/> to "Practice Tune"
        /// so the next call to <see cref="GenerateNotesAsync"/> will use the supplied tune.
        /// </summary>
        public void SelectPracticeTune(PracticeTune tune)
        {
            CurrentTune = tune ?? throw new ArgumentNullException(nameof(tune));
            Tune = "Practice Tune";
            OnPropertyChanged(nameof(CurrentTune));
        }

        public List<string> TuneOptions { get; } = new() { "Selected Scale", "Random", "Tuner", "Practice Tune" };

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
        private static int NaturalPcForLetter(char letter) => letter switch
        {
            'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5,
            'G' => 7, 'A' => 9, 'B' => 11, _ => 0
        };

        /// <summary>
        /// Spells one MIDI note using the given letter, computing the correct octave and
        /// accidental by comparing <paramref name="targetMidi"/> with the nearest natural
        /// pitch of that letter.
        /// </summary>
        private static string SpellNote(char letter, int targetMidi)
        {
            var naturalPC  = NaturalPcForLetter(letter);
            var letterOctave = (targetMidi - naturalPC) / 12;
            var naturalMidi  = (letterOctave + 1) * 12 + naturalPC;
            var diff = targetMidi - naturalMidi;

            // Correct for octave boundary: natural is in an adjacent octave
            if      (diff >  6) { diff -= 12; letterOctave++; }
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
            int  tonicMidi,
            int[] up,
            int[]? letterOffsets)
        {
            var tonicIdx    = Array.IndexOf(Letters, tonicLetter);
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
    }
}

