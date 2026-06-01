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
        private Drawables.V3StaffDrawable? _v3Drawable;
        private readonly SessionDatabase _sessionDb = null!;
        private readonly SessionResultDatabase _sessionResultDb = null!;
        private readonly NoteAttemptDatabase _noteAttemptDb = null!;
        private readonly IOrientationService _orientation = null!;
        private readonly ThemeService _theme_service = null!;

        private readonly object _processLock = new();
        private DateTime _lastProcess = DateTime.MinValue;
        private CancellationTokenSource? _playCts;
        private bool _isPlaying = false;
        private bool _isRunning = false;
        private bool _isBelowThreshold = true;
        private bool _isProgrammaticColorConfirm = false;
        private bool _isPageVisible = false;
        // A new GUID is assigned each time a session starts (see StartListeningAndEvaluatingAsync).
        // It is stored with every NoteAttempt so attempts can be grouped by session.
        private string _currentSessionId = string.Empty;
        private readonly SemaphoreSlim _regenerateSemaphore = new SemaphoreSlim(1, 1);
        private bool _suppressPickerSync = false;
        private string? _savedInstrumentForPlayback = null;
        private int _savedInstrumentIndexForPlayback = -1;

        // V3 home bottom-row picker references (initialized after InitializeComponent)
        private Picker _v3HomeInstrumentPicker = null!;
        private Picker _v3HomeKeyPicker = null!;
        private Picker _v3HomeScaleTunePicker = null!;
        private Label _v3HomeConcertKeyLabel = null!;
        private Button _v3StartStopButton = null!;

        // fields for inactivity tracking
        private DateTime _lastHeardTime = DateTime.UtcNow;
        private bool _inactivityStopped = false;
#pragma warning disable CS0414
        private readonly TimeSpan _inactivityTimeout = TimeSpan.FromMinutes(5);

        // When true, RegenerateNotesAsync is suppressed so the post-autoplay
        // green feedbacks and session stats remain visible until the next session.
        private bool _freezeStaff = false;

        // When true, the session result banner is being shown after a child-home
        // session completed with AutoRepeat off.  OnAppearing will skip auto-start
        // so the result stays visible until the user taps Start/Stop.
        private bool _holdResultForChildSession = false;
