using CommunityToolkit.Maui.Alerts;
using musicmate.Controls;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;
using musicmate.Utilities;
using System.ComponentModel;
using System.Diagnostics;

namespace musicmate.Pages
{
    public partial class MainPage : ContentPage
    {
       
        private readonly NoteSessionService _session = null!;
        private readonly IAudioCaptureService _audio = null!;
        private readonly IAudioPlaybackService _player = null!;
        private readonly Drawables.StaffDrawable _drawable = null!;
        private Drawables.V2MeasureDrawable? _v2Drawable;
        private readonly SessionDatabase _sessionDb = null!;
        private readonly IOrientationService _orientation = null!;
        private readonly ThemeService _theme_service = null!;

        private readonly object _processLock = new();
        private DateTime _lastProcess = DateTime.MinValue;
        private CancellationTokenSource? _playCts;
        private bool _isPlaying = false;
        private bool _isRunning = false;
        private bool _isBelowThreshold = true;
        private bool _isProgrammaticColorConfirm = false;
        private string? _savedInstrumentForPlayback = null;
        private int _savedInstrumentIndexForPlayback = -1;

        // fields for inactivity tracking
        private DateTime _lastHeardTime = DateTime.UtcNow;
        private bool _inactivityStopped = false;
        private readonly TimeSpan _inactivityTimeout = TimeSpan.FromMinutes(5);

        // When true, RegenerateNotesAsync is suppressed so the post-autoplay
        // green feedbacks and session stats remain visible until the next session.
        private bool _freezeStaff = false;

        /// <summary>
        /// Apply saved panel background color at startup. If no saved color exists,
        /// reset ColorPickerDialog to defaults, use its preview color, persist it,
        /// and apply.
        /// </summary>
        private void DeploySavedPanelBackground()
        {
            try
            {
                var savedColorHex = Preferences.Default.Get<string?>("StaffPanelColor", null);
                if (!string.IsNullOrEmpty(savedColorHex))
                {
                    var savedColor = Color.FromArgb(savedColorHex);
                    _theme_service.PanelBackgroundColor = savedColor;
                    StaffBorder.Background = new SolidColorBrush(savedColor);
                    return;
                }

                var dialog = this.FindByName<Controls.ColorPickerDialog>("ColorPickerDialog");
                if (dialog != null)
                {
                    dialog.ResetToDefaults();
                    var preview = dialog.PreviewColor;
                    _theme_service?.PanelBackgroundColor = preview;
                    Preferences.Default.Set("StaffPanelColor", preview.ToHex());
                    StaffBorder.Background = new SolidColorBrush(preview);
                }
            }
            catch (Exception ex)
            {
                Utils.Log($"[DeploySavedPanelBackground] ERROR: {ex}");
            }
        }

        private float[] _pitchBuffer = Array.Empty<float>();
        private int _pitchBufferPos = 0;

        private static readonly HashSet<string> FreeKeys = new() { "C", "F", "Bb", "G", "D" };
        private int _lastFreeKeyIndex = 0;
        //private int _lastFreeScaleIndex = 0;
        private bool _autoRepeat = false;
        private bool _repeatSameTune = false;
        private List<NoteInfo>? _savedNotesToRepeat = null;

        public string? Tune => _session?.Tune;
        public bool AutoRepeat
        {
            get => _autoRepeat;
            set
            {
                if (_autoRepeat != value)
                {
                    _autoRepeat = value;
                    UpdateAutoRepeatButtons();
                }
            }
        }

        public bool RepeatSameTune
        {
            get => _repeatSameTune;
            set
            {
                if (_repeatSameTune != value)
                {
                    _repeatSameTune = value;
                    UpdateAutoRepeatButtons();
                }
            }
        }

