using CommunityToolkit.Maui.Alerts;
using Microsoft.Maui.Controls.Shapes;
using musicmate.Controls;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;
using musicmate.Diagnostics;
using musicmate.V3LayoutDebug;
using musicmate.Utilities;
using Microsoft.Maui.Graphics;
using SkiaSharp;
using System.ComponentModel;
using System.Diagnostics;
  //  2026.06.12 1516  Just so I can do a commit before using the long prompt for sustained notes and rests.
namespace musicmate.Pages
{
    public partial class MainPage : ContentPage
    {
       
        private readonly NoteSessionService _session = null!;
        private readonly IAudioCaptureService _audio = null!;
        private readonly IAudioPlaybackService _player = null!;
        private readonly Drawables.StaffDrawable _drawable = null!;
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
        private Border _v3StartStopButton = null!;
        private Border _v3PlayButton = null!;
        private Grid _titleMarqueeGrid = null!;

        // fields for inactivity tracking
        private DateTime _lastHeardTime = DateTime.UtcNow;
#pragma warning disable CS0414
        private bool _inactivityStopped = false;
        private readonly TimeSpan _inactivityTimeout = TimeSpan.FromMinutes(5);

        // When true, RegenerateNotesAsync is suppressed so the post-autoplay
        // green feedbacks and session stats remain visible until the next session.
        private bool _freezeStaff = false;

        /// <summary>Session completion was triggered after Play playback — skip result banner.</summary>
        private bool _completionFromPlayback;

        // When true, the session result banner is being shown after a child-home
        // session completed with AutoRepeat off.  Blocks auto-start until the user
        // leaves the page (e.g. opens Settings) or taps Start/Stop.
        private bool _holdResultForChildSession = false;
        private string? _sessionEndMarqueeMessage;
        private bool _deferNewLevelMarqueeUntilBannerDismissed;
        private string? _pendingInstrumentForMarquee;
        private CancellationTokenSource? _autoStartCts;
        private CancellationTokenSource? _sessionStartCts;
        private bool _suppressSessionRegenerate;
        private const string ChildLevelPrefKey = "ChildHome.Level";
#if DEBUG //  2026.06.18 0935 TEMP BLOCK
        private static readonly bool UseArpeggioPreview = false;//  2026.06.18 0935 
        private const string ArpeggioPreviewRootNote = "Bb3"; //  2026.06.18 0935 
#endif //  2026.06.18 0935 
        private readonly Dictionary<string, ArpeggioPickerChoice> _arpeggioPickerChoices = new(StringComparer.Ordinal);
#pragma warning restore CS0414

        private sealed record ArpeggioPickerChoice(
            string Label,
            ArpeggioPattern Pattern,
            string RootNote);

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
            UpdatePlayButtonVisibility();
            Dispatcher.Dispatch(UpdateV3PlayButtonPosition);
        }

        public bool IsNotV3Mode => _session?.StaffDisplayMode != StaffDisplayMode.V3;
        public bool IsV3Mode => _session?.StaffDisplayMode == StaffDisplayMode.V3;
        public bool IsV3PlayButtonVisible => IsV3Mode && (!_isRunning || _isPlaying);
        public bool IsV3BottomPickersVisible => IsV3Mode && _session?.Tune != "Tuner";
        public bool IsBottomButtonRowVisible => IsNotV3Mode || _session?.Tune != "Tuner";
        public bool IsPlayEvaluateButtonVisible => IsNotV3Mode && (!_isRunning || _isPlaying);
        public bool IsChildLevelSliderVisible => _session?.ChildLevel > 0 && _session.Tune != "Tuner";
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
                _v3StartStopButton      = this.FindByName<Border>("V3StartStopButton")!;
                _v3PlayButton           = this.FindByName<Border>("V3PlayButton")!;
                _titleMarqueeGrid       = this.FindByName<Grid>("TitleMarqueeGrid")!;
                UpdateV3StartStopButtonVisual(false);

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

                // Force V3 mode at all levels when starting from Home page
                _session.StaffDisplayMode = StaffDisplayMode.V3;

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

                // V3 staff drawable setup
                var safeAreaService = ServiceHelper.GetService<ISafeAreaService>();
                _v3Drawable = new Drawables.V3StaffDrawable(_session, _theme_service!, safeAreaService);
                V3StaffGraphicsView.Drawable = _v3Drawable;
                V3StaffGraphicsView.SetBinding(GraphicsView.BackgroundColorProperty, new Binding("PanelBackgroundColor"));
                _v3StartStopButton.SizeChanged += (_, _) => UpdateV3PlayButtonPosition();
                _v3StartStopButton.HandlerChanged += (_, _) => UpdateV3PlayButtonPosition();
                _titleMarqueeGrid.SizeChanged += (_, _) => UpdateV3PlayButtonPosition();
                _titleMarqueeGrid.HandlerChanged += (_, _) => UpdateV3PlayButtonPosition();
                V3StaffBorder.SizeChanged += (_, _) => UpdateV3PlayButtonPosition();
                SizeChanged += (_, _) => UpdateV3PlayButtonPosition();
                SetPlayButtonPlaying(false);

                // Tuner graphics setup
                TunerBorder.BindingContext = _theme_service;
                TunerInfoBorder.SetBinding(Border.BackgroundColorProperty,
                    new Binding("PanelBackgroundColor", source: _theme_service));
                TunerGraphicsView.BindingContext = _theme_service;
                TunerGraphicsView.Drawable = _drawable;
                TunerGraphicsView.SetBinding(GraphicsView.BackgroundColorProperty, new Binding("PanelBackgroundColor"));
                MainPageRootGrid.SizeChanged += (_, _) =>
                {
                    if (_session?.Tune == "Tuner")
                        ApplyTunerHeight();
                };

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
                        // V3 mid-session staff refreshes are handled by SyncV3NoteStates; a
                        // SessionCompletedAsync callback means every note was played, so always
                        // run the summary / AutoRepeat restart flow below.

                        await UpdateNoteStatsDatabaseAsync();

                        string shortInstrumentForMarquee = _session.Instrument?.Split(',')[0].Trim() ?? "";
                        int levelBeforeSave = _session.ChildLevel;
                        var countSinceBeforeSave = Services.LevelUpService.CountSinceUtc;

                        // Capture display stats before SaveSessionStatAsync — a level-up refreshes
                        // the staff and clears NoteFeedbacks / CorrectNoteIndices.
                        var (correct, wrong, apc) = _session.GetSessionCorrectWrongTotals();
                        var detectedBpm = _session.GetDetectedBpm();

                        // Rolling per-note attempt history runs unconditionally,
                        // independent of the CollectNoteStats preference.
                        // Save session summary before attempt rows are cleared.
                        int? newChildLevel = await SaveSessionStatAsync();
                        await SaveNoteAttemptsForSessionAsync();

                        if (_completionFromPlayback)
                        {
                            _completionFromPlayback = false;
                            _session.SessionCompleted = true;
                            try { _audio.StopCapture(); } catch { }
                            SetButtonStates(false);
                            _holdResultForChildSession = false;
                            return;
                        }

