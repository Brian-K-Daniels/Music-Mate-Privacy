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
        private readonly DisplayedTuneHistory _displayedTunes = null!;
        private readonly IAudioCaptureService _audio = null!;
        private readonly IAudioPlaybackService _player = null!;
        
        private StaffDrawable? _staffDrawable;
        private readonly SessionDatabase _sessionDb = null!;
        private readonly SessionResultDatabase _sessionResultDb = null!;
        private readonly NoteAttemptDatabase _noteAttemptDb = null!;
        private readonly IOrientationService _orientation = null!;
        private readonly ThemeService _theme_service = null!;
        private readonly SavedTuneStore _savedTunes = null!;

        private readonly object _processLock = new();
        private DateTime _lastProcess = DateTime.MinValue;
        private CancellationTokenSource? _playCts;
        private bool _isPlaying = false;
        private bool _isRunning = false;
        private bool _isBelowThreshold = true;
        private bool _dismissedResultBannerForFirstSound;
        private bool _isProgrammaticColorConfirm = false;
        private bool _isPageVisible = false;
        /// <summary>
        /// Soft-pause: listening was active when Music was obscured. Keep <see cref="_isRunning"/>
        /// so return can resume Count-In without regenerating notes or treating hide as Stop.
        /// </summary>
        private bool _listeningPausedForPageHide;
        /// <summary>
        /// Sticky until Go: user pressed Stop (or equivalent). Prevents AutoStart / Count-In
        /// from restarting on return after an intentional stop.
        /// </summary>
        private bool _userStoppedListening;
        /// <summary>Tracks whether the Tuner UI surface is currently shown on this page.</summary>
        private bool _tunerUiActive;
        /// <summary>
        /// Dedicated CTS for soft-pause Count-In resume so OnNavigatedTo AutoStart cannot
        /// cancel it (they previously shared <see cref="_autoStartCts"/>).
        /// </summary>
        private CancellationTokenSource? _resumeListeningCts;
        // A new GUID is assigned each time a session starts (see StartListeningAndEvaluatingAsync).
        // It is stored with every NoteAttempt so attempts can be grouped by session.
        private string _currentSessionId = string.Empty;
        private bool _applyingArpeggioSelection;
        private readonly SemaphoreSlim _regenerateSemaphore = new SemaphoreSlim(1, 1);
        private int _pickerSyncSuppressCount;
        private bool IsPickerSyncSuppressed => _pickerSyncSuppressCount > 0;

        private static Task EnsureMainThreadAsync()
            => MainThread.IsMainThread
                ? Task.CompletedTask
                : MainThread.InvokeOnMainThreadAsync(() => { });

        private void RunOnMainThread(Action action)
        {
            if (MainThread.IsMainThread)
                action();
            else
                MainThread.BeginInvokeOnMainThread(action);
        }

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
        private CancellationTokenSource? _childLevelApplyCts;
        private CancellationTokenSource? _staffLayoutSettleCts;
        private int? _pendingChildLevel;
        private bool _suppressSessionRegenerate;
        /// <summary>
        /// When true, <see cref="UpdateStaffDisplayAsync"/> generates into the drawable
        /// without ApplyStaffHeight/Invalidate so uniqueness retries are not painted.
        /// </summary>
        private bool _suppressStaffViewCommit;
        /// <summary>
        /// When false, StaffGraphicsView.SizeChanged must not rebuild notes — OnAppearing
        /// still needs to hydrate ChildLevel / level-derived settings first.
        /// </summary>
        private bool _allowStaffLayoutSettle;
        private const string ChildLevelPrefKey = "ChildPractice.Level";
        private readonly Dictionary<string, ArpeggioPickerChoice> _arpeggioPickerChoices = new(StringComparer.Ordinal);
        /// <summary>
        /// Canvas width last used to create the current packed staff layout (0 = unknown).
        /// Updated only after an actual pack/repack — never stamped from a settle skip.
        /// </summary>
        private double _staffWidthUsedForLayout;
        /// <summary>Cached generated page for width-only re-Split (no regeneration).</summary>
        private StaffPagePackState? _staffPagePack;
        /// <summary>True when settle wanted a repack but freeze/hold blocked it.</summary>
        private bool _pendingStaffWidthRepack;

        // ── Tuner reference-tone (tuning fork) ─────────────────────────────────
        private bool _isReferenceTonePlaying;
        private CancellationTokenSource? _referenceToneCts;
        private int _referenceToneGeneration;
        private bool _isTunerPitchPlaying;
        private CancellationTokenSource? _tunerPitchCts;
        private int _tunerPitchGeneration;
        /// <summary>Source of truth for picker, staff, Play Note pitch, and ◀/▶ enablement.</summary>
        private int _referenceWrittenMidi;
        private IReadOnlyList<TunerReferenceNoteChoice> _referenceNoteChoices =
            Array.Empty<TunerReferenceNoteChoice>();
        private bool _isUpdatingReferenceNoteUi;
        private string? _lastLoggedTunerHeardNote;
        /// <summary>Tuner-local BPM for repeated reference playback (independent of Settings until synced).</summary>
        private int _tunerTempoBpm = NoteSessionService.DefaultTempo;
        private bool _isUpdatingTunerTempoPicker;
        private const string PrefTunerTempoKey = "musicmate.TunerTempo";
        /// <summary>Fraction of each beat that the reference note sounds (remainder is silence).</summary>

        // ── Waiting count-in (Music practice) ─────────────────────────────────
        private WaitingCountInPlayer? _waitingCountInPlayer;
        private CancellationTokenSource? _waitingCountInCts;
        private bool _waitingCountInActive;
        private int _waitingCountInGeneration;
        /// <summary>Tracks IgnoreAudio edge for [AudioSuppress] OFF logging.</summary>
        private bool _audioSuppressWasActive;
        private int _startListeningEpoch;
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

        private readonly PitchWindowAccumulator _pitchWindow = new();

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
        public bool IsTitlePlayButtonVisible =>
            TunerTitleChrome.IsPlayButtonVisible(_session?.Tune, _isRunning, _isPlaying);
        public bool IsBottomPickersVisible => _session?.Tune != "Tuner";
        public bool IsBottomButtonRowVisible => _session?.Tune != "Tuner";
        public bool IsChildLevelSliderVisible => _session?.ChildLevel > 0 && _session.Tune != "Tuner";
        public bool IsTempoControlVisible => _isTempoControlVisible && _session?.Tune != "Tuner";
        public bool IsTimeSignatureControlVisible =>
            _isTimeSignatureControlVisible && _session?.Tune != "Tuner";

        private bool _isTempoControlVisible;
        private bool _isTimeSignatureControlVisible;
        private bool _timeSignatureOptionButtonsBuilt;
        /// <summary>Ignore StaffBorder taps that fire in the same gesture as tempo/time-sig hit-target Clicked.</summary>
        private long _suppressStaffOverlayTapUntilMs;

        /// <summary>
        /// When true, the tempo hit Button shows translucent diagnostic chrome.
        /// Keep false in normal builds; hit area stays enlarged and TEMPO TAP logs remain.
        /// </summary>
        private static bool TempoHitTargetDiagnosticsVisible = false;

        public string MakeItEasyButtonText =>
            _session?.IsMakeItEasyActive == true ? "Make It Easy — ON" : "Make It Easy — OFF";

        public Color MakeItEasyButtonBackgroundColor
        {
            get
            {
                if (_session?.IsMakeItEasyActive == true)
                    return Color.FromArgb("#2E7D32");
                if (Application.Current?.Resources.TryGetValue("ThemeButtonBackground", out var bg) == true
                    && bg is Color themed)
                    return themed;
                return Color.FromArgb("#8B4513");
            }
        }

        public Color MakeItEasyButtonTextColor
        {
            get
            {
                if (_session?.IsMakeItEasyActive == true)
                    return Colors.White;
                if (Application.Current?.Resources.TryGetValue("ThemeContrastingText", out var fg) == true
                    && fg is Color themed)
                    return themed;
                return Colors.White;
            }
        }
        public bool IsEffectiveScaleLabelVisible =>
            _session?.IsRandomMode == true
            || _session?.ScaleSelectionMode == ScaleSelectionMode.Random;
        public string EffectiveScaleLabelText => _session?.EffectiveScaleDisplay ?? string.Empty;
        private string _practicePlayItemLabelText = string.Empty;
        public string PracticePlayItemLabelText
        {
            get => _practicePlayItemLabelText;
            private set
            {
                if (_practicePlayItemLabelText == value)
                    return;
                _practicePlayItemLabelText = value;
                OnPropertyChanged(nameof(PracticePlayItemLabelText));
            }
        }
        private void UpdateEffectiveScaleLabel()
        {
            if (!MainThread.IsMainThread)
            {
                RunOnMainThread(UpdateEffectiveScaleLabel);
                return;
            }

            OnPropertyChanged(nameof(IsEffectiveScaleLabelVisible));
            OnPropertyChanged(nameof(EffectiveScaleLabelText));
            if (_session?.IsRandomMode == true)
            {
                UpdatePracticePlayItemLabel();
                SyncPlayItemStatusMessage();
            }
        }

#if DEBUG
        private void RefreshBuildIdentificationLabels()
        {
            if (BuildIdentificationLabel is null)
                return;

            BuildIdentificationLabel.Text = BuildIdentification.FullMultiline;
            BuildIdentificationLabel.IsVisible = true;
        }

        private void RefreshMidi61DiagnosticLabel()
        {
            if (Midi61DiagnosticLabel is null)
                return;

            var lines = new List<string>();
            if (!string.IsNullOrEmpty(ChromaticMidi61Diagnostics.LastRendererInputSummary))
                lines.Add(ChromaticMidi61Diagnostics.LastRendererInputSummary);
            if (!string.IsNullOrEmpty(ChromaticMidi61Diagnostics.LastDrawTableSummary))
                lines.Add(ChromaticMidi61Diagnostics.LastDrawTableSummary);
            if (!string.IsNullOrEmpty(ChromaticMidi61Diagnostics.LastPlaybackSummary))
                lines.Add(ChromaticMidi61Diagnostics.LastPlaybackSummary);
            lines.Add(ChromaticMidi61Diagnostics.CompactStatusLine());

            Midi61DiagnosticLabel.Text = string.Join("\n", lines.Where(l => !string.IsNullOrWhiteSpace(l)));
            Midi61DiagnosticLabel.IsVisible = ChromaticMidi61Diagnostics.IsEnabled;
        }
#else
        private void RefreshBuildIdentificationLabels() { }
        private void RefreshMidi61DiagnosticLabel() { }
