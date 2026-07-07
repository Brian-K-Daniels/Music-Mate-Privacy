using CommunityToolkit.Maui.Alerts;
using Microsoft.Maui.Controls.Shapes;
using musicmate.Controls;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;
using musicmate.Diagnostics;
using musicmate.LayoutDebug;
using musicmate.Utilities;
using Microsoft.Maui.Graphics;
using SkiaSharp;
using System.ComponentModel;
using System.Diagnostics;
//  2026.06.12 1516  Just so I can do a commit before using the long prompt for sustained notes and rests.
namespace musicmate.Pages
{
    public partial class MusicPage : ContentPage
    {
        private readonly NoteSessionService _session = null!;
        private readonly IAudioCaptureService _audio = null!;
        private readonly IAudioPlaybackService _player = null!;
        
        private StaffDrawable? _staffDrawable;
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
        private bool _dismissedResultBannerForFirstSound;
        private bool _isProgrammaticColorConfirm = false;
        private bool _isPageVisible = false;
        // A new GUID is assigned each time a session starts (see StartListeningAndEvaluatingAsync).
        // It is stored with every NoteAttempt so attempts can be grouped by session.
        private string _currentSessionId = string.Empty;
        private bool _applyingArpeggioSelection;
        private readonly SemaphoreSlim _regenerateSemaphore = new SemaphoreSlim(1, 1);
        private int _pickerSyncSuppressCount;
        private bool IsPickerSyncSuppressed => _pickerSyncSuppressCount > 0;
        private string? _savedInstrumentForPlayback = null;
        private int _savedInstrumentIndexForPlayback = -1;

        private bool _practicePickerEventsWired;

        // fields for inactivity tracking
        private DateTime _lastHeardTime = DateTime.UtcNow;
#pragma warning disable CS0414
        private readonly TimeSpan _inactivityTimeout = TimeSpan.FromMinutes(5);

        // When true, RegenerateNotesAsync is suppressed so the post-autoplay
        // green feedbacks and session stats remain visible until the next session.
        private bool _freezeStaff = false;

        /// <summary>Session completion was triggered after Play playback — skip result banner.</summary>
        private bool _completionFromPlayback;

        // When true, the session result banner is being shown after a child-Practice
        // session completed with AutoRepeat off.  Blocks auto-start until the user
        // leaves the page (e.g. opens Settings) or taps Start/Stop.
        private bool _holdResultForChildSession = false;
        private string? _sessionEndMarqueeMessage;
        private bool _deferNewLevelMarqueeUntilBannerDismissed;
        private string? _pendingInstrumentForMarquee;
        private CancellationTokenSource? _autoStartCts;
        private CancellationTokenSource? _sessionStartCts;
        private bool _suppressSessionRegenerate;
        private const string ChildLevelPrefKey = "ChildPractice.Level";
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
                var panelColor = _theme_service?.PanelBackgroundColor ?? Colors.White;
                StaffBorder.Background = new SolidColorBrush(panelColor);
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

        private PracticeSessionSnapshot? _repeatSameSnapshot;

        public string? Tune => _session?.Tune;
        public bool AutoRepeat
        {
            get => _session.AutoRepeat;
            set => _session.AutoRepeat = value;
        }

        public bool RepeatSameTune
        {
            get => _session.RepeatSameTune;
            set => _session.RepeatSameTune = value;
        }

        private void UpdateAutoRepeatButtons()
        {
            // Auto-repeat button styling is handled on WhatToPlayPage.
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
            var isRandom = _session?.IsRandomMode == true;
            IsScaleRepeatButtonVisible = _isAutoRepeatVisible && !isRandom;
            UpdatePlayButtonVisibility();
            Dispatcher.Dispatch(UpdateTitlePlayButtonPosition);
        }

        public bool IsTitlePlayButtonVisible => !_isRunning || _isPlaying;
        public bool IsBottomPickersVisible => _session?.Tune != "Tuner";
        public bool IsBottomButtonRowVisible => _session?.Tune != "Tuner";
        public bool IsChildLevelSliderVisible => _session?.ChildLevel > 0 && _session.Tune != "Tuner";

        public bool IsEffectiveScaleLabelVisible =>
            _session?.IsRandomMode == true
            || _session?.ScaleSelectionMode == ScaleSelectionMode.Random;

        public string EffectiveScaleLabelText => _session?.EffectiveScaleDisplay ?? string.Empty;

        private void UpdateEffectiveScaleLabel()
        {
            OnPropertyChanged(nameof(IsEffectiveScaleLabelVisible));
            OnPropertyChanged(nameof(EffectiveScaleLabelText));
            if (_session?.IsRandomMode == true)
            {
                UpdatePracticePlayItemPickerCore();
                SyncPlayItemStatusMessage();
            }
        }