#pragma warning restore CS0414

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
            var isV3 = _session?.StaffDisplayMode == StaffDisplayMode.V3;
            var isRandom = _session?.IsRandomMode == true;
            IsRandomRepeatButtonsVisible = _isAutoRepeatVisible && isRandom && !isV3;
            IsScaleRepeatButtonVisible = _isAutoRepeatVisible && !isRandom && !isV3;
            OnPropertyChanged(nameof(IsNotV3Mode));
            OnPropertyChanged(nameof(IsV3Mode));
        }

        public bool IsNotV3Mode => _session?.StaffDisplayMode != StaffDisplayMode.V3;
        public bool IsV3Mode => _session?.StaffDisplayMode == StaffDisplayMode.V3;
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

                // V3 home bottom-row pickers — resolved here; populated in OnAppearing
                _v3HomeInstrumentPicker = this.FindByName<Picker>("V3HomeInstrumentPicker")!;
                _v3HomeKeyPicker        = this.FindByName<Picker>("V3HomeKeyPicker")!;
                _v3HomeScaleTunePicker  = this.FindByName<Picker>("V3HomeScaleTunePicker")!;
                _v3HomeConcertKeyLabel  = this.FindByName<Label>("V3HomeConcertKeyLabel")!;
                _v3StartStopButton      = this.FindByName<Button>("V3StartStopButton")!;

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
                _sessionResultDb = ServiceHelper.GetService<SessionResultDatabase>()!;
                _noteAttemptDb = ServiceHelper.GetService<NoteAttemptDatabase>()!;
                _audio = ServiceHelper.GetService<IAudioCaptureService>()!;
                _player = ServiceHelper.GetService<IAudioPlaybackService>()!;

                BindingContext = _session;
                StaffBorder.BindingContext = _theme_service;
                StaffGraphicsView.BindingContext = _theme_service;
                _audio.MaxBlocksReached += async () =>
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        if (_session.SessionCompleted)
                        {
                            // Only auto-restart if AutoRepeat is active; otherwise leave the
                            // final status visible and wait for the user to tap Start.
                            if (AutoRepeat)
                            {
                                Debug.WriteLine("[MainPage] MaxBlocksReached after session completed: full restart (AutoRepeat on).");
                                await StartListeningAndEvaluatingAsync();
                            }
                            else
                            {
                                Debug.WriteLine("[MainPage] MaxBlocksReached after session completed: AutoRepeat off, not restarting.");
                            }
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
                _v2Drawable = new Drawables.V2MeasureDrawable(_session, _theme_service!);
                V2StaffGraphicsView.Drawable = _v2Drawable;
                V2StaffGraphicsView.SetBinding(GraphicsView.BackgroundColorProperty, new Binding("PanelBackgroundColor"));

                // V3 staff drawable setup
                _v3Drawable = new Drawables.V3StaffDrawable(_session, _theme_service);
                V3StaffGraphicsView.Drawable = _v3Drawable;
                V3StaffGraphicsView.SetBinding(GraphicsView.BackgroundColorProperty, new Binding("PanelBackgroundColor"));

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
                        if (_session.StaffDisplayMode == StaffDisplayMode.V3)
                        {
                            // V3: the cycle is managed by SyncV3NoteStates / RefreshV3UpperStaffAsync.
                            // A full session-end here means both staffs are complete.
                            // In two-octave scale mode (UpperHasEndBar) OR Practice Tune mode this is
                            // a true end-of-sequence: run the normal summary + AutoRepeat path.
                            if ((_v3Drawable != null && _v3Drawable.UpperHasEndBar)
                                || _session.Tune == "Practice Tune")
                            {
                                // Fall through to the standard summary / AutoRepeat flow below.
                            }
                            else
                            {
                                if (AutoRepeat)
                                {
                                    await UpdateV3DisplayAsync();
                                    return;
                                }
                                // AutoRepeat is off — fall through to the summary / stop flow.
                            }
                        }

                        if (_session.V2StaffMode)
                        {
                            await AppendV2MeasuresAsync(V2BatchSize);
                            return;
                        }

                        await UpdateNoteStatsDatabaseAsync();
                        // SaveSessionStatAsync also saves SessionResult and checks level-up
                        // for child sessions; returns the new level if advanced, else null.
                        int? newChildLevel = await SaveSessionStatAsync();
                        // Rolling per-note attempt history runs unconditionally,
                        // independent of the CollectNoteStats preference.
                        await SaveNoteAttemptsForSessionAsync();

                        var (correct, wrong, apc) = _session.GetSessionCorrectWrongTotals();
                        var total = correct + wrong;
                        var percent = total > 0 ? (double)correct * 100 / total : 0.0;
                        var (meanBpm, stdBpm) = _session.GetFinalBpmStats();

                        // Show result banner at the top of the page.
                        // Append a level-up notice when the child has just advanced.
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            var bpmText = meanBpm.HasValue ? $"  ·  Tempo {meanBpm.Value:F0} BPM" : string.Empty;
                            var levelUpText = newChildLevel.HasValue
                                ? $"  🎉 Great job! You advanced to Level {newChildLevel.Value}!"
                                : string.Empty;
                            SessionResultLabel.Text =
                                $"✓ {apc:F0}% correct  ({(int)correct}/{(int)(correct + wrong)}){bpmText}{levelUpText}";
                            SessionResultBanner.IsVisible = true;
                        });
                        // Show level-up progress: session count, rolling averages, and note count
                        try
                        {
                            // Get recent qualifying sessions for this instrument/level
                            var rows = await _sessionResultDb.GetByLevelAndInstrumentAsync(_session.ChildLevel, _session.Instrument);
                            var qualifying = rows
                                .Where(r => r.TotalNotes >= Services.LevelUpService.MinNotesPerSession)
                                .Where(r => !(r.TotalNotes > 0 && r.OverallAccuracyPercent == 0))
                                .ToList();
                            int sessionCount = Services.LevelUpService.SessionCount;
                            var recent = qualifying.Take(sessionCount).ToList();
                            double avgPitch = recent.Count > 0 ? recent.Average(r => r.PitchAccuracyPercent) : 0.0;
                            double avgOverall = recent.Count > 0 ? recent.Average(r => r.OverallAccuracyPercent) : 0.0;
                            var timingSessions = recent.Where(r => r.AverageTimingMs > 0).ToList();
                            double avgTiming = timingSessions.Count > 0
                                ? timingSessions.Average(r => {
                                    double cv = r.TimingStdDevMs / r.AverageTimingMs * 100.0;
                                    return Math.Max(0, 100.0 - cv);
                                })
                                : 100.0;
                            int noteCount = (int)correct + (int)wrong;
                            // Debug info: show qualifying session details
                            string debug = string.Join(" | ", qualifying.Select(r => $"L{r.Level} {r.Instrument} Pch={r.PitchAccuracyPercent:F1} Tmg={(r.AverageTimingMs > 0 ? (100.0 - r.TimingStdDevMs / r.AverageTimingMs * 100.0).ToString("F1") : "-")} Ovrl={r.OverallAccuracyPercent:F1} N={r.TotalNotes}"));
                            StatusService.Instance.StatusMessage =
                                $"ssns={recent.Count}/{sessionCount}, Pch={avgPitch:F1}%, Tmg={avgTiming:F1}%, Overall={avgOverall:F1}%, Notes={noteCount}  [Q:{qualifying.Count}] {debug}";
                        }
                        catch (Exception ex)
                        {
                            StatusService.Instance.StatusMessage = $"LevelUp Progress: (error: {ex.Message})";
                        }
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
                            // For child-home sessions keep the result banner visible;
                            // block OnAppearing from auto-starting until the user acts.
                            if (_session.ChildLevel > 0)
                                _holdResultForChildSession = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Session completion error: {ex}");
                    }
                };

                _session.PropertyChanged += async (_, e) =>
                {
                    if (e.PropertyName == nameof(NoteSessionService.AutoStart) && _session.AutoStart && _isPageVisible)
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

                // Combined Scale + Tune picker: Tuner / individual practice tunes / scales
                var practiceTuneTitles = musicmate.Models.TuneLibrary.All.Select(t => t.Title).ToArray();
                var scaleTuneOptions = new[] { "Tuner" }
                    .Concat(practiceTuneTitles)
                    .Concat(NoteSessionService.AvailableScales)
                    .ToArray();
                ScaleTunePicker.ItemsSource = scaleTuneOptions;

                var savedTune = Preferences.Default.Get<string?>("SelectedTune", null);
                if (!string.IsNullOrEmpty(savedTune) && savedTune == "Tuner")
                    _session.Tune = savedTune;
                else if (!string.IsNullOrEmpty(savedTune) && practiceTuneTitles.Contains(savedTune))
                {
                    var savedPT = musicmate.Models.TuneLibrary.All.FirstOrDefault(t => t.Title == savedTune);
                    if (savedPT != null)
                        _session.SelectPracticeTune(savedPT);
                }
                // else _session.Tune stays "Selected Scale" (persisted via SelectedTune preference)

                var initialScaleTuneSelection = _session.Tune == "Tuner" ? "Tuner"
                    : _session.Tune == "Practice Tune" ? (_session.CurrentTune?.Title ?? practiceTuneTitles[0])
                    : _session.SelectedScale;
                var scaleTuneIdx = Array.IndexOf(scaleTuneOptions, initialScaleTuneSelection);
                ScaleTunePicker.SelectedIndex = scaleTuneIdx >= 0 ? scaleTuneIdx : 0;
                _lastValidScaleTuneIndex = ScaleTunePicker.SelectedIndex;
                IsAutoRepeatVisible = _session.Tune != "Tuner";

                ScaleTunePicker.SelectedIndexChanged += OnScaleTunePickerChanged;

                _v3HomeInstrumentPicker.ItemsSource = instrumentOptions.Select(s => s.Split(',')[0].Trim()).ToArray();
                _v3HomeInstrumentPicker.SelectedIndex = InstrumentPicker.SelectedIndex;

                _v3HomeKeyPicker.ItemsSource = KeyPicker.ItemsSource;
                _v3HomeKeyPicker.SelectedIndex = KeyPicker.SelectedIndex;

                _v3HomeScaleTunePicker.ItemsSource = scaleTuneOptions;
                _v3HomeScaleTunePicker.SelectedIndex = ScaleTunePicker.SelectedIndex;

                InstrumentPicker.SelectedIndexChanged += InstrumentPicker_SelectedIndexChanged;
                KeyPicker.SelectedIndexChanged += OnKeyPickerChangedWithPrompt;

                // Apply initial pickers-row visibility based on the loaded display mode
                UpdatePickersContainerVisibility();
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
            // Prevent concurrent calls — if a regeneration is already in progress, skip this one.
            if (!await _regenerateSemaphore.WaitAsync(0))
                return;
            try
            {
            // Hide any previous session result banner when new notes are generated.
            await MainThread.InvokeOnMainThreadAsync(() => SessionResultBanner.IsVisible = false);
            _holdResultForChildSession = false;

            // While showing post-autoplay results, do not overwrite the staff.
            if (_freezeStaff)
                return;

            var width = StaffGraphicsView.Width <= 0 ? 360 : StaffGraphicsView.Width;
            await _session.GenerateNotesAsync(width);

#if DEBUG
            if (_session.IsRandomMode)
            {
                var names = string.Join(", ", _session.NotesToDraw.Select(n => n.Name));
                Debug.WriteLine($"[Random] Generated {_session.NotesToDraw.Count} notes: {names}");
            }
#endif

            if (_session.Tune == "Tuner")
            {
                // Tuner mode: never generate a scale sequence — just update visibility.
                UpdateTunerVisibility();
            }
            else if (_session.StaffDisplayMode == StaffDisplayMode.V3)
            {
                // Show V3 border first so the GraphicsView gets a layout width before we draw.
                StaffBorder.IsVisible   = false;
                V2StaffBorder.IsVisible = false;
                V2ModeBanner.IsVisible  = false;
                V3StaffBorder.IsVisible = true;

                // Wait up to 500 ms for the view to get a measured width.
                var sw2 = System.Diagnostics.Stopwatch.StartNew();
                while (V3StaffGraphicsView.Width <= 0 && sw2.ElapsedMilliseconds < 500)
                    await Task.Delay(20);

                await UpdateV3DisplayAsync();
            }
            else if (_session.V2StaffMode)
            {
                UpdateV2Display();
                StaffBorder.IsVisible = false;
                V2StaffBorder.IsVisible = true;
                V3StaffBorder.IsVisible = false;
                V2ModeBanner.IsVisible = true;
            }
            else
            {
                StaffBorder.IsVisible = true;
                V2StaffBorder.IsVisible = false;
                V3StaffBorder.IsVisible = false;
                V2ModeBanner.IsVisible = false;
                StaffGraphicsView.Invalidate();
                UpdateStaffHeight();
            }
            }
            finally
            {
                _regenerateSemaphore.Release();
            }
        }

        // ── V2 multi-measure queue ────────────────────────────────────────────────

        /// <summary>How many measures to generate at once (initial fill and each top-up).</summary>
        private const int V2BatchSize = 8;
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
            _v2ExcludedMidis = _session.IsRandomMode
                ? await _session.GetMasteredMidiNumbersAsync()
                : new HashSet<int>();
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

            var gen = new MusicSequenceGenerator
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
                UseScaleOrder        = !_session.IsRandomMode,
                // Resume the scale walk at the correct position when appending batches
                // so the descending branch continues instead of jumping back to the bottom.
                ScaleWalkOffset      = _v2NextGlobalNoteIndex,
                // Pass through the accidental-density setting so V2 random mode
                // inserts chromatic tones at the same rate the user configured.
                AccidentalPercent    = _session.IsRandomMode ? _session.AccidentalPercent : 0
            };
            Debug.WriteLine($"[V2Gen] Tune={_session.Tune} Random={_session.IsRandomMode} AccPct={_session.AccidentalPercent} EffectiveAccPct={(_session.IsRandomMode ? _session.AccidentalPercent : 0)}");
            return gen;
        }

        /// <summary>
        /// Converts a <see cref="PracticeTune"/> into a flat list of <see cref="GeneratedNote"/>
        /// with correct <see cref="GeneratedNote.BeatPosition"/>, <see cref="GeneratedNote.MeasureIndex"/>,
        /// and accidentals parsed from each note's spelled name.
        /// </summary>
        private static List<GeneratedNote> BuildV2NotesFromTune(PracticeTune tune, string key = "C")
        {
            var result = new List<GeneratedNote>();
            double beatCursor = 0.0;
            int measureIndex = 0;

            foreach (var measure in tune.Measures)
            {
                double measureBeat = beatCursor;
                foreach (var mn in measure.Notes)
                {
                    GeneratedNote gn;
                    if (mn.IsRest)
                    {
                        gn = GeneratedNote.Rest(mn.Duration, measureIndex, beatCursor);
                    }
                    else
                    {
                        var raw    = mn.SpelledName.Trim();
                        char letter = char.ToUpperInvariant(raw[0]);
                        int octave  = 4;
                        for (int i = raw.Length - 1; i >= 0; i--)
                        {
                            if (char.IsDigit(raw[i]))
                            {
                                int j = i;
                                while (j > 0 && char.IsDigit(raw[j - 1])) j--;
                                if (int.TryParse(raw.Substring(j, i - j + 1), out var oct)) octave = oct;
                                break;
                            }
                        }
                        Accidental acc = Accidental.None;
                        if (raw.Contains("##"))      acc = Accidental.DoubleSharp;
                        else if (raw.Contains("bb")) acc = Accidental.DoubleFlat;
                        else if (raw.Contains('#'))  acc = Accidental.Sharp;
                        else if (raw.Length > 1 && raw[1] == 'b') acc = Accidental.Flat;

                        // Apply the key signature: if the note has no explicit accidental,
                        // adjust the MIDI number for any flat/sharp implied by the key.
                        var adjustedMidi = NoteSessionService.ApplyKeySignatureToMidi(mn.SpelledName, mn.MidiNumber, key);

                        gn = new GeneratedNote
                        {
                            MidiNumber      = adjustedMidi,
                            Letter          = letter,
                            Octave          = octave,
                            Accidental      = acc,
                            SpelledName     = mn.SpelledName,
                            TargetFrequency = 440.0 * Math.Pow(2.0, (adjustedMidi - 69) / 12.0),
                            Duration        = mn.Duration,
                            IsRest          = false,
                            MeasureIndex    = measureIndex,
                            BeatPosition    = beatCursor,
                        };
                    }
                    result.Add(gn);
                    beatCursor += mn.Duration.ToBeatValue();
                }
                measureIndex++;
            }
            return result;
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

                List<GeneratedNote> flat;
                List<double> barBeats;
                var existingBarBeats = new HashSet<double>();

                if (_session.Tune == "Practice Tune" && _session.CurrentTune != null)
                {
                    // Build the note list directly from the tune so the actual melody
                    // (e.g. Ode to Joy) is shown instead of a generated scale walk.
                    flat     = BuildV2NotesFromTune(_session.CurrentTune, _session.Key);
                    barBeats = ComputeNewBarBeats(flat, existingBarBeats);
                    // Advance offsets to the end of the tune so appending is disabled.
                    _v2NextMeasureIndex    = _session.CurrentTune.Measures.Count;
                    _v2NextBeatOffset      = flat.Sum(n => n.BeatDuration);
                    _v2NextGlobalNoteIndex = flat.Count(n => !n.IsRest);
                }
                else
                {
                    await LoadV2ExcludedMidisAsync();

                    var gen      = BuildV2Generator(V2BatchSize);
                    var measures = gen.GenerateSequence();
                    flat     = MusicSequenceGenerator.Flatten(measures);
                    barBeats = ComputeNewBarBeats(flat, existingBarBeats);

                    // Advance offsets past the generated measures.
                    _v2NextMeasureIndex    += measures.Count;
                    _v2NextBeatOffset      += measures.Count * (double)gen.TimeSignature.TotalBeats;
                    _v2NextGlobalNoteIndex += flat.Count(n => !n.IsRest);
                }

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

        // ── V3 two-staff display ──────────────────────────────────────────────────

        /// <summary>How many measures to put on each V3 staff.</summary>
        private const int V3MeasuresPerStaff = 4;

        // Offsets for appending the lower staff content.
        private int    _v3LowerMeasureIndex    = 0;
        private double _v3LowerBeatOffset      = 0.0;
        private int    _v3LowerGlobalNoteIndex = 0;

        /// <summary>
        /// Populates the V3 drawable with an upper and lower staff worth of notes.
        /// Upper staff is played first; lower staff follows.
        /// </summary>
        private async Task UpdateV3DisplayAsync()
        {
            if (_v3Drawable == null) return;
            try
            {
                // Reset queue offsets.
                _v2NextMeasureIndex    = 0;
                _v2NextBeatOffset      = 0.0;
                _v2NextGlobalNoteIndex = 0;
                _v2AppendInProgress    = false;

                List<GeneratedNote> upperFlat;
                List<double>        upperBarBeats;
                List<GeneratedNote> lowerFlat;
                List<double>        lowerBarBeats;
                var existingUpper = new HashSet<double>();
                var existingLower = new HashSet<double>();

                if (_session.Tune == "Practice Tune" && _session.CurrentTune != null)
                {
                    // Split tune measures between upper and lower staff.
                    var allNotes = BuildV2NotesFromTune(_session.CurrentTune, _session.Key);
                    var allMeasures = _session.CurrentTune.Measures.Count;
                    int splitAt = allMeasures / 2;

                    // Gather beat threshold for split.
                    double splitBeat = 0.0;
                    for (int m = 0; m < splitAt && m < _session.CurrentTune.Measures.Count; m++)
                        foreach (var mn in _session.CurrentTune.Measures[m].Notes)
                            splitBeat += mn.Duration.ToBeatValue();

                    upperFlat     = allNotes.Where(n => (n.BeatPosition ?? 0) < splitBeat).ToList();
                    lowerFlat     = allNotes.Where(n => (n.BeatPosition ?? 0) >= splitBeat).ToList();
                    upperBarBeats = ComputeNewBarBeats(upperFlat, existingUpper);
                    lowerBarBeats = ComputeNewBarBeats(lowerFlat, existingLower);

                    _v2NextMeasureIndex    = allMeasures;
                    _v2NextBeatOffset      = allNotes.Sum(n => n.BeatDuration);
                    _v2NextGlobalNoteIndex = allNotes.Count(n => !n.IsRest);

                    _v3LowerMeasureIndex    = _v2NextMeasureIndex;
                    _v3LowerBeatOffset      = _v2NextBeatOffset;
                    _v3LowerGlobalNoteIndex = _v2NextGlobalNoteIndex;
                    if (_v3Drawable != null) _v3Drawable.UpperHasEndBar = false;
                }
                else
                {
                    await LoadV2ExcludedMidisAsync();

                    // Detect a two-octave scale range: when the hi−lo span is ≥ 24 semitones
                    // (two full octaves) and we are in scale-order mode, generate the full
                    // ascending+descending walk as a single sequence, then split it at the
                    // peak so the ascending half goes on the upper staff and the descending
                    // half goes on the lower staff.
                    bool isScaleMode = !_session.IsRandomMode;
                    int loMidi = NoteSessionService.NoteNameToMidi(_session.LowestNote);
                    int hiMidi = NoteSessionService.NoteNameToMidi(_session.HighestNote);
                    // Use the two-staff split only when the range contains at least two tonic
                    // notes (i.e. an octave-bounded scale walk tonic-to-tonic is possible).
                    // This avoids picking the wrong peak when the raw range spans > 12 semitones
                    // but holds only a single tonic (e.g. C4–F5 in Bb major).
                    bool isTwoOctave = false;
                    if (isScaleMode && loMidi >= 0 && hiMidi > loMidi)
                    {
                        int tonicPc = ((NoteSessionService.NoteNameToMidi($"{_session.Key}4") % 12) + 12) % 12;
                        int tOctStart = loMidi;
                        while (tOctStart <= hiMidi && ((tOctStart % 12 + 12) % 12) != tonicPc) tOctStart++;
                        int tOctEnd = hiMidi;
                        while (tOctEnd >= loMidi && ((tOctEnd % 12 + 12) % 12) != tonicPc) tOctEnd--;
                        if (tOctStart == tOctEnd) tOctStart -= 12;
                        isTwoOctave = tOctStart < tOctEnd;
                    }

                    if (isTwoOctave)
                    {
                        // Build a combined generator sized to hold the full ascending+descending
                        // scale walk.  The walk length for N pitch-pool notes is (2N − 2) events
                        // so use enough measures to hold it all at the smallest allowed duration.
                        var timeSig = _session.V2TimeSignature switch
                        {
                            "3/4" => TimeSignature.ThreeFour,
                            "2/4" => TimeSignature.TwoFour,
                            _     => TimeSignature.FourFour
                        };
                        // A safe upper bound: even a chromatic 3-octave range (37 pitches) needs
                        // at most (2*37−2)=72 quarter notes = 18 bars of 4/4.  Cap at 24 to be safe.
                        var genAll = BuildV2Generator(24);
                        var allMeasures = genAll.GenerateSequence();
                        var allNotes = MusicSequenceGenerator.Flatten(allMeasures);

                        // Find the first occurrence of the maximum MIDI in the walk
                        // — that is the peak note where ascending turns to descending.
                        int peakMidi = allNotes.Where(n => !n.IsRest).Max(n => n.MidiNumber);
                        int peakIdx  = allNotes.FindIndex(n => !n.IsRest && n.MidiNumber == peakMidi);

                        // Upper staff: notes up to and including the peak note.
                        // Lower staff: notes after the peak.
                        // Trim trailing rests from upper so the end bar lands right after the peak.
                        int splitIdx = peakIdx >= 0 ? peakIdx + 1 : allNotes.Count / 2;
                        upperFlat = allNotes.Take(splitIdx).ToList();
                        lowerFlat = allNotes.Skip(splitIdx).ToList();

                        // Drop notes once the descending walk turns back upward — that
                        // signals the start of the next cycle.  The generator intentionally
                        // stops one step above the bottom tonic (e.g. D4 for C Major) so
                        // the bottom tonic never appears in the lower half; searching for
                        // loMidi would find the C4 that starts the *next* cycle instead.
                        int cutIdx = lowerFlat.Count;
                        int prevPitchMidi = -1;
                        for (int li = 0; li < lowerFlat.Count; li++)
                        {
                            if (lowerFlat[li].IsRest) continue;
                            int m = lowerFlat[li].MidiNumber;
                            if (prevPitchMidi >= 0 && m > prevPitchMidi)
                            {
                                cutIdx = li;   // stop before the ascending restart
                                break;
                            }
                            prevPitchMidi = m;
                        }
                        lowerFlat = lowerFlat.Take(cutIdx).ToList();

                        // Re-offset lower staff beat positions to start at 0.
                        double lowerBeatShift = lowerFlat.Count > 0 ? (lowerFlat[0].BeatPosition ?? 0.0) : 0.0;
                        if (lowerBeatShift > 0.0)
                        {
                            for (int i = 0; i < lowerFlat.Count; i++)
                            {
                                var n = lowerFlat[i];
                                lowerFlat[i] = new GeneratedNote
                                {
                                    MidiNumber       = n.MidiNumber,
                                    Letter           = n.Letter,
                                    Octave           = n.Octave,
                                    Accidental       = n.Accidental,
                                    SpelledName      = n.SpelledName,
                                    TargetFrequency  = n.TargetFrequency,
                                    Duration         = n.Duration,
                                    IsRest           = n.IsRest,
                                    MeasureIndex     = n.MeasureIndex,
                                    BeatPosition     = (n.BeatPosition ?? 0.0) - lowerBeatShift,
                                    IsPlayedCorrectly = n.IsPlayedCorrectly
                                };
                            }
                        }

                        upperBarBeats = ComputeNewBarBeats(upperFlat, existingUpper);
                        lowerBarBeats = ComputeNewBarBeats(lowerFlat, existingLower);

                        double upperBeats = upperFlat.Sum(n => n.BeatDuration);
                        double lowerBeats = lowerFlat.Sum(n => n.BeatDuration);
                        int upperPitches  = upperFlat.Count(n => !n.IsRest);
                        int lowerPitches  = lowerFlat.Count(n => !n.IsRest);

                        _v2NextMeasureIndex    = allMeasures.Count;
                        _v2NextBeatOffset      = upperBeats + lowerBeats;
                        _v2NextGlobalNoteIndex = upperPitches + lowerPitches;
                        _v3LowerMeasureIndex    = _v2NextMeasureIndex;
                        _v3LowerBeatOffset      = _v2NextBeatOffset;
                        _v3LowerGlobalNoteIndex = _v2NextGlobalNoteIndex;
                    }
                    else
                    {
                        // Standard path: two independent measures-per-staff blocks.
                        var genUpper = BuildV2Generator(V3MeasuresPerStaff);
                        var upperMeasures = genUpper.GenerateSequence();
                        upperFlat     = MusicSequenceGenerator.Flatten(upperMeasures);
                        upperBarBeats = ComputeNewBarBeats(upperFlat, existingUpper);

                        _v2NextMeasureIndex    += upperMeasures.Count;
                        _v2NextBeatOffset      += upperMeasures.Count * (double)genUpper.TimeSignature.TotalBeats;
                        _v2NextGlobalNoteIndex += upperFlat.Count(n => !n.IsRest);

                        var genLower = BuildV2Generator(V3MeasuresPerStaff);
                        var lowerMeasures = genLower.GenerateSequence();
                        lowerFlat     = MusicSequenceGenerator.Flatten(lowerMeasures);
                        lowerBarBeats = ComputeNewBarBeats(lowerFlat, existingLower);

                        _v3LowerMeasureIndex    = _v2NextMeasureIndex + lowerMeasures.Count;
                        _v3LowerBeatOffset      = _v2NextBeatOffset + lowerMeasures.Count * (double)genLower.TimeSignature.TotalBeats;
                        _v3LowerGlobalNoteIndex = _v2NextGlobalNoteIndex + lowerFlat.Count(n => !n.IsRest);

                        _v2NextMeasureIndex    = _v3LowerMeasureIndex;
                        _v2NextBeatOffset      = _v3LowerBeatOffset;
                        _v2NextGlobalNoteIndex = _v3LowerGlobalNoteIndex;
                    }

                    // Signal the drawable whether to draw a single end bar on the upper staff.
                    _v3Drawable.UpperHasEndBar = isTwoOctave;
                }

                // ── Push to V3 drawable ────────────────────────────────────────────
                _v3Drawable.UpperNotes      = upperFlat;
                _v3Drawable.LowerNotes      = lowerFlat;
                _v3Drawable.UpperBarBeats   = upperBarBeats;
                _v3Drawable.LowerBarBeats   = lowerBarBeats;
                _v3Drawable.UpperNoteStates = new V2NoteState[upperFlat.Count];
                _v3Drawable.LowerNoteStates = new V2NoteState[lowerFlat.Count];
                _v3Drawable.IsUpperActive   = true;
                _v3Drawable.ActiveNoteIndex = 0;
                _v3Drawable.UpperAlpha      = 1f;
                _v3Drawable.LowerAlpha      = 1f;

                // Mark first non-rest note on upper staff as Current.
                for (int i = 0; i < upperFlat.Count; i++)
                {
                    if (!upperFlat[i].IsRest)
                    {
                        _v3Drawable.UpperNoteStates[i] = V2NoteState.Current;
                        _v3Drawable.ActiveNoteIndex = i;
                        break;
                    }
                }

                // Populate session NotesToDraw from upper then lower.
                int sessionIdx = 0;
                _session.NotesToDraw.Clear();
                _session.FeedbackViewModels.Clear();
                foreach (var gn in upperFlat.Concat(lowerFlat))
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
                    _session.FeedbackViewModels.Add(new FeedbackItem(sessionIdx++, 0, 0, false));
                }

                int upperPitchCount = upperFlat.Count(n => !n.IsRest);

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    ApplyV3Height();
                    V3StaffGraphicsView.Invalidate();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[V3] UpdateV3DisplayAsync ERROR: {ex}");
                StatusService.Instance.StatusMessage = $"[V3 Error] {ex.Message}";
            }
        }

        /// <summary>
        /// Syncs V3 note states from session progress; mirrors <see cref="SyncV2NoteStates"/>
        /// but covers two staffs.  When the player finishes the upper staff the lower becomes
        /// active, and new notes are loaded onto the upper staff (fade in).
        /// </summary>
        private void SyncV3NoteStates()
        {
            if (_v3Drawable == null) return;

            int upperPitchCount = _v3Drawable.UpperNotes.Count(n => !n.IsRest);
            int currentSession  = _session.CurrentNoteIndex;

            bool isUpperActive = currentSession < upperPitchCount;
            _v3Drawable.IsUpperActive = isUpperActive;

            // ── Upper staff states ────────────────────────────────────────────────
            var upperStates = new V2NoteState[_v3Drawable.UpperNotes.Count];
            int si = 0;
            for (int i = 0; i < _v3Drawable.UpperNotes.Count; i++)
            {
                if (_v3Drawable.UpperNotes[i].IsRest) { upperStates[i] = V2NoteState.Pending; continue; }
                if (si < currentSession)
                    upperStates[i] = _session.CorrectNoteIndices.Contains(si) ? V2NoteState.Correct : V2NoteState.Wrong;
                else if (si == currentSession && isUpperActive)
                {
                    bool hasWrong = _session.NoteFeedbacks.TryGetValue(si, out var fb) && fb.Wrong > 0;
                    upperStates[i] = hasWrong ? V2NoteState.Wrong : V2NoteState.Current;
                }
                else
                    upperStates[i] = V2NoteState.Pending;
                si++;
            }
            _v3Drawable.UpperNoteStates = upperStates;

            // ── Lower staff states ────────────────────────────────────────────────
            var lowerStates = new V2NoteState[_v3Drawable.LowerNotes.Count];
            int li = 0;
            for (int i = 0; i < _v3Drawable.LowerNotes.Count; i++)
            {
                if (_v3Drawable.LowerNotes[i].IsRest) { lowerStates[i] = V2NoteState.Pending; continue; }
                int globalIdx = upperPitchCount + li;
                if (globalIdx < currentSession)
                    lowerStates[i] = _session.CorrectNoteIndices.Contains(globalIdx) ? V2NoteState.Correct : V2NoteState.Wrong;
                else if (globalIdx == currentSession && !isUpperActive)
                {
                    bool hasWrong = _session.NoteFeedbacks.TryGetValue(globalIdx, out var fb2) && fb2.Wrong > 0;
                    lowerStates[i] = hasWrong ? V2NoteState.Wrong : V2NoteState.Current;
                }
                else
                    lowerStates[i] = V2NoteState.Pending;
                li++;
            }
            _v3Drawable.LowerNoteStates = lowerStates;

            _v3Drawable.ActiveNoteIndex = isUpperActive ? currentSession : currentSession - upperPitchCount;

            V3StaffGraphicsView.Invalidate();

            // ── Transition: player just moved onto lower staff → refresh upper ────
            // Skip in two-octave scale mode: the sequence is a fixed complete walk.
            if (!isUpperActive && _v3Drawable.UpperAlpha >= 1f && _session.Tune != "Practice Tune"
                && !_v3Drawable.UpperHasEndBar)
            {
                // Check whether we're on the first note of the lower staff (just transitioned).
                int lowerSessionStart = upperPitchCount;
                if (currentSession == lowerSessionStart)
                    _ = RefreshV3UpperStaffAsync();
            }
        }

        /// <summary>
        /// Generates new notes for the upper V3 staff while the player is on the lower staff,
        /// then fades in the new upper staff content.
        /// </summary>
        private async Task RefreshV3UpperStaffAsync()
        {
            if (_v3Drawable == null || _session.Tune == "Practice Tune") return;
            try
            {
                var gen      = BuildV2Generator(V3MeasuresPerStaff);
                var measures = gen.GenerateSequence();
                var newNotes = MusicSequenceGenerator.Flatten(measures);
                var barBeats = ComputeNewBarBeats(newNotes, new HashSet<double>());

                _v2NextMeasureIndex    += measures.Count;
                _v2NextBeatOffset      += measures.Count * (double)gen.TimeSignature.TotalBeats;
                _v2NextGlobalNoteIndex += newNotes.Count(n => !n.IsRest);

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    _v3Drawable.UpperNotes      = newNotes;
                    _v3Drawable.UpperBarBeats   = barBeats;
                    _v3Drawable.UpperNoteStates = new V2NoteState[newNotes.Count];
                    _v3Drawable.UpperAlpha      = 0f;
                    ApplyV3Height();
                });
                var sw = System.Diagnostics.Stopwatch.StartNew();
                const double fadeDuration = 300.0;
                while (sw.ElapsedMilliseconds < fadeDuration)
                {
                    float alpha = (float)(sw.ElapsedMilliseconds / fadeDuration);
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        _v3Drawable.UpperAlpha = alpha;
                        V3StaffGraphicsView.Invalidate();
                    });
                    await Task.Delay(16);
                }
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    _v3Drawable.UpperAlpha = 1f;
                    V3StaffGraphicsView.Invalidate();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[V3] RefreshV3UpperStaffAsync ERROR: {ex}");
            }
        }

        /// <summary>
        /// Generates new notes for the lower V3 staff while the player is on the upper staff,
        /// then fades in the new lower staff content.  Called when play starts on upper staff
        /// after a lower-staff refresh cycle.
        /// </summary>
        private async Task RefreshV3LowerStaffAsync()
        {
            if (_v3Drawable == null || _session.Tune == "Practice Tune") return;
            try
            {
                var gen      = BuildV2Generator(V3MeasuresPerStaff);
                var measures = gen.GenerateSequence();
                var newNotes = MusicSequenceGenerator.Flatten(measures);
                var barBeats = ComputeNewBarBeats(newNotes, new HashSet<double>());

                _v3LowerMeasureIndex    += measures.Count;
                _v3LowerBeatOffset      += measures.Count * (double)gen.TimeSignature.TotalBeats;
                _v3LowerGlobalNoteIndex += newNotes.Count(n => !n.IsRest);

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    _v3Drawable.LowerNotes      = newNotes;
                    _v3Drawable.LowerBarBeats   = barBeats;
                    _v3Drawable.LowerNoteStates = new V2NoteState[newNotes.Count];
                    _v3Drawable.LowerAlpha      = 0f;
                    ApplyV3Height();
                });

                var sw = System.Diagnostics.Stopwatch.StartNew();
                const double fadeDuration = 300.0;
                while (sw.ElapsedMilliseconds < fadeDuration)
                {
                    float alpha = (float)(sw.ElapsedMilliseconds / fadeDuration);
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        _v3Drawable.LowerAlpha = alpha;
                        V3StaffGraphicsView.Invalidate();
                    });
                    await Task.Delay(16);
                }
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    _v3Drawable.LowerAlpha = 1f;
                    V3StaffGraphicsView.Invalidate();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[V3] RefreshV3LowerStaffAsync ERROR: {ex}");
            }
        }

        /// <summary>
        /// Appends <paramref name="measureCount"/> more measures to the v2 sequence
        /// without restarting the session.  Called when the player is getting close
        /// to the end of the visible note list.
        /// </summary>
        private async Task AppendV2MeasuresAsync(int measureCount)
        {
            // Practice Tune is fully loaded upfront — nothing to append.
            if (_session.Tune == "Practice Tune") return;
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

        /// <summary>
        /// Resizes the V3 staff canvas and repositions the Start/Stop button midway
        /// between the upper and lower staffs.  Must be called on the main thread.
        /// </summary>
        private void ApplyV3Height()
        {
            // Measure available height: window height minus shell nav bar.
            // In V3 mode the pickers row is hidden so the staff fills the full content area.
            float availH = 300f;
            try
            {
                var win = Application.Current?.Windows?.FirstOrDefault();
                if (win != null)
                {
                    double winH = win.Height;
                    // Subtract shell nav bar (~50 dp) to get usable content area.
                    availH = (float)Math.Max(100, winH - 50);
                }
            }
            catch { /* keep default */ }

            _v3Drawable.AvailableHeight      = availH;
            var h   = _v3Drawable.ComputeRequiredHeight();
            V3StaffGraphicsView.HeightRequest = h;
            V3StaffBorder.HeightRequest       = h;
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
                    _v3StartStopButton.Text = "■";
                    _v3StartStopButton.TextColor = Color.FromArgb("#E04040");
                    PlayEvaluateButton.IsEnabled = false;
                }
                else
                {
                    StartStopButton.Text = "●";
                    StartStopButton.TextColor = Color.FromArgb("#008000");
                    _v3StartStopButton.Text = "●";
                    _v3StartStopButton.TextColor = Colors.Green;
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
                    StatusService.Instance.StatusMessage = "Stopped. Tap circle to listen, arrowhead (scroll down) to play.";
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
                _holdResultForChildSession = false;
                _session.SessionCompleted = false;
                StatusService.Instance.StatusMessage = "Listening, go ahead and play!";
                await StartListeningAndEvaluatingAsync();
            }
        }

        protected async override void OnAppearing()
        {
            base.OnAppearing();
            _isPageVisible = true;
            _orientation.ForceLandscape();

            Debug.WriteLine($"[DEBUG] OnAppearing: IsAutoRepeatVisible={IsAutoRepeatVisible}, Tune={_session.Tune}");
            IsAutoRepeatVisible = _session.Tune != "Tuner";
            DeviceDisplay.Current.KeepScreenOn = true;

            // Populate V3 home pickers here — visual tree is guaranteed ready after InitializeComponent
            // XAML wires the SelectedIndexChanged events; we only need to populate ItemsSource + index.
            _v3HomeScaleTunePicker ??= this.FindByName<Picker>("V3HomeScaleTunePicker");
            _v3HomeInstrumentPicker ??= this.FindByName<Picker>("V3HomeInstrumentPicker");
            _v3HomeKeyPicker ??= this.FindByName<Picker>("V3HomeKeyPicker");
            _v3HomeConcertKeyLabel ??= this.FindByName<Label>("V3HomeConcertKeyLabel");
            if (_v3HomeScaleTunePicker != null && _v3HomeScaleTunePicker.ItemsSource == null)
                UpdateScaleTunePicker();
            if (_v3HomeInstrumentPicker != null && _v3HomeInstrumentPicker.ItemsSource == null)
            {
                var instrumentOptions = NoteSessionService.InstrumentOptions.Cast<string>().ToArray();
                _v3HomeInstrumentPicker.ItemsSource = instrumentOptions.Select(s => s.Split(',')[0].Trim()).ToArray();
                _v3HomeInstrumentPicker.SelectedIndex = InstrumentPicker.SelectedIndex;
            }
            if (_v3HomeKeyPicker != null && _v3HomeKeyPicker.ItemsSource == null)
            {
                _v3HomeKeyPicker.ItemsSource = KeyPicker.ItemsSource;
                _v3HomeKeyPicker.SelectedIndex = KeyPicker.SelectedIndex;
            }

#if DEBUG
            if (_session.AutoStart && _session.Tune != "Tuner" && _session.ChildLevel == 0)
            {
                AutoRepeat = true;
            }
#endif

            if (_session.AutoStart)
            {
                // If we're holding the result banner for a just-completed child session,
                // skip auto-start this one time so the player can see their score.
                if (_holdResultForChildSession)
                    return;
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
                    // Wait for whichever staff view is active to get a valid layout width.
                    if (_session.StaffDisplayMode == StaffDisplayMode.V3)
                    {
                        while (V3StaffGraphicsView != null && V3StaffGraphicsView.Width <= 0 && sw.ElapsedMilliseconds < 1500)
                            await Task.Delay(40);
                    }
                    else
                    {
                        while (StaffGraphicsView != null && StaffGraphicsView.Width <= 0 && sw.ElapsedMilliseconds < 1000)
                            await Task.Delay(40);
                    }
                    await RegenerateNotesAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[OnAppearing] ERROR regenerating notes: {ex}");
                }
            }
        }

        protected override void OnNavigatedTo(NavigatedToEventArgs args)
        {
            base.OnNavigatedTo(args);
            // Shell calls OnNavigatedTo after it has finished restoring scroll position,
            // so this is the correct place to snap the scroll so no note heads are hidden.
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    // Compute the Y position of the topmost pixel of the highest note
                    // in the active staff so we scroll exactly to show it.
                    double scrollY = 0;
                    if (_session.StaffDisplayMode == StaffDisplayMode.V3 && _v3Drawable != null)
                    {
                        // V3StaffBorder sits inside the VerticalStackLayout.
                        // Its Y relative to MainScrollView content is its absolute position
                        // within MainPageMainLayout.
                        double borderY = V3StaffBorder.Y
                                       + (V3StaffBorder.Parent is View p ? p.Y : 0);
                        // TopMargin inside the drawable is the clearance above the highest note.
                        // Subtract TopMargin so the scroll top lands at the notehead top edge.
                        scrollY = Math.Max(0, borderY);
                    }
                    await MainScrollView.ScrollToAsync(0, scrollY, false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[OnNavigatedTo] scroll error: {ex}");
                }
            });
        }
        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            _isPageVisible = false;
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

            // If currently listening, stop before starting auto-play.
            if (_isRunning)
            {
                try
                {
                    _playCts?.Cancel();
                    _audio.StopCapture();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PlayEvaluate] Stop listening error: {ex}");
                }
                finally
                {
                    SetButtonStates(false);
                }
                await Task.Delay(80); // brief pause so audio pipeline drains
            }

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
                    // Notify session so consecutive same-pitch notes can be distinguished
                    _session.NotifySilence();
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
                        if (_session.V2StaffMode)
                            V2StaffGraphicsView.Invalidate();
                        else
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
                    if (_session.StaffDisplayMode == StaffDisplayMode.V3)
                    {
                        var result = _session.Evaluate(freq);
                        if (_session.UpdateFeedbackForCurrent(freq, result))
                        {
                            if (result.correct)
                            {
                                var prevName = _session.NotesToDraw.ElementAtOrDefault(_session.CurrentNoteIndex - 1)?.Name ?? "";
                                if (!string.IsNullOrEmpty(prevName))
                                    _session.RecordRandomSessionNoteResult(prevName, true);

                                // When upper staff is exhausted, trigger lower-staff refresh.
                                int upperPitchCount = _v3Drawable?.UpperNotes.Count(n => !n.IsRest) ?? 0;
                                if (_session.CurrentNoteIndex == upperPitchCount && _v3Drawable != null
                                    && _v3Drawable.LowerAlpha >= 1f && !_v3Drawable.UpperHasEndBar)
                                {
                                    // No lower-staff refresh here: replacing lower notes while
                                    // the player is about to play them causes a display/session
                                    // mismatch.  The session will complete and AutoRepeat will
                                    // generate a fresh set.
                                }
                            }
                            SyncV3NoteStates();
                        }
                        else
                        {
                            SyncV3NoteStates();
                        }
                    }
                    else if (_session.V2StaffMode)
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
                    else if (_session.IsRandomMode && _session.CurrentNoteIndex < _session.NotesToDraw.Count)
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
                    else if (!_session.IsRandomMode)
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

        /// <summary>
        /// Saves one <see cref="NoteAttempt"/> per note slot in the completed session,
        /// then lets <see cref="NoteAttemptDatabase.SaveAttemptAsync"/> prune old rows so
        /// each note+instrument pair keeps at most MaxAttemptsPerNote attempts.
        ///
        /// Called at session end from <see cref="UpdateNoteStatsDatabaseAsync"/>.
        /// Playback (autoplay) sessions are skipped because no real pitch was detected.
        /// </summary>
        private async Task SaveNoteAttemptsForSessionAsync()
        {
            if (_noteAttemptDb == null) return;
            // Skip autoplay sessions — no microphone input, nothing meaningful to record.
            if (_isPlaying) return;

            try
            {
                await _noteAttemptDb.InitializeAsync();

                var instrument = _session.Instrument ?? string.Empty;
                var level = _session.ChildLevel;
                var sessionId = _currentSessionId;
                // Concert key is available via GetConcertKey() (public).
                var concertKey = _session.GetConcertKey();

                for (int i = 0; i < _session.NotesToDraw.Count; i++)
                {
                    var note = _session.NotesToDraw[i];

                    // Only record notes the user actually attempted; skip unreached slots.
                    bool wasAttempted = _session.CorrectNoteIndices.Contains(i)
                                     || _session.NoteFeedbacks.ContainsKey(i);
                    if (!wasAttempted) continue;

                    bool pitchCorrect = _session.CorrectNoteIndices.Contains(i);
                    int cents = 0;
                    if (_session.NoteFeedbacks.TryGetValue(i, out var fb))
                        cents = fb.Cents;

                    // Concert pitch name: the session's written key is already the transposed
                    // (written) key; record the concert key for reference.
                    // Full per-note concert-pitch resolution would require a public transpose
                    // offset — deferred until that API is exposed.
                    string concertName = concertKey != _session.Key ? concertKey : string.Empty;

                    var attempt = new musicmate.Models.NoteAttempt
                    {
                        // DateTime.UtcNow is the session-end write time, not the per-note play time.
                        // AttemptId (auto-increment) gives reliable ordering within a session.
                        DateTime            = DateTime.UtcNow,
                        SessionId           = sessionId,
                        Instrument          = instrument,
                        Level               = level,
                        // Identity key for rolling limit: WrittenNoteName + Instrument
                        WrittenNoteName     = note.Name,
                        ConcertPitchNoteName = concertName,
                        MidiNumber          = note.Midi,
                        ExpectedDurationBeats = note.Duration.HasValue
                            ? (double?)note.Duration.Value.ToBeatValue() : null,
                        PitchErrorCents     = cents,  // measured cents offset (0 if not available)
                        TimingErrorMs       = null,       // not yet tracked per-note
                        WasPitchCorrect     = pitchCorrect,
                        WasTimingCorrect    = null,       // reserved for future timing scoring
                        WasOverallCorrect   = pitchCorrect
                    };

                    // SaveAttemptAsync inserts the row and immediately prunes old rows
                    // for the same WrittenNoteName+Instrument pair if the count exceeds
                    // MaxAttemptsPerNote (read from Preferences each time).
                    await _noteAttemptDb.SaveAttemptAsync(attempt);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NoteAttempts] SaveNoteAttemptsForSessionAsync error: {ex}");
            }
        }

        private async Task StartListeningAndEvaluatingAsync(bool playBack = false)
        {
            try
            {
                // Any new session clears the post-autoplay results freeze.
                _freezeStaff = false;

                // Assign a fresh session ID so all NoteAttempts from this run are grouped together.
                _currentSessionId = Guid.NewGuid().ToString();

                StatusService.Instance.StatusMessage = "Listening, tap red square to stop";
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
                        if (_session.StaffDisplayMode == StaffDisplayMode.V3 && _v3Drawable != null)
                        {
                            int upperPitchCount = _v3Drawable.UpperNotes.Count(n => !n.IsRest);
                            bool onUpper = i < upperPitchCount;
                            _v3Drawable.IsUpperActive = onUpper;
                            _v3Drawable.ActiveNoteIndex = onUpper ? i : i - upperPitchCount;
                            // Set states
                            var us = new V2NoteState[_v3Drawable.UpperNotes.Count];
                            int si2 = 0;
                            for (int d = 0; d < _v3Drawable.UpperNotes.Count; d++)
                            {
                                if (_v3Drawable.UpperNotes[d].IsRest) continue;
                                us[d] = si2 < i ? V2NoteState.Correct : si2 == i ? V2NoteState.Current : V2NoteState.Pending;
                                si2++;
                            }
                            _v3Drawable.UpperNoteStates = us;
                            var ls = new V2NoteState[_v3Drawable.LowerNotes.Count];
                            int li2 = 0;
                            for (int d = 0; d < _v3Drawable.LowerNotes.Count; d++)
                            {
                                if (_v3Drawable.LowerNotes[d].IsRest) continue;
                                int gi = upperPitchCount + li2;
                                ls[d] = gi < i ? V2NoteState.Correct : gi == i ? V2NoteState.Current : V2NoteState.Pending;
                                li2++;
                            }
                            _v3Drawable.LowerNoteStates = ls;
                            V3StaffGraphicsView.Invalidate();
                        }
                        else if (_session.V2StaffMode && _v2Drawable != null)
                        {
                            // Map session note index i to the drawable note index (skipping rests)
                            int drawIdx = 0, noteCount = 0;
                            for (int d = 0; d < _v2Drawable.Notes.Count; d++)
                            {
                                if (!_v2Drawable.Notes[d].IsRest)
                                {
                                    if (noteCount == i) { drawIdx = d; break; }
                                    noteCount++;
                                }
                            }
                            _v2Drawable.CurrentNoteIndex = drawIdx;
                            var states = new V2NoteState[_v2Drawable.Notes.Count];
                            int si = 0;
                            for (int d = 0; d < _v2Drawable.Notes.Count; d++)
                            {
                                if (_v2Drawable.Notes[d].IsRest) { states[d] = V2NoteState.Pending; continue; }
                                states[d] = si < i ? V2NoteState.Correct : si == i ? V2NoteState.Current : V2NoteState.Pending;
                                si++;
                            }
                            _v2Drawable.NoteStates = states;
                            V2StaffGraphicsView.Invalidate();
                        }
                        else
                        {
                            StaffGraphicsView.Invalidate();
                        }
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
                            if (_session.V2StaffMode)
                                V2StaffGraphicsView.Invalidate();
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


        /// <summary>
        /// Saves session statistics and, for child-home sessions, a SessionResult.
        /// Returns the new child level if a level-up occurred, otherwise null.
        /// </summary>
        private async Task<int?> SaveSessionStatAsync()
        {
            if (_sessionDb == null) { Utils.Log("[LevelUpDebug] _sessionDb is null"); return null; }

            // Do not record Tuner sessions
            if (_session.Tune == "Tuner") { Utils.Log("[LevelUpDebug] Tuner session, skipping"); return null; }

            // Respect the user's collection preference
            if (!Preferences.Default.Get("CollectSessionStats", true)) { Utils.Log("[LevelUpDebug] CollectSessionStats is false"); return null; }

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
                Sc = _session.IsRandomMode ? "Random"
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

            // If this session was started from ChildHomePage, save a child SessionResult
            // then check level-up criteria.
            Utils.Log($"[LevelUpDebug] _session.ChildLevel={_session.ChildLevel}, _sessionResultDb null?={_sessionResultDb == null}");
            if (_session.ChildLevel > 0)
            {
                await SaveSessionResultAsync(apc, meanBpm, stdBpm);

                if (_sessionResultDb != null)
                {
                    var shortInstrument = _session.Instrument?.Split(',')[0].Trim() ?? "";
                    Utils.Log($"[LevelUpDebug] Calling CheckAndApplyLevelUpAsync: level={_session.ChildLevel}, instrument={shortInstrument}");
                    var newLevel = await Services.LevelUpService.CheckAndApplyLevelUpAsync(
                        _sessionResultDb, _session.ChildLevel, shortInstrument);

                    if (newLevel.HasValue)
                    {
                        Utils.Log($"[LevelUpDebug] Level up! New level={newLevel.Value}");
                        _session.ChildLevel = newLevel.Value;
                    }
                    else
                    {
                        Utils.Log("[LevelUpDebug] No level up this session.");
                    }

                    return newLevel;
                }
                else
                {
                    Utils.Log("[LevelUpDebug] _sessionResultDb is null inside ChildLevel>0 block");
                }
            }

            return null;
        }

        /// <summary>
        /// Calculates and persists a <see cref="SessionResult"/> for child-home sessions.
        /// Called only when <see cref="NoteSessionService.ChildLevel"/> &gt; 0.
        ///
        /// Pitch accuracy:
        ///   • TotalNotes  = number of non-rest note slots generated.
        ///   • CorrectPitchCount = notes the player eventually sang correctly
        ///     (CorrectNoteIndices.Count from the session).
        ///   • WrongPitchCount = total incorrect attempts across all note slots
        ///     (sum of NoteFeedbacks[i].Wrong for all i).
        ///   • PitchAccuracyPercent = CorrectPitchCount / TotalNotes * 100.
        ///
        /// Average pitch error:
        ///   • Taken from NoteFeedbacks[i].Cents for indices in CorrectNoteIndices.
        ///   • Cents is the deviation reported by Evaluate() at the moment the note
        ///     was accepted — positive = sharp, negative = flat.
        ///   • We store the mean absolute value so it is always a positive "closeness" number.
        ///
        /// Timing:
        ///   • Converted from BPM statistics already computed by NoteSessionService.
        ///   • ms-per-beat = 60000 / meanBpm; stdDev in ms = 60000 * stdDevBpm / meanBpm².
        ///
        /// FUTURE (level-up criteria): after saving, query
        ///   var recent = await _sessionResultDb.GetByLevelAsync(_session.ChildLevel);
        ///   and check whether the last N sessions all exceed a target accuracy.
        /// </summary>
        private async Task SaveSessionResultAsync(double pitchAccuracyPercent,
                                                   double? meanBpm, double? stdBpm)
        {
            if (_sessionResultDb == null) return;

            try
            {
                await _sessionResultDb.InitializeAsync();

                var totalNotes = _session.NotesToDraw.Count(n => !n.IsRest);
                var (correctCount, wrongCount, _) = _session.GetSessionCorrectWrongTotals();

                // Average absolute pitch error in cents across correctly played notes,
                // excluding attempts whose cents magnitude exceeds the outlier threshold
                // (Rule 1: |PitchErrorCents| > NoteAttemptThresholds.MaxPitchErrorCentsForCorrectNote).
                // Those rows are stale or race-condition data and would inflate the average.
                double avgCents = 0;
                var correctIndices = _session.CorrectNoteIndices;
                if (correctIndices.Count > 0)
                {
                    var centsList = correctIndices
                        .Where(i => _session.NoteFeedbacks.ContainsKey(i))
                        .Select(i => Math.Abs(_session.NoteFeedbacks[i].Cents))
                        .Where(c => c <= Models.NoteAttemptThresholds.MaxPitchErrorCentsForCorrectNote)
                        .ToList();
                    if (centsList.Count > 0)
                        avgCents = centsList.Average();
                }

                // Convert BPM statistics to milliseconds.
                double avgTimingMs  = meanBpm.HasValue && meanBpm.Value > 0
                    ? 60000.0 / meanBpm.Value : 0;
                double stdTimingMs  = (meanBpm.HasValue && meanBpm.Value > 0 && stdBpm.HasValue)
                    ? 60000.0 * stdBpm.Value / (meanBpm.Value * meanBpm.Value) : 0;

                var result = new Models.SessionResult
                {
                    DateTime               = DateTime.UtcNow,
                    Instrument             = _session.Instrument?.Split(',')[0].Trim() ?? "",
                    Level                  = _session.ChildLevel,
                    TotalNotes             = totalNotes,
                    CorrectPitchCount      = (int)correctCount,
                    WrongPitchCount        = (int)wrongCount,
                    PitchAccuracyPercent   = pitchAccuracyPercent,
                    AveragePitchErrorCents = avgCents,
                    AverageTimingMs        = avgTimingMs,
                    TimingStdDevMs         = stdTimingMs,
                    // Overall: currently pitch accuracy only.
                    // FUTURE: blend with timing accuracy once timing scoring is calibrated.
                    OverallAccuracyPercent = pitchAccuracyPercent,
                };

                await _sessionResultDb.InsertAsync(result);

                Utils.Log($"[SessionResult] Saved: Level={result.Level}, " +
                          $"Correct={result.CorrectPitchCount}/{result.TotalNotes}, " +
                          $"Pitch={result.PitchAccuracyPercent:F1}%, " +
                          $"AvgCents={result.AveragePitchErrorCents:F1}, " +
                          $"AvgTimingMs={result.AverageTimingMs:F0}");
            }
            catch (Exception ex)
            {
                Utils.Log($"[SessionResult] SaveSessionResultAsync error: {ex}");
            }
        }
        private async void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_session.SelectedScale) ||
                e.PropertyName == nameof(NoteSessionService.Instrument) ||
                e.PropertyName == nameof(NoteSessionService.Key) ||
                e.PropertyName == nameof(NoteSessionService.Tune) ||
                e.PropertyName == nameof(NoteSessionService.CurrentTune))
            {
                // Saved notes are for a specific scale/key/tune — invalidate them when any of those change
                // so the next repeat generates fresh notes for the new selection rather than restoring stale ones.
                if (e.PropertyName == nameof(_session.SelectedScale) ||
                    e.PropertyName == nameof(NoteSessionService.Key) ||
                    e.PropertyName == nameof(NoteSessionService.Tune) ||
                    e.PropertyName == nameof(NoteSessionService.CurrentTune))
                {
                    _savedNotesToRepeat = null;
                }

                // Only regenerate when the page is visible; if called while navigating in from
                // ChildHomePage the session properties are being batch-set and OnAppearing will
                // trigger the first regeneration once the page is actually on screen.
                if (_isPageVisible)
                {
                    await RegenerateNotesAsync();
                    UpdateTunerVisibility();
                    UpdateKeyPickerVisibility();
                }
            }

            if (e.PropertyName == nameof(_session.SelectedScale) ||
                e.PropertyName == nameof(NoteSessionService.Key) ||
                e.PropertyName == nameof(NoteSessionService.Instrument) ||
                e.PropertyName == nameof(NoteSessionService.Tune) ||
                e.PropertyName == nameof(NoteSessionService.CurrentTune))
            {
                UpdateConcertKeyLabel();
                UpdateScaleTunePicker();
            }

            if (e.PropertyName == nameof(NoteSessionService.Instrument))
                UpdateInstrumentPickerSelection();

            if (e.PropertyName == nameof(NoteSessionService.Key))
                UpdateKeyPickerSelection();

            if (e.PropertyName == nameof(NoteSessionService.StaffDisplayMode))
            {
                UpdatePickersContainerVisibility();
                UpdateRepeatButtonsVisibility();
                await RegenerateNotesAsync();
            }
        }

        private void UpdateInstrumentPickerSelection()
        {
            if (InstrumentPicker.ItemsSource is not string[] items) return;
            var idx = Array.IndexOf(items, _session.Instrument);
            if (idx >= 0 && InstrumentPicker.SelectedIndex != idx)
                InstrumentPicker.SelectedIndex = idx;
            if (idx >= 0 && _v3HomeInstrumentPicker?.SelectedIndex != idx)
                _v3HomeInstrumentPicker!.SelectedIndex = idx;
            SelectedInstrumentShort = _session.Instrument?.Split(',')[0].Trim() ?? string.Empty;
        }

        private void UpdateKeyPickerSelection()
        {
            if (KeyPicker.ItemsSource is not string[] items) return;
            var idx = Array.IndexOf(items, _session.Key);
            if (idx >= 0 && KeyPicker.SelectedIndex != idx)
                KeyPicker.SelectedIndex = idx;
            if (idx >= 0 && _v3HomeKeyPicker?.SelectedIndex != idx)
                _v3HomeKeyPicker!.SelectedIndex = idx;
        }

        private void UpdateConcertKeyLabel()
        {
            var text = $"(Concert {_session.GetConcertKey()})";
            ConcertKeyLabel.Text = text;
            if (_v3HomeConcertKeyLabel != null) _v3HomeConcertKeyLabel.Text = text;
        }

        private void UpdateKeyPickerVisibility()
        {
            var hide = _session.Tune == "Tuner";
            KeyPicker.IsVisible = !hide;
            KeyLabel.IsVisible = !hide;
            KeyBorder.IsVisible = !hide;
            ConcertKeyLabel.IsVisible = !hide;
        }

        private void UpdatePickersContainerVisibility()
        {
            // In V3 mode the pickers live on the What to Play page
            PickersContainer.IsVisible = _session.StaffDisplayMode != StaffDisplayMode.V3;
        }


        private void UpdateTunerVisibility()
        {
            var isTuner = _session.Tune == "Tuner";
            var isV3    = _session.StaffDisplayMode == StaffDisplayMode.V3;
            var isV2Tuner = isTuner && _session.V2StaffMode;

            if (isTuner)
            {
                // In V2 Tuner mode show the large V2 staff panel; otherwise hide all staff panels.
                StaffBorder.IsVisible   = false;
                V2StaffBorder.IsVisible = isV2Tuner;
                V2ModeBanner.IsVisible  = false;
                V3StaffBorder.IsVisible = false;
            }
            else if (isV3)
            {
                StaffBorder.IsVisible   = false;
                V2StaffBorder.IsVisible = false;
                V2ModeBanner.IsVisible  = false;
                V3StaffBorder.IsVisible = true;
            }
            else if (_session.V2StaffMode)
            {
                StaffBorder.IsVisible  = false;
                V2StaffBorder.IsVisible = true;
                V2ModeBanner.IsVisible = true;
                // Clear any width constraint left over from V2 Tuner mode.
                V2StaffBorder.WidthRequest      = -1;
                V2StaffBorder.HorizontalOptions = LayoutOptions.Fill;
            }
            else
            {
                StaffBorder.IsVisible  = true;
                V2StaffBorder.IsVisible = false;
                V2ModeBanner.IsVisible = false;
                // Clear any width constraint left over from V2 Tuner mode.
                V2StaffBorder.WidthRequest      = -1;
                V2StaffBorder.HorizontalOptions = LayoutOptions.Fill;
            }

            TunerGrid.IsVisible = isTuner;

            if (isTuner)
            {
                // In V2 Tuner mode the large staff is shown in V2StaffBorder; hide the duplicate
                // staff canvas inside TunerGrid and collapse its column so only the info panel shows.
                TunerBorder.IsVisible = !isV2Tuner;
                if (isV2Tuner)
                {
                    // Constrain V2StaffBorder to a narrow fixed width so it ends just past the staff
                    // lines, leaving the TunerGrid text column clearly separated to its right.
                    const double staffPanelWidth = 220;
                    V2StaffBorder.WidthRequest   = staffPanelWidth;
                    V2StaffBorder.HorizontalOptions = LayoutOptions.Start;
                    TunerGrid.ColumnDefinitions[0] = new ColumnDefinition(staffPanelWidth);
                }
                else
                {
                    V2StaffBorder.WidthRequest      = -1;
                    V2StaffBorder.HorizontalOptions = LayoutOptions.Fill;
                    TunerGrid.ColumnDefinitions[0] = new ColumnDefinition(GridLength.Star);
                }

                // V2 Tuner: use the StaffDrawable (which renders the tuner note) on the V2 panel.
                if (isV2Tuner && _v2Drawable != null)
                    V2StaffGraphicsView.Drawable = _drawable;

                _session.SessionCompleted = false;
                if (isV2Tuner)
                    V2StaffGraphicsView.Invalidate();
                else
                    TunerGraphicsView.Invalidate();
                if (!_isRunning)
                {
                    _ = StartListeningAndEvaluatingAsync();
                }
            }
            else if (_v2Drawable != null && V2StaffGraphicsView.Drawable != _v2Drawable)
            {
                // Restore V2 drawable when leaving Tuner mode.
                V2StaffGraphicsView.Drawable = _v2Drawable;
            }
        }
        private void UpdateScaleTunePicker()
        {
            if (ScaleTunePicker.ItemsSource is not string[] items) return;
            var selection = _session.Tune == "Tuner" ? "Tuner"
                : _session.Tune == "Practice Tune" ? (_session.CurrentTune?.Title ?? string.Empty)
                : _session.SelectedScale;
            var idx = Array.IndexOf(items, selection);
            _suppressPickerSync = true;
            try
            {
                if (idx >= 0 && ScaleTunePicker.SelectedIndex != idx)
                    ScaleTunePicker.SelectedIndex = idx;
                // Keep V3 home picker in sync — populate ItemsSource on first call if needed
                if (_v3HomeScaleTunePicker != null)
                {
                    if (_v3HomeScaleTunePicker.ItemsSource == null)
                        _v3HomeScaleTunePicker.ItemsSource = items;
                    if (_v3HomeScaleTunePicker.ItemsSource is string[] v3Items)
                    {
                        var v3Idx = Array.IndexOf(v3Items, selection);
                        if (v3Idx >= 0 && _v3HomeScaleTunePicker.SelectedIndex != v3Idx)
                            _v3HomeScaleTunePicker.SelectedIndex = v3Idx;
                    }
                }
            }
            finally
            {
                _suppressPickerSync = false;
            }
        }

        private void V3HomeInstrumentPicker_SelectedIndexChanged(object? sender, EventArgs e)
        {
            var idx = _v3HomeInstrumentPicker.SelectedIndex;
            if (idx < 0) return;
            var fullInstrument = NoteSessionService.InstrumentOptions[idx];
            _session.Instrument = fullInstrument;
            if (InstrumentPicker.SelectedIndex != idx)
                InstrumentPicker.SelectedIndex = idx;
            SelectedInstrumentShort = fullInstrument.Split(',')[0].Trim();
        }

        private async void V3HomeKeyPicker_SelectedIndexChanged(object? sender, EventArgs e)
        {
            var selectedKey = _v3HomeKeyPicker.SelectedItem?.ToString();
            if (selectedKey == null) return;
            var shortKey = selectedKey.Split(',')[0].Trim();
            if (IsPremiumKey(shortKey) && !StatusService.Instance.IsPremiumUser)
            {
                var purchased = await PremiumPromptHelper.ShowAsync(this,
                    onDecline: () => _v3HomeKeyPicker.SelectedIndex = _lastFreeKeyIndex);
                if (!purchased) return;
            }
            else { _lastFreeKeyIndex = _v3HomeKeyPicker.SelectedIndex; }
            _session.Key = shortKey;
            if (KeyPicker.SelectedIndex != _v3HomeKeyPicker.SelectedIndex)
                KeyPicker.SelectedIndex = _v3HomeKeyPicker.SelectedIndex;
            UpdateConcertKeyLabel();
        }

        private void V3HomeScaleTunePickerChanged(object? sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[PickerDBG] V3HomeScaleTunePickerChanged fired. suppress={_suppressPickerSync} idx={_v3HomeScaleTunePicker.SelectedIndex} item='{_v3HomeScaleTunePicker.SelectedItem}'");
            if (_suppressPickerSync) return;
            var v3Items = _v3HomeScaleTunePicker.ItemsSource as string[];
            var v3Idx = _v3HomeScaleTunePicker.SelectedIndex;
            System.Diagnostics.Debug.WriteLine($"[PickerDBG] v3Items null={v3Items==null} v3Idx={v3Idx} len={v3Items?.Length}");
            if (v3Items == null || v3Idx < 0 || v3Idx >= v3Items.Length) return;
            // Sync the hidden ScaleTunePicker index silently (it is not visible in V3 mode,
            // so its SelectedIndexChanged event is unreliable — always handle directly here).
            _suppressPickerSync = true;
            try { ScaleTunePicker.SelectedIndex = v3Idx; }
            finally { _suppressPickerSync = false; }
            // Always delegate to the main handler, passing _v3HomeScaleTunePicker as sender
            // so the correct item is read by index from the visible picker.
            OnScaleTunePickerChanged(_v3HomeScaleTunePicker, e);
        }

        private async void OnScaleTunePickerChanged(object? sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"[PickerDBG] OnScaleTunePickerChanged fired. suppress={_suppressPickerSync} sender={sender?.GetType().Name}");
            if (_suppressPickerSync) return;
            var sourcePicker = (sender as Picker) ?? ScaleTunePicker;
            var items = sourcePicker.ItemsSource as string[];
            var idx = sourcePicker.SelectedIndex;
            System.Diagnostics.Debug.WriteLine($"[PickerDBG] sourcePicker={sourcePicker.GetType().Name} idx={idx} items null={items==null} len={items?.Length}");
            if (items == null || idx < 0 || idx >= items.Length) return;
            var selected = items[idx];
            System.Diagnostics.Debug.WriteLine($"[PickerDBG] selected='{selected}'");

            // Check if the selection is a practice tune title
            var practiceTune = musicmate.Models.TuneLibrary.All.FirstOrDefault(t => t.Title == selected);
            System.Diagnostics.Debug.WriteLine($"[PickerDBG] practiceTune={practiceTune?.Title ?? "null"} TuneLibrary.All count={musicmate.Models.TuneLibrary.All.Count}");
            if (practiceTune != null)
            {
                _lastValidScaleTuneIndex = sourcePicker.SelectedIndex;
                _session.SelectPracticeTune(practiceTune);
                Preferences.Default.Set("SelectedTune", selected);
                IsAutoRepeatVisible = true;
                UpdateKeyPickerVisibility();
                await RegenerateNotesAsync();
                return;
            }

            if (selected == "Tuner")
            {
                _lastValidScaleTuneIndex = sourcePicker.SelectedIndex;
                _session.Tune = selected;
                Preferences.Default.Set("SelectedTune", selected);
                IsAutoRepeatVisible = false;
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

            _lastValidScaleTuneIndex = sourcePicker.SelectedIndex;
            _session.Tune = "Selected Scale";
            _session.SelectedScale = selected;
            Preferences.Default.Set("SelectedTune", "Selected Scale");
            IsAutoRepeatVisible = true;
            UpdateKeyPickerVisibility();
            await RegenerateNotesAsync();
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