#endif

        private void RefreshNoteAttemptsDebugButtonVisibility()
        {
            if (NoteAttemptsDebugButton is null)
                return;

            bool show = NoteAttemptsViewerSettings.IsMusicPageButtonEnabled
                && IsBottomButtonRowVisible;
            NoteAttemptsDebugButton.IsVisible = show;
        }

        private async void OnNoteAttemptsDebugClicked(object? sender, EventArgs e)
        {
            try
            {
                await NavigationBusyService.GoToAsync("//NoteAttemptsDebugPage");
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[MusicPage] Note Attempts navigate ERROR: {ex}");
            }
        }
        private void UpdateNoteEmphasisBanner()
        {
            if (!MainThread.IsMainThread)
            {
                RunOnMainThread(UpdateNoteEmphasisBanner);
                return;
            }

            bool show = _session.HasTemporaryNoteEmphasis;
            if (NoteEmphasisBanner is not null)
                NoteEmphasisBanner.IsVisible = show;
            if (NoteEmphasisLabel is not null)
                NoteEmphasisLabel.Text = _session.TemporaryNoteEmphasisBannerText;
        }
        private async void OnClearNoteEmphasisClicked(object? sender, EventArgs e)
        {
            _session.ClearTemporaryNoteEmphasis("user-clear");
            UpdateNoteEmphasisBanner();
            await RegenerateNotesAsync();
        }
        /// <summary>
        /// Keeps the title status bar aligned with the random-mode picker label
        /// without clobbering live pitch-feedback messages.
        /// </summary>
        private void SyncPlayItemStatusMessage()
        {
            if (!MainThread.IsMainThread)
            {
                RunOnMainThread(SyncPlayItemStatusMessage);
                return;
            }

            if (StatusService.Instance.IsTemporaryMessageActive)
                return;

            if (_session.Tune == "Tuner")
            {
                StatusService.Instance.StatusMessage = _isTunerPitchPlaying
                    ? "Playing reference tone."
                    : _isRunning
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
                _displayedTunes = ServiceHelper.GetService<DisplayedTuneHistory>()
                    ?? new DisplayedTuneHistory();
                PlayModePickerOptions.ApplyPersistedSelection(_session);
                _sessionDb = ServiceHelper.GetService<SessionDatabase>()!;
                _sessionResultDb = ServiceHelper.GetService<SessionResultDatabase>()!;
                _noteAttemptDb = ServiceHelper.GetService<NoteAttemptDatabase>()!;
                _savedTunes = ServiceHelper.GetService<SavedTuneStore>()!;
                _audio = ServiceHelper.GetService<IAudioCaptureService>()!;
                _player = ServiceHelper.GetService<IAudioPlaybackService>()!;
                var countInClicks = ServiceHelper.GetService<ICountInClickService>()!;
                _waitingCountInPlayer = new WaitingCountInPlayer(countInClicks);
                AppCueAudioGate.SuspendRequested -= OnAppCueAudioSuspended;
                AppCueAudioGate.SuspendRequested += OnAppCueAudioSuspended;
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
                // Keep landscape locked; ForceLandscape runs again in OnAppearing.
                // Do not AllowAutorotate here — that briefly unlocks portrait during Shell navigation.

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
                _staffDrawable.MusicBpmMarkingBoundsChanged += (_, _) =>
                    MainThread.BeginInvokeOnMainThread(SyncTempoMarkingHitTarget);
                _staffDrawable.TimeSignatureBoundsChanged += (_, _) =>
                    MainThread.BeginInvokeOnMainThread(SyncTimeSignatureHitTarget);
                StaffGraphicsView.Drawable = _staffDrawable;
                StaffGraphicsView.SetBinding(GraphicsView.BackgroundColorProperty, new Binding("PanelBackgroundColor"));
                StaffGraphicsView.SizeChanged += (_, _) =>
                {
                    if (_allowStaffLayoutSettle
                        && _isPageVisible
                        && !PlayModePickerOptions.IsTunerMode(_session))
                        ScheduleStaffLayoutSettleRefresh();
                    ScheduleSyncTempoMarkingHitTarget();
                    ScheduleSyncTimeSignatureHitTarget();
                };
                StaffOverlayGrid.SizeChanged += (_, _) =>
                {
                    UpdateTitlePlayButtonPosition();
                    ScheduleSyncTempoMarkingHitTarget();
                    ScheduleSyncTimeSignatureHitTarget();
                };
                StaffBorder.SizeChanged += (_, _) =>
                {
                    UpdateTitlePlayButtonPosition();
                    ScheduleSyncTempoMarkingHitTarget();
                    ScheduleSyncTimeSignatureHitTarget();
                };
                SizeChanged += (_, _) => UpdateTitlePlayButtonPosition();
                SetPlayButtonPlaying(false);

                // Tuner graphics setup
                TunerBorder.BindingContext = _theme_service;
                // Info panel binds Heard/Nearest Hz to the session (not the theme).
                if (TunerInfoBorder != null)
                {
                    TunerInfoBorder.BindingContext = _session;
                    TunerInfoBorder.SetBinding(Border.BackgroundColorProperty,
                        new Binding("PanelBackgroundColor", source: _theme_service));
                }
                TunerGraphicsView.BindingContext = _theme_service;
                TunerGraphicsView.Drawable = _staffDrawable;
                TunerGraphicsView.SetBinding(GraphicsView.BackgroundColorProperty, new Binding("PanelBackgroundColor"));
                MainPageRootGrid.SizeChanged += (_, _) =>
                {
                    if (_session?.Tune == "Tuner")
                        ApplyTunerHeight();
                    else
                        ApplyStaffHeight();
                };
                if (PickersContainer != null)
                    PickersContainer.SizeChanged += (_, _) => ApplyStaffHeight();
                if (SessionResultBanner != null)
                    SessionResultBanner.SizeChanged += (_, _) => ApplyStaffHeight();

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
                        // Persist session summary + this session's attempts first; optional
                        // cleanup then removes older sessions only (never the one just saved).
                        var saveOutcome = await SaveSessionStatAsync();
                        int? newChildLevel = saveOutcome.NewChildLevel;
                        string? levelUpActivityWarning = saveOutcome.ActivityWarning;
                        await SaveNoteAttemptsForSessionAsync(levelBeforeSave);
                        await NoteAttemptSessionCleanup.RetainOnlyCompletedSessionIfEnabledAsync(
                            _noteAttemptDb,
                            _currentSessionId,
                            _session.ClearNoteAttemptsAfterSession);

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
                                if (SessionResultLabel != null)
                                {
                                    SessionResultLabel.Text = PracticeSessionLifecycle.FormatSessionResultBanner(
                                        summary, newChildLevel, levelUpActivityWarning);
                                }
                                if (SessionResultBanner != null)
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
                        IsAutoRepeatVisible = !PlayModePickerOptions.IsTunerMode(_session);
                        UpdateRepeatButtonsVisibility();
                        UpdateKeyPickerVisibility();
                        OnPropertyChanged(nameof(Tune));

                        // Hamburger / WhatToPlay set Tune via ApplyOtherSelection. If Music is
                        // already visible, OnAppearing may not re-run — switch Tuner chrome here
                        // so both entry points use the same display activation path.
                        if (_isPageVisible)
                        {
                            if (PlayModePickerOptions.IsTunerMode(_session))
                                ApplyTunerDisplayState();
                            else
                                UpdateTunerVisibility();
                        }
                    }

                    if (e.PropertyName == nameof(NoteSessionService.ShowConductorCues)
                        || e.PropertyName == nameof(NoteSessionService.NoteNameDisplay)
                        || e.PropertyName == nameof(NoteSessionService.ShowSignaturesOnBothStaffs))
                    {
                        // Signatures-on-both changes first-note X; bust layout cache without regenerating notes.
                        if (e.PropertyName == nameof(NoteSessionService.ShowSignaturesOnBothStaffs))
                            _staffDrawable?.InvalidateLayoutCache();
                        MainThread.BeginInvokeOnMainThread(() => StaffGraphicsView?.Invalidate());
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

                // ApplyPersistedSelection already restored Tune / scale / random / practice tune.
                // Rhythm layout flag and concrete arpeggio still need page-local wiring.
                var savedTune = PlayModePickerOptions.NormalizeRhythmNoteTunePreference(
                    Preferences.Default.Get<string?>("SelectedTune", null));
                if (PlayModePickerOptions.IsRhythmNoteTuneSelection(savedTune))
                    LayoutTestTune.SetEnabled(true);
                else if (!string.IsNullOrEmpty(savedTune)
                         && ArpeggioCatalog.TryResolveQuality(savedTune, out var savedArpeggio))
                {
                    _session.ApplyArpeggioQuality(savedArpeggio);
                }

                var (displayCategory, displaySelection) = PlayModePickerOptions.ResolveDisplayedPicker(
                    _session, LayoutTestTune.IsEnabled);
                var initialScaleTuneSelection = displayCategory switch
                {
                    PlayModePickerCategory.Tunes => displaySelection,
                    PlayModePickerCategory.Arpeggios => displaySelection,
                    PlayModePickerCategory.Other when displaySelection == PlayModePickerOptions.Tuner
                        => PlayModePickerOptions.Tuner,
                    PlayModePickerCategory.Scales => displaySelection,
                    _ => _session.Tune == "Practice Tune"
                        ? (_session.CurrentTune?.Title ?? practiceTuneTitles.FirstOrDefault() ?? string.Empty)
                        : _session.Tune == "Arpeggio"
                            ? _session.SelectedArpeggioDisplay
                            : _session.SelectedScale
                };
                var scaleTuneIdx = Array.IndexOf(scaleTuneOptions, initialScaleTuneSelection);
                ScaleTunePicker.SelectedIndex = scaleTuneIdx >= 0 ? scaleTuneIdx : 0;
                _lastValidScaleTuneIndex = ScaleTunePicker.SelectedIndex;
                IsAutoRepeatVisible = _session.Tune != "Tuner";

                ScaleTunePicker.SelectedIndexChanged += OnScaleTunePickerChanged;

                KeyPicker.SelectedIndexChanged += OnKeyPickerChangedWithPrompt;

                InitializePracticePickers(instrumentOptions);
                UpdateInstrumentPickerSelection();

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
            // Count-in needs an active listening session. Start when AutoStart OR count-in is on.
            bool wantListen = _session.AutoStart || WaitingCountInSettings.Enabled;
            if (!wantListen || _session.Tune == "Tuner")
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
                if (_holdResultForChildSession || _isRunning || _userStoppedListening || !_isPageVisible)
                    return;

                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (StaffGraphicsView != null && StaffGraphicsView.Width <= 0
                       && sw.ElapsedMilliseconds < 1500)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!_isPageVisible)
                        return;
                    await Task.Delay(40, ct);
                }

                await Task.Delay(150, ct);
                ct.ThrowIfCancellationRequested();

                bool wantListen = _session.AutoStart || WaitingCountInSettings.Enabled;
                if (!wantListen || _session.Tune == "Tuner"
                    || _holdResultForChildSession || _isRunning
                    || _userStoppedListening || !_isPageVisible)
                    return;

                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (!_isPageVisible || _isRunning || _userStoppedListening)
                        return;
                    await StartListeningAndEvaluatingAsync(scaleKeyTrigger: "AutoStart");
                });
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

        /// <summary>
        /// After Music returns from being obscured: at most one fresh Count-In arm, or
        /// capture resume — never a duplicate of a stale background timer.
        /// Uses a dedicated CTS so OnNavigatedTo AutoStart cannot cancel this resume.
        /// </summary>
        private void ScheduleResumeListeningAfterAppear()
        {
            try { _resumeListeningCts?.Cancel(); } catch { }
            _resumeListeningCts = new CancellationTokenSource();
            var cts = _resumeListeningCts;
            _ = RunResumeListeningAfterAppearAsync(cts.Token);
        }

        private async Task RunResumeListeningAfterAppearAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(40, ct);
                ct.ThrowIfCancellationRequested();
                if (!_isPageVisible || !_isRunning || _userStoppedListening)
                    return;

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (!_isPageVisible || !_isRunning || _userStoppedListening)
                        return;
                    ResumeListeningAfterPageVisible();
                });
            }
            catch (OperationCanceledException)
            {
                // Superseded by hide / Stop / newer appear.
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[CountIn] resume-after-appear ERROR: {ex}");
            }
        }

        private void ResumeListeningAfterPageVisible()
        {
            if (!_isPageVisible || !_isRunning || _userStoppedListening)
                return;
            if (PlayModePickerOptions.IsTunerMode(_session))
                return;

            // Drop any stale Count-In generation before arming exactly one fresh sequence.
            StopWaitingCountIn();

            bool restartCountIn = MusicListeningVisibility.ShouldRestartCountInOnResume(
                WaitingCountInSettings.Enabled,
                _session?.NotesToDraw?.Count ?? 0,
                _session?.CurrentNoteIndex ?? 0,
                _session?.FirstPitchDetectedUtc != null);

            if (restartCountIn)
            {
                int countInGen = WaitingCountInArming.Arm(ref _waitingCountInGeneration);
                _waitingCountInActive = true;
                StatusService.Instance.ShowTemporaryMessage(
                    StatusService.CountInStatusMessage,
                    StatusService.CountInStatusDuration);
                DebugLog.WriteLine("[CountIn] resume after page visible — fresh Count-In");
                _ = StartWaitingCountInAsync(countInGen);
                return;
            }

            if (_audio != null && !_audio.IsCapturing)
            {
                ResetPitchCapture();
                if (!_audio.TryStartCapture(OnAudioBlock, out var err))
                    DebugLog.WriteLine($"[CountIn] resume capture failed: {err}");
            }
        }

        /// <summary>
        /// Tuner shares this page — OnDisappearing does not run. Stop Music Count-In as soon
        /// as the Tuner surface becomes active so no delayed beep can sound over Tuner.
        /// </summary>
        private void PauseMusicCountInForTunerDisplay(bool enteringTuner)
        {
            if (!MusicListeningVisibility.ShouldStopMusicCountInForTunerDisplay(
                    PlayModePickerOptions.IsTunerMode(_session)))
                return;

            bool countInAlive = _waitingCountInActive
                || _waitingCountInPlayer?.IsActive == true;

            // Re-entrant UpdateTunerVisibility while already in Tuner must not tear down
            // Tuner mic / pending EnsureTunerListening — only act when entering or a
            // Music Count-In loop is still alive.
            if (!enteringTuner && !countInAlive)
                return;

            if (MusicListeningVisibility.ShouldPauseMusicListeningForTuner(
                    enteringTuner,
                    _isRunning || countInAlive,
                    _waitingCountInActive,
                    _userStoppedListening))
            {
                _listeningPausedForPageHide = true;
            }

            if (enteringTuner)
                CancelPendingListeningStarts();

            StopWaitingCountIn();
            try { ServiceHelper.GetService<ICountInClickService>()?.Stop(); } catch { }

            if (enteringTuner)
            {
                try { _audio?.StopCapture(); } catch { }
                _session?.ClearCountInClickSelfSoundSuppress("tuner displayed");
                if (_isRunning)
                    SetButtonStates(false);
            }

            DebugLog.WriteLine(
                $"[CountIn] stopped for Tuner display (entering={enteringTuner} " +
                $"paused={_listeningPausedForPageHide})");
        }

        /// <summary>
        /// After leaving Tuner back to Music practice: at most one fresh Count-In when still
        /// soft-paused; otherwise normal AutoStart/Count-In schedule if notes are ready.
        /// </summary>
        private void ResumeMusicListeningAfterLeavingTuner()
        {
            if (!MusicListeningVisibility.ShouldResumeMusicListeningAfterLeavingTuner(
                    leavingTuner: true,
                    listeningPaused: _listeningPausedForPageHide,
                    userStoppedListening: _userStoppedListening))
            {
                return;
            }

            _listeningPausedForPageHide = false;
            if (PlayModePickerOptions.IsTunerMode(_session) || !_isPageVisible)
                return;

            bool canRestartCountIn = MusicListeningVisibility.ShouldRestartCountInOnResume(
                WaitingCountInSettings.Enabled,
                _session?.NotesToDraw?.Count ?? 0,
                _session?.CurrentNoteIndex ?? 0,
                _session?.FirstPitchDetectedUtc != null);

            if (canRestartCountIn)
            {
                SetButtonStates(true);
                ResumeListeningAfterPageVisible();
                return;
            }

            // Tuner clears exercise notes on entry — wait for regenerate / AutoStart rather
            // than inventing a new listening session here.
            if (!_userStoppedListening
                && (_session.AutoStart || WaitingCountInSettings.Enabled)
                && (_session?.NotesToDraw?.Count ?? 0) > 0)
            {
                ScheduleAutoStartOnAppear();
            }
        }
        private async Task RegenerateNotesAsync()
        {
            await EnsureMainThreadAsync();

            // Repeat Same keeps the saved exercise unless the user changes key/scale/tune.
            if (_session.RepeatSameTune
                && _repeatSameSnapshot?.Notes.Count > 0
                && !_suppressSessionRegenerate
                && !_session.IsDirty)  //  2026.07.09 1120  
            {
                DebugLog.WriteLine("[RepeatSame] Skipping RegenerateNotesAsync — restoring saved exercise");
                await RestoreRepeatSameSnapshotAsync(_repeatSameSnapshot.Notes);
                return;
            }

            // Wait for any in-flight regeneration — never skip after session Reset() cleared notes.
            await _regenerateSemaphore.WaitAsync();
            await EnsureMainThreadAsync();
            try
            {
                // Tuner: skip Assortment by Level / composition / note-generation entirely.
                // Do NOT call ApplyTunerDisplayState here — that re-enters listening setup
                // and is owned by OnAppearing / EnterTunerMode / UpdateTunerVisibility.
                if (PlayModePickerOptions.IsTunerMode(_session))
                {
                    using (PracticeSessionStartProfiler.Scope("RegenerateNotes.Banner"))
                        await HideSessionResultBannerAsync(refreshMarqueeForNewLevel: true);
                    _holdResultForChildSession = false;
                    DebugLog.WriteLine("[Tuner] RegenerateNotesAsync skipped (no exercise notes)");
                    return;
                }

                using (PracticeSessionStartProfiler.Scope("RegenerateNotes.Banner"))
                    await HideSessionResultBannerAsync(refreshMarqueeForNewLevel: true);
                _holdResultForChildSession = false;

                if (_session.IsRandomMode)
                    _session.EnsureRandomModeGenerationSettings();
                else if (_session.ChildLevel > 0)
                    DifficultyLevelMapper.ApplyLevelDerivedSettings(_session.ChildLevel, _session);

                _session.PrepareEffectiveScaleForGeneration(_generationSeed);

                // While showing post-autoplay results, do not overwrite the staff.
                if (_freezeStaff)
                    return;

                bool generatedTuneChecks = GeneratedTuneAcceptance.ChecksRequired(
                    _session, LayoutTestTune.IsEnabled);
                int generationAttempts = generatedTuneChecks
                    ? GeneratedTuneAcceptance.MaxGenerationAttempts
                    : 1;

                bool restoreSuppressRegenerate = _suppressSessionRegenerate;
                bool restoreSuppressView = _suppressStaffViewCommit;
                _suppressSessionRegenerate = true;
                _suppressStaffViewCommit = true;
                string? chosenSignature = null;
                bool enoughPitches = false;
                try
                {
                    for (int attempt = 0; attempt < generationAttempts; attempt++)
                    {
                        if (attempt > 0)
                        {
                            if (_session.IsRandomMode)
                                _session.EnsureRandomModeGenerationSettings();
                            else if (_session.ChildLevel > 0)
                                DifficultyLevelMapper.ApplyLevelDerivedSettings(_session.ChildLevel, _session);

                            if (!LayoutTestTune.IsEnabled)
                            {
                                PracticeCompositionSelector.ApplyNextExerciseIfNeeded(_session, _generationSeed);
                                PracticeCompositionSelector.PreferRandomWhenFixedPatternWouldRepeat(
                                    _session, _generationSeed);
                            }

                            _session.PrepareFreshScaleAndKeyForGeneration(
                                "GeneratedTuneRetry", repeatSame: false, _generationSeed);
                        }

                        _generationSeed = unchecked(_generationSeed + 1);

                        UpdateTunerVisibility();
                        StaffBorder.IsVisible = true;

                        using (PracticeSessionStartProfiler.Scope("RegenerateNotes.ViewWidthWait"))
                        {
                            var sw2 = System.Diagnostics.Stopwatch.StartNew();
                            while (StaffGraphicsView?.Width <= 0 && sw2.ElapsedMilliseconds < 500)
                            {
                                await Task.Delay(20);
                                await EnsureMainThreadAsync();
                            }
                        }

                        using (PracticeSessionStartProfiler.Scope("RegenerateNotes.StaffDisplay"))
                            await UpdateStaffDisplayAsync();
                        await EnsureMainThreadAsync();

                        generatedTuneChecks = GeneratedTuneAcceptance.ChecksRequired(
                            _session, LayoutTestTune.IsEnabled);
                        enoughPitches = GeneratedTuneAcceptance.HasEnoughDistinctSoundedPitches(
                            _staffDrawable?.UpperNotes,
                            _staffDrawable?.LowerNotes);
                        bool uniquenessAccepted;
                        if (generatedTuneChecks && enoughPitches)
                        {
                            chosenSignature = GeneratedTuneSignature.FromStaffNotes(
                                _staffDrawable?.UpperNotes,
                                _staffDrawable?.LowerNotes);
                            uniquenessAccepted = GeneratedTuneAcceptance.IsAcceptableSuccessor(
                                _displayedTunes, chosenSignature);
                        }
                        else if (generatedTuneChecks && !enoughPitches)
                        {
                            chosenSignature = null;
                            uniquenessAccepted = false;
                            DebugLog.WriteLine(
                                "[GeneratedTune] Rejected candidate with fewer than 2 distinct sounded pitches");
                        }
                        else
                        {
                            uniquenessAccepted = true;
                        }

                        if (!GeneratedTuneAcceptance.ShouldRetry(
                                generatedTuneChecks,
                                enoughPitches,
                                uniquenessAccepted,
                                attempt,
                                generationAttempts))
                            break;
                    }

                    if (generatedTuneChecks && enoughPitches)
                        _displayedTunes.CommitDisplayed(chosenSignature);
                }
                finally
                {
                    _suppressStaffViewCommit = restoreSuppressView;
                    _suppressSessionRegenerate = restoreSuppressRegenerate;
                    UpdateEffectiveScaleLabel();
                    UpdateNoteEmphasisBanner();
                    await PublishStaffViewAsync();
                }

                _session.IsDirty = false;  //  2026.07.09 1131  

#if DEBUG
                if (_session.IsRandomMode)
                {
                    var names = string.Join(", ", _session.NotesToDraw.Select(n => n.Name));
                    DebugLog.WriteLine($"[Random] {_session.EffectiveScaleDisplay} → {_session.NotesToDraw.Count} notes: {names}");
                }
#endif

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
            // Omit mastered written pitches for random / candidate-selection generation
            // (including Assortment by Level composition Random and temporary note emphasis).
            // Fixed tunes, scales, and arpeggios leave exclusions empty so required notes remain.
            bool candidateSelection =
                _session.IsRandomMode || _session.HasTemporaryNoteEmphasis;

            _excludedMidis = candidateSelection
                ? await _session.GetMasteredMidiNumbersAsync()
                : new HashSet<int>();

            DebugLog.WriteLine(
                $"[MasteryOmit] LoadExcludedMidis activity={DescribeMasteryOmitActivity()} " +
                $"omissionOn={_session.UseNoteMasteryForGeneration} candidateSelection={candidateSelection} " +
                $"excludedCount={_excludedMidis.Count} " +
                $"excluded=[{MasteredNoteOmission.FormatMidiSample(_excludedMidis)}]");
        }
        private string DescribeMasteryOmitActivity()
        {
            if (_session.HasTemporaryNoteEmphasis)
                return "EmphasizedNote";
            if (_session.ScaleSelectionMode == ScaleSelectionMode.ByLevel && _session.IsRandomMode)
                return "ByLevel-Random";
            if (_session.IsRandomMode)
                return "Random";
            if (_session.Tune == "Practice Tune")
                return "FixedTune";
            if (_session.Tune == "Arpeggio")
                return "Arpeggio";
            return _session.IsRandomMode ? "Random" : "Scale";
        }
        /// <summary>
        /// Builds a <see cref="MusicSequenceGenerator"/> configured with the current
        /// session parameters, append offsets, and mastery exclusions.
        /// </summary>
        private MusicSequenceGenerator BuildSequenceGenerator(int measureCount, int startPrevPitch = -1, int seedSalt = 0)
        {
            // Translate persisted string settings to model types.
            var timeSig = TimeSignature.FromDisplayString(_session.MeterTimeSignature);

            // Selected-scale practice (Major, etc. from What to Play) is a straight
            // quarter-note scale walk — no rests, halves, or mixed rhythm.
            // Temporary Note Mastery emphasis uses random melodic generation without
            // permanently changing the user's What to Play mode.
            bool emphasizeActive = _session.HasTemporaryNoteEmphasis;
            bool simpleSelectedScale =
                _session.Tune == "Selected Scale" && !_session.IsRandomMode && !emphasizeActive;
            bool randomStyle = _session.IsRandomMode || emphasizeActive;

            int rhythmVariety = simpleSelectedScale
                ? 0
                : RhythmSettingsResolver.ResolveVarietyPercent(_session);
            var smallestDuration = simpleSelectedScale
                ? NoteDuration.Quarter
                : RhythmSettingsResolver.ParseSmallestDuration(_session.SmallestRhythmNote);

            var gen = new MusicSequenceGenerator
            {
                Key = _session.Key,
                Scale = _session.GenerationScale,
                LowestNote = _session.LowestNote,
                HighestNote = _session.HighestNote,
                TimeSignature = timeSig,
                MeasureCount = measureCount,
                RhythmVarietyPercent = rhythmVariety,
                SmallestDuration = smallestDuration,
                StartMeasureIndex = _seqNextMeasureIndex,
                StartBeatOffset = _seqNextBeatOffset,
                StartGlobalNoteIndex = _seqNextGlobalNoteIndex,
                StartPrevPitch = startPrevPitch,
                ExcludedMidiNumbers = _excludedMidis,
                UseScaleOrder = !randomStyle,
                ScaleWalkOffset = _seqNextGlobalNoteIndex,
                AccidentalPercent = randomStyle ? _session.AccidentalPercent : 0,
                MaxMelodicIntervalSemitones = randomStyle ? _session.MaxMelodicIntervalSemitones : 0,
                SyncopationLevel = simpleSelectedScale
                    ? SyncopationLevel.None
                    : SyncopationLevelHelper.Parse(_session.SyncopationSetting),
                RestChancePercent = simpleSelectedScale ? 0 : _session.PracticeRestChancePercent,
                EmphasizedMidiNumber = _session.GetTemporaryEmphasizedMidi(),
                EmphasizedNoteSelectionPercent = _session.TemporaryEmphasizedSelectionPercent,
                ActivityType = DescribeMasteryOmitActivity(),
                ChildLevel = _session.ChildLevel,
                MinDistinctPitches = MelodicVarietyRules.GetMinimumDistinctPitchesForLevel(
                    _session.ChildLevel),
                RandomSeed = GeneratedTuneAcceptance.MixRandomSeed(
                    _generationSeed, _session.ChildLevel, seedSalt)
            };
            DebugLog.WriteLine(
                $"[StaffGen] Tune={_session.Tune} Random={_session.IsRandomMode} " +
                $"SimpleScale={simpleSelectedScale} AccPct={_session.AccidentalPercent} " +
                $"EffectiveAccPct={(_session.IsRandomMode ? _session.AccidentalPercent : 0)} " +
                $"SmallestCfg={_session.SmallestRhythmNote} SmallestEff={smallestDuration} " +
                $"VarietyCfg={_session.RhythmVarietyPercent}/{_session.RhythmMode} VarietyEff={rhythmVariety} " +
                $"Activity={gen.ActivityType} Excluded={_excludedMidis.Count}");
            return gen;
        }
        private void ReportMasteryOmissionFallback(MusicSequenceGenerator gen)
        {
            if (gen.LastMasteryFallback == MasteredNoteOmission.FallbackKind.None)
                return;

            DebugLog.WriteLine(
                $"[MasteryOmit] fallback ({gen.ActivityType}/{gen.LastMasteryFallback}): " +
                $"{gen.LastMasteryFallbackReason}");

            // Only surface the all-mastered case — small unmastered pools are expected.
            if (gen.LastMasteryFallback
                    == MasteredNoteOmission.FallbackKind.AllowedMasteredAllEligibleMastered
                && !string.IsNullOrWhiteSpace(gen.LastMasteryFallbackReason))
            {
                StatusService.Instance.StatusMessage = gen.LastMasteryFallbackReason;
            }
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
            ReportMasteryOmissionFallback(genUpper);
            double measureBeats = genUpper.TimeSignature.TotalBeats;
            var upperFlat = BarLineTieNormalizer.Normalize(
                MusicSequenceGenerator.Flatten(upperMeasures), measureBeats);
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
                var genLower = BuildSequenceGenerator(lowerMc, lowerStartPitch, seedSalt: 0x5A5A5A5A);
                var lowerMeasures = genLower.GenerateSequence();
                ReportMasteryOmissionFallback(genLower);
                lowerFlat = BarLineTieNormalizer.Normalize(
                    MusicSequenceGenerator.Flatten(lowerMeasures),
                    genLower.TimeSignature.TotalBeats);

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
                            IsPlayedCorrectly = n.IsPlayedCorrectly,
                            TieGroupId = n.TieGroupId,
                            IsTieContinuation = n.IsTieContinuation,
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
            // Saved / practice tunes always use their authored key (or C when missing).
            // Ignore optional key/scale overrides so the Music-page key cannot rewrite the tune.
            _ = key;
            _ = scale;
            var (noteKey, noteScale) = NoteSessionService.ResolvePracticeTuneNotation(tune);

            var result = new List<GeneratedNote>();
            double measureBeats = tune.TimeSignature.TotalBeats;
            int measureIndex = 0;

            foreach (var measure in tune.Measures)
            {
                // Snap each notated bar to the meter grid so under/over-full legacy
                // measures cannot shift subsequent bar lines.
                double beatCursor = measureIndex * measureBeats;
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

                        // Apply key signature from the natural letter MIDI so already-flatted
                        // pool/MIDI values are not shifted a second time.
                        int naturalMidi = NoteSessionService.NoteNameToMidi($"{letter}{octave}");
                        var adjustedMidi = NoteSessionService.ApplyKeySignatureToMidi(
                            mn.SpelledName, naturalMidi, noteKey, noteScale);
                        // Prefer authored MIDI when the name already encodes the accidental.
                        if (NoteSessionService.HasExplicitAccidentalInSpelledName(mn.SpelledName))
                            adjustedMidi = mn.MidiNumber;
                        var (resolvedAcc, displayName) = NoteSessionService.ResolveAccidentalAndSpelling(
                            mn.SpelledName, adjustedMidi, letter, octave, noteKey, noteScale);
                        acc = resolvedAcc;
                        letter = char.ToUpperInvariant(displayName[0]);
                        octave = NoteSessionService.ParseOctaveFromSpelledName(displayName);

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

            // Re-pack onto the meter grid so a note that crosses / overflows a bar becomes
            // tied segments (durations sum to the original) instead of an extra onset on
            // the next bar line.
            return BarLineTieNormalizer.Normalize(result, measureBeats);
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

            if (regular.Count > 0)
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
                    TieGroupId = n.TieGroupId,
                    IsTieContinuation = n.IsTieContinuation,
                });
            }
            return shifted;
        }
        // ── Two-staff display ──────────────────────────────────────────────────
        /// <summary>How many measures to put on each staff (non-child / high levels).</summary>
        private const int MeasuresPerStaff = 4;
        /// <summary>Bumped on each regeneration so child random tunes differ every time.</summary>
        private int _generationSeed = Random.Shared.Next();
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
            await EnsureMainThreadAsync();
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
                    double measureBeats = testTune.TimeSignature.TotalBeats;
                    if (measureBeats <= 0)
                        measureBeats = 4;
                    var pageBars = ComputeStaffBarBeats(allNotes, measureBeats, new HashSet<double>());
                    var (canvasWidth, canvasHeight, isProvisional) = ResolveStaffCanvasSize();

                    StoreStaffPagePack(
                        allNotes, pageBars, measureBeats,
                        isTwoOctaveScaleCut: false,
                        canvasWidth, canvasHeight, isProvisional,
                        splitMode: StaffDrawable.StaffMeasureSplitMode.FillUpperFirst,
                        keepAllNotesVisible: true);

                    var split = PracticeTuneStaffSplit.PartitionForDisplay(
                        _staffDrawable!,
                        allNotes,
                        pageBars,
                        testTune.Measures.Count,
                        canvasWidth,
                        canvasHeight);
                    upperFlat = split.UpperNotes;
                    lowerFlat = split.LowerNotes;

                    double lowerBeatShift = lowerFlat.Count > 0 ? (lowerFlat[0].BeatPosition ?? 0.0) : 0.0;
                    lowerFlat = ShiftStaffBeatPositions(lowerFlat, lowerBeatShift);

                    upperBarBeats = ComputeStaffBarBeats(upperFlat, measureBeats, existingUpper);
                    lowerBarBeats = ComputeStaffBarBeats(lowerFlat, measureBeats, existingLower);

                    _seqNextMeasureIndex = split.UpperMeasureCount + split.LowerMeasureCount;
                    _seqNextBeatOffset = upperFlat.Sum(n => n.BeatDuration) + lowerFlat.Sum(n => n.BeatDuration);
                    _seqNextGlobalNoteIndex = upperFlat.Count(n => !n.IsRest) + lowerFlat.Count(n => !n.IsRest);
                    _lowerMeasureIndex = _seqNextMeasureIndex;
                    _lowerBeatOffset = _seqNextBeatOffset;
                    _lowerGlobalNoteIndex = _seqNextGlobalNoteIndex;
                    if (_staffDrawable != null) _staffDrawable.UpperHasEndBar = false;

                    StatusService.Instance.StatusMessage =
                        "Layout test tune (see debug log for bar beats)";
                }
                else if (_session.Tune == "Practice Tune" && _session.CurrentTune != null)
                {
                    // Width-aware wrap (fill upper first). Short tunes stay upper-only.
                    // KeepAllNotesVisible so a saved tune is never truncated off the page.
                    var allNotes = BuildNotesFromTune(_session.CurrentTune);
                    var allMeasures = _session.CurrentTune.Measures.Count;
                    double measureBeats = _session.CurrentTune.TimeSignature.TotalBeats;
                    if (measureBeats <= 0)
                        measureBeats = 4;
                    var pageBars = ComputeStaffBarBeats(allNotes, measureBeats, new HashSet<double>());
                    var (canvasWidth, canvasHeight, isProvisional) = ResolveStaffCanvasSize();

                    StoreStaffPagePack(
                        allNotes, pageBars, measureBeats,
                        isTwoOctaveScaleCut: false,
                        canvasWidth, canvasHeight, isProvisional,
                        splitMode: StaffDrawable.StaffMeasureSplitMode.FillUpperFirst,
                        keepAllNotesVisible: true);

                    var split = PracticeTuneStaffSplit.PartitionForDisplay(
                        _staffDrawable!,
                        allNotes,
                        pageBars,
                        allMeasures,
                        canvasWidth,
                        canvasHeight);
                    upperFlat = split.UpperNotes;
                    lowerFlat = split.LowerNotes;

                    double lowerBeatShift = lowerFlat.Count > 0 ? (lowerFlat[0].BeatPosition ?? 0.0) : 0.0;
                    lowerFlat = ShiftStaffBeatPositions(lowerFlat, lowerBeatShift);

                    upperBarBeats = ComputeStaffBarBeats(upperFlat, measureBeats, existingUpper);
                    lowerBarBeats = ComputeStaffBarBeats(lowerFlat, measureBeats, existingLower);

                    _seqNextMeasureIndex = split.UpperMeasureCount + split.LowerMeasureCount;
                    _seqNextBeatOffset = upperFlat.Sum(n => n.BeatDuration) + lowerFlat.Sum(n => n.BeatDuration);
                    _seqNextGlobalNoteIndex = upperFlat.Count(n => !n.IsRest) + lowerFlat.Count(n => !n.IsRest);

                    _lowerMeasureIndex = _seqNextMeasureIndex;
                    _lowerBeatOffset = _seqNextBeatOffset;
                    _lowerGlobalNoteIndex = _seqNextGlobalNoteIndex;
                    if (_staffDrawable != null) _staffDrawable.UpperHasEndBar = false;

#if DEBUG
                    DebugLog.WriteLine(
                        $"[Staff PracticeTune] packed upper={split.UpperMeasureCount} " +
                        $"lower={split.LowerMeasureCount} unplaced={split.UnplacedMeasureCount} " +
                        $"provisional={isProvisional} width={canvasWidth:F0} " +
                        $"Upper: {upperFlat.Count} notes, Lower: {lowerFlat.Count} notes");
#endif
                }
                else if (_session.Tune == "Arpeggio")
                {
                    var pattern = ArpeggioCatalog.All.FirstOrDefault(p => p.Id == _session.SelectedArpeggioId)
                        ?? ArpeggioCatalog.MajorTriad;
                    var allNotes = await _session.LoadArpeggioAsync(pattern, _session.SelectedArpeggioRoot);
                    await EnsureMainThreadAsync();

                    var (canvasWidth, canvasHeight, isProvisional) = ResolveStaffCanvasSize();
                    double measureBeats = TimeSignature.FromDisplayString(_session.GetDisplayTimeSignature()).TotalBeats;
                    if (measureBeats <= 0)
                        measureBeats = 4;
                    var pageBars = ComputeStaffBarBeats(allNotes, measureBeats, new HashSet<double>());

                    StoreStaffPagePack(
                        allNotes, pageBars, measureBeats,
                        isTwoOctaveScaleCut: false,
                        canvasWidth, canvasHeight, isProvisional);

                    var split = _staffDrawable!.SplitMeasuresAcrossStaves(
                        allNotes, pageBars, canvasWidth, canvasHeight);
                    upperFlat = split.UpperNotes;
                    lowerFlat = split.LowerNotes;

                    double lowerBeatShift = lowerFlat.Count > 0 ? (lowerFlat[0].BeatPosition ?? 0.0) : 0.0;
                    lowerFlat = ShiftStaffBeatPositions(lowerFlat, lowerBeatShift);

                    upperBarBeats = ComputeStaffBarBeats(upperFlat, measureBeats, existingUpper);
                    lowerBarBeats = ComputeStaffBarBeats(lowerFlat, measureBeats, existingLower);

                    int placedMeasures = split.UpperMeasureCount + split.LowerMeasureCount;
                    double upperBeats = upperFlat.Sum(n => n.BeatDuration);
                    double lowerBeats = lowerFlat.Sum(n => n.BeatDuration);
                    int upperPitches = upperFlat.Count(n => !n.IsRest);
                    int lowerPitches = lowerFlat.Count(n => !n.IsRest);

                    _seqNextMeasureIndex = placedMeasures;
                    _seqNextBeatOffset = upperBeats + lowerBeats;
                    _seqNextGlobalNoteIndex = upperPitches + lowerPitches;
                    _lowerMeasureIndex = _seqNextMeasureIndex;
                    _lowerBeatOffset = _seqNextBeatOffset;
                    _lowerGlobalNoteIndex = _seqNextGlobalNoteIndex;

#if DEBUG
                    DebugLog.WriteLine(
                        $"[Staff Arpeggio] {pattern.DisplayName} packed upper={split.UpperMeasureCount} " +
                        $"lower={split.LowerMeasureCount} unplaced={split.UnplacedMeasureCount} " +
                        $"(events unplaced={split.UnplacedNotes.Count}) provisional={isProvisional} width={canvasWidth:F0} " +
                        $"Upper: {upperFlat.Count} notes ({upperPitches} pitched), " +
                        $"Lower: {lowerFlat.Count} notes ({lowerPitches} pitched)");
#endif

                    if (_staffDrawable != null) _staffDrawable.UpperHasEndBar = false;
                }
                else
                {
                    using (PracticeSessionStartProfiler.Scope("StaffDisplay.LoadExcluded"))
                        await LoadExcludedMidisAsync();
                    await EnsureMainThreadAsync();

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
                        // Sequence generation is CPU-heavy; keep it off the UI thread.
                        // Staff split/layout stays on this path so drawable state is not
                        // mutated concurrently with Draw.
                        var (canvasWidth, canvasHeight, isProvisional) = ResolveStaffCanvasSize();

                        var generated = await Task.Run(() =>
                        {
                            var genAll = BuildSequenceGenerator(24);
                            var allMeasures = genAll.GenerateSequence();
                            double beats = genAll.TimeSignature.TotalBeats;
                            var flat = BarLineTieNormalizer.Normalize(
                                MusicSequenceGenerator.Flatten(allMeasures), beats);
                            var bars = ComputeStaffBarBeats(flat, beats, new HashSet<double>());
                            return (genAll, flat, beats, bars, MeasureCount: allMeasures.Count);
                        });
                        await EnsureMainThreadAsync();

                        ReportMasteryOmissionFallback(generated.genAll);
                        var allNotes = generated.flat;
                        double measureBeats = generated.beats;
                        var allBarBeats = generated.bars;

                        StoreStaffPagePack(
                            allNotes, allBarBeats, measureBeats,
                            isTwoOctaveScaleCut: false,
                            canvasWidth, canvasHeight, isProvisional);

                        var split = _staffDrawable!.SplitMeasuresAcrossStaves(
                            allNotes, allBarBeats, canvasWidth, canvasHeight);
                        upperFlat = split.UpperNotes;
                        lowerFlat = split.LowerNotes;

                        double lowerBeatShift = lowerFlat.Count > 0 ? (lowerFlat[0].BeatPosition ?? 0.0) : 0.0;
                        lowerFlat = ShiftStaffBeatPositions(lowerFlat, lowerBeatShift);

                        upperBarBeats = ComputeStaffBarBeats(upperFlat, measureBeats, existingUpper);
                        lowerBarBeats = ComputeStaffBarBeats(lowerFlat, measureBeats, existingLower);

                        int placedMeasures = split.UpperMeasureCount + split.LowerMeasureCount;
                        double upperBeats = upperFlat.Sum(n => n.BeatDuration);
                        double lowerBeats = lowerFlat.Sum(n => n.BeatDuration);
                        int upperPitches = upperFlat.Count(n => !n.IsRest);
                        int lowerPitches = lowerFlat.Count(n => !n.IsRest);

                        _seqNextMeasureIndex = placedMeasures;
                        _seqNextBeatOffset = upperBeats + lowerBeats;
                        _seqNextGlobalNoteIndex = upperPitches + lowerPitches;
                        _lowerMeasureIndex = _seqNextMeasureIndex;
                        _lowerBeatOffset = _seqNextBeatOffset;
                        _lowerGlobalNoteIndex = _seqNextGlobalNoteIndex;

#if DEBUG
                        DebugLog.WriteLine(
                            $"[Staff TwoOctave] { _session.SelectedScale} packed upper={split.UpperMeasureCount} " +
                            $"lower={split.LowerMeasureCount} unplaced={split.UnplacedMeasureCount} " +
                            $"(events unplaced={split.UnplacedNotes.Count}) provisional={isProvisional} width={canvasWidth:F0} " +
                            $"Upper: {upperFlat.Count} notes ({upperPitches} pitched), " +
                            $"Lower: {lowerFlat.Count} notes ({lowerPitches} pitched)");
#endif
                        }
                    }
                    else
                    {
                        var (upperMc, lowerMc) = GetStaffMeasureCounts();
                        var (canvasWidth, canvasHeight, isProvisional) = ResolveStaffCanvasSize();
                        int desiredMeasures = Math.Max(1, upperMc + lowerMc);

                        // Generate a continuous page, then pack whole measures by engraved
                        // width onto upper/lower — never force a fixed 3+3/4+4 onto a narrow
                        // staff by crushing ink.
                        MusicSequenceGenerator pageGen;
                        List<GeneratedNote> pageFlat;
                        double measureBeats;
                        List<double> pageBars;
                        using (PracticeSessionStartProfiler.Scope("StaffDisplay.SequenceGen"))
                        {
                            (pageGen, pageFlat, measureBeats, pageBars) = await Task.Run(() =>
                            {
                                var gen = BuildSequenceGenerator(desiredMeasures);
                                var measures = gen.GenerateSequence();
                                double beats = gen.TimeSignature.TotalBeats;
                                var flat = BarLineTieNormalizer.Normalize(
                                    MusicSequenceGenerator.Flatten(measures), beats);
                                var bars = ComputeStaffBarBeats(flat, beats, new HashSet<double>());
                                return (gen, flat, beats, bars);
                            });
                        }
                        await EnsureMainThreadAsync();

                        ReportMasteryOmissionFallback(pageGen);

                        StoreStaffPagePack(
                            pageFlat, pageBars, measureBeats,
                            isTwoOctaveScaleCut: false,
                            canvasWidth, canvasHeight, isProvisional);

                        var split = _staffDrawable!.SplitMeasuresAcrossStaves(
                            pageFlat, pageBars, canvasWidth, canvasHeight);
                        upperFlat = split.UpperNotes;
                        lowerFlat = split.LowerNotes;

                        double lowerBeatShift = lowerFlat.Count > 0 ? (lowerFlat[0].BeatPosition ?? 0.0) : 0.0;
                        lowerFlat = ShiftStaffBeatPositions(lowerFlat, lowerBeatShift);

                        upperBarBeats = ComputeStaffBarBeats(upperFlat, measureBeats, existingUpper);
                        lowerBarBeats = ComputeStaffBarBeats(lowerFlat, measureBeats, existingLower);

                        int placedMeasures = split.UpperMeasureCount + split.LowerMeasureCount;
                        double upperBeats = upperFlat.Sum(n => n.BeatDuration);
                        double lowerBeats = lowerFlat.Sum(n => n.BeatDuration);
                        int upperPitches = upperFlat.Count(n => !n.IsRest);
                        int lowerPitches = lowerFlat.Count(n => !n.IsRest);

                        _seqNextMeasureIndex = placedMeasures;
                        _seqNextBeatOffset = upperBeats + lowerBeats;
                        _seqNextGlobalNoteIndex = upperPitches + lowerPitches;
                        _lowerMeasureIndex = _seqNextMeasureIndex;
                        _lowerBeatOffset = _seqNextBeatOffset;
                        _lowerGlobalNoteIndex = _seqNextGlobalNoteIndex;

#if DEBUG
                        DebugLog.WriteLine(
                            $"[Staff Standard] L{_session.ChildLevel} desired={desiredMeasures} " +
                            $"packed upper={split.UpperMeasureCount} lower={split.LowerMeasureCount} " +
                            $"unplaced={split.UnplacedMeasureCount} (events retained={split.UnplacedNotes.Count}) " +
                            $"provisional={isProvisional} width={canvasWidth:F0} " +
                            $"Upper: {upperFlat.Count} notes ({upperPitches} pitched), " +
                            $"Lower: {lowerFlat.Count} notes ({lowerPitches} pitched)");
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
#if DEBUG
                ChromaticMidi61Diagnostics.LogRendererInput("upper", upperFlat);
                ChromaticMidi61Diagnostics.LogRendererInput("lower", lowerFlat);
#endif
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
                // Tie continuations are engraved on the staff but are not separate
                // playback/detection targets — DurationBeats covers the whole tied group.
                var rhythmOrder = upperFlat.Concat(lowerFlat).ToList();
                var tieGroupTotals = rhythmOrder
                    .Where(n => n.TieGroupId.HasValue && !n.IsRest)
                    .GroupBy(n => n.TieGroupId!.Value)
                    .ToDictionary(g => g.Key, g => g.Sum(n => n.BeatDuration));

                int sessionIdx = 0;
                _session.NotesToDraw.Clear();
                _session.FeedbackViewModels.Clear();
                var (noteKey, noteScale) = _session.GetNotationKeyAndScale();
                double? prevStartBeat = null;
                foreach (var gn in rhythmOrder)
                {
                    if (gn.IsRest) continue;
                    if (gn.IsTieContinuation) continue;
                    var (midi, name) = NoteSessionService.ResolveTargetPitch(gn, noteKey, noteScale);
                    double startBeat = gn.BeatPosition ?? 0.0;
                    double durationBeats = gn.TieGroupId is int tieId
                        && tieGroupTotals.TryGetValue(tieId, out double total)
                        ? total
                        : gn.BeatDuration;
                    double gateBeats = prevStartBeat.HasValue ? startBeat - prevStartBeat.Value : 0.0;
                    _session.NotesToDraw.Add(new NoteInfo
                    {
                        Midi = midi,
                        Name = name,
                        TargetFreq = 440.0 * Math.Pow(2.0, (midi - 69) / 12.0),
                        X = 0f,
                        Duration = gn.Duration,
                        StartBeat = startBeat,
                        DurationBeats = durationBeats,
                        GateBeatsAfterPrevious = gateBeats,
                    });
                    _session.FeedbackViewModels.Add(new FeedbackItem(sessionIdx++, 0, 0, false));
                    prevStartBeat = startBeat;
                }

                _session.ConfigureRhythmStartGates();

                _sessionUpperPitchCount = upperFlat.Count(n => !n.IsRest);

                if (!_suppressStaffViewCommit)
                    await PublishStaffViewAsync();

#if DEBUG
                RefreshMidi61DiagnosticLabel();
#endif
                }
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[Staff] UpdateStaffDisplayAsync ERROR: {ex}");
                StatusService.Instance.StatusMessage = $"[Staff Error] {ex.Message}";
            }
        }

        private async Task PublishStaffViewAsync()
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ApplyStaffHeight();
                // Width stamp happens only in StoreStaffPagePack / RepackStaffPageAtWidth
                // for width-packed pages. Other modes record the live view width here.
                if (_staffPagePack == null && StaffGraphicsView?.Width > 0)
                    _staffWidthUsedForLayout = StaffGraphicsView.Width;
                StaffGraphicsView?.Invalidate();