        /// <summary>
        /// Keeps the title status bar aligned with the random-mode picker label
        /// without clobbering live pitch-feedback messages.
        /// </summary>
        private void SyncPlayItemStatusMessage()
        {
            if (_session.Tune == "Tuner")
            {
                StatusService.Instance.StatusMessage = _isRunning
                    ? "Listening…"
                    : "Stopped listening.";
                return;
            }

            var msg = StatusService.Instance.StatusMessage;
            if (!string.IsNullOrEmpty(msg) && (
                    msg.StartsWith("Expected:", StringComparison.Ordinal) ||
                    msg.StartsWith("Playing note", StringComparison.Ordinal) ||
                    msg.Contains('¢', StringComparison.Ordinal)))
                return;

            StatusService.Instance.StatusMessage = GetCurrentPlayItemName();
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
        private int _lastValidPlayItemIndex = 0;

        private enum PlayModeCategory
        {
            Tunes,
            Scales,
            Arpeggios,
            Other
        }
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
        

        private void UpdateInstrumentPickerVisibility()
        {
            // After selection, always show only the label with the short string
            IsInstrumentPickerVisible = false;
            IsInstrumentLabelVisible = true;
        }

        public MusicPage()
        {
            try
            {
                InitializeComponent();

                UpdateTitleStartStopButtonVisual(false);

                // Ensure ThemeService is available so we can deploy saved/default panel background
                _theme_service = ServiceHelper.GetService<ThemeService>()!;

                // Apply saved panel background (or default) before the Practice page is shown
                DeploySavedPanelBackground();

                // disable iOS safe area for this page (use per-edge API available on this MAUI version)
                // Use reflection helper so the project compiles on non-iOS targets
                musicmate.Utilities.Utils.DisableIosSafeArea(this);

                BackgroundColor = Colors.White;
                _orientation = ServiceHelper.GetService<IOrientationService>()!;
                _session = ServiceHelper.GetService<NoteSessionService>()!;
                PlayModePickerOptions.MigrateLegacySessionSelection(_session);
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
                                DebugLog.WriteLine("[MusicPage] MaxBlocksReached after session completed: full restart (AutoRepeat on).");
                                await StartListeningAndEvaluatingAsync(
                                    forceNewNotes: PracticeSessionLifecycle.ShouldForceNewNotesForRepeatMode(
                                        _session.RepeatSameTune),
                                    scaleKeyTrigger: "AutoStart");
                            }
                            else
                            {
                                DebugLog.WriteLine("[MusicPage] MaxBlocksReached after session completed: AutoRepeat off, not restarting.");
                            }
                        }
                        else
                        {
                            DebugLog.WriteLine("[MusicPage] MaxBlocksReached mid-session: restarting capture only.");
                            await RestartAudioCaptureAsync();
                        }
                    });
                };

                StatusLabelShell?.BindingContext = StatusService.Instance;
                _orientation.AllowAutorotate();

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

                var safeAreaService = ServiceHelper.GetService<ISafeAreaService>();
                _staffDrawable = new StaffDrawable(_session, _theme_service!, safeAreaService);
                StaffGraphicsView.Drawable = _staffDrawable;
                StaffGraphicsView.SetBinding(GraphicsView.BackgroundColorProperty, new Binding("PanelBackgroundColor"));
                if (TitleStartStopButton != null)
                {
                    TitleStartStopButton.SizeChanged += (_, _) => UpdateTitlePlayButtonPosition();
                    TitleStartStopButton.HandlerChanged += (_, _) => UpdateTitlePlayButtonPosition();
                }
                if (TitleMarqueeGrid != null)
                {
                    TitleMarqueeGrid.SizeChanged += (_, _) => UpdateTitlePlayButtonPosition();
                    TitleMarqueeGrid.HandlerChanged += (_, _) => UpdateTitlePlayButtonPosition();
                }
                StaffBorder.SizeChanged += (_, _) => UpdateTitlePlayButtonPosition();
                SizeChanged += (_, _) => UpdateTitlePlayButtonPosition();
                SetPlayButtonPlaying(false);

                // Tuner graphics setup
                TunerBorder.BindingContext = _theme_service;
                TunerInfoBorder.SetBinding(Border.BackgroundColorProperty,
                    new Binding("PanelBackgroundColor", source: _theme_service));
                TunerGraphicsView.BindingContext = _theme_service;
                TunerGraphicsView.Drawable = _staffDrawable;
                TunerGraphicsView.SetBinding(GraphicsView.BackgroundColorProperty, new Binding("PanelBackgroundColor"));
                MainPageRootGrid.SizeChanged += (_, _) =>
                {
                    if (_session?.Tune == "Tuner")
                        ApplyTunerHeight();
                };

                // ColorPickerDialog event: update theme color for all pages
                ColorPickerDialog.AppColorPicked += (s, e) =>
                {
                    _theme_service?.SetColor(e.Target, e.Color);
                    if (e.Target == AppColorTarget.PanelBackground)
                        StaffBorder.Background = new SolidColorBrush(e.Color);
                };

                ColorPickerDialog.DialogClosed += async (_, _) =>
                {
                    if (!_isProgrammaticColorConfirm)
                        await StartListeningAndEvaluatingAsync();
                };

                _theme_service?.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(ThemeService.PanelBackgroundColor))
                    {
                        StaffBorder.Background = new SolidColorBrush(_theme_service.PanelBackgroundColor);
                        StaffGraphicsView.Invalidate();
                        ApplyStaffHeight();
                    }
                };

                // Session completed event
                _session.SessionCompletedAsync += async () =>
                {
                    try
                    {
                        // Mid-session staff refreshes are handled by SyncStaffNoteStates; a
                        // SessionCompletedAsync callback means every note was played, so always
                        // run the summary / AutoRepeat restart flow below.

                        await UpdateNoteStatsDatabaseAsync();

                        string instrumentKeyForMarquee = _session.InstrumentKey;
                        int levelBeforeSave = _session.ChildLevel;
                        var countSinceBeforeSave = Services.LevelUpService.CountSinceUtc;

                        var summary = PracticeSessionLifecycle.CaptureCompletionSummary(_session);

                        // Rolling per-note attempt history runs unconditionally,
                        // independent of the CollectNoteStats preference.
                        // Save session summary before attempt rows are cleared.
                        int? newChildLevel = await SaveSessionStatAsync();
                        await SaveNoteAttemptsForSessionAsync(levelBeforeSave);

                        if (_completionFromPlayback)
                        {
                            _completionFromPlayback = false;
                            _session.SessionCompleted = true;
                            try { _audio.StopCapture(); } catch { }
                            SetButtonStates(false);
                            _holdResultForChildSession = false;
                            return;
                        }

                        // Show result banner only when a level-up occurs.
                        if (newChildLevel.HasValue)
                        {
                            await MainThread.InvokeOnMainThreadAsync(() =>
                            {
                                SessionResultLabel.Text = PracticeSessionLifecycle.FormatSessionResultBanner(
                                    summary, newChildLevel);
                                SessionResultBanner.IsVisible = true;
                            });
                        }

                        // Level-up progress: qualifying sessions at current level since start / last level-up
                        try
                        {
                            if (newChildLevel.HasValue)
                            {
                                // Keep the completed-level marquee while the congratulatory banner is visible.
                                _deferNewLevelMarqueeUntilBannerDismissed = true;
                                _pendingInstrumentForMarquee = instrumentKeyForMarquee;
                                await BuildAndPublishSessionEndMarqueeAsync(
                                    levelBeforeSave, instrumentKeyForMarquee, countSinceBeforeSave);
                            }
                            else
                            {
                                await BuildAndPublishSessionEndMarqueeAsync(
                                    _session.ChildLevel, instrumentKeyForMarquee, Services.LevelUpService.CountSinceUtc);
                            }
                        }
                        catch (Exception ex)
                        {
                            StatusService.Instance.StatusMessage = $"LevelUp Progress: (error: {ex.Message})";
                        }
                        _session.SessionCompleted = true;

                        if (PracticeSessionLifecycle.ShouldAutoRepeat(AutoRepeat, _session.Tune ?? string.Empty))
                        {
                            double repeatDelay = Preferences.Default.Get("RepeatDelaySeconds", 2.0);
                            await Task.Delay(PracticeSessionLifecycle.GetAutoRepeatDelayMs(repeatDelay));
                            _holdResultForChildSession = false;
                            _session.SessionCompleted = false;

                            // Repeat New must force fresh generation at the current child level;
                            // Repeat Same restores the saved snapshot.
                            await StartListeningAndEvaluatingAsync(
                                forceNewNotes: PracticeSessionLifecycle.ShouldForceNewNotesForRepeatMode(
                                    _session.RepeatSameTune),
                                scaleKeyTrigger: "AutoStart");
                        }
                        else
                        {
                            _audio.StopCapture();
                            SetButtonStates(false);
                            // For child-Practice sessions, keep the result banner visible only when level increased;
                            // block OnAppearing from auto-starting until the user acts.
                            if (_session.ChildLevel > 0 && newChildLevel.HasValue)
                                _holdResultForChildSession = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLog.WriteLine($"Session completion error: {ex}");
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

                    if (e.PropertyName == nameof(NoteSessionService.ShowConductorCues)
                        || e.PropertyName == nameof(NoteSessionService.NoteNameDisplay))
                    {
                        StaffGraphicsView?.Invalidate();
                    }
                };

                IsAutoRepeatVisible = _session.Tune != "Tuner";
                UpdateRepeatButtonsVisibility();

                // Show instrument names only; the session maps each name to InstrumentKey internally.
                var instrumentOptions = NoteSessionService.InstrumentOptions.Cast<string>().ToArray();
                InstrumentPicker.ItemsSource = instrumentOptions;
                var instrumentShort = _session.InstrumentDisplayName;
                var selectedIndex = Array.FindIndex(instrumentOptions, s => s == instrumentShort);
                InstrumentPicker.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
                _session.Instrument = instrumentOptions[InstrumentPicker.SelectedIndex];
                SelectedInstrumentShort = _session.InstrumentDisplayName;

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

                PlayModePickerOptions.MigrateLegacySelectedTunePreference();
                var savedTune = PlayModePickerOptions.NormalizeRhythmNoteTunePreference(
                    Preferences.Default.Get<string?>("SelectedTune", null));
                if (!string.IsNullOrEmpty(savedTune) && savedTune == "Tuner")
                    _session.Tune = savedTune;
                else if (PlayModePickerOptions.IsRhythmNoteTuneSelection(savedTune))
                {
                    LayoutTestTune.SetEnabled(true);
                    PlayModePickerOptions.ApplyRhythmNoteTuneSelection(_session);
                }
                else if (!string.IsNullOrEmpty(savedTune)
                         && practiceTuneTitles.Contains(savedTune))
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

                var initialScaleTuneSelection = LayoutTestTune.IsEnabled
                    ? PlayModePickerOptions.HalfThroughSixteenthNotes
                    : _session.Tune == "Tuner" ? "Tuner"
                    : _session.Tune == "Practice Tune" ? (_session.CurrentTune?.Title ?? practiceTuneTitles[0])
                    : _session.Tune == "Arpeggio" ? _session.SelectedArpeggioDisplay
                    : _session.SelectedScale;
                var scaleTuneIdx = Array.IndexOf(scaleTuneOptions, initialScaleTuneSelection);
                ScaleTunePicker.SelectedIndex = scaleTuneIdx >= 0 ? scaleTuneIdx : 0;
                _lastValidScaleTuneIndex = ScaleTunePicker.SelectedIndex;
                IsAutoRepeatVisible = _session.Tune != "Tuner";

                ScaleTunePicker.SelectedIndexChanged += OnScaleTunePickerChanged;

                KeyPicker.SelectedIndexChanged += OnKeyPickerChangedWithPrompt;

                InitializePracticePickers(instrumentOptions);

                // Apply initial pickers-row visibility based on the loaded display mode
                UpdatePickersContainerVisibility();
                UpdateKeyPickerSelection();
                UpdateConcertKeyLabel();
                UpdateKeyPickerVisibility();
            }
            catch (Exception ex)
            {
                Utils.Log($"[MusicPage Constructor] ERROR: {ex}");
                DebugLog.WriteLine($"[MusicPage Constructor] ERROR: {ex}");
                throw;
            }
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
                while (StaffGraphicsView != null && StaffGraphicsView.Width <= 0
                       && sw.ElapsedMilliseconds < 1500)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(40, ct);
                }

                await Task.Delay(150, ct);
                ct.ThrowIfCancellationRequested();

                if (!_session.AutoStart || _session.Tune == "Tuner"
                    || _holdResultForChildSession || _isRunning)
                    return;

                await StartListeningAndEvaluatingAsync(scaleKeyTrigger: "AutoStart");
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer navigation or page hide.
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[AutoStart] ERROR: {ex}");
            }
        }

        private async Task RegenerateNotesAsync()
        {
            // Repeat Same keeps the saved exercise unless the user changes key/scale/tune.
            if (_session.RepeatSameTune
                && _repeatSameSnapshot?.Notes.Count > 0
                && !_suppressSessionRegenerate)
            {
                DebugLog.WriteLine("[RepeatSame] Skipping RegenerateNotesAsync — restoring saved exercise");
                await RestoreRepeatSameSnapshotAsync(_repeatSameSnapshot.Notes);
                return;
            }

            // Wait for any in-flight regeneration — never skip after session Reset() cleared notes.
            await _regenerateSemaphore.WaitAsync();
            try
            {
                using (PracticeSessionStartProfiler.Scope("RegenerateNotes.Banner"))
                    await HideSessionResultBannerAsync(refreshMarqueeForNewLevel: true);
                _holdResultForChildSession = false;

                if (_session.IsRandomMode)
                    _session.EnsureRandomModeGenerationSettings();
                else if (_session.ChildLevel > 0)
                    DifficultyLevelMapper.ApplyLevelDerivedSettings(_session.ChildLevel, _session);

                _session.PrepareEffectiveScaleForGeneration(_generationSeed);
                UpdateEffectiveScaleLabel();

                // While showing post-autoplay results, do not overwrite the staff.
                if (_freezeStaff)
                    return;

                _generationSeed = unchecked(_generationSeed + 1);

                if (_session.Tune == "Tuner")
                {
                    UpdateTunerStaffDisplay();
                    UpdateTunerVisibility();
                    return;
                }

                UpdateTunerVisibility();

                StaffBorder.IsVisible = true;

                using (PracticeSessionStartProfiler.Scope("RegenerateNotes.ViewWidthWait"))
                {
                    var sw2 = System.Diagnostics.Stopwatch.StartNew();
                    while (StaffGraphicsView?.Width <= 0 && sw2.ElapsedMilliseconds < 500)
                        await Task.Delay(20);
                }

                using (PracticeSessionStartProfiler.Scope("RegenerateNotes.StaffDisplay"))
                    await UpdateStaffDisplayAsync();

#if DEBUG
                if (_session.IsRandomMode)
                {
                    var names = string.Join(", ", _session.NotesToDraw.Select(n => n.Name));
                    DebugLog.WriteLine($"[Random] {_session.EffectiveScaleDisplay} → {_session.NotesToDraw.Count} notes: {names}");
                }
#endif

                if (_session.Tune == "Arpeggio" || _session.IsRandomMode)
                    SyncPlayItemStatusMessage();
            }
            finally
            {
                _regenerateSemaphore.Release();
            }
        }

        // ── Sequence generation ────────────────────────────────────────────────

        /// <summary>How many measures to generate at once (initial fill and each top-up).</summary>
        private const int DefaultMeasureBatchSize = 8;

        private int GetMeasureBatchSize()
            => _session.ChildLevel > 0 && _session.ChildMeasureBatchSize > 0
                ? _session.ChildMeasureBatchSize
                : DefaultMeasureBatchSize;

        /// <summary>
        /// Level-aware measure counts for each staff.  Child levels start with a
        /// single upper-staff measure and grow toward <see cref="MeasuresPerStaff"/>.
        /// </summary>
        private (int upper, int lower) GetStaffMeasureCounts()
        {
            if (_session.ChildLevel <= 0)
                return (MeasuresPerStaff, MeasuresPerStaff);

            int level = _session.ChildLevel;

            if (level <= 5)
                return (1, 0);

            if (level <= 15)
            {
                int batch = GetMeasureBatchSize();
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
            return (MeasuresPerStaff, MeasuresPerStaff);
        }

        // Generator offsets: updated every time we append more measures.
        private int _seqNextMeasureIndex = 0;
        private double _seqNextBeatOffset = 0.0;
        private int _seqNextGlobalNoteIndex = 0;

        /// <summary>Cached excluded MIDI set rebuilt whenever a new sequence starts.</summary>
        private HashSet<int> _excludedMidis = new();

        /// <summary>
        /// Loads the set of mastered MIDI numbers from the note database using the
        /// same logic as <see cref="NoteSessionService.BuildRandomSequenceAsync"/>.
        /// </summary>
        private async Task LoadExcludedMidisAsync()
        {
            _excludedMidis = _session.IsRandomMode
                ? await _session.GetMasteredMidiNumbersAsync()
                : new HashSet<int>();
        }

        /// <summary>
        /// Builds a <see cref="MusicSequenceGenerator"/> configured with the current
        /// session parameters, append offsets, and mastery exclusions.
        /// </summary>
        private MusicSequenceGenerator BuildSequenceGenerator(int measureCount, int startPrevPitch = -1)
        {
            // Translate persisted string settings to model types.
            var timeSig = _session.MeterTimeSignature switch
            {
                "3/4" => TimeSignature.ThreeFour,
                "2/4" => TimeSignature.TwoFour,
                _ => TimeSignature.FourFour
            };

            // Selected-scale practice (Major, etc. from What to Play) is a straight
            // quarter-note scale walk — no rests, halves, or mixed rhythm.
            bool simpleSelectedScale = _session.Tune == "Selected Scale" && !_session.IsRandomMode;

            int rhythmVariety = simpleSelectedScale
                ? 0
                : _session.RhythmVarietyPercent >= 0
                    ? _session.RhythmVarietyPercent
                    : _session.RhythmMode == "Mixed" ? 60 : 0;

            var gen = new MusicSequenceGenerator
            {
                Key = _session.Key,
                Scale = _session.GenerationScale,
                LowestNote = _session.LowestNote,
                HighestNote = _session.HighestNote,
                TimeSignature = timeSig,
                MeasureCount = measureCount,
                RhythmVarietyPercent = rhythmVariety,
                SmallestDuration = simpleSelectedScale
                    ? NoteDuration.Quarter
                    : _session.SmallestRhythmNote switch
                    {
                        "Sixteenth" => NoteDuration.Sixteenth,
                        "Eighth" => NoteDuration.Eighth,
                        _ => NoteDuration.Quarter
                    },
                StartMeasureIndex = _seqNextMeasureIndex,
                StartBeatOffset = _seqNextBeatOffset,
                StartGlobalNoteIndex = _seqNextGlobalNoteIndex,
                StartPrevPitch = startPrevPitch,
                ExcludedMidiNumbers = _excludedMidis,
                UseScaleOrder = !_session.IsRandomMode,
                ScaleWalkOffset = _seqNextGlobalNoteIndex,
                AccidentalPercent = _session.IsRandomMode ? _session.AccidentalPercent : 0,
                MaxMelodicIntervalSemitones = _session.IsRandomMode ? _session.MaxMelodicIntervalSemitones : 0,
                SyncopationLevel = simpleSelectedScale
                    ? SyncopationLevel.None
                    : SyncopationLevelHelper.Parse(_session.SyncopationSetting),
                RestChancePercent = simpleSelectedScale ? 0 : _session.PracticeRestChancePercent,
                RandomSeed = Environment.TickCount
                                           ^ _generationSeed
                                           ^ (_session.ChildLevel * 7919)
            };
            DebugLog.WriteLine($"[StaffGen] Tune={_session.Tune} Random={_session.IsRandomMode} SimpleScale={simpleSelectedScale} AccPct={_session.AccidentalPercent} EffectiveAccPct={(_session.IsRandomMode ? _session.AccidentalPercent : 0)}");
            return gen;
        }

        private sealed class StandardStaffGenResult
        {
            public required List<GeneratedNote> UpperFlat { get; init; }
            public required List<double> UpperBarBeats { get; init; }
            public required List<GeneratedNote> LowerFlat { get; init; }
            public required List<double> LowerBarBeats { get; init; }
            public required int SeqNextMeasureIndex { get; init; }
            public required double SeqNextBeatOffset { get; init; }
            public required int SeqNextGlobalNoteIndex { get; init; }
            public required int LowerMeasureIndex { get; init; }
            public required double LowerBeatOffset { get; init; }
            public required int LowerGlobalNoteIndex { get; init; }
        }

        /// <summary>CPU-only staff sequence generation (safe to run off the UI thread).</summary>
        private StandardStaffGenResult BuildStandardStaffNoteLists(int upperMc, int lowerMc)
        {
            var existingUpper = new HashSet<double>();
            var existingLower = new HashSet<double>();

            var genUpper = BuildSequenceGenerator(upperMc);
            var upperMeasures = genUpper.GenerateSequence();
            var upperFlat = MusicSequenceGenerator.Flatten(upperMeasures);
            double measureBeats = genUpper.TimeSignature.TotalBeats;
            var upperBarBeats = ComputeStaffBarBeats(upperFlat, measureBeats, existingUpper);

            int seqNextMeasureIndex = upperMeasures.Count;
            double seqNextBeatOffset = upperMeasures.Count * (double)genUpper.TimeSignature.TotalBeats;
            int seqNextGlobalNoteIndex = upperFlat.Count(n => !n.IsRest);

            List<GeneratedNote> lowerFlat;
            List<double> lowerBarBeats;
            int lowerMeasureIndex;
            double lowerBeatOffset;
            int lowerGlobalNoteIndex;

            if (lowerMc > 0)
            {
                int lowerStartPitch = MusicSequenceGenerator.LastPitchedMidi(upperFlat);
                var genLower = BuildSequenceGenerator(lowerMc, lowerStartPitch);
                var lowerMeasures = genLower.GenerateSequence();
                lowerFlat = MusicSequenceGenerator.Flatten(lowerMeasures);

                double lowerBeatShift = lowerFlat.Count > 0 ? (lowerFlat[0].BeatPosition ?? 0.0) : 0.0;
                if (lowerBeatShift > 0.0)
                {
                    for (int i = 0; i < lowerFlat.Count; i++)
                    {
                        var n = lowerFlat[i];
                        lowerFlat[i] = new GeneratedNote
                        {
                            MidiNumber = n.MidiNumber,
                            Letter = n.Letter,
                            Octave = n.Octave,
                            Accidental = n.Accidental,
                            SpelledName = n.SpelledName,
                            TargetFrequency = n.TargetFrequency,
                            Duration = n.Duration,
                            IsRest = n.IsRest,
                            MeasureIndex = n.MeasureIndex,
                            BeatPosition = (n.BeatPosition ?? 0.0) - lowerBeatShift,
                            IsPlayedCorrectly = n.IsPlayedCorrectly
                        };
                    }
                }

                lowerBarBeats = ComputeStaffBarBeats(lowerFlat, measureBeats, existingLower);
                lowerMeasureIndex = seqNextMeasureIndex + lowerMeasures.Count;
                lowerBeatOffset = seqNextBeatOffset + lowerMeasures.Count * (double)genLower.TimeSignature.TotalBeats;
                lowerGlobalNoteIndex = seqNextGlobalNoteIndex + lowerFlat.Count(n => !n.IsRest);
                seqNextMeasureIndex = lowerMeasureIndex;
                seqNextBeatOffset = lowerBeatOffset;
                seqNextGlobalNoteIndex = lowerGlobalNoteIndex;
            }
            else
            {
                lowerFlat = new List<GeneratedNote>();
                lowerBarBeats = new List<double>();
                lowerMeasureIndex = seqNextMeasureIndex;
                lowerBeatOffset = seqNextBeatOffset;
                lowerGlobalNoteIndex = seqNextGlobalNoteIndex;
            }

            return new StandardStaffGenResult
            {
                UpperFlat = upperFlat,
                UpperBarBeats = upperBarBeats,
                LowerFlat = lowerFlat,
                LowerBarBeats = lowerBarBeats,
                SeqNextMeasureIndex = seqNextMeasureIndex,
                SeqNextBeatOffset = seqNextBeatOffset,
                SeqNextGlobalNoteIndex = seqNextGlobalNoteIndex,
                LowerMeasureIndex = lowerMeasureIndex,
                LowerBeatOffset = lowerBeatOffset,
                LowerGlobalNoteIndex = lowerGlobalNoteIndex,
            };
        }

        /// <summary>
        /// Converts a <see cref="PracticeTune"/> into a flat list of <see cref="GeneratedNote"/>
        /// with correct <see cref="GeneratedNote.BeatPosition"/>, <see cref="GeneratedNote.MeasureIndex"/>,
        /// and accidentals parsed from each note's spelled name.
        /// </summary>
        private static List<GeneratedNote> BuildNotesFromTune(PracticeTune tune, string? key = null, string? scale = null)
        {
            var (noteKey, noteScale) = NoteSessionService.ResolvePracticeTuneNotation(tune);
            if (string.IsNullOrWhiteSpace(tune.Key))
            {
                noteKey = key ?? noteKey;
                noteScale = scale ?? noteScale;
            }

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
                        var raw = mn.SpelledName.Trim();
                        char letter = char.ToUpperInvariant(raw[0]);
                        int octave = 4;
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
                        if (raw.Contains("##")) acc = Accidental.DoubleSharp;
                        else if (raw.Contains("bb")) acc = Accidental.DoubleFlat;
                        else if (raw.Contains('#')) acc = Accidental.Sharp;
                        else if (raw.Length > 1 && raw[1] == 'b') acc = Accidental.Flat;

                        // Apply the key signature: if the note has no explicit accidental,
                        // adjust the MIDI number for any flat/sharp implied by the key.
                        var adjustedMidi = NoteSessionService.ApplyKeySignatureToMidi(mn.SpelledName, mn.MidiNumber, noteKey, noteScale);
                        var (resolvedAcc, displayName) = NoteSessionService.ResolveAccidentalAndSpelling(
                            mn.SpelledName, adjustedMidi, letter, octave, noteKey, noteScale);
                        acc = resolvedAcc;

                        gn = new GeneratedNote
                        {
                            MidiNumber = adjustedMidi,
                            Letter = letter,
                            Octave = octave,
                            Accidental = acc,
                            SpelledName = displayName,
                            TargetFrequency = 440.0 * Math.Pow(2.0, (adjustedMidi - 69) / 12.0),
                            Duration = mn.Duration,
                            IsRest = false,
                            MeasureIndex = measureIndex,
                            BeatPosition = beatCursor,
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
        /// Bar lines every <paramref name="measureBeats"/> from the first note's beat
        /// through the staff content end. Used for written tunes with a fixed meter.
        /// </summary>
        private static List<double> ComputeRegularBarBeats(
            IReadOnlyList<GeneratedNote> notes,
            double measureBeats,
            HashSet<double> existingBarBeats)
        {
            var result = new List<double>();
            if (notes.Count == 0 || measureBeats <= 0)
                return result;

            double origin = double.PositiveInfinity;
            double contentEnd = 0;
            foreach (var n in notes)
            {
                double start = n.BeatPosition ?? 0.0;
                if (start < origin)
                    origin = start;
                double end = start + n.BeatDuration;
                if (end > contentEnd)
                    contentEnd = end;
            }

            if (double.IsPositiveInfinity(origin))
                origin = 0.0;

            for (double bar = origin + measureBeats; bar < contentEnd - 1e-6; bar += measureBeats)
            {
                if (!existingBarBeats.Contains(bar))
                {
                    result.Add(bar);
                    existingBarBeats.Add(bar);
                }
            }

            return result;
        }

        /// <summary>
        /// Bar beats every <paramref name="measureBeats"/> from accumulated note duration.
        /// Measure-index transitions are kept only when they add more boundaries than the
        /// regular grid (pickup/anacrusis); otherwise the meter grid wins.
        /// </summary>
        private static List<double> ComputeStaffBarBeats(
            IReadOnlyList<GeneratedNote> notes,
            double measureBeats,
            HashSet<double> existingBarBeats)
        {
            var fromMeasures = new List<double>();
            if (notes.Count > 0 && notes.Any(n => n.MeasureIndex.HasValue))
            {
                int prevMeasure = -1;
                foreach (var n in notes.OrderBy(n => n.BeatPosition ?? 0.0))
                {
                    int mi = n.MeasureIndex ?? (prevMeasure >= 0 ? prevMeasure : 0);
                    if (prevMeasure >= 0 && mi != prevMeasure && n.BeatPosition.HasValue)
                    {
                        double bp = n.BeatPosition.Value;
                        if (fromMeasures.Count == 0 || bp > fromMeasures[^1] + 1e-6)
                            fromMeasures.Add(bp);
                    }
                    prevMeasure = mi;
                }
            }

            var regular = ComputeRegularBarBeats(notes, measureBeats, existingBarBeats);

#if DEBUG
            if (fromMeasures.Count > 0 && regular.Count > fromMeasures.Count)
            {
                DebugLog.WriteLine(
                    $"[LayoutTest] ComputeStaffBarBeats: regular grid ({regular.Count}) replaces sparse measure-index ({fromMeasures.Count})");
            }
#endif

            if (regular.Count >= fromMeasures.Count && regular.Count > 0)
                return regular;

            if (fromMeasures.Count > 0)
            {
                var result = new List<double>();
                foreach (double bp in fromMeasures)
                {
                    if (!existingBarBeats.Contains(bp))
                    {
                        result.Add(bp);
                        existingBarBeats.Add(bp);
                    }
                }
                return result;
            }

            return regular;
        }

        private static List<GeneratedNote> ShiftStaffBeatPositions(
            IReadOnlyList<GeneratedNote> notes,
            double beatShift)
        {
            if (beatShift <= 1e-9)
                return notes.ToList();

            var shifted = new List<GeneratedNote>(notes.Count);
            foreach (var n in notes)
            {
                shifted.Add(new GeneratedNote
                {
                    MidiNumber = n.MidiNumber,
                    Letter = n.Letter,
                    Octave = n.Octave,
                    Accidental = n.Accidental,
                    SpelledName = n.SpelledName,
                    TargetFrequency = n.TargetFrequency,
                    Duration = n.Duration,
                    IsRest = n.IsRest,
                    MeasureIndex = n.MeasureIndex,
                    BeatPosition = (n.BeatPosition ?? 0.0) - beatShift,
                    IsPlayedCorrectly = n.IsPlayedCorrectly,
                    CentsDeviation = n.CentsDeviation,
                    RenderX = n.RenderX,
                });
            }
            return shifted;
        }

        // ── Two-staff display ──────────────────────────────────────────────────

        /// <summary>How many measures to put on each staff (non-child / high levels).</summary>
        private const int MeasuresPerStaff = 4;

        /// <summary>Bumped on each regeneration so child random tunes differ every time.</summary>
        private int _generationSeed;

        // Offsets for appending the lower staff content.
        private int _lowerMeasureIndex = 0;
        private double _lowerBeatOffset = 0.0;
        private int _lowerGlobalNoteIndex = 0;

        /// <summary>
        /// Pitched-note count on the upper staff when <see cref="NoteSessionService.NotesToDraw"/>
        /// was built.  The upper drawable may be replaced mid-session (lookahead refresh) with
        /// a different note count; session indices must stay tied to this value.
        /// </summary>
        private int _sessionUpperPitchCount = 0;

        /// <summary>
        /// Populates the staff drawable with an upper and lower staff worth of notes.
        /// Upper staff is played first; lower staff follows.
        /// </summary>
        private async Task UpdateStaffDisplayAsync()
        {
            if (_staffDrawable == null) return;
            try
            {
                // Reset queue offsets.
                _seqNextMeasureIndex = 0;
                _seqNextBeatOffset = 0.0;
                _seqNextGlobalNoteIndex = 0;
                _sessionUpperPitchCount = 0;

                List<GeneratedNote> upperFlat;
                List<double> upperBarBeats;
                List<GeneratedNote> lowerFlat;
                List<double> lowerBarBeats;
                var existingUpper = new HashSet<double>();
                var existingLower = new HashSet<double>();

                if (LayoutTestTune.IsEnabled)
                {
                    var testTune = LayoutTestTune.Create();
                    LayoutTestTune.LogContents(testTune);

                    var allNotes = BuildNotesFromTune(testTune, _session.Key, _session.SelectedScale);
                    int splitAt = testTune.Measures.Count / 2;
                    double splitBeat = 0.0;
                    for (int m = 0; m < splitAt && m < testTune.Measures.Count; m++)
                        foreach (var mn in testTune.Measures[m].Notes)
                            splitBeat += mn.Duration.ToBeatValue();

                    upperFlat = allNotes.Where(n => (n.BeatPosition ?? 0) < splitBeat).ToList();
                    lowerFlat = ShiftStaffBeatPositions(
                        allNotes.Where(n => (n.BeatPosition ?? 0) >= splitBeat).ToList(),
                        splitBeat);
                    double measureBeats = testTune.TimeSignature.TotalBeats;
                    upperBarBeats = ComputeStaffBarBeats(upperFlat, measureBeats, existingUpper);
                    lowerBarBeats = ComputeStaffBarBeats(lowerFlat, measureBeats, existingLower);

                    _seqNextMeasureIndex = testTune.Measures.Count;
                    _seqNextBeatOffset = allNotes.Sum(n => n.BeatDuration);
                    _seqNextGlobalNoteIndex = allNotes.Count(n => !n.IsRest);
                    _lowerMeasureIndex = _seqNextMeasureIndex;
                    _lowerBeatOffset = _seqNextBeatOffset;
                    _lowerGlobalNoteIndex = _seqNextGlobalNoteIndex;
                    if (_staffDrawable != null) _staffDrawable.UpperHasEndBar = false;

                    StatusService.Instance.StatusMessage =
                        "Layout test tune (see debug log for bar beats)";
                }
                else if (_session.Tune == "Practice Tune" && _session.CurrentTune != null)
                {
                    // Split tune measures between upper and lower staff.
                    var allNotes = BuildNotesFromTune(_session.CurrentTune);
                    var allMeasures = _session.CurrentTune.Measures.Count;
                    int splitAt = allMeasures / 2;

                    // Gather beat threshold for split.
                    double splitBeat = 0.0;
                    for (int m = 0; m < splitAt && m < _session.CurrentTune.Measures.Count; m++)
                        foreach (var mn in _session.CurrentTune.Measures[m].Notes)
                            splitBeat += mn.Duration.ToBeatValue();

                    upperFlat = allNotes.Where(n => (n.BeatPosition ?? 0) < splitBeat).ToList();
                    lowerFlat = ShiftStaffBeatPositions(
                        allNotes.Where(n => (n.BeatPosition ?? 0) >= splitBeat).ToList(),
                        splitBeat);
                    double measureBeats = _session.CurrentTune.TimeSignature.TotalBeats;
                    upperBarBeats = ComputeStaffBarBeats(upperFlat, measureBeats, existingUpper);
                    lowerBarBeats = ComputeStaffBarBeats(lowerFlat, measureBeats, existingLower);

                    _seqNextMeasureIndex = allMeasures;
                    _seqNextBeatOffset = allNotes.Sum(n => n.BeatDuration);
                    _seqNextGlobalNoteIndex = allNotes.Count(n => !n.IsRest);

                    _lowerMeasureIndex = _seqNextMeasureIndex;
                    _lowerBeatOffset = _seqNextBeatOffset;
                    _lowerGlobalNoteIndex = _seqNextGlobalNoteIndex;
                    if (_staffDrawable != null) _staffDrawable.UpperHasEndBar = false;
                }
                else if (_session.Tune == "Arpeggio")
                {
                    var pattern = ArpeggioCatalog.All.FirstOrDefault(p => p.Id == _session.SelectedArpeggioId)
                        ?? ArpeggioCatalog.MajorTriad;
                    var allNotes = await _session.LoadArpeggioAsync(pattern, _session.SelectedArpeggioRoot);

                    const int arpeggioUpperMeasureCount = 4;
                    upperFlat = allNotes
                        .Where(n => (n.MeasureIndex ?? 0) < arpeggioUpperMeasureCount)
                        .ToList();
                    lowerFlat = allNotes
                        .Where(n => (n.MeasureIndex ?? 0) >= arpeggioUpperMeasureCount)
                        .ToList();
                    upperBarBeats = ComputeNewBarBeats(upperFlat, existingUpper);
                    lowerBarBeats = ComputeNewBarBeats(lowerFlat, existingLower);

                    _seqNextMeasureIndex = 8;
                    _seqNextBeatOffset = allNotes.Sum(n => n.BeatDuration);
                    _seqNextGlobalNoteIndex = allNotes.Count(n => !n.IsRest);
                    _lowerMeasureIndex = _seqNextMeasureIndex;
                    _lowerBeatOffset = _seqNextBeatOffset;
                    _lowerGlobalNoteIndex = _seqNextGlobalNoteIndex;
                    if (_staffDrawable != null) _staffDrawable.UpperHasEndBar = false;
                }
                else
                {
                    using (PracticeSessionStartProfiler.Scope("StaffDisplay.LoadExcluded"))
                        await LoadExcludedMidisAsync();

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
                        using (PracticeSessionStartProfiler.Scope("StaffDisplay.SequenceGen"))
                        {
                        // Build a combined generator sized to hold the full ascending+descending
                        // scale walk.  The walk length for N pitch-pool notes is (2N − 2) events
                        // so use enough measures to hold it all at the smallest allowed duration.
                        var timeSig = _session.MeterTimeSignature switch
                        {
                            "3/4" => TimeSignature.ThreeFour,
                            "2/4" => TimeSignature.TwoFour,
                            _ => TimeSignature.FourFour
                        };
                        // A safe upper bound: even a chromatic 3-octave range (37 pitches) needs
                        // at most (2*37−2)=72 quarter notes = 18 bars of 4/4.  Cap at 24 to be safe.
                        var genAll = BuildSequenceGenerator(24);
                        var allMeasures = genAll.GenerateSequence();
                        var allNotes = MusicSequenceGenerator.Flatten(allMeasures);
                        double measureBeats = genAll.TimeSignature.TotalBeats;
                        var allBarBeats = ComputeStaffBarBeats(allNotes, measureBeats, new HashSet<double>());

                        float canvasWidth = StaffGraphicsView?.Width > 0 ? (float)StaffGraphicsView.Width : 360f;
                        float canvasHeight = StaffGraphicsView?.Height > 0 ? (float)StaffGraphicsView.Height : 480f;
                        var split = _staffDrawable!.SplitMeasuresAcrossStaves(allNotes, allBarBeats, canvasWidth, canvasHeight);
                        upperFlat = split.UpperNotes;
                        lowerFlat = split.LowerNotes;

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

                        double lowerBeatShift = lowerFlat.Count > 0 ? (lowerFlat[0].BeatPosition ?? 0.0) : 0.0;
                        lowerFlat = ShiftStaffBeatPositions(lowerFlat, lowerBeatShift);

                        upperBarBeats = ComputeStaffBarBeats(upperFlat, measureBeats, existingUpper);
                        lowerBarBeats = ComputeStaffBarBeats(lowerFlat, measureBeats, existingLower);

                        double upperBeats = upperFlat.Sum(n => n.BeatDuration);
                        double lowerBeats = lowerFlat.Sum(n => n.BeatDuration);
                        int upperPitches = upperFlat.Count(n => !n.IsRest);
                        int lowerPitches = lowerFlat.Count(n => !n.IsRest);

                        _seqNextMeasureIndex = allMeasures.Count;
                        _seqNextBeatOffset = upperBeats + lowerBeats;
                        _seqNextGlobalNoteIndex = upperPitches + lowerPitches;
                        _lowerMeasureIndex = _seqNextMeasureIndex;
                        _lowerBeatOffset = _seqNextBeatOffset;
                        _lowerGlobalNoteIndex = _seqNextGlobalNoteIndex;
                        }
                    }
                    else
                    {
                        var (upperMc, lowerMc) = GetStaffMeasureCounts();
                        StandardStaffGenResult genResult;
                        using (PracticeSessionStartProfiler.Scope("StaffDisplay.SequenceGen"))
                            genResult = await Task.Run(() => BuildStandardStaffNoteLists(upperMc, lowerMc));

                        upperFlat = genResult.UpperFlat;
                        upperBarBeats = genResult.UpperBarBeats;
                        lowerFlat = genResult.LowerFlat;
                        lowerBarBeats = genResult.LowerBarBeats;
                        _seqNextMeasureIndex = genResult.SeqNextMeasureIndex;
                        _seqNextBeatOffset = genResult.SeqNextBeatOffset;
                        _seqNextGlobalNoteIndex = genResult.SeqNextGlobalNoteIndex;
                        _lowerMeasureIndex = genResult.LowerMeasureIndex;
                        _lowerBeatOffset = genResult.LowerBeatOffset;
                        _lowerGlobalNoteIndex = genResult.LowerGlobalNoteIndex;

#if DEBUG
                        DebugLog.WriteLine($"[Staff Standard] L{_session.ChildLevel} upperMc={upperMc} lowerMc={lowerMc} Upper: {upperFlat.Count} notes ({upperFlat.Count(n => !n.IsRest)} pitched), Lower: {lowerFlat.Count} notes ({lowerFlat.Count(n => !n.IsRest)} pitched)");
#endif
                    }

                    // Signal the drawable whether to draw a single end bar on the upper staff.
                    _staffDrawable.UpperHasEndBar = isTwoOctave;
                }

                // ── Push to staff drawable ────────────────────────────────────────────
                using (PracticeSessionStartProfiler.Scope("StaffDisplay.Apply"))
                {
                var v3Drawable = _staffDrawable;
                if (v3Drawable == null) return;

                v3Drawable.UpperNotes = upperFlat;
                v3Drawable.LowerNotes = lowerFlat;
                v3Drawable.UpperBarBeats = upperBarBeats;
                v3Drawable.LowerBarBeats = lowerBarBeats;
                v3Drawable.InvalidateLayoutCache();
                v3Drawable.UpperNoteStates = new StaffNoteState[upperFlat.Count];
                v3Drawable.LowerNoteStates = new StaffNoteState[lowerFlat.Count];
                v3Drawable.IsUpperActive = true;
                v3Drawable.ActiveNoteIndex = 0;
                v3Drawable.UpperAlpha = 1f;
                v3Drawable.LowerAlpha = 1f;

                // Mark first non-rest note on upper staff as Current.
                for (int i = 0; i < upperFlat.Count; i++)
                {
                    if (!upperFlat[i].IsRest)
                    {
                        v3Drawable.UpperNoteStates[i] = StaffNoteState.Current;
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
                        Midi = gn.MidiNumber,
                        Name = NoteSessionService.ResolveWrittenNoteName(
                            gn.SpelledName, gn.MidiNumber, gn.Letter, gn.Octave, _session.Key, _session.SelectedScale),
                        TargetFreq = gn.TargetFrequency,
                        X = 0f,
                        Duration = gn.Duration,
                        StartBeat = slot.StartBeat,
                        DurationBeats = slot.DurationBeats,
                        GateBeatsAfterPrevious = slot.GateBeatsAfterPrevious,
                    });
                    _session.FeedbackViewModels.Add(new FeedbackItem(sessionIdx++, 0, 0, false));
                }

                _session.ConfigureRhythmStartGates();

                _sessionUpperPitchCount = upperFlat.Count(n => !n.IsRest);

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    ApplyStaffHeight();
                    StaffGraphicsView?.Invalidate();
                });
                }
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[Staff] UpdateStaffDisplayAsync ERROR: {ex}");
                StatusService.Instance.StatusMessage = $"[Staff Error] {ex.Message}";
            }
        }

        private static bool StaffNoteStatesEqual(StaffNoteState[] a, StaffNoteState[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        /// <summary>
        /// Syncs note states from session progress on the two-staff display.
        /// </summary>
        private void SyncStaffNoteStates()
        {
            if (_staffDrawable == null) return;

            int upperPitchCount = _sessionUpperPitchCount;
            int currentSession = _session.CurrentNoteIndex;

            bool isUpperActive = currentSession < upperPitchCount;

            // ── Upper staff states ────────────────────────────────────────────────
            var upperStates = new StaffNoteState[_staffDrawable.UpperNotes.Count];
            int si = 0;
            for (int i = 0; i < _staffDrawable.UpperNotes.Count; i++)
            {
                if (_staffDrawable.UpperNotes[i].IsRest) { upperStates[i] = StaffNoteState.Pending; continue; }
                if (si < currentSession)
                    upperStates[i] = _session.CorrectNoteIndices.Contains(si) ? StaffNoteState.Correct : StaffNoteState.Wrong;
                else if (si == currentSession && isUpperActive)
                {
                    bool hasWrong = _session.NoteFeedbacks.TryGetValue(si, out var fb) && fb.Wrong > 0;
                    upperStates[i] = hasWrong ? StaffNoteState.Wrong : StaffNoteState.Current;
                }
                else
                    upperStates[i] = StaffNoteState.Pending;
                si++;
            }

            // ── Lower staff states ────────────────────────────────────────────────
            var lowerStates = new StaffNoteState[_staffDrawable.LowerNotes.Count];
            int li = 0;
            for (int i = 0; i < _staffDrawable.LowerNotes.Count; i++)
            {
                if (_staffDrawable.LowerNotes[i].IsRest) { lowerStates[i] = StaffNoteState.Pending; continue; }
                int globalIdx = upperPitchCount + li;
                if (globalIdx < currentSession)
                    lowerStates[i] = _session.CorrectNoteIndices.Contains(globalIdx) ? StaffNoteState.Correct : StaffNoteState.Wrong;
                else if (globalIdx == currentSession && !isUpperActive)
                {
                    bool hasWrong = _session.NoteFeedbacks.TryGetValue(globalIdx, out var fb2) && fb2.Wrong > 0;
                    lowerStates[i] = hasWrong ? StaffNoteState.Wrong : StaffNoteState.Current;
                }
                else
                    lowerStates[i] = StaffNoteState.Pending;
                li++;
            }

            int activeNoteIndex = isUpperActive ? currentSession : currentSession - upperPitchCount;
            if (_staffDrawable.IsUpperActive == isUpperActive
                && _staffDrawable.ActiveNoteIndex == activeNoteIndex
                && StaffNoteStatesEqual(_staffDrawable.UpperNoteStates, upperStates)
                && StaffNoteStatesEqual(_staffDrawable.LowerNoteStates, lowerStates))
            {
                return;
            }

            _staffDrawable.IsUpperActive = isUpperActive;
            _staffDrawable.UpperNoteStates = upperStates;
            _staffDrawable.LowerNoteStates = lowerStates;
            _staffDrawable.ActiveNoteIndex = activeNoteIndex;

            StaffGraphicsView.Invalidate();

            // Upper staff keeps completed note colors (green/red) while the player works on
            // the lower staff.  RefreshV3UpperStaffAsync replaces UpperNotes and resets
            // UpperNoteStates to Pending, which turned correct upper notes black at the
            // upper→lower transition; defer upper refresh until RegenerateNotesAsync.
        }

        /// <summary>
        /// Generates new notes for the upper staff while the player is on the lower staff,
        /// then fades in the new upper staff content.
        /// </summary>




        /// <summary>
        /// Resizes the staff canvas.  Must be called on the main thread.
        /// </summary>
        private void ApplyStaffHeight()
        {
            // Measure available height: window height minus shell nav bar.
            // In two-staff mode the pickers row is hidden so the staff fills the full content area.
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

            _staffDrawable?.AvailableHeight = availH;
            var h = _staffDrawable?.ComputeRequiredHeight() ?? 0;
            StaffGraphicsView.HeightRequest = h;
            StaffBorder.HeightRequest = h;
            UpdateTitlePlayButtonPosition();
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
                DebugLog.WriteLine($"[ApplyTunerHeight] ERROR: {ex}");
            }
        }

        private const double TitlePlayButtonSizeMm = 6;

        private void UpdateTitlePlayButtonPosition()
        {
            if (TitlePlayButton == null || TitleStartStopButton == null || TitleMarqueeGrid == null
                || !TitlePlayButton.IsVisible)
                return;

            var overlayParent = MainPageRootGrid;
            if (overlayParent.Width <= 0 || TitleStartStopButton.Width <= 0)
                return;

            var goBounds = TryGetScreenBounds(TitleStartStopButton);
            var marqueeBounds = TryGetScreenBounds(TitleMarqueeGrid);
            var parentBounds = TryGetScreenBounds(overlayParent);
            if (!goBounds.HasValue || !marqueeBounds.HasValue || !parentBounds.HasValue)
                return;

            var go = goBounds.Value;
            var marquee = marqueeBounds.Value;
            var parent = parentBounds.Value;

            double playSize = MarginUtils.MmToDips(TitlePlayButtonSizeMm);
            TitlePlayButton.WidthRequest = playSize;
            TitlePlayButton.HeightRequest = playSize;
            UpdateTitlePlayButtonFontSize();

            // Top edge flush with bottom of status marquee; center aligned under GO.
            double top = Math.Max(0, marquee.Bottom - parent.Top);
            double left = go.Center.X - parent.Left - playSize * 0.5;
            TitlePlayButton.Margin = new Thickness(Math.Max(0, left), top, 0, 0);
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
            if (TitlePlayButton == null)
                return;

            if (isPlaying)
            {
                TitlePlayButton.BackgroundColor = PlayButtonRed;
                TitlePlayButton.Stroke = PlayButtonRedBorder;
                _titlePlayLabelText = TitleStopLabelText;
            }
            else
            {
                TitlePlayButton.BackgroundColor = PlayButtonGreen;
                TitlePlayButton.Stroke = PlayButtonGreenBorder;
                _titlePlayLabelText = TitlePlayLabelText;
            }

            if (isEnabled.HasValue)
                TitlePlayButton.IsEnabled = isEnabled.Value;

            UpdateTitlePlayButtonFontSize();
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
                DebugLog.WriteLine($"[Play] Stop listening error: {ex}");
            }
            finally
            {
                SetButtonStates(false);
            }

            await Task.Delay(80);
        }

        private const int TitleStartStopButtonSize = 32;
        /// <summary>Title-bar slot width — stop state expands to this so "Stop" fits.</summary>
        private const int TitleStartStopSlotWidth = 38;
        private const string TitleGoLabelText = "GO";
        private const string TitlePlayLabelText = "Play";
        private const string TitleStopLabelText = "Stop";
        private string _titlePlayLabelText = TitlePlayLabelText;

        private static SKTypeface? _v3UiRegularTypeface;

        /// <summary>OpenSansRegular base face; MAUI applies synthetic bold via FontAttributes.Bold.</summary>
        private static SKTypeface TitleUiRegularTypeface =>
            _v3UiRegularTypeface ??= SKTypeface.FromFamilyName("Open Sans", SKFontStyle.Normal)
                ?? SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal)
                ?? SKTypeface.Default;

        private static void ConfigureTitleUiBoldFont(SKFont font, float size)
        {
            font.Size = size;
            font.Typeface = TitleUiRegularTypeface;
            // Match MAUI synthetic bold on OpenSansRegular (wider than native bold metrics).
            font.Embolden = true;
            font.Edging = SKFontEdging.Antialias;
            font.Subpixel = true;
        }

        /// <summary>
        /// Largest bold font size whose glyph bounds fit inside the box, with slack for
        /// embolden stroke and MAUI Label rendering wider than Skia advance width.
        /// </summary>
        private static double GetTitleFittedFontSize(string text, double maxWidth, double maxHeight)
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
                ConfigureTitleUiBoldFont(font, (float)mid);

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
        /// Largest bold "GO" font size that fits fully inside the green start circle.
        /// </summary>
        private static double GetTitleGoFontSize(double circleDiameter)
        {
            if (circleDiameter <= 0)
                return 10;

            const double inset = 3;
            double inner = circleDiameter - inset * 2;
            return GetTitleFittedFontSize(TitleGoLabelText, inner, inner);
        }

        /// <summary>Largest bold "Stop" font size that fits inside the red stop button.</summary>
        private static double GetTitleStopFontSize(double width, double height)
        {
            if (width <= 0 || height <= 0)
                return 10;

            const double inset = 2;
            return GetTitleFittedFontSize(TitleStopLabelText, width - inset * 2, height - inset * 2);
        }

        private void UpdateTitlePlayButtonFontSize()
        {
            if (TitlePlayButton == null)
                return;

            string text = _titlePlayLabelText;
            double size = TitlePlayButton.HeightRequest > 0
                ? TitlePlayButton.HeightRequest
                : TitlePlayButton.Height;
            if (size <= 0)
                size = MarginUtils.MmToDips(TitlePlayButtonSizeMm);

            // StrokeThickness=1 plus slack so glyphs stay inside the square.
            const double inset = 3;
            double inner = size - inset * 2;
            if (inner <= 0)
                return;

            // Square button: largest font that fits the current label fully inside.
            double fontSize = GetTitleFittedFontSize(text, inner, inner);

            TitlePlayButton.Content = new Label
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

        private void UpdateTitleStartStopButtonVisual(bool isRunning)
        {
            if (TitleStartStopButton == null)
                return;

            if (isRunning)
            {
                double width = TitleStartStopSlotWidth;
                double height = TitleStartStopButtonSize;

                TitleStartStopButton.WidthRequest = width;
                TitleStartStopButton.HeightRequest = height;
                TitleStartStopButton.BackgroundColor = Colors.Red;
                TitleStartStopButton.StrokeShape = new RoundRectangle { CornerRadius = 6 };
                TitleStartStopButton.Content = new Label
                {
                    Text = TitleStopLabelText,
                    FontSize = GetTitleStopFontSize(width, height),
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
                double diameter = TitleStartStopButtonSize;

                TitleStartStopButton.WidthRequest = diameter;
                TitleStartStopButton.HeightRequest = diameter;
                TitleStartStopButton.BackgroundColor = Color.FromArgb("#008000");
                TitleStartStopButton.StrokeShape = new RoundRectangle { CornerRadius = diameter / 2 };
                TitleStartStopButton.Content = new Label
                {
                    Text = TitleGoLabelText,
                    FontSize = GetTitleGoFontSize(diameter),
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
                UpdateTitleStartStopButtonVisual(isRunning);
                SetPlayButtonPlaying(_isPlaying, isRunning ? (keepPlayEnabled || _isPlaying) : true);
                UpdatePlayButtonVisibility();
                if (_session?.Tune == "Tuner")
                    SyncPlayItemStatusMessage();
            });
        }

        private void UpdatePlayButtonVisibility()
        {
            OnPropertyChanged(nameof(IsTitlePlayButtonVisible));
            UpdateTitlePlayButtonPosition();
        }

        private string GetCurrentPlayItemName()
        {
            if (LayoutTestTune.IsEnabled)
                return PlayModePickerOptions.HalfThroughSixteenthNotes;

            if (_session.Tune == "Tuner")
                return "Tuner";

            if (_session.Tune == "Arpeggio")
                return string.IsNullOrWhiteSpace(_session.SelectedArpeggioDisplay)
                    ? "Arpeggio"
                    : _session.SelectedArpeggioDisplay;

            if (_session.Tune == "Practice Tune")
            {
                if (!string.IsNullOrWhiteSpace(_session.CurrentTune?.Title))
                {
                    if (_session.ScaleSelectionMode == ScaleSelectionMode.ByLevel
                        && !PlayModePickerOptions.IsUserSelectedPracticeTuneTitle(
                            Preferences.Default.Get<string?>("SelectedTune", null)))
                        return $"{_session.CurrentTune.Title} (By Level)";

                    return _session.CurrentTune.Title;
                }

                var savedTitle = Preferences.Default.Get<string?>("SelectedTune", null);
                if (!string.IsNullOrWhiteSpace(savedTitle)
                    && musicmate.Models.TuneLibrary.All.Any(t => t.Title == savedTitle))
                    return savedTitle;

                return _session.EffectiveScaleDisplay;
            }

            if (_session.IsRandomMode)
                return _session.EffectiveScaleDisplay;

            if (_session.ScaleSelectionMode == ScaleSelectionMode.ByLevel)
                return $"{_session.Key} {_session.EffectiveScale} (By Level)";

            if (_session.ScaleSelectionMode == ScaleSelectionMode.Random)
                return _session.EffectiveScaleDisplay;

            return _session.SelectedScale ?? "Selected Scale";
        }

        private int GetChildLevelForSlider()
        {
            if (_session.ChildLevel <= 0) return 1;
            return Math.Clamp(Preferences.Default.Get(ChildLevelPrefKey, _session.ChildLevel), 1, 100);
        }

        private async Task ApplyChildLevelAndRefreshAsync(int level)
        {
            level = Math.Clamp(level, 1, 100);

            PracticeDifficultySettings difficulty;
            _suppressSessionRegenerate = true;
            try
            {
                _session.ChildLevel = level;
                difficulty = DifficultyLevelMapper.ApplyLevelChangeToSession(
                    level, _session, preserveUserPracticeSettings: _session.ChildPracticeSettingsCustomized);
                Preferences.Default.Set(ChildLevelPrefKey, level);
                LevelUpService.MarkCountSinceNow();

                _session.PrepareEffectiveScaleForGeneration(_generationSeed);
                UpdateEffectiveScaleLabel();
                UpdateKeyPickerSelection();
                UpdateScaleTunePicker();
                UpdateConcertKeyLabel();
                UpdateKeyPickerVisibility();
            }
            finally
            {
                _suppressSessionRegenerate = false;
            }

            _repeatSameSnapshot = null;
            if (ChildLevelSliderValueLabel != null)
                ChildLevelSliderValueLabel.Text = level.ToString();

            if (_session.IsRandomMode)
                SyncPlayItemStatusMessage();
            else
                StatusService.Instance.StatusMessage =
                    $"Level {level}: {difficulty.StageLabel} — {difficulty.SuggestedKey} {difficulty.SuggestedScale}";

            await RefreshDisplayForLevelChangeAsync();
        }

        private async void OnChildLevelDeltaClicked(object? sender, EventArgs e)
        {
            if (_session.ChildLevel <= 0
                || sender is not Button { CommandParameter: string param }
                || !int.TryParse(param, out int delta))
                return;

            int level = Math.Clamp(GetChildLevelForSlider() + delta, 1, 100);
            if (level == _session.ChildLevel)
                return;

            await ApplyChildLevelAndRefreshAsync(level);
        }

        private async Task RefreshDisplayForLevelChangeAsync()
        {
            _freezeStaff = false;
            _holdResultForChildSession = false;
            _repeatSameSnapshot = null;
            _session.SessionCompleted = false;

            await HideSessionResultBannerAsync(refreshMarqueeForNewLevel: false);

            if (!LayoutTestTune.IsEnabled)
                PracticeCompositionSelector.ApplyNextExerciseIfNeeded(_session, _generationSeed);

            if (!_session.RepeatSameTune)
                PrepareFreshScaleAndKeyIfNeeded(forceNewNotes: true, scaleKeyTrigger: "GoButton");

            await RegenerateNotesAsync();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ApplyStaffHeight();
                StaffGraphicsView.Invalidate();
                UpdateTitlePlayButtonPosition();
            });
        }

        /// <summary>
        /// Apply child-level range and batch sizing for a new run.
        /// Scale/key refresh is handled by <see cref="NoteSessionService.PrepareFreshScaleAndKeyForGeneration"/>.
        /// Skipped when Repeat Same will restore the previous tune.
        /// </summary>
        private void PickChildSessionSettingsIfNeeded(bool preserveRepeatSameTune)
        {
            if (_session.ChildLevel <= 0)
                return;
            if (preserveRepeatSameTune && _session.RepeatSameTune && _repeatSameSnapshot?.Notes.Count > 0)
                return;

            DifficultyLevelMapper.ApplyLevelDerivedSettings(_session.ChildLevel, _session);
        }

        private async Task BuildAndPublishSessionEndMarqueeAsync(
            int progressLevel,
            string instrumentKey,
            DateTime countSince)
        {
            if (_sessionResultDb == null)
                return;

            await _sessionResultDb.InitializeAsync();
            var rows = await _sessionResultDb.GetByLevelAndInstrumentAsync(progressLevel, instrumentKey);
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
            if (SessionResultBanner != null)
                await MainThread.InvokeOnMainThreadAsync(() => SessionResultBanner.IsVisible = false);
            if (refreshMarqueeForNewLevel)
                await RefreshMarqueeAfterCongratulatoryBannerAsync();
        }

        /// <summary>
        /// Hides the post-session result banner and scrolls so the staff is fully visible.
        /// Called when the microphone first detects playing (RMS above threshold).
        /// </summary>
        private async Task DismissSessionResultBannerAndScrollToStaffAsync()
        {
            if (_dismissedResultBannerForFirstSound || SessionResultBanner?.IsVisible != true)
                return;

            _dismissedResultBannerForFirstSound = true;
            _holdResultForChildSession = false;

            await HideSessionResultBannerAsync(refreshMarqueeForNewLevel: true);

            if (_session.Tune == "Tuner")
                ApplyTunerHeight();
            else
                ApplyStaffHeight();

            // Let layout settle after the banner is removed from the visual tree.
            await Task.Delay(50);

            double scrollY = GetViewYOffsetInMainLayout(StaffBorder);
            try
            {
                await MainScrollView.ScrollToAsync(0, scrollY, true);
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[FirstSound] scroll error: {ex}");
            }
        }

        private double GetViewYOffsetInMainLayout(VisualElement view)
        {
            double y = view.Y;
            Element? parent = view.Parent;
            while (parent is VisualElement pv && parent != MainPageMainLayout)
            {
                y += pv.Y;
                parent = pv.Parent;
            }
            return Math.Max(0, y);
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
            string instrumentKey = _pendingInstrumentForMarquee
                ?? _session.InstrumentKey;
            _pendingInstrumentForMarquee = null;

            if (_session.ChildLevel > 0 && !string.IsNullOrEmpty(instrumentKey))
            {
                await BuildAndPublishSessionEndMarqueeAsync(
                    _session.ChildLevel, instrumentKey, Services.LevelUpService.CountSinceUtc);
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
            if (SessionResultBanner != null)
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
            DifficultyLevelMapper.ApplyLevelDerivedSettings(level, _session);

            if (ChildLevelSliderValueLabel != null)
                ChildLevelSliderValueLabel.Text = level.ToString();
        }

        /// <summary>
        /// Adopts the saved Home-page level when Music is opened without Home → Start
        /// (e.g. via the flyout menu). Applies range/batch settings only — does not
        /// overwrite the user's current tune, key, or scale selection.
        /// </summary>
        private void EnsureChildLevelFromPreferences()
        {
            if (_session.ChildLevel > 0)
                return;

            int saved = Preferences.Default.Get(ChildLevelPrefKey, 0);
            if (saved <= 0)
                return;

            saved = Math.Clamp(saved, 1, 100);
            _session.ChildLevel = saved;
            if (_session.ScaleSelectionMode == ScaleSelectionMode.ByLevel)
                _session.SelectedScale = ChildLevelProgression.GetDefaultScaleForLevel(saved);
            else
                _session.ApplyScaleSelectionOnLevelChange(saved);
            DifficultyLevelMapper.ApplyLevelDerivedSettings(saved, _session);
#if DEBUG
            DebugLog.WriteLine($"[ChildLevel] Hydrated from preferences: L{saved}");
#endif
        }

        protected async override void OnAppearing()
        {
            try
            {
                await OnAppearingCoreAsync();
            }
            catch (Exception ex)
            {
                Utils.Log($"[MusicPage.OnAppearing] ERROR: {ex}");
                DebugLog.WriteLine($"[MusicPage.OnAppearing] ERROR: {ex}");
            }
        }

        private async Task OnAppearingCoreAsync()
        {
            base.OnAppearing();
#if DEBUG
            if (Diagnostics.DebugLogSettings.IsEnabled(Diagnostics.DebugLogCategory.StaffSelfTests))
            {
                Drawables.StaffDrawable.RunKeySignatureTests();
                Drawables.StaffDrawable.RunMeasureLayoutTests();
            }
#endif
            bool returningToPage = !_isPageVisible;
            _isPageVisible = true;
            if (returningToPage)
            {
                _freezeStaff = false;
                // Keep result hold when returning from Settings so the session-end
                // marquee and banner stay visible until the user starts again.
                _holdResultForChildSession = ShouldPreserveSessionEndMarquee();
            }
            _orientation?.ForceLandscape();

            DebugLog.WriteLine($"[DEBUG] OnAppearing: IsAutoRepeatVisible={IsAutoRepeatVisible}, Tune={_session.Tune}");
            IsAutoRepeatVisible = _session.Tune != "Tuner";
            UpdateAutoRepeatButtons();
            UpdateEffectiveScaleLabel();
            UpdateTunerVisibility();
            DeviceDisplay.Current.KeepScreenOn = true;

            EnsurePracticePickersReady();

            EnsureChildLevelFromPreferences();
            UpdateChildLevelSliderDisplay();
            // Android may lay out the slider row after OnAppearing; refresh once more.
            Dispatcher.Dispatch(UpdateChildLevelSliderDisplay);
            Dispatcher.Dispatch(UpdateTitlePlayButtonPosition);

#if DEBUG
            if (_session.AutoStart && _session.Tune != "Tuner" && _session.ChildLevel == 0)
            {
                AutoRepeat = true;
            }
#endif

            // Regenerate before AutoStart so random→scale changes refresh the staff.
            // When Repeat Same is on, restore the saved snapshot instead of re-randomizing key.
            if (!_isRunning && !ShouldPreserveSessionEndMarquee() && !_holdResultForChildSession)
            {
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (StaffGraphicsView != null && StaffGraphicsView.Width <= 0 && sw.ElapsedMilliseconds < 1500)
                        await Task.Delay(40);
                    if (_session.RepeatSameTune && _repeatSameSnapshot?.Notes.Count > 0)
                        await RestoreRepeatSameSnapshotAsync(_repeatSameSnapshot.Notes);
                    else
                        await RegenerateNotesAsync();
                }
                catch (Exception ex)
                {
                    DebugLog.WriteLine($"[OnAppearing] ERROR regenerating notes: {ex}");
                }
            }

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

            RestoreSessionEndMarqueeIfNeeded();
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
                    if (_staffDrawable != null)
                    {
                        // StaffBorder sits inside the VerticalStackLayout.
                        // Its Y relative to MainScrollView content is its absolute position
                        // within MainPageMainLayout.
                        double borderY = StaffBorder.Y
                                       + (StaffBorder.Parent is View p ? p.Y : 0);
                        // TopMargin inside the drawable is the clearance above the highest note.
                        // Subtract TopMargin so the scroll top lands at the notehead top edge.
                        scrollY = Math.Max(0, borderY);
                    }
                    await MainScrollView.ScrollToAsync(0, scrollY, false);
                }
                catch (Exception ex)
                {
                    DebugLog.WriteLine($"[OnNavigatedTo] scroll error: {ex}");
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
                var instIdx = Array.FindIndex(instrumentOptions, s => s == "Concert Pitch");
                if (instIdx >= 0)
                {
                    InstrumentPicker?.SelectedIndex = instIdx;
                    _session.Instrument = instrumentOptions[instIdx];
                    SelectedInstrumentShort = _session.InstrumentDisplayName;
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
                    DebugLog.WriteLine("[Audio] Below RMS threshold, ignoring");
                    _isBelowThreshold = true;
                    // Notify session so consecutive same-pitch notes can be distinguished
                    _session.NotifySilence();
                }
                _pitchBufferPos = 0;
                return;
            }

            bool firstSoundAfterSilence = _isBelowThreshold;
            _isBelowThreshold = false;

            if (firstSoundAfterSilence
                && _isRunning
                && !_dismissedResultBannerForFirstSound
                && SessionResultBanner?.IsVisible == true)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                    _ = DismissSessionResultBannerAndScrollToStaffAsync());
            }

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
                        UpdateTunerStaffDisplay();
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
                    var result = _session.Evaluate(freq);
                    if (_session.UpdateFeedbackForCurrent(freq, result))
                    {
                        if (result.correct)
                        {
                            // When upper staff is exhausted, trigger lower-staff refresh.
                            int upperPitchCount = _sessionUpperPitchCount;
                            if (_session.CurrentNoteIndex == upperPitchCount && _staffDrawable != null
                                && _staffDrawable.LowerAlpha >= 1f && !_staffDrawable.UpperHasEndBar)
                            {
                                // No lower-staff refresh here: replacing lower notes while
                                // the player is about to play them causes a display/session
                                // mismatch.  The session will complete and AutoRepeat will
                                // generate a fresh set.
                            }
                        }
                        SyncStaffNoteStates();
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
                DebugLog.WriteLine("[Restart] Restarting audio capture (session preserved)...");
                _audio.StopCapture();
                _pitchBufferPos = 0;
                await _audio.EnsurePermissionAsync();
                _audio.StartCapture(OnAudioBlock);
                DebugLog.WriteLine("[Restart] Audio capture restarted");
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[Restart] ERROR: {ex}");
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
                        MasteryEvaluator.RefreshMasteredFields(stat, _session);
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
                        MasteryEvaluator.RefreshMasteredFields(stat, _session);
                        await db.UpdateAsync(stat);
                    }
                }

                // Prune if over the size limit set in Settings
                long maxBytes = (long)Preferences.Default.Get("MaxNoteDbSizeMb", 50) * 1024 * 1024;
                await db!.PruneToSizeLimitAsync(maxBytes);
                ServiceHelper.GetService<StatisticsCacheService>()?.InvalidateNoteStats();
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[Stats] UpdateNoteStatsDatabaseAsync error: {ex.Message}");
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
        /// <paramref name="sessionLevel"/> is the child level at session start (before any level-up).
        /// </summary>
        private async Task SaveNoteAttemptsForSessionAsync(int sessionLevel)
        {
            if (_noteAttemptDb == null) return;
            // Skip autoplay sessions — no microphone input, nothing meaningful to record.
            if (_isPlaying) return;

            try
            {
                await _noteAttemptDb.InitializeAsync();

                var instrument = _session.Instrument ?? string.Empty;
                var level = sessionLevel;
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
                ServiceHelper.GetService<StatisticsCacheService>()?.InvalidateNoteStats();
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[NoteAttempts] SaveNoteAttemptsForSessionAsync error: {ex}");
            }
        }
        /// <summary>
        /// Picks a fresh scale/key before regeneration when Repeat Same is off.
        /// </summary>
        private void PrepareFreshScaleAndKeyIfNeeded(bool forceNewNotes, string? scaleKeyTrigger)
        {
            if (_session.RepeatSameTune)
                return;

            if (!LayoutTestTune.IsEnabled)
                PracticeCompositionSelector.ApplyNextExerciseIfNeeded(_session, _generationSeed);

            string trigger = forceNewNotes ? "GoButton" : (scaleKeyTrigger ?? "SessionStart");
            _session.PrepareFreshScaleAndKeyForGeneration(trigger, repeatSame: false, _generationSeed);
            UpdateKeyPickerSelection();
            UpdateConcertKeyLabel();
            UpdateScaleTunePicker();
            UpdateEffectiveScaleLabel();
        }

        private void CaptureRepeatSameSnapshot()
        {
            if (_staffDrawable == null || _session.NotesToDraw.Count == 0)
                return;

            _repeatSameSnapshot = PracticeSessionLifecycle.CaptureSnapshot(
                _session,
                _session.NotesToDraw,
                new StaffLayoutCapture(
                    _staffDrawable.UpperNotes,
                    _staffDrawable.LowerNotes,
                    _staffDrawable.UpperBarBeats,
                    _staffDrawable.LowerBarBeats,
                    _sessionUpperPitchCount,
                    _staffDrawable.UpperHasEndBar));
        }

        private async Task RestoreRepeatSameSnapshotAsync(IReadOnlyList<NoteInfo> notes)
        {
            var snap = _repeatSameSnapshot;
            if (snap == null)
                return;

            _suppressSessionRegenerate = true;
            try
            {
                PracticeSessionLifecycle.RestoreGenerationContext(_session, snap);
                PracticeSessionLifecycle.RestoreNotesToSession(_session, notes);

                if (_staffDrawable != null)
                    _sessionUpperPitchCount = PracticeSessionLifecycle.RestoreStaffDrawable(_staffDrawable, snap);

                _session.ConfigureRhythmStartGates();

                UpdateKeyPickerSelection();
                UpdateConcertKeyLabel();
                UpdateScaleTunePicker();
                UpdateEffectiveScaleLabel();
                SyncPlayItemStatusMessage();

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    ApplyStaffHeight();
                    StaffGraphicsView?.Invalidate();
                });
            }
            finally
            {
                _suppressSessionRegenerate = false;
            }
        }

        private async Task StartListeningAndEvaluatingAsync(
            bool playBack = false,
            bool forceNewNotes = false,
            string? scaleKeyTrigger = null)
        {
            _sessionStartCts = PracticeSessionLifecycle.ReplaceSessionStartCancellation(_sessionStartCts);
            var ct = _sessionStartCts.Token;

            try
            {
                // Any new session clears the post-autoplay results freeze.
                _freezeStaff = false;
                ClearSessionEndMarquee();

                // Assign a fresh session ID so all NoteAttempts from this run are grouped together.
                _currentSessionId = PracticeSessionLifecycle.NewSessionId();

                DebugLog.WriteLine($"[Start] Starting listening, playBack={playBack}, forceNewNotes={forceNewNotes}");
                SetButtonStates(true, keepPlayEnabled: playBack);

                using (PracticeSessionStartProfiler.Scope("SessionReset"))
                {
                    _lastProcess = DateTime.MinValue;
                    _isBelowThreshold = true;
                    _dismissedResultBannerForFirstSound = false;
                    _pitchBufferPos = 0;
                    _session.Reset();
                }

                ct.ThrowIfCancellationRequested();

                if (!_session.RepeatSameTune)
                    _repeatSameSnapshot = null;

                PickChildSessionSettingsIfNeeded(preserveRepeatSameTune: !forceNewNotes);

                if (_session.RepeatSameTune && _repeatSameSnapshot == null
                    && _session.NotesToDraw?.Count > 0)
                {
                    CaptureRepeatSameSnapshot();
                }

                var startPlan = PracticeSessionLifecycle.PlanExerciseStart(
                    _session.RepeatSameTune, _repeatSameSnapshot, forceNewNotes, scaleKeyTrigger);

                using (PracticeSessionStartProfiler.Scope("ExercisePrepare"))
                {
                    switch (startPlan.Action)
                    {
                        case PracticeExerciseStartAction.RestoreRepeatSame:
                        {
                            if (startPlan.LogScaleKeyWithoutChanging)
                            {
                                _session.PrepareFreshScaleAndKeyForGeneration(
                                    scaleKeyTrigger!, repeatSame: true, _generationSeed);
                            }

                            var db = ServiceHelper.GetService<NoteDatabase>();
                            List<NoteInfo> notesToRestore;
                            using (PracticeSessionStartProfiler.Scope("MasteryFilter"))
                            {
                                notesToRestore = await PracticeSessionLifecycle.ResolveRepeatSameNotesAsync(
                                    _repeatSameSnapshot!, _session, db);
                            }

                            if (notesToRestore.Count < PracticeSessionLifecycle.MinNotesAfterMasteryFilter)
                            {
                                PickChildSessionSettingsIfNeeded(preserveRepeatSameTune: false);
                                PrepareFreshScaleAndKeyIfNeeded(forceNewNotes, scaleKeyTrigger);
                                using (PracticeSessionStartProfiler.Scope("RegenerateNotes"))
                                    await RegenerateNotesAsync();
                                if (_session?.NotesToDraw != null && _session.NotesToDraw.Count > 0)
                                    CaptureRepeatSameSnapshot();
                            }
                            else
                            {
                                using (PracticeSessionStartProfiler.Scope("RestoreRepeatSame"))
                                    await RestoreRepeatSameSnapshotAsync(notesToRestore);
                                DebugLog.WriteLine($"[Start] Restored {notesToRestore.Count} notes for Repeat Same (filtered from {_repeatSameSnapshot!.Notes.Count})");
                            }
                            break;
                        }
                        default:
                            PrepareFreshScaleAndKeyIfNeeded(forceNewNotes, scaleKeyTrigger);
                            using (PracticeSessionStartProfiler.Scope("RegenerateNotes"))
                                await RegenerateNotesAsync();

                            if (_session.RepeatSameTune
                                && _session?.NotesToDraw != null
                                && _session.NotesToDraw.Count > 0)
                            {
                                CaptureRepeatSameSnapshot();
                                DebugLog.WriteLine($"[Start] Generated and saved {_session.NotesToDraw.Count} notes for Repeat Same");
                            }
                            break;
                    }
                }

                SyncPlayItemStatusMessage();

                ct.ThrowIfCancellationRequested();

                if (!playBack)
                {
                    if (!_isRunning)
                    {
                        DebugLog.WriteLine("[Start] Aborted before capture: no longer running");
                        return;
                    }

                    DebugLog.WriteLine("[Start] Requesting audio permission...");
                    using (PracticeSessionStartProfiler.Scope("AudioPermission"))
                    {
                        await _audio.EnsurePermissionAsync();
                    }
                    ct.ThrowIfCancellationRequested();
                    if (!_isRunning)
                    {
                        DebugLog.WriteLine("[Start] Aborted after permission: no longer running");
                        return;
                    }

                    try { _audio.StopCapture(); } catch { }
                    IEnumerable<NoteInfo> notesSource =
                        _session?.NotesToDraw ?? Enumerable.Empty<NoteInfo>();
                    var expectedNotes = notesSource
                        .Take(5)
                        .Select(n => n.Name)
                        .ToArray();
                    var sessionLog =
                        $"[Start] Instrument={_session?.Instrument}, " +
                        $"transpose={_session?.InstrumentTransposeOffset}, " +
                        $"Key={_session?.Key}, Scale={_session?.SelectedScale}, " +
                        $"Notes=[{string.Join(", ", expectedNotes)}]";
                    DebugLog.WriteLine(sessionLog);
                    Utils.Log(sessionLog);
                    DebugLog.WriteLine("[Start] Starting audio capture...");
                    _audio.StartCapture(OnAudioBlock);
                    _session?.StartListeningClock();
                    DebugLog.WriteLine("[Start] Audio capture started");
                }
                else
                {
                    try { _audio.StopCapture(); } catch { }
                }

                if (playBack)
                {
                    ct.ThrowIfCancellationRequested();

                    if ((_session?.NotesToDraw?.Count ?? 0) == 0)
                    {
                        _isPlaying = false;
                        SetPlayButtonPlaying(false);
                        SetButtonStates(false);
                        StatusService.Instance.StatusMessage =
                            "No notes to play — try again.";
                        DebugLog.WriteLine("[Start] Play aborted: NotesToDraw is empty after regenerate");
                        return;
                    }

                    _playCts?.Cancel();
                    _playCts = new CancellationTokenSource();
                    _ = PlayDisplayedAsync(_playCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                DebugLog.WriteLine("[Start] Cancelled");
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[Start] ERROR: {ex}");
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
        /// <summary>Full rhythmic sequence (notes and rests) for autoplay.</summary>
        private List<GeneratedNote>? TryGetAutoplayRhythmSequence()
        {
            if (_staffDrawable != null)
                return _staffDrawable.UpperNotes.Concat(_staffDrawable.LowerNotes).ToList();
            return null;
        }
        private void ApplyAutoplayRhythmHighlight(int eventIndex, IReadOnlyList<GeneratedNote> sequence, int pitchIndex)
        {
            _session.PlaybackHighlightIndex = sequence[eventIndex].IsRest ? null : pitchIndex;

            if (_staffDrawable != null)
            {
                int upperCount = _staffDrawable.UpperNotes.Count;
                bool onUpper = eventIndex < upperCount;
                _staffDrawable.IsUpperActive = onUpper;
                _staffDrawable.ActiveNoteIndex = onUpper ? eventIndex : eventIndex - upperCount;

                _staffDrawable.UpperNoteStates = BuildRhythmStaffStates(_staffDrawable.UpperNotes, 0, eventIndex);
                _staffDrawable.LowerNoteStates = BuildRhythmStaffStates(_staffDrawable.LowerNotes, upperCount, eventIndex);
                StaffGraphicsView.Invalidate();
            }
            else
            {
                StaffGraphicsView.Invalidate();
            }
        }
        private static StaffNoteState[] BuildRhythmStaffStates(
            IReadOnlyList<GeneratedNote> staffNotes, int globalOffset, int eventIndex)
        {
            var states = new StaffNoteState[staffNotes.Count];
            for (int d = 0; d < staffNotes.Count; d++)
            {
                int globalIdx = globalOffset + d;
                if (globalIdx < eventIndex)
                    states[d] = staffNotes[d].IsRest ? StaffNoteState.Pending : StaffNoteState.Correct;
                else if (globalIdx == eventIndex)
                    states[d] = StaffNoteState.Current;
                else
                    states[d] = StaffNoteState.Pending;
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
                DebugLog.WriteLine($"[PlayDisplayedAsync] ERROR: {ex}");
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
                    StatusService.Instance.StatusMessage = GetCurrentPlayItemName();
                }
            }
        }
        /// <summary>
        /// Saves session statistics and, for child-Practice sessions, a SessionResult.
        /// Returns the new child level if a level-up occurred, otherwise null.
        /// </summary>
        private async Task<int?> SaveSessionStatAsync()
        {
            var outcome = await PracticeSessionPersistence.SaveSessionStatAsync(
                _session,
                _sessionDb,
                _sessionResultDb,
                Preferences.Default.Get("CollectSessionStats", true),
                (long)Preferences.Default.Get("MaxSessionDbSizeMb", 50) * 1024 * 1024);

            if (outcome.NewChildLevel.HasValue)
            {
                _repeatSameSnapshot = null;
                UpdateChildLevelSliderDisplay();
                UpdateKeyPickerSelection();
                UpdateScaleTunePicker();
                UpdateConcertKeyLabel();
                await RefreshDisplayForLevelChangeAsync();
            }

            return outcome.NewChildLevel;
        }

        private async void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NoteSessionService.IsRandomMode)
                || e.PropertyName == nameof(NoteSessionService.EffectiveScale)
                || e.PropertyName == nameof(NoteSessionService.EffectiveScaleDisplay)
                || e.PropertyName == nameof(NoteSessionService.GenerationScale)
                || e.PropertyName == nameof(NoteSessionService.ScaleSelectionMode))
            {
                UpdateEffectiveScaleLabel();
            }

            if (e.PropertyName == nameof(_session.SelectedScale) ||
                e.PropertyName == nameof(NoteSessionService.Instrument) ||
                e.PropertyName == nameof(NoteSessionService.Key) ||
                e.PropertyName == nameof(NoteSessionService.Tune) ||
                e.PropertyName == nameof(NoteSessionService.CurrentTune) ||
                e.PropertyName == nameof(NoteSessionService.IsRandomMode))
            {
                // Saved notes are for a specific scale/key/tune — invalidate them when any of those change
                // so the next repeat generates fresh notes for the new selection rather than restoring stale ones.
                if (e.PropertyName == nameof(_session.SelectedScale) ||
                    e.PropertyName == nameof(NoteSessionService.Key) ||
                    e.PropertyName == nameof(NoteSessionService.Tune) ||
                    e.PropertyName == nameof(NoteSessionService.CurrentTune) ||
                    e.PropertyName == nameof(NoteSessionService.IsRandomMode))
                {
                    if (!_suppressSessionRegenerate)
                        _repeatSameSnapshot = null;
                }

                // Only regenerate when the page is visible; if called while navigating in from
                // HomePage the session properties are being batch-set and OnAppearing will
                // trigger the first regeneration once the page is actually on screen.
                if (_isPageVisible && !_suppressSessionRegenerate)
                {
#if DEBUG
                    if (e.PropertyName == nameof(NoteSessionService.IsRandomMode))
                        DebugLog.WriteLine($"[PickerTest] IsRandomMode={_session.IsRandomMode} Tune={_session.Tune} → RegenerateNotesAsync");
#endif
                    await RegenerateNotesAsync();
                    UpdateKeyPickerVisibility();
                }
            }

            if (e.PropertyName == nameof(_session.SelectedScale) ||
                e.PropertyName == nameof(NoteSessionService.Key) ||
                e.PropertyName == nameof(NoteSessionService.Instrument) ||
                e.PropertyName == nameof(NoteSessionService.Tune) ||
                e.PropertyName == nameof(NoteSessionService.CurrentTune) ||
                e.PropertyName == nameof(NoteSessionService.SelectedArpeggioDisplay) ||
                e.PropertyName == nameof(NoteSessionService.IsRandomMode))
            {
                UpdateConcertKeyLabel();
                if (!_applyingArpeggioSelection)
                {
                    if (!_suppressSessionRegenerate)
                        UpdateScaleTunePicker();
                    else
                        UpdatePracticePlayItemPickerCore();
                }
                UpdateKeyPickerVisibility();
            }

            if (e.PropertyName == nameof(NoteSessionService.Instrument))
                UpdateInstrumentPickerSelection();

            if (e.PropertyName == nameof(NoteSessionService.Key))
                UpdateKeyPickerSelection();

            if (e.PropertyName == nameof(NoteSessionService.AutoRepeat)
                || e.PropertyName == nameof(NoteSessionService.RepeatSameTune))
            {
                UpdateAutoRepeatButtons();
                if (_session.RepeatSameTune
                    && _repeatSameSnapshot == null
                    && _session.NotesToDraw.Count > 0
                    && _staffDrawable != null)
                {
                    CaptureRepeatSameSnapshot();
                }
                else if (!_session.RepeatSameTune)
                    _repeatSameSnapshot = null;
            }

            if (e.PropertyName == nameof(NoteSessionService.MusicBpm))
            {
                _staffDrawable?.InvalidateLayoutCache();
                MainThread.BeginInvokeOnMainThread(() => StaffGraphicsView?.Invalidate());
            }

            if (e.PropertyName == nameof(NoteSessionService.ChildLevel))
            {
                _repeatSameSnapshot = null;
                UpdateChildLevelSliderDisplay();
            }
        }
        private void UpdateInstrumentPickerSelection()
        {
            if (InstrumentPicker.ItemsSource is not string[] items) return;
            var idx = Array.IndexOf(items, _session.InstrumentDisplayName);
            if (idx >= 0 && InstrumentPicker.SelectedIndex != idx)
                InstrumentPicker.SelectedIndex = idx;
            if (idx >= 0 && PracticeInstrumentPicker != null && PracticeInstrumentPicker.SelectedIndex != idx)
                PracticeInstrumentPicker.SelectedIndex = idx;
            SelectedInstrumentShort = _session.InstrumentDisplayName;
        }
        private void EnterPickerSyncSuppress() => _pickerSyncSuppressCount++;

        /// <summary>
        /// Clears picker-sync suppression on the next UI frame so any
        /// <see cref="Picker.SelectedIndexChanged"/> events queued by a programmatic
        /// <c>ItemsSource</c>/<c>SelectedIndex</c> update are still ignored.
        /// </summary>
        private void ExitPickerSyncSuppress()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_pickerSyncSuppressCount > 0)
                    _pickerSyncSuppressCount--;
            });
        }

        private void UpdateKeyPickerSelection()
        {
            if (KeyPicker.ItemsSource is not string[] items) return;
            var idx = Array.IndexOf(items, _session.Key);
            EnterPickerSyncSuppress();
            try
            {
                if (idx >= 0 && KeyPicker.SelectedIndex != idx)
                    KeyPicker.SelectedIndex = idx;
                if (idx >= 0 && PracticeKeyPicker != null && PracticeKeyPicker.SelectedIndex != idx)
                    PracticeKeyPicker.SelectedIndex = idx;
            }
            finally
            {
                ExitPickerSyncSuppress();
            }
        }

        private void MarkChildKeyScaleOverrideIfNeeded()
        {
            if (_session.ChildLevel > 0)
                _session.MarkChildPracticeSettingsCustomized();
        }

        /// <summary>
        /// Reads the selected key from a picker index (avoids stale <see cref="Picker.SelectedItem"/> on programmatic sync).
        /// </summary>
        private static string? GetPickerKeyShortName(Picker picker)
        {
            if (picker.SelectedIndex < 0)
                return null;

            if (picker.ItemsSource is string[] items
                && picker.SelectedIndex < items.Length)
                return items[picker.SelectedIndex].Split(',')[0].Trim();

            return picker.SelectedItem?.ToString()?.Split(',')[0].Trim();
        }

        private void UpdateConcertKeyLabel()
        {
            var text = $"(Concert {_session.GetConcertKey()})";
            ConcertKeyLabel.Text = text;
            if (PracticeConcertKeyLabel != null) PracticeConcertKeyLabel.Text = text;
        }

        private void InitializePracticePickers(string[] instrumentOptions)
        {
            if (PracticeInstrumentPicker == null || PracticeKeyPicker == null || PracticeScaleTunePicker == null)
                return;

            if (PracticeInstrumentPicker.ItemsSource == null)
            {
                PracticeInstrumentPicker.ItemsSource = instrumentOptions;
                PracticeInstrumentPicker.SelectedIndex = InstrumentPicker?.SelectedIndex ?? 0;
            }

            if (PracticeKeyPicker.ItemsSource == null && KeyPicker?.ItemsSource != null)
            {
                PracticeKeyPicker.ItemsSource = KeyPicker.ItemsSource;
                PracticeKeyPicker.SelectedIndex = KeyPicker.SelectedIndex;
            }

            if (!_practicePickerEventsWired)
            {
                PracticeInstrumentPicker.SelectedIndexChanged += PracticeInstrumentPicker_SelectedIndexChanged;
                PracticeKeyPicker.SelectedIndexChanged += PracticeKeyPicker_SelectedIndexChanged;
                _practicePickerEventsWired = true;
            }

            UpdatePracticePlayItemPickerCore();
        }

        private void EnsurePracticePickersReady()
        {
            if (PracticeInstrumentPicker == null || PracticeKeyPicker == null || PracticeScaleTunePicker == null)
                return;

            if (PracticeInstrumentPicker.ItemsSource == null && InstrumentPicker?.ItemsSource is string[] instrumentOptions)
                InitializePracticePickers(instrumentOptions);
            else
                UpdatePracticePlayItemPickerCore();

            UpdateKeyPickerVisibility();
            UpdateConcertKeyLabel();
        }

        private void UpdateKeyPickerVisibility()
        {
            var show = _session.Tune != "Tuner" && _session.Tune != "Arpeggio";
            if (KeyPicker != null)
            {
                KeyPicker.IsVisible = show;
                KeyPicker.IsEnabled = show;
            }
            if (KeyLabel != null) KeyLabel.IsVisible = show;
            if (KeyBorder != null) KeyBorder.IsVisible = show;
            if (ConcertKeyLabel != null) ConcertKeyLabel.IsVisible = show;

            if (PracticeKeyPicker != null)
            {
                PracticeKeyPicker.IsVisible = show;
                PracticeKeyPicker.IsEnabled = show;
            }
            if (PracticeKeyLabel != null) PracticeKeyLabel.IsVisible = show;
            if (PracticeKeyBorder != null) PracticeKeyBorder.IsVisible = show;
            if (PracticeConcertKeyLabel != null) PracticeConcertKeyLabel.IsVisible = show;
        }

        private void UpdateTunerStaffDisplay()
        {
            if (_staffDrawable == null || _session.Tune != "Tuner")
                return;

            var tunerNote = NoteSessionService.TryBuildGeneratedNoteFromSpelledName(_session.TunerLastNoteName);
            var upper = tunerNote != null
                ? new List<GeneratedNote> { tunerNote }
                : new List<GeneratedNote>();

            _staffDrawable.LowerNotes = new List<GeneratedNote>();
            _staffDrawable.LowerBarBeats = new List<double>();
            _staffDrawable.LowerNoteStates = Array.Empty<StaffNoteState>();
            _staffDrawable.LowerAlpha = 0f;

            _staffDrawable.UpperNotes = upper;
            _staffDrawable.UpperBarBeats = upper.Count > 0 ? new List<double> { 4.0 } : new List<double>();
            _staffDrawable.UpperNoteStates = new StaffNoteState[upper.Count];
            if (upper.Count > 0)
                _staffDrawable.UpperNoteStates[0] = StaffNoteState.Current;
            _staffDrawable.IsUpperActive = true;
            _staffDrawable.ActiveNoteIndex = upper.Count > 0 ? 0 : -1;
            _staffDrawable.UpperAlpha = 1f;
            _staffDrawable.UpperHasEndBar = false;
            _staffDrawable.InvalidateLayoutCache();

            _session.NotesToDraw.Clear();
            _session.FeedbackViewModels.Clear();
            if (tunerNote != null)
            {
                _session.NotesToDraw.Add(new NoteInfo
                {
                    Midi = tunerNote.MidiNumber,
                    Name = tunerNote.SpelledName,
                    TargetFreq = tunerNote.TargetFrequency,
                    X = 0f,
                    Duration = tunerNote.Duration,
                });
                _session.FeedbackViewModels.Add(new FeedbackItem(0, 0, 0, false));
            }

            MainThread.BeginInvokeOnMainThread(() => TunerGraphicsView?.Invalidate());
        }

        private void EnterTunerMode()
        {
            _repeatSameSnapshot = null;
            _session.IsRandomMode = false;
            _session.Tune = "Tuner";
            _session.ClearTunerDetection();
            _session.NotesToDraw.Clear();
            _session.FeedbackViewModels.Clear();
            Preferences.Default.Set("SelectedTune", "Tuner");
            IsAutoRepeatVisible = false;
            UpdateTunerStaffDisplay();
            UpdateKeyPickerVisibility();
            UpdatePracticePlayItemPicker();
            UpdateTunerVisibility();
        }

        private void UpdatePickersContainerVisibility()
        {
            // Scale/key/instrument pickers live on the What to Play page.
            PickersContainer.IsVisible = false;
        }


        private void UpdateTunerVisibility()
        {
            var isTuner = _session.Tune == "Tuner";

            if (StaffBorder != null)
            {
                StaffBorder.IsVisible = false;
                StaffBorder.IsVisible = !isTuner;
            }

            if (TunerGrid != null)
                TunerGrid.IsVisible = isTuner;

            OnPropertyChanged(nameof(IsChildLevelSliderVisible));
            OnPropertyChanged(nameof(IsBottomPickersVisible));
            OnPropertyChanged(nameof(IsBottomButtonRowVisible));

            if (isTuner)
            {
                if (TunerBorder != null)
                    TunerBorder.IsVisible = true;
                if (TunerGrid?.ColumnDefinitions.Count > 0)
                    TunerGrid.ColumnDefinitions[0] = new ColumnDefinition(GridLength.Star);

                _session.SessionCompleted = false;
                UpdateTunerStaffDisplay();
                ApplyTunerHeight();
                Dispatcher.Dispatch(ApplyTunerHeight);
                if (!_isRunning)
                {
                    _ = StartListeningAndEvaluatingAsync();
                }
            }
            else
            {
                if (TunerBorder != null)
                    TunerBorder.IsVisible = false;
                if (StaffAreaStack != null)
                    StaffAreaStack.HeightRequest = -1;
                if (TunerGrid != null)
                    TunerGrid.HeightRequest = -1;
                if (TunerBorder != null)
                    TunerBorder.HeightRequest = -1;
                if (TunerGraphicsView != null)
                    TunerGraphicsView.HeightRequest = -1;
                if (TunerInfoBorder != null)
                    TunerInfoBorder.HeightRequest = -1;
                if (MainPageMainLayout != null)
                    MainPageMainLayout.Spacing = 16;
            }
        }

        private string[] BuildScaleTuneOptions()
        {
            var practiceTuneTitles = musicmate.Models.TuneLibrary.All.Select(t => t.Title).ToArray();
            var arpeggioTitles = BuildArpeggioPickerChoices().Select(choice => choice.Label).ToArray();
            return new[] { "Tuner", PlayModePickerOptions.HalfThroughSixteenthNotes }
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
            var seen = new HashSet<string>(StringComparer.Ordinal);

            // Match WhatToPlayPage: list arpeggios from fixed root keys, not scale degrees
            // of the current session key (which changes when an arpeggio is selected).
            foreach (string rootKey in GetArpeggioRootKeys())
            {
                foreach (var pattern in availability.Patterns)
                {
                    string rootNote = ChooseArpeggioRootInRange(rootKey);
                    string rootName = TrimOctave(rootNote);
                    string label = $"{rootName} {pattern.DisplayName.ToLowerInvariant()}";
                    if (!seen.Add($"{rootNote}|{pattern.Id}"))
                        continue;
                    if (_arpeggioPickerChoices.ContainsKey(label))
                        continue;

                    var choice = new ArpeggioPickerChoice(label, pattern, rootNote);
                    _arpeggioPickerChoices[label] = choice;
                    choices.Add(choice);
                }
            }

            return choices;
        }

        private static IEnumerable<string> GetArpeggioRootKeys()
        {
            yield return "C";
            yield return "G";
            yield return "D";
            yield return "A";
            yield return "E";
            yield return "B";
            yield return "F#";
            yield return "C#";
            yield return "F";
            yield return "Bb";
            yield return "Eb";
            yield return "Ab";
            yield return "Db";
            yield return "Gb";
            yield return "Cb";
        }

        private string ChooseArpeggioRootInRange(string rootKey)
        {
            const int baseOctave = 4;
            int rootMidi = NoteSessionService.NoteNameToMidi($"{rootKey}{baseOctave}");
            int minMidi = NoteSessionService.NoteNameToMidi(_session.LowestNote);
            int maxMidi = NoteSessionService.NoteNameToMidi(_session.HighestNote);
            if (minMidi < 0 || maxMidi < minMidi)
                return $"{rootKey}{baseOctave}";

            int candidate = rootMidi;
            while (candidate < minMidi)
                candidate += 12;
            while (candidate > maxMidi)
                candidate -= 12;

            int octave = baseOctave + ((candidate - rootMidi) / 12);
            return $"{rootKey}{octave}";
        }

        private static string TrimOctave(string noteName)
            => new(noteName.TakeWhile(c => !char.IsDigit(c)).ToArray());

        private static string GetArpeggioKeySignature(ArpeggioPickerChoice choice)
        {
            var root = NormalizeMajorKeyName(TrimOctave(choice.RootNote));
            if (UsesMinorFamilyKeySignature(choice.Pattern))
                return RelativeMajorKeyForMinorRoot(root);

            return root;
        }

        private static bool UsesMinorFamilyKeySignature(ArpeggioPattern pattern)
            => pattern.SemitoneIntervals.Contains(3) && !pattern.SemitoneIntervals.Contains(4);

        private static string RelativeMajorKeyForMinorRoot(string minorRoot) => minorRoot switch
        {
            "A" => "C",
            "E" => "G",
            "B" => "D",
            "F#" => "A",
            "C#" => "E",
            "G#" => "B",
            "D#" => "F#",
            "A#" => "C#",
            "D" => "F",
            "G" => "Bb",
            "C" => "Eb",
            "F" => "Ab",
            "Bb" => "Db",
            "Eb" => "Gb",
            "Ab" => "Cb",
            _ => minorRoot
        };

        private static string NormalizeMajorKeyName(string key) => key switch
        {
            "A#" => "Bb",
            "D#" => "Eb",
            "G#" => "Ab",
            "E#" => "F",
            "B#" => "C",
            "Fb" => "E",
            _ => key
        };

        private void UpdateScaleTunePicker()
        {
            if (ScaleTunePicker == null)
            {
                UpdatePracticePlayItemPickerCore();
                return;
            }

            var items = BuildScaleTuneOptions();
            var selection = LayoutTestTune.IsEnabled
                ? PlayModePickerOptions.HalfThroughSixteenthNotes
                : _session.Tune == "Tuner" ? "Tuner"
                : _session.Tune == "Practice Tune" ? (_session.CurrentTune?.Title ?? string.Empty)
                : _session.Tune == "Arpeggio" ? _session.SelectedArpeggioDisplay
                : _session.SelectedScale;
            var idx = Array.IndexOf(items, selection);
            if (idx < 0 && _session.Tune == "Arpeggio")
            {
                idx = Array.FindIndex(items, label =>
                    _arpeggioPickerChoices.TryGetValue(label, out var choice)
                    && choice.Pattern.Id == _session.SelectedArpeggioId
                    && choice.RootNote == _session.SelectedArpeggioRoot);
            }

            EnterPickerSyncSuppress();
            try
            {
                ScaleTunePicker.ItemsSource = items;
                if (idx >= 0 && ScaleTunePicker.SelectedIndex != idx)
                    ScaleTunePicker.SelectedIndex = idx;
                if (idx >= 0)
                    _lastValidScaleTuneIndex = idx;
            }
            finally
            {
                ExitPickerSyncSuppress();
            }

            UpdatePracticePlayItemPickerCore();
        }

        private bool IsArpeggioSelectionApplied(string label)
            => _session.Tune == "Arpeggio"
               && string.Equals(_session.SelectedArpeggioDisplay, label, StringComparison.Ordinal);

        private async Task ApplyArpeggioSelectionAsync(ArpeggioPickerChoice arpeggioChoice, string preferenceLabel, int validIndex)
        {
            _applyingArpeggioSelection = true;
            _suppressSessionRegenerate = true;
            EnterPickerSyncSuppress();
            try
            {
                // Select arpeggio before Key so PropertyChanged picker-sync handlers
                // see the new display name, not the previous arpeggio (e.g. Ab vs Gb loop).
                _session.SelectArpeggio(arpeggioChoice.Pattern, arpeggioChoice.RootNote, arpeggioChoice.Label);
                _session.Key = GetArpeggioKeySignature(arpeggioChoice);
                Preferences.Default.Set("SelectedTune", preferenceLabel);
                IsAutoRepeatVisible = true;
                UpdateKeyPickerSelection();
                UpdateConcertKeyLabel();
                UpdateKeyPickerVisibility();
                UpdateScaleTunePickerWithoutPracticeCascade();
            }
            finally
            {
                _suppressSessionRegenerate = false;
                ExitPickerSyncSuppress();
            }

            if (validIndex >= 0)
                _lastValidPlayItemIndex = validIndex;
            _lastValidScaleTuneIndex = validIndex;

            try
            {
                await RegenerateNotesAsync();
            }
            finally
            {
                MainThread.BeginInvokeOnMainThread(() => _applyingArpeggioSelection = false);
            }
        }

        /// <summary>Syncs the legacy scale/tune picker without re-entering practice picker sync.</summary>
        private void UpdateScaleTunePickerWithoutPracticeCascade()
        {
            var items = BuildScaleTuneOptions();
            var selection = LayoutTestTune.IsEnabled
                ? PlayModePickerOptions.HalfThroughSixteenthNotes
                : _session.Tune == "Tuner" ? "Tuner"
                : _session.Tune == "Practice Tune" ? (_session.CurrentTune?.Title ?? string.Empty)
                : _session.Tune == "Arpeggio" ? _session.SelectedArpeggioDisplay
                : _session.SelectedScale;
            var idx = Array.IndexOf(items, selection);
            if (idx < 0 && _session.Tune == "Arpeggio")
            {
                idx = Array.FindIndex(items, label =>
                    _arpeggioPickerChoices.TryGetValue(label, out var choice)
                    && choice.Pattern.Id == _session.SelectedArpeggioId
                    && choice.RootNote == _session.SelectedArpeggioRoot);
            }

            EnterPickerSyncSuppress();
            try
            {
                ScaleTunePicker.ItemsSource = items;
                if (idx >= 0 && ScaleTunePicker.SelectedIndex != idx)
                    ScaleTunePicker.SelectedIndex = idx;
                if (idx >= 0)
                    _lastValidScaleTuneIndex = idx;
            }
            finally
            {
                ExitPickerSyncSuppress();
            }

            UpdatePracticePlayItemPickerCore();
        }

        private PlayModeCategory GetActivePlayModeCategory()
        {
            if (LayoutTestTune.IsEnabled
                || PlayModePickerOptions.IsRhythmNoteTuneSelection(
                    Preferences.Default.Get<string?>("SelectedTune", null)))
                return PlayModeCategory.Tunes;
            if (PlayModePickerOptions.UsesOtherPicker(_session, LayoutTestTune.IsEnabled))
                return PlayModeCategory.Other;
            if (_session.Tune == "Practice Tune")
                return PlayModeCategory.Tunes;
            if (_session.Tune == "Arpeggio")
                return PlayModeCategory.Arpeggios;
            return PlayModeCategory.Scales;
        }

        private string[] BuildPracticePlayItemOptions(PlayModeCategory category)
            => category switch
            {
                PlayModeCategory.Tunes =>
                    PlayModePickerOptions.BuildTunePickerOptions(),
                PlayModeCategory.Scales =>
                    NoteSessionService.ScalePickerOptions.ToArray(),
                PlayModeCategory.Arpeggios =>
                    BuildArpeggioPickerChoices().Select(choice => choice.Label).ToArray(),
                _ => PlayModePickerOptions.OtherOptions.ToArray()
            };

        private string GetPracticePlayItemSelection(PlayModeCategory category)
            => category switch
            {
                PlayModeCategory.Tunes => LayoutTestTune.IsEnabled
                    ? PlayModePickerOptions.HalfThroughSixteenthNotes
                    : _session.CurrentTune?.Title ?? string.Empty,
                PlayModeCategory.Scales => _session.SelectedScale,
                PlayModeCategory.Arpeggios => _session.SelectedArpeggioDisplay,
                PlayModeCategory.Other => PlayModePickerOptions.ResolveOtherSelection(
                    _session, LayoutTestTune.IsEnabled),
                _ => string.Empty
            };

        private int FindPracticePlayItemIndex(string[] items, PlayModeCategory category, string selection)
        {
            var idx = Array.IndexOf(items, selection);
            if (idx >= 0 || category != PlayModeCategory.Arpeggios)
                return idx;

            idx = Array.FindIndex(items, label =>
                _arpeggioPickerChoices.TryGetValue(label, out var choice)
                && choice.Pattern.Id == _session.SelectedArpeggioId
                && choice.RootNote == _session.SelectedArpeggioRoot);

            if (idx >= 0)
                return idx;

            return Array.FindIndex(items, label =>
                _arpeggioPickerChoices.TryGetValue(label, out var choice)
                && choice.Label == _session.SelectedArpeggioDisplay);
        }

        private void UpdatePracticePlayItemPickerCore()
        {
            if (PracticeScaleTunePicker == null)
                return;

            var category = GetActivePlayModeCategory();
            var items = BuildPracticePlayItemOptions(category);
            var selection = GetPracticePlayItemSelection(category);
            var idx = FindPracticePlayItemIndex(items, category, selection);

            EnterPickerSyncSuppress();
            try
            {
                PracticeScaleTunePicker.ItemsSource = items;
                if (idx >= 0)
                {
                    if (PracticeScaleTunePicker.SelectedIndex != idx)
                        PracticeScaleTunePicker.SelectedIndex = idx;
                    _lastValidPlayItemIndex = idx;
                }
            }
            finally
            {
                ExitPickerSyncSuppress();
            }
        }

        private void UpdatePracticePlayItemPicker()
            => UpdatePracticePlayItemPickerCore();

        private void PracticeInstrumentPicker_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (PracticeInstrumentPicker == null) return;
            var idx = PracticeInstrumentPicker.SelectedIndex;
            if (idx < 0) return;
            var fullInstrument = NoteSessionService.InstrumentOptions[idx];
            _session.Instrument = fullInstrument;
            if (InstrumentPicker.SelectedIndex != idx)
                InstrumentPicker.SelectedIndex = idx;
            SelectedInstrumentShort = _session.InstrumentDisplayName;
        }

        private async void PracticeKeyPicker_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (PracticeKeyPicker == null) return;
            if (IsPickerSyncSuppressed) return;
            var shortKey = GetPickerKeyShortName(PracticeKeyPicker);
            if (shortKey == null) return;
            if (IsPremiumKey(shortKey) && !StatusService.Instance.IsPremiumUser)
            {
                var purchased = await PremiumPromptHelper.ShowAsync(this,
                    onDecline: () => PracticeKeyPicker.SelectedIndex = _lastFreeKeyIndex);
                if (!purchased) return;
            }
            else { _lastFreeKeyIndex = PracticeKeyPicker.SelectedIndex; }
            _session.Key = shortKey;
            MarkChildKeyScaleOverrideIfNeeded();
            EnterPickerSyncSuppress();
            try
            {
                if (KeyPicker.SelectedIndex != PracticeKeyPicker.SelectedIndex)
                    KeyPicker.SelectedIndex = PracticeKeyPicker.SelectedIndex;
            }
            finally { ExitPickerSyncSuppress(); }
            UpdateConcertKeyLabel();
            _staffDrawable?.InvalidateLayoutCache();
            if (_isPageVisible && !_suppressSessionRegenerate)
                await RegenerateNotesAsync();
        }

        private void PracticeScaleTunePickerChanged(object? sender, EventArgs e)
        {
            if (IsPickerSyncSuppressed) return;
            OnPracticePlayItemPickerChanged();
        }

        private async void OnPracticePlayItemPickerChanged()
        {
            if (_applyingArpeggioSelection) return;
            if (PracticeScaleTunePicker == null) return;

            var items = PracticeScaleTunePicker.ItemsSource as string[];
            var idx = PracticeScaleTunePicker.SelectedIndex;
            if (items == null || idx < 0 || idx >= items.Length) return;

            var selected = items[idx];
            switch (GetActivePlayModeCategory())
            {
                case PlayModeCategory.Tunes:
                    await ApplyTuneSelectionAsync(selected, idx);
                    break;
                case PlayModeCategory.Scales:
                    await ApplyScaleSelectionAsync(selected, idx);
                    break;
                case PlayModeCategory.Arpeggios:
                    await ApplyArpeggioSelectionAsync(selected, idx);
                    break;
                case PlayModeCategory.Other:
                    await ApplyOtherSelectionAsync(selected, idx);
                    break;
            }
        }

        private async Task ApplyTuneSelectionAsync(string selected, int idx)
        {
            if (PlayModePickerOptions.IsRhythmNoteTuneSelection(selected)
                || selected == PlayModePickerOptions.HalfThroughSixteenthNotes)
            {
                _lastValidPlayItemIndex = idx;
                LayoutTestTune.SetEnabled(true);
                PlayModePickerOptions.ApplyRhythmNoteTuneSelection(_session);
                IsAutoRepeatVisible = true;
                UpdateKeyPickerSelection();
                UpdateConcertKeyLabel();
                UpdateKeyPickerVisibility();
                await RegenerateNotesAsync();
                return;
            }

            var practiceTune = musicmate.Models.TuneLibrary.All.FirstOrDefault(t => t.Title == selected);
            if (practiceTune == null) return;

            _lastValidPlayItemIndex = idx;
            LayoutTestTune.SetEnabled(false);
            _session.IsRandomMode = false;
            _session.SelectPracticeTune(practiceTune);
            Preferences.Default.Set("SelectedTune", selected);
            IsAutoRepeatVisible = true;
            UpdateKeyPickerSelection();
            UpdateConcertKeyLabel();
            UpdateKeyPickerVisibility();
            await RegenerateNotesAsync();
        }

        private async Task ApplyScaleSelectionAsync(string selected, int idx)
        {
            if (!NoteSessionService.IsNamedScaleOption(selected))
                return;

            if (!FreeScales.Contains(selected) && !StatusService.Instance.IsPremiumUser)
            {
                var purchased = await PremiumPromptHelper.ShowAsync(this,
                    onDecline: () => PracticeScaleTunePicker.SelectedIndex = _lastValidPlayItemIndex);
                if (!purchased)
                    return;
            }

            _lastValidPlayItemIndex = idx;
            LayoutTestTune.SetEnabled(false);
            _session.IsRandomMode = false;
            _session.Tune = "Selected Scale";
            if (!_session.TryApplyScalePickerSelection(selected, out _))
            {
                var fallbackIdx = Array.IndexOf(
                    PracticeScaleTunePicker.ItemsSource as string[] ?? Array.Empty<string>(),
                    _session.SelectedScale);
                if (fallbackIdx >= 0)
                    _lastValidPlayItemIndex = fallbackIdx;
            }
            MarkChildKeyScaleOverrideIfNeeded();
            Preferences.Default.Set("SelectedTune", selected);
            IsAutoRepeatVisible = true;
            UpdateKeyPickerVisibility();
            UpdatePracticePlayItemPickerCore();
            await RegenerateNotesAsync();
        }

        private async Task ApplyArpeggioSelectionAsync(string selected, int idx)
        {
            if (!_arpeggioPickerChoices.TryGetValue(selected, out var arpeggioChoice))
                return;

            if (IsArpeggioSelectionApplied(selected))
                return;

            _lastValidPlayItemIndex = idx;
            LayoutTestTune.SetEnabled(false);
            _session.IsRandomMode = false;
            await ApplyArpeggioSelectionAsync(arpeggioChoice, selected, idx);
        }

        private async Task ApplyOtherSelectionAsync(string selected, int idx)
        {
            _lastValidPlayItemIndex = idx;

            if (selected == PlayModePickerOptions.Tuner)
            {
                EnterTunerMode();
                return;
            }

            if (selected == PlayModePickerOptions.RandomMelodic)
            {
                if (_session.IsRandomMode)
                {
                    UpdateKeyPickerVisibility();
                    return;
                }

                LayoutTestTune.SetEnabled(false);
                PlayModePickerOptions.ApplyOtherSelection(_session, selected);
                IsAutoRepeatVisible = true;
                UpdateKeyPickerVisibility();
                await RegenerateNotesAsync();
                return;
            }

            if (selected == NoteSessionService.ScaleSelectionByLevel)
            {
                LayoutTestTune.SetEnabled(false);
                PlayModePickerOptions.ApplyOtherSelection(_session, selected);
                IsAutoRepeatVisible = true;
                UpdateKeyPickerVisibility();
                UpdatePracticePlayItemPickerCore();
                await RegenerateNotesAsync();
            }
        }

        private async void OnScaleTunePickerChanged(object? sender, EventArgs e)
        {
            DebugLog.WriteLine($"[PickerDBG] OnScaleTunePickerChanged fired. suppress={IsPickerSyncSuppressed} sender={sender?.GetType().Name}");
            if (IsPickerSyncSuppressed) return;
            var sourcePicker = (sender as Picker) ?? ScaleTunePicker;
            var items = sourcePicker.ItemsSource as string[];
            var idx = sourcePicker.SelectedIndex;
            DebugLog.WriteLine($"[PickerDBG] sourcePicker={sourcePicker.GetType().Name} idx={idx} items null={items == null} len={items?.Length}");
            if (items == null || idx < 0 || idx >= items.Length) return;
            var selected = items[idx];
            DebugLog.WriteLine($"[PickerDBG] selected='{selected}'");

            if (PlayModePickerOptions.IsRhythmNoteTuneSelection(selected)
                || selected == PlayModePickerOptions.HalfThroughSixteenthNotes)
            {
                _lastValidScaleTuneIndex = sourcePicker.SelectedIndex;
                LayoutTestTune.SetEnabled(true);
                PlayModePickerOptions.ApplyRhythmNoteTuneSelection(_session);
                IsAutoRepeatVisible = true;
                UpdateKeyPickerSelection();
                UpdateConcertKeyLabel();
                UpdateKeyPickerVisibility();
                await RegenerateNotesAsync();
                return;
            }

            // Check if the selection is a practice tune title
            var practiceTune = musicmate.Models.TuneLibrary.All.FirstOrDefault(t => t.Title == selected);
            DebugLog.WriteLine($"[PickerDBG] practiceTune={practiceTune?.Title ?? "null"} TuneLibrary.All count={musicmate.Models.TuneLibrary.All.Count}");
            if (practiceTune != null)
            {
                _lastValidScaleTuneIndex = sourcePicker.SelectedIndex;
                _session.SelectPracticeTune(practiceTune);
                Preferences.Default.Set("SelectedTune", selected);
                IsAutoRepeatVisible = true;
                UpdateKeyPickerSelection();
                UpdateConcertKeyLabel();
                UpdateKeyPickerVisibility();
                await RegenerateNotesAsync();
                return;
            }

            if (_arpeggioPickerChoices.TryGetValue(selected, out var arpeggioChoice))
            {
                if (IsArpeggioSelectionApplied(selected))
                    return;

                _lastValidScaleTuneIndex = sourcePicker.SelectedIndex;
                LayoutTestTune.SetEnabled(false);
                _session.IsRandomMode = false;
                await ApplyArpeggioSelectionAsync(arpeggioChoice, selected, sourcePicker.SelectedIndex);
                return;
            }

            if (selected == "Tuner")
            {
                _lastValidScaleTuneIndex = sourcePicker.SelectedIndex;
                EnterTunerMode();
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
            UpdateKeyPickerSelection();
            UpdateConcertKeyLabel();
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
                _session.Instrument = s;
                SelectedInstrumentShort = _session.InstrumentDisplayName;
                // Hide picker and show label immediately
                IsInstrumentPickerVisible = false;
                IsInstrumentLabelVisible = true;
                // Workaround: immediately unfocus picker to prevent unwanted stage
                InstrumentPicker.Unfocus();
            }
        }

        private void InstrumentPicker_Unfocused(object? sender, EventArgs e)
        {
            IsInstrumentPickerVisible = false;
            IsInstrumentLabelVisible = true;
        }

        async void OnKeyPickerChangedWithPrompt(object? sender, EventArgs e)
        {
            if (IsPickerSyncSuppressed) return;
            var shortKey = GetPickerKeyShortName(KeyPicker);
            if (shortKey == null)
                return;

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

        private async void OnStartStopToggleClicked(object? sender, EventArgs e)
        {
            var plan = PracticeSessionLifecycle.PlanStopToggle(
                _isRunning, _session.RepeatSameTune, _repeatSameSnapshot);

            if (plan.Action is PracticeSessionLifecycle.StopToggleAction.StopRestoreRepeatSame
                or PracticeSessionLifecycle.StopToggleAction.StopRegenerateFresh)
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
                    _holdResultForChildSession = false;
                    _freezeStaff = false;
                    SetButtonStates(false);

                    _session.Reset();

                    if (plan.ClearRepeatSameSnapshot)
                        _repeatSameSnapshot = null;

                    if (plan.Action == PracticeSessionLifecycle.StopToggleAction.StopRestoreRepeatSame)
                        await RestoreRepeatSameSnapshotAsync(_repeatSameSnapshot!.Notes);
                    else
                        await RegenerateNotesAsync();

                    StatusService.Instance.StatusMessage = GetCurrentPlayItemName();
                }
                catch (Exception ex)
                {
                    DebugLog.WriteLine($"Stop error: {ex}");
                    SetButtonStates(false);
                }
            }
            else
            {
                _holdResultForChildSession = false;
                _session.SessionCompleted = false;

                await StartListeningAndEvaluatingAsync(
                    forceNewNotes: plan.ForceNewNotes,
                    scaleKeyTrigger: plan.ScaleKeyTrigger);
            }
        }

        private void OnStartStopToggleClicked(object sender, TappedEventArgs e)
        {

        }

        private void OnAutoRepeatScaleClicked(object? sender, EventArgs e)
        {
            // Toggle auto-repeat for scale mode
            AutoRepeat = !AutoRepeat;
            RepeatSameTune = false; // Not applicable for scales
        }
    }
}