                        // Show result banner at the top of the page.
                        // Append a level-up notice when the child has just advanced.
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            var bpmText = detectedBpm.HasValue ? $"  ·  Detected {detectedBpm.Value} BPM" : string.Empty;
                            var levelUpText = newChildLevel.HasValue
                                ? $"  🎉 Great job! You advanced to Level {newChildLevel.Value}!"
                                : string.Empty;
                            SessionResultLabel.Text =
                                $"✓ {apc:F0}% correct  ({(int)correct}/{(int)(correct + wrong)}){bpmText}{levelUpText}";
                            SessionResultBanner.IsVisible = true;
                        });
                        // Level-up progress: qualifying sessions at current level since start / last level-up
                        try
                        {
                            if (newChildLevel.HasValue)
                            {
                                // Keep the completed-level marquee while the congratulatory banner is visible.
                                _deferNewLevelMarqueeUntilBannerDismissed = true;
                                _pendingInstrumentForMarquee = shortInstrumentForMarquee;
                                await BuildAndPublishSessionEndMarqueeAsync(
                                    levelBeforeSave, shortInstrumentForMarquee, countSinceBeforeSave);
                            }
                            else
                            {
                                await BuildAndPublishSessionEndMarqueeAsync(
                                    _session.ChildLevel, shortInstrumentForMarquee, Services.LevelUpService.CountSinceUtc);
                            }
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
                            _holdResultForChildSession = false;
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

                // Combined Scale + Tune picker: Tuner / individual practice tunes / arpeggios / scales
                var practiceTuneTitles = musicmate.Models.TuneLibrary.All.Select(t => t.Title).ToArray();
                var scaleTuneOptions = BuildScaleTuneOptions();
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
                else if (!string.IsNullOrEmpty(savedTune) && _arpeggioPickerChoices.TryGetValue(savedTune, out var savedArpeggio))
                {
                    _session.SelectArpeggio(savedArpeggio.Pattern, savedArpeggio.RootNote, savedArpeggio.Label);
                }
                // else _session.Tune stays "Selected Scale" (persisted via SelectedTune preference)

                var initialScaleTuneSelection = _session.Tune == "Tuner" ? "Tuner"
                    : _session.Tune == "Practice Tune" ? (_session.CurrentTune?.Title ?? practiceTuneTitles[0])
                    : _session.Tune == "Arpeggio" ? _session.SelectedArpeggioDisplay
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

        private void ScheduleAutoStartOnAppear()
        {
            if (!_session.AutoStart || _session.Tune == "Tuner")
                return;

            _autoStartCts?.Cancel();
            _autoStartCts = new CancellationTokenSource();
            var cts = _autoStartCts;
            _ = RunAutoStartOnAppearAsync(cts.Token);
        }

        private async Task RunAutoStartOnAppearAsync(CancellationToken ct)
        {
            try
            {
                if (_holdResultForChildSession || _isRunning)
                    return;

                var sw = System.Diagnostics.Stopwatch.StartNew();
                if (_session.StaffDisplayMode == StaffDisplayMode.V3)
                {
                    while (V3StaffGraphicsView != null && V3StaffGraphicsView.Width <= 0
                           && sw.ElapsedMilliseconds < 1500)
                    {
                        ct.ThrowIfCancellationRequested();
                        await Task.Delay(40, ct);
                    }
                }
                else
                {
                    while (StaffGraphicsView != null && StaffGraphicsView.Width <= 0
                           && sw.ElapsedMilliseconds < 1000)
                    {
                        ct.ThrowIfCancellationRequested();
                        await Task.Delay(40, ct);
                    }
                }

                await Task.Delay(150, ct);
                ct.ThrowIfCancellationRequested();

                if (!_session.AutoStart || _session.Tune == "Tuner"
                    || _holdResultForChildSession || _isRunning)
                    return;

                await StartListeningAndEvaluatingAsync();
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer navigation or page hide.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AutoStart] ERROR: {ex}");
            }
        }

        private async Task RegenerateNotesAsync()
        {
            // Wait for any in-flight regeneration — never skip after session Reset() cleared notes.
            await _regenerateSemaphore.WaitAsync();
            try
            {
            // Hide any previous session result banner when new notes are generated.
            await HideSessionResultBannerAsync(refreshMarqueeForNewLevel: true);
            _holdResultForChildSession = false;

            // While showing post-autoplay results, do not overwrite the staff.
            if (_freezeStaff)
                return;

            _v3GenerationSeed = unchecked(_v3GenerationSeed + 1);

            float width = 360f;
            if (_session.StaffDisplayMode == StaffDisplayMode.V3 && V3StaffGraphicsView?.Width > 0)
                width = (float)V3StaffGraphicsView.Width;
            else if (StaffGraphicsView.Width > 0)
                width = (float)StaffGraphicsView.Width;
            await _session.GenerateNotesAsync(width);

#if DEBUG
            if (_session.IsRandomMode)
            {
                var names = string.Join(", ", _session.NotesToDraw.Select(n => n.Name));
                Debug.WriteLine($"[Random] Generated {_session.NotesToDraw.Count} notes: {names}");
            }
#endif

            UpdateTunerVisibility();

            if (_session.Tune != "Tuner" && _session.StaffDisplayMode == StaffDisplayMode.V3)
            {
                // Show V3 border first so the GraphicsView gets a layout width before we draw.
                StaffBorder.IsVisible   = false;
                V3StaffBorder.IsVisible = true;

                // Wait up to 500 ms for the view to get a measured width.
                var sw2 = System.Diagnostics.Stopwatch.StartNew();
                while (V3StaffGraphicsView.Width <= 0 && sw2.ElapsedMilliseconds < 500)
                    await Task.Delay(20);

                await UpdateV3DisplayAsync();
            }
            else if (_session.Tune != "Tuner")
            {
                StaffBorder.IsVisible = true;
                V3StaffBorder.IsVisible = false;
                StaffGraphicsView.Invalidate();
                UpdateStaffHeight();
            }
            }
            finally
            {
                _regenerateSemaphore.Release();
            }
        }

        // ── V3 sequence generation ────────────────────────────────────────────────

        /// <summary>How many measures to generate at once (initial fill and each top-up).</summary>
        private const int V3DefaultMeasureBatchSize = 8;

        private int GetV3MeasureBatchSize()
            => _session.ChildLevel > 0 && _session.ChildMeasureBatchSize > 0
                ? _session.ChildMeasureBatchSize
                : V3DefaultMeasureBatchSize;

        /// <summary>
        /// Level-aware measure counts for each V3 staff.  Child levels start with a
        /// single upper-staff measure and grow toward <see cref="V3MeasuresPerStaff"/>.
        /// </summary>
        private (int upper, int lower) GetV3StaffMeasureCounts()
        {
            if (_session.ChildLevel <= 0)
                return (V3MeasuresPerStaff, V3MeasuresPerStaff);

            int level = _session.ChildLevel;

            if (level <= 5)
                return (1, 0);

            if (level <= 15)
            {
                int batch = GetV3MeasureBatchSize();
                int upper = Math.Max(1, (batch + 1) / 2);
                return (upper, Math.Max(0, batch - upper));
            }

            if (level <= 20)
                return (2, 2);

            if (level <= 25)
                return (3, 3);

            if (level <= 30)
                return (3, 3);

            // Level 31+: eight bars total (four per staff).
            return (V3MeasuresPerStaff, V3MeasuresPerStaff);
        }

        // Generator offsets: updated every time we append more measures.
        private int    _v3SeqNextMeasureIndex    = 0;
        private double _v3SeqNextBeatOffset      = 0.0;
        private int    _v3SeqNextGlobalNoteIndex = 0;

        /// <summary>Cached excluded MIDI set rebuilt whenever a new sequence starts.</summary>
        private HashSet<int> _v3ExcludedMidis = new();

        /// <summary>
        /// Loads the set of mastered MIDI numbers from the note database using the
        /// same logic as <see cref="NoteSessionService.BuildRandomSequenceAsync"/>.
        /// </summary>
        private async Task LoadV3ExcludedMidisAsync()
        {
            _v3ExcludedMidis = _session.IsRandomMode
                ? await _session.GetMasteredMidiNumbersAsync()
                : new HashSet<int>();
        }

        /// <summary>
        /// Builds a <see cref="MusicSequenceGenerator"/> configured with the current
        /// session parameters, append offsets, and mastery exclusions.
        /// </summary>
        private MusicSequenceGenerator BuildV3SequenceGenerator(int measureCount)
        {
            // Translate persisted string settings to model types.
            var timeSig = _session.V3TimeSignature switch
            {
                "3/4" => TimeSignature.ThreeFour,
                "2/4" => TimeSignature.TwoFour,
                _     => TimeSignature.FourFour
            };

            // Selected-scale practice (Major, etc. from What to Play) is a straight
            // quarter-note scale walk — no rests, halves, or mixed rhythm.
            bool simpleSelectedScale = _session.Tune == "Selected Scale" && !_session.IsRandomMode;

            int rhythmVariety = simpleSelectedScale
                ? 0
                : _session.V3RhythmVarietyPercent >= 0
                    ? _session.V3RhythmVarietyPercent
                    : _session.V3RhythmMode == "Mixed" ? 60 : 0;

            var gen = new MusicSequenceGenerator
            {
                Key                  = _session.Key,
                Scale                = _session.SelectedScale,
                LowestNote           = _session.LowestNote,
                HighestNote          = _session.HighestNote,
                TimeSignature        = timeSig,
                MeasureCount         = measureCount,
                RhythmVarietyPercent = rhythmVariety,
                SmallestDuration     = simpleSelectedScale
                    ? NoteDuration.Quarter
                    : _session.V3SmallestNote switch
                {
                    "Sixteenth" => NoteDuration.Sixteenth,
                    "Eighth"    => NoteDuration.Eighth,
                    _           => NoteDuration.Quarter
                },
                StartMeasureIndex    = _v3SeqNextMeasureIndex,
                StartBeatOffset      = _v3SeqNextBeatOffset,
                StartGlobalNoteIndex = _v3SeqNextGlobalNoteIndex,
                ExcludedMidiNumbers  = _v3ExcludedMidis,
                UseScaleOrder        = !_session.IsRandomMode,
                ScaleWalkOffset      = _v3SeqNextGlobalNoteIndex,
                AccidentalPercent    = _session.IsRandomMode ? _session.AccidentalPercent : 0,
                MaxMelodicIntervalSemitones = _session.IsRandomMode ? _session.MaxMelodicIntervalSemitones : 0,
                SyncopationLevel         = simpleSelectedScale
                    ? SyncopationLevel.None
                    : SyncopationLevelHelper.Parse(_session.V3Syncopation),
                RestChancePercent        = simpleSelectedScale ? 0 : _session.V3RestChancePercent,
                RandomSeed               = Environment.TickCount
                                           ^ _v3GenerationSeed
                                           ^ (_session.ChildLevel * 7919)
            };
            Debug.WriteLine($"[V3Gen] Tune={_session.Tune} Random={_session.IsRandomMode} SimpleScale={simpleSelectedScale} AccPct={_session.AccidentalPercent} EffectiveAccPct={(_session.IsRandomMode ? _session.AccidentalPercent : 0)}");
            return gen;
        }

        /// <summary>
        /// Converts a <see cref="PracticeTune"/> into a flat list of <see cref="GeneratedNote"/>
        /// with correct <see cref="GeneratedNote.BeatPosition"/>, <see cref="GeneratedNote.MeasureIndex"/>,
        /// and accidentals parsed from each note's spelled name.
        /// </summary>
        private static List<GeneratedNote> BuildV3NotesFromTune(PracticeTune tune, string key = "C", string scale = "Major")
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
                        var adjustedMidi = NoteSessionService.ApplyKeySignatureToMidi(mn.SpelledName, mn.MidiNumber, key, scale);
                        var (resolvedAcc, displayName) = NoteSessionService.ResolveAccidentalAndSpelling(
                            mn.SpelledName, adjustedMidi, letter, octave, key, scale);
                        acc = resolvedAcc;

                        gn = new GeneratedNote
                        {
                            MidiNumber      = adjustedMidi,
                            Letter          = letter,
                            Octave          = octave,
                            Accidental      = acc,
                            SpelledName     = displayName,
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

        // ── V3 two-staff display ──────────────────────────────────────────────────

        /// <summary>How many measures to put on each V3 staff (non-child / high levels).</summary>
        private const int V3MeasuresPerStaff = 4;

        /// <summary>Bumped on each regeneration so child random tunes differ every time.</summary>
        private int _v3GenerationSeed;

        // Offsets for appending the lower staff content.
        private int    _v3LowerMeasureIndex    = 0;
        private double _v3LowerBeatOffset      = 0.0;
        private int    _v3LowerGlobalNoteIndex = 0;

        /// <summary>
        /// Pitched-note count on the upper staff when <see cref="NoteSessionService.NotesToDraw"/>
        /// was built.  The upper drawable may be replaced mid-session (lookahead refresh) with
        /// a different note count; session indices must stay tied to this value.
        /// </summary>
        private int _v3SessionUpperPitchCount = 0;

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
                _v3SeqNextMeasureIndex    = 0;
                _v3SeqNextBeatOffset      = 0.0;
                _v3SeqNextGlobalNoteIndex = 0;
                _v3SessionUpperPitchCount = 0;

                List<GeneratedNote> upperFlat;
                List<double>        upperBarBeats;
                List<GeneratedNote> lowerFlat;
                List<double>        lowerBarBeats;
                var existingUpper = new HashSet<double>();
                var existingLower = new HashSet<double>();

                if (V3LayoutTestTune.IsEnabled)
                {
                    var testTune = V3LayoutTestTune.Create();
                    V3LayoutTestTune.LogContents(testTune);

                    var allNotes = BuildV3NotesFromTune(testTune, _session.Key, _session.SelectedScale);
                    int splitAt = testTune.Measures.Count / 2;
                    double splitBeat = 0.0;
                    for (int m = 0; m < splitAt && m < testTune.Measures.Count; m++)
                        foreach (var mn in testTune.Measures[m].Notes)
                            splitBeat += mn.Duration.ToBeatValue();

                    upperFlat     = allNotes.Where(n => (n.BeatPosition ?? 0) < splitBeat).ToList();
                    lowerFlat     = allNotes.Where(n => (n.BeatPosition ?? 0) >= splitBeat).ToList();
                    upperBarBeats = ComputeNewBarBeats(upperFlat, existingUpper);
                    lowerBarBeats = ComputeNewBarBeats(lowerFlat, existingLower);

                    _v3SeqNextMeasureIndex    = testTune.Measures.Count;
                    _v3SeqNextBeatOffset      = allNotes.Sum(n => n.BeatDuration);
                    _v3SeqNextGlobalNoteIndex = allNotes.Count(n => !n.IsRest);
                    _v3LowerMeasureIndex    = _v3SeqNextMeasureIndex;
                    _v3LowerBeatOffset      = _v3SeqNextBeatOffset;
                    _v3LowerGlobalNoteIndex = _v3SeqNextGlobalNoteIndex;
                    if (_v3Drawable != null) _v3Drawable.UpperHasEndBar = false;

                    StatusService.Instance.StatusMessage =
                        "V3 fixed test tune (see debug log for bar beats)";
                }
                else if (_session.Tune == "Practice Tune" && _session.CurrentTune != null)
                {
                    // Split tune measures between upper and lower staff.
                    var allNotes = BuildV3NotesFromTune(_session.CurrentTune, _session.Key, _session.SelectedScale);
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

                    _v3SeqNextMeasureIndex    = allMeasures;
                    _v3SeqNextBeatOffset      = allNotes.Sum(n => n.BeatDuration);
                    _v3SeqNextGlobalNoteIndex = allNotes.Count(n => !n.IsRest);

                    _v3LowerMeasureIndex    = _v3SeqNextMeasureIndex;
                    _v3LowerBeatOffset      = _v3SeqNextBeatOffset;
                    _v3LowerGlobalNoteIndex = _v3SeqNextGlobalNoteIndex;
                    if (_v3Drawable != null) _v3Drawable.UpperHasEndBar = false;
                }
                else if (_session.Tune == "Arpeggio")
                {
                    var pattern = ArpeggioCatalog.All.FirstOrDefault(p => p.Id == _session.SelectedArpeggioId)
                        ?? ArpeggioCatalog.MajorTriad;
                    var allNotes = await _session.LoadArpeggioAsync(pattern, _session.SelectedArpeggioRoot);

                    upperFlat = allNotes;
                    lowerFlat = new List<GeneratedNote>();
                    upperBarBeats = ComputeNewBarBeats(upperFlat, existingUpper);
                    lowerBarBeats = new List<double>();

                    _v3SeqNextMeasureIndex = 0;
                    _v3SeqNextBeatOffset = allNotes.Sum(n => n.BeatDuration);
                    _v3SeqNextGlobalNoteIndex = allNotes.Count(n => !n.IsRest);
                    _v3LowerMeasureIndex = _v3SeqNextMeasureIndex;
                    _v3LowerBeatOffset = _v3SeqNextBeatOffset;
                    _v3LowerGlobalNoteIndex = _v3SeqNextGlobalNoteIndex;
                    if (_v3Drawable != null) _v3Drawable.UpperHasEndBar = false;
                }
                else
                {
                    await LoadV3ExcludedMidisAsync();

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
                        var timeSig = _session.V3TimeSignature switch
                        {
                            "3/4" => TimeSignature.ThreeFour,
                            "2/4" => TimeSignature.TwoFour,
                            _     => TimeSignature.FourFour
                        };
                        // A safe upper bound: even a chromatic 3-octave range (37 pitches) needs
                        // at most (2*37−2)=72 quarter notes = 18 bars of 4/4.  Cap at 24 to be safe.
                        var genAll = BuildV3SequenceGenerator(24);
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

                        _v3SeqNextMeasureIndex    = allMeasures.Count;
                        _v3SeqNextBeatOffset      = upperBeats + lowerBeats;
                        _v3SeqNextGlobalNoteIndex = upperPitches + lowerPitches;
                        _v3LowerMeasureIndex    = _v3SeqNextMeasureIndex;
                        _v3LowerBeatOffset      = _v3SeqNextBeatOffset;
                        _v3LowerGlobalNoteIndex = _v3SeqNextGlobalNoteIndex;
                    }
                    else
                    {
                        // Standard path: level-sized measure blocks per staff.
                        var (upperMc, lowerMc) = GetV3StaffMeasureCounts();
                        var genUpper = BuildV3SequenceGenerator(upperMc);
                        var upperMeasures = genUpper.GenerateSequence();
                        upperFlat     = MusicSequenceGenerator.Flatten(upperMeasures);
                        upperBarBeats = ComputeNewBarBeats(upperFlat, existingUpper);

                        _v3SeqNextMeasureIndex    += upperMeasures.Count;
                        _v3SeqNextBeatOffset      += upperMeasures.Count * (double)genUpper.TimeSignature.TotalBeats;
                        _v3SeqNextGlobalNoteIndex += upperFlat.Count(n => !n.IsRest);

                        double lowerBeatShift = 0.0;
                        if (lowerMc > 0)
                        {
                            var genLower = BuildV3SequenceGenerator(lowerMc);
                            var lowerMeasures = genLower.GenerateSequence();
                            lowerFlat     = MusicSequenceGenerator.Flatten(lowerMeasures);

                            // Re-offset lower staff beat positions to start at 0 (independent staff)
                            lowerBeatShift = lowerFlat.Count > 0 ? (lowerFlat[0].BeatPosition ?? 0.0) : 0.0;
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

                            lowerBarBeats = ComputeNewBarBeats(lowerFlat, existingLower);

                            _v3LowerMeasureIndex    = _v3SeqNextMeasureIndex + lowerMeasures.Count;
                            _v3LowerBeatOffset      = _v3SeqNextBeatOffset + lowerMeasures.Count * (double)genLower.TimeSignature.TotalBeats;
                            _v3LowerGlobalNoteIndex = _v3SeqNextGlobalNoteIndex + lowerFlat.Count(n => !n.IsRest);

                            _v3SeqNextMeasureIndex    = _v3LowerMeasureIndex;
                            _v3SeqNextBeatOffset      = _v3LowerBeatOffset;
                            _v3SeqNextGlobalNoteIndex = _v3LowerGlobalNoteIndex;
                        }
                        else
                        {
                            lowerFlat     = new List<GeneratedNote>();
                            lowerBarBeats = new List<double>();
                            _v3LowerMeasureIndex    = _v3SeqNextMeasureIndex;
                            _v3LowerBeatOffset      = _v3SeqNextBeatOffset;
                            _v3LowerGlobalNoteIndex = _v3SeqNextGlobalNoteIndex;
                        }

#if DEBUG
                        Debug.WriteLine($"[V3 Standard] L{_session.ChildLevel} upperMc={upperMc} lowerMc={lowerMc} Upper: {upperFlat.Count} notes ({upperFlat.Count(n => !n.IsRest)} pitched), Lower: {lowerFlat.Count} notes ({lowerFlat.Count(n => !n.IsRest)} pitched), lowerBeatShift: {lowerBeatShift:F2}");

                        // Log ALL upper staff content
                        Debug.WriteLine($"[V3 Upper] Bar beats: {string.Join(", ", upperBarBeats)}");
                        for (int i = 0; i < upperFlat.Count; i++)
                        {
                            var n = upperFlat[i];
                            Debug.WriteLine($"  [{i}] {(n.IsRest ? "REST" : n.SpelledName)} {n.Duration} @ beat {n.BeatPosition:F2}, measure {n.MeasureIndex}");
                        }

                        // Log ALL lower staff content
                        Debug.WriteLine($"[V3 Lower] Bar beats: {string.Join(", ", lowerBarBeats)}");
                        for (int i = 0; i < lowerFlat.Count; i++)
                        {
                            var n = lowerFlat[i];
                            Debug.WriteLine($"  [{i}] {(n.IsRest ? "REST" : n.SpelledName)} {n.Duration} @ beat {n.BeatPosition:F2}, measure {n.MeasureIndex}");
                        }
#endif
                    }

                    // Signal the drawable whether to draw a single end bar on the upper staff.
                    _v3Drawable.UpperHasEndBar = isTwoOctave;
                }

                // ── Push to V3 drawable ────────────────────────────────────────────
                var v3Drawable = _v3Drawable;
                if (v3Drawable == null) return;
