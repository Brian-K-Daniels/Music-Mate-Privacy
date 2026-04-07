using CommunityToolkit.Maui.Alerts;
using musicmate.Controls;
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
        private readonly SessionDatabase _sessionDb = null!;
        private readonly IOrientationService _orientation = null!;
        private readonly ThemeService _themeService = null!;

        private readonly object _processLock = new();
        private DateTime _lastProcess = DateTime.MinValue;
        private CancellationTokenSource? _playCts;
        private bool _isPlaying = false;
        private bool _isRunning = false;
        private bool _isBelowThreshold = true;
        private bool _isProgrammaticColorConfirm = false;

        // fields for inactivity tracking
        private DateTime _lastHeardTime = DateTime.UtcNow;
        private bool _inactivityStopped = false;
        private readonly TimeSpan _inactivityTimeout = TimeSpan.FromMinutes(5);

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
                    _themeService.PanelBackgroundColor = savedColor;
                    StaffBorder.Background = new SolidColorBrush(savedColor);
                    return;
                }

                var dialog = this.FindByName<Controls.ColorPickerDialog>("ColorPickerDialog");
                if (dialog != null)
                {
                    dialog.ResetToDefaults();
                    var preview = dialog.PreviewColor;
                    _themeService?.PanelBackgroundColor = preview;
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
        public string? Tune => _session?.Tune;
        public bool AutoRepeat
        {
            get => _autoRepeat;
            set
            {
                if (_autoRepeat != value)
                {
                    _autoRepeat = value;
                    UpdateAutoRepeatButton();
                    if (!_autoRepeat)
                    {
                        _ = StopListeningAndEvaluatingAsync();
                    }
                }
            }
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
                }
            }
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

        private void UpdateAutoRepeatButton()
        {
            var btn = AutoRepeatToggleButton;
            if (btn == null) return;
            if (AutoRepeat)
            {
                btn.BackgroundColor = Colors.Green;
                btn.TextColor = Colors.White;
            }
            else
            {
                btn.BackgroundColor = Color.FromArgb("#8B4513");
                btn.TextColor = Colors.White;
            }
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

                // disable iOS safe area for this page (use per-edge API available on this MAUI version)
                // Use reflection helper so the project compiles on non-iOS targets
                musicmate.Utilities.Utils.DisableIosSafeArea(this);

                BackgroundColor = Colors.White;
                _orientation = ServiceHelper.GetService<IOrientationService>()!;
                _session = ServiceHelper.GetService<NoteSessionService>()!;
                _sessionDb = ServiceHelper.GetService<SessionDatabase>()!;
                _audio = ServiceHelper.GetService<IAudioCaptureService>()!;
                _player = ServiceHelper.GetService<IAudioPlaybackService>()!;
                _themeService = ServiceHelper.GetService<ThemeService>()!;
                BindingContext = _session;
                StaffBorder.BindingContext = _themeService;
                StaffGraphicsView.BindingContext = _themeService;

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
                _drawable = new Drawables.StaffDrawable(_session, _themeService);
                StaffGraphicsView.Drawable = _drawable;

                // Ensure the GraphicsView reserves computed height. Update when width or notes change.
                StaffGraphicsView.SizeChanged += (s, e) => UpdateStaffHeight();
                // When notes are regenerated, RegenerateNotesAsync will call UpdateStaffHeight indirectly.

                // Ensure visuals reflect ThemeService value applied at app startup.
                // Do not re-deploy saved color here (handled in App startup) — just apply current ThemeService value to visual properties.
                var panelColor = _themeService?.PanelBackgroundColor ?? Colors.White;
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

                // Tuner graphics setup
                TunerBorder.BindingContext = _themeService;
                TunerGraphicsView.BindingContext = _themeService;
                TunerGraphicsView.Drawable = _drawable;
                TunerGraphicsView.SetBinding(GraphicsView.BackgroundColorProperty, new Binding("PanelBackgroundColor"));

                // ColorPickerDialog event: update theme color for all pages
                ColorPickerDialog.ColorPicked += async (s, color) =>
                {
                    _themeService?.PanelBackgroundColor = color;
                    Preferences.Default.Set("StaffPanelColor", color.ToHex());
                    StaffBorder.Background = new SolidColorBrush(color);
                    if (!_isProgrammaticColorConfirm)
                    {
                        await StartListeningAndEvaluatingAsync();
                    }
                };

                // High-contrast drawing update
                _themeService?.PropertyChanged += (s, e) =>
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

                        if (AutoRepeat && _session.Tune == "Random")
                        {
                            double repeatDelay = Preferences.Default.Get("RepeatDelaySeconds", 2.0);
                            await Task.Delay((int)(repeatDelay * 1000));
                            _session.SessionCompleted = false;
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
                        IsAutoRepeatVisible = _session.Tune == "Random";
                        UpdateKeyPickerVisibility();
                        OnPropertyChanged(nameof(Tune));
                    }
                };

                IsAutoRepeatVisible = _session.Tune == "Random";

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
                            "C", "F", "Bb", "G", "D", "A", "E", "B", "F#", "C#",
                            "Eb", "Ab", "Db", "Gb", "Cb"
                        };
                KeyPicker.SelectedIndex = Array.IndexOf((string[])KeyPicker.ItemsSource, _session.Key);
                if (KeyPicker.SelectedIndex < 0)
                    KeyPicker.SelectedIndex = 0;
                _lastFreeKeyIndex = KeyPicker.SelectedIndex;

                var tuneOptions = new[] { "Selected Scale", "Random", "Tuner" };
                TunePicker.ItemsSource = tuneOptions;
                var savedTune = Preferences.Default.Get<string?>("SelectedTune", null);
                if (!string.IsNullOrEmpty(savedTune) && tuneOptions.Contains(savedTune))
                {
                    _session.Tune = savedTune;
                }
                var tuneIdx = Array.IndexOf(tuneOptions, _session.Tune);
                TunePicker.SelectedIndex = tuneIdx >= 0 ? tuneIdx : 0;
                IsAutoRepeatVisible = _session.Tune == "Random";

                TunePicker.SelectedIndexChanged += (s, e) =>
                {
                    if (TunePicker.SelectedItem is string selectedTune)
                    {
                        _session.Tune = selectedTune;
                        Preferences.Default.Set("SelectedTune", selectedTune);
                        IsAutoRepeatVisible = selectedTune == "Random";
                        UpdateKeyPickerVisibility();
                    }
                };

                // InstrumentPicker.SelectedIndexChanged += OnSettingsChanged;

                InstrumentPicker.SelectedIndexChanged += InstrumentPicker_SelectedIndexChanged;
                KeyPicker.SelectedIndexChanged += OnKeyPickerChangedWithPrompt;
                TunePicker.SelectedIndexChanged += OnSettingsChanged;
                ColorPickerDialog.ColorPreviewed -= OnStaffPanelColorPreviewed;
                ColorPickerDialog.ColorPreviewed += OnStaffPanelColorPreviewed;

                UpdateConcertKeyLabel();
                UpdateSelectedScaleLabel();
                UpdateTunerVisibility();
                StatusService.Instance.StatusMessage =
                    $"Select Keys, swipe up to scroll, touch green button and play.";
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
            var width = StaffGraphicsView.Width <= 0 ? 360 : StaffGraphicsView.Width;
            await _session.GenerateNotesAsync(width);