        private void UpdateAutoRepeatButtons()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (AutoRepeatNewButton != null)
                {
                    AutoRepeatNewButton.BackgroundColor = _autoRepeat && !_repeatSameTune
                        ? Color.FromArgb("#008000")  // green  = repeat new on
                        : Color.FromArgb("#8B4513"); // brown = repeat off
                }
                if (AutoRepeatSameButton != null)
                {
                    AutoRepeatSameButton.BackgroundColor = _autoRepeat && _repeatSameTune
                        ? Color.FromArgb("#008000")  // green  = repeat same on
                        : Color.FromArgb("#8B4513"); // brown = repeat off
                }
                if (AutoRepeatScaleButton != null)
                {
                    AutoRepeatScaleButton.BackgroundColor = _autoRepeat
                        ? Color.FromArgb("#008000")  // green  = repeat on
                        : Color.FromArgb("#8B4513"); // brown = repeat off
                }
            });
        }

        private bool _isAutoRepeatVisible;
        public bool IsAutoRepeatVisible
        {
            get => _isAutoRepeatVisible;
            set
            {
                if (_isAutoRepeatVisible != value)
                {
                    _isAutoRepeatVisible = value;
                    OnPropertyChanged(nameof(IsAutoRepeatVisible));
                    UpdateRepeatButtonsVisibility();
                }
            }
        }

        private bool _isRandomRepeatButtonsVisible;
        public bool IsRandomRepeatButtonsVisible
        {
            get => _isRandomRepeatButtonsVisible;
            set
            {
                if (_isRandomRepeatButtonsVisible != value)
                {
                    _isRandomRepeatButtonsVisible = value;
                    OnPropertyChanged(nameof(IsRandomRepeatButtonsVisible));
                }
            }
        }

        private bool _isScaleRepeatButtonVisible;
        public bool IsScaleRepeatButtonVisible
        {
            get => _isScaleRepeatButtonVisible;
            set
            {
                if (_isScaleRepeatButtonVisible != value)
                {
                    _isScaleRepeatButtonVisible = value;
                    OnPropertyChanged(nameof(IsScaleRepeatButtonVisible));
                }
            }
        }

        private void UpdateRepeatButtonsVisibility()
        {
            var isRandom = _session?.Tune == "Random";
            IsRandomRepeatButtonsVisible = _isAutoRepeatVisible && isRandom;
            IsScaleRepeatButtonVisible = _isAutoRepeatVisible && !isRandom;
        }
        private string _selectedInstrumentShort = "";
        public string SelectedInstrumentShort
        {
            get => _selectedInstrumentShort;
            set
            {
                if (_selectedInstrumentShort != value)
                {
                    _selectedInstrumentShort = value;
                    OnPropertyChanged(nameof(SelectedInstrumentShort));
                    UpdateInstrumentPickerVisibility();
                }
            }
        }

        private bool _isInstrumentPickerVisible = true;
        public bool IsInstrumentPickerVisible
        {
            get => _isInstrumentPickerVisible;
            set
            {
                if (_isInstrumentPickerVisible != value)
                {
                    _isInstrumentPickerVisible = value;
                    OnPropertyChanged(nameof(IsInstrumentPickerVisible));
                }
            }
        }

        private bool _isInstrumentLabelVisible = false;
        private static readonly HashSet<string> FreeScales = new() { "Major", "Harmonic Minor" };
        private int _lastValidScaleTuneIndex = 0;
        public bool IsInstrumentLabelVisible
        {
            get => _isInstrumentLabelVisible;
            set
            {
                if (_isInstrumentLabelVisible != value)
                {
                    _isInstrumentLabelVisible = value;
                    OnPropertyChanged(nameof(IsInstrumentLabelVisible));
                }
            }
        }
        private bool IsPremiumKey(string item) => !FreeKeys.Contains(item);
        private void InstrumentPicker_Unfocused(object? sender, EventArgs e)
        {
            IsInstrumentPickerVisible = false;
            IsInstrumentLabelVisible = true;
        }

        private void UpdateInstrumentPickerVisibility()
        {
            // After selection, always show only the label with the short string
            IsInstrumentPickerVisible = false;
            IsInstrumentLabelVisible = true;
        }

        

        private void AutoRepeatToggleButtonClicked(object sender, EventArgs e)
        {
            Debug.WriteLine($"[DEBUG] AutoRepeatToggleButtonClicked fired. Current AutoRepeat={AutoRepeat}, IsAutoRepeatVisible={IsAutoRepeatVisible}, Tune={_session.Tune}");
            AutoRepeat = !AutoRepeat;
        }

        
        public MainPage()
        {
            try
            {
                InitializeComponent();

                // Ensure ThemeService is available so we can deploy saved/default panel background
                _theme_service = ServiceHelper.GetService<ThemeService>()!;

                // Apply saved panel background (or default) before the Home page is shown
                DeploySavedPanelBackground();

                // disable iOS safe area for this page (use per-edge API available on this MAUI version)
                // Use reflection helper so the project compiles on non-iOS targets
                musicmate.Utilities.Utils.DisableIosSafeArea(this);

                BackgroundColor = Colors.White;
                _orientation = ServiceHelper.GetService<IOrientationService>()!;
                _session = ServiceHelper.GetService<NoteSessionService>()!;
                _sessionDb = ServiceHelper.GetService<SessionDatabase>()!;
                _audio = ServiceHelper.GetService<IAudioCaptureService>()!;
                _player = ServiceHelper.GetService<IAudioPlaybackService>()!;

                BindingContext = _session;
                StaffBorder.BindingContext = _theme_service;
                StaffGraphicsView.BindingContext = _theme_service;

                // Subscribe to MaxBlocksReached event to gracefully restart capture
                _audio.MaxBlocksReached += async () =>
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        if (_session.SessionCompleted)
                        {
                            Debug.WriteLine("[MainPage] MaxBlocksReached after session completed: full restart.");
                            await StartListeningAndEvaluatingAsync();
                        }
                        else
                        {
                            Debug.WriteLine("[MainPage] MaxBlocksReached mid-session: restarting capture only.");
                            await RestartAudioCaptureAsync();
                        }
                    });
                };

                StatusLabelShell.BindingContext = StatusService.Instance;
                _orientation.AllowAutorotate();

                // Staff graphics setup
                _drawable = new Drawables.StaffDrawable(_session, _theme_service);
                StaffGraphicsView.Drawable = _drawable;

                // Ensure the GraphicsView reserves computed height. Update when width or notes change.
                StaffGraphicsView.SizeChanged += (s, e) => UpdateStaffHeight();
                // When notes are regenerated, RegenerateNotesAsync will call UpdateStaffHeight indirectly.

                // Ensure visuals reflect ThemeService value applied at app startup.
                // Do not re-deploy saved color here (handled above in DeploySavedPanelBackground) — just apply current ThemeService value to visual properties.
                var panelColor = _theme_service?.PanelBackgroundColor ?? Colors.White;
                try
                {
                    StaffBorder.Background = new SolidColorBrush(panelColor);
                    StaffBorder.BackgroundColor = panelColor;
                }
                catch { }
                try
                {
                    StaffGraphicsView.Background = new SolidColorBrush(panelColor);
                    StaffGraphicsView.BackgroundColor = panelColor;
                }
                catch { }
                StaffGraphicsView.SetBinding(GraphicsView.BackgroundColorProperty, new Binding("PanelBackgroundColor"));

                // V2 staff drawable setup
                _v2Drawable = new Drawables.V2MeasureDrawable(_session, _theme_service);
                V2StaffGraphicsView.Drawable = _v2Drawable;
                V2StaffGraphicsView.SetBinding(GraphicsView.BackgroundColorProperty, new Binding("PanelBackgroundColor"));

                // Tuner graphics setup
                TunerBorder.BindingContext = _theme_service;
                TunerGraphicsView.BindingContext = _theme_service;
                TunerGraphicsView.Drawable = _drawable;
                TunerGraphicsView.SetBinding(GraphicsView.BackgroundColorProperty, new Binding("PanelBackgroundColor"));

                // ColorPickerDialog event: update theme color for all pages
                ColorPickerDialog.ColorPicked += async (s, color) =>
                {
                    _theme_service?.PanelBackgroundColor = color;
                    Preferences.Default.Set("StaffPanelColor", color.ToHex());
                    StaffBorder.Background = new SolidColorBrush(color);
                    if (!_isProgrammaticColorConfirm)
                    {
                        await StartListeningAndEvaluatingAsync();
                    }
                };

                // High-contrast drawing update
                _theme_service?.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(ThemeService.PanelBackgroundColor))
                    {
                        StaffGraphicsView.Invalidate();
                        // After regenerating notes, recompute and apply the required height so the view isn't clipped
                        UpdateStaffHeight();
                    }
                };

                // Session completed event
                _session.SessionCompletedAsync += async () =>
                {
                    try
                    {
                        // In V2 staff mode the note buffer is infinite; reaching the end of the
                        // current session window just means more notes need to be generated.
                        // Skip the normal session-end flow and let AppendV2MeasuresAsync handle it.
                        if (_session.V2StaffMode)
                        {
                            await AppendV2MeasuresAsync(V2BatchSize);
                            return;
                        }

                        await UpdateNoteStatsDatabaseAsync();
                        await SaveSessionStatAsync();

                        var (correct, wrong, apc) = _session.GetSessionCorrectWrongTotals();
                        var total = correct + wrong;
                        var percent = total > 0 ? (double)correct * 100 / total : 0.0;
                        var (meanBpm, stdBpm) = _session.GetFinalBpmStats();
#if DEBUG
                        {
                            double? cv = null;
                            if (meanBpm.HasValue && meanBpm.Value != 0 && stdBpm.HasValue)
                                cv = 100.0 * stdBpm.Value / meanBpm.Value;
                            StatusService.Instance.StatusMessage =
                                $"Correct = {apc:F1}%, Tempo = {meanBpm?.ToString("F1") ?? "N/A"} +/- {stdBpm?.ToString("F1") ?? "N/A"} (cv {((cv.HasValue) ? cv.Value.ToString("F1") : "N/A")}%)  (raw {percent:F1})";
                        }
#else
                        {
                            double? cv = null;
                            if (meanBpm.HasValue && meanBpm.Value != 0 && stdBpm.HasValue)
                                cv = 100.0 * stdBpm.Value / meanBpm.Value;
                            StatusService.Instance.StatusMessage =
                                $"Correct = {apc:F1}%, Tempo = {meanBpm?.ToString("F1") ?? "N/A"} +/- {stdBpm?.ToString("F1") ?? "N/A"} (cv {((cv.HasValue)?cv.Value.ToString("F1"):"N/A")}%)";
                        }
#endif
                        _session.SessionCompleted = true;

                        if (AutoRepeat && _session.Tune != "Tuner")
                        {
                            double repeatDelay = Preferences.Default.Get("RepeatDelaySeconds", 2.0);
                            await Task.Delay((int)(repeatDelay * 1000));
                            _session.SessionCompleted = false;

                            // StartListeningAndEvaluatingAsync will handle note restoration or generation
                            // based on the _repeatSameTune flag
                            await StartListeningAndEvaluatingAsync();
                        }
                        else
                        {
                            _audio.StopCapture();
                            SetButtonStates(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Session completion error: {ex}");
                    }
                };

                _session.PropertyChanged += async (_, e) =>
                {
                    if (e.PropertyName == nameof(NoteSessionService.AutoStart) && _session.AutoStart)
                    {
                        await StartAfterDelayAsync();
                    }
                };

                _session.PropertyChanged += Session_PropertyChanged;

                _session.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(NoteSessionService.Tune))
                    {
                        IsAutoRepeatVisible = _session.Tune != "Tuner";
                        UpdateRepeatButtonsVisibility();
                        UpdateKeyPickerVisibility();
                        OnPropertyChanged(nameof(Tune));
                    }
                };

                IsAutoRepeatVisible = _session.Tune != "Tuner";
                UpdateRepeatButtonsVisibility();

                // Show full long strings in picker
                var instrumentOptions = NoteSessionService.InstrumentOptions.Cast<string>().ToArray();
                InstrumentPicker.ItemsSource = instrumentOptions;
                // Select by matching short string
                var instrumentShort = _session.Instrument?.Split(',')[0].Trim() ?? instrumentOptions[0].Split(',')[0].Trim();
                var selectedIndex = Array.FindIndex(instrumentOptions, s => s.Split(',')[0].Trim() == instrumentShort);
                InstrumentPicker.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
                _session.Instrument = instrumentOptions[InstrumentPicker.SelectedIndex];
                SelectedInstrumentShort = instrumentOptions[InstrumentPicker.SelectedIndex].Split(',')[0].Trim();

                KeyPicker.ItemsSource = new[]
                {
                    "C", "F", "Bb", "G", "D", "A", "E", "B", "F#" , "C#",
                    "Eb", "Ab", "Db", "Gb", "Cb"
                };

                KeyPicker.SelectedIndex = Array.IndexOf((string[])KeyPicker.ItemsSource, _session.Key);
                if (KeyPicker.SelectedIndex < 0)
                    KeyPicker.SelectedIndex = 0;
                _lastFreeKeyIndex = KeyPicker.SelectedIndex;

                // Combined Scale + Tune picker: Tuner / Random / individual practice tunes / scales
                var practiceTuneTitles = musicmate.Models.TuneLibrary.All.Select(t => t.Title).ToArray();
                var scaleTuneOptions = new[] { "Tuner", "Random" }
                    .Concat(practiceTuneTitles)
                    .Concat(NoteSessionService.AvailableScales)
                    .ToArray();
                ScaleTunePicker.ItemsSource = scaleTuneOptions;

                var savedTune = Preferences.Default.Get<string?>("SelectedTune", null);
                if (!string.IsNullOrEmpty(savedTune) && (savedTune == "Random" || savedTune == "Tuner"))
                    _session.Tune = savedTune;
                else if (!string.IsNullOrEmpty(savedTune) && practiceTuneTitles.Contains(savedTune))
                {
                    var savedPT = musicmate.Models.TuneLibrary.All.FirstOrDefault(t => t.Title == savedTune);
                    if (savedPT != null)
                        _session.SelectPracticeTune(savedPT);
                }
                // else _session.Tune stays "Selected Scale" (persisted via SelectedTune preference)

                var initialScaleTuneSelection = _session.Tune == "Random" ? "Random"
                    : _session.Tune == "Tuner" ? "Tuner"
                    : _session.Tune == "Practice Tune" ? (_session.CurrentTune?.Title ?? practiceTuneTitles[0])
                    : _session.SelectedScale;
                var scaleTuneIdx = Array.IndexOf(scaleTuneOptions, initialScaleTuneSelection);
                ScaleTunePicker.SelectedIndex = scaleTuneIdx >= 0 ? scaleTuneIdx : 0;
                _lastValidScaleTuneIndex = ScaleTunePicker.SelectedIndex;
                IsAutoRepeatVisible = _session.Tune != "Tuner";

                ScaleTunePicker.SelectedIndexChanged += OnScaleTunePickerChanged;

                InstrumentPicker.SelectedIndexChanged += InstrumentPicker_SelectedIndexChanged;
                KeyPicker.SelectedIndexChanged += OnKeyPickerChangedWithPrompt;
            }
            catch (Exception ex)
            {
                Utils.Log($"[MainPage Constructor] ERROR: {ex}");
            }
        }

        private void OnStaffPanelColorPreviewed(object? sender, Color color)
        {
            StaffBorder.Background = new SolidColorBrush(color);
        }
        private Task StartAfterDelayAsync(bool playBack = false)
        {
            return MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Task.Delay(150);
                await StartListeningAndEvaluatingAsync(playBack);
            });
        }

        private async Task RegenerateNotesAsync()
        {
            // While showing post-autoplay results, do not overwrite the staff.
            if (_freezeStaff)
                return;

            var width = StaffGraphicsView.Width <= 0 ? 360 : StaffGraphicsView.Width;
            await _session.GenerateNotesAsync(width);

#if DEBUG
            if (_session.Tune == "Random")
            {
                var names = string.Join(", ", _session.NotesToDraw.Select(n => n.Name));
                Debug.WriteLine($"[Random] Generated {_session.NotesToDraw.Count} notes: {names}");
            }
#endif

            if (_session.V2StaffMode)
            {
                UpdateV2Display();
                StaffBorder.IsVisible = false;
                V2StaffBorder.IsVisible = true;
            }
            else
            {
                StaffBorder.IsVisible = true;
                V2StaffBorder.IsVisible = false;
                StaffGraphicsView.Invalidate();
                UpdateStaffHeight();
            }
        }

        // ── V2 multi-measure queue ────────────────────────────────────────────────

        /// <summary>How many measures to generate at once (initial fill and each top-up).</summary>
        private const int V2BatchSize = 4;
        /// <summary>Trigger a top-up when this many measures remain ahead of the current one.</summary>
        private const int V2RefillThreshold = 2;

        // Generator offsets: updated every time we append more measures.
        private int    _v2NextMeasureIndex    = 0;
        private double _v2NextBeatOffset      = 0.0;
        private int    _v2NextGlobalNoteIndex = 0;
        private bool   _v2AppendInProgress    = false;

        /// <summary>Cached excluded MIDI set rebuilt whenever a new sequence starts.</summary>
        private HashSet<int> _v2ExcludedMidis = new();

        /// <summary>
        /// Loads the set of mastered MIDI numbers from the note database using the
        /// same logic as <see cref="NoteSessionService.BuildRandomSequenceAsync"/>.
        /// </summary>
        private async Task LoadV2ExcludedMidisAsync()
        {
            _v2ExcludedMidis = await _session.GetMasteredMidiNumbersAsync();
        }

        /// <summary>
        /// Builds a <see cref="MusicSequenceGenerator"/> configured with the current
        /// session parameters, append offsets, and mastery exclusions.
        /// </summary>
        private MusicSequenceGenerator BuildV2Generator(int measureCount)
        {
            // Translate persisted string settings to model types.
            var timeSig = _session.V2TimeSignature switch
            {
                "3/4" => TimeSignature.ThreeFour,
                "2/4" => TimeSignature.TwoFour,
                _     => TimeSignature.FourFour
            };

            // "Simple" = quarter notes only; "Mixed" = variety up to the chosen smallest value.
            // RhythmVarietyPercent drives how often non-quarter durations appear.
            int rhythmVariety = _session.V2RhythmMode == "Mixed" ? 60 : 0;

            return new MusicSequenceGenerator
            {
                Key                  = _session.Key,
                Scale                = _session.SelectedScale,
                LowestNote           = _session.LowestNote,
                HighestNote          = _session.HighestNote,
                TimeSignature        = timeSig,
                MeasureCount         = measureCount,
                RhythmVarietyPercent = rhythmVariety,
                SmallestDuration     = _session.V2SmallestNote switch
                {
                    "Sixteenth" => NoteDuration.Sixteenth,
                    "Eighth"    => NoteDuration.Eighth,
                    _           => NoteDuration.Quarter
                },
                StartMeasureIndex    = _v2NextMeasureIndex,
                StartBeatOffset      = _v2NextBeatOffset,
                StartGlobalNoteIndex = _v2NextGlobalNoteIndex,
                ExcludedMidiNumbers  = _v2ExcludedMidis,
                // Walk the scale in order (up then down) when a specific scale is selected.
                // Random mode uses random pitch picking instead.
                UseScaleOrder        = _session.Tune != "Random"
            };
        }

        /// <summary>
        /// Derives bar-beat positions from a flat list of <see cref="GeneratedNote"/>
        /// based on the <see cref="GeneratedNote.MeasureIndex"/> transitions.
        /// Returns only the positions of bar lines that do not already exist in
        /// <paramref name="existingBarBeats"/> (so appended bars never duplicate).
        /// </summary>
        private static List<double> ComputeNewBarBeats(
            IReadOnlyList<GeneratedNote> notes,
            HashSet<double> existingBarBeats)
        {
            var result = new List<double>();
            int prevMeasure = notes.Count > 0 ? (notes[0].MeasureIndex ?? 0) : 0;
            foreach (var n in notes)
            {
                if ((n.MeasureIndex ?? prevMeasure) != prevMeasure && n.BeatPosition.HasValue)
                {
                    double bp = n.BeatPosition.Value;
                    if (!existingBarBeats.Contains(bp))
                    {
                        result.Add(bp);
                        existingBarBeats.Add(bp);
                    }
                    prevMeasure = n.MeasureIndex ?? prevMeasure;
                }
            }
            return result;
        }

        /// <summary>
        /// Populates the v2 drawable with a freshly generated measure sequence and
        /// invalidates the v2 GraphicsView.  Also populates _session.NotesToDraw so
        /// the existing Evaluate/UpdateFeedbackForCurrent pitch logic works unchanged.
        /// Generates <see cref="V2BatchSize"/> measures ahead from the start.
        /// </summary>
        private async Task UpdateV2DisplayAsync()
        {
            if (_v2Drawable == null) return;
            try
            {
                // Reset queue state.
                _v2NextMeasureIndex    = 0;
                _v2NextBeatOffset      = 0.0;
                _v2NextGlobalNoteIndex = 0;
                _v2AppendInProgress    = false;

                await LoadV2ExcludedMidisAsync();

                var gen      = BuildV2Generator(V2BatchSize);
                var measures = gen.GenerateSequence();
                var flat     = MusicSequenceGenerator.Flatten(measures);

                // Advance offsets past the generated measures.
                _v2NextMeasureIndex    += measures.Count;
                _v2NextBeatOffset      += measures.Count * (double)gen.TimeSignature.TotalBeats;
                _v2NextGlobalNoteIndex += flat.Count(n => !n.IsRest);

                // Bar beats: every position where MeasureIndex changes.
                var existingBarBeats = new HashSet<double>();
                var barBeats = ComputeNewBarBeats(flat, existingBarBeats);

                // ── Push to drawable ──────────────────────────────────────────────
                _v2Drawable.Notes           = flat;
                _v2Drawable.MeasureBarBeats = barBeats;
                _v2Drawable.CurrentNoteIndex = 0;
                _v2Drawable.NoteStates       = new V2NoteState[flat.Count];

                // Mark first playable note as current.
                for (int i = 0; i < flat.Count; i++)
                {
                    if (!flat[i].IsRest)
                    {
                        _v2Drawable.NoteStates[i] = V2NoteState.Current;
                        _v2Drawable.CurrentNoteIndex = i;
                        break;
                    }
                }

                // Populate session NotesToDraw.
                RebuildSessionNotesFromV2(flat, 0);

                // Resize the GraphicsView to fit the tallest note (ledger lines + labels).
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    var h = _v2Drawable.ComputeRequiredHeight();
                    V2StaffGraphicsView.HeightRequest = h;
                    V2StaffBorder.HeightRequest = h;
                });

                V2StaffGraphicsView.Invalidate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[V2] UpdateV2DisplayAsync ERROR: {ex}");
            }
        }

        /// <summary>
        /// Legacy sync wrapper kept so existing call sites (RegenerateNotesAsync) compile.
        /// Fires the async path and discards the task on the calling thread.
        /// </summary>
        private void UpdateV2Display() => _ = UpdateV2DisplayAsync();

        /// <summary>
        /// Appends <paramref name="measureCount"/> more measures to the v2 sequence
        /// without restarting the session.  Called when the player is getting close
        /// to the end of the visible note list.
        /// </summary>
        private async Task AppendV2MeasuresAsync(int measureCount)
        {
            if (_v2Drawable == null || _v2AppendInProgress) return;
            _v2AppendInProgress = true;
            try
            {
                var gen      = BuildV2Generator(measureCount);
                var measures = gen.GenerateSequence();
                var newNotes = MusicSequenceGenerator.Flatten(measures);

                if (newNotes.Count == 0) return;

                // Advance offsets.
                _v2NextMeasureIndex    += measures.Count;
                _v2NextBeatOffset      += measures.Count * (double)gen.TimeSignature.TotalBeats;
                _v2NextGlobalNoteIndex += newNotes.Count(n => !n.IsRest);

                // Compute bar beats for the new notes only.
                var existingSet = new HashSet<double>(_v2Drawable.MeasureBarBeats);
                var newBarBeats = ComputeNewBarBeats(newNotes, existingSet);

                // Append to drawable on the main thread.
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    var combinedNotes    = _v2Drawable.Notes.Concat(newNotes).ToList();
                    var combinedBarBeats = _v2Drawable.MeasureBarBeats.Concat(newBarBeats).ToList();

                    // Expand NoteStates, keeping existing state entries.
                    var oldStates    = _v2Drawable.NoteStates;
                    var newStates    = new V2NoteState[combinedNotes.Count];
                    Array.Copy(oldStates, newStates,
                        Math.Min(oldStates.Length, newStates.Length));
                    // New entries default to V2NoteState.Pending (= 0), which is correct.

                    _v2Drawable.Notes           = combinedNotes;
                    _v2Drawable.MeasureBarBeats = combinedBarBeats;
                    _v2Drawable.NoteStates      = newStates;

                    // Resize to accommodate any newly added high/low notes.
                    var h = _v2Drawable.ComputeRequiredHeight();
                    V2StaffGraphicsView.HeightRequest = h;
                    V2StaffBorder.HeightRequest = h;

                    // Extend NotesToDraw with the new pitch notes.
                    int sessionOffset = _session.NotesToDraw.Count;
                    foreach (var gn in newNotes)
                    {
                        if (gn.IsRest) continue;
                        _session.NotesToDraw.Add(new NoteInfo
                        {
                            Midi       = gn.MidiNumber,
                            Name       = gn.SpelledName,
                            TargetFreq = gn.TargetFrequency,
                            X          = 0f,
                            Duration   = gn.Duration
                        });
                        _session.FeedbackViewModels.Add(
                            new FeedbackItem(sessionOffset++, 0, 0, false));
                    }

                    V2StaffGraphicsView.Invalidate();
                    Debug.WriteLine($"[V2] Appended {measureCount} measures. Total notes: {combinedNotes.Count}");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[V2] AppendV2MeasuresAsync ERROR: {ex}");
            }
            finally
            {
                _v2AppendInProgress = false;
            }
        }

        /// <summary>
        /// Rebuilds <see cref="NoteSessionService.NotesToDraw"/> and
        /// <see cref="NoteSessionService.FeedbackViewModels"/> from a flat
        /// <see cref="GeneratedNote"/> list, starting at <paramref name="sessionStart"/>.
        /// </summary>
        private void RebuildSessionNotesFromV2(
            IReadOnlyList<GeneratedNote> flat, int sessionStart)
        {
            _session.NotesToDraw.Clear();
            _session.FeedbackViewModels.Clear();
            int idx = sessionStart;
            foreach (var gn in flat)
            {
                if (gn.IsRest) continue;
                _session.NotesToDraw.Add(new NoteInfo
                {
                    Midi       = gn.MidiNumber,
                    Name       = gn.SpelledName,
                    TargetFreq = gn.TargetFrequency,
                    X          = 0f,
                    Duration   = gn.Duration
                });
                _session.FeedbackViewModels.Add(new FeedbackItem(idx++, 0, 0, false));
            }
        }

        /// <summary>
        /// Syncs V2NoteState array from session feedback/progress after each pitch evaluation.
        /// Called on the main thread.  Also triggers a look-ahead append when the player
        /// is approaching the end of the current note buffer.
        /// </summary>
        private void SyncV2NoteStates()
        {
            if (_v2Drawable == null || _v2Drawable.Notes.Count == 0) return;

            var states = _v2Drawable.NoteStates;
            if (states.Length != _v2Drawable.Notes.Count)
                states = new V2NoteState[_v2Drawable.Notes.Count];

            // Walk v2 Notes; for each non-rest note find its session index
            int sessionIdx        = 0;
            int currentSessionIdx = _session.CurrentNoteIndex;

            for (int i = 0; i < _v2Drawable.Notes.Count; i++)
            {
                var gn = _v2Drawable.Notes[i];
                if (gn.IsRest)
                {
                    states[i] = V2NoteState.Pending;
                    continue;
                }

                if (sessionIdx < currentSessionIdx)
                {
                    bool correct = _session.CorrectNoteIndices.Contains(sessionIdx);
                    states[i] = correct ? V2NoteState.Correct : V2NoteState.Wrong;
                }
                else if (sessionIdx == currentSessionIdx)
                {
                    bool hasWrong = _session.NoteFeedbacks.TryGetValue(sessionIdx, out var fb) && fb.Wrong > 0;
                    states[i] = hasWrong ? V2NoteState.Wrong : V2NoteState.Current;
                }
                else
                {
                    states[i] = V2NoteState.Pending;
                }

                sessionIdx++;
            }

            _v2Drawable.NoteStates       = states;
            _v2Drawable.CurrentNoteIndex = _session.CurrentNoteIndex;
            V2StaffGraphicsView.Invalidate();

            // ── Look-ahead top-up ─────────────────────────────────────────────────
            // Determine which measure the current note is in, then check how many
            // measures still lie ahead.  Trigger an append when only a few remain.
            if (!_v2AppendInProgress && _v2Drawable.Notes.Count > 0)
            {
                int curDrawIdx = _v2Drawable.CurrentNoteIndex;
                if (curDrawIdx >= 0 && curDrawIdx < _v2Drawable.Notes.Count)
                {
                    int currentMeasure = _v2Drawable.Notes[curDrawIdx].MeasureIndex ?? 0;
                    int lastMeasure    = _v2Drawable.Notes[^1].MeasureIndex ?? 0;
                    int measuresAhead  = lastMeasure - currentMeasure;

                    if (measuresAhead <= V2RefillThreshold)
                        _ = AppendV2MeasuresAsync(V2BatchSize);
                }
            }
        }

        private void UpdateStaffHeight()
        {
            try
            {
                if (StaffGraphicsView == null || _drawable == null)
                    return;

                if (StaffGraphicsView.Width <= 0)
                    return;

                // Use exact computed height - no extra margins
                var heightReq = _drawable.ComputeRequiredHeight((float)StaffGraphicsView.Width);
                // DEBUG: Log height calculations and current sizes
                //Debug.WriteLine($"[UpdateStaffHeight] Computed height: {heightReq:F1}");
                //Debug.WriteLine($"[UpdateStaffHeight] StaffGraphicsView current: Width={StaffGraphicsView.Width:F1}, Height={StaffGraphicsView.Height:F1}");
                //Debug.WriteLine($"[UpdateStaffHeight] StaffBorder current: Width={StaffBorder.Width:F1}, Height={StaffBorder.Height:F1}");
                //Debug.WriteLine($"[UpdateStaffHeight] StaffBorder Padding: {StaffBorder.Padding}");
                //Debug.WriteLine($"[UpdateStaffHeight] StaffBorder Margin: {StaffBorder.Margin}");

                StaffGraphicsView.HeightRequest = heightReq;
                StaffBorder.HeightRequest = heightReq;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateStaffHeight] ERROR: {ex}");
            }
        }

        private void SetButtonStates(bool isRunning)
        {
            _isRunning = isRunning;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (isRunning)
                {
                    StartStopButton.Text = "■";
                    StartStopButton.TextColor = Color.FromArgb("#E04040");
                    PlayEvaluateButton.IsEnabled = false;
                }
                else
                {
                    StartStopButton.Text = "●";
                    StartStopButton.TextColor = Color.FromArgb("#008000");
                    PlayEvaluateButton.IsEnabled = true;
                }
            });
        }

        private async void OnStartStopToggleClicked(object? sender, EventArgs e)
        {
            if (_isRunning)
            {
                try
                {
                    _playCts?.Cancel();
                    _audio.StopCapture();
                    StatusService.Instance.StatusMessage = "Stopped. Tap circle to listen, arrowhead to play.";
                    _session.SessionCompleted = true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Stop error: {ex}");
                }
                finally
                {
                    SetButtonStates(false);
                }
            }
            else
            {
                _session.SessionCompleted = false;
                StatusService.Instance.StatusMessage = "Listening, go ahead and play!";
                await StartListeningAndEvaluatingAsync();
            }
        }

        protected async override void OnAppearing()
        {
            base.OnAppearing();
            _orientation.ForceLandscape();

            Debug.WriteLine($"[DEBUG] OnAppearing: IsAutoRepeatVisible={IsAutoRepeatVisible}, Tune={_session.Tune}");
            IsAutoRepeatVisible = _session.Tune != "Tuner";
            DeviceDisplay.Current.KeepScreenOn = true;

#if DEBUG
            if (_session.AutoStart && _session.Tune != "Tuner")
            {
                AutoRepeat = true;
                StatusService.Instance.IsPremiumUser = true;
            }
#endif

            if (_session.AutoStart)
            {
                await StartAfterDelayAsync();
                return;
            }

            // Always regenerate notes on every appearance so that changes made on
            // Settings/Advanced pages (key, scale, range, etc.) are reflected immediately.
            // Wait for the layout to provide a valid staff width first.
            if (!_isRunning)
            {
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (StaffGraphicsView != null && StaffGraphicsView.Width <= 0 && sw.ElapsedMilliseconds < 1000)
                    {
                        await Task.Delay(40);
                    }
                    await RegenerateNotesAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[OnAppearing] ERROR regenerating notes: {ex}");
                }
            }