#if DEBUG //  2026.06.18 0935 TEMP BLOCK
                if (UseArpeggioPreview) //  2026.06.18 0935 
                { //  2026.06.18 0935 
                    var preview = await _session.LoadArpeggioPreviewAsync( //  2026.06.18 0935 
                        ArpeggioCatalog.MajorTriad, //  2026.06.18 0935 
                        rootNote: ArpeggioPreviewRootNote); //  2026.06.18 0935 
                    upperFlat = preview; //  2026.06.18 0935 
                    lowerFlat = new List<GeneratedNote>(); //  2026.06.18 0935 
                    upperBarBeats = ComputeNewBarBeats(upperFlat, new HashSet<double>()); //  2026.06.18 0935 
                    lowerBarBeats = new List<double>(); //  2026.06.18 0935 
                    v3Drawable.UpperHasEndBar = false; //  2026.06.18 0935 
                    _v3SeqNextMeasureIndex = 0; //  2026.06.18 0935 
                    _v3SeqNextBeatOffset = upperFlat.Sum(n => n.BeatDuration); //  2026.06.18 0935 
                    _v3SeqNextGlobalNoteIndex = upperFlat.Count(n => !n.IsRest); //  2026.06.18 0935 
                    _v3LowerMeasureIndex = _v3SeqNextMeasureIndex; //  2026.06.18 0935 
                    _v3LowerBeatOffset = _v3SeqNextBeatOffset; //  2026.06.18 0935 
                    _v3LowerGlobalNoteIndex = _v3SeqNextGlobalNoteIndex; //  2026.06.18 0935 
                    StatusService.Instance.StatusMessage = $"DEBUG arpeggio preview: {ArpeggioPreviewRootNote} major triad"; //  2026.06.18 0935 
                } //  2026.06.18 0935 