#if DEBUG
                _ = Task.Run(async () =>
                {
                    await Task.Delay(120);
                    await MainThread.InvokeOnMainThreadAsync(RefreshMidi61DiagnosticLabel);
                });
#endif
            });
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
            // Tuner owns its own single-note display colors via UpdateTunerStaffDisplay.
            if (_session.Tune == "Tuner")
                return;

            int upperPitchCount = _sessionUpperPitchCount;
            int currentSession = _session.CurrentNoteIndex;
            var diag = _session.BuildNoteStateDiagContext(currentSession);
            bool isUpperActive = currentSession < upperPitchCount;

            // ── Upper staff states ────────────────────────────────────────────────
            var upperStates = new StaffNoteState[_staffDrawable.UpperNotes.Count];
            var upperSessionIndices = new List<int>();
            var upperNames = new List<string>();
            int si = 0;
            for (int i = 0; i < _staffDrawable.UpperNotes.Count; i++)
            {
                if (_staffDrawable.UpperNotes[i].IsRest) { upperStates[i] = StaffNoteState.Pending; continue; }
                string noteName = si < _session.NotesToDraw.Count
                    ? _session.ResolveWrittenEvaluationName(_session.NotesToDraw[si])
                    : $"idx{si}";
                var oldState = i < (_staffDrawable.UpperNoteStates?.Length ?? 0)
                    ? _staffDrawable.UpperNoteStates![i]
                    : StaffNoteState.Pending;
                upperStates[i] = ResolveStaffNoteState(si, currentSession, isUpperActive, noteName, diag);
                if (oldState != upperStates[i])
                {
                    NoteStateChangeDiagnostics.GetCaller(out var method, out var file, out var line);
                    NoteStateChangeDiagnostics.LogStaffStateTransition(
                        method,
                        file,
                        line,
                        "Upper",
                        i,
                        si,
                        noteName,
                        oldState,
                        upperStates[i],
                        diag,
                        "SyncStaffNoteStates per-slot diff");
                }
                upperSessionIndices.Add(si);
                upperNames.Add(noteName);
                si++;
            }

            // ── Lower staff states ────────────────────────────────────────────────
            var lowerStates = new StaffNoteState[_staffDrawable.LowerNotes.Count];
            var lowerSessionIndices = new List<int>();
            var lowerNames = new List<string>();
            int li = 0;
            for (int i = 0; i < _staffDrawable.LowerNotes.Count; i++)
            {
                if (_staffDrawable.LowerNotes[i].IsRest) { lowerStates[i] = StaffNoteState.Pending; continue; }
                int globalIdx = upperPitchCount + li;
                string noteName = globalIdx < _session.NotesToDraw.Count
                    ? _session.ResolveWrittenEvaluationName(_session.NotesToDraw[globalIdx])
                    : $"idx{globalIdx}";
                var oldState = i < (_staffDrawable.LowerNoteStates?.Length ?? 0)
                    ? _staffDrawable.LowerNoteStates![i]
                    : StaffNoteState.Pending;
                lowerStates[i] = ResolveStaffNoteState(globalIdx, currentSession, !isUpperActive, noteName, diag);
                if (oldState != lowerStates[i])
                {
                    NoteStateChangeDiagnostics.GetCaller(out var method, out var file, out var line);
                    NoteStateChangeDiagnostics.LogStaffStateTransition(
                        method,
                        file,
                        line,
                        "Lower",
                        i,
                        globalIdx,
                        noteName,
                        oldState,
                        lowerStates[i],
                        diag,
                        "SyncStaffNoteStates per-slot diff");
                }
                lowerSessionIndices.Add(globalIdx);
                lowerNames.Add(noteName);
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

            NoteStateChangeDiagnostics.GetCaller(out var bulkMethod, out var bulkFile, out var bulkLine);
            NoteStateChangeDiagnostics.LogStaffBulkAssign(
                bulkMethod,
                bulkFile,
                bulkLine,
                "Upper",
                _staffDrawable.UpperNoteStates,
                upperStates,
                upperSessionIndices.ToArray(),
                upperNames.ToArray(),
                diag,
                "SyncStaffNoteStates commit upper array");
            NoteStateChangeDiagnostics.LogStaffBulkAssign(
                bulkMethod,
                bulkFile,
                bulkLine,
                "Lower",
                _staffDrawable.LowerNoteStates,
                lowerStates,
                lowerSessionIndices.ToArray(),
                lowerNames.ToArray(),
                diag,
                "SyncStaffNoteStates commit lower array");

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
        /// Colors a pitched note from session feedback — only notes with explicit wrong
        /// feedback render red, so conductor catch-up cannot paint unattempted notes red.
        /// </summary>
        private StaffNoteState ResolveStaffNoteState(
            int sessionIndex,
            int currentSession,
            bool isActiveStaff,
            string noteName,
            NoteStateChangeDiagnostics.NoteStateDiagContext diag)
            => StaffNoteStateResolver.Resolve(
                sessionIndex,
                currentSession,
                isActiveStaff,
                _session.CorrectNoteIndices,
                _session.NoteFeedbacks,
                diag,
                noteName,
                "SyncStaffNoteStates.Resolve");

        /// <summary>
        /// Generates new notes for the upper staff while the player is on the lower staff,
        /// then fades in the new upper staff content.
        /// </summary>
        /// <summary>
        /// Resizes the staff canvas.  Must be called on the main thread.
        /// </summary>
        private void ApplyStaffHeight()
        {
            if (_session != null && PlayModePickerOptions.IsTunerMode(_session))
                return;

            // Staff fills the content viewport minus the top picker row so both staves
            // stay on-screen. Level / Save / bottom pickers stay in the same ScrollView
            // below the staff and may sit below the fold.
            float availH = 300f;
            try
            {
                double viewport = 0;
                if (MainPageRootGrid?.Height > 1)
                    viewport = MainPageRootGrid.Height;
                else if (Height > 1)
                    viewport = Height;
                else
                {
                    var win = Application.Current?.Windows?.FirstOrDefault();
                    if (win != null)
                        viewport = win.Height - 50;
                }

                availH = (float)Math.Max(160, viewport - EstimateMusicTopChromeHeight());
            }
            catch { /* keep default */ }

            if (_staffDrawable != null)
                _staffDrawable.AvailableHeight = availH;
            var h = _staffDrawable?.ComputeRequiredHeight() ?? 0;
            StaffGraphicsView.HeightRequest = h;
            StaffBorder.HeightRequest = h;
            UpdateTitlePlayButtonPosition();
        }

        /// <summary>
        /// Height of Music-page chrome above the staff (instrument/key/scale row and
        /// optional banners) plus the layout spacing before the staff. Bottom Level /
        /// Save controls are not reserved — they may scroll below the fold.
        /// </summary>
        private double EstimateMusicTopChromeHeight()
        {
            double top = 0;
            if (PickersContainer?.IsVisible == true)
            {
                double ph = PickersContainer.Height;
                top += ph > 1 ? ph : 48;
            }

            if (SessionResultBanner?.IsVisible == true)
            {
                double bh = SessionResultBanner.Height;
                top += (bh > 1 ? bh : 32) + SessionResultBanner.Margin.Top
                    + SessionResultBanner.Margin.Bottom;
            }

            if (top > 0 && MainPageMainLayout != null)
                top += MainPageMainLayout.Spacing;

            return top;
        }
        /// <summary>
        /// Size the tuner panels short enough that both green borders and
        /// their contents stay fully visible without scrolling (portrait and landscape).
        /// Staff is ~40% width; Metronome controls ~60%.
        /// </summary>
        private void ApplyTunerHeight()
        {
            if (_session?.Tune != "Tuner" || TunerGraphicsView == null)
                return;

            try
            {
                double availH = 0;
                double availW = 0;
                if (MainPageRootGrid?.Height > 0)
                {
                    availH = MainPageRootGrid.Height;
                    availW = MainPageRootGrid.Width;
                }
                else if (Height > 0)
                {
                    availH = Height;
                    availW = Width;
                }
                else
                {
                    var win = Application.Current?.Windows?.FirstOrDefault();
                    if (win != null)
                    {
                        availH = Math.Max(180, win.Height - 72);
                        availW = win.Width;
                    }
                }

                availH = Math.Max(160, availH);

                if (SessionResultBanner?.IsVisible == true && SessionResultBanner.Height > 0)
                    availH -= SessionResultBanner.Height + 4;

                // Outer inset keeps stroke clear of cutouts / viewport edges.
                const double edge = 8;
                // Bottom reserve so the green border and instrument row stay on-screen.
                const double bottomReserve = 8;
                double usableH = Math.Max(160, availH - edge * 2 - bottomReserve);

                bool landscape = availW > availH && availW > 0;

            // Right-panel content: Note Heard + freqs + two pickers + metronome + tempo.
            const double minContentH = 176;
                double panelH = landscape
                    ? Math.Min(usableH, Math.Max(minContentH, usableH * 0.98))
                    : Math.Min(usableH, Math.Max(minContentH, Math.Min(280, usableH * 0.72)));

                MainPageMainLayout.Spacing = 0;
                if (MainScrollView != null)
                {
                    MainScrollView.VerticalScrollBarVisibility = ScrollBarVisibility.Never;
                    MainScrollView.HorizontalOptions = LayoutOptions.Fill;
                    if (_theme_service?.PanelBackgroundColor is Color panelBg)
                    {
                        MainScrollView.BackgroundColor = panelBg;
                        if (MainPageRootGrid != null)
                            MainPageRootGrid.BackgroundColor = panelBg;
                        BackgroundColor = panelBg;
                    }
                }

                StaffAreaStack.Margin = new Thickness(edge);
                StaffAreaStack.HeightRequest = panelH;
                StaffAreaStack.HorizontalOptions = LayoutOptions.Fill;
                StaffAreaStack.VerticalOptions = LayoutOptions.Start;

                // Equal-width columns fill the shared height; avoid oversized HeightRequests.
                TunerGrid.HeightRequest = panelH;
                TunerGrid.Margin = new Thickness(0);
                TunerGrid.HorizontalOptions = LayoutOptions.Fill;
                TunerGrid.VerticalOptions = LayoutOptions.Start;
                TunerBorder.HeightRequest = -1;
                TunerInfoBorder.HeightRequest = -1;
                TunerGraphicsView.HeightRequest = -1;
                TunerBorder.VerticalOptions = LayoutOptions.Fill;
                TunerInfoBorder.VerticalOptions = LayoutOptions.Fill;
                TunerGraphicsView.Invalidate();
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[ApplyTunerHeight] ERROR: {ex}");
            }
        }
        /// <summary>
        /// Compact Play/Stop pill height (mm). Short enough to sit in the above-staff
        /// breathing room without covering tempo / clef / key; still tappable when paired
        /// with a wider text-fitted width.
        /// </summary>
        private const double TitlePlayButtonHeightMm = 4.0;
        private const double TitlePlayButtonMinWidthMm = 7.0;
        private const double TitlePlayButtonPadH = 6;
        private const double TitlePlayButtonPadV = 2;
        private void UpdateTitlePlayButtonPosition()
        {
            if (TitlePlayButton == null || !TitlePlayButton.IsVisible)
                return;

            double height = MarginUtils.MmToDips(TitlePlayButtonHeightMm);
            double minWidth = MarginUtils.MmToDips(TitlePlayButtonMinWidthMm);
            string text = _titlePlayLabelText;

            // Fit font to the short height first, then widen for the label.
            const double inset = 2;
            double innerH = Math.Max(1, height - TitlePlayButtonPadV * 2 - inset);
            double fontSize = GetTitleFittedFontSize(text, minWidth * 2, innerH);
            double textWidth = MeasureTitleUiTextWidth(text, fontSize);
            double width = Math.Max(minWidth, textWidth + TitlePlayButtonPadH * 2 + inset);

            TitlePlayButton.WidthRequest = width;
            TitlePlayButton.HeightRequest = height;
            TitlePlayButton.MinimumWidthRequest = width;
            TitlePlayButton.MinimumHeightRequest = height;
            // Keep tucked into the graphics corner; layout Margin in XAML handles insets.
            TitlePlayButton.Margin = new Thickness(2, 2, 0, 0);
            UpdateTitlePlayButtonFontSize(fontSize, width, height);
        }

        private static double MeasureTitleUiTextWidth(string text, double fontSize)
        {
            if (string.IsNullOrEmpty(text) || fontSize <= 0)
                return 0;

            using var font = new SKFont();
            ConfigureTitleUiBoldFont(font, (float)fontSize);
            font.MeasureText(text, out var bounds);
            return bounds.Width + 2; // embolden / MAUI slack
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

            UpdateTitlePlayButtonPosition();
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
        private const int TitleBarChromeHeight = 52;
        private const int TitleStartStopButtonSize = TitleBarChromeHeight;
        /// <summary>Shared height for title Back and Stop (running) chrome.</summary>
        private const int TitleControlHeight = 40;
        /// <summary>Title-bar slot width — stop state expands to this so "Stop" fits.</summary>
        private const int TitleStartStopSlotWidth = 64;
        private const int TunerControlCornerRadius = 8;
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
        /// <summary>Largest bold "Stop" font size that fits inside the red title button.</summary>
        private static double GetTitleStopFontSize(double width, double height, string labelText)
        {
            if (width <= 0 || height <= 0)
                return 10;

            const double inset = 2;
            return GetTitleFittedFontSize(labelText, width - inset * 2, height - inset * 2);
        }
        private static double GetTitleStopFontSize(double width, double height)
            => GetTitleStopFontSize(width, height, TitleStopLabelText);
        private void UpdateTitlePlayButtonFontSize()
            => UpdateTitlePlayButtonPosition();

        private void UpdateTitlePlayButtonFontSize(double fontSize, double width, double height)
        {
            if (TitlePlayButton == null)
                return;

            if (fontSize <= 0 || width <= 0 || height <= 0)
                return;

            TitlePlayButton.Content = new Label
            {
                Text = _titlePlayLabelText,
                FontSize = fontSize,
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
        private void UpdateTitleStartStopButtonVisual(bool isRunning)
        {
            if (TitleStartStopButton == null)
                return;

            // Tuner metronome has its own Start/Stop; keep the title button as green Go while it runs.
            var titleAction = TunerTitleChrome.ResolveTitleAction(
                _session?.Tune, isRunning, _isReferenceTonePlaying);
            if (titleAction == TunerTitleChrome.TitleAction.Stop)
            {
                double width = TitleStartStopSlotWidth;
                double height = TitleControlHeight;
                string label = TitleStopLabelText;

                TitleStartStopButton.WidthRequest = width;
                TitleStartStopButton.HeightRequest = height;
                TitleStartStopButton.MinimumWidthRequest = width;
                TitleStartStopButton.MinimumHeightRequest = height;
                TitleStartStopButton.BackgroundColor = Colors.Red;
                TitleStartStopButton.Stroke = Colors.Transparent;
                TitleStartStopButton.StrokeThickness = 0;
                TitleStartStopButton.StrokeShape = new RoundRectangle { CornerRadius = TunerControlCornerRadius };
                TitleStartStopButton.Content = new Label
                {
                    Text = label,
                    FontSize = GetTitleStopFontSize(width, height, label),
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
                TitleStartStopButton.MinimumWidthRequest = diameter;
                TitleStartStopButton.MinimumHeightRequest = diameter;
                TitleStartStopButton.BackgroundColor = Color.FromArgb("#008000");
                TitleStartStopButton.Stroke = Colors.Transparent;
                TitleStartStopButton.StrokeThickness = 0;
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
                if (_session?.Tune == "Tuner")
                    SetPlayButtonPlaying(_isTunerPitchPlaying, isEnabled: true);
                else
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
            string? practiceTitle = _session.CurrentTune?.Title;
            if (string.IsNullOrWhiteSpace(practiceTitle) && _session.Tune == "Practice Tune")
            {
                var savedTitle = Preferences.Default.Get<string?>("SelectedTune", null);
                if (!string.IsNullOrWhiteSpace(savedTitle)
                    && musicmate.Models.TuneLibrary.All.Any(t => t.Title == savedTitle))
                    practiceTitle = savedTitle;
            }

            return PlayModePickerOptions.ResolveExerciseStatusLabel(
                LayoutTestTune.IsEnabled,
                _session.Tune ?? string.Empty,
                _session.ScaleSelectionMode,
                _session.IsRandomMode,
                practiceTitle,
                _session.SelectedArpeggioDisplay,
                _session.Key,
                _session.EffectiveScale,
                _session.SelectedScale,
                Preferences.Default.Get<string?>("SelectedTune", null));
        }
        private async Task ApplyChildLevelAndRefreshAsync(int level)
        {
            level = Math.Clamp(level, 1, 100);

            PracticeDifficultySettings difficulty;
            _suppressSessionRegenerate = true;
            try
            {
                // Persist before mutating session so PropertyChanged handlers that read
                // preferences cannot restore a stale level.
                Preferences.Default.Set(ChildLevelPrefKey, level);
                LevelUpService.MarkCountSinceNow();

                difficulty = DifficultyLevelMapper.ApplyLevelSettings(
                    level, _session, preserveUserPracticeSettings: _session.ChildPracticeSettingsCustomized);

                _session.PrepareEffectiveScaleForGeneration(_generationSeed);
                UpdateEffectiveScaleLabel();
                UpdateKeyPickerSelection();
                UpdateScaleTunePicker();
                UpdateConcertKeyLabel();
                UpdateKeyPickerVisibility();
                UpdateChildLevelSliderDisplay();
            }
            finally
            {
                _suppressSessionRegenerate = false;
            }

            _repeatSameSnapshot = null;

            // Manual level change: resume listening with Repeat New Each Time on.
            _session.EnableAutoStartWithRepeatNew();

            if (_session.IsRandomMode)
                SyncPlayItemStatusMessage();
            else
                StatusService.Instance.StatusMessage =
                    $"Level {level}: {difficulty.StageLabel} — {difficulty.SuggestedKey} {difficulty.SuggestedScale}";

            await RefreshDisplayForLevelChangeAsync();

            if (!_isPlaying)
            {
                await StartListeningAndEvaluatingAsync(
                    forceNewNotes: true,
                    scaleKeyTrigger: "AutoStart");
            }
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

            // Holding +/- queues many clicks; coalesce so the last level wins and
            // StartListening is not cancelled mid-microphone start (left on GO).
            _pendingChildLevel = level;
            _childLevelApplyCts?.Cancel();
            _childLevelApplyCts = new CancellationTokenSource();
            var ct = _childLevelApplyCts.Token;
            try
            {
                await Task.Delay(120, ct);
                if (_pendingChildLevel is int pending)
                {
                    _pendingChildLevel = null;
                    await ApplyChildLevelAndRefreshAsync(pending);
                }
            }
            catch (OperationCanceledException)
            {
                // Newer +/- click replaced this apply.
            }
        }

        private void OnStaffAreaTapped(object? sender, TappedEventArgs e)
        {
            var point = e.GetPosition(StaffGraphicsView);
            long now = Environment.TickCount64;
            if (now < _suppressStaffOverlayTapUntilMs)
            {
                LogTempoTap(
                    "STAFF BORDER TAP SUPPRESSED",
                    $"same-gesture-as-hit-target remainingMs={_suppressStaffOverlayTapUntilMs - now}");
                return;
            }

            LogTempoTap(
                "STAFF BORDER TAP",
                $"point={(point is Point p ? $"{p.X:F0},{p.Y:F0}" : "null")} " +
                $"borderInputTransparent={StaffBorder?.InputTransparent} " +
                $"gvInputTransparent={StaffGraphicsView?.InputTransparent}");
            TryHandleStaffOverlayDismissTap(point);
        }

        private void OnStaffGraphicsStartInteraction(object? sender, TouchEventArgs e)
        {
            // Expected to be rare/never while GraphicsView.InputTransparent=true.
            string detail = e.Touches.Length == 0
                ? "no-touches"
                : $"xy={e.Touches[0].X:F0},{e.Touches[0].Y:F0}";
            LogTempoTap("GRAPHICS VIEW START INTERACTION", detail);
        }

        private void OnTempoMarkingHitTargetClicked(object? sender, EventArgs e)
        {
            // Exact diagnostic line — before any strip open/close logic.
            LogTempoTap("HIT TARGET", BuildTempoHitTargetStateDetail());

            // StaffBorder TapGestureRecognizer often fires after this Clicked and would
            // immediately dismiss (or toggle) the strip — suppress that same-gesture tap.
            _suppressStaffOverlayTapUntilMs = Environment.TickCount64 + 400;
            SetTimeSignatureControlVisible(false);
            SetTempoControlVisible(!_isTempoControlVisible);
            LogTempoTap(
                "OPEN STRIP",
                $"IsTempoControlVisible={IsTempoControlVisible} tempo={_session?.Tempo}");
        }

        private void TryHandleStaffOverlayDismissTap(Point? point)
        {
            if (_session == null || PlayModePickerOptions.IsTunerMode(_session))
                return;

            if (_isTempoControlVisible)
            {
                if (point is Point p && IsPointOverTempoHitTarget(p))
                {
                    LogTempoTap("STAFF TAP OVER HIT TARGET IGNORED", $"xy={p.X:F0},{p.Y:F0}");
                    return;
                }

                LogTempoTap("STAFF TAP DISMISS STRIP", point is Point d ? $"xy={d.X:F0},{d.Y:F0}" : "no-point");
                SetTempoControlVisible(false);
            }

            if (_isTimeSignatureControlVisible)
            {
                if (point is Point p2 && IsPointOverTimeSignatureHitTarget(p2))
                    return;

                SetTimeSignatureControlVisible(false);
            }
        }

        private bool IsPointOverTempoHitTarget(Point pointInGraphicsView)
        {
            if (TempoMarkingHitTarget == null || !TempoMarkingHitTarget.IsVisible)
                return false;

            // Hit target uses Margin in StaffOverlayGrid; GraphicsView shares the same origin in practice mode.
            double x = TempoMarkingHitTarget.Margin.Left;
            double y = TempoMarkingHitTarget.Margin.Top;
            double w = TempoMarkingHitTarget.Width > 0 ? TempoMarkingHitTarget.Width : TempoMarkingHitTarget.WidthRequest;
            double h = TempoMarkingHitTarget.Height > 0 ? TempoMarkingHitTarget.Height : TempoMarkingHitTarget.HeightRequest;
            if (w > 0 && h > 0
                && pointInGraphicsView.X >= x
                && pointInGraphicsView.X <= x + w
                && pointInGraphicsView.Y >= y
                && pointInGraphicsView.Y <= y + h)
            {
                return true;
            }

            return _staffDrawable != null
                && _staffDrawable.HitTestMusicBpmMarking((float)pointInGraphicsView.X, (float)pointInGraphicsView.Y);
        }

        private bool IsPointOverTimeSignatureHitTarget(Point pointInGraphicsView)
        {
            if (TimeSignatureHitTarget == null || !TimeSignatureHitTarget.IsVisible)
                return false;

            double x = TimeSignatureHitTarget.Margin.Left;
            double y = TimeSignatureHitTarget.Margin.Top;
            double w = TimeSignatureHitTarget.Width > 0 ? TimeSignatureHitTarget.Width : TimeSignatureHitTarget.WidthRequest;
            double h = TimeSignatureHitTarget.Height > 0 ? TimeSignatureHitTarget.Height : TimeSignatureHitTarget.HeightRequest;
            if (w > 0 && h > 0
                && pointInGraphicsView.X >= x
                && pointInGraphicsView.X <= x + w
                && pointInGraphicsView.Y >= y
                && pointInGraphicsView.Y <= y + h)
            {
                return true;
            }

            return _staffDrawable != null
                && _staffDrawable.HitTestTimeSignature((float)pointInGraphicsView.X, (float)pointInGraphicsView.Y);
        }

        private void OnTempoDeltaClicked(object? sender, EventArgs e)
        {
            if (_session == null
                || sender is not Button { CommandParameter: string param }
                || !int.TryParse(param, out int delta))
                return;

            // Center button (current BPM): dismiss without changing tempo.
            if (delta == 0)
            {
                SetTempoControlVisible(false);
                return;
            }

            int next = TempoControlLogic.ApplyDelta(_session.Tempo, delta);
            if (next == _session.Tempo)
            {
                RefreshTempoControlDisplay();
                return;
            }

            // Single authoritative tempo — ApplyTempo persists prefs and rebuilds rhythm gates.
            _session.Tempo = next;
            RefreshTempoControlDisplay();
            SyncTempoControlRowPosition();
            _staffDrawable?.InvalidateLayoutCache();
            StaffGraphicsView?.Invalidate();
            ScheduleSyncTempoMarkingHitTarget();
        }

        private void OnTimeSignatureHitTargetClicked(object? sender, EventArgs e)
        {
            _suppressStaffOverlayTapUntilMs = Environment.TickCount64 + 400;
            SetTempoControlVisible(false);
            SetTimeSignatureControlVisible(!_isTimeSignatureControlVisible);
        }

        private async void OnTimeSignatureOptionClicked(object? sender, EventArgs e)
        {
            if (_session == null
                || sender is not Button { CommandParameter: string selected })
                return;

            string? next = TimeSignatureControlLogic.NormalizeSelection(selected);
            if (next == null)
                return;

            SetTimeSignatureControlVisible(false);

            if (!TimeSignatureControlLogic.WouldChange(_session.MeterTimeSignature, next))
            {
                RefreshTimeSignatureControlDisplay();
                return;
            }

            // Built-in Practice Tune meters are owned by the tune; still persist preference for
            // generated music, but do not force a practice-tune rewrite.
            _session.MeterTimeSignature = next;
            if (_session.ChildLevel > 0)
                _session.MarkChildPracticeSettingsCustomized();

            // Meter change invalidates any Repeat Same snapshot packed for the old signature.
            _repeatSameSnapshot = null;
            _session.IsDirty = true;

            RefreshTimeSignatureControlDisplay();
            _staffDrawable?.InvalidateLayoutCache();
            StaffGraphicsView?.Invalidate();
            ScheduleSyncTimeSignatureHitTarget();
            ScheduleSyncTempoMarkingHitTarget();

            if (!PlayModePickerOptions.IsTunerMode(_session) && _session.Tune != "Practice Tune")
                await RegenerateNotesAsync();
        }

        private void SetTimeSignatureControlVisible(bool visible)
        {
            if (_session != null
                && (PlayModePickerOptions.IsTunerMode(_session) || _session.Tune == "Practice Tune"))
                visible = false;

            if (_isTimeSignatureControlVisible == visible)
            {
                if (visible)
                {
                    EnsureTimeSignatureOptionButtons();
                    RefreshTimeSignatureControlDisplay();
                    SyncTimeSignatureControlRowPosition();
                }
                ApplyTimeSignatureControlRowVisibility(visible);
                return;
            }

            _isTimeSignatureControlVisible = visible;
            OnPropertyChanged(nameof(IsTimeSignatureControlVisible));
            if (visible)
            {
                EnsureTimeSignatureOptionButtons();
                RefreshTimeSignatureControlDisplay();
                SyncTimeSignatureControlRowPosition();
            }
            ApplyTimeSignatureControlRowVisibility(visible);
        }

        private void ApplyTimeSignatureControlRowVisibility(bool visible)
        {
            if (TimeSignatureControlRow == null)
                return;

            TimeSignatureControlRow.IsVisible = visible;
            TimeSignatureControlRow.Opacity = visible ? 1 : 0;
            TimeSignatureControlRow.InputTransparent = !visible;
            if (visible)
            {
                TimeSignatureControlRow.InvalidateMeasure();
                StaffOverlayGrid?.InvalidateMeasure();
            }
        }

        private void EnsureTimeSignatureOptionButtons()
        {
            if (_timeSignatureOptionButtonsBuilt || TimeSignatureOptionsGrid == null)
                return;

            _timeSignatureOptionButtonsBuilt = true;
            TimeSignatureOptionsGrid.Children.Clear();

            Color textColor = Colors.White;
            Color bgColor = Color.FromArgb("#8B4513");
            if (Application.Current?.Resources.TryGetValue("ThemeContrastingText", out var fg) == true
                && fg is Color themedFg)
                textColor = themedFg;
            if (Application.Current?.Resources.TryGetValue("ThemeButtonBackground", out var bg) == true
                && bg is Color themedBg)
                bgColor = themedBg;

            var options = TimeSignatureControlLogic.Options;
            for (int i = 0; i < options.Count; i++)
            {
                string opt = options[i];
                var btn = new Button
                {
                    Text = opt,
                    FontSize = 12,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = textColor,
                    BackgroundColor = bgColor,
                    CornerRadius = 10,
                    Padding = 0,
                    HeightRequest = 34,
                    MinimumHeightRequest = 34,
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = LayoutOptions.Center,
                    CommandParameter = opt,
                };
                btn.Clicked += OnTimeSignatureOptionClicked;
                SemanticProperties.SetDescription(btn, $"Set time signature to {opt}");
                Grid.SetRow(btn, i / 3);
                Grid.SetColumn(btn, i % 3);
                TimeSignatureOptionsGrid.Children.Add(btn);
            }
        }

        private void RefreshTimeSignatureControlDisplay()
        {
            if (TimeSignatureOptionsGrid == null || _session == null)
                return;

            string current = _session.GetDisplayTimeSignature();
            foreach (var child in TimeSignatureOptionsGrid.Children)
            {
                if (child is not Button btn || btn.CommandParameter is not string opt)
                    continue;

                bool selected = string.Equals(opt, current, StringComparison.Ordinal)
                    || string.Equals(opt, _session.MeterTimeSignature, StringComparison.Ordinal);
                if (selected)
                {
                    btn.BackgroundColor = Color.FromArgb("#F5D76E");
                    btn.TextColor = Color.FromArgb("#1A1A1A");
                }
                else
                {
                    if (Application.Current?.Resources.TryGetValue("ThemeButtonBackground", out var bg) == true
                        && bg is Color themedBg)
                        btn.BackgroundColor = themedBg;
                    else
                        btn.BackgroundColor = Color.FromArgb("#8B4513");
                    if (Application.Current?.Resources.TryGetValue("ThemeContrastingText", out var fg) == true
                        && fg is Color themedFg)
                        btn.TextColor = themedFg;
                    else
                        btn.TextColor = Colors.White;
                }
            }
        }

        private void SyncTimeSignatureControlRowPosition()
        {
            if (TimeSignatureControlRow == null)
                return;

            double stroke = StaffBorder?.StrokeThickness ?? 0;
            const double gap = 4;
            double left;
            double top;
            RectF? bounds = _staffDrawable?.LastTimeSignatureBounds;
            if (bounds is RectF r)
            {
                // Place the selector just to the right of the numerals when there is room;
                // otherwise tuck it directly under the meter.
                left = stroke + r.X + r.Width + gap;
                top = stroke + r.Y;
                double overlayW = StaffOverlayGrid?.Width ?? 0;
                double stripW = TimeSignatureControlRow.WidthRequest > 0
                    ? TimeSignatureControlRow.WidthRequest
                    : 220;
                if (overlayW > 0 && left + stripW > overlayW - 8)
                {
                    left = Math.Max(8, stroke + r.X);
                    top = stroke + r.Y + r.Height + gap;
                }
            }
            else
            {
                left = 8;
                top = stroke + 56;
            }

            if (left < 0) left = 0;
            if (top < 0) top = 0;

            TimeSignatureControlRow.VerticalOptions = LayoutOptions.Start;
            TimeSignatureControlRow.HorizontalOptions = LayoutOptions.Start;
            TimeSignatureControlRow.Margin = new Thickness(left, top, 8, 0);
        }

        private void ScheduleSyncTimeSignatureHitTarget()
        {
            Dispatcher.Dispatch(SyncTimeSignatureHitTarget);
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), SyncTimeSignatureHitTarget);
        }

        private void SyncTimeSignatureHitTarget()
        {
            if (TimeSignatureHitTarget == null || StaffGraphicsView == null)
                return;

            bool practiceTuneOwned = _session?.Tune == "Practice Tune" && _session.CurrentTune != null;
            if (_session == null
                || PlayModePickerOptions.IsTunerMode(_session)
                || practiceTuneOwned)
            {
                TimeSignatureHitTarget.IsVisible = false;
                if (_isTimeSignatureControlVisible)
                    SetTimeSignatureControlVisible(false);
                return;
            }

            RectF? bounds = _staffDrawable?.LastTimeSignatureBounds;
            double stroke = StaffBorder?.StrokeThickness ?? 0;
            const double minSize = 48;
            const double pad = 12;

            double x;
            double y;
            double w;
            double h;
            if (bounds is RectF r)
            {
                x = stroke + r.X - pad;
                y = stroke + r.Y - pad;
                w = Math.Max(minSize, r.Width + pad * 2);
                h = Math.Max(minSize, r.Height + pad * 2);
            }
            else
            {
                x = stroke + 48;
                y = stroke + 24;
                w = minSize;
                h = 72;
            }

            if (x < 0) x = 0;
            if (y < 0) y = 0;

            TimeSignatureHitTarget.Margin = new Thickness(x, y, 0, 0);
            TimeSignatureHitTarget.WidthRequest = w;
            TimeSignatureHitTarget.HeightRequest = h;
            TimeSignatureHitTarget.MinimumWidthRequest = minSize;
            TimeSignatureHitTarget.MinimumHeightRequest = minSize;
            TimeSignatureHitTarget.InputTransparent = false;
            TimeSignatureHitTarget.IsEnabled = true;
            TimeSignatureHitTarget.IsVisible = true;

            if (_isTimeSignatureControlVisible)
                SyncTimeSignatureControlRowPosition();
        }

        private void SetTempoControlVisible(bool visible)
        {
            if (_session != null && PlayModePickerOptions.IsTunerMode(_session))
                visible = false;

            if (_isTempoControlVisible == visible)
            {
                if (visible)
                {
                    RefreshTempoControlDisplay();
                    SyncTempoControlRowPosition();
                }
                ApplyTempoControlRowVisibility(visible);
                return;
            }

            _isTempoControlVisible = visible;
            OnPropertyChanged(nameof(IsTempoControlVisible));
            if (visible)
            {
                RefreshTempoControlDisplay();
                SyncTempoControlRowPosition();
            }
            ApplyTempoControlRowVisibility(visible);
        }

        private void ApplyTempoControlRowVisibility(bool visible)
        {
            if (TempoControlRow == null)
            {
                LogTempoTap("STRIP VIS", "TempoControlRow=null");
                return;
            }

            // Direct assignment — do not rely solely on IsTempoControlVisible binding.
            TempoControlRow.IsVisible = visible;
            TempoControlRow.Opacity = visible ? 1 : 0;
            TempoControlRow.InputTransparent = !visible;
            if (visible)
            {
                TempoControlRow.HeightRequest = 52;
                TempoControlRow.MinimumHeightRequest = 52;
                // Force a layout pass so Android measures the newly shown strip.
                TempoControlRow.InvalidateMeasure();
                StaffOverlayGrid?.InvalidateMeasure();
            }

            LogTempoTap(
                "STRIP VIS",
                $"set={visible} row.IsVisible={TempoControlRow.IsVisible} " +
                $"h={TempoControlRow.Height:F0} req={TempoControlRow.HeightRequest:F0} " +
                $"opacity={TempoControlRow.Opacity:F1} z={TempoControlRow.ZIndex} " +
                $"parent={(TempoControlRow.Parent?.GetType().Name ?? "null")}");

            if (visible)
            {
                Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), () =>
                {
                    if (TempoControlRow == null)
                        return;
                    LogTempoTap(
                        "STRIP LAID OUT",
                        $"IsVisible={TempoControlRow.IsVisible} h={TempoControlRow.Height:F0} " +
                        $"w={TempoControlRow.Width:F0} x={TempoControlRow.X:F0} y={TempoControlRow.Y:F0} " +
                        $"opacity={TempoControlRow.Opacity:F1}");
                });
            }
        }

        private void RefreshTempoControlDisplay()
        {
            if (_session == null)
                return;

            int bpm = Math.Clamp(_session.Tempo, NoteSessionService.MinTempo, NoteSessionService.MaxTempo);
            int[] deltas = TempoControlLogic.GetDeltaChoices(bpm);

            ApplyTempoNudgeButton(TempoMinus10Button, bpm, deltas[0]);
            ApplyTempoNudgeButton(TempoMinus5Button, bpm, deltas[1]);
            ApplyTempoNudgeButton(TempoCurrentButton, bpm, deltas[2]);
            ApplyTempoNudgeButton(TempoPlus5Button, bpm, deltas[3]);
            ApplyTempoNudgeButton(TempoPlus10Button, bpm, deltas[4]);
        }

        private static void ApplyTempoNudgeButton(Button? button, int bpm, int delta)
        {
            if (button == null)
                return;
            button.Text = TempoControlLogic.FormatButtonLabel(bpm, delta);
            button.CommandParameter = delta.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Places the strip so its top edge sits just below the ♩=BPM marking (small gap).
        /// </summary>
        private void SyncTempoControlRowPosition()
        {
            if (TempoControlRow == null)
                return;

            double stroke = StaffBorder?.StrokeThickness ?? 0;
            const double gap = 4;
            double top;
            RectF? bounds = _staffDrawable?.LastMusicBpmMarkingBounds;
            if (bounds is RectF r)
                top = stroke + r.Y + r.Height + gap;
            else
                top = stroke + 40;

            if (top < 0)
                top = 0;

            TempoControlRow.VerticalOptions = LayoutOptions.Start;
            TempoControlRow.HorizontalOptions = LayoutOptions.Fill;
            TempoControlRow.Margin = new Thickness(8, top, 8, 0);
        }

        private void ScheduleSyncTempoMarkingHitTarget()
        {
            // Bounds are written during Draw, which runs after Invalidate — sync next frame(s).
            Dispatcher.Dispatch(SyncTempoMarkingHitTarget);
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), SyncTempoMarkingHitTarget);
            ScheduleSyncTimeSignatureHitTarget();
        }

        private void SyncTempoMarkingHitTarget()
        {
            if (TempoMarkingHitTarget == null || StaffGraphicsView == null)
                return;

            if (_session == null || PlayModePickerOptions.IsTunerMode(_session))
            {
                TempoMarkingHitTarget.IsVisible = false;
                return;
            }

            // Prefer drawable bounds; if missing, still place a tappable fallback near the header.
            RectF? bounds = _staffDrawable?.LastMusicBpmMarkingBounds;
            double stroke = StaffBorder?.StrokeThickness ?? 0;
            const double minSize = 48;
            const double pad = 20;

            double x;
            double y;
            double w;
            double h;
            string source;
            if (bounds is RectF r)
            {
                x = stroke + r.X - pad;
                y = stroke + r.Y - pad;
                w = Math.Max(minSize, r.Width + pad * 2);
                h = Math.Max(minSize, r.Height + pad * 2);
                source = "drawable-bounds";
            }
            else
            {
                x = stroke + 100;
                y = stroke + 2;
                w = 96;
                h = minSize;
                source = "fallback-no-drawable-bounds";
            }

            if (x < 0) x = 0;
            if (y < 0) y = 0;

            TempoMarkingHitTarget.Margin = new Thickness(x, y, 0, 0);
            TempoMarkingHitTarget.WidthRequest = w;
            TempoMarkingHitTarget.HeightRequest = h;
            TempoMarkingHitTarget.MinimumWidthRequest = minSize;
            TempoMarkingHitTarget.MinimumHeightRequest = minSize;
            TempoMarkingHitTarget.InputTransparent = false;
            TempoMarkingHitTarget.IsEnabled = true;
            TempoMarkingHitTarget.IsVisible = true;
            ApplyTempoHitTargetDiagnosticsChrome();

            // Keep the strip tucked under the marking whenever hit-target bounds refresh.
            if (_isTempoControlVisible)
                SyncTempoControlRowPosition();

            LogTempoTap(
                "SYNC",
                $"source={source} margin={x:F0},{y:F0} size={w:F0}x{h:F0} " +
                $"visible={TempoMarkingHitTarget.IsVisible} enabled={TempoMarkingHitTarget.IsEnabled} " +
                $"inputTransparent={TempoMarkingHitTarget.InputTransparent} z={TempoMarkingHitTarget.ZIndex} " +
                $"drawableBounds={(bounds is RectF b ? $"{b.X:F0},{b.Y:F0},{b.Width:F0}x{b.Height:F0}" : "null")}");
        }

        private void ApplyTempoHitTargetDiagnosticsChrome()
        {
            if (TempoMarkingHitTarget == null)
                return;

            if (TempoHitTargetDiagnosticsVisible)
            {
                TempoMarkingHitTarget.BackgroundColor = Color.FromRgba(255, 136, 0, 0x99);
                TempoMarkingHitTarget.BorderColor = Colors.Red;
                TempoMarkingHitTarget.BorderWidth = 2;
                TempoMarkingHitTarget.Text = "♩";
                TempoMarkingHitTarget.TextColor = Colors.Black;
                TempoMarkingHitTarget.FontSize = 18;
            }
            else
            {
                TempoMarkingHitTarget.BackgroundColor = Colors.Transparent;
                TempoMarkingHitTarget.BorderColor = Colors.Transparent;
                TempoMarkingHitTarget.BorderWidth = 0;
                TempoMarkingHitTarget.Text = string.Empty;
            }
        }

        private string BuildTempoHitTargetStateDetail()
        {
            if (TempoMarkingHitTarget == null)
                return "hitTarget=null";
            return $"visible={TempoMarkingHitTarget.IsVisible} enabled={TempoMarkingHitTarget.IsEnabled} " +
                   $"inputTransparent={TempoMarkingHitTarget.InputTransparent} " +
                   $"w={TempoMarkingHitTarget.WidthRequest:F0} h={TempoMarkingHitTarget.HeightRequest:F0} " +
                   $"margin={TempoMarkingHitTarget.Margin.Left:F0},{TempoMarkingHitTarget.Margin.Top:F0} " +
                   $"z={TempoMarkingHitTarget.ZIndex} tempo={_session?.Tempo}";
        }

        private static void LogTempoTap(string stage, string detail)
        {
            // Release-safe Android logcat + Debug output (Utils.Log is DEBUG-conditional only).
            FirstNoteAndroidReleaseLog.WriteAlways($"TEMPO TAP {stage}", detail);
        }

        private void OnMakeItEasyClicked(object? sender, EventArgs e)
        {
            if (_session == null)
                return;

            bool nowOn = _session.ToggleMakeItEasy();
            RefreshMakeItEasyButtonAppearance();
            StatusService.Instance.StatusMessage = nowOn
                ? "Make It Easy ON — wider timing, slower red marks (tap again to restore your settings)"
                : "Make It Easy OFF — your previous detection settings were restored";
        }

        private void RefreshMakeItEasyButtonAppearance()
        {
            OnPropertyChanged(nameof(MakeItEasyButtonText));
            OnPropertyChanged(nameof(MakeItEasyButtonBackgroundColor));
            OnPropertyChanged(nameof(MakeItEasyButtonTextColor));
        }

        private async void OnSaveTuneAsClicked(object? sender, EventArgs e)
        {
            try
            {
                string proposed = _savedTunes.SuggestNextTitle();
                string? name = await DisplayPromptAsync(
                    "Save Tune As",
                    "Enter a name for this tune.",
                    accept: "Save",
                    cancel: "Cancel",
                    placeholder: proposed,
                    maxLength: 80,
                    keyboard: Keyboard.Text,
                    initialValue: proposed);

                if (string.IsNullOrWhiteSpace(name))
                    return;

                name = name.Trim();
                if (SavedTuneStore.IsBuiltInTuneTitle(name)
                    || string.Equals(name, PlayModePickerOptions.HalfThroughSixteenthNotes, StringComparison.Ordinal))
                {
                    await DisplayAlertAsync(
                        "Cannot Save",
                        "That name is reserved for a built-in tune. Choose a different name.",
                        "OK");
                    return;
                }

                PracticeTune? toSave = TryCaptureCurrentPracticeTune(name);
                if (toSave == null || toSave.NoteCount == 0)
                {
                    await DisplayAlertAsync(
                        "Nothing to Save",
                        "There is no tune on the staff to save.",
                        "OK");
                    return;
                }

                _savedTunes.Save(toSave);

                // Persist into What to Play → Tunes only. Do not switch the active play mode;
                // otherwise every GO/Stop reloads this saved title instead of Assortment/Random/etc.
                RefreshPracticeTunePickers();
                UpdatePracticePlayItemLabel();
                SyncPlayItemStatusMessage();
                await DisplayAlertAsync(
                    "Saved",
                    $"\"{name}\" was saved and added to What to Play → Tunes. Select it there when you want to practice it.",
                    "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Save Failed", ex.Message, "OK");
            }
        }

        private async void OnDeleteSavedTuneClicked(object? sender, EventArgs e)
        {
            try
            {
                string? title = _session.CurrentTune?.Title
                    ?? Preferences.Default.Get<string?>("SelectedTune", null);

                if (string.IsNullOrWhiteSpace(title)
                    || !SavedTuneStore.IsSavedTuneTitle(title)
                    || SavedTuneStore.IsBuiltInTuneTitle(title)
                    || _savedTunes.GetByTitle(title) == null)
                {
                    await DisplayAlertAsync(
                        "Cannot Delete",
                        "Only user-saved tunes whose names contain \"Saved Tune\" can be deleted with this button.",
                        "OK");
                    return;
                }

                bool confirmed = await DisplayAlertAsync(
                    "Delete Saved Tune",
                    $"Permanently delete \"{title}\"? This cannot be undone.",
                    "Delete",
                    "Cancel");
                if (!confirmed)
                    return;

                if (!_savedTunes.Delete(title))
                {
                    await DisplayAlertAsync("Delete Failed", "The saved tune could not be removed.", "OK");
                    return;
                }

                // Fall back to the first built-in tune so the staff stays usable.
                var fallback = TuneLibrary.All.FirstOrDefault() ?? TuneLibrary.CMajorScale;
                LayoutTestTune.SetEnabled(false);
                _session.IsRandomMode = false;
                _session.SelectPracticeTune(fallback);
                Preferences.Default.Set("SelectedTune", fallback.Title);
                RefreshPracticeTunePickers();
                UpdatePracticePlayItemLabel();
                // Title bar binds StatusMessage — label refresh alone leaves the deleted name visible.
                StatusService.Instance.StatusMessage = GetCurrentPlayItemName();
                await RegenerateNotesAsync();
                await DisplayAlertAsync("Deleted", $"\"{title}\" was removed.", "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Delete Failed", ex.Message, "OK");
            }
        }

        /// <summary>
        /// Captures the currently displayed music as a <see cref="PracticeTune"/>.
        /// Prefers <see cref="NoteSessionService.CurrentTune"/> when already in Practice Tune mode.
        /// </summary>
        private PracticeTune? TryCaptureCurrentPracticeTune(string title)
        {
            if (_session.Tune == "Practice Tune" && _session.CurrentTune != null && _session.CurrentTune.NoteCount > 0)
                return SavedTuneStore.CloneWithTitle(_session.CurrentTune, title);

            var notes = CollectDisplayedGeneratedNotes();
            if (notes.Count == 0)
                return null;

            var ts = TimeSignature.FromDisplayString(_session.GetDisplayTimeSignature());
            // Capture the displayed notation identity (written key + scale/mode), not a
            // pitch-inferred signature — scale walks with minor-family roots depend on Scale.
            var (notationKey, notationScale) = _session.GetNotationKeyAndScale();
            return SavedTuneStore.FromGeneratedNotes(
                title, notes, ts, notationKey, notationScale, _session.InstrumentKey);
        }

        private List<GeneratedNote> CollectDisplayedGeneratedNotes()
        {
            if (_staffDrawable == null)
                return new List<GeneratedNote>();

            var upper = (_staffDrawable.UpperNotes ?? new List<GeneratedNote>())
                .OrderBy(n => n.BeatPosition ?? 0.0)
                .ThenBy(n => n.MeasureIndex ?? 0)
                .ToList();
            var lower = (_staffDrawable.LowerNotes ?? new List<GeneratedNote>())
                .OrderBy(n => n.BeatPosition ?? 0.0)
                .ThenBy(n => n.MeasureIndex ?? 0)
                .ToList();

            if (upper.Count == 0 && lower.Count == 0)
                return new List<GeneratedNote>();

            // Each staff row is laid out from beat 0 with its own MeasureIndex space.
            // Saving must concatenate them into one chronological tune; otherwise
            // overlapping indexes merge two bars into one over-full measure.
            var result = new List<GeneratedNote>(upper.Count + lower.Count);
            result.AddRange(upper);

            if (lower.Count == 0)
                return result;

            double measureBeats = TimeSignature.FromDisplayString(_session.GetDisplayTimeSignature()).TotalBeats;
            double upperEndBeat = 0.0;
            int upperMeasureCount = 0;
            foreach (var n in upper)
            {
                double end = (n.BeatPosition ?? 0.0) + n.BeatDuration;
                if (end > upperEndBeat)
                    upperEndBeat = end;
                int mi = (n.MeasureIndex ?? 0) + 1;
                if (mi > upperMeasureCount)
                    upperMeasureCount = mi;
            }

            if (measureBeats > 1e-9)
            {
                int upperBars = Math.Max(
                    upperMeasureCount,
                    (int)Math.Ceiling(upperEndBeat / measureBeats - 1e-9));
                upperEndBeat = Math.Max(upperEndBeat, upperBars * measureBeats);
                upperMeasureCount = Math.Max(upperMeasureCount, upperBars);
            }

            foreach (var n in lower)
            {
                result.Add(new GeneratedNote
                {
                    MidiNumber = n.MidiNumber,
                    Letter = n.Letter,
                    Octave = n.Octave,
                    Accidental = n.Accidental,
                    SpelledName = n.SpelledName,
                    TargetFrequency = n.TargetFrequency,
                    Duration = n.Duration,
                    IsRest = n.IsRest,
                    MeasureIndex = (n.MeasureIndex ?? 0) + upperMeasureCount,
                    BeatPosition = (n.BeatPosition ?? 0.0) + upperEndBeat,
                    IsPlayedCorrectly = n.IsPlayedCorrectly,
                });
            }

            return result;
        }

        private void RefreshPracticeTunePickers()
        {
            try
            {
                UpdateScaleTunePicker();
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[SavedTune] Refresh pickers: {ex.Message}");
            }
        }

        private async Task RefreshDisplayForLevelChangeAsync()
        {
            _freezeStaff = false;
            _holdResultForChildSession = false;
            _repeatSameSnapshot = null;
            _session.SessionCompleted = false;

            await HideSessionResultBannerAsync(refreshMarqueeForNewLevel: false);

            if (PlayModePickerOptions.IsTunerMode(_session))
            {
                ApplyTunerDisplayState();
                return;
            }

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
        /// <summary>
        /// Updates Level +/- visibility and the on-screen level label from the live session.
        /// Does not mutate <see cref="NoteSessionService.ChildLevel"/> or re-apply settings —
        /// full apply is owned by <see cref="DifficultyLevelMapper.ApplyLevelSettings"/> /
        /// <see cref="DifficultyLevelMapper.PickAndApplyToSession"/>.
        /// </summary>
        private void UpdateChildLevelSliderDisplay()
        {
            OnPropertyChanged(nameof(IsChildLevelSliderVisible));
            if (_session.ChildLevel <= 0) return;

            if (ChildLevelSliderValueLabel != null)
                ChildLevelSliderValueLabel.Text = _session.ChildLevel.ToString();
        }
        /// <summary>
        /// Adopts the saved Home-page level when Music is opened without Home → Start
        /// (e.g. via the flyout menu). Always reapplies range/batch settings so the first
        /// staff build after WhatToPlay → Music matches subsequent displays.
        /// Missing preference defaults to 1 (same as Home), so the level controls
        /// appear on a fresh install without requiring Home → Start first.
        /// </summary>
        private void EnsureChildLevelFromPreferences()
        {
            if (_session.ChildLevel <= 0)
            {
                // Home uses default 1; do not treat "key missing" as ChildLevel 0 or the
                // slider stays invisible until the user taps Start on Home.
                int saved = Math.Clamp(Preferences.Default.Get(ChildLevelPrefKey, 1), 1, 100);
                Preferences.Default.Set(ChildLevelPrefKey, saved);

                _session.ChildLevel = saved;
                if (_session.ScaleSelectionMode == ScaleSelectionMode.ByLevel)
                    _session.SelectedScale = ChildLevelProgression.GetDefaultScaleForLevel(saved);
                else
                    _session.ApplyScaleSelectionOnLevelChange(saved);
#if DEBUG
                DebugLog.WriteLine($"[ChildLevel] Hydrated from preferences: L{saved}");
#endif
            }

            // ChildLevel may already be set from Home while batch/range were never applied
            // this visit — always refresh before generating notes.
            if (_session.ChildLevel > 0)
                DifficultyLevelMapper.ApplyLevelDerivedSettings(_session.ChildLevel, _session);
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
            bool returningToPage = !_isPageVisible;
            // Keep PropertyChanged-driven regenerate off until level-derived settings are ready.
            _allowStaffLayoutSettle = false;
            if (returningToPage)
            {
                _freezeStaff = false;
                // Keep result hold when returning from Settings so the session-end
                // marquee and banner stay visible until the user starts again.
                _holdResultForChildSession = ShouldPreserveSessionEndMarquee();
            }
            _orientation?.ForceLandscape();

            DebugLog.WriteLine($"[DEBUG] OnAppearing: IsAutoRepeatVisible={IsAutoRepeatVisible}, Tune={_session.Tune}");
#if DEBUG
            RefreshBuildIdentificationLabels();
#endif
            // LocalRelease / Release also need this — button defaults to IsVisible=False in XAML.
            RefreshNoteAttemptsDebugButtonVisibility();
            IsAutoRepeatVisible = !PlayModePickerOptions.IsTunerMode(_session);
            UpdateAutoRepeatButtons();
            UpdateEffectiveScaleLabel();
            UpdateNoteEmphasisBanner();

            // Capture soft-pause before Tuner display activation clears _isRunning.
            bool appearAsTuner = PlayModePickerOptions.IsTunerMode(_session);
            bool softPausedBeforeDisplay = _listeningPausedForPageHide;
            bool wasRunningBeforeDisplay = _isRunning;

            if (appearAsTuner)
                ApplyTunerDisplayState();
            else
                UpdateTunerVisibility();
            DeviceDisplay.Current.KeepScreenOn = true;

            if (_session.PendingMasteryPracticeNavigation)
            {
                _session.PendingMasteryPracticeNavigation = false;
                _session.IsDirty = true;
            }

            EnsurePracticePickersReady();
            UpdateInstrumentPickerSelection();
            UpdateKeyPickerSelection();

            // Hydrate level + range/batch BEFORE marking the page visible so a
            // SelectedScale PropertyChanged cannot regenerate with ChildLevel 0 / empty batch.
            EnsureChildLevelFromPreferences();
            UpdateChildLevelSliderDisplay();
            Dispatcher.Dispatch(UpdateChildLevelSliderDisplay);
            Dispatcher.Dispatch(UpdateTitlePlayButtonPosition);

            bool resumeListening = MusicListeningVisibility.ShouldResumeListeningOnAppear(
                softPausedBeforeDisplay || _listeningPausedForPageHide,
                wasRunningBeforeDisplay || _isRunning || softPausedBeforeDisplay,
                _userStoppedListening,
                pageIsVisible: true);

            // Soft-paused Music → appear already in Tuner: keep pause until Tuner is left;
            // never arm Music Count-In while Tuner is the active surface.
            if (MusicListeningVisibility.ShouldDeferResumeWhileTunerVisible(
                    resumeListening, appearAsTuner))
            {
                _listeningPausedForPageHide = true;
                if (_isRunning)
                {
                    StopWaitingCountIn();
                    try { ServiceHelper.GetService<ICountInClickService>()?.Stop(); } catch { }
                    try { _audio?.StopCapture(); } catch { }
                    SetButtonStates(false);
                }
            }
            else
            {
                _listeningPausedForPageHide = false;
            }

            _isPageVisible = true;

            // Regenerate before AutoStart so random→scale changes refresh the staff.
            // When Repeat Same is on, restore the saved snapshot instead of re-randomizing key.
            // Soft-paused return must not regenerate or advance notes.
            // Always prepare scale/key/range first so the first paint matches later ones
            // (previously AutoStart was the first path that picked a usable key/range).
            if (!_isRunning && !resumeListening
                && !ShouldPreserveSessionEndMarquee() && !_holdResultForChildSession)
            {
                try
                {
                    await NavigationBusyService.Instance.RunAsync(async () =>
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        while (StaffGraphicsView != null && StaffGraphicsView.Width <= 0 && sw.ElapsedMilliseconds < 1500)
                            await Task.Delay(40);
                        if (_session.RepeatSameTune && _repeatSameSnapshot?.Notes.Count > 0)
                            await RestoreRepeatSameSnapshotAsync(_repeatSameSnapshot.Notes);
                        else
                        {
                            PrepareFreshScaleAndKeyIfNeeded(forceNewNotes: false, scaleKeyTrigger: "OnAppearing");
                            await RegenerateNotesAsync();
                        }
                    });
                }
                catch (Exception ex)
                {
                    DebugLog.WriteLine($"[OnAppearing] ERROR regenerating notes: {ex}");
                }
            }

            // WhatToPlay → Music often arrives before Shell finishes the first real layout pass.
            // A deferred settle refresh rebuilds/redraws once Width is stable (fixes first-visit haywire).
            _allowStaffLayoutSettle = true;
            ScheduleStaffLayoutSettleRefresh();
            ScheduleSyncTempoMarkingHitTarget();

            if (resumeListening
                && !MusicListeningVisibility.ShouldDeferResumeWhileTunerVisible(
                    resumeListening, appearAsTuner))
            {
                ScheduleResumeListeningAfterAppear();
                return;
            }

            if (MusicListeningVisibility.ShouldScheduleAutoStartOnAppear(
                    listeningPausedForHide: false,
                    userStoppedListening: _userStoppedListening,
                    autoStart: _session.AutoStart,
                    countInEnabled: WaitingCountInSettings.Enabled,
                    isTuner: appearAsTuner,
                    holdResult: _holdResultForChildSession))
            {
                ScheduleAutoStartOnAppear();
                return;
            }

            if (_holdResultForChildSession)
            {
                RestoreSessionEndMarqueeIfNeeded();
                return;
            }

            RestoreSessionEndMarqueeIfNeeded();
        }
        /// <summary>
        /// After navigation, wait until StaffGraphicsView width stops changing, then
        /// re-pack the same generated page at the settled width when the prior pack
        /// was provisional or used a materially different width.
        /// </summary>
        private void ScheduleStaffLayoutSettleRefresh()
        {
            _staffLayoutSettleCts?.Cancel();
            _staffLayoutSettleCts = new CancellationTokenSource();
            var ct = _staffLayoutSettleCts.Token;
            _ = SettleStaffLayoutAfterNavigationAsync(ct);
        }
        private async Task SettleStaffLayoutAfterNavigationAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(80, ct);

                double lastWidth = -1;
                for (int i = 0; i < 12; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!_isPageVisible)
                        return;

                    double width = StaffGraphicsView?.Width ?? 0;
                    if (width > 0 && lastWidth > 0 && Math.Abs(width - lastWidth) < 1.0)
                        break;

                    lastWidth = width;
                    await Task.Delay(40, ct);
                }

                ct.ThrowIfCancellationRequested();
                if (!_isPageVisible)
                    return;

                if (PlayModePickerOptions.IsTunerMode(_session))
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        ApplyTunerHeight();
                        UpdateTitlePlayButtonPosition();
                    });
                    return;
                }

                double settledWidth = StaffGraphicsView?.Width ?? 0;
                if (settledWidth <= 0)
                    return;

                bool needsRepack =
                    _pendingStaffWidthRepack
                    || StaffPageWidthPolicy.NeedsRepackForSettledWidth(
                        _staffPagePack, _staffWidthUsedForLayout, settledWidth);

                if (needsRepack && _staffPagePack != null)
                {
                    if (!StaffPageWidthPolicy.CanRepackNow(_freezeStaff, _holdResultForChildSession))
                    {
                        _pendingStaffWidthRepack = true;
                        DebugLog.WriteLine(
                            $"[StaffLayoutSettle] Defer width repack (freeze={_freezeStaff} hold={_holdResultForChildSession}); " +
                            $"packedAt={_staffWidthUsedForLayout:F0} settled={settledWidth:F0} provisional={_staffPagePack.IsProvisional}");
                        await MainThread.InvokeOnMainThreadAsync(() =>
                        {
                            _staffDrawable?.InvalidateLayoutCache();
                            ApplyStaffHeight();
                            StaffGraphicsView?.Invalidate();
                            UpdateTitlePlayButtonPosition();
                        });
                        return;
                    }

                    // Visual-only: same generated page, even while listening / AutoStart.
                    float height = StaffGraphicsView?.Height > 0
                        ? (float)StaffGraphicsView.Height
                        : StaffPageWidthPolicy.FallbackHeightDip;
                    bool repacked = await RepackStaffPageAtWidthAsync(
                        (float)settledWidth, height, preserveSessionProgress: true);
                    DebugLog.WriteLine(
                        $"[StaffLayoutSettle] Width repack {(repacked ? "ok" : "skipped")} " +
                        $"settled={settledWidth:F0} running={_isRunning} provisionalWas={_staffPagePack?.IsProvisional}");
                    await MainThread.InvokeOnMainThreadAsync(UpdateTitlePlayButtonPosition);
                    return;
                }

                // No width-driven pack change — refresh draw only. Never stamp
                // _staffWidthUsedForLayout here (it must match an actual pack).
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    _staffDrawable?.InvalidateLayoutCache();
                    ApplyStaffHeight();
                    StaffGraphicsView?.Invalidate();
                    UpdateTitlePlayButtonPosition();
                });
            }
            catch (OperationCanceledException)
            {
                // Newer appear/disappear superseded this settle pass.
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[StaffLayoutSettle] ERROR: {ex}");
            }
        }

        private (float Width, float Height, bool IsProvisional) ResolveStaffCanvasSize()
            => StaffPageWidthPolicy.ResolveCanvasSize(
                StaffGraphicsView?.Width ?? 0,
                StaffGraphicsView?.Height ?? 0);

        private void ClearStaffPagePack()
        {
            _staffPagePack = null;
            _pendingStaffWidthRepack = false;
        }

        private void StoreStaffPagePack(
            List<GeneratedNote> pageNotes,
            List<double> pageBars,
            double measureBeats,
            bool isTwoOctaveScaleCut,
            float canvasWidth,
            float canvasHeight,
            bool isProvisional,
            StaffDrawable.StaffMeasureSplitMode splitMode = StaffDrawable.StaffMeasureSplitMode.Balanced,
            bool keepAllNotesVisible = false)
        {
            _staffPagePack = new StaffPagePackState
            {
                PageNotes = pageNotes,
                PageBarBeats = pageBars,
                MeasureBeats = measureBeats,
                IsTwoOctaveScaleCut = isTwoOctaveScaleCut,
                SplitMode = splitMode,
                KeepAllNotesVisible = keepAllNotesVisible,
                IsProvisional = isProvisional,
                PackedCanvasWidth = canvasWidth,
                PackedCanvasHeight = canvasHeight,
            };
            _staffWidthUsedForLayout = StaffPageWidthPolicy.WidthRecordedAfterPack(canvasWidth);
            _pendingStaffWidthRepack = false;

            if (isProvisional)
            {
                DebugLog.WriteLine(
                    $"[StaffPack] Provisional layout at fallback width {canvasWidth:F0} DIP " +
                    $"(GraphicsView.Width unknown); will repack when settled.");
            }
        }

        /// <summary>
        /// Re-runs <see cref="StaffDrawable.SplitMeasuresAcrossStaves(List{GeneratedNote}, IReadOnlyList{double}, float, float, StaffDrawable.StaffMeasureSplitMode)"/> on the cached page
        /// at <paramref name="canvasWidth"/>. Does not call the sequence generator.
        /// </summary>
        private async Task<bool> RepackStaffPageAtWidthAsync(
            float canvasWidth,
            float canvasHeight,
            bool preserveSessionProgress)
        {
            if (_staffDrawable == null || _staffPagePack == null || canvasWidth <= 0)
                return false;

            var state = _staffPagePack;
            var split = StaffPageWidthPolicy.SplitCachedPage(
                _staffDrawable, state, canvasWidth, canvasHeight);

            var upperFlat = split.UpperNotes.ToList();
            var lowerFlat = state.IsTwoOctaveScaleCut
                ? StaffPageWidthPolicy.ApplyTwoOctaveLowerCut(split.LowerNotes)
                : split.LowerNotes.ToList();

            double lowerBeatShift = lowerFlat.Count > 0 ? (lowerFlat[0].BeatPosition ?? 0.0) : 0.0;
            lowerFlat = ShiftStaffBeatPositions(lowerFlat, lowerBeatShift);

            var existingUpper = new HashSet<double>();
            var existingLower = new HashSet<double>();
            var upperBarBeats = ComputeStaffBarBeats(upperFlat, state.MeasureBeats, existingUpper);
            var lowerBarBeats = ComputeStaffBarBeats(lowerFlat, state.MeasureBeats, existingLower);

            int placedMeasures = split.UpperMeasureCount + split.LowerMeasureCount;
            if (state.IsTwoOctaveScaleCut)
            {
                // Keep generation cursor at full generated walk length (unchanged by width).
            }
            else
            {
                double upperBeats = upperFlat.Sum(n => n.BeatDuration);
                double lowerBeats = lowerFlat.Sum(n => n.BeatDuration);
                int upperPitches = upperFlat.Count(n => !n.IsRest);
                int lowerPitches = lowerFlat.Count(n => !n.IsRest);
                // Saved/practice tunes keep every note on a staff; cursor covers the full page.
                _seqNextMeasureIndex = state.KeepAllNotesVisible
                    ? split.TotalMeasureCount
                    : placedMeasures;
                _seqNextBeatOffset = upperBeats + lowerBeats;
                _seqNextGlobalNoteIndex = upperPitches + lowerPitches;
                _lowerMeasureIndex = _seqNextMeasureIndex;
                _lowerBeatOffset = _seqNextBeatOffset;
                _lowerGlobalNoteIndex = _seqNextGlobalNoteIndex;
            }

            int preservedCurrent = _session.CurrentNoteIndex;
            var preservedFeedback = preserveSessionProgress
                ? _session.FeedbackViewModels.ToList()
                : null;
            var preservedCorrect = preserveSessionProgress
                ? new HashSet<int>(_session.CorrectNoteIndices)
                : null;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ApplyStaffSplitToDrawable(
                    upperFlat, lowerFlat, upperBarBeats, lowerBarBeats,
                    resetNoteStates: !preserveSessionProgress);

                if (preserveSessionProgress)
                    RebuildNotesToDrawPreservingProgress(
                        upperFlat, lowerFlat, preservedCurrent, preservedFeedback, preservedCorrect);
                else
                    RebuildNotesToDrawFresh(upperFlat, lowerFlat);

                state.IsProvisional = false;
                state.PackedCanvasWidth = canvasWidth;
                state.PackedCanvasHeight = canvasHeight;
                _staffWidthUsedForLayout = StaffPageWidthPolicy.WidthRecordedAfterPack(canvasWidth);
                _pendingStaffWidthRepack = false;

                ApplyStaffHeight();
                StaffGraphicsView?.Invalidate();
            });