#if DEBUG
            try
            {
                await Task.Delay(200);
                if (_session.Tune == "Tuner")
                {
                    await Task.Delay(100);
                    await MainScrollView.ScrollToAsync(PickersContainer, ScrollToPosition.Start, false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LAYOUT DEBUG] ERROR: {ex}");
            }
#endif
        }
        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            // Stop listening and evaluating
            _playCts?.Cancel();
            _audio?.StopCapture();
            SetButtonStates(false);
            DeviceDisplay.Current.KeepScreenOn = false;
            StatusService.Instance.StatusMessage = "Stopped listening.";
        }         

        private async void OnPlayEvaluateClicked(object? sender, EventArgs e)
        {
            if (_isPlaying)
                return;

            // Save user's instrument selection to restore after playback
            _savedInstrumentForPlayback = _session.Instrument;
            _savedInstrumentIndexForPlayback = InstrumentPicker?.SelectedIndex ?? -1;

            try
            {
                var instrumentOptions = NoteSessionService.InstrumentOptions.Cast<string>().ToArray();
                var instIdx = Array.FindIndex(instrumentOptions, s => s.Split(',')[0].Trim() == "C");
                if (instIdx >= 0)
                {
                    InstrumentPicker?.SelectedIndex = instIdx;
                    _session.Instrument = instrumentOptions[instIdx];
                    SelectedInstrumentShort = instrumentOptions[instIdx].Split(',')[0].Trim();
                    UpdateInstrumentPickerVisibility();
                }
            }
            catch
            {
                // best-effort; ignore failures
            }

            _isPlaying = true;
            PlayEvaluateButton.Text = "■";
            PlayEvaluateButton.TextColor = Color.FromArgb("#E04040");
            await StartListeningAndEvaluatingAsync(playBack: true);
        }

        private void OnAudioBlock(short[] pcm16)
        {
            var buf = new float[pcm16.Length];
            for (var i = 0; i < pcm16.Length; i++)
            {
                buf[i] = pcm16[i] / 32768f;
            }

            var rms = PitchDetectionService.ComputeRms(buf);

            if (rms < _session.RmsThreshold)
            {
                if (!_isBelowThreshold)
                {
                    Debug.WriteLine("[Audio] Below RMS threshold, ignoring");
                    _isBelowThreshold = true;
                }
                _pitchBufferPos = 0;
                return;
            }

            _isBelowThreshold = false;

            // Don't accumulate audio during ignore period — ensures the first
            // detection after cooldown uses entirely fresh samples
            if (_session.ShouldIgnoreAudio(DateTime.UtcNow))
            {
                _pitchBufferPos = 0;
                return;
            }

            // Accumulate samples into a larger window for reliable low-frequency detection
            var windowSize = _session.PitchWindowSize;
            if (_pitchBuffer.Length != windowSize)
            {
                _pitchBuffer = new float[windowSize];
                _pitchBufferPos = 0;
            }

            var toCopy = Math.Min(buf.Length, windowSize - _pitchBufferPos);
            Array.Copy(buf, 0, _pitchBuffer, _pitchBufferPos, toCopy);
            _pitchBufferPos += toCopy;

            if (_pitchBufferPos < windowSize)
                return;

            // Hop-based overlap: shift the buffer by half so subsequent
            // detections reuse the stable tail of the previous window,
            // reducing transient/attack bias that causes flat readings.
            int hopSize = windowSize / 2;
            Array.Copy(_pitchBuffer, hopSize, _pitchBuffer, 0, windowSize - hopSize);
            _pitchBufferPos = windowSize - hopSize;

            var now = DateTime.UtcNow;
            if ((now - _lastProcess).TotalMilliseconds < _session.CooldownMs)
                return;

            lock (_processLock)
            {
                // Check ignore period BEFORE detection/smoothing to prevent
                // transitional audio from polluting the smoothing history
                if (_session.ShouldIgnoreAudio(DateTime.UtcNow))
                    return;

                _lastProcess = now;

                double freq = PitchDetectionService.DetectPitchMcLeod(_pitchBuffer, _pitchBuffer.Length, _session.SampleRate);
                freq = freq * Math.Pow(2, _session.PitchOffsetCents / 1200.0);

                if (freq == 0)
                    return;

                freq = _session.SmoothPitch(freq);

                if (_session.Tune == "Tuner")
                {
                    _session.UpdateTunerLastNote(freq);
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        TunerGraphicsView.Invalidate();
                    });
                    return;
                }

                // Skip processing after session completion to preserve the summary message
                if (_session.SessionCompleted)
                    return;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    // Don't interfere with PlayDisplayedAsync's direct feedback updates
                    if (_isPlaying)
                        return;

                    // Only accept the note as correct if it matches the expected note (including octave) at the current index
                    if (_session.V2StaffMode)
                    {
                        // V2 mode: use same evaluate/updatefeedback pipeline, then sync visual states
                        var result = _session.Evaluate(freq);
                        if (_session.UpdateFeedbackForCurrent(freq, result))
                        {
                            if (result.correct && !string.IsNullOrEmpty(_session.NotesToDraw.ElementAtOrDefault(_session.CurrentNoteIndex > 0 ? _session.CurrentNoteIndex - 1 : 0)?.Name))
                            {
                                var prevName = _session.NotesToDraw.ElementAtOrDefault(_session.CurrentNoteIndex - 1)?.Name ?? "";
                                _session.RecordRandomSessionNoteResult(prevName, true);
                            }
                            SyncV2NoteStates();
                        }
                        else
                        {
                            // wrong attempt — still sync to show red
                            SyncV2NoteStates();
                        }
                    }
                    else if (_session.Tune == "Random" && _session.CurrentNoteIndex < _session.NotesToDraw.Count)
                    {
                        var expectedName = _session.NotesToDraw[_session.CurrentNoteIndex].Name;
                        // Accept enharmonic equivalents for accidentals
                        var detectedInfo = _session.MapPitch(freq);
                        var detectedNote = detectedInfo.WrittenName;
                        var expectedInfo = _session.NotesToDraw[_session.CurrentNoteIndex];

                        var detectedNoteInfo = new NoteInfo
                        {
                            Midi = musicmate.Services.NoteSessionService.NoteNameToMidi(detectedNote),
                            Name = detectedNote
                        };

                        bool isMatch = detectedNote == expectedName
                            || detectedNoteInfo.EnharmonicNames.Contains(expectedName)
                            || expectedInfo.EnharmonicNames.Contains(detectedNote);

                        

                        // Only clear noise-accumulated wrongs when the FIRST correct note
                        // is detected — keeps the "clean start" behaviour without blocking
                        // wrong-note tracking for completely different pitch classes.
                        if (isMatch && _session.CorrectNoteIndices.Count == 0)
                        {
                            var keys = _session.NoteFeedbacks.Keys.ToList();
                            foreach (var k in keys)
                            {
                                var v = _session.NoteFeedbacks[k];
                                _session.NoteFeedbacks[k] = (0, v.Cents);
                            }
                        }

                        // Always evaluate — UpdateFeedbackForCurrent handles both
                        // wrong-pitch-class and correct/in-tolerance notes internally,
                        // ensuring NoteFeedbacks is updated for all outcomes.
                        // Previously the isMatch gate caused wrong-pitch-class notes to
                        // bypass NoteFeedbacks entirely, making GetSessionCorrectWrongTotals
                        // report 0 wrongs → 100% correct.
                        var result = _session.Evaluate(freq);
                        var advanced = _session.UpdateFeedbackForCurrent(freq, result);
                        if (advanced)
                        {
                            _session.RecordRandomSessionNoteResult(expectedName, result.correct);
                            Debug.WriteLine($"[Random] Recorded {(result.correct ? "correct" : "wrong")} for {expectedName} (heard {detectedNote})");
                            StaffGraphicsView.Invalidate();
                        }
                    }
                    else if (_session.Tune != "Random")
                    {
                        // For other modes, evaluate and update feedback
                        var result = _session.Evaluate(freq);
                        if (_session.UpdateFeedbackForCurrent(freq, result))
                            StaffGraphicsView.Invalidate();
                    }
                });
            }
        }       

        /// <summary>
        /// Restarts only the audio capture stream without resetting session state,
        /// notes, or progress. Used when MaxBlocks is reached mid-session.
        /// </summary>
        private async Task RestartAudioCaptureAsync()
        {
            try
            {
                Debug.WriteLine("[Restart] Restarting audio capture (session preserved)...");
                _audio.StopCapture();
                _pitchBufferPos = 0;
                await _audio.EnsurePermissionAsync();
                _audio.StartCapture(OnAudioBlock);
                Debug.WriteLine("[Restart] Audio capture restarted");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Restart] ERROR: {ex}");
                SetButtonStates(false);
                StatusService.Instance.StatusMessage = "Listening stopped — tap ● to restart.";
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    var snack = Snackbar.Make(
                        "Audio stream dropped. Tap ● to restart listening.",
                        duration: TimeSpan.FromSeconds(5));
                    await snack.Show();
                });
            }
        }

        

