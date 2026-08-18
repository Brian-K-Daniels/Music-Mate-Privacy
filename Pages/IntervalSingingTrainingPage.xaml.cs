using System.ComponentModel;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;
using musicmate.Utilities;

namespace musicmate.Pages
{
    public partial class IntervalSingingTrainingPage : ContentPage
    {
        private readonly NoteSessionService _session;
        private readonly ThemeService _theme;
        private readonly IAudioPlaybackService _player;
        private readonly IAudioCaptureService _audio;
        private readonly IOrientationService _orientation;
        private readonly ISafeAreaService? _safeArea;

        private readonly IntervalSingingListenGate _gate = new();
        private readonly IntervalSingingSessionStats _stats = new();
        private readonly PitchWindowAccumulator _pitchWindow = new();
        private readonly object _processLock = new();
        private readonly Dictionary<int, Button> _intervalButtons = new();
        private readonly List<int> _startNoteMidis = new();
        private readonly SungPitchStabilizer _singStabilizer = new();

        private CancellationTokenSource? _playCts;
        private IntervalEarTrainingLogic.IntervalPitches? _exercise;
        private IntervalSingingTrainingMode _mode = IntervalSingingTrainingLogic.DefaultMode;
        private IntervalDirectionMode _directionMode = IntervalSingingTrainingLogic.DefaultDirectionMode;
        private IntervalSingingExerciseState _state = IntervalSingingExerciseState.Idle;
        private IntervalSingingImitateTracker? _imitate;
        private StaffDrawable? _staffDrawable;
        private DateTime _ignoreUntilUtc = DateTime.MinValue;
        private DateTime _lastProcess;
        private int _level = IntervalSingingTrainingLogic.DefaultLevel;
        private int? _selectedStartWrittenMidi;
        private int _transposeOffsetForStartNote;
        private int _startNotePickerSyncSuppress;
        private int _instrumentPickerSyncSuppress;
        private int _modePickerSyncSuppress;
        private int _playGeneration;
        private bool _revealed;
        private bool _succeeded;

        public IntervalSingingTrainingPage()
        {
            InitializeComponent();
            Utils.DisableIosSafeArea(this);
            _session = ServiceHelper.GetService<NoteSessionService>()!;
            _theme = ServiceHelper.GetService<ThemeService>()!;
            _player = ServiceHelper.GetService<IAudioPlaybackService>()!;
            _audio = ServiceHelper.GetService<IAudioCaptureService>()!;
            _orientation = ServiceHelper.GetService<IOrientationService>()!;
            _safeArea = ServiceHelper.GetService<ISafeAreaService>();

            BuildIntervalButtons();
            InitModePicker();
            LoadPreferences();
            InitInstrumentPicker();
            _transposeOffsetForStartNote = _session.InstrumentTransposeOffset;
            InitStartNotePicker();
            UpdateActionButtons();
            HideStaffReveal();
            ApplySafeAreaPadding();
            SizeChanged += (_, _) => ApplyLandscapeLayout();
            IntervalsSection.SizeChanged += (_, _) => SyncStaffLayout();
            StaffGraphicsView.SizeChanged += (_, _) => SyncStaffLayout();
        }