#if DEBUG
            DebugLog.WriteLine(
                $"[StaffRepack] same page → upper={split.UpperMeasureCount} lower={split.LowerMeasureCount} " +
                $"unplaced={split.UnplacedMeasureCount} width={canvasWidth:F0} " +
                $"preserve={preserveSessionProgress} current={_session.CurrentNoteIndex}");
#endif
            return true;
        }

        private void ApplyStaffSplitToDrawable(
            List<GeneratedNote> upperFlat,
            List<GeneratedNote> lowerFlat,
            List<double> upperBarBeats,
            List<double> lowerBarBeats,
            bool resetNoteStates)
        {
            var v3 = _staffDrawable;
            if (v3 == null)
                return;

            v3.UpperNotes = upperFlat;
            v3.LowerNotes = lowerFlat;
            v3.UpperBarBeats = upperBarBeats;
            v3.LowerBarBeats = lowerBarBeats;
            v3.InvalidateLayoutCache();
            _sessionUpperPitchCount = upperFlat.Count(n => !n.IsRest);

            if (resetNoteStates)
            {
                v3.UpperNoteStates = new StaffNoteState[upperFlat.Count];
                v3.LowerNoteStates = new StaffNoteState[lowerFlat.Count];
                v3.IsUpperActive = true;
                v3.ActiveNoteIndex = 0;
                v3.UpperAlpha = 1f;
                v3.LowerAlpha = 1f;
                for (int i = 0; i < upperFlat.Count; i++)
                {
                    if (!upperFlat[i].IsRest)
                    {
                        v3.UpperNoteStates[i] = StaffNoteState.Current;
                        v3.ActiveNoteIndex = i;
                        break;
                    }
                }
            }
            else
            {
                // Keep lengths aligned; SyncStaffNoteStates refreshes colors from session.
                if (v3.UpperNoteStates == null || v3.UpperNoteStates.Length != upperFlat.Count)
                    v3.UpperNoteStates = new StaffNoteState[upperFlat.Count];
                if (v3.LowerNoteStates == null || v3.LowerNoteStates.Length != lowerFlat.Count)
                    v3.LowerNoteStates = new StaffNoteState[lowerFlat.Count];
                SyncStaffNoteStates();
            }
        }

        private void RebuildNotesToDrawFresh(
            List<GeneratedNote> upperFlat,
            List<GeneratedNote> lowerFlat)
        {
            var rhythmOrder = upperFlat.Concat(lowerFlat).ToList();
            var rhythmSlots = RhythmStartGate.BuildSlots(rhythmOrder);
            int sessionIdx = 0;
            int pitchIdx = 0;
            _session.NotesToDraw.Clear();
            _session.FeedbackViewModels.Clear();
            var (noteKey, noteScale) = _session.GetNotationKeyAndScale();
            foreach (var gn in rhythmOrder)
            {
                if (gn.IsRest) continue;
                var slot = rhythmSlots[pitchIdx++];
                var (midi, name) = NoteSessionService.ResolveTargetPitch(gn, noteKey, noteScale);
                _session.NotesToDraw.Add(new NoteInfo
                {
                    Midi = midi,
                    Name = name,
                    TargetFreq = 440.0 * Math.Pow(2.0, (midi - 69) / 12.0),
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
        }

        private void RebuildNotesToDrawPreservingProgress(
            List<GeneratedNote> upperFlat,
            List<GeneratedNote> lowerFlat,
            int preservedCurrent,
            List<FeedbackItem>? preservedFeedback,
            HashSet<int>? preservedCorrect)
        {
            var rhythmOrder = upperFlat.Concat(lowerFlat).ToList();
            var rhythmSlots = RhythmStartGate.BuildSlots(rhythmOrder);
            int sessionIdx = 0;
            int pitchIdx = 0;
            _session.NotesToDraw.Clear();
            _session.FeedbackViewModels.Clear();
            var (noteKey, noteScale) = _session.GetNotationKeyAndScale();
            foreach (var gn in rhythmOrder)
            {
                if (gn.IsRest) continue;
                var slot = rhythmSlots[pitchIdx++];
                var (midi, name) = NoteSessionService.ResolveTargetPitch(gn, noteKey, noteScale);
                _session.NotesToDraw.Add(new NoteInfo
                {
                    Midi = midi,
                    Name = name,
                    TargetFreq = 440.0 * Math.Pow(2.0, (midi - 69) / 12.0),
                    X = 0f,
                    Duration = gn.Duration,
                    StartBeat = slot.StartBeat,
                    DurationBeats = slot.DurationBeats,
                    GateBeatsAfterPrevious = slot.GateBeatsAfterPrevious,
                });

                if (preservedFeedback != null && sessionIdx < preservedFeedback.Count)
                    _session.FeedbackViewModels.Add(preservedFeedback[sessionIdx]);
                else
                    _session.FeedbackViewModels.Add(new FeedbackItem(sessionIdx, 0, 0, false));
                sessionIdx++;
            }

            _session.ConfigureRhythmStartGates();
            _session.RestoreCurrentNoteIndexAfterStaffRepack(preservedCurrent);
            _sessionUpperPitchCount = upperFlat.Count(n => !n.IsRest);

            // CorrectNoteIndices are index-based; prefix indices remain valid when packing
            // places earlier measures first. Drop any that fall outside the new list.
            if (preservedCorrect != null)
            {
                _session.CorrectNoteIndices.Clear();
                foreach (int idx in preservedCorrect)
                {
                    if (idx >= 0 && idx < _session.NotesToDraw.Count)
                        _session.CorrectNoteIndices.Add(idx);
                }
            }

            SyncStaffNoteStates();
        }
        protected override void OnNavigatedTo(NavigatedToEventArgs args)
        {
            base.OnNavigatedTo(args);
            // Soft-paused return keeps _isRunning true and OnAppearing already scheduled
            // ScheduleResumeListeningAfterAppear. AutoStart shares that CTS — calling it
            // here cancels the resume (repro: Music → My Progress → Music).
            if (MusicListeningVisibility.ShouldScheduleAutoStartOnNavigatedTo(
                    holdResult: _holdResultForChildSession,
                    isTuner: PlayModePickerOptions.IsTunerMode(_session),
                    userStoppedListening: _userStoppedListening,
                    isRunning: _isRunning,
                    autoStart: _session.AutoStart,
                    countInEnabled: WaitingCountInSettings.Enabled))
            {
                ScheduleAutoStartOnAppear();
            }

            // Shell calls OnNavigatedTo after it has finished restoring scroll position,
            // so this is the correct place to snap the scroll so no note heads are hidden.
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (StaffGraphicsView != null && StaffGraphicsView.Width <= 0
                           && sw.ElapsedMilliseconds < 1500)
                        await Task.Delay(40);

                    // One extra frame so StaffBorder.Y is valid after Width settles.
                    await Task.Delay(50);

                    double scrollY = 0;
                    if (_staffDrawable != null)
                    {
                        // StaffBorder sits inside the VerticalStackLayout.
                        // Its Y relative to MainScrollView content is its absolute position
                        // within MainPageMainLayout.
                        double borderY = StaffBorder.Y
                                       + (StaffBorder.Parent is View p ? p.Y : 0);
                        scrollY = Math.Max(0, borderY);
                    }
                    await MainScrollView.ScrollToAsync(0, scrollY, false);
                    UpdateTitlePlayButtonPosition();
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
            _allowStaffLayoutSettle = false;

            _listeningPausedForPageHide = MusicListeningVisibility.ShouldPauseListeningForHide(
                _isRunning,
                _waitingCountInActive,
                _userStoppedListening);

            // Cancel pending AutoStart / in-flight StartListening so a delayed Arm cannot
            // sound Count-In after Music is obscured (NoteAttempts or any other page).
            CancelPendingListeningStarts();
            _staffLayoutSettleCts?.Cancel();
            _playCts?.Cancel();
            StopWaitingCountIn();
            _ = StopReferenceToneAsync(resumeListening: false);
            _ = StopTunerPitchAsync();
            try { ServiceHelper.GetService<ICountInClickService>()?.Stop(); } catch { }
            _audio?.StopCapture();
            _session?.ClearCountInClickSelfSoundSuppress("page disappearing");

            if (_listeningPausedForPageHide)
            {
                // Soft-pause: keep _isRunning so return resumes Count-In without
                // regenerating notes or treating hide as an intentional Stop.
                if (!ShouldPreserveSessionEndMarquee())
                    StatusService.Instance.StatusMessage = "Listening paused.";
            }
            else
            {
                SetButtonStates(false);
                if (!ShouldPreserveSessionEndMarquee())
                    StatusService.Instance.StatusMessage = "Stopped listening.";
            }

            DeviceDisplay.Current.KeepScreenOn = false;
        }
        private async void OnNavigateWhatToPlayClicked(object? sender, EventArgs e)
        {
            await NavigationBusyService.GoToAsync("//WhatToPlayPage");
        }
        private async void OnPlayEvaluateClicked(object? sender, EventArgs e)
        {
            if (_session.Tune == "Tuner")
            {
                // Obsolete title Play is never shown in Tuner; pitch is staff/note UI only.
                return;
            }

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

            _suppressSessionRegenerate = true;
            try
            {
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
            catch
            {
                FinishPlaybackInstrumentRestore();
                _isPlaying = false;
                SetPlayButtonPlaying(false);
                SetButtonStates(false);
                throw;
            }
        }

        /// <summary>
        /// Restores the pre-Play instrument without regenerating the displayed exercise.
        /// </summary>
        private void FinishPlaybackInstrumentRestore()
        {
            try
            {
                if (_savedInstrumentIndexForPlayback >= 0 && InstrumentPicker != null)
                    InstrumentPicker.SelectedIndex = _savedInstrumentIndexForPlayback;
                if (!string.IsNullOrEmpty(_savedInstrumentForPlayback))
                    _session.Instrument = _savedInstrumentForPlayback;
            }
            catch
            {
                // best-effort
            }
            finally
            {
                _savedInstrumentForPlayback = null;
                _savedInstrumentIndexForPlayback = -1;
                _suppressSessionRegenerate = false;
            }
        }
        private void ResetPitchCapture()
        {
            _pitchWindow.Reset();
            _isBelowThreshold = true;
        }

        private void OnAudioBlock(short[] pcm16)
        {
            var buf = new float[pcm16.Length];
            for (var i = 0; i < pcm16.Length; i++)
            {
                buf[i] = pcm16[i] / 32768f;
            }

            var rms = PitchDetectionService.ComputeRms(buf);
            _session.SetLastDetectionTelemetry(rms);
            _session.ObserveLoudness(rms);

            // Don't accumulate audio during ignore period — ensures the first
            // detection after cooldown uses entirely fresh samples.
            // Exception: during waiting Count-In, keep ingesting so on-beat playing
            // (which overlaps click suppress) can still fill a detection window.
            // Click self-sound is filtered at accept time by frequency proximity.
            bool suppressActive = _session.ShouldIgnoreAudio(DateTime.UtcNow);
            bool ignoreAudio = suppressActive && !_waitingCountInActive;
            if (_audioSuppressWasActive && !suppressActive)
            {
                DebugLog.WriteLine("[AudioSuppress] OFF — reason: guard completed");
                _audioSuppressWasActive = false;
            }
            else if (suppressActive)
            {
                _audioSuppressWasActive = true;
            }
            _pitchWindow.EnsureWindowSize(_session.PitchWindowSize);
            var ingest = _pitchWindow.Ingest(buf, rms, _session.RmsThreshold, ignoreAudio);

            if (ingest.Kind == PitchWindowIngestKind.IgnoredQuiet
                || ingest.Kind == PitchWindowIngestKind.DiscardedIgnorePeriod)
            {
                if (_session.CurrentNoteIndex == 0
                    && _session.Tune != "Tuner"
                    && !_session.SessionCompleted
                    && FirstNoteAndroidReleaseLog.StillWaitingForFirstAccept)
                {
                    // SPECIAL DEBUG FOR ANDROID LOG IN RELEASE MODE
                    _session.LogFirstNoteAndroidReleaseDiagnostic(
                        stage: ingest.Kind == PitchWindowIngestKind.IgnoredQuiet
                            ? "preEval-quiet"
                            : "preEval-discardedIgnorePeriod",
                        freq: 0,
                        accepted: false,
                        rejectReason: ingest.Kind == PitchWindowIngestKind.IgnoredQuiet
                            ? "NotDetected-RmsBelowThreshold"
                            : "NotDetected-AudioIgnorePeriod",
                        countInOrConductorState: _waitingCountInActive
                            ? "waitingCountIn"
                            : null,
                        extra: $"rms={rms:F4} threshold={_session.RmsThreshold:F4}");
                }
                return;
            }

            if (ingest.Kind == PitchWindowIngestKind.BecameSilent)
            {
                if (!_isBelowThreshold)
                {
                    DebugLog.WriteLine("[Audio] Sustained quiet — resetting pitch window");
                    _isBelowThreshold = true;
                    _session.NotifySilence();
                }

                // Keep polling while silent so the late window can freeze the musical
                // timeline even when BecameSilent fired before the window expired.
                if (_session.AdvanceTimelineForExpiredNotes())
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (!_session.SessionCompleted)
                            SyncStaffNoteStates();
                    });
                }
                return;
            }

            if (ingest.StartedNewOnset)
            {
                _isBelowThreshold = false;
                // Fresh onset after silence — completes same-pitch re-trigger arming.
                _session.NotifyNoteAttack();

                if (_isRunning
                    && !_dismissedResultBannerForFirstSound
                    && SessionResultBanner?.IsVisible == true)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                        _ = DismissSessionResultBannerAndScrollToStaffAsync());
                }
            }

            if (ingest.Kind != PitchWindowIngestKind.WindowReady)
                return;

            // Hop-based overlap: shift the buffer by half so subsequent
            // detections reuse the stable tail of the previous window,
            // reducing transient/attack bias that causes flat readings.
            _pitchWindow.HopHalf();

            var now = DateTime.UtcNow;
            if ((now - _lastProcess).TotalMilliseconds < _session.CooldownMs)
                return;

            lock (_processLock)
            {
                // Normal cooldown: skip detection so transitional audio cannot
                // pollute smoothing. During waiting Count-In, keep detecting —
                // click self-sound is rejected by frequency on the UI thread.
                // (Previously this return blocked on-beat first notes for most of
                // each beat while IgnoreAudio covered the click.)
                if (_session.ShouldIgnoreAudio(DateTime.UtcNow) && !_waitingCountInActive)
                    return;

                _lastProcess = now;

                double freq = PitchDetectionService.DetectPitchMcLeod(
                    _pitchWindow.Buffer, _pitchWindow.WindowSize, _session.SampleRate);
                freq = freq * Math.Pow(2, _session.PitchOffsetCents / 1200.0);

                if (freq == 0)
                {
                    // Detector often drops during tonguing while RMS stays loud (clarinet/voice).
                    // Count that as silence toward unlocking a repeated same pitch.
                    if (_session.IsAwaitingNoteOn)
                        _session.NotifyPitchStopped();
                    if (_session.CurrentNoteIndex == 0
                        && _session.Tune != "Tuner"
                        && !_session.SessionCompleted
                        && FirstNoteAndroidReleaseLog.StillWaitingForFirstAccept)
                    {
                        // SPECIAL DEBUG FOR ANDROID LOG IN RELEASE MODE
                        _session.LogFirstNoteAndroidReleaseDiagnostic(
                            stage: "preEval-freqZero",
                            freq: 0,
                            accepted: false,
                            rejectReason: "NotDetected-PitchDetectorReturnedZero",
                            countInOrConductorState: _waitingCountInActive
                                ? "waitingCountIn"
                                : null);
                    }
                    return;
                }

                // Pitch returned after a pitch-dropout silence arm — completes same-pitch
                // re-trigger when RMS never dipped (so NotifyNoteAttack from onset did not fire).
                _session.NotifyPitchResumed();

                freq = _session.SmoothPitch(freq);

                if (_session.Tune == "Tuner")
                {
                    // Live mic detection only — never mutate _referenceWrittenMidi / picker selection.
                    if (_isReferenceTonePlaying || _isTunerPitchPlaying)
                        return;

                    _session.UpdateTunerLastNote(freq);
                    // Log only when the heard note identity changes (avoid per-buffer spam).
                    string? heard = _session.TunerLastNoteName;
                    if (!string.Equals(heard, _lastLoggedTunerHeardNote, StringComparison.Ordinal))
                    {
                        _lastLoggedTunerHeardNote = heard;
                        DebugLog.WriteLine(
                            $"[Tuner] accept freq={freq:F1} note={heard} cents={_session.TunerLastCents} " +
                            $"(selectedRefMidi={_referenceWrittenMidi})");
                    }

                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        RefreshTunerPickerDisplayLabel();
                        UpdateTunerStaffDisplay();
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

                    // Waiting count-in: accept correct first note; reject click self-sound
                    // by frequency (and incorrect pitches). Do not require the listening
                    // gap between clicks — players articulate on the beat.
                    if (_waitingCountInActive)
                    {
                        if (_session.CurrentNoteIndex != 0 || _session.NotesToDraw.Count == 0)
                            return;

                        var countInResult = _session.Evaluate(freq);
                        bool nearClick = WaitingCountInLogic.IsNearCountInClickFrequency(
                            freq,
                            WaitingCountInSettings.AccentedPitchHz,
                            WaitingCountInSettings.UnaccentedPitchHz);
                        if (!WaitingCountInLogic.ShouldAcceptFirstNoteToEndCountIn(
                                countInResult.correct,
                                countInActive: true,
                                currentNoteIndex: 0,
                                withinSelfSoundSuppressWindow: false,
                                heardHz: freq,
                                accentedClickHz: WaitingCountInSettings.AccentedPitchHz,
                                unaccentedClickHz: WaitingCountInSettings.UnaccentedPitchHz))
                        {
                            if (nearClick)
                            {
                                DebugLog.WriteLine(
                                    $"[CountIn] ignored click self-sound heardHz={freq:F1} correct={countInResult.correct}");
                            }
                            else if (countInResult.correct)
                            {
                                DebugLog.WriteLine(
                                    $"[CountIn] correct pitch not accepted heardHz={freq:F1}");
                            }
                            else
                            {
                                DebugLog.WriteLine(
                                    $"[CountIn] waiting for first note heardHz={freq:F1} correct=False");
                            }

                            // SPECIAL DEBUG FOR ANDROID LOG IN RELEASE MODE
                            string reason = nearClick
                                ? "CountIn-ClickSelfSound"
                                : countInResult.correct
                                    ? "CountIn-CorrectPitchNotAccepted"
                                    : "CountIn-WrongPitch";
                            _session.LogFirstNoteAndroidReleaseDiagnostic(
                                stage: "countIn-reject",
                                freq: freq,
                                evaluateResult: countInResult,
                                pitchPassed: countInResult.correct,
                                timingPassed: null,
                                accepted: false,
                                rejectReason: reason,
                                countInOrConductorState: "waitingCountIn");
                            return;
                        }

                        // Stop clicks and CLEAR suppress before scoring — a residual
                        // Suppress here would make UpdateFeedbackForCurrent reject the note
                        // and leave the session unable to accept pitches cleanly.
                        StopWaitingCountIn();
                        _session.ClearCountInClickSelfSoundSuppress("first note accepted — clear before score");
                        _session.StartListeningClock();
                        if (!_audio.IsCapturing
                            && !_audio.TryStartCapture(OnAudioBlock, out var capErr))
                        {
                            DebugLog.WriteLine($"[CountIn] capture after first note failed: {capErr}");
                        }
                        DebugLog.WriteLine(
                            $"[AudioSuppress] pitch evaluated — expected first note, heardHz={freq:F1}");
                        if (_session.UpdateFeedbackForCurrent(freq, countInResult))
                            SyncStaffNoteStates();
                        // Brief residual guard only AFTER scoring, so click bleed cannot steal note 1.
                        _session.SuppressCountInClickSelfSound(0, "residual guard after first-note accept");
                        ResetPitchCapture();
                        return;
                    }

                    // Re-check gates on the UI thread so queued callbacks cannot
                    // advance more than one note from a single sustained tone.
                    if (_session.ShouldIgnoreAudio(DateTime.UtcNow))
                    {
                        var cooldownResult = _session.Evaluate(freq);
                        _session.LogAudioCooldownRejectionIfPitchIdentified(freq, cooldownResult.cents);
                        DebugLog.WriteLine(
                            $"[AudioSuppress] pitch detected but ignored — reason: IgnoreAudioUntilUtc " +
                            $"heardHz={freq:F1} correct={cooldownResult.correct}");
                        // SPECIAL DEBUG FOR ANDROID LOG IN RELEASE MODE
                        _session.LogFirstNoteAndroidReleaseDiagnostic(
                            stage: "preEval-audioCooldown",
                            freq: freq,
                            evaluateResult: cooldownResult,
                            pitchPassed: cooldownResult.correct,
                            accepted: false,
                            rejectReason: "AudioCooldown");
                        return;
                    }

                    if (_session.IsAwaitingNoteOn)
                    {
                        var pendingResult = _session.Evaluate(freq);
                        _session.LogNoteOnGateRejectionIfPitchIdentified(freq, pendingResult.cents);

                        // SPECIAL DEBUG FOR ANDROID LOG IN RELEASE MODE
                        _session.LogFirstNoteAndroidReleaseDiagnostic(
                            stage: "preEval-awaitingNoteOn",
                            freq: freq,
                            evaluateResult: pendingResult,
                            pitchPassed: pendingResult.correct,
                            accepted: false,
                            rejectReason: "AwaitingNoteOn");

                        // Keep the status bar honest during same-pitch repeats (E-E-E): the
                        // previous note already matched; we are waiting for re-articulation.
                        if (_session.IsAwaitingSamePitchRetrigger
                            && _session.CurrentNoteIndex < _session.NotesToDraw.Count)
                        {
                            var expected = _session.ResolveWrittenEvaluationName(
                                _session.NotesToDraw[_session.CurrentNoteIndex]);
                            StatusService.Instance.StatusMessage =
                                $"Expected: {expected} — tongue/re-attack for repeated note";
                        }
                        return;
                    }

                    var result = _session.Evaluate(freq);
                    if (!_session.TryArmListeningClockOnFirstCorrectPitch(result.correct))
                    {
                        // SPECIAL DEBUG FOR ANDROID LOG IN RELEASE MODE
                        _session.LogFirstNoteAndroidReleaseDiagnostic(
                            stage: "preEval-clockNotArmed",
                            freq: freq,
                            evaluateResult: result,
                            pitchPassed: result.correct,
                            timingPassed: null,
                            accepted: false,
                            rejectReason: result.correct
                                ? "ClockNotArmed"
                                : "WrongPitchBeforeClockArmed");
                        return;
                    }

                    string expectedName = _session.CurrentNoteIndex < _session.NotesToDraw.Count
                        ? _session.ResolveWrittenEvaluationName(_session.NotesToDraw[_session.CurrentNoteIndex])
                        : "-";
                    DebugLog.WriteLine(
                        $"[AudioSuppress] pitch detected and evaluated — expected {expectedName}, " +
                        $"heardHz={freq:F1} correct={result.correct}");

                    // Only accept the note as correct if it matches the expected note (including octave) at the current index
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
        /// Runs Count-In for a generation armed by the caller. Must not increment the
        /// generation again — Stop invalidates by bumping it so this method exits.
        /// </summary>
        private async Task StartWaitingCountInAsync(int armedGeneration)
        {
            if (_waitingCountInPlayer == null || _session == null)
                return;

            if (!WaitingCountInArming.MayBegin(
                    armedGeneration,
                    Volatile.Read(ref _waitingCountInGeneration),
                    _isRunning)
                || !_isPageVisible
                || PlayModePickerOptions.IsTunerMode(_session))
            {
                DebugLog.WriteLine("[CountIn] skipped — Stopped, obscured, Tuner, or superseded before arm");
                return;
            }

            try { _waitingCountInCts?.Cancel(); } catch { }
            try { _waitingCountInCts?.Dispose(); } catch { }
            _waitingCountInCts = new CancellationTokenSource();
            var ct = _waitingCountInCts.Token;
            _waitingCountInActive = true;
            _session?.MarkCountInStartUtc();

            try
            {
                int beats = WaitingCountInLogic.GetBeatsPerMeasure(_session.GetDisplayTimeSignature());
                DebugLog.WriteLine(
                    $"[CountIn] starting beats/measure={beats} tempo={_session.Tempo} " +
                    $"accentHz={WaitingCountInSettings.AccentedPitchHz:F0} " +
                    $"vol={WaitingCountInSettings.AccentedVolume:F2}/{WaitingCountInSettings.UnaccentedVolume:F2} " +
                    $"durPct={WaitingCountInSettings.BeatDurationPercent}");

                // Mic stays open for the whole count-in so Mary can be heard.
                // Clicks use the shared sine playback service (audible with mic open on Android).
                try { _audio.StopCapture(); } catch { }
                await Task.Delay(80, ct).ConfigureAwait(false);

                if (!WaitingCountInArming.MayContinue(
                        armedGeneration,
                        Volatile.Read(ref _waitingCountInGeneration),
                        _isRunning,
                        ct.IsCancellationRequested)
                    || !_isPageVisible
                    || PlayModePickerOptions.IsTunerMode(_session))
                {
                    DebugLog.WriteLine("[CountIn] aborted after delay — Stopped, obscured, Tuner, or superseded");
                    return;
                }

                ResetPitchCapture();
                if (!_audio.TryStartCapture(OnAudioBlock, out var startCapErr))
                {
                    DebugLog.WriteLine($"[CountIn] initial capture failed: {startCapErr}");
                    // SPECIAL DEBUG FOR ANDROID LOG IN RELEASE MODE
                    FirstNoteAndroidReleaseLog.WriteAlways(
                        "countIn-capture",
                        $"initialFailed err={startCapErr}");
                }

                await _waitingCountInPlayer.RunAsync(
                    tempoBpm: _session.Tempo,
                    beatsPerMeasure: beats,
                    accentedVolume: WaitingCountInSettings.AccentedVolume,
                    unaccentedVolume: WaitingCountInSettings.UnaccentedVolume,
                    accentedPitchHz: WaitingCountInSettings.AccentedPitchHz,
                    unaccentedPitchHz: WaitingCountInSettings.UnaccentedPitchHz,
                    beatDurationPercent: WaitingCountInSettings.BeatDurationPercent,
                    externalCt: ct,
                    beforeClickAsync: (clickDurationMs, clickCt) =>
                    {
                        // Do not arm IgnoreAudio per click. Time-based suppress blocked
                        // on-beat first notes (most of each beat). Click self-sound is
                        // filtered by frequency in the Count-In accept path instead.
                        _ = clickDurationMs;
                        _ = clickCt;
                        if (!_isPageVisible || PlayModePickerOptions.IsTunerMode(_session))
                            throw new OperationCanceledException();
                        return Task.CompletedTask;
                    },
                    getTempoBpm: () => Math.Clamp(
                        _session.Tempo,
                        NoteSessionService.MinTempo,
                        NoteSessionService.MaxTempo));

                DebugLog.WriteLine("[CountIn] loop ended");

                // If Count-In was cancelled/stopped, restore evaluation immediately.
                // Do NOT extend suppress here — that permanently blocked post-Count-In detection
                // when combined with StopWaitingCountIn's previous residual Suppress.
                try
                {
                    if (!_waitingCountInActive
                        || armedGeneration != Volatile.Read(ref _waitingCountInGeneration)
                        || !_isPageVisible)
                    {
                        _session?.ClearCountInClickSelfSoundSuppress("Count-In loop ended (cancelled/stopped)");
                    }
                    else
                    {
                        // Still waiting for first note after a fixed loop end: brief residual only.
                        _session?.SuppressCountInClickSelfSound(0, "residual guard after Count-In loop");
                        ResetPitchCapture();
                    }
                }
                catch { }

                // Downbeat after count-in: do not start the conductor clock here.
                // Elapsed time must begin at the first detected pitch so notes are not
                // marked missed while the player waits to begin playing.
                if (WaitingCountInArming.MayContinue(
                        armedGeneration,
                        Volatile.Read(ref _waitingCountInGeneration),
                        _isRunning,
                        cancellationRequested: false)
                    && _isPageVisible
                    && !PlayModePickerOptions.IsTunerMode(_session)
                    && _session != null)
                {
                    _session.MarkCountInEndUtc();
                    DebugLog.WriteLine("[CountIn] downbeat — conductor clock deferred until first pitch");
                }

                // Keep mic open if still waiting for the first note.
                if (WaitingCountInArming.MayContinue(
                        armedGeneration,
                        Volatile.Read(ref _waitingCountInGeneration),
                        _isRunning && _waitingCountInActive,
                        cancellationRequested: false)
                    && _isPageVisible
                    && !PlayModePickerOptions.IsTunerMode(_session)
                    && !_audio.IsCapturing)
                {
                    ResetPitchCapture();
                    if (!_audio.TryStartCapture(OnAudioBlock, out var err))
                        DebugLog.WriteLine($"[CountIn] post-loop capture failed: {err}");
                }
            }
            catch (OperationCanceledException)
            {
                DebugLog.WriteLine("[CountIn] cancelled");
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[CountIn] error: {ex}");
            }
            finally
            {
                if (armedGeneration == Volatile.Read(ref _waitingCountInGeneration)
                    && _waitingCountInPlayer?.IsActive != true)
                    _waitingCountInActive = false;
            }
        }

        private void StopWaitingCountIn()
        {
            WaitingCountInArming.Invalidate(ref _waitingCountInGeneration);
            _waitingCountInActive = false;
            // Always restore evaluation when Count-In stops/cancels. Residual protection
            // for an accepted first note is applied by the early-accept path AFTER scoring.
            try
            {
                _session?.ClearCountInClickSelfSoundSuppress("Count-In stopped");
                ResetPitchCapture();
            }
            catch { }
            try { _waitingCountInCts?.Cancel(); } catch { }
            try { _waitingCountInPlayer?.Stop(); } catch { }
        }

        /// <summary>
        /// App swipe-away / sleep / window close — stop Count-In and Tuner metronome beeps.
        /// </summary>
        private void OnAppCueAudioSuspended()
        {
            try
            {
                void StopLocal()
                {
                    StopWaitingCountIn();
                    try { _waitingCountInPlayer?.Stop(); } catch { }
                    try { ServiceHelper.GetService<ICountInClickService>()?.Stop(); } catch { }
                    try { _audio?.StopCapture(); } catch { }
                    _session?.ClearCountInClickSelfSoundSuppress("app suspended");
                    _ = StopReferenceToneAsync(resumeListening: false);
                    _ = StopTunerPitchAsync();
                }

                if (MainThread.IsMainThread)
                    StopLocal();
                else
                    MainThread.BeginInvokeOnMainThread(StopLocal);
            }
            catch { }
        }

        /// <summary>
        /// Cancels pending AutoStart / session-start work so Stop cannot be undone by a
        /// delayed callback that would re-arm Count-In.
        /// </summary>
        private void CancelPendingListeningStarts()
        {
            try { _autoStartCts?.Cancel(); } catch { }
            try { _resumeListeningCts?.Cancel(); } catch { }
            try { _sessionStartCts?.Cancel(); } catch { }
            Interlocked.Increment(ref _startListeningEpoch);
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
                ResetPitchCapture();
                await _audio.EnsurePermissionAsync();
                if (!_audio.TryStartCapture(OnAudioBlock, out var restartErr))
                {
                    DebugLog.WriteLine($"[Restart] capture failed: {restartErr}");
                    throw new InvalidOperationException(restartErr ?? "capture failed");
                }
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

                // Snapshot: RecordAttemptOutcome can mutate the live list while we save.
                var outcomes = _session.GetSessionAttemptOutcomes().ToList();
                foreach (var outcome in outcomes)
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

            if (PlayModePickerOptions.IsTunerMode(_session))
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
            await EnsureMainThreadAsync();

            // Duplicate AutoStart / OnNavigatedTo must not cancel an in-flight count-in.
            if (_isRunning
                && !playBack
                && !forceNewNotes
                && string.Equals(scaleKeyTrigger, "AutoStart", StringComparison.Ordinal))
            {
                DebugLog.WriteLine("[Start] Skip duplicate AutoStart — already listening/count-in");
                return;
            }

            if (_session.Tune == "Tuner" && _isTunerPitchPlaying)
            {
                await StopTunerPitchAsync();
                await EnsureMainThreadAsync();
            }

            int epoch = Interlocked.Increment(ref _startListeningEpoch);
            _sessionStartCts = PracticeSessionLifecycle.ReplaceSessionStartCancellation(_sessionStartCts);
            var ct = _sessionStartCts.Token;
            bool launchedPlayback = false;
            _userStoppedListening = false;
            _listeningPausedForPageHide = false;

            try
            {
                int displayedCount = _session.NotesToDraw?.Count ?? 0;
                if (PracticeSessionLifecycle.ShouldAbortPlaybackBecauseEmpty(playBack, displayedCount))
                {
                    DebugLog.WriteLine("[Start] Play aborted: no displayed notes (will not generate)");
                    _isPlaying = false;
                    SetPlayButtonPlaying(false);
                    SetButtonStates(false);
                    StatusService.Instance.StatusMessage = "No notes to play — try again.";
                    FinishPlaybackInstrumentRestore();
                    return;
                }

                bool reuseDisplayed = PracticeSessionLifecycle.ShouldReuseDisplayedExercise(
                    playBack, forceNewNotes, displayedCount);

                DebugLog.WriteLine($"[Start] Starting listening, playBack={playBack}, forceNewNotes={forceNewNotes}, reuseDisplayed={reuseDisplayed}");
                SetButtonStates(true, keepPlayEnabled: playBack);
                // SPECIAL DEBUG FOR ANDROID LOG IN RELEASE MODE
                FirstNoteAndroidReleaseLog.ResetForNewSession();
                FirstNoteAndroidReleaseLog.WriteAlways(
                    "session-armed",
                    $"playBack={playBack} forceNew={forceNewNotes} reuse={reuseDisplayed} " +
                    $"tune={_session.Tune} notes={_session.NotesToDraw?.Count ?? 0} tempo={_session.Tempo}");

                if (!reuseDisplayed)
                {
                    // Any new session clears the post-autoplay results freeze.
                    _freezeStaff = false;
                    ClearSessionEndMarquee();

                    // Assign a fresh session ID so all NoteAttempts from this run are grouped together.
                    _currentSessionId = PracticeSessionLifecycle.NewSessionId();

                    using (PracticeSessionStartProfiler.Scope("SessionReset"))
                    {
                        // Stop the old capture before Reset so OnAudioBlock cannot race
                        // against an empty/rebuilding NotesToDraw (missed first-note greens).
                        try { _audio.StopCapture(); } catch { }
                        StopWaitingCountIn();

                        _lastProcess = DateTime.MinValue;
                        _dismissedResultBannerForFirstSound = false;
                        ResetPitchCapture();
                        _session.Reset();
                    }

                    ct.ThrowIfCancellationRequested();

                    // Tuner uses the same mic/pitch pipeline as scales, but must not run
                    // exercise preparation (RegenerateNotesAsync / Assortment by Level / composition).
                    if (PlayModePickerOptions.IsTunerMode(_session) && !playBack)
                    {
                        await StartTunerMicrophoneCaptureAsync(ct);
                        return;
                    }

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
                            await EnsureMainThreadAsync();

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
                }
                else
                {
                    // Play the staff as-is. Do not Reset, regenerate, or re-roll What to Play.
                    _freezeStaff = false;
                    try { _audio.StopCapture(); } catch { }
                    StopWaitingCountIn();
                    DebugLog.WriteLine(
                        "[Start] Play reusing displayed exercise "
                        + PracticeSessionLifecycle.DisplayedExerciseFingerprint(
                            _session.NotesToDraw ?? Enumerable.Empty<NoteInfo>()));
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
                    await EnsureMainThreadAsync();
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
                    NoteStateChangeDiagnostics.ClearLog();
                    Utils.Log($"[NoteStateDiag] Diagnostic log: {NoteStateChangeDiagnostics.LogFilePath}");
                    DebugLog.WriteLine(
                        $"[NoteStateDiag] Diagnostic log file: {NoteStateChangeDiagnostics.LogFilePath}");
                    _session?.MarkPlaybackArmUtc();
                    // Count-in pauses capture around each click; start capture only when
                    // not using count-in, otherwise the first beforeClick will stop it.
                    bool startCountIn = WaitingCountInSettings.Enabled
                        && (_session?.NotesToDraw?.Count ?? 0) > 0;
                    if (startCountIn)
                    {
                        if (epoch != Volatile.Read(ref _startListeningEpoch)
                            || !_isPageVisible
                            || PlayModePickerOptions.IsTunerMode(_session))
                        {
                            DebugLog.WriteLine("[Start] Count-in aborted — superseded, obscured, or Tuner");
                            return;
                        }

                        // Always cancel any prior Count-In before arming a fresh one
                        // (including reuseDisplayed restarts that skip SessionReset).
                        StopWaitingCountIn();

                        // Arm generation before fire-and-forget so Stop cannot be raced by a late start.
                        int countInGen = WaitingCountInArming.Arm(ref _waitingCountInGeneration);
                        _waitingCountInActive = true;
                        StatusService.Instance.ShowTemporaryMessage(
                            StatusService.CountInStatusMessage,
                            StatusService.CountInStatusDuration);
                        DebugLog.WriteLine("[Start] Count-in enabled — starting click loop");
                        // Own CTS so session-start replacement cannot kill the loop mid-measure.
                        _ = StartWaitingCountInAsync(countInGen);
                    }
                    else
                    {
                        _waitingCountInActive = false;
                        if (!_audio.TryStartCapture(OnAudioBlock, out var capErr))
                        {
                            DebugLog.WriteLine($"[Start] capture failed: {capErr}");
                            // SPECIAL DEBUG FOR ANDROID LOG IN RELEASE MODE
                            FirstNoteAndroidReleaseLog.WriteAlways(
                                "session-capture",
                                $"failed err={capErr}");
                            StatusService.Instance.StatusMessage =
                                "Microphone unavailable — tap ● to retry.";
                            SetButtonStates(false);
                            return;
                        }
                        // Conductor clock starts on first correct note (see OnAudioBlock / TryArmListeningClockOnFirstCorrectPitch).
                    }
                    DebugLog.WriteLine("[Start] Audio capture / count-in armed");
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
                        FinishPlaybackInstrumentRestore();
                        return;
                    }

                    _playCts?.Cancel();
                    _playCts = new CancellationTokenSource();
                    _ = PlayDisplayedAsync(_playCts.Token);
                    launchedPlayback = true;
                }
            }
            catch (OperationCanceledException)
            {
                DebugLog.WriteLine("[Start] Cancelled");
                if (playBack && !launchedPlayback)
                    FinishPlaybackInstrumentRestore();
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[Start] ERROR: {ex}");
                _isPlaying = false;
                SetPlayButtonPlaying(false);
                SetButtonStates(false);
                if (playBack && !launchedPlayback)
                    FinishPlaybackInstrumentRestore();
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

        /// <summary>
        /// Tuner mic start: same permission + StartCapture(OnAudioBlock) path as scales,
        /// without regenerating exercise notes or requiring an active staff target.
        /// </summary>
        private async Task StartTunerMicrophoneCaptureAsync(CancellationToken ct)
        {
            DebugLog.WriteLine("[Tuner] activated — starting microphone (shared scale pipeline)");

            if (!_isRunning)
            {
                DebugLog.WriteLine("[Tuner] Aborted before permission: no longer running");
                return;
            }

            using (PracticeSessionStartProfiler.Scope("AudioPermission"))
            {
                await _audio.EnsurePermissionAsync();
            }
            DebugLog.WriteLine("[Tuner] microphone permission requested/checked");

            ct.ThrowIfCancellationRequested();
            if (!_isRunning)
            {
                DebugLog.WriteLine("[Tuner] Aborted after permission: no longer running");
                return;
            }

            try { _audio.StopCapture(); } catch { }
            ResetPitchCapture();
            _lastProcess = DateTime.MinValue;

            DebugLog.WriteLine("[Tuner] microphone start requested");
            if (!_audio.TryStartCapture(OnAudioBlock, out var tunerCapErr))
            {
                DebugLog.WriteLine($"[Tuner] capture failed: {tunerCapErr}");
                StatusService.Instance.StatusMessage = "Microphone unavailable — tap Listen to retry.";
                SetButtonStates(false);
                return;
            }
            _session?.StartListeningClock();
            StatusService.Instance.StatusMessage = "Listening…";
            UpdateTunerModeChrome();
            RefreshTunerPickerDisplayLabel();
            DebugLog.WriteLine("[Tuner] microphone capture running");
        }
        /// <summary>Full rhythmic sequence (notes and rests) for autoplay.</summary>
        private List<GeneratedNote>? TryGetAutoplayRhythmSequence()
        {
            if (_staffDrawable != null)
                return DisplayedPlaybackSync.BuildDisplayedRhythmSequence(
                    _staffDrawable.UpperNotes, _staffDrawable.LowerNotes);
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

                    var bpm = Math.Clamp(_session.Tempo, NoteSessionService.MinTempo, NoteSessionService.MaxTempo);
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
#if DEBUG
                                if (ev.MidiNumber is ChromaticMidi61Diagnostics.MidiC4
                                    or ChromaticMidi61Diagnostics.MidiCs4
                                    or ChromaticMidi61Diagnostics.MidiD4)
                                {
                                    ChromaticMidi61Diagnostics.RecordPlayback(ev.MidiNumber, ev.SpelledName);
                                    RefreshMidi61DiagnosticLabel();
                                }
#endif
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

                var bpmLegacy = Math.Clamp(_session.Tempo, NoteSessionService.MinTempo, NoteSessionService.MaxTempo);
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
#if DEBUG
                        if (note.Midi is ChromaticMidi61Diagnostics.MidiC4
                            or ChromaticMidi61Diagnostics.MidiCs4
                            or ChromaticMidi61Diagnostics.MidiD4)
                        {
                            ChromaticMidi61Diagnostics.RecordPlayback(note.Midi, note.Name);
                            RefreshMidi61DiagnosticLabel();
                        }
#endif
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

                        var tempo = (double)_session.Tempo;
                        StatusService.Instance.StatusMessage =
                            $"Playback {tempo:F0} BPM. Tap GO to listen or Play to hear again.";
                    }

                    // Restore instrument after freeze — suppress regenerate so Play
                    // cannot replace the displayed exercise.
                    FinishPlaybackInstrumentRestore();

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
        /// Returns the save outcome (including new child level when a level-up occurred).
        /// </summary>        
        private async Task<PracticeSessionPersistence.SaveOutcome> SaveSessionStatAsync()
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
            return outcome;
        }
        private async void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            await EnsureMainThreadAsync();

            if (e.PropertyName == nameof(NoteSessionService.IsRandomMode)
                || e.PropertyName == nameof(NoteSessionService.EffectiveScale)
                || e.PropertyName == nameof(NoteSessionService.EffectiveScaleDisplay)
                || e.PropertyName == nameof(NoteSessionService.GenerationScale)
                || e.PropertyName == nameof(NoteSessionService.ScaleSelectionMode))
            {
                UpdateEffectiveScaleLabel();
            }

            if (e.PropertyName == nameof(NoteSessionService.IsMakeItEasyActive))
                RefreshMakeItEasyButtonAppearance();

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
                    // Post-Play freeze keeps green feedback; a Key change must redraw for the new tonic.
                    if (e.PropertyName == nameof(NoteSessionService.Key))
                        _freezeStaff = false;
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
                        UpdatePracticePlayItemLabel();
                }
                UpdateKeyPickerVisibility();
            }

            if (e.PropertyName == nameof(NoteSessionService.Instrument))
            {
                UpdateInstrumentPickerSelection();
                if (_session.Tune == "Tuner")
                    RebuildReferenceNotesForInstrumentChange();
            }

            if (e.PropertyName == nameof(NoteSessionService.Key))
            {
                UpdateKeyPickerSelection();
                if (_session.Tune == "Tuner")
                {
                    BuildReferenceNoteChoices();
                    SelectReferenceNote(_referenceWrittenMidi, stopTone: false);
                }
            }

            if (e.PropertyName == nameof(NoteSessionService.Key)
                || e.PropertyName == nameof(NoteSessionService.Instrument))
            {
                if (_session.Tune == "Arpeggio" && !_applyingArpeggioSelection)
                    _session.SyncArpeggioRootToCurrentKey();
            }
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

            if (e.PropertyName == nameof(NoteSessionService.TemporaryEmphasizedWrittenNote)
                || e.PropertyName == nameof(NoteSessionService.HasTemporaryNoteEmphasis))
            {
                UpdateNoteEmphasisBanner();
            }

            if (e.PropertyName == nameof(NoteSessionService.Tempo)
                || e.PropertyName == nameof(NoteSessionService.MusicBpm)
                || e.PropertyName == nameof(NoteSessionService.PlaybackBpm))
            {
                RefreshTempoControlDisplay();
                _staffDrawable?.InvalidateLayoutCache();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    StaffGraphicsView?.Invalidate();
                    ScheduleSyncTempoMarkingHitTarget();
                });
            }

            if (e.PropertyName == nameof(NoteSessionService.MeterTimeSignature))
            {
                RefreshTimeSignatureControlDisplay();
                _staffDrawable?.InvalidateLayoutCache();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    StaffGraphicsView?.Invalidate();
                    ScheduleSyncTimeSignatureHitTarget();
                    ScheduleSyncTempoMarkingHitTarget();
                });
            }

            if (e.PropertyName == nameof(NoteSessionService.ChildLevel))
            {
                _repeatSameSnapshot = null;
                UpdateChildLevelSliderDisplay();
            }
        }
        private void UpdateInstrumentPickerSelection()
        {
            string display = _session.InstrumentDisplayName;
            int idx = InstrumentCatalog.IndexOfOption(_session.Instrument);
            if (idx < 0)
                return;

            EnterPickerSyncSuppress();
            try
            {
                if (InstrumentPicker?.ItemsSource != null && InstrumentPicker.SelectedIndex != idx)
                    InstrumentPicker.SelectedIndex = idx;
                if (PracticeInstrumentPicker?.ItemsSource != null
                    && PracticeInstrumentPicker.SelectedIndex != idx)
                    PracticeInstrumentPicker.SelectedIndex = idx;
                if (TunerInstrumentPicker?.ItemsSource != null
                    && TunerInstrumentPicker.SelectedIndex != idx)
                    TunerInstrumentPicker.SelectedIndex = idx;
            }
            finally
            {
                ExitPickerSyncSuppress();
            }

            SelectedInstrumentShort = display;
            if (_session.Tune == "Tuner")
                SizeTunerCompactPickers();
        }

        private static int ResolveInstrumentOptionIndex(string displayName)
            => InstrumentCatalog.IndexOfOption(displayName);
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
            if (!MainThread.IsMainThread)
            {
                RunOnMainThread(UpdateKeyPickerSelection);
                return;
            }

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
            if (!MainThread.IsMainThread)
            {
                RunOnMainThread(UpdateConcertKeyLabel);
                return;
            }

            var text = $"(Concert {_session.GetConcertKey()})";
            ConcertKeyLabel.Text = text;
            if (PracticeConcertKeyLabel != null) PracticeConcertKeyLabel.Text = text;
        }
        private void InitializePracticePickers(string[] instrumentOptions)
        {
            if (PracticeInstrumentPicker == null || PracticeKeyPicker == null)
                return;

            if (PracticeInstrumentPicker.ItemsSource == null)
            {
                PracticeInstrumentPicker.ItemsSource = instrumentOptions;
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

            if (TunerInstrumentPicker != null)
            {
                EnterPickerSyncSuppress();
                try
                {
                    TunerInstrumentPicker.ItemsSource = TunerInstrumentPickerItem.BuildAll().ToList();
                    var tunerIdx = ResolveInstrumentOptionIndex(_session.Instrument);
                    if (tunerIdx < 0)
                        tunerIdx = InstrumentPicker?.SelectedIndex ?? 0;
                    if (tunerIdx >= 0)
                        TunerInstrumentPicker.SelectedIndex = tunerIdx;
                }
                finally
                {
                    ExitPickerSyncSuppress();
                }
                SizeTunerCompactPickers();
            }

            UpdatePracticePlayItemLabel();
        }
        private void EnsurePracticePickersReady()
        {
            if (PracticeInstrumentPicker == null || PracticeKeyPicker == null)
                return;

            if (PracticeInstrumentPicker.ItemsSource == null && InstrumentPicker?.ItemsSource is string[] instrumentOptions)
                InitializePracticePickers(instrumentOptions);
            else
                UpdatePracticePlayItemLabel();

            UpdateInstrumentPickerSelection();
            UpdateKeyPickerSelection();
            UpdateKeyPickerVisibility();
            UpdateConcertKeyLabel();
        }
        private void UpdateKeyPickerVisibility()
        {
            if (!MainThread.IsMainThread)
            {
                RunOnMainThread(UpdateKeyPickerVisibility);
                return;
            }

            var show = _session.Tune != "Tuner";
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
        // ── Tuner reference-tone helpers ───────────────────────────────────────

        private bool PreferFlatsForReferenceSpelling()
            => KeySignatureRules.IsFlatKeyName(_session.Key);

        private void EnsureReferenceNoteUi()
        {
            if (_session.Tune != "Tuner")
                return;

            EnsureTunerTempoPicker();
            WireTunerPickerTextFit();
            BuildReferenceNoteChoices();

            // Restore order: in-memory → persisted written MIDI → default middle.
            // Never auto-play on restore.
            int initial = _referenceWrittenMidi;
            if (initial <= 0)
                initial = LoadPersistedReferenceWrittenMidi();
            if (initial <= 0)
            {
                var midis = _referenceNoteChoices.Select(c => c.WrittenMidi).ToList();
                initial = TunerReferenceNoteCatalog.DefaultMiddleMidi(midis);
            }

            SelectReferenceNote(initial, stopTone: false);
        }

        private static int LoadPersistedReferenceWrittenMidi()
        {
            int saved = Preferences.Default.Get(TunerReferenceNoteCatalog.PreferenceKeyWrittenMidi, 0);
            return saved > 0 ? saved : 0;
        }

        private static void PersistReferenceWrittenMidi(int writtenMidi)
        {
            if (writtenMidi <= 0)
                return;
            Preferences.Default.Set(TunerReferenceNoteCatalog.PreferenceKeyWrittenMidi, writtenMidi);
        }

        private static int LoadPersistedTunerTempo()
        {
            int saved = Preferences.Default.Get(PrefTunerTempoKey, 0);
            if (saved >= NoteSessionService.MinTempo && saved <= NoteSessionService.MaxTempo)
                return saved;
            return 0;
        }

        private static void PersistTunerTempo(int bpm)
        {
            Preferences.Default.Set(
                PrefTunerTempoKey,
                Math.Clamp(bpm, NoteSessionService.MinTempo, NoteSessionService.MaxTempo));
        }

        private void EnsureTunerTempoPicker()
        {
            if (TunerTempoPicker == null)
                return;

            if (TunerTempoPicker.Items.Count == 0)
            {
                for (int bpm = NoteSessionService.MinTempo; bpm <= NoteSessionService.MaxTempo; bpm++)
                    TunerTempoPicker.Items.Add(bpm.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            int tempo = _tunerTempoBpm;
            if (tempo < NoteSessionService.MinTempo || tempo > NoteSessionService.MaxTempo)
            {
                int persisted = LoadPersistedTunerTempo();
                tempo = persisted > 0
                    ? persisted
                    : Math.Clamp(_session.Tempo, NoteSessionService.MinTempo, NoteSessionService.MaxTempo);
            }

            SetTunerTempo(tempo, persist: true, updatePicker: true);
        }

        private void SetTunerTempo(int bpm, bool persist, bool updatePicker)
        {
            int clamped = Math.Clamp(bpm, NoteSessionService.MinTempo, NoteSessionService.MaxTempo);
            _tunerTempoBpm = clamped;
            if (persist)
                PersistTunerTempo(clamped);

            if (!updatePicker || TunerTempoPicker == null)
                return;

            int index = clamped - NoteSessionService.MinTempo;
            if (index < 0 || index >= TunerTempoPicker.Items.Count)
                return;

            if (TunerTempoPicker.SelectedIndex == index)
                return;

            _isUpdatingTunerTempoPicker = true;
            try
            {
                TunerTempoPicker.SelectedIndex = index;
            }
            finally
            {
                _isUpdatingTunerTempoPicker = false;
            }
        }

        private void OnTunerTempoPickerChanged(object? sender, EventArgs e)
        {
            if (_isUpdatingTunerTempoPicker || TunerTempoPicker == null)
                return;
            if (TunerTempoPicker.SelectedIndex < 0)
                return;

            int bpm = NoteSessionService.MinTempo + TunerTempoPicker.SelectedIndex;
            SetTunerTempo(bpm, persist: true, updatePicker: false);
            DebugLog.WriteLine($"[ReferenceTone] tuner tempo → {bpm} BPM (loop reads next beat)");
        }

        private void OnTunerUseSettingsTempoClicked(object? sender, EventArgs e)
        {
            int settingsTempo = Math.Clamp(
                _session.Tempo,
                NoteSessionService.MinTempo,
                NoteSessionService.MaxTempo);
            SetTunerTempo(settingsTempo, persist: true, updatePicker: true);
            DebugLog.WriteLine($"[ReferenceTone] Use Settings Tempo → {settingsTempo} BPM");
        }

        private void RebuildReferenceNotesForInstrumentChange()
        {
            bool pitchWasPlaying = _isTunerPitchPlaying;
            _ = StopReferenceToneAsync(resumeListening: false);
            BuildReferenceNoteChoices();
            // Keep the same written MIDI when still valid; ClampToRange picks nearest otherwise.
            int keep = _referenceWrittenMidi > 0
                ? _referenceWrittenMidi
                : LoadPersistedReferenceWrittenMidi();
            SelectReferenceNote(keep, stopTone: false);
            UpdateTunerStaffDisplay();
            if (pitchWasPlaying)
                _ = PlayTunerPitchAsync(restartIfPlaying: true);
        }

        private void BuildReferenceNoteChoices()
        {
            var profile = TunerReferenceNoteCatalog.GetEffectiveInstrumentProfile(_session.Instrument);
            _referenceNoteChoices = TunerReferenceNoteCatalog.BuildChoices(
                profile,
                PreferFlatsForReferenceSpelling());

            if (TunerNotePicker == null)
                return;

            var labels = _referenceNoteChoices
                .Select(c => TunerReferenceNoteCatalog.FormatCompactWrittenLabel(c.WrittenMidi))
                .ToList();
            bool owned = !_isUpdatingReferenceNoteUi;
            if (owned)
                _isUpdatingReferenceNoteUi = true;
            try
            {
                TunerNotePicker.ItemsSource = labels;
            }
            finally
            {
                if (owned)
                    _isUpdatingReferenceNoteUi = false;
            }

            SizeTunerCompactPickers();
        }

        // Note-list UI removed from the Tuner panel (kept as no-ops for any residual calls).
        private void InvalidateTunerNoteListButtons() { }
        private void RebuildTunerNoteListButtons() { }
        private void HighlightSelectedTunerNoteButton() { }
        private void SetTunerNoteListVisible(bool visible) { }
        private void OnTunerNoteChooserTapped(object? sender, TappedEventArgs e) { }
        private async Task ScrollSelectedTunerNoteIntoViewAsync() => await Task.CompletedTask;

        private int IndexOfReferenceMidi(int writtenMidi)
        {
            for (int i = 0; i < _referenceNoteChoices.Count; i++)
            {
                if (_referenceNoteChoices[i].WrittenMidi == writtenMidi)
                    return i;
            }
            return -1;
        }

        /// <param name="resumeListeningAfterStop">
        /// When stopping a playing tone (e.g. instrument change), resume the mic.
        /// Note navigation (◀/▶/picker) keeps an active loop running so the next beat
        /// uses the newly selected note without starting a second loop.
        /// </param>
        private void SelectReferenceNote(
            int writtenMidi,
            bool stopTone = true,
            bool resumeListeningAfterStop = true)
        {
            if (_isUpdatingReferenceNoteUi)
                return;

            if (stopTone && _isReferenceTonePlaying)
            {
                try
                {
                    _referenceToneGeneration++; // invalidate in-flight PlayReferenceToneAsync finally
                    _referenceToneCts?.Cancel();
                    try { _waitingCountInPlayer?.Stop(); } catch { }
                    try { _player.CancelPlayback(); } catch { }
                }
                catch { /* best-effort */ }
                _isReferenceTonePlaying = false;
                UpdateReferencePlayButtonUi();
                UpdateTunerModeChrome();
                if (resumeListeningAfterStop)
                    _ = ResumeTunerListeningAfterReferenceToneAsync();
            }

            var midis = _referenceNoteChoices.Select(c => c.WrittenMidi).ToList();
            if (midis.Count == 0)
            {
                _referenceWrittenMidi = 0;
                SyncReferenceNoteChrome();
                return;
            }

            _isUpdatingReferenceNoteUi = true;
            try
            {
                // Clamp is order-independent; choices are high→low for the picker UI.
                _referenceWrittenMidi = TunerReferenceNoteCatalog.ClampToRange(writtenMidi, midis);
                PersistReferenceWrittenMidi(_referenceWrittenMidi);
                // Selection is intentional — don't keep showing a prior heard pitch on the staff
                // until the next detection; the note-to-play chip always follows this midi.
                if (!_isReferenceTonePlaying && !_isTunerPitchPlaying)
                    _session.ClearTunerDetection();
                _lastLoggedTunerHeardNote = null;
                SyncReferenceNoteChrome();
                UpdateTunerStaffDisplay();

                int offset = TunerReferenceNoteCatalog.GetEffectiveTransposeOffset(_session.Instrument);
                int concertMidi = TunerReferenceNoteCatalog.ToConcertMidi(_referenceWrittenMidi, offset);
                double hz = NoteSessionService.MidiToFreqPublic(concertMidi);
                DebugLog.WriteLine(
                    $"[ReferenceTone] select picker='{TunerReferenceNoteCatalog.FormatPickerLabel(_referenceWrittenMidi)}' " +
                    $"writtenMidi={_referenceWrittenMidi} transpose={offset} " +
                    $"concertMidi={concertMidi} hz={hz:F2}");
            }
            finally
            {
                _isUpdatingReferenceNoteUi = false;
            }
        }

        private void SyncReferenceNoteChrome()
        {
            RefreshTunerPickerDisplayLabel();
            SyncTunerNotePickerSelection();
            SizeTunerCompactPickers();
            UpdateTunerModeChrome();
        }

        private void SizeTunerCompactPickers()
        {
            string inst = TunerReferenceNoteCatalog.GetTunerInstrumentPickerDisplayName(_session?.Instrument);
            string note = _referenceWrittenMidi > 0
                ? TunerReferenceNoteCatalog.FormatCompactWrittenLabel(_referenceWrittenMidi)
                : "C4";

            double rowWidth = TunerPickerRow?.Width ?? TunerReferenceControls?.Width ?? 0;
            if (rowWidth <= 1 && TunerInfoBorder?.Width > 1)
                rowWidth = TunerInfoBorder.Width - 12;

            double instTextWidth = MeasureTunerPickerTextWidth(inst, TunerPickerLayout.BaseFontSize);
            double noteTextWidth = MeasureTunerPickerTextWidth(note, TunerPickerLayout.BaseFontSize);
            var layout = TunerPickerLayout.Allocate(rowWidth, instTextWidth, noteTextWidth);

            ApplyTunerPickerChrome(TunerInstrumentPickerChrome, TunerInstrumentPicker, layout.InstrumentChromeMin, layout.FontSize);
            ApplyTunerPickerChrome(TunerNotePickerChrome, TunerNotePicker, layout.NoteChromeMin, layout.FontSize);
        }

        private static double MeasureTunerPickerTextWidth(string text, float fontSize)
        {
            using var font = new SKFont();
            ConfigureTitleUiBoldFont(font, fontSize);
            font.MeasureText(string.IsNullOrEmpty(text) ? "M" : text, out var bounds);
            return Math.Max(0, bounds.Width) + TunerPickerLayout.TextRenderSlack;
        }

        private static void ApplyTunerPickerChrome(Border? chrome, Picker? picker, double minimumWidth, float fontSize)
        {
            if (chrome == null)
                return;

            chrome.MinimumWidthRequest = minimumWidth;
            chrome.WidthRequest = -1;
            chrome.HorizontalOptions = LayoutOptions.Fill;
            if (picker != null)
                picker.FontSize = fontSize;
        }

        private bool _tunerPickerHandlersWired;

        private void WireTunerPickerTextFit()
        {
            if (_tunerPickerHandlersWired)
                return;
            _tunerPickerHandlersWired = true;
            AttachTunerPickerTextFit(TunerInstrumentPicker);
            AttachTunerPickerTextFit(TunerNotePicker);
            AttachTunerPickerTextFit(TunerTempoPicker);
            if (TunerPickerRow != null)
                TunerPickerRow.SizeChanged += (_, _) => SizeTunerCompactPickers();
        }

        private static void AttachTunerPickerTextFit(Picker? picker)
        {
            if (picker == null)
                return;
            picker.HandlerChanged += (_, _) => ApplyTunerPickerPlatformTextFit(picker);
            picker.SizeChanged += (_, _) => ApplyTunerPickerPlatformTextFit(picker);
            ApplyTunerPickerPlatformTextFit(picker);
        }

        private static void ApplyTunerPickerPlatformTextFit(Picker picker)
        {
#if ANDROID
            if (picker.Handler?.PlatformView is not Android.Widget.AbsSpinner spinner)
                return;

            spinner.SetMinimumHeight(0);
            spinner.SetClipToPadding(false);
            spinner.SetClipChildren(false);
            // Border chrome already applies horizontal inset — avoid double padding that clips text.
            spinner.SetPadding(0, 0, 0, 0);

            if (spinner.GetChildAt(0) is Android.Widget.TextView selected)
            {
                selected.SetIncludeFontPadding(false);
                selected.SetPadding(0, 0, 0, 0);
                selected.Gravity = Android.Views.GravityFlags.CenterVertical | Android.Views.GravityFlags.Start;
                selected.Ellipsize = null;
                selected.SetSingleLine(true);
            }
#endif
        }

        private void SyncTunerNotePickerSelection()
        {
            if (TunerNotePicker == null || _referenceNoteChoices.Count == 0)
                return;

            int idx = IndexOfReferenceMidi(_referenceWrittenMidi);
            if (idx < 0 || TunerNotePicker.SelectedIndex == idx)
                return;

            bool owned = !_isUpdatingReferenceNoteUi;
            if (owned)
                _isUpdatingReferenceNoteUi = true;
            try
            {
                TunerNotePicker.SelectedIndex = idx;
            }
            finally
            {
                if (owned)
                    _isUpdatingReferenceNoteUi = false;
            }
        }

        private void OnTunerNotePickerChanged(object? sender, EventArgs e)
        {
            if (_isUpdatingReferenceNoteUi || TunerNotePicker == null)
                return;
            int idx = TunerNotePicker.SelectedIndex;
            if (idx < 0 || idx >= _referenceNoteChoices.Count)
                return;

            var choice = _referenceNoteChoices[idx];
            _ = OnTunerNotePickedAsync(choice.WrittenMidi, choice.PickerLabel);
        }

        private async Task OnTunerNotePickedAsync(int writtenMidi, string labelText)
        {
            if (writtenMidi <= 0)
                return;

            DebugLog.WriteLine($"[ReferenceTone] Note row tapped midi={writtenMidi} text='{labelText}'");

            bool pitchWasPlaying = _isTunerPitchPlaying;
            // Keep the Tuner metronome loop running independently of the reference pitch.
            SelectReferenceNote(writtenMidi, stopTone: false);

            if (pitchWasPlaying)
                await PlayTunerPitchAsync(restartIfPlaying: true);
        }

        /// <summary>
        /// Updates the Tuner "Note Heard" label with the written note for the selected instrument.
        /// </summary>
        private void RefreshTunerPickerDisplayLabel()
        {
            if (TunerNoteHeardValueLabel == null || _session.Tune != "Tuner")
                return;

            if (!string.IsNullOrWhiteSpace(_session.TunerLastNoteName))
            {
                int heardMidi = NoteSessionService.NoteNameToMidi(_session.TunerLastNoteName!);
                TunerNoteHeardValueLabel.Text = heardMidi > 0
                    ? TunerReferenceNoteCatalog.FormatHeardDisplayLabel(
                        heardMidi, _session.TunerLastCents)
                    : _session.TunerLastNoteName!;
            }
            else
            {
                TunerNoteHeardValueLabel.Text = "—";
            }
        }

        private void UpdateTunerModeChrome()
        {
            if (TitlePageModeLabel != null)
                TitlePageModeLabel.Text = _session.Tune == "Tuner" ? "  Tuner " : "  Music ";

            if (TunerModeStatusLabel != null)
            {
                // Big "Heard"/"Playing" row only while the mic is listening.
                //  //  2026.08.04 1804  TunerModeStatusLabel.IsVisible = _isRunning;
                //TunerModeStatusLabel.Text = _isReferenceTonePlaying ? "Playing" : "Heard";
                TunerModeStatusLabel.Text = "Metronome";
            }

            UpdateTitleStartStopButtonVisual(_isRunning);
            // Force Play visibility refresh so Tuner never re-shows the obsolete Play control.
            UpdatePlayButtonVisibility();
        }

        private void MoveReferenceNote(int semitoneDelta)
        {
            if (_referenceNoteChoices.Count == 0 || semitoneDelta == 0)
                return;

            int idx = IndexOfReferenceMidi(_referenceWrittenMidi);
            if (idx < 0)
                return;

            int next = Math.Clamp(idx + semitoneDelta, 0, _referenceNoteChoices.Count - 1);
            if (next == idx)
                return;

            // Do not stop a running loop — the next repetition uses the new note.
            SelectReferenceNote(_referenceNoteChoices[next].WrittenMidi, stopTone: false);
        }

        private void OnTunerLowerNoteClicked(object? sender, EventArgs e)
            => MoveReferenceNote(+1); // list is high→low

        private void OnTunerHigherNoteClicked(object? sender, EventArgs e)
            => MoveReferenceNote(-1);

        private async void OnTunerPlayNoteClicked(object? sender, EventArgs e)
            => await ToggleReferenceToneAsync();

        private async void OnTunerStaffTapped(object? sender, TappedEventArgs e)
            => await ToggleReferenceToneAsync();

        private async Task ToggleTunerPitchAsync()
        {
            if (_session.Tune != "Tuner")
                return;

            if (_isTunerPitchPlaying)
            {
                await StopTunerPitchAsync();
                return;
            }

            await PlayTunerPitchAsync(restartIfPlaying: false);
        }

        /// <summary>
        /// Sustained concert-pitch reference tone for the selected written note.
        /// Independent of GO / microphone listening and of the Tuner metronome.
        /// </summary>
        private async Task PlayTunerPitchAsync(bool restartIfPlaying)
        {
            if (_session.Tune != "Tuner" || _referenceWrittenMidi <= 0)
                return;
            if (_isTunerPitchPlaying && !restartIfPlaying)
                return;

            int generation = ++_tunerPitchGeneration;
            _tunerPitchCts?.Cancel();
            try { _tunerPitchCts?.Dispose(); } catch { }
            _tunerPitchCts = new CancellationTokenSource();
            var ct = _tunerPitchCts.Token;

            try { _player.CancelPlayback(); } catch { }

            _isTunerPitchPlaying = true;
            if (_isRunning)
            {
                try { _audio.StopCapture(); } catch { }
                _session.ClearTunerDetection();
                _lastLoggedTunerHeardNote = null;
                _isRunning = false;
                UpdateTitleStartStopButtonVisual(false);
            }

            SetPlayButtonPlaying(true);
            UpdatePlayButtonVisibility();
            UpdateTunerStaffDisplay();
            StatusService.Instance.StatusMessage = "Playing reference tone.";

            try
            {
                double hz = TunerReferenceNoteCatalog.ConcertFrequencyHz(
                    _referenceWrittenMidi,
                    TunerReferenceNoteCatalog.GetEffectiveTransposeOffset(_session.Instrument));
                if (hz <= 0)
                    return;

                DebugLog.WriteLine(
                    $"[TunerPitch] play writtenMidi={_referenceWrittenMidi} hz={hz:F2} (no GO)");
                await _player.PlaySustainedAsync(hz, volume: 0.35f, ct);
            }
            catch (OperationCanceledException)
            {
                // Stopped by user, note change, GO, or navigation.
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[TunerPitch] Play error: {ex}");
            }
            finally
            {
                if (generation == _tunerPitchGeneration)
                {
                    _isTunerPitchPlaying = false;
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        SetPlayButtonPlaying(false);
                        UpdatePlayButtonVisibility();
                        UpdateTunerStaffDisplay();
                    });
                }
            }
        }

        private async Task StopTunerPitchAsync()
        {
            _tunerPitchGeneration++;
            try
            {
                _tunerPitchCts?.Cancel();
                try { _player.CancelPlayback(); } catch { }
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[TunerPitch] Stop error: {ex}");
            }

            _isTunerPitchPlaying = false;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                SetPlayButtonPlaying(false);
                UpdatePlayButtonVisibility();
                UpdateTunerModeChrome();
                UpdateTunerStaffDisplay();
            });
        }

        /// <summary>
        /// Stops Tuner capture without clearing listening intent (<see cref="_isRunning"/>).
        /// Shared by metronome pause and <see cref="StopTunerListeningAsync"/>.
        /// </summary>
        private void StopTunerMicrophoneCaptureCore(bool clearDetection)
        {
            try { _audio.StopCapture(); } catch { }
            ResetPitchCapture();
            _lastProcess = DateTime.MinValue;
            if (clearDetection)
            {
                _session.ClearTunerDetection();
                _lastLoggedTunerHeardNote = null;
            }
        }

        /// <summary>
        /// Halts Tuner pitch detection while the metronome runs; listening resumes on Stop.
        /// </summary>
        private void PauseTunerListeningForMetronome()
        {
            if (!_isRunning)
                return;

            DebugLog.WriteLine("[TunerMetronome] pausing microphone for metronome");
            StopTunerMicrophoneCaptureCore(clearDetection: true);
            UpdateTunerStaffDisplay();
            RefreshTunerPickerDisplayLabel();
        }

        private async Task ToggleReferenceToneAsync()
        {
            if (_session.Tune != "Tuner")
                return;

            if (_isReferenceTonePlaying)
            {
                await StopReferenceToneAsync(resumeListening: _isRunning);
                return;
            }

            await PlayReferenceToneAsync();
        }

        private async Task PlayReferenceToneAsync()
        {
            // Tuner metronome uses Waiting Count-In click sounds at the Tuner tempo.
            if (_isReferenceTonePlaying)
                return;

            if (_waitingCountInPlayer == null)
                return;

            int generation = ++_referenceToneGeneration;
            _referenceToneCts?.Cancel();
            try { _referenceToneCts?.Dispose(); } catch { }
            _referenceToneCts = new CancellationTokenSource();
            var ct = _referenceToneCts.Token;

            // Stop mic before any metronome clicks or UI that could show stale detections.
            PauseTunerListeningForMetronome();

            _isReferenceTonePlaying = true;
            UpdateReferencePlayButtonUi();
            UpdateTunerModeChrome();
            UpdateTunerStaffDisplay();
            RefreshTunerPickerDisplayLabel();

            try
            {
                int beats = WaitingCountInLogic.GetBeatsPerMeasure(_session.GetDisplayTimeSignature());
                int tempo = Math.Clamp(
                    _tunerTempoBpm,
                    NoteSessionService.MinTempo,
                    NoteSessionService.MaxTempo);

                DebugLog.WriteLine(
                    $"[TunerMetronome] start tempo={tempo} beats/measure={beats} " +
                    $"accentHz={WaitingCountInSettings.AccentedPitchHz:F0} " +
                    $"vol={WaitingCountInSettings.AccentedVolume:F2}/{WaitingCountInSettings.UnaccentedVolume:F2} " +
                    $"durPct={WaitingCountInSettings.BeatDurationPercent}");

                await _waitingCountInPlayer.RunAsync(
                    tempoBpm: tempo,
                    beatsPerMeasure: beats,
                    accentedVolume: WaitingCountInSettings.AccentedVolume,
                    unaccentedVolume: WaitingCountInSettings.UnaccentedVolume,
                    accentedPitchHz: WaitingCountInSettings.AccentedPitchHz,
                    unaccentedPitchHz: WaitingCountInSettings.UnaccentedPitchHz,
                    beatDurationPercent: WaitingCountInSettings.BeatDurationPercent,
                    externalCt: ct,
                    getTempoBpm: () => Math.Clamp(
                        _tunerTempoBpm,
                        NoteSessionService.MinTempo,
                        NoteSessionService.MaxTempo));
            }
            catch (OperationCanceledException)
            {
                // Stopped by user or navigation.
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[TunerMetronome] Play error: {ex}");
            }
            finally
            {
                try { _waitingCountInPlayer?.Stop(); } catch { }

                // A newer play/stop superseded this run — don't clobber its UI or mic state.
                if (generation == _referenceToneGeneration)
                {
                    _isReferenceTonePlaying = false;
                    UpdateReferencePlayButtonUi();
                    UpdateTunerModeChrome();
                    UpdateTunerStaffDisplay();
                    RefreshTunerPickerDisplayLabel();
                    if (_isPageVisible && _session.Tune == "Tuner")
                        await ResumeTunerListeningAfterReferenceToneAsync();
                }
            }
        }

        private async Task StopReferenceToneAsync(bool resumeListening)
        {
            try
            {
                _referenceToneGeneration++; // invalidate in-flight PlayReferenceToneAsync finally
                _referenceToneCts?.Cancel();
                try { _waitingCountInPlayer?.Stop(); } catch { }
                try { _player.CancelPlayback(); } catch { }
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[TunerMetronome] Stop error: {ex}");
            }

            bool wasPlaying = _isReferenceTonePlaying;
            _isReferenceTonePlaying = false;
            UpdateReferencePlayButtonUi();
            UpdateTunerModeChrome();
            if (wasPlaying)
            {
                UpdateTunerStaffDisplay();
                RefreshTunerPickerDisplayLabel();
            }
            if (resumeListening && _isPageVisible && _session.Tune == "Tuner")
                await ResumeTunerListeningAfterReferenceToneAsync();
        }

        private async Task ResumeTunerListeningAfterReferenceToneAsync()
        {
            if (_isReferenceTonePlaying || !_isPageVisible || _session.Tune != "Tuner")
            {
                DebugLog.WriteLine(
                    $"[Tuner] resume skipped playing={_isReferenceTonePlaying} " +
                    $"visible={_isPageVisible} tune={_session.Tune}");
                return;
            }

            // User tapped Stop — stay quiet so they can pick notes, see them on the staff, and play.
            if (!_isRunning)
            {
                DebugLog.WriteLine("[Tuner] resume skipped: not listening (_isRunning=false)");
                return;
            }

            try
            {
                DebugLog.WriteLine("[Tuner] resume microphone after reference tone");
                await _audio.EnsurePermissionAsync();
                StopTunerMicrophoneCaptureCore(clearDetection: false);
                if (!_audio.TryStartCapture(OnAudioBlock, out var resumeErr))
                {
                    DebugLog.WriteLine($"[Tuner] resume mic failed: {resumeErr}");
                    StatusService.Instance.StatusMessage = "Microphone unavailable — tap Listen to retry.";
                    return;
                }
                StatusService.Instance.StatusMessage = "Listening…";
                UpdateTunerModeChrome();
                RefreshTunerPickerDisplayLabel();
                DebugLog.WriteLine("[Tuner] microphone capture running (resumed)");
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[Tuner] Resume mic error: {ex}");
            }
        }

        private void UpdateReferencePlayButtonUi()
        {
            if (TunerPlayNoteButton == null)
                return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_isReferenceTonePlaying)
                {
                    TunerPlayNoteButton.Text = "Stop";
                    TunerPlayNoteButton.TextColor = Colors.Yellow;
                    TunerPlayNoteButton.BackgroundColor = Colors.Red;
                }
                else
                {
                    TunerPlayNoteButton.Text = "Start";
                    TunerPlayNoteButton.TextColor = Colors.White;
                    TunerPlayNoteButton.BackgroundColor = Color.FromArgb("#2E8B57");
                }
            });
        }

        private void UpdateTunerStaffDisplay()
        {
            if (_staffDrawable == null || _session.Tune != "Tuner")
                return;

            bool preferFlats = PreferFlatsForReferenceSpelling();

            // Listening + detection → show heard note (visual only; does not change selection).
            // Playing / idle → show selected reference note (_referenceWrittenMidi).
            string? spelling = null;
            bool showingHeard = false;
            if (!_isReferenceTonePlaying
                && !_isTunerPitchPlaying
                && _isRunning
                && !string.IsNullOrWhiteSpace(_session.TunerLastNoteName))
            {
                spelling = _session.TunerLastNoteName;
                showingHeard = true;
            }
            else if (_referenceWrittenMidi > 0)
            {
                int idx = IndexOfReferenceMidi(_referenceWrittenMidi);
                spelling = idx >= 0
                    ? _referenceNoteChoices[idx].StaffSpellingAscii
                    : NoteSessionService.MidiToNoteName(_referenceWrittenMidi, preferFlats);
            }

            var tunerNote = NoteSessionService.TryBuildGeneratedNoteFromSpelledName(spelling);
            var upper = tunerNote != null
                ? new List<GeneratedNote> { tunerNote }
                : new List<GeneratedNote>();

            _staffDrawable.LowerNotes = new List<GeneratedNote>();
            _staffDrawable.LowerBarBeats = new List<double>();
            _staffDrawable.LowerNoteStates = Array.Empty<StaffNoteState>();
            _staffDrawable.LowerAlpha = 0f;

            _staffDrawable.UpperNotes = upper;
            // No measure bar beats — Tuner staff must not draw an end bar line.
            _staffDrawable.UpperBarBeats = new List<double>();
            _staffDrawable.UpperNoteStates = new StaffNoteState[upper.Count];
            if (upper.Count > 0)
            {
                // Always green on Tuner (Correct color); Music/Sight pages keep their own states.
                _staffDrawable.UpperNoteStates[0] = StaffNoteState.Correct;
            }
            _staffDrawable.IsUpperActive = true;
            _staffDrawable.ActiveNoteIndex = upper.Count > 0 ? 0 : -1;
            _staffDrawable.UpperAlpha = 1f;
            _staffDrawable.UpperHasEndBar = false;
            _staffDrawable.InvalidateLayoutCache();

            // Do not clear session exercise state used by scales when leaving Tuner;
            // while in Tuner these lists only mirror the single displayed note for feedback chrome.
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
                // Cents feedback for heard notes (flat / sharp / in tune).
                int cents = showingHeard ? _session.TunerLastCents : 0;
                _session.FeedbackViewModels.Add(new FeedbackItem(0, 0, cents, false));
            }

            MainThread.BeginInvokeOnMainThread(() => TunerGraphicsView?.Invalidate());
        }
        /// <summary>
        /// Music-page Tuner entry (legacy pickers). Session state comes from the same
        /// <see cref="PlayModePickerOptions.ApplyOtherSelection"/> path as hamburger / WhatToPlay.
        /// </summary>
        private void EnterTunerMode()
        {
            _repeatSameSnapshot = null;
            LayoutTestTune.SetEnabled(false);
            PlayModePickerOptions.ApplyOtherSelection(_session, PlayModePickerOptions.Tuner);
            ApplyTunerDisplayState();
        }

        /// <summary>Shows the existing Tuner UI and starts listening when Tune is already Tuner.</summary>
        private void ApplyTunerDisplayState()
        {
            IsAutoRepeatVisible = false;
            UpdateTunerModeChrome();
            UpdateKeyPickerVisibility();
            UpdatePracticePlayItemLabel();
            // UpdateTunerVisibility owns EnsureReferenceNoteUi / staff / height / listening.
            UpdateTunerVisibility();
        }

        /// <summary>
        /// Starts the shared scale mic pipeline for Tuner exactly once when not already running.
        /// </summary>
        private async Task EnsureTunerListeningAsync()
        {
            if (_session.Tune != "Tuner" || _isReferenceTonePlaying || _isTunerPitchPlaying)
                return;
            if (_isRunning)
            {
                DebugLog.WriteLine("[Tuner] EnsureTunerListening: already running");
                return;
            }

            DebugLog.WriteLine("[Tuner] EnsureTunerListening: requesting StartListening");
            await StartListeningAndEvaluatingAsync();
        }
        private void UpdatePickersContainerVisibility()
        {
            // Scale/key/instrument pickers live on the What to Play page.
            PickersContainer.IsVisible = false;
        }
        private void UpdateTunerVisibility()
        {
            if (!MainThread.IsMainThread)
            {
                RunOnMainThread(UpdateTunerVisibility);
                return;
            }

            var isTuner = _session.Tune == "Tuner";
            bool enteringTuner = !_tunerUiActive && isTuner;
            bool leavingTuner = _tunerUiActive && !isTuner;
            _tunerUiActive = isTuner;

            if (isTuner && _isTempoControlVisible)
            {
                _isTempoControlVisible = false;
                ApplyTempoControlRowVisibility(false);
            }

            if (isTuner && _isTimeSignatureControlVisible)
            {
                _isTimeSignatureControlVisible = false;
                ApplyTimeSignatureControlRowVisibility(false);
            }

            if (TempoMarkingHitTarget != null && isTuner)
                TempoMarkingHitTarget.IsVisible = false;
            else if (!isTuner)
                ScheduleSyncTempoMarkingHitTarget();

            if (TimeSignatureHitTarget != null && isTuner)
                TimeSignatureHitTarget.IsVisible = false;
            else if (!isTuner)
                ScheduleSyncTimeSignatureHitTarget();

            if (StaffBorder != null)
            {
                StaffBorder.IsVisible = false;
                StaffBorder.IsVisible = !isTuner;
            }

            if (TunerGrid != null)
                TunerGrid.IsVisible = isTuner;

            OnPropertyChanged(nameof(IsChildLevelSliderVisible));
            OnPropertyChanged(nameof(IsTempoControlVisible));
            OnPropertyChanged(nameof(IsTimeSignatureControlVisible));
            OnPropertyChanged(nameof(IsBottomPickersVisible));
            OnPropertyChanged(nameof(IsBottomButtonRowVisible));
            RefreshNoteAttemptsDebugButtonVisibility();

            if (isTuner)
            {
                // Same page as Music practice — stop Music Count-In immediately (no OnDisappearing).
                PauseMusicCountInForTunerDisplay(enteringTuner);

                if (TunerBorder != null)
                    TunerBorder.IsVisible = true;

                _session.SessionCompleted = false;
                EnsureReferenceNoteUi();
                UpdateTunerStaffDisplay();
                ApplyTunerHeight();
                Dispatcher.Dispatch(ApplyTunerHeight);
                if (!_isRunning && !_isReferenceTonePlaying && !_isTunerPitchPlaying)
                {
                    DebugLog.WriteLine("[Tuner] UpdateTunerVisibility → EnsureTunerListening");
                    _ = EnsureTunerListeningAsync();
                }
            }
            else
            {
                _ = StopReferenceToneAsync(resumeListening: false);
                _ = StopTunerPitchAsync();
                if (TitlePageModeLabel != null)
                    TitlePageModeLabel.Text = "  Music ";
                BackgroundColor = Color.FromArgb("#F7F7F7");
                if (MainPageRootGrid != null)
                    MainPageRootGrid.BackgroundColor = Colors.Transparent;
                if (MainScrollView != null)
                {
                    MainScrollView.VerticalScrollBarVisibility = ScrollBarVisibility.Default;
                    MainScrollView.BackgroundColor = Colors.Transparent;
                }
                if (TunerBorder != null)
                    TunerBorder.IsVisible = false;
                if (StaffAreaStack != null)
                {
                    StaffAreaStack.HeightRequest = -1;
                    StaffAreaStack.Margin = new Thickness(0);
                }
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

                if (leavingTuner)
                    ResumeMusicListeningAfterLeavingTuner();
            }
        }
        private string[] BuildScaleTuneOptions()
        {
            var practiceTuneTitles = PlayModePickerOptions.BuildTunePickerOptions()
                .Where(t => t != PlayModePickerOptions.HalfThroughSixteenthNotes)
                .ToArray();
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
            int level = _session.ResolvePracticeLevel();
            var choices = new List<ArpeggioPickerChoice>();

            foreach (var pattern in ArpeggioCatalog.GetAvailablePatterns(level))
            {
                string label = ArpeggioCatalog.QualityLabel(pattern);
                if (_arpeggioPickerChoices.ContainsKey(label))
                    continue;

                var choice = new ArpeggioPickerChoice(label, pattern, _session.ChooseConcertRootForWrittenTonic());
                _arpeggioPickerChoices[label] = choice;
                choices.Add(choice);
            }

            return choices;
        }
        private void UpdateScaleTunePicker()
        {
            if (!MainThread.IsMainThread)
            {
                RunOnMainThread(UpdateScaleTunePicker);
                return;
            }

            if (ScaleTunePicker == null)
            {
                UpdatePracticePlayItemLabel();
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
                    && choice.Pattern.Id == _session.SelectedArpeggioId);
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

            UpdatePracticePlayItemLabel();
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
                _session.ApplyArpeggioQuality(arpeggioChoice.Pattern);
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
        /// <summary>Syncs the legacy scale/tune picker without re-entering practice label sync.</summary>
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
                    && choice.Pattern.Id == _session.SelectedArpeggioId);
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

            UpdatePracticePlayItemLabel();
        }
        private void UpdatePracticePlayItemLabel()
        {
            PracticePlayItemLabelText = GetCurrentPlayItemName();
        }
        private void PracticeInstrumentPicker_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (IsPickerSyncSuppressed) return;
            if (PracticeInstrumentPicker == null) return;
            var idx = PracticeInstrumentPicker.SelectedIndex;
            if (idx < 0 || idx >= NoteSessionService.InstrumentOptions.Length) return;
            var fullInstrument = NoteSessionService.InstrumentOptions[idx];
            _session.Instrument = fullInstrument;
            SelectedInstrumentShort = _session.InstrumentDisplayName;

            EnterPickerSyncSuppress();
            try
            {
                if (InstrumentPicker.SelectedIndex != idx)
                    InstrumentPicker.SelectedIndex = idx;
                if (TunerInstrumentPicker != null && TunerInstrumentPicker.SelectedIndex != idx)
                    TunerInstrumentPicker.SelectedIndex = idx;
            }
            finally
            {
                ExitPickerSyncSuppress();
            }
        }
        private void TunerInstrumentPicker_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (IsPickerSyncSuppressed) return;
            if (TunerInstrumentPicker == null) return;
            var idx = TunerInstrumentPicker.SelectedIndex;
            if (idx < 0) return;

            string fullInstrument;
            if (TunerInstrumentPicker.SelectedItem is TunerInstrumentPickerItem item)
                fullInstrument = item.StoredInstrument;
            else if (idx < NoteSessionService.InstrumentOptions.Length)
                fullInstrument = NoteSessionService.InstrumentOptions[idx];
            else
                return;

            _session.Instrument = fullInstrument;
            SelectedInstrumentShort = _session.InstrumentDisplayName;

            EnterPickerSyncSuppress();
            try
            {
                if (InstrumentPicker.SelectedIndex != idx)
                    InstrumentPicker.SelectedIndex = idx;
                if (PracticeInstrumentPicker != null && PracticeInstrumentPicker.SelectedIndex != idx)
                    PracticeInstrumentPicker.SelectedIndex = idx;
            }
            finally
            {
                ExitPickerSyncSuppress();
            }
            SizeTunerCompactPickers();
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
            {
                _freezeStaff = false;
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

            // Check if the selection is a practice tune title (built-in or saved)
            var practiceTune = PlayModePickerOptions.TryResolvePracticeTune(selected);
            DebugLog.WriteLine($"[PickerDBG] practiceTune={practiceTune?.Title ?? "null"}");
            if (practiceTune != null)
            {
                _lastValidScaleTuneIndex = sourcePicker.SelectedIndex;
                _session.IsRandomMode = false;
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
            if (IsPickerSyncSuppressed) return;
            if (InstrumentPicker.SelectedItem is not string s)
                return;

            _session.Instrument = s;
            SelectedInstrumentShort = _session.InstrumentDisplayName;

            // Keep Practice + Tuner instrument pickers in lockstep with Music.
            int idx = InstrumentPicker.SelectedIndex;
            EnterPickerSyncSuppress();
            try
            {
                if (PracticeInstrumentPicker != null && PracticeInstrumentPicker.SelectedIndex != idx)
                    PracticeInstrumentPicker.SelectedIndex = idx;
                if (TunerInstrumentPicker != null && TunerInstrumentPicker.SelectedIndex != idx)
                    TunerInstrumentPicker.SelectedIndex = idx;
            }
            finally
            {
                ExitPickerSyncSuppress();
            }

            if (_session.Tune == "Tuner")
                SizeTunerCompactPickers();

            // Hide picker and show label immediately
            IsInstrumentPickerVisible = false;
            IsInstrumentLabelVisible = true;
            // Workaround: immediately unfocus picker to prevent unwanted stage
            InstrumentPicker.Unfocus();
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
            _userStoppedListening = true;
            _listeningPausedForPageHide = false;
            CancelPendingListeningStarts();
            _playCts?.Cancel();
            StopWaitingCountIn();
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
            => await HandleStartStopToggleAsync();

        private async void OnStartStopToggleClicked(object? sender, TappedEventArgs e)
            => await HandleStartStopToggleAsync();

        private async Task HandleStartStopToggleAsync()
        {
            // Tuner: dedicated listen/stop so Stop leaves the page quiet for pick → staff → play.
            if (_session.Tune == "Tuner")
            {
                if (_isTunerPitchPlaying)
                {
                    await StopTunerPitchAsync();
                    await EnsureTunerListeningAsync();
                    UpdateTunerModeChrome();
                    return;
                }

                // Metronome running: title stays Go; tap still stops metronome and resumes listening.
                if (_isReferenceTonePlaying)
                {
                    await StopReferenceToneAsync(resumeListening: _isRunning);
                    if (!_isRunning)
                        await EnsureTunerListeningAsync();
                    UpdateTunerModeChrome();
                    return;
                }

                if (_isRunning)
                {
                    await StopTunerListeningAsync();
                    return;
                }

                await EnsureTunerListeningAsync();
                UpdateTunerModeChrome();
                return;
            }

            var plan = PracticeSessionLifecycle.PlanStopToggle(
                _isRunning, _session.RepeatSameTune, _repeatSameSnapshot);

            if (plan.Action is PracticeSessionLifecycle.StopToggleAction.StopRestoreRepeatSame
                or PracticeSessionLifecycle.StopToggleAction.StopRegenerateFresh)
            {
                _userStoppedListening = true;
                _listeningPausedForPageHide = false;
                CancelPendingListeningStarts();
                try
                {
                    _playCts?.Cancel();
                    StopWaitingCountIn();
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
                _userStoppedListening = false;
                _listeningPausedForPageHide = false;
                _holdResultForChildSession = false;
                _session.SessionCompleted = false;

                await StartListeningAndEvaluatingAsync(
                    forceNewNotes: plan.ForceNewNotes,
                    scaleKeyTrigger: plan.ScaleKeyTrigger);
            }
        }

        /// <summary>
        /// Tuner Stop: halt mic capture and keep the selected note on the staff/picker
        /// so the user can choose and play reference tones without live detections.
        /// </summary>
        private async Task StopTunerListeningAsync()
        {
            _sessionStartCts?.Cancel();
            try
            {
                StopTunerMicrophoneCaptureCore(clearDetection: true);
                if (_isReferenceTonePlaying)
                    await StopReferenceToneAsync(resumeListening: false);

                SetButtonStates(false);
                StatusService.Instance.StatusMessage =
                    "Stopped — pick a note to play, or tap GO to listen.";
                UpdateTunerModeChrome();
                RefreshTunerPickerDisplayLabel();
                UpdateTunerStaffDisplay();
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[Tuner] Stop listening error: {ex}");
                SetButtonStates(false);
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