async Task UpdateNoteStatsDatabaseAsync()
        {
            // Respect the user's collection preference
            if (!Preferences.Default.Get("CollectNoteStats", true)) return;

            try
            {
                var db = ServiceHelper.GetService<NoteDatabase>();
                var sessionStats = _session.GetAndClearRandomSessionNoteStats();
                var sessionStreaks = _session.GetSessionStreaks();

                foreach (var (writtenName, (correct, wrong, totalMs, msCount)) in sessionStats)
                {
                    var noteLetter = writtenName[0].ToString();
                    var accidental = writtenName.Length > 2 && (writtenName[1] == '#' || writtenName[1] == 'b') ? writtenName[1].ToString() : "";
                    var octaveStr = new string(writtenName.SkipWhile(c => !char.IsDigit(c)).ToArray());
                    var octave = int.TryParse(octaveStr, out var o) ? o : 0;

                    var stat = await db!.GetByWrittenNameAsync(writtenName);
                    if (stat == null)
                    {
                        stat = new NoteStat
                        {
                            NoteLetter = noteLetter,
                            Accidental = accidental,
                            Octave = octave,
                            WrittenName = writtenName,
                            Correct = correct,
                            Wrong = wrong,
                            MsCount = msCount,
                            MsAverage = msCount > 0 ? totalMs / msCount : 0.0,
                            Streak = sessionStreaks.GetValueOrDefault(writtenName, 0)
                        };
                        await db.InsertOrReplaceAsync(stat);
                    }
                    else
                    {
                        stat.Correct += correct;
                        stat.Wrong += wrong;
                        if (msCount > 0)
                        {
                            var newMsCount = stat.MsCount + msCount;
                            stat.MsAverage = (stat.MsAverage * stat.MsCount + totalMs) / newMsCount;
                            stat.MsCount = newMsCount;
                        }
                        // Update streak: accumulate if no wrongs this session, otherwise reset to session-end streak
                        if (sessionStreaks.TryGetValue(writtenName, out var sessionStreak))
                        {
                            if (sessionStreak > 0 && wrong == 0)
                                stat.Streak += sessionStreak;
                            else
                                stat.Streak = sessionStreak;
                        }
                        await db.UpdateAsync(stat);
                    }
                }

                // Prune if over the size limit set in Settings
                long maxBytes = (long)Preferences.Default.Get("MaxNoteDbSizeMb", 50) * 1024 * 1024;
                await db!.PruneToSizeLimitAsync(maxBytes);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Stats] UpdateNoteStatsDatabaseAsync error: {ex.Message}");
            }
        }
        // Replace duplicate method implementations with these single canonical versions.

        private async Task StartListeningAndEvaluatingAsync(bool playBack = false)
        {
            try
            {
                // Any new session clears the post-autoplay results freeze.
                _freezeStaff = false;

                StatusService.Instance.StatusMessage = "Listening";
                Debug.WriteLine($"[Start] Starting listening, playBack={playBack}");
                SetButtonStates(true);

                _lastProcess = DateTime.MinValue;
                _isBelowThreshold = true;
                _pitchBufferPos = 0;
                _session.Reset();

                // Handle note generation based on repeat mode
                if (_repeatSameTune && _savedNotesToRepeat != null && _savedNotesToRepeat.Count > 0)
                {
                    // Filter out notes that are now mastered before restoring
                    var notesToRestore = _savedNotesToRepeat.ToList();
                    var db = ServiceHelper.GetService<NoteDatabase>();
                    if (db != null)
                    {
                        await db.InitializeAsync();
                        var statsList = await db.GetAllAsync();
                        var stats = statsList.ToDictionary(s => s.WrittenName, s => s);
                        notesToRestore = notesToRestore.Where(n =>
                        {
                            if (!stats.TryGetValue(n.Name, out var stat)) return true;
                            if (_session.MasteredMethod == "Streak")
                                return stat.Streak < _session.StreakCrit;
                            return !(stat.PercentCorrect >= _session.CorrectThreshold && stat.Correct >= _session.MinCorrectCount);
                        }).ToList();
                    }

                    // If too few notes remain after mastery filtering, regenerate instead
                    if (notesToRestore.Count < 2)
                    {
                        await RegenerateNotesAsync();
                        if (_session?.NotesToDraw != null && _session.NotesToDraw.Count > 0)
                            _savedNotesToRepeat = new List<NoteInfo>(_session.NotesToDraw);
                    }
                    else
                    {
                        // Restore the filtered notes for "Repeat Same" mode
                        _session.NotesToDraw.Clear();
                        _session.NotesToDraw.AddRange(notesToRestore);
                        // Re-populate FeedbackViewModels (cleared by Reset) to match the restored notes
                        _session.FeedbackViewModels.Clear();
                        for (int i = 0; i < notesToRestore.Count; i++)
                            _session.FeedbackViewModels.Add(new FeedbackItem(i, 0, 0, false));
                        await MainThread.InvokeOnMainThreadAsync(() => StaffGraphicsView.Invalidate());
                        Debug.WriteLine($"[Start] Restored {notesToRestore.Count} notes for Repeat Same (filtered from {_savedNotesToRepeat.Count})");
                    }
                }
                else
                {
                    // Generate new notes (for first run, "Repeat New", or scale modes)
                    await RegenerateNotesAsync();

                    // Save notes for potential "Repeat Same" after generation
                    if (_session?.NotesToDraw != null && _session.NotesToDraw.Count > 0)
                    {
                        _savedNotesToRepeat = new List<NoteInfo>(_session.NotesToDraw);
                        Debug.WriteLine($"[Start] Generated and saved {_session.NotesToDraw.Count} notes");
                    }
                }

                if (!playBack)
                {
                    Debug.WriteLine("[Start] Requesting audio permission...");
                    await _audio.EnsurePermissionAsync();
                    try { _audio.StopCapture(); } catch { }
                    Debug.WriteLine("[Start] Starting audio capture...");
                    _audio.StartCapture(OnAudioBlock);
                    Debug.WriteLine("[Start] Audio capture started");
                }
                else
                {
                    try { _audio.StopCapture(); } catch { }
                }

                if (playBack)
                {
                    _playCts?.Cancel();
                    _playCts = new CancellationTokenSource();
                    _ = PlayDisplayedAsync(_playCts.Token);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Start] ERROR: {ex}");
                _isPlaying = false;
                SetButtonStates(false);
                StatusService.Instance.StatusMessage = "Could not start microphone.";
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    var snack = Snackbar.Make(
                        "Microphone unavailable — check app permissions.",
                        duration: TimeSpan.FromSeconds(4));
                    await snack.Show();
                });
            }
        }
        private async Task PlayDisplayedAsync(CancellationToken ct)
        {
            var cancelled = false;
            try
            {
                if (_session.NotesToDraw.Count == 0)
                    return;

                var bpm = Math.Clamp(_session.PlaybackBpm, 30, 200);
                var beatSeconds = 60.0 / bpm;

                for (int i = 0; i < _session.NotesToDraw.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var note = _session.NotesToDraw[i];

                    // Scale duration by note's rhythmic value when available (practice tune mode).
                    // A quarter note = 1 beat; half = 2 beats; eighth = 0.5 beats, etc.
                    var durationBeats = note.Duration.HasValue
                        ? note.Duration.Value.ToBeatValue()
                        : 1.0;
                    var totalSeconds = beatSeconds * durationBeats;
                    var gapSeconds = Math.Min(0.02, totalSeconds * 0.05);
                    var noteSeconds = Math.Max(0.05, totalSeconds - gapSeconds);

                    StatusService.Instance.StatusMessage = $"Playing note {i + 1}/{_session.NotesToDraw.Count}: {note.Name}";

                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        _session.PlaybackHighlightIndex = i;
                        StaffGraphicsView.Invalidate();
                    });

                    bool notePlayed = false;
                    try
                    {
                        await _player.PlayAsync(new[] { note.TargetFreq }, noteSeconds, gapSeconds, 0.22f, ct);
                        notePlayed = true;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    finally
                    {
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            if (notePlayed && i < _session.FeedbackViewModels.Count)
                            {
                                var cur = _session.FeedbackViewModels[i];
                                _session.FeedbackViewModels[i] = new FeedbackItem(i, cur.WrongAttempts, 0, true);
                            }
                            _session.PlaybackHighlightIndex = null;
                            StaffGraphicsView.Invalidate();
                        });
                    }
                }
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PlayDisplayedAsync] ERROR: {ex}");
            }
            finally
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    _isPlaying = false;
                    PlayEvaluateButton.Text = "▶";
                    PlayEvaluateButton.TextColor = Color.FromArgb("#008000");

                    if (!cancelled && _session.NotesToDraw.Count > 0)
                    {
                        // Freeze the staff NOW so the green feedbacks survive
                        // the instrument restoration that follows.
                        _freezeStaff = true;
                        _session.SessionCompleted = true;
                        await _session.TriggerSessionCompletionAsync();

                        // TriggerSessionCompletionAsync sets "Correct = NaN%" for autoplay because
                        // CorrectNoteIndices and NoteFeedbacks are empty (no mic input).
                        // Override with correct autoplay stats: all notes played = 100%, tempo =
                        // PlaybackBpm with zero variance (computer-controlled constant tempo).
                        var playbackBpm = (double)_session.PlaybackBpm;
#if DEBUG
                        StatusService.Instance.StatusMessage =
                            $"Correct = 100.0%, Tempo = {playbackBpm:F1} +/- 0.0 (cv 0.0%)  (raw 100.0)";
#else
                        StatusService.Instance.StatusMessage =
                            $"Correct = 100.0%, Tempo = {playbackBpm:F1} +/- 0.0 (cv 0.0%)";
#endif
                    }

                    // Restore instrument after freeze — its PropertyChanged will
                    // trigger RegenerateNotesAsync which is now suppressed.
                    if (_savedInstrumentIndexForPlayback >= 0)
                    {
                        InstrumentPicker.SelectedIndex = _savedInstrumentIndexForPlayback;
                    }
                    _savedInstrumentForPlayback = null;
                    _savedInstrumentIndexForPlayback = -1;
                });

                if (!cancelled)
                {
                    try
                    {
                        await _audio.EnsurePermissionAsync();
                        _audio.StartCapture(OnAudioBlock);
                        SetButtonStates(false);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[PlayDisplayedAsync] restart capture ERROR: {ex}");
                        SetButtonStates(false);
                    }
                }
                else
                {
                    SetButtonStates(false);
                }
            }
        }


        private async Task SaveSessionStatAsync()
        {
            if (_sessionDb == null) return;

            // Do not record Tuner sessions
            if (_session.Tune == "Tuner") return;

            // Respect the user's collection preference
            if (!Preferences.Default.Get("CollectSessionStats", true)) return;

            await _sessionDb.InitializeAsync();

            var (correct, wrong, apc) = _session.GetSessionCorrectWrongTotals();
            var total = correct + wrong;
            var pc = total > 0 ? (double)correct * 100.0 / total : 0.0;
            var (meanBpm, stdBpm) = _session.GetFinalBpmStats();
            var hi = _session.NotesToDraw.OrderByDescending(n => n.Midi).FirstOrDefault();
            var lo = _session.NotesToDraw.OrderBy(n => n.Midi).FirstOrDefault();

            var stat = new SessionStat
            {
                Dt = DateTime.Now,
                Key = _session.Key,
                Instrument = _session.Instrument?.Split(',')[0].Trim() ?? string.Empty,
                Sc = _session.Tune == "Random" ? "Random"
                    : _session.Tune == "Practice Tune" ? (_session.CurrentTune?.Title ?? "Practice Tune")
                    : _session.SelectedScale,
                Hi = hi?.Name ?? "",
                Lo = lo?.Name ?? "",
                Pc = apc,
                PcRaw = pc,
                Tp = meanBpm ?? 0,
                Ts = stdBpm ?? 0
            };

            await _sessionDb.InsertAsync(stat);

            // Prune if over the size limit set in Settings
            long maxBytes = (long)Preferences.Default.Get("MaxSessionDbSizeMb", 50) * 1024 * 1024;
            await _sessionDb.PruneToSizeLimitAsync(maxBytes);
        }
        private async void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_session.SelectedScale) ||
                e.PropertyName == nameof(NoteSessionService.Instrument) ||
                e.PropertyName == nameof(NoteSessionService.Key) ||
                e.PropertyName == nameof(NoteSessionService.Tune))
            {
                // Saved notes are for a specific scale/key/tune — invalidate them when any of those change
                // so the next repeat generates fresh notes for the new selection rather than restoring stale ones.
                if (e.PropertyName == nameof(_session.SelectedScale) ||
                    e.PropertyName == nameof(NoteSessionService.Key) ||
                    e.PropertyName == nameof(NoteSessionService.Tune))
                {
                    _savedNotesToRepeat = null;
                }

                await RegenerateNotesAsync();
                UpdateTunerVisibility();
                UpdateKeyPickerVisibility();
            }

            if (e.PropertyName == nameof(_session.SelectedScale) ||
                e.PropertyName == nameof(NoteSessionService.Key) ||
                e.PropertyName == nameof(NoteSessionService.Instrument))
            {
                UpdateConcertKeyLabel();
                UpdateScaleTunePicker();
            }

            if (e.PropertyName == nameof(NoteSessionService.Instrument))
                UpdateInstrumentPickerSelection();

            if (e.PropertyName == nameof(NoteSessionService.Key))
                UpdateKeyPickerSelection();
        }

        private void UpdateInstrumentPickerSelection()
        {
            if (InstrumentPicker.ItemsSource is not string[] items) return;
            var idx = Array.IndexOf(items, _session.Instrument);
            if (idx >= 0 && InstrumentPicker.SelectedIndex != idx)
                InstrumentPicker.SelectedIndex = idx;
            SelectedInstrumentShort = _session.Instrument?.Split(',')[0].Trim() ?? string.Empty;
        }

        private void UpdateKeyPickerSelection()
        {
            if (KeyPicker.ItemsSource is not string[] items) return;
            var idx = Array.IndexOf(items, _session.Key);
            if (idx >= 0 && KeyPicker.SelectedIndex != idx)
                KeyPicker.SelectedIndex = idx;
        }

        private void UpdateConcertKeyLabel()
        {
            ConcertKeyLabel.Text = $"(Concert {_session.GetConcertKey()})";
        }

        private void UpdateKeyPickerVisibility()
        {
            var hide = _session.Tune == "Tuner";
            KeyPicker.IsVisible = !hide;
            KeyLabel.IsVisible = !hide;
            KeyBorder.IsVisible = !hide;
            ConcertKeyLabel.IsVisible = !hide;
        }
        private void UpdateTunerVisibility()
        {
            var isTuner = _session.Tune == "Tuner";
            if (isTuner)
            {
                StaffBorder.IsVisible  = false;
                V2StaffBorder.IsVisible = false;
            }
            else if (_session.V2StaffMode)
            {
                StaffBorder.IsVisible  = false;
                V2StaffBorder.IsVisible = true;
            }
            else
            {
                StaffBorder.IsVisible  = true;
                V2StaffBorder.IsVisible = false;
            }
            TunerGrid.IsVisible = isTuner;
            if (isTuner)
            {
                _session.SessionCompleted = false;
                TunerGraphicsView.Invalidate();
                if (!_isRunning)
                {
                    _ = StartListeningAndEvaluatingAsync();
                }
            }
        }
        private void UpdateScaleTunePicker()
        {
            if (ScaleTunePicker.ItemsSource is not string[] items) return;
            var selection = _session.Tune == "Random" ? "Random"
                : _session.Tune == "Tuner" ? "Tuner"
                : _session.Tune == "Practice Tune" ? (_session.CurrentTune?.Title ?? string.Empty)
                : _session.SelectedScale;
            var idx = Array.IndexOf(items, selection);
            if (idx >= 0 && ScaleTunePicker.SelectedIndex != idx)
                ScaleTunePicker.SelectedIndex = idx;
        }

        private async void OnScaleTunePickerChanged(object? sender, EventArgs e)
        {
            if (ScaleTunePicker.SelectedItem is not string selected) return;

            // Check if the selection is a practice tune title
            var practiceTune = musicmate.Models.TuneLibrary.All.FirstOrDefault(t => t.Title == selected);
            if (practiceTune != null)
            {
                _lastValidScaleTuneIndex = ScaleTunePicker.SelectedIndex;
                _session.SelectPracticeTune(practiceTune);
                Preferences.Default.Set("SelectedTune", selected);
                IsAutoRepeatVisible = true;
                UpdateKeyPickerVisibility();
                await RegenerateNotesAsync();
                return;
            }

            if (selected == "Random" || selected == "Tuner")
            {
                _lastValidScaleTuneIndex = ScaleTunePicker.SelectedIndex;
                _session.Tune = selected;
                Preferences.Default.Set("SelectedTune", selected);
                IsAutoRepeatVisible = selected == "Random";
                UpdateKeyPickerVisibility();
                return;
            }

            // Scale selected — premium check for non-free scales
            if (!FreeScales.Contains(selected) && !StatusService.Instance.IsPremiumUser)
            {
                var purchased = await PremiumPromptHelper.ShowAsync(this,
                    onDecline: () => ScaleTunePicker.SelectedIndex = _lastValidScaleTuneIndex);
                if (!purchased)
                    return;
            }

            _lastValidScaleTuneIndex = ScaleTunePicker.SelectedIndex;
            _session.Tune = "Selected Scale";
            _session.SelectedScale = selected;
            Preferences.Default.Set("SelectedTune", "Selected Scale");
            IsAutoRepeatVisible = true;
            UpdateKeyPickerVisibility();
        }
        

        private async void OnSettingsChanged(object? sender, EventArgs e)
        {
            _session.Instrument = InstrumentPicker.SelectedItem?.ToString() ?? _session.Instrument;
            _session.Key = KeyPicker.SelectedItem?.ToString() ?? _session.Key;

            UpdateConcertKeyLabel();
            UpdateTunerVisibility();
            await RegenerateNotesAsync();
        }
        private void InstrumentPicker_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (InstrumentPicker.SelectedItem is string s)
            {
                // Always show only the short string in overlay label
                var shortInstrument = s.Split(',')[0].Trim();
                SelectedInstrumentShort = shortInstrument;
                // Store the full string so GetInstrumentTransposeOffset can match it
                _session.Instrument = s;
                // Hide picker and show label immediately
                IsInstrumentPickerVisible = false;
                IsInstrumentLabelVisible = true;
                // Workaround: immediately unfocus picker to prevent unwanted stage
                InstrumentPicker.Unfocus();
            }
        }

        async void OnKeyPickerChangedWithPrompt(object? sender, EventArgs e)
        {
            var selectedKey = KeyPicker.SelectedItem?.ToString();
            if (selectedKey == null)
                return;

            var shortKey = selectedKey.Split(',')[0].Trim();

            if (IsPremiumKey(shortKey) && !StatusService.Instance.IsPremiumUser)
            {
                // onDecline: revert picker to last free key before closing the popup
                var purchased = await PremiumPromptHelper.ShowAsync(this,
                    onDecline: () => KeyPicker.SelectedIndex = _lastFreeKeyIndex);

                if (!purchased)
                    return;

                // Premium just purchased — allow the key through
            }
            else
            {
                _lastFreeKeyIndex = KeyPicker.SelectedIndex;
            }

            _session.Key = shortKey;
            OnSettingsChanged(sender, e);
        }

        // Use base BindableObject.OnPropertyChanged so XAML bindings receive change notifications

        private async Task StopListeningAndEvaluatingAsync(string statusMessage = "Stopped.")
        {
            _playCts?.Cancel();
            _audio.StopCapture();
            StatusService.Instance.StatusMessage = statusMessage;
            SetButtonStates(false);
        }
        // show the picker when overlay label is tapped and focus it
        private async void OnInstrumentLabelTapped(object? sender, EventArgs e)
        {
            try
            {
                // ensure changes happen on UI thread and allow layout to update before focus
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    IsInstrumentLabelVisible = false;
                    IsInstrumentPickerVisible = true;

                    // small delay to allow the picker to become visible/layout
                    await Task.Delay(80);

                    try
                    {
                        InstrumentPicker?.Focus();
                    }
                    catch
                    {
                        // best-effort; some platforms won't open on Focus()
                    }
                });
            }
            catch
            {
                // best-effort
            }
        }
        private async void OnColorButtonClicked(object? sender, EventArgs e)
        {
            await StopListeningAndEvaluatingAsync("Paused for color selection.");
            ColorPickerDialog.Show(_theme_service.PanelBackgroundColor);
        }

        private void OnAutoRepeatNewClicked(object? sender, EventArgs e)
        {
            if (_autoRepeat && !_repeatSameTune)
            {
                // Turn off auto-repeat new
                AutoRepeat = false;
                RepeatSameTune = false;
            }
            else
            {
                // Turn on auto-repeat with new tune
                AutoRepeat = true;
                RepeatSameTune = false;
            }
        }

        private void OnAutoRepeatSameClicked(object? sender, EventArgs e)
        {
            if (_autoRepeat && _repeatSameTune)
            {
                // Turn off auto-repeat same
                AutoRepeat = false;
                RepeatSameTune = false;
            }
            else
            {
                // Turn on auto-repeat with same tune
                AutoRepeat = true;
                RepeatSameTune = true;
                // Save the current notes to repeat
                if (_session?.NotesToDraw != null)
                {
                    _savedNotesToRepeat = new List<NoteInfo>(_session.NotesToDraw);
                }
            }
        }

        private void OnAutoRepeatScaleClicked(object? sender, EventArgs e)
        {
            // Toggle auto-repeat for scale mode
            AutoRepeat = !AutoRepeat;
            RepeatSameTune = false; // Not applicable for scales
        }
    }
}