        private async void OnNavigateMusicClicked(object? sender, EventArgs e)
        {
            try
            {
                await NavigationBusyService.GoToAsync("//MusicPage");
            }
            catch (Exception ex)
            {
                Utils.Log($"[SingingTraining] Navigate to Music ERROR: {ex.Message}");
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            _orientation?.ForceLandscape();
            _gate.PageVisible = true;
            LoadPreferences();
            _session.PropertyChanged -= OnSessionPropertyChanged;
            _session.PropertyChanged += OnSessionPropertyChanged;
            SyncInstrumentPickerFromSession();
            RefreshStartNotePicker(preserveConcertPitch: false);
            ApplyLandscapeLayout();
            ApplyStaffForState();
            if (Shell.Current is AppShell shell)
                shell.EnsureFlyoutItemsVisiblePublic();
        }

        protected override void OnDisappearing()
        {
            StopPageAudio();
            _session.PropertyChanged -= OnSessionPropertyChanged;
            if (_staffDrawable != null)
            {
                _staffDrawable.NotationKeyOverride = null;
                _staffDrawable.NotationScaleOverride = null;
                _staffDrawable.OmitStaffHeader = false;
            }
            if (Shell.Current is AppShell shell)
                shell.EnsureFlyoutItemsVisiblePublic();
            base.OnDisappearing();
        }

        /// <summary>Stops mic, playback, and pending tasks. Safe to call from navigation away.</summary>
        private void StopPageAudio()
        {
            _gate.PageVisible = false;
            StopListening();
            CancelPlayback();
            _gate.StopAll();
            _state = _gate.State;
            _imitate = null;
            _singStabilizer.Reset();
        }

        private void ApplyLandscapeLayout()
        {
            ApplySafeAreaPadding();

            double pageW = Width;
            if (pageW < 8)
                return;

            if (PageRoot.WidthRequest >= 0)
                PageRoot.WidthRequest = -1;
            if (IntervalsSection.WidthRequest >= 0)
                IntervalsSection.WidthRequest = -1;

            double inner = Math.Max(200, pageW - PageRoot.Padding.Left - PageRoot.Padding.Right);
            double staffW = Math.Clamp(inner * 0.16, 110, 128);
            if (Math.Abs(StaffRevealBorder.WidthRequest - staffW) > 0.5)
            {
                StaffRevealBorder.WidthRequest = staffW;
                StaffRevealBorder.MinimumWidthRequest = staffW;
            }

            StaffRevealBorder.IsVisible = true;
            SyncStaffLayout();
        }

        private void ApplySafeAreaPadding()
        {
            var insets = _safeArea?.GetSafeAreaInsets() ?? (0f, 0f, 0f, 0f);
            const double edge = 4;
            PageRoot.Padding = new Thickness(
                edge + Math.Min(insets.Left, 8),
                1,
                edge + Math.Min(insets.Right, 8),
                Math.Max(2, Math.Min(insets.Bottom, 6)));
        }

        private void BuildIntervalButtons()
        {
            IntervalButtonsHost.Children.Clear();
            IntervalButtonsHost.RowDefinitions.Clear();
            _intervalButtons.Clear();

            const int columns = 3;
            const int rows = 5;
            for (int r = 0; r < rows; r++)
                IntervalButtonsHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });

            var gridStyle = (Style)Resources["SingTrainIntervalButton"];
            var intervals = IntervalEarTrainingCatalog.Intervals;
            for (int i = 0; i < intervals.Count; i++)
            {
                int semitones = intervals[i].Semitones;
                int col = i % columns;
                int row = i / columns;
                string label = IntervalEarTrainingCatalog.FormatButtonLabel(semitones);
                var btn = new Button
                {
                    Text = label,
                    Style = gridStyle,
                };
                SemanticProperties.SetDescription(btn, $"Interval {label}");
                ApplyFamilyColor(btn, semitones);
                int captured = semitones;
                btn.Clicked += async (_, _) => await OnIntervalClickedAsync(captured);
                Grid.SetColumn(btn, col);
                Grid.SetRow(btn, row);
                IntervalButtonsHost.Children.Add(btn);
                _intervalButtons[semitones] = btn;
            }
        }

        private static void ApplyFamilyColor(Button button, int semitones)
        {
            button.BackgroundColor = IntervalEarTrainingButtonColors.FamilyBackground(semitones);
            button.TextColor = IntervalEarTrainingButtonColors.LabelText;
            button.BorderWidth = 0;
        }

        private void RestoreAllIntervalFamilyColors()
        {
            foreach (var (semitones, btn) in _intervalButtons)
                ApplyFamilyColor(btn, semitones);
        }

        private void LoadPreferences()
        {
            _mode = IntervalSingingTrainingLogic.LoadPersistedMode();
            _gate.Mode = _mode;
            _directionMode = IntervalSingingTrainingLogic.LoadPersistedDirection();
            _level = IntervalSingingTrainingLogic.LoadPersistedLevel();
            ApplyModePickerSelection();
            LevelSlider.Value = _level;
            LevelValueLabel.Text = _level.ToString();
            UpdateDirectionButtonVisuals();
        }

        private void InitModePicker()
        {
            _modePickerSyncSuppress++;
            try
            {
                ModePicker.ItemsSource = IntervalSingingTrainingLogic.ModePickerItems;
            }
            finally
            {
                EndModePickerSyncSuppress();
            }

            ApplyModePickerSelection();
        }

        private void ApplyModePickerSelection()
        {
            int idx = IntervalSingingTrainingLogic.PickerIndexFromMode(_mode);
            if (ModePicker.SelectedIndex == idx)
                return;

            _modePickerSyncSuppress++;
            try
            {
                ModePicker.SelectedIndex = idx;
            }
            finally
            {
                EndModePickerSyncSuppress();
            }
        }