#endif //  2026.06.18 0935 
                v3Drawable.UpperNotes      = upperFlat;
                v3Drawable.LowerNotes      = lowerFlat;
                v3Drawable.UpperBarBeats   = upperBarBeats;
                v3Drawable.LowerBarBeats   = lowerBarBeats;
                v3Drawable.InvalidateLayoutCache();
                v3Drawable.UpperNoteStates = new V3NoteState[upperFlat.Count];
                v3Drawable.LowerNoteStates = new V3NoteState[lowerFlat.Count];
                v3Drawable.IsUpperActive   = true;
                v3Drawable.ActiveNoteIndex = 0;
                v3Drawable.UpperAlpha      = 1f;
                v3Drawable.LowerAlpha      = 1f;

                // Mark first non-rest note on upper staff as Current.
                for (int i = 0; i < upperFlat.Count; i++)
                {
                    if (!upperFlat[i].IsRest)
                    {
                        v3Drawable.UpperNoteStates[i] = V3NoteState.Current;
                        v3Drawable.ActiveNoteIndex = i;
                        break;
                    }
                }

                // Populate session NotesToDraw from upper then lower.
                var rhythmOrder = upperFlat.Concat(lowerFlat).ToList();
                var rhythmSlots = RhythmStartGate.BuildSlots(rhythmOrder);
                int sessionIdx = 0;
                int pitchIdx = 0;
                _session.NotesToDraw.Clear();
                _session.FeedbackViewModels.Clear();
                foreach (var gn in rhythmOrder)
                {
                    if (gn.IsRest) continue;
                    var slot = rhythmSlots[pitchIdx++];
                    _session.NotesToDraw.Add(new NoteInfo
                    {
                        Midi                   = gn.MidiNumber,
                        Name                   = NoteSessionService.ResolveWrittenNoteName(
                            gn.SpelledName, gn.MidiNumber, gn.Letter, gn.Octave, _session.Key, _session.SelectedScale),
                        TargetFreq             = gn.TargetFrequency,
                        X                      = 0f,
                        Duration               = gn.Duration,
                        StartBeat              = slot.StartBeat,
                        DurationBeats          = slot.DurationBeats,
                        GateBeatsAfterPrevious = slot.GateBeatsAfterPrevious,
                    });
                    _session.FeedbackViewModels.Add(new FeedbackItem(sessionIdx++, 0, 0, false));
                }

                _session.ConfigureRhythmStartGates(_session.StaffDisplayMode == StaffDisplayMode.V3);

                _v3SessionUpperPitchCount = upperFlat.Count(n => !n.IsRest);

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

        private static bool V3NoteStatesEqual(V3NoteState[] a, V3NoteState[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        /// <summary>
        /// Syncs V3 note states from session progress on the two-staff display.
        /// </summary>
        private void SyncV3NoteStates()
        {
            if (_v3Drawable == null) return;

            int upperPitchCount = _v3SessionUpperPitchCount;
            int currentSession  = _session.CurrentNoteIndex;

            bool isUpperActive = currentSession < upperPitchCount;

            // ── Upper staff states ────────────────────────────────────────────────
            var upperStates = new V3NoteState[_v3Drawable.UpperNotes.Count];
            int si = 0;
            for (int i = 0; i < _v3Drawable.UpperNotes.Count; i++)
            {
                if (_v3Drawable.UpperNotes[i].IsRest) { upperStates[i] = V3NoteState.Pending; continue; }
                if (si < currentSession)
                    upperStates[i] = _session.CorrectNoteIndices.Contains(si) ? V3NoteState.Correct : V3NoteState.Wrong;
                else if (si == currentSession && isUpperActive)
                {
                    bool hasWrong = _session.NoteFeedbacks.TryGetValue(si, out var fb) && fb.Wrong > 0;
                    upperStates[i] = hasWrong ? V3NoteState.Wrong : V3NoteState.Current;
                }
                else
                    upperStates[i] = V3NoteState.Pending;
                si++;
            }

            // ── Lower staff states ────────────────────────────────────────────────
            var lowerStates = new V3NoteState[_v3Drawable.LowerNotes.Count];
            int li = 0;
            for (int i = 0; i < _v3Drawable.LowerNotes.Count; i++)
            {
                if (_v3Drawable.LowerNotes[i].IsRest) { lowerStates[i] = V3NoteState.Pending; continue; }
                int globalIdx = upperPitchCount + li;
                if (globalIdx < currentSession)
                    lowerStates[i] = _session.CorrectNoteIndices.Contains(globalIdx) ? V3NoteState.Correct : V3NoteState.Wrong;
                else if (globalIdx == currentSession && !isUpperActive)
                {
                    bool hasWrong = _session.NoteFeedbacks.TryGetValue(globalIdx, out var fb2) && fb2.Wrong > 0;
                    lowerStates[i] = hasWrong ? V3NoteState.Wrong : V3NoteState.Current;
                }
                else
                    lowerStates[i] = V3NoteState.Pending;
                li++;
            }

            int activeNoteIndex = isUpperActive ? currentSession : currentSession - upperPitchCount;
            if (_v3Drawable.IsUpperActive == isUpperActive
                && _v3Drawable.ActiveNoteIndex == activeNoteIndex
                && V3NoteStatesEqual(_v3Drawable.UpperNoteStates, upperStates)
                && V3NoteStatesEqual(_v3Drawable.LowerNoteStates, lowerStates))
            {
                return;
            }

            _v3Drawable.IsUpperActive = isUpperActive;
            _v3Drawable.UpperNoteStates = upperStates;
            _v3Drawable.LowerNoteStates = lowerStates;
            _v3Drawable.ActiveNoteIndex = activeNoteIndex;

            V3StaffGraphicsView.Invalidate();

            // Upper staff keeps completed note colors (green/red) while the player works on
            // the lower staff.  RefreshV3UpperStaffAsync replaces UpperNotes and resets
            // UpperNoteStates to Pending, which turned correct upper notes black at the
            // upper→lower transition; defer upper refresh until RegenerateNotesAsync.
        }

        /// <summary>
        /// Generates new notes for the upper V3 staff while the player is on the lower staff,
        /// then fades in the new upper staff content.
        /// </summary>
        private async Task RefreshV3UpperStaffAsync()
        {
            if (_v3Drawable == null || _session.Tune == "Practice Tune" || _session.Tune == "Arpeggio" || V3LayoutTestTune.IsEnabled) return;
            try
            {
                var gen      = BuildV3SequenceGenerator(GetV3StaffMeasureCounts().upper);
                var measures = gen.GenerateSequence();
                var newNotes = MusicSequenceGenerator.Flatten(measures);
                var barBeats = ComputeNewBarBeats(newNotes, new HashSet<double>());

                _v3SeqNextMeasureIndex    += measures.Count;
                _v3SeqNextBeatOffset      += measures.Count * (double)gen.TimeSignature.TotalBeats;
                _v3SeqNextGlobalNoteIndex += newNotes.Count(n => !n.IsRest);

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    _v3Drawable.UpperNotes      = newNotes;
                    _v3Drawable.UpperBarBeats   = barBeats;
                    _v3Drawable.UpperNoteStates = new V3NoteState[newNotes.Count];
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
            if (_v3Drawable == null || _session.Tune == "Practice Tune" || _session.Tune == "Arpeggio" || V3LayoutTestTune.IsEnabled) return;
            int lowerMc = GetV3StaffMeasureCounts().lower;
            if (lowerMc <= 0) return;
            try
            {
                var gen      = BuildV3SequenceGenerator(lowerMc);
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
                    _v3Drawable.LowerNoteStates = new V3NoteState[newNotes.Count];
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
        /// Resizes the V3 staff canvas.  Must be called on the main thread.
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
            UpdateV3PlayButtonPosition();
        }

        /// <summary>
        /// Expand the tuner staff panel to fill the content area below the title bar.
        /// </summary>
        private void ApplyTunerHeight()
        {
            if (_session?.Tune != "Tuner" || TunerGraphicsView == null)
                return;

            try
            {
                var win = Application.Current?.Windows?.FirstOrDefault();
                double availH = 400;
                if (win != null)
                    availH = Math.Max(200, win.Height - 50);

                if (SessionResultBanner?.IsVisible == true && SessionResultBanner.Height > 0)
                    availH -= SessionResultBanner.Height + 4;

                var h = (float)availH;
                MainPageMainLayout.Spacing = 0;
                StaffAreaStack.HeightRequest = h;
                TunerGrid.HeightRequest = h;
                TunerBorder.HeightRequest = h;
                TunerGraphicsView.HeightRequest = h;
                TunerInfoBorder.HeightRequest = h;
                TunerGraphicsView.Invalidate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ApplyTunerHeight] ERROR: {ex}");
            }
        }

        private const double V3PlayButtonSizeMm = 6;

        private void UpdateV3PlayButtonPosition()
        {
            if (_v3PlayButton == null || _v3StartStopButton == null || _titleMarqueeGrid == null
                || _session.StaffDisplayMode != StaffDisplayMode.V3
                || !_v3PlayButton.IsVisible)
                return;

            var overlayParent = MainPageRootGrid;
            if (overlayParent.Width <= 0 || _v3StartStopButton.Width <= 0)
                return;

            var goBounds = TryGetScreenBounds(_v3StartStopButton);
            var marqueeBounds = TryGetScreenBounds(_titleMarqueeGrid);
            var parentBounds = TryGetScreenBounds(overlayParent);
            if (!goBounds.HasValue || !marqueeBounds.HasValue || !parentBounds.HasValue)
                return;

            var go = goBounds.Value;
            var marquee = marqueeBounds.Value;
            var parent = parentBounds.Value;

            double playSize = MarginUtils.MmToDips(V3PlayButtonSizeMm);
            _v3PlayButton.WidthRequest = playSize;
            _v3PlayButton.HeightRequest = playSize;
            UpdateV3PlayButtonFontSize();

            // Top edge flush with bottom of status marquee; center aligned under GO.
            double top = Math.Max(0, marquee.Bottom - parent.Top);
            double left = go.Center.X - parent.Left - playSize * 0.5;
            _v3PlayButton.Margin = new Thickness(Math.Max(0, left), top, 0, 0);
        }

        /// <summary>
        /// Screen bounds in MAUI logical units. TitleView lives outside the page content tree,
        /// so parent-chain X/Y cannot align Shell chrome with MainPageRootGrid.
        /// </summary>
        private static Rect? TryGetScreenBounds(VisualElement element)
        {
            if (element?.Handler?.PlatformView == null)
                return null;

#if WINDOWS
            if (element.Handler.PlatformView is not Microsoft.UI.Xaml.UIElement platformView)
                return null;

            var window = Application.Current?.Windows?.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            if (window?.Content is not Microsoft.UI.Xaml.UIElement windowContent)
                return null;

            try
            {
                var transform = platformView.TransformToVisual(windowContent);
                var topLeft = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                double width = element.Width > 0 ? element.Width : element.WidthRequest;
                double height = element.Height > 0 ? element.Height : element.HeightRequest;
                if (platformView is Microsoft.UI.Xaml.FrameworkElement fe)
                {
                    if (fe.ActualWidth > 0)
                        width = fe.ActualWidth;
                    if (fe.ActualHeight > 0)
                        height = fe.ActualHeight;
                }

                return new Rect(topLeft.X, topLeft.Y, width, height);
            }
            catch
            {
                return null;
            }
#elif ANDROID
            if (element.Handler.PlatformView is not Android.Views.View view)
                return null;

            int[] location = new int[2];
            view.GetLocationOnScreen(location);
            double density = DeviceDisplay.MainDisplayInfo.Density;
            if (density <= 0)
                density = 1;

            double width = view.Width / density;
            double height = view.Height / density;
            if (width <= 0)
                width = element.Width > 0 ? element.Width : element.WidthRequest;
            if (height <= 0)
                height = element.Height > 0 ? element.Height : element.HeightRequest;
            return new Rect(location[0] / density, location[1] / density, width, height);
#else
            return null;
#endif
        }

        private static readonly Color PlayButtonGreen = Color.FromArgb("#2E8B57");
        private static readonly Color PlayButtonGreenBorder = Color.FromArgb("#1F5C3A");
        private static readonly Color PlayButtonRed = Color.FromArgb("#C62828");
        private static readonly Color PlayButtonRedBorder = Color.FromArgb("#8B0000");

        private void SetPlayButtonPlaying(bool isPlaying, bool? isEnabled = null)
        {
            void ApplyStandard(Button button)
            {
                if (isPlaying)
                {
                    button.Text = "Stop";
                    button.TextColor = Colors.White;
                    button.BackgroundColor = PlayButtonRed;
                    button.BorderColor = PlayButtonRedBorder;
                }
                else
                {
                    button.Text = "Play";
                    button.TextColor = Colors.White;
                    button.BackgroundColor = PlayButtonGreen;
                    button.BorderColor = PlayButtonGreenBorder;
                }

                if (isEnabled.HasValue)
                    button.IsEnabled = isEnabled.Value;
            }

            ApplyStandard(PlayEvaluateButton);

            if (isPlaying)
            {
                _v3PlayButton.BackgroundColor = PlayButtonRed;
                _v3PlayButton.Stroke = PlayButtonRedBorder;
                _v3PlayLabelText = V3StopLabelText;
            }
            else
            {
                _v3PlayButton.BackgroundColor = PlayButtonGreen;
                _v3PlayButton.Stroke = PlayButtonGreenBorder;
                _v3PlayLabelText = V3PlayLabelText;
            }

            if (isEnabled.HasValue)
                _v3PlayButton.IsEnabled = isEnabled.Value;

            UpdateV3PlayButtonFontSize();
        }

        private async Task StopListeningForPlaybackAsync()
        {
            try
            {
                _playCts?.Cancel();
                _player.CancelPlayback();
                _audio.StopCapture();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Play] Stop listening error: {ex}");
            }
            finally
            {
                SetButtonStates(false);
            }

            await Task.Delay(80);
        }

        private const int V3StartStopButtonSize = 32;
        /// <summary>Title-bar slot width — stop state expands to this so "Stop" fits.</summary>
        private const int V3StartStopSlotWidth = 38;
        private const string V3GoLabelText = "GO";
        private const string V3PlayLabelText = "Play";
        private const string V3StopLabelText = "Stop";
        private string _v3PlayLabelText = V3PlayLabelText;

        private static SKTypeface? _v3UiRegularTypeface;

        /// <summary>OpenSansRegular base face; MAUI applies synthetic bold via FontAttributes.Bold.</summary>
        private static SKTypeface V3UiRegularTypeface =>
            _v3UiRegularTypeface ??= SKTypeface.FromFamilyName("Open Sans", SKFontStyle.Normal)
                ?? SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal)
                ?? SKTypeface.Default;

        private static void ConfigureV3UiBoldFont(SKFont font, float size)
        {
            font.Size = size;
            font.Typeface = V3UiRegularTypeface;
            // Match MAUI synthetic bold on OpenSansRegular (wider than native bold metrics).
            font.Embolden = true;
            font.Edging = SKFontEdging.Antialias;
            font.Subpixel = true;
        }

        /// <summary>
        /// Largest bold font size whose glyph bounds fit inside the box, with slack for
        /// embolden stroke and MAUI Label rendering wider than Skia advance width.
        /// </summary>
        private static double GetV3FittedFontSize(string text, double maxWidth, double maxHeight)
        {
            if (maxWidth <= 0 || maxHeight <= 0 || string.IsNullOrEmpty(text))
                return 10;

            const double edgeSlack = 1.5;
            const double mauiWidthSlack = 2;
            const float emboldenPad = 2f;

            using var font = new SKFont();

            double lo = 1, hi = Math.Max(maxWidth, maxHeight) * 1.5, best = 1;
            while (hi - lo > 0.25)
            {
                double mid = (lo + hi) / 2;
                ConfigureV3UiBoldFont(font, (float)mid);

                font.MeasureText(text, out var bounds);
                float width = bounds.Width + emboldenPad + (float)(edgeSlack * 2 + mauiWidthSlack);
                float height = bounds.Height + emboldenPad + (float)(edgeSlack * 2);

                if (width <= maxWidth && height <= maxHeight)
                {
                    best = mid;
                    lo = mid;
                }
                else
                    hi = mid;
            }

            return best;
        }

        /// <summary>
        /// Skia glyph-bounds width for bold UI text, including embolden and MAUI slack.
        /// </summary>
        private static double MeasureV3BoldTextWidth(string text, double fontSize)
        {
            using var font = new SKFont();
            ConfigureV3UiBoldFont(font, (float)fontSize);
            font.MeasureText(text, out var bounds);

            const double edgeSlack = 1.5;
            const double mauiWidthSlack = 2;
            const float emboldenPad = 2f;
            return bounds.Width + emboldenPad + edgeSlack * 2 + mauiWidthSlack;
        }

        /// <summary>
        /// Largest bold "GO" font size that fits fully inside the green start circle.
        /// </summary>
        private static double GetV3GoFontSize(double circleDiameter)
        {
            if (circleDiameter <= 0)
                return 10;

            const double inset = 3;
            double inner = circleDiameter - inset * 2;
            return GetV3FittedFontSize(V3GoLabelText, inner, inner);
        }

        /// <summary>Largest bold "Stop" font size that fits inside the red stop button.</summary>
        private static double GetV3StopFontSize(double width, double height)
        {
            if (width <= 0 || height <= 0)
                return 10;

            const double inset = 2;
            return GetV3FittedFontSize(V3StopLabelText, width - inset * 2, height - inset * 2);
        }

        private void UpdateV3PlayButtonFontSize()
        {
            if (_v3PlayButton == null)
                return;

            string text = _v3PlayLabelText;
            double size = _v3PlayButton.HeightRequest > 0
                ? _v3PlayButton.HeightRequest
                : _v3PlayButton.Height;
            if (size <= 0)
                size = MarginUtils.MmToDips(V3PlayButtonSizeMm);

            // StrokeThickness=1 plus slack so glyphs stay inside the square.
            const double inset = 3;
            double inner = size - inset * 2;
            if (inner <= 0)
                return;

            // Square button: largest font that fits the current label fully inside.
            double fontSize = GetV3FittedFontSize(text, inner, inner);

            _v3PlayButton.Content = new Label
            {
                Text = text,
                FontSize = fontSize,
                FontFamily = "OpenSansRegular",
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.Yellow,
                InputTransparent = true,
                WidthRequest = size,
                HeightRequest = size,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.NoWrap,
                MaxLines = 1,
                FontAutoScalingEnabled = false,
                Padding = 0
            };
        }

        private void UpdateV3StartStopButtonVisual(bool isRunning)
        {
            if (_v3StartStopButton == null)
                return;

            if (isRunning)
            {
                double width = V3StartStopSlotWidth;
                double height = V3StartStopButtonSize;

                _v3StartStopButton.WidthRequest = width;
                _v3StartStopButton.HeightRequest = height;
                _v3StartStopButton.BackgroundColor = Colors.Red;
                _v3StartStopButton.StrokeShape = new RoundRectangle { CornerRadius = 6 };
                _v3StartStopButton.Content = new Label
                {
                    Text = V3StopLabelText,
                    FontSize = GetV3StopFontSize(width, height),
                    FontFamily = "OpenSansRegular",
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.Yellow,
                    InputTransparent = true,
                    WidthRequest = width,
                    HeightRequest = height,
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = LayoutOptions.Fill,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                    LineBreakMode = LineBreakMode.NoWrap,
                    MaxLines = 1,
                    FontAutoScalingEnabled = false,
                    Padding = 0
                };
            }
            else
            {
                double diameter = V3StartStopButtonSize;

                _v3StartStopButton.WidthRequest = diameter;
                _v3StartStopButton.HeightRequest = diameter;
                _v3StartStopButton.BackgroundColor = Color.FromArgb("#008000");
                _v3StartStopButton.StrokeShape = new RoundRectangle { CornerRadius = diameter / 2 };
                _v3StartStopButton.Content = new Label
                {
                    Text = V3GoLabelText,
                    FontSize = GetV3GoFontSize(diameter),
                    FontFamily = "OpenSansRegular",
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.Yellow,
                    InputTransparent = true,
                    WidthRequest = diameter,
                    HeightRequest = diameter,
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = LayoutOptions.Fill,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                    LineBreakMode = LineBreakMode.NoWrap,
                    MaxLines = 1,
                    FontAutoScalingEnabled = false,
                    Padding = 0
                };
            }
        }

        private void SetButtonStates(bool isRunning, bool keepPlayEnabled = false)
        {
            _isRunning = isRunning;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (isRunning)
                {
                    StartStopButton.Text = V3StopLabelText;
                    StartStopButton.TextColor = Colors.Yellow;
                    StartStopButton.BackgroundColor = Colors.Red;
                    StartStopButton.FontSize = 14;
                    StartStopButton.FontAttributes = FontAttributes.Bold;
                    UpdateV3StartStopButtonVisual(true);
                    SetPlayButtonPlaying(_isPlaying, keepPlayEnabled || _isPlaying);
                }
                else
                {
                    StartStopButton.Text = "●";
                    StartStopButton.TextColor = Color.FromArgb("#008000");
                    StartStopButton.BackgroundColor = Color.FromArgb("#F7F7F7");
                    StartStopButton.FontSize = 26;
                    StartStopButton.FontAttributes = FontAttributes.None;
                    UpdateV3StartStopButtonVisual(false);
                    SetPlayButtonPlaying(false, true);
                }

                UpdatePlayButtonVisibility();
            });
        }

        private void UpdatePlayButtonVisibility()
        {
            OnPropertyChanged(nameof(IsV3PlayButtonVisible));
            OnPropertyChanged(nameof(IsPlayEvaluateButtonVisible));
            UpdateV3PlayButtonPosition();
        }

        private async void OnStartStopToggleClicked(object? sender, EventArgs e)
        {
            if (_isRunning)
            {
                _sessionStartCts?.Cancel();
                try
                {
                    _playCts?.Cancel();
                    _player.CancelPlayback();
                    _audio.StopCapture();
                    _isPlaying = false;
                    SetPlayButtonPlaying(false);
                    _session.SessionCompleted = false;
                    _savedNotesToRepeat = null;
                    _holdResultForChildSession = false;
                    SetButtonStates(false);

                    _session.Reset();
                    if (_session.ChildLevel > 0)
                        PickChildSessionSettingsIfNeeded(preserveRepeatSameTune: false);
                    await RegenerateNotesAsync();

                    StatusService.Instance.StatusMessage =
                        "Tap GO and play the notes. Tap Play for 'phone to play.";
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Stop error: {ex}");
                    SetButtonStates(false);
                }
            }
            else
            {
                _holdResultForChildSession = false;
                _session.SessionCompleted = false;
                _savedNotesToRepeat = null;
                StatusService.Instance.StatusMessage = "Listening, go ahead and play!";
                await StartListeningAndEvaluatingAsync(forceNewNotes: true);
            }
        }

        private int GetChildLevelForSlider()
        {
            if (_session.ChildLevel <= 0) return 1;
            return Math.Clamp(Preferences.Default.Get(ChildLevelPrefKey, _session.ChildLevel), 1, 100);
        }

        private async void OnChildLevelDeltaClicked(object? sender, EventArgs e)
        {
            if (_session.ChildLevel <= 0
                || sender is not Button { CommandParameter: string param }
                || !int.TryParse(param, out int delta))
                return;

            int level = Math.Clamp(_session.ChildLevel + delta, 1, 100);
            if (level == _session.ChildLevel)
                return;

            await ApplyChildLevelAndRefreshAsync(level);
        }

        private async Task ApplyChildLevelAndRefreshAsync(int level)
        {
            level = Math.Clamp(level, 1, 100);

            PracticeDifficultySettings difficulty;
            _suppressSessionRegenerate = true;
            try
            {
                _session.ChildLevel = level;
                difficulty = DifficultyLevelMapper.PickAndApplyToSession(
                    level, _session, forceClassicMode: false);
                Preferences.Default.Set(ChildLevelPrefKey, level);
                LevelUpService.MarkCountSinceNow();

                UpdateKeyPickerSelection();
                UpdateScaleTunePicker();
                UpdateConcertKeyLabel();
            }
            finally
            {
                _suppressSessionRegenerate = false;
            }

            _savedNotesToRepeat = null;
            ChildLevelSliderValueLabel.Text = level.ToString();

            StatusService.Instance.StatusMessage =
                $"Level {level}: {difficulty.StageLabel} — {difficulty.SuggestedKey} {difficulty.SuggestedScale}";

            await RefreshDisplayForLevelChangeAsync();
        }

        private async Task RefreshDisplayForLevelChangeAsync()
        {
            _freezeStaff = false;
            _holdResultForChildSession = false;
            _savedNotesToRepeat = null;
            _session.SessionCompleted = false;

            await HideSessionResultBannerAsync(refreshMarqueeForNewLevel: false);

            await RegenerateNotesAsync();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (_session.StaffDisplayMode == StaffDisplayMode.V3)
                {
                    ApplyV3Height();
                    V3StaffGraphicsView.Invalidate();
                }
                else
                    StaffGraphicsView.Invalidate();

                UpdateV3PlayButtonPosition();
            });
        }

        /// <summary>
        /// Apply child-level session settings for a new run. Re-picks key/scale from level pools
        /// unless the user has customized key, scale, accidental, or rhythm settings.
        /// Skipped when Repeat Same will restore the previous tune.
        /// </summary>
        private void PickChildSessionSettingsIfNeeded(bool preserveRepeatSameTune)
        {
            if (_session.ChildLevel <= 0)
                return;
            if (preserveRepeatSameTune && _repeatSameTune && _savedNotesToRepeat?.Count > 0)
                return;

            DifficultyLevelMapper.PickAndApplyToSession(
                _session.ChildLevel, _session, forceClassicMode: false,
                preserveUserPracticeSettings: true);
            UpdateKeyPickerSelection();
            UpdateScaleTunePicker();
            UpdateConcertKeyLabel();
        }

        private async Task BuildAndPublishSessionEndMarqueeAsync(
            int progressLevel,
            string shortInstrument,
            DateTime countSince)
        {
            if (_sessionResultDb == null)
                return;

            await _sessionResultDb.InitializeAsync();
            var rows = await _sessionResultDb.GetByLevelAndInstrumentAsync(progressLevel, shortInstrument);
            var qualifying = rows
                .Where(r => r.DateTime >= countSince)
                .Where(r => r.TotalNotes >= Services.LevelUpService.MinNotesPerSession)
                .Where(r => !(r.TotalNotes > 0 && r.OverallAccuracyPercent == 0))
                .ToList();
            int sessionCount = Services.LevelUpService.SessionCount;
            int ssns = qualifying.Count;
            var recent = qualifying.Take(sessionCount).ToList();
            double avgPitch = recent.Count > 0 ? recent.Average(r => r.PitchAccuracyPercent) : 0.0;
            double avgOverall = recent.Count > 0 ? recent.Average(r => r.OverallAccuracyPercent) : 0.0;
            var timingSessions = recent.Where(r => r.TimingAccuracyPercent.HasValue).ToList();
            double avgTiming = timingSessions.Count > 0
                ? timingSessions.Average(r => r.TimingAccuracyPercent!.Value)
                : 0.0;
            var tempoSessions = recent.Where(r => r.DetectedBpm.HasValue).ToList();
            int avgDetectedBpm = tempoSessions.Count > 0
                ? (int)Math.Round(tempoSessions.Average(r => r.DetectedBpm!.Value))
                : 0;
            SetSessionEndMarqueeMessage(
                progressLevel, ssns, sessionCount, avgPitch, avgOverall, avgTiming, avgDetectedBpm);
        }

        private async Task HideSessionResultBannerAsync(bool refreshMarqueeForNewLevel)
        {
            await MainThread.InvokeOnMainThreadAsync(() => SessionResultBanner.IsVisible = false);
            if (refreshMarqueeForNewLevel)
                await RefreshMarqueeAfterCongratulatoryBannerAsync();
        }

        /// <summary>
        /// After the congratulatory session-result banner is dismissed, publish marquee stats
        /// for the new level. While the banner was visible, the saved marquee was kept at the
        /// completed level.
        /// </summary>
        private async Task RefreshMarqueeAfterCongratulatoryBannerAsync()
        {
            if (!_deferNewLevelMarqueeUntilBannerDismissed)
                return;

            _deferNewLevelMarqueeUntilBannerDismissed = false;
            string instrument = _pendingInstrumentForMarquee
                ?? _session.Instrument?.Split(',')[0].Trim()
                ?? string.Empty;
            _pendingInstrumentForMarquee = null;

            if (_session.ChildLevel > 0 && !string.IsNullOrEmpty(instrument))
            {
                await BuildAndPublishSessionEndMarqueeAsync(
                    _session.ChildLevel, instrument, Services.LevelUpService.CountSinceUtc);
            }
            else if (!string.IsNullOrEmpty(_sessionEndMarqueeMessage))
            {
                StatusService.Instance.StatusMessage = _sessionEndMarqueeMessage;
            }
        }

        private void SetSessionEndMarqueeMessage(
            int progressLevel,
            int ssns,
            int sessionCount,
            double avgPitch,
            double avgOverall,
            double avgTiming,
            int avgDetectedBpm)
        {
#if DEBUG
            _sessionEndMarqueeMessage =
                $"L{progressLevel} ssns={ssns}/{sessionCount}, Pch={avgPitch:F0}%, Ovrl={avgOverall:F0}%, Tmg={avgTiming:F0}%, Det={avgDetectedBpm}";
#else
            _sessionEndMarqueeMessage =
                $"ssns={ssns}/{sessionCount}, Pch={avgPitch:F0}%, Ovrl={avgOverall:F0}%, Tmg={avgTiming:F0}%, Det={avgDetectedBpm}";
#endif
            StatusService.Instance.StatusMessage = _sessionEndMarqueeMessage;
        }

        private bool ShouldPreserveSessionEndMarquee() =>
            !string.IsNullOrEmpty(_sessionEndMarqueeMessage)
            && _session.SessionCompleted
            && _session.ChildLevel > 0;

        private void RestoreSessionEndMarqueeIfNeeded()
        {
            if (!ShouldPreserveSessionEndMarquee())
                return;

            StatusService.Instance.StatusMessage = _sessionEndMarqueeMessage!;
            MainThread.BeginInvokeOnMainThread(() => SessionResultBanner.IsVisible = true);
        }

        private void ClearSessionEndMarquee()
        {
            _sessionEndMarqueeMessage = null;
            _deferNewLevelMarqueeUntilBannerDismissed = false;
            _pendingInstrumentForMarquee = null;
        }

        private void UpdateChildLevelSliderDisplay()
        {
            OnPropertyChanged(nameof(IsChildLevelSliderVisible));
            if (_session.ChildLevel <= 0) return;

            int level = GetChildLevelForSlider();
            _session.ChildLevel = level;

            ChildLevelSliderValueLabel.Text = level.ToString();
        }

        protected async override void OnAppearing()
        {
            base.OnAppearing();
            bool returningToPage = !_isPageVisible;
            _isPageVisible = true;
            if (returningToPage)
            {
                _freezeStaff = false;
                // Keep result hold when returning from Settings so the session-end
                // marquee and banner stay visible until the user starts again.
                _holdResultForChildSession = ShouldPreserveSessionEndMarquee();
            }
            _orientation.ForceLandscape();

            Debug.WriteLine($"[DEBUG] OnAppearing: IsAutoRepeatVisible={IsAutoRepeatVisible}, Tune={_session.Tune}");
            IsAutoRepeatVisible = _session.Tune != "Tuner";
            UpdateTunerVisibility();
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

            UpdateChildLevelSliderDisplay();
            // Android may lay out the slider row after OnAppearing; refresh once more.
            Dispatcher.Dispatch(UpdateChildLevelSliderDisplay);
            Dispatcher.Dispatch(UpdateV3PlayButtonPosition);

#if DEBUG
            if (_session.AutoStart && _session.Tune != "Tuner" && _session.ChildLevel == 0)
            {
                AutoRepeat = true;
            }
#endif

            if (_session.AutoStart)
            {
                if (_holdResultForChildSession)
                {
                    RestoreSessionEndMarqueeIfNeeded();
                    return;
                }
                ScheduleAutoStartOnAppear();
                return;
            }

            // Always regenerate notes on every appearance so that changes made on
            // Settings/Advanced pages (key, scale, range, etc.) are reflected immediately.
            // Wait for the layout to provide a valid staff width first.
            if (!_isRunning && !ShouldPreserveSessionEndMarquee())
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

            RestoreSessionEndMarqueeIfNeeded();
            //TEMP
#if DEBUG
            var notes = _session.BuildArpeggioPreviewNotes(
                ArpeggioCatalog.MajorTriad,
                rootNote: "Bb3");
#endif 
        }

        protected override void OnNavigatedTo(NavigatedToEventArgs args)
        {
            base.OnNavigatedTo(args);
            if (_session.AutoStart && !_holdResultForChildSession)
                ScheduleAutoStartOnAppear();

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
            _autoStartCts?.Cancel();
            _sessionStartCts?.Cancel();
            // Stop listening and evaluating
            _playCts?.Cancel();
            _audio?.StopCapture();
            SetButtonStates(false);
            DeviceDisplay.Current.KeepScreenOn = false;
            if (!ShouldPreserveSessionEndMarquee())
                StatusService.Instance.StatusMessage = "Stopped listening.";
        }

        private async void OnPlayEvaluateClicked(object? sender, EventArgs e)
        {
            if (_isPlaying)
            {
                _playCts?.Cancel();
                _player.CancelPlayback();
                _isPlaying = false;
                SetPlayButtonPlaying(false);
                SetButtonStates(false);
                UpdatePlayButtonVisibility();
                return;
            }

            if (_isRunning)
                await StopListeningForPlaybackAsync();

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
            SetPlayButtonPlaying(true);
            UpdatePlayButtonVisibility();
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
                    MainThread.BeginInvokeOnMainThread(() => TunerGraphicsView.Invalidate());
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
                                // When upper staff is exhausted, trigger lower-staff refresh.
                                int upperPitchCount = _v3SessionUpperPitchCount;
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

                    TimingDiagnostics.Flush();
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
                var sessionStats = _session.GetAndClearSessionNoteStats();
                var sessionStreaks = _session.GetSessionStreaks();

                foreach (var (writtenName, agg) in sessionStats)
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
                            PitchCorrectCount = agg.PitchCorrect,
                            PitchWrongCount = agg.PitchWrong,
                            TimingCorrectCount = agg.TimingCorrect,
                            TimingWrongCount = agg.TimingWrong,
                            OverallCorrectCount = agg.OverallCorrect,
                            OverallWrongCount = agg.OverallWrong,
                            RestCorrectCount = 0,
                            RestWrongCount = 0,
                            Correct = agg.OverallCorrect,
                            Wrong = agg.OverallWrong,
                            MsCount = agg.MsCount,
                            MsAverage = agg.MsCount > 0 ? agg.TotalMs / agg.MsCount : 0.0,
                            Streak = sessionStreaks.GetValueOrDefault(writtenName, 0)
                        };
                        await db.InsertOrReplaceAsync(stat);
                    }
                    else
                    {
                        stat.PitchCorrectCount += agg.PitchCorrect;
                        stat.PitchWrongCount += agg.PitchWrong;
                        stat.TimingCorrectCount += agg.TimingCorrect;
                        stat.TimingWrongCount += agg.TimingWrong;
                        stat.OverallCorrectCount += agg.OverallCorrect;
                        stat.OverallWrongCount += agg.OverallWrong;
                        stat.Correct = stat.OverallCorrectCount;
                        stat.Wrong = stat.OverallWrongCount;
                        if (agg.MsCount > 0)
                        {
                            var newMsCount = stat.MsCount + agg.MsCount;
                            stat.MsAverage = (stat.MsAverage * stat.MsCount + agg.TotalMs) / newMsCount;
                            stat.MsCount = newMsCount;
                        }
                        if (sessionStreaks.TryGetValue(writtenName, out var sessionStreak))
                        {
                            if (sessionStreak > 0 && agg.OverallWrong == 0)
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
                var concertKey = _session.GetConcertKey();

                foreach (var outcome in _session.GetSessionAttemptOutcomes())
                {
                    string concertName = concertKey != _session.Key ? concertKey : string.Empty;
                    double? durationBeats = null;
                    if (!string.IsNullOrEmpty(outcome.ExpectedDuration))
                    {
                        durationBeats = outcome.ExpectedDuration switch
                        {
                            "Whole" => 4.0,
                            "Half" => 2.0,
                            "Quarter" => 1.0,
                            "Eighth" => 0.5,
                            "Sixteenth" => 0.25,
                            _ => null
                        };
                    }

                    var attempt = new musicmate.Models.NoteAttempt
                    {
                        DateTime = DateTime.UtcNow,
                        SessionId = sessionId,
                        Instrument = instrument,
                        Level = level,
                        WrittenNoteName = outcome.ExpectedWrittenNoteName,
                        ExpectedWrittenNoteName = outcome.ExpectedWrittenNoteName,
                        ActualDetectedNoteName = outcome.ActualDetectedNoteName,
                        IsRest = outcome.IsRest,
                        ExpectedDuration = outcome.ExpectedDuration,
                        ExpectedBeat = outcome.ExpectedBeat,
                        ExpectedStartMs = outcome.ExpectedStartMs,
                        ActualDetectedMs = outcome.ActualDetectedMs,
                        ConcertPitchNoteName = concertName,
                        MidiNumber = outcome.MidiNumber,
                        ExpectedDurationBeats = durationBeats,
                        PitchErrorCents = outcome.PitchErrorCents,
                        TimingErrorMs = outcome.TimingErrorMs,
                        TimingToleranceMs = outcome.TimingToleranceMs,
                        PitchCorrect = outcome.PitchCorrect,
                        TimingCorrect = outcome.TimingCorrect,
                        OverallCorrect = outcome.OverallCorrect,
                        WrongReason = outcome.WrongReason,
                    };

                    await _noteAttemptDb.SaveAttemptAsync(attempt);
                }

                _session.ClearSessionAttemptOutcomes();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NoteAttempts] SaveNoteAttemptsForSessionAsync error: {ex}");
            }
        }

        private async Task StartListeningAndEvaluatingAsync(bool playBack = false, bool forceNewNotes = false)
        {
            _sessionStartCts?.Cancel();
            var startCts = new CancellationTokenSource();
            _sessionStartCts = startCts;
            var ct = startCts.Token;

            try
            {
                // Any new session clears the post-autoplay results freeze.
                _freezeStaff = false;
                ClearSessionEndMarquee();

                // Assign a fresh session ID so all NoteAttempts from this run are grouped together.
                _currentSessionId = Guid.NewGuid().ToString();

                StatusService.Instance.StatusMessage = playBack
                    ? "Playing… tap Stop to end playback"
                    : "Listening, tap red square to stop";
                Debug.WriteLine($"[Start] Starting listening, playBack={playBack}, forceNewNotes={forceNewNotes}");
                SetButtonStates(true, keepPlayEnabled: playBack);

                _lastProcess = DateTime.MinValue;
                _isBelowThreshold = true;
                _pitchBufferPos = 0;
                _session.Reset();

                ct.ThrowIfCancellationRequested();

                PickChildSessionSettingsIfNeeded(preserveRepeatSameTune: !forceNewNotes);

                // Handle note generation based on repeat mode
                if (!forceNewNotes
                    && _repeatSameTune && _savedNotesToRepeat != null && _savedNotesToRepeat.Count > 0)
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
                            return !MasteryEvaluator.IsFullyMastered(stat, _session);
                        }).ToList();
                    }

                    // If too few notes remain after mastery filtering, regenerate instead
                    if (notesToRestore.Count < 2)
                    {
                        PickChildSessionSettingsIfNeeded(preserveRepeatSameTune: false);
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
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            if (_session.StaffDisplayMode == StaffDisplayMode.V3)
                                V3StaffGraphicsView.Invalidate();
                            else
                                StaffGraphicsView.Invalidate();
                        });
                        Debug.WriteLine($"[Start] Restored {notesToRestore.Count} notes for Repeat Same (filtered from {_savedNotesToRepeat.Count})");
                    }
                }
                else
                {
                    // Generate new notes (for first run, "Repeat New", manual GO, or scale modes)
                    await RegenerateNotesAsync();

                    // Save notes for potential "Repeat Same" after generation
                    if (_session?.NotesToDraw != null && _session.NotesToDraw.Count > 0)
                    {
                        _savedNotesToRepeat = new List<NoteInfo>(_session.NotesToDraw);
                        Debug.WriteLine($"[Start] Generated and saved {_session.NotesToDraw.Count} notes");
                    }
                }

                ct.ThrowIfCancellationRequested();

                if (!playBack)
                {
                    if (!_isRunning)
                    {
                        Debug.WriteLine("[Start] Aborted before capture: no longer running");
                        return;
                    }

                    Debug.WriteLine("[Start] Requesting audio permission...");
                    await _audio.EnsurePermissionAsync();
                    ct.ThrowIfCancellationRequested();
                    if (!_isRunning)
                    {
                        Debug.WriteLine("[Start] Aborted after permission: no longer running");
                        return;
                    }

                    try { _audio.StopCapture(); } catch { }
                    var expectedNotes = _session.NotesToDraw
                        .Take(5)
                        .Select(n => n.Name)
                        .ToArray();
                    var sessionLog =
                        $"[Start] Instrument={_session.Instrument}, " +
                        $"transpose={_session.InstrumentTransposeOffset}, " +
                        $"Key={_session.Key}, Scale={_session.SelectedScale}, " +
                        $"Notes=[{string.Join(", ", expectedNotes)}]";
                    Debug.WriteLine(sessionLog);
                    Utils.Log(sessionLog);
                    Debug.WriteLine("[Start] Starting audio capture...");
                    _audio.StartCapture(OnAudioBlock);
                    _session.StartListeningClock();
                    Debug.WriteLine("[Start] Audio capture started");
                }
                else
                {
                    try { _audio.StopCapture(); } catch { }
                }

                if (playBack)
                {
                    ct.ThrowIfCancellationRequested();

                    if (_session.NotesToDraw.Count == 0)
                    {
                        _isPlaying = false;
                        SetPlayButtonPlaying(false);
                        SetButtonStates(false);
                        StatusService.Instance.StatusMessage =
                            "No notes to play — try again.";
                        Debug.WriteLine("[Start] Play aborted: NotesToDraw is empty after regenerate");
                        return;
                    }

                    _playCts?.Cancel();
                    _playCts = new CancellationTokenSource();
                    _ = PlayDisplayedAsync(_playCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[Start] Cancelled");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Start] ERROR: {ex}");
                _isPlaying = false;
                SetPlayButtonPlaying(false);
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
        /// <summary>Full rhythmic sequence (notes and rests) for V3 autoplay; null for classic staff.</summary>
        private List<GeneratedNote>? TryGetAutoplayRhythmSequence()
        {
            if (_session.StaffDisplayMode == StaffDisplayMode.V3 && _v3Drawable != null)
                return _v3Drawable.UpperNotes.Concat(_v3Drawable.LowerNotes).ToList();
            return null;
        }

        private void ApplyAutoplayRhythmHighlight(int eventIndex, IReadOnlyList<GeneratedNote> sequence, int pitchIndex)
        {
            _session.PlaybackHighlightIndex = sequence[eventIndex].IsRest ? null : pitchIndex;

            if (_session.StaffDisplayMode == StaffDisplayMode.V3 && _v3Drawable != null)
            {
                int upperCount = _v3Drawable.UpperNotes.Count;
                bool onUpper = eventIndex < upperCount;
                _v3Drawable.IsUpperActive = onUpper;
                _v3Drawable.ActiveNoteIndex = onUpper ? eventIndex : eventIndex - upperCount;

                _v3Drawable.UpperNoteStates = BuildRhythmStaffStates(_v3Drawable.UpperNotes, 0, eventIndex);
                _v3Drawable.LowerNoteStates = BuildRhythmStaffStates(_v3Drawable.LowerNotes, upperCount, eventIndex);
                V3StaffGraphicsView.Invalidate();
            }
            else
            {
                StaffGraphicsView.Invalidate();
            }
        }

        private static V3NoteState[] BuildRhythmStaffStates(
            IReadOnlyList<GeneratedNote> staffNotes, int globalOffset, int eventIndex)
        {
            var states = new V3NoteState[staffNotes.Count];
            for (int d = 0; d < staffNotes.Count; d++)
            {
                int globalIdx = globalOffset + d;
                if (globalIdx < eventIndex)
                    states[d] = staffNotes[d].IsRest ? V3NoteState.Pending : V3NoteState.Correct;
                else if (globalIdx == eventIndex)
                    states[d] = V3NoteState.Current;
                else
                    states[d] = V3NoteState.Pending;
            }

            return states;
        }

        private async Task PlayDisplayedAsync(CancellationToken ct)
        {
            var cancelled = false;
            try
            {
                var rhythmSeq = TryGetAutoplayRhythmSequence();
                if (rhythmSeq != null && rhythmSeq.Count > 0)
                {
                    PlaybackRhythmDiagnostics.LogRhythmSpan("sequence", rhythmSeq);

                    var bpm = Math.Clamp(_session.PlaybackBpm, 30, 200);
                    var beatSeconds = 60.0 / bpm;
                    int pitchCount = rhythmSeq.Count(n => !n.IsRest);
                    int pitchIndex = 0;

                    for (int i = 0; i < rhythmSeq.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();

                        var ev = rhythmSeq[i];
                        var totalSeconds = beatSeconds * ev.BeatDuration;
                        var gapSeconds = Math.Min(0.02, totalSeconds * 0.05);
                        var noteSeconds = Math.Max(0.05, totalSeconds - gapSeconds);

                        StatusService.Instance.StatusMessage = ev.IsRest
                            ? $"Rest {i + 1}/{rhythmSeq.Count}"
                            : $"Playing note {pitchIndex + 1}/{pitchCount}: {ev.SpelledName}";

                        await MainThread.InvokeOnMainThreadAsync(() =>
                            ApplyAutoplayRhythmHighlight(i, rhythmSeq, pitchIndex));

                        bool notePlayed = false;
                        try
                        {
                            if (ev.IsRest)
                                await Task.Delay(TimeSpan.FromSeconds(totalSeconds), ct);
                            else
                            {
                                await _player.PlayAsync(new[] { ev.TargetFrequency }, noteSeconds, gapSeconds, 0.22f, ct);
                                notePlayed = true;
                                pitchIndex++;
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        finally
                        {
                            await MainThread.InvokeOnMainThreadAsync(() =>
                            {
                                if (notePlayed && pitchIndex - 1 < _session.FeedbackViewModels.Count)
                                {
                                    int fb = pitchIndex - 1;
                                    var cur = _session.FeedbackViewModels[fb];
                                    _session.FeedbackViewModels[fb] = new FeedbackItem(fb, cur.WrongAttempts, 0, true);
                                }

                                _session.PlaybackHighlightIndex = null;
                                StaffGraphicsView.Invalidate();
                                if (_session.StaffDisplayMode == StaffDisplayMode.V3)
                                    V3StaffGraphicsView.Invalidate();
                            });
                        }
                    }

                    return;
                }

                if (_session.NotesToDraw.Count == 0)
                    return;

                var bpmLegacy = Math.Clamp(_session.PlaybackBpm, 30, 200);
                var beatSecondsLegacy = 60.0 / bpmLegacy;

                for (int i = 0; i < _session.NotesToDraw.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var note = _session.NotesToDraw[i];

                    var durationBeats = note.Duration.HasValue
                        ? note.Duration.Value.ToBeatValue()
                        : 1.0;
                    var totalSeconds = beatSecondsLegacy * durationBeats;
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
                    SetPlayButtonPlaying(false);
                    UpdatePlayButtonVisibility();

                    if (!cancelled && _session.NotesToDraw.Count > 0)
                    {
                        // Freeze the staff NOW so the green feedbacks survive
                        // the instrument restoration that follows.
                        _freezeStaff = true;
                        _session.SessionCompleted = true;
                        _completionFromPlayback = true;
                        await _session.TriggerSessionCompletionAsync();

                        var playbackBpm = (double)_session.PlaybackBpm;
                        StatusService.Instance.StatusMessage =
                            $"Playback {playbackBpm:F0} BPM. Tap GO to listen or Play to hear again.";
                    }

                    // Restore instrument after freeze — its PropertyChanged will
                    // trigger RegenerateNotesAsync which is now suppressed.
                    if (_savedInstrumentIndexForPlayback >= 0)
                    {
                        InstrumentPicker.SelectedIndex = _savedInstrumentIndexForPlayback;
                    }
                    _savedInstrumentForPlayback = null;
                    _savedInstrumentIndexForPlayback = -1;

                    SetButtonStates(false);
                    UpdatePlayButtonVisibility();
                });

                if (cancelled)
                {
                    SetButtonStates(false);
                    StatusService.Instance.StatusMessage =
                        "Stopped. Tap GO to listen or Play to hear the tune.";
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
            var detectedBpm = _session.GetDetectedBpm();
            var hi = _session.NotesToDraw.OrderByDescending(n => n.Midi).FirstOrDefault();
            var lo = _session.NotesToDraw.OrderBy(n => n.Midi).FirstOrDefault();

            // Get timing accuracy (null when <3 notes)
            double? timingAccuracyPercent = _session.GetTimingAccuracyPercent();

            // Blend pitch and timing into overall accuracy
            double overallAccuracy = timingAccuracyPercent.HasValue
                ? (apc + timingAccuracyPercent.Value) / 2.0
                : apc;

            var (pitchRight, pitchWrong, timingRight, timingWrong,
                 overallRight, overallWrong, restRight, restWrong) = _session.GetSessionSummaryCounts();

            var stat = new SessionStat
            {
                Dt = DateTime.Now,
                Key = _session.Key,
                Tune = _session.Tune ?? string.Empty,
                Instrument = _session.Instrument?.Split(',')[0].Trim() ?? string.Empty,
                Sc = _session.Tune == "Practice Tune"
                    ? (_session.CurrentTune?.Title ?? "Practice Tune")
                    : _session.SelectedScale,
                Rand = _session.IsRandomMode,
                AccPct = _session.AccidentalPercent,
                Hi = hi?.Name ?? "",
                Lo = lo?.Name ?? "",
                Pc = apc,
                PcRaw = pc,
                Tp = detectedBpm ?? 0,
                Ts = 0,
                // New timing/accuracy fields
                Level = _session.ChildLevel,
                Pch = apc,
                Tmg = timingAccuracyPercent ?? 0.0,
                Ovrl = overallAccuracy,
                PitchRightCount = pitchRight,
                PitchWrongCount = pitchWrong,
                TimingRightCount = timingRight,
                TimingWrongCount = timingWrong,
                OverallRightCount = overallRight,
                OverallWrongCount = overallWrong,
                RestRightCount = restRight,
                RestWrongCount = restWrong
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
                await SaveSessionResultAsync(apc);

                if (_sessionResultDb != null)
                {
                    var shortInstrument = _session.Instrument?.Split(',')[0].Trim() ?? "";
                    Utils.Log($"[LevelUpDebug] Calling CheckAndApplyLevelUpAsync: level={_session.ChildLevel}, instrument={shortInstrument}");
                    var newLevel = await Services.LevelUpService.CheckAndApplyLevelUpAsync(
                        _sessionResultDb, _session.ChildLevel, shortInstrument);

                    if (newLevel.HasValue)
                    {
                        Utils.Log($"[LevelUpDebug] Level up! New level={newLevel.Value}");
                        DifficultyLevelMapper.PickAndApplyToSession(
                            newLevel.Value, _session, forceClassicMode: false);
                        _session.ChildLevel = newLevel.Value;
                        UpdateChildLevelSliderDisplay();
                        UpdateKeyPickerSelection();
                        UpdateScaleTunePicker();
                        UpdateConcertKeyLabel();
                        await RefreshDisplayForLevelChangeAsync();
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
        ///   • Sourced from NoteSessionService.GetTimingAccuracyPercent() — computed via
        ///     least-squares onset fitting.
        ///   • Null when fewer than 3 notes were played (insufficient for regression).
        ///
        /// FUTURE (level-up criteria): after saving, query
        ///   var recent = await _sessionResultDb.GetByLevelAsync(_session.ChildLevel);
        ///   and check whether the last N sessions all exceed a target accuracy.
        /// </summary>
        private async Task SaveSessionResultAsync(double pitchAccuracyPercent)
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

                // Get timing accuracy from least-squares onset fitting
                double? timingAccuracyPercent = _session.GetTimingAccuracyPercent();
                int? detectedBpm = _session.GetDetectedBpm();

                // Blend pitch and timing into overall accuracy.
                // When timing data is unavailable (< 3 notes), fall back to pitch only.
                double overallAccuracy = timingAccuracyPercent.HasValue
                    ? (pitchAccuracyPercent + timingAccuracyPercent.Value) / 2.0
                    : pitchAccuracyPercent;

                var (pitchRight, pitchWrong, timingRight, timingWrong,
                     overallRight, overallWrong, restRight, restWrong) = _session.GetSessionSummaryCounts();

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
                    TimingAccuracyPercent  = timingAccuracyPercent,
                    DetectedBpm            = detectedBpm,
                    OverallAccuracyPercent = overallAccuracy,
                    PitchRightCount        = pitchRight,
                    PitchWrongCount        = pitchWrong,
                    TimingRightCount       = timingRight,
                    TimingWrongCount       = timingWrong,
                    OverallRightCount      = overallRight,
                    OverallWrongCount      = overallWrong,
                    RestRightCount         = restRight,
                    RestWrongCount         = restWrong,
                };

                await _sessionResultDb.InsertAsync(result);

                Utils.Log($"[SessionResult] Saved: Level={result.Level}, " +
                          $"Correct={result.CorrectPitchCount}/{result.TotalNotes}, " +
                          $"Pitch={result.PitchAccuracyPercent:F1}%, " +
                          $"AvgCents={result.AveragePitchErrorCents:F1}, " +
                          $"Timing={result.TimingAccuracyPercent?.ToString("F1") ?? "N/A"}%");
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
                if (_isPageVisible && !_suppressSessionRegenerate)
                {
                    await RegenerateNotesAsync();
                    UpdateKeyPickerVisibility();
                }
            }

            if (e.PropertyName == nameof(_session.SelectedScale) ||
                e.PropertyName == nameof(NoteSessionService.Key) ||
                e.PropertyName == nameof(NoteSessionService.Instrument) ||
                e.PropertyName == nameof(NoteSessionService.Tune) ||
                e.PropertyName == nameof(NoteSessionService.CurrentTune) ||
                e.PropertyName == nameof(NoteSessionService.SelectedArpeggioDisplay))
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
                if (!_suppressSessionRegenerate)
                    await RegenerateNotesAsync();
            }

            if (e.PropertyName == nameof(NoteSessionService.MusicBpm)
                && _session.StaffDisplayMode == StaffDisplayMode.V3)
            {
                _v3Drawable?.InvalidateLayoutCache();
                MainThread.BeginInvokeOnMainThread(() => V3StaffGraphicsView?.Invalidate());
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
            _suppressPickerSync = true;
            try
            {
                if (idx >= 0 && KeyPicker.SelectedIndex != idx)
                    KeyPicker.SelectedIndex = idx;
                if (idx >= 0 && _v3HomeKeyPicker?.SelectedIndex != idx)
                    _v3HomeKeyPicker!.SelectedIndex = idx;
            }
            finally
            {
                _suppressPickerSync = false;
            }
        }

        private void MarkChildKeyScaleOverrideIfNeeded()
        {
            if (_session.ChildLevel > 0)
                _session.MarkChildPracticeSettingsCustomized();
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
            // V3-only: scale/key/instrument pickers live on the What to Play page.
            PickersContainer.IsVisible = false;
        }


        private void UpdateTunerVisibility()
        {
            var isTuner = _session.Tune == "Tuner";

            // V3-only: hide legacy Classic staff panel; show V3 or tuner UI.
            StaffBorder.IsVisible   = false;
            V3StaffBorder.IsVisible = !isTuner;

            TunerGrid.IsVisible = isTuner;

            OnPropertyChanged(nameof(IsChildLevelSliderVisible));
            OnPropertyChanged(nameof(IsV3BottomPickersVisible));
            OnPropertyChanged(nameof(IsBottomButtonRowVisible));

            if (isTuner)
            {
                TunerBorder.IsVisible = true;
                TunerGrid.ColumnDefinitions[0] = new ColumnDefinition(GridLength.Star);

                _session.SessionCompleted = false;
                ApplyTunerHeight();
                Dispatcher.Dispatch(ApplyTunerHeight);
                TunerGraphicsView.Invalidate();
                if (!_isRunning)
                {
                    _ = StartListeningAndEvaluatingAsync();
                }
            }
            else
            {
                TunerBorder.IsVisible = false;
                StaffAreaStack.HeightRequest = -1;
                TunerGrid.HeightRequest = -1;
                TunerBorder.HeightRequest = -1;
                TunerGraphicsView.HeightRequest = -1;
                TunerInfoBorder.HeightRequest = -1;
                MainPageMainLayout.Spacing = 16;
            }
        }

        private string[] BuildScaleTuneOptions()
        {
            var practiceTuneTitles = musicmate.Models.TuneLibrary.All.Select(t => t.Title).ToArray();
            var arpeggioTitles = BuildArpeggioPickerChoices().Select(choice => choice.Label).ToArray();
            return new[] { "Tuner" }
                .Concat(practiceTuneTitles)
                .Concat(arpeggioTitles)
                .Concat(NoteSessionService.AvailableScales)
                .ToArray();
        }

        private IReadOnlyList<ArpeggioPickerChoice> BuildArpeggioPickerChoices()
        {
            _arpeggioPickerChoices.Clear();
            int level = _session.ChildLevel > 0 ? _session.ChildLevel : 1;
            var availability = ArpeggioCatalog.GetAvailabilityForLevel(level);
            var choices = new List<ArpeggioPickerChoice>();

            foreach (var root in availability.RootOptions)
            {
                int rootMidi = GetScaleDegreeMidi(_session.Key, _session.SelectedScale, root.ScaleDegree);
                foreach (var pattern in availability.Patterns)
                {
                    string rootNote = ChooseArpeggioRootInRange(rootMidi);
                    string rootName = TrimOctave(rootNote);
                    string label = $"{rootName} {pattern.DisplayName.ToLowerInvariant()}";
                    if (_arpeggioPickerChoices.ContainsKey(label))
                        continue;

                    var choice = new ArpeggioPickerChoice(label, pattern, rootNote);
                    _arpeggioPickerChoices[label] = choice;
                    choices.Add(choice);
                }
            }

            return choices;
        }

        private string ChooseArpeggioRootInRange(int rootMidi)
        {
            int minMidi = NoteSessionService.NoteNameToMidi(_session.LowestNote);
            int maxMidi = NoteSessionService.NoteNameToMidi(_session.HighestNote);
            bool preferFlats = KeyPrefersFlats(_session.Key);
            if (minMidi < 0 || maxMidi < minMidi)
                return NoteSessionService.MidiToNoteName(rootMidi, preferFlats);

            int candidate = rootMidi;
            while (candidate < minMidi)
                candidate += 12;
            while (candidate > maxMidi)
                candidate -= 12;

            return NoteSessionService.MidiToNoteName(candidate, preferFlats);
        }

        private static int GetScaleDegreeMidi(string key, string scale, int degree)
        {
            int[] intervals = scale switch
            {
                "Natural Minor" or "Aeolian" => new[] { 0, 2, 3, 5, 7, 8, 10 },
                "Harmonic Minor" => new[] { 0, 2, 3, 5, 7, 8, 11 },
                "Melodic Minor" or "Jazz Melodic Minor" => new[] { 0, 2, 3, 5, 7, 9, 11 },
                "Dorian" => new[] { 0, 2, 3, 5, 7, 9, 10 },
                "Phrygian" => new[] { 0, 1, 3, 5, 7, 8, 10 },
                "Lydian" => new[] { 0, 2, 4, 6, 7, 9, 11 },
                "Mixolydian" => new[] { 0, 2, 4, 5, 7, 9, 10 },
                "Locrian" => new[] { 0, 1, 3, 5, 6, 8, 10 },
                "Major Pentatonic" => new[] { 0, 2, 4, 7, 9, 12, 14 },
                "Minor Pentatonic" => new[] { 0, 3, 5, 7, 10, 12, 15 },
                "Blues" or "Minor Blues" => new[] { 0, 3, 5, 6, 7, 10, 12 },
                _ => new[] { 0, 2, 4, 5, 7, 9, 11 }
            };

            int tonicMidi = NoteSessionService.NoteNameToMidi($"{key}4");
            int idx = Math.Clamp(degree, 1, 7) - 1;
            return tonicMidi + intervals[idx % intervals.Length];
        }

        private static bool KeyPrefersFlats(string key)
            => key is "F" or "Bb" or "Eb" or "Ab" or "Db" or "Gb" or "Cb";

        private static string TrimOctave(string noteName)
            => new(noteName.TakeWhile(c => !char.IsDigit(c)).ToArray());

        private void UpdateScaleTunePicker()
        {
            var items = BuildScaleTuneOptions();
            ScaleTunePicker.ItemsSource = items;
            if (_v3HomeScaleTunePicker != null)
                _v3HomeScaleTunePicker.ItemsSource = items;
            var selection = _session.Tune == "Tuner" ? "Tuner"
                : _session.Tune == "Practice Tune" ? (_session.CurrentTune?.Title ?? string.Empty)
                : _session.Tune == "Arpeggio" ? _session.SelectedArpeggioDisplay
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
            if (_suppressPickerSync) return;
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
            MarkChildKeyScaleOverrideIfNeeded();
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

            if (_arpeggioPickerChoices.TryGetValue(selected, out var arpeggioChoice))
            {
                _lastValidScaleTuneIndex = sourcePicker.SelectedIndex;
                _session.SelectArpeggio(arpeggioChoice.Pattern, arpeggioChoice.RootNote, arpeggioChoice.Label);
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
            MarkChildKeyScaleOverrideIfNeeded();
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
            if (_suppressPickerSync) return;
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
            MarkChildKeyScaleOverrideIfNeeded();
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