#if DEBUG
            if (_session.Tune == "Random")
            {
                var names = string.Join(", ", _session.NotesToDraw.Select(n => n.Name));
                Debug.WriteLine($"[Random] Generated {_session.NotesToDraw.Count} notes: {names}");
            }
#endif

            StaffGraphicsView.Invalidate();
            // After regenerating notes, recompute and apply the required height so the view isn't clipped
            UpdateStaffHeight();
        }

        private void UpdateStaffHeight()
        {
            try
            {
                if (StaffGraphicsView == null || _drawable == null)
                    return;

                // Need a valid width to compute height; if not ready, skip
                if (StaffGraphicsView.Width <= 0)
                    return;

                var needed = _drawable.ComputeRequiredHeight((float)StaffGraphicsView.Width);
                // Add safety margin for glyph ascenders/descenders and feedback
                var margin = 12f;
                var heightReq = needed + margin;

                // Apply to GraphicsView and containing Border so layout gives the space
                StaffGraphicsView.HeightRequest = heightReq;
                StaffBorder.HeightRequest = heightReq;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateStaffHeight] ERROR: {ex}");
            }
        }

        private async void SetButtonStates(bool isRunning)
        {
            _isRunning = isRunning;
            if (isRunning)
            {
                StartStopButton.Text = "■";
                StartStopButton.TextColor = Color.FromArgb("#E04040");
                PlayEvaluateButton.TextColor = Color.FromArgb("#CCCCCC");
                PlayEvaluateButton.IsEnabled = false;
            }
            else
            {
                StartStopButton.Text = "●";
                StartStopButton.TextColor = Color.FromArgb("#008000");
                PlayEvaluateButton.TextColor = Color.FromArgb("#555555");
                PlayEvaluateButton.IsEnabled = true;
            }
        }

        private async void OnStartStopToggleClicked(object? sender, EventArgs e)
        {
            if (_isRunning)
            {
                try
                {
                    _playCts?.Cancel();
                    _audio.StopCapture();
                    StatusService.Instance.StatusMessage = "Stopped, press green button to start listening.";
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
            // Find main layout and scroll container
            var mainLayout = this.FindByName<VerticalStackLayout>("MainPageMainLayout");
            var mainScroll = this.FindByName<ScrollView>("MainScrollView");

            // Programmatic left gutter/padding removed — layout now uses explicit gutter column in XAML.

            Dispatcher.Dispatch(UpdateAutoRepeatButton);

            Debug.WriteLine($"[DEBUG] OnAppearing: IsAutoRepeatVisible={IsAutoRepeatVisible}, Tune={_session.Tune}");
            var grid = this.FindByName<Grid>(""); // root Grid if named, or use this.Content as Grid
            Debug.WriteLine($"MainPage padding: {this.Padding}");
            Debug.WriteLine($"MainLayout margin: {MainPageMainLayout.Margin}");
            Debug.WriteLine($"Device density: {DeviceDisplay.MainDisplayInfo.Density}, width: {DeviceDisplay.MainDisplayInfo.Width}");
            IsAutoRepeatVisible = _session.Tune == "Random";
            DeviceDisplay.Current.KeepScreenOn = true;

#if DEBUG
            if (_session.AutoStart && _session.Tune == "Random")
            {
                AutoRepeat = true;
                StatusService.Instance.IsPremiumUser = true;
            }
#endif

            if (_session.AutoStart)
            {
                await StartAfterDelayAsync();
            }
            // Ensure notes are displayed on staff at app start when not Premium and not auto start
            if (!StatusService.Instance.IsPremiumUser && !_session.AutoStart)
            {
                await RegenerateNotesAsync();
            }

            // When Tuner mode is active, scroll so pickers are just in view at the top
            if (_session.Tune == "Tuner")
            {
                await Task.Delay(100);
                await MainScrollView.ScrollToAsync(PickersContainer, ScrollToPosition.Start, false);
            }

#if DEBUG
            // Diagnostic layout logging: wait briefly for layout to complete then print bounds/margins.
            // Also apply temporary distinct background colors to help visualize empty space.
            try
            {
                await Task.Delay(200); // allow layout pass to complete

                // Avoid reusing `mainScroll` name; use dbgMainScroll for the debug lookup to prevent shadowing issues.
                var dbgMainScroll = this.FindByName<ScrollView>("MainScrollView");
                var staffBorder = this.FindByName<Border>("StaffBorder");
                var playLbl = this.FindByName<Label>("PlayEvaluateButton");
                var startLbl = this.FindByName<Label>("StartStopButton");

                void LogElement(string name, View? el)
                {
                    if (el == null)
                    {
                        Debug.WriteLine($"[LAYOUT DEBUG] {name}: null");
                        return;
                    }
                    Debug.WriteLine($"[LAYOUT DEBUG] {name}: Bounds={el.Bounds} Margin={el.Margin} Width={el.Width} Height={el.Height} X={el.X} Y={el.Y} Visibility={(el.IsVisible ? "Visible" : "Hidden")}");
                }

                Debug.WriteLine("[LAYOUT DEBUG] After layout:");
                LogElement("MainPageMainLayout", mainLayout);
                LogElement("MainScrollView", dbgMainScroll);
                LogElement("StaffBorder", staffBorder);
                LogElement("PlayEvaluateButton", playLbl);
                LogElement("StartStopButton", startLbl);

                // Temporary visual cues to help locate empty space. Remove when done.
                //try { if (mainLayout != null) mainLayout.Background = new SolidColorBrush(Colors.LightPink); } catch { }
                //try { if (dbgMainScroll != null) dbgMainScroll.Background = new SolidColorBrush(Colors.LightBlue); } catch { }
                //try { if (staffBorder != null) staffBorder.Background = new SolidColorBrush(Colors.LightGreen); } catch { }
                //try { if (playLbl != null) playLbl.Background = new SolidColorBrush(Colors.Yellow); } catch { }
                //try { if (startLbl != null) startLbl.Background = new SolidColorBrush(Colors.Orange); } catch { }  //  2026.04.02 1830  block out
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LAYOUT DEBUG] ERROR: {ex}");
            }
#endif
            // If notes haven't been generated yet, generate them so the staff isn't empty initially.
            try
            {
                if (_session != null && _session.NotesToDraw.Count == 0)
                {
                    // Allow layout to settle so StaffGraphicsView has a valid Width where possible.
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (StaffGraphicsView != null && StaffGraphicsView.Width <= 0 && sw.ElapsedMilliseconds < 1000)
                    {
                        await Task.Delay(40);
                    }

                    // Regenerate notes and refresh view
                    await RegenerateNotesAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OnAppearing] ERROR regenerating notes: {ex}");
            }
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

            // When user taps auto-play and playback is not already running,
            // set the instrument short name to C (leave the tune/key signature unchanged).
            try
            {
                var instrumentOptions = NoteSessionService.InstrumentOptions.Cast<string>().ToArray();
                var instIdx = Array.FindIndex(instrumentOptions, s => s.Split(',')[0].Trim() == "C");
                if (instIdx >= 0)
                {
                    InstrumentPicker.SelectedIndex = instIdx;
                    _session.Instrument = instrumentOptions[instIdx];
                    SelectedInstrumentShort = instrumentOptions[instIdx].Split(',')[0].Trim();
                    // Show overlay label instead of picker after selection
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
            SetButtonStates(false);
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
                    // Only accept the note as correct if it matches the expected note (including octave) at the current index
                    if (_session.Tune == "Random" && _session.CurrentNoteIndex < _session.NotesToDraw.Count)
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

                        if (isMatch)
                        {
                            // If this is the first correct note heard in the session,
                            // clear accumulated wrong counts so the display/metrics start fresh.
                            if (_session.CorrectNoteIndices.Count == 0)
                            {
                                var keys = _session.NoteFeedbacks.Keys.ToList();
                                foreach (var k in keys)
                                {
                                    var v = _session.NoteFeedbacks[k];
                                    _session.NoteFeedbacks[k] = (0, v.Cents);
                                }
                            }

                            var result = _session.Evaluate(freq);
                            var advanced = _session.UpdateFeedbackForCurrent(freq, result);
                            if (advanced)
                            {
                                _session.RecordRandomSessionNoteResult(expectedName, result.correct);
                                Debug.WriteLine($"[Random] Recorded result for {expectedName}");
                                StaffGraphicsView.Invalidate();
                            }
                        }
                        else
                        {
                            _session.RecordRandomSessionNoteResult(expectedName, false);
                            Debug.WriteLine($"[Random] Recorded wrong for {expectedName} (heard {detectedNote})");
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

        public async Task StartListeningAndEvaluatingAsync(bool playBack = false)
        {
            try
            {
                StatusService.Instance.StatusMessage = "Listening";
                Debug.WriteLine($"[Start] Starting listening, playBack={playBack}");
                SetButtonStates(true);

                _lastProcess = DateTime.MinValue;
                _isBelowThreshold = true;
                _pitchBufferPos = 0;
                _session.Reset();
                await RegenerateNotesAsync();

                Debug.WriteLine("[Start] Requesting audio permission...");
                await _audio.EnsurePermissionAsync();
                Debug.WriteLine("[Start] Starting audio capture...");
                _audio.StartCapture(OnAudioBlock);
                Debug.WriteLine("[Start] Audio capture started");

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

        private async Task PlayDisplayedAsync(CancellationToken ct)
        {
            if (_session.NotesToDraw.Count == 0)
                return;

            var freqs = _session.NotesToDraw.Select(n => n.TargetFreq);
            var bpm = Math.Clamp(_session.PlaybackBpm, 30, 200);
            var beatSeconds = 60.0 / bpm;
            var gapSeconds = Math.Min(0.02, beatSeconds * 0.05);
            var noteSeconds = Math.Max(0.05, beatSeconds - gapSeconds);

            await _player.PlayAsync(freqs, noteSeconds, gapSeconds, 0.22f, ct);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                _isPlaying = false;
                PlayEvaluateButton.Text = "▶";
                PlayEvaluateButton.TextColor = Color.FromArgb("#008000");
                SetButtonStates(false);
            });
        }

        private async Task UpdateNoteStatsDatabaseAsync()
        {
            try
            {
                var db = ServiceHelper.GetService<NoteDatabase>();
                var sessionStats = _session.GetAndClearRandomSessionNoteStats();

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
                            MsAverage = msCount > 0 ? totalMs / msCount : 0.0
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
                        await db.UpdateAsync(stat);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Stats] UpdateNoteStatsDatabaseAsync error: {ex.Message}");
            }
        }

        private async Task SaveSessionStatAsync()
        {
            if (_sessionDb == null) return;
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
                Tune = _session.Tune ?? string.Empty,
                Instrument = _session.Instrument?.Split(',')[0].Trim() ?? string.Empty,
                Sc = _session.SelectedScale,
                Hi = hi?.Name ?? "",
                Lo = lo?.Name ?? "",
                Pc = apc,
                PcRaw = pc,
                Tp = meanBpm ?? 0,
                Ts = stdBpm ?? 0
            };

            await _sessionDb.InsertAsync(stat);
        }

        private async void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(_session.SelectedScale) ||
                e.PropertyName == nameof(NoteSessionService.Instrument) ||
                e.PropertyName == nameof(NoteSessionService.Key) ||
                e.PropertyName == nameof(NoteSessionService.Tune))
            {
                await RegenerateNotesAsync();
                UpdateTunerVisibility();
                UpdateKeyPickerVisibility();
            }

            if (e.PropertyName == nameof(_session.SelectedScale) ||
                e.PropertyName == nameof(NoteSessionService.Key) ||
                e.PropertyName == nameof(NoteSessionService.Instrument))
            {
                UpdateConcertKeyLabel();
                UpdateSelectedScaleLabel();
            }
        }

        private void UpdateKeyPickerVisibility()
        {
            var hide = _session.Tune == "Tuner";
            KeyPicker.IsVisible = !hide;
            KeyLabel.IsVisible = !hide;
            KeyBorder.IsVisible = !hide;
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

            // Always use only the short key (before comma)
            var shortKey = selectedKey.Split(',')[0].Trim();

            if (IsPremiumKey(shortKey) && !StatusService.Instance.IsPremiumUser)
            {
                bool upgrade = await DisplayAlertAsync("Premium Feature", $"The key '{shortKey}' is a premium feature. Upgrade to access. (See About page)", "Upgrade", "Cancel");
                if (!upgrade)
                {
                    KeyPicker.SelectedIndex = _lastFreeKeyIndex;
                    return;
                }
            }
            else
            {
                _lastFreeKeyIndex = KeyPicker.SelectedIndex;
            }
            // Assign only the short key
            _session.Key = shortKey;
            OnSettingsChanged(sender, e);
        }

        private async void OnSettingsChanged(object? sender, EventArgs e)
        {
            _session.Instrument = InstrumentPicker.SelectedItem?.ToString() ?? _session.Instrument;
            _session.Key = KeyPicker.SelectedItem?.ToString() ?? _session.Key;
            _session.Tune = TunePicker.SelectedItem?.ToString() ?? _session.Tune;

            UpdateConcertKeyLabel();
            UpdateSelectedScaleLabel();
            UpdateTunerVisibility();
            await RegenerateNotesAsync();
        }

        private void UpdateConcertKeyLabel()
        {
            ConcertKeyLabel.Text = $"Concert Key: {_session.GetConcertKey()}, ";
        }

        private void UpdateSelectedScaleLabel()
        {
            SelectedScaleLabel.Text = $"Selected Scale: {_session.SelectedScale}";
        }

        private void UpdateTunerVisibility()
        {
            var isTuner = _session.Tune == "Tuner";
            StaffBorder.IsVisible = !isTuner;
            TunerGrid.IsVisible = isTuner;
            // when entering tuner mode, clear any previous session state and start listening
            if (isTuner)
            {
                _session.SessionCompleted = false;
                TunerGraphicsView.Invalidate();
                // ensure audio capture is running so notes are heard on the Tuner page
                if (!_isRunning)
                {
                    _ = StartListeningAndEvaluatingAsync();
                }
            }
        }

        // Use base BindableObject.OnPropertyChanged so XAML bindings receive change notifications

        private async Task StopListeningAndEvaluatingAsync()
        {
            _playCts?.Cancel();
            _audio.StopCapture();
            StatusService.Instance.StatusMessage = "Paused for color selection.";
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
            await StopListeningAndEvaluatingAsync();
            ColorPickerDialog.Show(_themeService.PanelBackgroundColor);
        }

        //private void OnResetClicked(object? sender, EventArgs e)
        //{
        //    ColorPickerDialog.ResetToDefaults();
        //    var color = ColorPickerDialog.PreviewColor;
        //    _themeService.PanelBackgroundColor = color;
        //    Preferences.Default.Set("StaffPanelColor", color.ToHex());
        //    StaffBorder.Background = new SolidColorBrush(color);
        //}
    }
}