        private void EndModePickerSyncSuppress()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_modePickerSyncSuppress > 0)
                    _modePickerSyncSuppress--;
            });
        }

        private void OnModePickerChanged(object? sender, EventArgs e)
        {
            if (_modePickerSyncSuppress > 0)
                return;

            var next = IntervalSingingTrainingLogic.ModeFromPickerIndex(ModePicker.SelectedIndex);
            if (next == _mode)
                return;

            StopListening();
            CancelPlayback();
            _mode = next;
            _gate.Mode = _mode;
            IntervalSingingTrainingLogic.PersistMode(_mode);
            ResetExercise(keepPitches: false);
            StatusLabel.Text = string.Empty;
            InstructionLabel.Text = _mode == IntervalSingingTrainingMode.HearAndIdentify
                ? IntervalSingingTrainingLogic.FormatIdentifyInstruction()
                : "Tap New Exercise to begin.";
        }

        private void OnLevelSliderValueChanged(object? sender, ValueChangedEventArgs e)
        {
            int level = IntervalSingingTrainingLogic.ClampLevel((int)Math.Round(e.NewValue));
            if (level == _level)
            {
                LevelValueLabel.Text = level.ToString();
                return;
            }

            _level = level;
            LevelValueLabel.Text = level.ToString();
            IntervalSingingTrainingLogic.PersistLevel(level);
        }

        private void InitInstrumentPicker()
        {
            if (InstrumentPicker.ItemsSource != null)
                return;

            _instrumentPickerSyncSuppress++;
            try
            {
                InstrumentPicker.ItemsSource = NoteSessionService.InstrumentOptions;
            }
            finally
            {
                EndInstrumentPickerSyncSuppress();
            }

            SyncInstrumentPickerFromSession();
        }

        private void SyncInstrumentPickerFromSession()
        {
            if (InstrumentPicker.ItemsSource == null)
            {
                InitInstrumentPicker();
                return;
            }

            int idx = InstrumentCatalog.IndexOfOption(_session.Instrument);
            if (idx < 0 || InstrumentPicker.SelectedIndex == idx)
                return;

            _instrumentPickerSyncSuppress++;
            try
            {
                InstrumentPicker.SelectedIndex = idx;
            }
            finally
            {
                EndInstrumentPickerSyncSuppress();
            }
        }

        private void EndInstrumentPickerSyncSuppress()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_instrumentPickerSyncSuppress > 0)
                    _instrumentPickerSyncSuppress--;
            });
        }

        private void OnInstrumentPickerChanged(object? sender, EventArgs e)
        {
            if (_instrumentPickerSyncSuppress > 0)
                return;
            if (InstrumentPicker.SelectedItem is not string selected)
                return;

            _session.Instrument = selected;
        }

        private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NoteSessionService.Instrument)
                || e.PropertyName == nameof(NoteSessionService.InstrumentDisplayName)
                || e.PropertyName == nameof(NoteSessionService.InstrumentKey)
                || e.PropertyName == nameof(NoteSessionService.InstrumentTransposeOffset))
            {
                SyncInstrumentPickerFromSession();
                RefreshStartNotePicker(preserveConcertPitch: true);
            }
            else if (e.PropertyName == nameof(NoteSessionService.LowestNote)
                || e.PropertyName == nameof(NoteSessionService.HighestNote))
            {
                RefreshStartNotePicker(preserveConcertPitch: false);
            }
        }

        private void InitStartNotePicker()
        {
            _selectedStartWrittenMidi = IntervalSingingTrainingLogic.LoadPersistedStartNote();
            RefreshStartNotePicker(preserveConcertPitch: false);
        }

        private void RefreshStartNotePicker(bool preserveConcertPitch)
        {
            int newOffset = _session.InstrumentTransposeOffset;
            if (preserveConcertPitch && _selectedStartWrittenMidi is int written)
            {
                int concert = TunerReferenceNoteCatalog.ToConcertMidi(
                    written, _transposeOffsetForStartNote);
                _selectedStartWrittenMidi = TunerReferenceNoteCatalog.FromConcertMidi(
                    concert, newOffset);
            }

            _transposeOffsetForStartNote = newOffset;

            var range = TryResolveSingingWrittenRange();
            _startNoteMidis.Clear();
            if (range is { } bounds)
            {
                _startNoteMidis.AddRange(IntervalEarTrainingLogic.BuildStartNoteMidis(
                    bounds.Low, bounds.High, _session.AvailableInstrumentMidis));
            }

            if (_selectedStartWrittenMidi is int midi && !_startNoteMidis.Contains(midi))
                _selectedStartWrittenMidi = null;

            IntervalSingingTrainingLogic.PersistStartNote(_selectedStartWrittenMidi);
            ApplyStartNotePickerItems();
        }

        private void ApplyStartNotePickerItems()
        {
            if (StartNotePicker == null)
                return;

            var items = new List<string>(_startNoteMidis.Count + 1)
            {
                IntervalEarTrainingLogic.RandomStartNoteToken,
            };
            foreach (int midi in _startNoteMidis)
                items.Add(TunerReferenceNoteCatalog.FormatCompactWrittenLabel(midi));

            int targetIndex = 0;
            if (_selectedStartWrittenMidi is int selected)
            {
                int midiIdx = _startNoteMidis.IndexOf(selected);
                if (midiIdx >= 0)
                    targetIndex = midiIdx + 1;
            }

            _startNotePickerSyncSuppress++;
            try
            {
                StartNotePicker.ItemsSource = items;
                StartNotePicker.SelectedIndex = targetIndex;
            }
            finally
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_startNotePickerSyncSuppress > 0)
                        _startNotePickerSyncSuppress--;
                });
            }
        }

        private void OnStartNotePickerChanged(object? sender, EventArgs e)
        {
            if (_startNotePickerSyncSuppress > 0)
                return;

            int idx = StartNotePicker.SelectedIndex;
            if (idx <= 0)
                _selectedStartWrittenMidi = null;
            else if (idx - 1 < _startNoteMidis.Count)
                _selectedStartWrittenMidi = _startNoteMidis[idx - 1];
            else
                _selectedStartWrittenMidi = null;

            IntervalSingingTrainingLogic.PersistStartNote(_selectedStartWrittenMidi);
        }

        private (int Low, int High)? TryResolveSingingWrittenRange()
        {
            int lowMidi = NoteSessionService.NoteNameToMidi(_session.LowestNote);
            int highMidi = NoteSessionService.NoteNameToMidi(_session.HighestNote);
            if (lowMidi <= 0 || highMidi <= 0)
                return null;

            if (!IntervalSingingTrainingLogic.TryResolveSingingWrittenRange(
                    _session.Instrument, lowMidi, highMidi, _session.InstrumentTransposeOffset,
                    out int low, out int high))
                return null;

            return (low, high);
        }

        private async Task<(int Low, int High)?> TryResolveSingingWrittenRangeAsync()
        {
            var range = TryResolveSingingWrittenRange();
            if (range is null)
            {
                await DisplayAlertAsync(
                    "Note range",
                    "Could not resolve a usable singing range from Settings.",
                    "OK");
                return null;
            }

            return range;
        }

        private void OnDirectionUpClicked(object? sender, EventArgs e)
            => SetDirectionMode(IntervalDirectionMode.Ascending);

        private void OnDirectionDownClicked(object? sender, EventArgs e)
            => SetDirectionMode(IntervalDirectionMode.Descending);

        private void OnDirectionBothClicked(object? sender, EventArgs e)
            => SetDirectionMode(IntervalDirectionMode.Random);

        private void SetDirectionMode(IntervalDirectionMode mode)
        {
            _directionMode = mode;
            IntervalSingingTrainingLogic.PersistDirection(mode);
            UpdateDirectionButtonVisuals();
        }

        private void UpdateDirectionButtonVisuals()
        {
            ApplyDirectionButtonState(DirectionUpButton, _directionMode == IntervalDirectionMode.Ascending);
            ApplyDirectionButtonState(DirectionDownButton, _directionMode == IntervalDirectionMode.Descending);
            ApplyDirectionButtonState(DirectionBothButton, _directionMode == IntervalDirectionMode.Random);
        }

        private static void ApplyDirectionButtonState(Button button, bool selected)
        {
            button.Opacity = selected ? 1.0 : 0.55;
            button.FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None;
        }

        private async void OnNewExerciseClicked(object? sender, EventArgs e)
        {
            try
            {
                await StartAutomaticExerciseAsync();
            }
            catch (Exception ex)
            {
                Utils.Log($"[SingingTraining] New Exercise ERROR: {ex}");
            }
        }

        private async void OnHearAgainClicked(object? sender, EventArgs e)
        {
            try
            {
                if (_exercise is not { } pitches)
                    return;
                await PresentCurrentExerciseAsync(pitches, replay: true);
            }
            catch (Exception ex)
            {
                Utils.Log($"[SingingTraining] Hear Again ERROR: {ex}");
            }
        }

        private void OnRevealClicked(object? sender, EventArgs e)
        {
            if (_exercise is null || _succeeded)
                return;

            StopListening();
            _revealed = true;
            _stats.RecordReveal(alreadySucceeded: false);
            SetState(IntervalSingingExerciseState.Revealed);
            ApplyStaffForState();
            RestoreAllIntervalFamilyColors();
            StatusLabel.Text = "Revealed — this does not count as a correct answer.";
            UpdateActionButtons();
        }

        private async Task OnIntervalClickedAsync(int semitones)
        {
            try
            {
                if (_mode == IntervalSingingTrainingMode.HearAndIdentify)
                {
                    await SubmitIdentifyAnswerAsync(semitones);
                    return;
                }

                await StartManualExerciseAsync(semitones);
            }
            catch (Exception ex)
            {
                Utils.Log($"[SingingTraining] Interval tap ERROR: {ex}");
            }
        }

        private async Task StartAutomaticExerciseAsync()
        {
            var range = await TryResolveSingingWrittenRangeAsync();
            if (range is null)
                return;
            var (low, high) = range.Value;

            if (!IntervalSingingTrainingLogic.TryPickAutomaticExercise(
                    _level, low, high, _directionMode, _selectedStartWrittenMidi, Random.Shared, out var pitches))
            {
                await DisplayAlertAsync(
                    "Interval unavailable",
                    "No automatic interval fits the current Level, Direction, Start note, and singing range.",
                    "OK");
                return;
            }

            await BeginExerciseAsync(pitches);
        }

        private async Task StartManualExerciseAsync(int semitones)
        {
            var range = await TryResolveSingingWrittenRangeAsync();
            if (range is null)
                return;
            var (low, high) = range.Value;

            if (!IntervalSingingTrainingLogic.TryPickManualInterval(
                    semitones, low, high, _directionMode, _selectedStartWrittenMidi, Random.Shared, out var pitches))
            {
                string startLabel = _selectedStartWrittenMidi is int start
                    ? TunerReferenceNoteCatalog.FormatCompactWrittenLabel(start)
                    : "a random start";
                await DisplayAlertAsync(
                    "Interval unavailable",
                    $"{startLabel} cannot form a {IntervalEarTrainingCatalog.GetName(semitones)} within the singing range.",
                    "OK");
                return;
            }

            await BeginExerciseAsync(pitches);
        }

        private async Task BeginExerciseAsync(IntervalEarTrainingLogic.IntervalPitches pitches)
        {
            StopListening();
            RestoreAllIntervalFamilyColors();
            _exercise = pitches;
            _revealed = false;
            _succeeded = false;
            _imitate = null;
            _singStabilizer.Reset();
            SetState(IntervalSingingExerciseState.Presenting);
            ApplyStaffForState();
            _stats.RecordExerciseStarted();
            await PresentCurrentExerciseAsync(pitches, replay: false);
        }

        private async Task PresentCurrentExerciseAsync(
            IntervalEarTrainingLogic.IntervalPitches pitches,
            bool replay)
        {
            StopListening();
            _gate.PlaybackActive = true;
            SetState(replay && _state is IntervalSingingExerciseState.Correct or IntervalSingingExerciseState.Revealed
                ? _state
                : IntervalSingingExerciseState.Presenting);

            if (_mode == IntervalSingingTrainingMode.HearAndIdentify)
            {
                InstructionLabel.Text = IntervalSingingTrainingLogic.FormatIdentifyInstruction();
                StatusLabel.Text = "Listen…";
                if (!replay)
                    SetState(IntervalSingingExerciseState.WaitingForSinger);
                ApplyStaffForState();
                await PlayPitchesAsync(pitches, startOnly: false);
                _gate.PlaybackActive = false;
                if (_mode == IntervalSingingTrainingMode.HearAndIdentify
                    && !_succeeded
                    && !_revealed)
                {
                    SetState(IntervalSingingExerciseState.WaitingForSinger);
                    StatusLabel.Text = "Which interval was that?";
                }

                UpdateActionButtons();
                return;
            }

            bool startOnly = _mode == IntervalSingingTrainingMode.SingInterval;
            InstructionLabel.Text = _mode == IntervalSingingTrainingMode.ImitateInterval
                ? IntervalSingingTrainingLogic.FormatImitateInstruction(pitches)
                : IntervalSingingTrainingLogic.FormatSingInstruction(pitches);
            StatusLabel.Text = startOnly ? "Listen to the starting note…" : "Listen to the interval…";
            ApplyStaffForState();
            await PlayPitchesAsync(pitches, startOnly);
            _gate.PlaybackActive = false;

            if (_succeeded || _revealed)
            {
                UpdateActionButtons();
                return;
            }

            await BeginListeningAsync(pitches);
        }

        private async Task PlayPitchesAsync(IntervalEarTrainingLogic.IntervalPitches pitches, bool startOnly)
        {
            CancelPlayback();
            int generation = ++_playGeneration;
            _playCts = new CancellationTokenSource();
            var ct = _playCts.Token;

            int offset = _session.InstrumentTransposeOffset;
            double hz1 = TunerReferenceNoteCatalog.ConcertFrequencyHz(pitches.StartWrittenMidi, offset);
            double hz2 = TunerReferenceNoteCatalog.ConcertFrequencyHz(pitches.EndWrittenMidi, offset);
            double noteSeconds = IntervalEarTrainingLogic.ResolvePlaybackNoteSeconds(
                IntervalEarTrainingLogic.DefaultNoteDurationMs);
            IEnumerable<double> freqs = startOnly ? new[] { hz1 } : new[] { hz1, hz2 };

            try
            {
                await _player.PlayAsync(freqs, noteSeconds, gapSeconds: 0, volume: 0.4f, ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Utils.Log($"[SingingTraining] Play error: {ex}");
            }

            if (generation != _playGeneration)
                return;

            _ignoreUntilUtc = DateTime.UtcNow.AddMilliseconds(Math.Max(80, _session.CooldownMs));
        }

        private void CancelPlayback()
        {
            try
            {
                _playCts?.Cancel();
                _player.CancelPlayback();
            }
            catch { }

            _gate.PlaybackActive = false;
        }

        private async Task BeginListeningAsync(IntervalEarTrainingLogic.IntervalPitches pitches)
        {
            _singStabilizer.Reset();
            if (_mode == IntervalSingingTrainingMode.ImitateInterval)
            {
                int offset = _session.InstrumentTransposeOffset;
                _imitate = new IntervalSingingImitateTracker(
                    IntervalSingingTrainingLogic.ToConcertMidi(pitches.StartWrittenMidi, offset),
                    IntervalSingingTrainingLogic.ToConcertMidi(pitches.EndWrittenMidi, offset),
                    Math.Max(IntervalSingingTrainingLogic.DefaultToleranceCents, _session.Tolerance));
            }
            else
            {
                _imitate = null;
            }

            SetState(IntervalSingingExerciseState.WaitingForSinger);
            StatusLabel.Text = "Listening…";
            UpdateActionButtons();

            try
            {
                await _audio.EnsurePermissionAsync();
            }
            catch (Exception ex)
            {
                Utils.Log($"[SingingTraining] Mic permission ERROR: {ex}");
                StatusLabel.Text = "Microphone permission is required to listen.";
                return;
            }

            _pitchWindow.EnsureWindowSize(_session.PitchWindowSize);
            _pitchWindow.Reset();
            _gate.State = IntervalSingingExerciseState.WaitingForSinger;
            if (!_gate.TryStartCapture())
                return;

            try { _audio.StopCapture(); } catch { }
            if (!_audio.TryStartCapture(OnAudioBlock, out var error))
            {
                _gate.StopCapture();
                StatusLabel.Text = string.IsNullOrWhiteSpace(error)
                    ? "Could not open the microphone."
                    : error;
                Utils.Log($"[SingingTraining] TryStartCapture failed: {error}");
            }
        }

        private void StopListening()
        {
            try { _audio.StopCapture(); } catch { }
            _gate.StopCapture();
            _pitchWindow.Reset();
            _singStabilizer.Reset();
        }

        private void OnAudioBlock(short[] pcm16)
        {
            if (!_gate.ShouldCapture || !_gate.CaptureActive)
                return;

            var buf = new float[pcm16.Length];
            for (var i = 0; i < pcm16.Length; i++)
                buf[i] = pcm16[i] / 32768f;

            var rms = PitchDetectionService.ComputeRms(buf);
            bool ignoreAudio = DateTime.UtcNow < _ignoreUntilUtc;
            _pitchWindow.EnsureWindowSize(_session.PitchWindowSize);
            var ingest = _pitchWindow.Ingest(buf, rms, _session.RmsThreshold, ignoreAudio);

            if (ingest.Kind == PitchWindowIngestKind.BecameSilent)
            {
                _imitate?.NotifySilence();
                return;
            }

            if (ingest.Kind != PitchWindowIngestKind.WindowReady)
                return;

            _pitchWindow.HopHalf();

            var now = DateTime.UtcNow;
            if ((now - _lastProcess).TotalMilliseconds < _session.CooldownMs)
                return;

            lock (_processLock)
            {
                if (!_gate.ShouldCapture)
                    return;

                _lastProcess = now;
                double freq = PitchDetectionService.DetectPitchMcLeod(
                    _pitchWindow.Buffer, _pitchWindow.WindowSize, _session.SampleRate);
                freq *= Math.Pow(2, _session.PitchOffsetCents / 1200.0);

                MainThread.BeginInvokeOnMainThread(() => ApplyDetectedFrequency(freq));
            }
        }

        private void ApplyDetectedFrequency(double freq)
        {
            if (_exercise is not { } pitches)
                return;
            if (_state != IntervalSingingExerciseState.WaitingForSinger)
                return;
            if (!_gate.PageVisible || _gate.PlaybackActive)
                return;

            int offset = _session.InstrumentTransposeOffset;
            int tolerance = Math.Max(IntervalSingingTrainingLogic.DefaultToleranceCents, _session.Tolerance);

            if (_mode == IntervalSingingTrainingMode.ImitateInterval && _imitate is not null)
            {
                var result = _imitate.Observe(freq);
                if (result.Kind == ImitateObserveKind.Pending)
                    return;

                if (result.Kind == ImitateObserveKind.BothCorrect)
                {
                    CompleteSuccess(pitches, IntervalEarTrainingCatalog.GetName(pitches.Semitones));
                    return;
                }

                if (result.Kind == ImitateObserveKind.FirstCorrect)
                {
                    StatusLabel.Text = result.FormatFeedback();
                    return;
                }

                SetState(IntervalSingingExerciseState.Incorrect);
                _stats.RecordIncorrect();
                StatusLabel.Text = result.FormatFeedback();
                SetState(IntervalSingingExerciseState.WaitingForSinger);
                if (result.Kind == ImitateObserveKind.FirstIncorrect)
                {
                    _imitate = new IntervalSingingImitateTracker(
                        IntervalSingingTrainingLogic.ToConcertMidi(pitches.StartWrittenMidi, offset),
                        IntervalSingingTrainingLogic.ToConcertMidi(pitches.EndWrittenMidi, offset),
                        tolerance);
                }

                UpdateActionButtons();
                return;
            }

            int targetConcert = IntervalSingingTrainingLogic.ToConcertMidi(pitches.EndWrittenMidi, offset);
            var verdict = IntervalSingingTrainingLogic.JudgeDetectedPitch(freq, targetConcert, tolerance);
            var accepted = _singStabilizer.Observe(verdict);
            if (accepted is null)
                return;

            if (accepted == SungPitchVerdict.Correct)
            {
                string label = TunerReferenceNoteCatalog.FormatCompactWrittenLabel(pitches.EndWrittenMidi);
                CompleteSuccess(pitches, label);
                return;
            }

            SetState(IntervalSingingExerciseState.Incorrect);
            _stats.RecordIncorrect();
            StatusLabel.Text = IntervalSingingTrainingLogic.FormatPitchFeedback(accepted.Value, string.Empty);
            SetState(IntervalSingingExerciseState.WaitingForSinger);
            UpdateActionButtons();
        }

        private void CompleteSuccess(IntervalEarTrainingLogic.IntervalPitches pitches, string label)
        {
            StopListening();
            _succeeded = true;
            _stats.RecordCorrect(_revealed);
            SetState(_revealed
                ? IntervalSingingExerciseState.Revealed
                : IntervalSingingExerciseState.Correct);
            ApplyStaffForState();
            StatusLabel.Text = IntervalSingingTrainingLogic.FormatPitchFeedback(
                SungPitchVerdict.Correct, label);
            if (_mode == IntervalSingingTrainingMode.ImitateInterval)
                StatusLabel.Text = $"Correct — {IntervalEarTrainingCatalog.GetName(pitches.Semitones)}";
            UpdateActionButtons();
        }

        private async Task SubmitIdentifyAnswerAsync(int answeredSemitones)
        {
            if (_exercise is not { } pitches)
                return;
            if (_state is IntervalSingingExerciseState.Correct or IntervalSingingExerciseState.Revealed)
                return;
            if (_state != IntervalSingingExerciseState.WaitingForSinger
                && _state != IntervalSingingExerciseState.Incorrect)
                return;

            bool correct = IntervalEarTrainingLogic.IsAnswerCorrect(pitches.Semitones, answeredSemitones);
            RestoreAllIntervalFamilyColors();
            if (_intervalButtons.TryGetValue(answeredSemitones, out var answered))
            {
                answered.BackgroundColor = correct
                    ? IntervalEarTrainingButtonColors.CorrectBackground
                    : IntervalEarTrainingButtonColors.WrongBackground;
                answered.TextColor = IntervalEarTrainingButtonColors.FeedbackLabelText;
            }

            if (correct)
            {
                CompleteSuccess(pitches, IntervalEarTrainingCatalog.GetName(pitches.Semitones));
                return;
            }

            _stats.RecordIncorrect();
            SetState(IntervalSingingExerciseState.Incorrect);
            ApplyStaffForState();
            StatusLabel.Text = "Try again";
            SetState(IntervalSingingExerciseState.WaitingForSinger);
            UpdateActionButtons();
            await Task.CompletedTask;
        }

        private void ResetExercise(bool keepPitches)
        {
            StopListening();
            if (!keepPitches)
                _exercise = null;
            _revealed = false;
            _succeeded = false;
            _imitate = null;
            SetState(IntervalSingingExerciseState.Idle);
            ApplyStaffForState();
            UpdateActionButtons();
        }

        private void SetState(IntervalSingingExerciseState state)
        {
            _state = state;
            _gate.State = state;
        }

        private void UpdateActionButtons()
        {
            bool hasExercise = _exercise.HasValue;
            HearAgainButton.IsEnabled = hasExercise;
            RevealButton.IsEnabled = hasExercise && !_succeeded && !_revealed;
        }

        private void ApplyStaffForState()
        {
            if (!IntervalSingingTrainingLogic.ShouldShowNotation(_state)
                || _exercise is not { } pitches)
            {
                HideStaffReveal();
                return;
            }

            ShowStaffForPitches(pitches);
        }

        private void HideStaffReveal()
            => EnsureEmptyStaffPanel();

        private void EnsureStaffDrawable()
        {
            if (_staffDrawable != null)
            {
                _staffDrawable.SingleStaffLayout = true;
                _staffDrawable.OmitStaffHeader = true;
                if (StaffGraphicsView.Drawable != _staffDrawable)
                    StaffGraphicsView.Drawable = _staffDrawable;
                return;
            }

            _staffDrawable = new StaffDrawable(_session, _theme, _safeArea)
            {
                SingleStaffLayout = true,
                OmitStaffHeader = true,
                AvailableHeight = (float)Math.Max(96, StaffGraphicsView.HeightRequest > 0
                    ? StaffGraphicsView.HeightRequest
                    : 140),
            };
            StaffGraphicsView.Drawable = _staffDrawable;
        }

        private void EnsureEmptyStaffPanel()
        {
            EnsureStaffDrawable();
            ApplyStaffKey();
            _staffDrawable!.UpperNotes = new List<GeneratedNote>();
            _staffDrawable.LowerNotes = new List<GeneratedNote>();
            _staffDrawable.UpperBarBeats = new List<double>();
            _staffDrawable.LowerBarBeats = new List<double>();
            _staffDrawable.UpperNoteStates = Array.Empty<StaffNoteState>();
            _staffDrawable.ActiveNoteIndex = -1;
            _staffDrawable.UpperHasEndBar = false;
            _staffDrawable.InvalidateLayoutCache();
            StaffRevealBorder.IsVisible = true;
            ApplyLandscapeLayout();
            StaffGraphicsView.Invalidate();
        }

        private void ShowStaffForPitches(IntervalEarTrainingLogic.IntervalPitches pitches)
        {
            EnsureStaffDrawable();
            ApplyStaffKey();

            var notes = IntervalEarTrainingNotation.BuildDisplayNotes(
                pitches,
                IntervalEarTrainingNotation.StaffDisplayKey,
                IntervalEarTrainingNotation.StaffDisplayScale);
            _staffDrawable!.SingleStaffLayout = true;
            _staffDrawable.OmitStaffHeader = true;
            _staffDrawable.InvalidateLayoutCache();
            _staffDrawable.UpperNotes = notes;
            _staffDrawable.LowerNotes = new List<GeneratedNote>();
            _staffDrawable.UpperBarBeats = IntervalEarTrainingNotation.BuildBarBeats();
            _staffDrawable.LowerBarBeats = new List<double>();
            _staffDrawable.IsUpperActive = true;
            _staffDrawable.UpperAlpha = 1f;
            _staffDrawable.LowerAlpha = 0f;
            _staffDrawable.UpperHasEndBar = false;
            _staffDrawable.ActiveNoteIndex = -1;
            _staffDrawable.UpperNoteStates = notes
                .Select(_ => StaffNoteState.Pending)
                .ToArray();
            StaffGraphicsView.Drawable = _staffDrawable;
            StaffRevealBorder.IsVisible = true;
            ApplyLandscapeLayout();
            StaffGraphicsView.Invalidate();
        }

        private void ApplyStaffKey()
        {
            if (_staffDrawable == null)
                return;
            _staffDrawable.NotationKeyOverride = IntervalEarTrainingNotation.StaffDisplayKey;
            _staffDrawable.NotationScaleOverride = IntervalEarTrainingNotation.StaffDisplayScale;
        }

        private void SyncStaffLayout()
        {
            if (_staffDrawable == null)
                return;

            float h = (float)Math.Max(64, StaffGraphicsView.Height > 1
                ? StaffGraphicsView.Height
                : StaffRevealBorder.Height > 1
                    ? StaffRevealBorder.Height
                    : 96);
            _staffDrawable.SingleStaffLayout = true;
            _staffDrawable.OmitStaffHeader = true;
            ApplyStaffKey();
            _staffDrawable.AvailableHeight = h;
            _staffDrawable.InvalidateLayoutCache();
            StaffGraphicsView.Invalidate();
        }
    }
}
