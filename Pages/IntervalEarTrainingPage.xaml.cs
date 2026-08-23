using System.ComponentModel;
using System.Globalization;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;
using musicmate.Utilities;

namespace musicmate.Pages
{
    public partial class IntervalEarTrainingPage : ContentPage
    {
        private readonly NoteSessionService _session;
        private readonly ThemeService _theme;
        private readonly IAudioPlaybackService _player;
        private readonly IOrientationService _orientation;
        private readonly ISafeAreaService? _safeArea;

        private CancellationTokenSource? _playCts;
        private CancellationTokenSource? _feedbackCts;
        private IntervalEarTrainingLogic.IntervalPitches? _lastPitches;
        /// <summary>
        /// Fixed first pitch for explore-mode interval buttons.
        /// Set only by Play Random Interval; never re-randomized by an interval-button tap.
        /// </summary>
        private int? _referenceStartWrittenMidi;
        /// <summary>
        /// Unanswered quiz (notes hidden), revealed quiz (notes shown after a correct
        /// answer), or manual interval playback (notes shown immediately).
        /// </summary>
        private IntervalEarTrainingInteraction _interaction = IntervalEarTrainingInteraction.ManualPlayback;
        private int _expectedSemitones;
        private bool _showingFeedback;
        private int _noteDurationMs = IntervalEarTrainingLogic.DefaultNoteDurationMs;
        private IntervalDirectionMode _directionMode = IntervalEarTrainingLogic.DefaultDirectionMode;
        private readonly Dictionary<int, Button> _intervalButtons = new();
        private int _playGeneration;
        private StaffDrawable? _staffDrawable;
        private Button PlayRandomButton = null!;
        private Button PlayAgainButton = null!;
        /// <summary>Semitone of the interval button currently outlined for explore-mode demo, if any.</summary>
        private int? _demoHighlightSemitones;
        /// <summary>
        /// Explicit start note for interval playback. null means Random
        /// (existing Play Random / explore-reference behavior).
        /// </summary>
        private int? _selectedStartWrittenMidi;
        private int _transposeOffsetForStartNote;
        private int _StartNotePickerSyncSuppress;
        private readonly List<int> _StartNoteMidis = new();
        private int _instrumentPickerSyncSuppress;

        public IntervalEarTrainingPage()
        {
            InitializeComponent();
            Utils.DisableIosSafeArea(this);
            _session = ServiceHelper.GetService<NoteSessionService>()!;
            _theme = ServiceHelper.GetService<ThemeService>()!;
            _player = ServiceHelper.GetService<IAudioPlaybackService>()!;
            _orientation = ServiceHelper.GetService<IOrientationService>()!;
            _safeArea = ServiceHelper.GetService<ISafeAreaService>();

            BuildIntervalButtons();
            LoadPreferences();
            InitInstrumentPicker();
            _transposeOffsetForStartNote = _session.InstrumentTransposeOffset;
            InitStartNotePicker();
            UpdatePlayAgainEnabled();
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
                Utils.Log($"[EarTraining] Navigate to Music ERROR: {ex.Message}");
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            _orientation?.ForceLandscape();
            LoadPreferences();
            _session.PropertyChanged -= OnSessionPropertyChanged;
            _session.PropertyChanged += OnSessionPropertyChanged;
            SyncInstrumentPickerFromSession();
            RefreshStartNotePicker(preserveConcertPitch: false);
            ClearDemoIntervalHighlight();
            ApplyLandscapeLayout();
            EnsureEmptyStaffPanel();
            if (Shell.Current is AppShell shell)
                shell.EnsureFlyoutItemsVisiblePublic();
        }

        protected override void OnDisappearing()
        {
            CancelPlayback();
            _feedbackCts?.Cancel();
            _session.PropertyChanged -= OnSessionPropertyChanged;
            ClearDemoIntervalHighlight();
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

        /// <summary>
        /// Landscape: keep the staff a usable width; let the interval grid fill the rest.
        /// Do not force PageRoot/IntervalsSection WidthRequest — that ignored Padding and
        /// clipped the left column of buttons against the screen edge.
        /// </summary>
        private void ApplyLandscapeLayout()
        {
            ApplySafeAreaPadding();

            double pageW = Width;
            if (pageW < 8)
                return;

            // Clear any prior forced widths so Padding / Fill layout applies.
            if (PageRoot.WidthRequest >= 0)
                PageRoot.WidthRequest = -1;
            if (IntervalsSection.WidthRequest >= 0)
                IntervalsSection.WidthRequest = -1;

            double inner = Math.Max(200, pageW - PageRoot.Padding.Left - PageRoot.Padding.Right);

            // Keep staff readable; leave interval columns wide enough for full one-line labels.
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
            // Tiny edge pad only. Full cutout insets reserved the camera strip as empty space.
            const double edge = 4;
            PageRoot.Padding = new Thickness(
                edge + Math.Min(insets.Left, 8),
                1,
                edge + Math.Min(insets.Right, 8),
                Math.Max(2, Math.Min(insets.Bottom, 6)));
        }

        private void BuildIntervalButtons()
        {
            ClearDemoIntervalHighlight();
            IntervalButtonsHost.Children.Clear();
            IntervalButtonsHost.RowDefinitions.Clear();
            _intervalButtons.Clear();

            // 3 columns × 5 rows:
            // [0,0] Play Random, [1,0] Play Again, [2,0] unison,
            // then intervals 1–12 fill rows 1–4 in order.
            const int columns = 3;
            const int rows = 5;
            for (int r = 0; r < rows; r++)
                IntervalButtonsHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });

            var playStyle = (Style)Resources["EarTrainButton"];
            var gridStyle = (Style)Resources["EarTrainIntervalButton"];

            PlayRandomButton = CreateGridButton("Play Random", playStyle, "Play a random interval for quiz");
            ApplyPlayRandomChrome(PlayRandomButton);
            PlayRandomButton.Clicked += OnPlayRandomClicked;
            Grid.SetColumn(PlayRandomButton, 0);
            Grid.SetRow(PlayRandomButton, 0);
            IntervalButtonsHost.Children.Add(PlayRandomButton);

            PlayAgainButton = CreateGridButton("Play Again", playStyle, "Play the last interval again");
            ApplyPlayButtonChrome(PlayAgainButton);
            PlayAgainButton.IsEnabled = false;
            PlayAgainButton.Clicked += OnPlayAgainClicked;
            Grid.SetColumn(PlayAgainButton, 1);
            Grid.SetRow(PlayAgainButton, 0);
            IntervalButtonsHost.Children.Add(PlayAgainButton);

            var intervals = IntervalEarTrainingCatalog.Intervals;
            for (int i = 0; i < intervals.Count; i++)
            {
                int semitones = intervals[i].Semitones;
                // Cells 0–1 are Play Random / Play Again; unison starts at cell 2 → [2,0].
                int cell = semitones + 2;
                int col = cell % columns;
                int row = cell / columns;

                string label = IntervalEarTrainingCatalog.FormatButtonLabel(semitones);
                var btn = CreateGridButton(label, gridStyle, $"Interval {label}");
                ApplyFamilyColor(btn, semitones);
                int captured = semitones;
                btn.Clicked += async (_, _) => await OnIntervalClickedAsync(captured);

                Grid.SetColumn(btn, col);
                Grid.SetRow(btn, row);
                IntervalButtonsHost.Children.Add(btn);
                _intervalButtons[semitones] = btn;
            }
        }

        private static void ApplyPlayButtonChrome(Button button)
        {
            button.BorderWidth = IntervalEarTrainingButtonColors.PlayButtonBorderWidth;
            button.BorderColor = IntervalEarTrainingButtonColors.PlayButtonBorder;
        }

        private static void ApplyPlayRandomChrome(Button button)
        {
            button.BorderWidth = IntervalEarTrainingButtonColors.PlayButtonBorderWidth;
            button.BorderColor = IntervalEarTrainingButtonColors.PlayRandomBorder;
        }

        private static void ApplyFamilyColor(Button button, int semitones)
        {
            button.BackgroundColor = IntervalEarTrainingButtonColors.FamilyBackground(semitones);
            button.TextColor = IntervalEarTrainingButtonColors.LabelText;
            button.BorderWidth = 0;
        }

        private void ClearDemoIntervalHighlight()
        {
            if (_demoHighlightSemitones is int s
                && _intervalButtons.TryGetValue(s, out var btn))
            {
                btn.BorderWidth = 0;
            }

            _demoHighlightSemitones = null;
        }

        /// <summary>
        /// Bold red outline on the interval being demonstrated; family fill stays unchanged.
        /// </summary>
        private void SetDemoIntervalHighlight(int semitones)
        {
            ClearDemoIntervalHighlight();
            if (!_intervalButtons.TryGetValue(semitones, out var btn))
                return;

            btn.BorderColor = IntervalEarTrainingButtonColors.DemoHighlightBorder;
            btn.BorderWidth = IntervalEarTrainingButtonColors.DemoHighlightBorderWidth;
            _demoHighlightSemitones = semitones;
        }

        private void RestoreAllIntervalFamilyColors()
        {
            ClearDemoIntervalHighlight();
            foreach (var (semitones, btn) in _intervalButtons)
                ApplyFamilyColor(btn, semitones);
        }

        /// <summary>
        /// Correct/wrong feedback temporarily overrides family colors on the involved buttons.
        /// </summary>
        private void ApplyAnswerFeedbackColors(bool correct, int answeredSemitones, int expectedSemitones)
        {
            if (_intervalButtons.TryGetValue(answeredSemitones, out var answered))
            {
                answered.BackgroundColor = correct
                    ? IntervalEarTrainingButtonColors.CorrectBackground
                    : IntervalEarTrainingButtonColors.WrongBackground;
                answered.TextColor = IntervalEarTrainingButtonColors.FeedbackLabelText;
            }

            if (!correct
                && answeredSemitones != expectedSemitones
                && _intervalButtons.TryGetValue(expectedSemitones, out var expected))
            {
                expected.BackgroundColor = IntervalEarTrainingButtonColors.CorrectBackground;
                expected.TextColor = IntervalEarTrainingButtonColors.FeedbackLabelText;
            }
        }

        private static Button CreateGridButton(string text, Style style, string semanticDescription)
        {
            var btn = new Button
            {
                Text = text,
                Style = style,
            };
            SemanticProperties.SetDescription(btn, semanticDescription);
            return btn;
        }

        private Task ScrollToIntervalsCenteredAsync()
            => Task.CompletedTask;

        private Task ScrollToPlayControlsAsync()
            => Task.CompletedTask;

        private void LoadPreferences()
        {
            _noteDurationMs = IntervalEarTrainingLogic.ClampNoteDurationMs(
                SessionPreferences.Get(
                    IntervalEarTrainingLogic.NoteDurationPreferenceKey,
                    IntervalEarTrainingLogic.DefaultNoteDurationMs));
            DurationSlider.Value = _noteDurationMs;
            UpdateDurationLabels();

            _directionMode = IntervalEarTrainingLogic.ParseDirectionMode(
                SessionPreferences.Get(
                    IntervalEarTrainingLogic.DirectionPreferenceKey,
                    IntervalEarTrainingLogic.DefaultDirectionMode.ToString()));
            UpdateDirectionButtonVisuals();
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
                || e.PropertyName == nameof(NoteSessionService.HighestNote)
                || e.PropertyName == nameof(NoteSessionService.ChildLevel))
            {
                RefreshStartNotePicker(preserveConcertPitch: false);
            }
        }

        private void InitStartNotePicker()
        {
            _selectedStartWrittenMidi = IntervalEarTrainingLogic.ParsePersistedStartNote(
                SessionPreferences.Get(
                    IntervalEarTrainingLogic.StartNotePreferenceKey,
                    IntervalEarTrainingLogic.RandomStartNoteToken));
            RefreshStartNotePicker(preserveConcertPitch: false);
        }

        /// <summary>
        /// Rebuilds picker items from the current written range and instrument.
        /// When <paramref name="preserveConcertPitch"/> is true, remaps the selected
        /// written MIDI through concert pitch for the new transposition.
        /// </summary>
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

            var range = TryResolveWrittenRange();
            _StartNoteMidis.Clear();
            if (range is { } bounds)
            {
                _StartNoteMidis.AddRange(IntervalEarTrainingLogic.BuildStartNoteMidis(
                    bounds.Low, bounds.High, _session.AvailableInstrumentMidis));
            }

            if (_selectedStartWrittenMidi is int midi && !_StartNoteMidis.Contains(midi))
                _selectedStartWrittenMidi = null;

            PersistStartNoteSelection();
            ApplyStartNotePickerItems();
            SyncExploreReferenceFromStartNotePicker();
        }

        private void PersistStartNoteSelection()
        {
            SessionPreferences.Set(
                IntervalEarTrainingLogic.StartNotePreferenceKey,
                IntervalEarTrainingLogic.PersistStartNote(_selectedStartWrittenMidi));
        }

        private void ApplyStartNotePickerItems()
        {
            if (StartNotePicker == null)
                return;

            var items = new List<string>(_StartNoteMidis.Count + 1)
            {
                IntervalEarTrainingLogic.RandomStartNoteToken,
            };
            foreach (int midi in _StartNoteMidis)
                items.Add(TunerReferenceNoteCatalog.FormatCompactWrittenLabel(midi));

            int targetIndex = 0;
            if (_selectedStartWrittenMidi is int selected)
            {
                int midiIdx = _StartNoteMidis.IndexOf(selected);
                if (midiIdx >= 0)
                    targetIndex = midiIdx + 1;
            }

            _StartNotePickerSyncSuppress++;
            try
            {
                StartNotePicker.ItemsSource = items;
                StartNotePicker.SelectedIndex = targetIndex;
            }
            finally
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_StartNotePickerSyncSuppress > 0)
                        _StartNotePickerSyncSuppress--;
                });
            }
        }

        private void SyncExploreReferenceFromStartNotePicker()
        {
            if (_selectedStartWrittenMidi is int midi)
                _referenceStartWrittenMidi = midi;
        }

        private void OnStartNotePickerChanged(object? sender, EventArgs e)
        {
            if (_StartNotePickerSyncSuppress > 0)
                return;

            int idx = StartNotePicker.SelectedIndex;
            if (idx <= 0)
                _selectedStartWrittenMidi = null;
            else if (idx - 1 < _StartNoteMidis.Count)
                _selectedStartWrittenMidi = _StartNoteMidis[idx - 1];
            else
                _selectedStartWrittenMidi = null;

            PersistStartNoteSelection();
            SyncExploreReferenceFromStartNotePicker();
            // Do not touch the staff — a hidden quiz answer must stay hidden.
        }

        private (int Low, int High)? TryResolveWrittenRange()
        {
            int lowMidi = NoteSessionService.NoteNameToMidi(_session.LowestNote);
            int highMidi = NoteSessionService.NoteNameToMidi(_session.HighestNote);
            if (lowMidi <= 0 || highMidi <= 0)
                return null;
            if (highMidi < lowMidi)
                (lowMidi, highMidi) = (highMidi, lowMidi);
            return (lowMidi, highMidi);
        }

        private void OnDurationChanged(object? sender, ValueChangedEventArgs e)
        {
            ClearDemoIntervalHighlight();
            int ms = IntervalEarTrainingLogic.ClampNoteDurationMs((int)Math.Round(e.NewValue));
            if (ms == _noteDurationMs)
            {
                UpdateDurationLabels();
                return;
            }

            _noteDurationMs = ms;
            SessionPreferences.Set(IntervalEarTrainingLogic.NoteDurationPreferenceKey, ms);
            UpdateDurationLabels();
        }

        private void UpdateDurationLabels()
        {
            int ms = _noteDurationMs;
            DurationMsLabel.Text = $"{ms.ToString(CultureInfo.InvariantCulture)} ms";

            // BPM from quarter-note equivalence: 60000 / durationMs.
            // Stored 0 ms still plays at MinAudibleNoteDurationMs — use that for BPM too.
            int bpmMs = ms <= 0
                ? IntervalEarTrainingLogic.MinAudibleNoteDurationMs
                : ms;
            int bpm = (int)Math.Round(60000.0 / bpmMs);
            DurationBpmLabel.Text = $"{bpm.ToString(CultureInfo.InvariantCulture)} bpm";
        }

        private void OnDirectionAscendingClicked(object? sender, EventArgs e)
            => SetDirectionMode(IntervalDirectionMode.Ascending);

        private void OnDirectionDescendingClicked(object? sender, EventArgs e)
            => SetDirectionMode(IntervalDirectionMode.Descending);

        private void OnDirectionRandomClicked(object? sender, EventArgs e)
            => SetDirectionMode(IntervalDirectionMode.Random);

        private void SetDirectionMode(IntervalDirectionMode mode)
        {
            if (_showingFeedback)
                return;

            ClearDemoIntervalHighlight();
            _directionMode = mode;
            SessionPreferences.Set(
                IntervalEarTrainingLogic.DirectionPreferenceKey,
                mode.ToString());
            UpdateDirectionButtonVisuals();
        }

        private void UpdateDirectionButtonVisuals()
        {
            ApplyDirectionButtonState(DirectionAscendingButton, _directionMode == IntervalDirectionMode.Ascending);
            ApplyDirectionButtonState(DirectionDescendingButton, _directionMode == IntervalDirectionMode.Descending);
            ApplyDirectionButtonState(DirectionRandomButton, _directionMode == IntervalDirectionMode.Random);
        }

        private static void ApplyDirectionButtonState(Button button, bool selected)
        {
            button.Opacity = selected ? 1.0 : 0.55;
            button.FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None;
            SemanticProperties.SetHint(button, selected ? "Selected" : "Not selected");
        }

        private void UpdatePlayAgainEnabled()
            => PlayAgainButton.IsEnabled = _lastPitches.HasValue && !_showingFeedback;

        private void UpdateStatus(string message)
            => StatusLabel.Text = message;

        private async void OnPlayRandomClicked(object? sender, EventArgs e)
        {
            ClearDemoIntervalHighlight();
            await PlayRandomQuizAsync();
        }

        private async void OnPlayAgainClicked(object? sender, EventArgs e)
        {
            ClearDemoIntervalHighlight();
            await PlayAgainAsync();
        }

        private async Task OnIntervalClickedAsync(int semitones)
        {
            if (_showingFeedback)
                return;

            ClearDemoIntervalHighlight();

            if (_interaction == IntervalEarTrainingInteraction.UnansweredQuiz)
            {
                await SubmitAnswerAsync(semitones);
                return;
            }

            await PlayDirectIntervalAsync(semitones);
        }

        private async Task PlayDirectIntervalAsync(int semitones)
        {
            var range = await TryResolveWrittenRangeAsync();
            if (range is null)
                return;
            var (low, high) = range.Value;

            IntervalEarTrainingLogic.IntervalPitches pitches;
            int? start = _selectedStartWrittenMidi ?? _referenceStartWrittenMidi;
            if (start is not int reference)
            {
                await DisplayAlertAsync(
                    "Choose a starting note",
                    "Pick a Start note in the header, or tap Play Random first. That picks a starting note; then each interval button uses the same starting note until you tap Play Random again.",
                    "OK");
                return;
            }

            if (!IntervalEarTrainingLogic.TryBuildIntervalFromReference(
                    reference, low, high, semitones, _directionMode, Random.Shared, out pitches))
            {
                string startLabel = TunerReferenceNoteCatalog.FormatCompactWrittenLabel(reference);
                string direction = _directionMode == IntervalDirectionMode.Descending
                    ? "descending"
                    : _directionMode == IntervalDirectionMode.Random
                        ? "in this direction"
                        : "ascending";
                await DisplayAlertAsync(
                    "Interval unavailable",
                    $"{startLabel} cannot form a {IntervalEarTrainingCatalog.GetName(semitones)} ({semitones} semitones) {direction} within your Settings note range.",
                    "OK");
                return;
            }

            _lastPitches = pitches;
            _interaction = IntervalEarTrainingInteraction.ManualPlayback;
            UpdatePlayAgainEnabled();
            UpdateStatus($"Playing {FormatPlayedLabel(pitches)}…");
            await PlayPitchesAsync(pitches);
            ApplyStaffForCurrentInteraction();
            SetDemoIntervalHighlight(semitones);
            await ScrollToIntervalsCenteredAsync();
            UpdateStatus("Play Random for quiz. Tap any interval to hear it.");
        }

        private async Task PlayRandomQuizAsync()
        {
            if (_showingFeedback)
                return;

            RestoreAllIntervalFamilyColors();
            // Drop leftover noteheads before the new quiz interval is chosen.
            HideStaffReveal();

            var range = await TryResolveWrittenRangeAsync();
            if (range is null)
            {
                _interaction = IntervalEarTrainingInteraction.ManualPlayback;
                return;
            }
            var (low, high) = range.Value;

            IntervalEarTrainingLogic.IntervalPitches pitches;
            if (_selectedStartWrittenMidi is int startMidi)
            {
                if (!IntervalEarTrainingLogic.TryPickRandomIntervalFromStart(
                        startMidi, low, high, _directionMode, Random.Shared, out pitches))
                {
                    _interaction = IntervalEarTrainingInteraction.ManualPlayback;
                    string startLabel = TunerReferenceNoteCatalog.FormatCompactWrittenLabel(startMidi);
                    await DisplayAlertAsync(
                        "Interval unavailable",
                        $"No interval from {startLabel} fits within your Settings note range in this direction.",
                        "OK");
                    return;
                }
            }
            else if (!IntervalEarTrainingLogic.TryPickRandomInterval(
                    low, high, _directionMode, Random.Shared, out pitches))
            {
                _interaction = IntervalEarTrainingInteraction.ManualPlayback;
                await DisplayAlertAsync(
                    "Range too narrow",
                    "Your Settings note range cannot fit any interval. Check Lowest note and Highest note in Settings.",
                    "OK");
                return;
            }

            // Explore-mode interval buttons keep this start until Play Random (Random Start note)
            // or until the header Start-note picker changes.
            _referenceStartWrittenMidi = pitches.StartWrittenMidi;
            _lastPitches = pitches;
            _expectedSemitones = pitches.Semitones;
            _interaction = IntervalEarTrainingInteraction.UnansweredQuiz;
            ApplyStaffForCurrentInteraction();
            UpdatePlayAgainEnabled();
            UpdateStatus("Listen… then tap the interval you heard.");
            await PlayPitchesAsync(pitches);
            await ScrollToIntervalsCenteredAsync();
            if (_interaction == IntervalEarTrainingInteraction.UnansweredQuiz)
                UpdateStatus("Which interval was that? Tap your answer (or Play Again).");
        }

        private async Task PlayAgainAsync()
        {
            if (_showingFeedback)
                return;

            if (_lastPitches is not { } pitches)
            {
                await DisplayAlertAsync("Nothing to replay", "Play an interval first.", "OK");
                return;
            }

            _expectedSemitones = pitches.Semitones;
            ApplyStaffForCurrentInteraction();
            UpdatePlayAgainEnabled();

            if (_interaction == IntervalEarTrainingInteraction.UnansweredQuiz)
                UpdateStatus("Playing again… then tap your answer.");
            else
                UpdateStatus($"Playing {FormatPlayedLabel(pitches)}…");
            await PlayPitchesAsync(pitches);
            await ScrollToIntervalsCenteredAsync();
            if (_interaction == IntervalEarTrainingInteraction.UnansweredQuiz)
                UpdateStatus("Which interval was that? Tap your answer (or Play Again).");
            else
                UpdateStatus("Play Random for quiz. Tap any interval to hear it.");
        }

        private async Task SubmitAnswerAsync(int answeredSemitones)
        {
            if (_interaction != IntervalEarTrainingInteraction.UnansweredQuiz || _showingFeedback)
                return;

            bool correct = IntervalEarTrainingLogic.IsAnswerCorrect(_expectedSemitones, answeredSemitones);
            _interaction = correct
                ? IntervalEarTrainingInteraction.RevealedQuiz
                : IntervalEarTrainingInteraction.UnansweredQuiz;
            ApplyStaffForCurrentInteraction();
            UpdatePlayAgainEnabled();

            string expectedLabel = _lastPitches is { } last
                ? FormatPlayedLabel(last)
                : IntervalEarTrainingCatalog.FormatButtonLabel(_expectedSemitones);
            string answeredLabel = IntervalEarTrainingCatalog.FormatButtonLabel(answeredSemitones);

            await ShowFeedbackAsync(correct, expectedLabel, answeredLabel, answeredSemitones, _expectedSemitones);
            if (_interaction == IntervalEarTrainingInteraction.UnansweredQuiz)
                UpdateStatus("Which interval was that? Tap your answer (or Play Again).");
            else
                UpdateStatus("Play Random for quiz. Tap any interval to hear it.");
        }

        private static string FormatPlayedLabel(IntervalEarTrainingLogic.IntervalPitches pitches)
        {
            string name = IntervalEarTrainingCatalog.FormatButtonLabel(pitches.Semitones);
            if (pitches.Semitones == 0)
                return name;
            return $"{name} ({IntervalEarTrainingLogic.FormatDirectionLabel(pitches.IsAscending)})";
        }

        private async Task ShowFeedbackAsync(
            bool correct,
            string expectedLabel,
            string answeredLabel,
            int answeredSemitones,
            int expectedSemitones)
        {
            _showingFeedback = true;
            SetControlsEnabled(false);
            UpdatePlayAgainEnabled();
            ApplyAnswerFeedbackColors(correct, answeredSemitones, expectedSemitones);

            FeedbackTitleLabel.Text = correct ? "Nice!" : "Oops!";
            FeedbackDetailLabel.Text = correct
                ? $"Yes — {expectedLabel}"
                : $"You chose {answeredLabel}. It was {expectedLabel}.";
            FeedbackEmojiLabel.Text = correct ? "🌟" : "😄";
            FeedbackEmojiLabel.IsVisible = true;
            // Optional artwork later: MauiImage ear_train_success.png / ear_train_fail.png.
            FeedbackImage.Source = null;
            FeedbackImage.IsVisible = false;

            SemanticProperties.SetDescription(FeedbackOverlay,
                correct ? $"Correct: {expectedLabel}" : $"Incorrect. Expected {expectedLabel}");

            FeedbackOverlay.IsVisible = true;
            FeedbackOverlay.Opacity = 0;
            await FeedbackOverlay.FadeToAsync(1, 120);

            if (!correct)
                await ScrollToPlayControlsAsync();

            SetFeedbackWaitVisible(true);
            _feedbackCts?.Cancel();
            _feedbackCts = new CancellationTokenSource();
            try
            {
                await Task.WhenAll(
                    FeedbackWaitProgress.ProgressTo(1, 1600, Easing.Linear),
                    Task.Delay(1600, _feedbackCts.Token));
            }
            catch (OperationCanceledException) { }

            SetFeedbackWaitVisible(false);

            if (correct)
                await ScrollToPlayControlsAsync();

            try
            {
                await FeedbackOverlay.FadeToAsync(0, 150);
            }
            catch { }

            FeedbackOverlay.IsVisible = false;
            RestoreAllIntervalFamilyColors();
            _showingFeedback = false;
            SetControlsEnabled(true);
            UpdatePlayAgainEnabled();
        }

        private void SetFeedbackWaitVisible(bool visible)
        {
            FeedbackWaitIndicator.IsVisible = visible;
            FeedbackWaitIndicator.IsRunning = visible;
            FeedbackWaitProgress.IsVisible = visible;
            if (visible)
                FeedbackWaitProgress.Progress = 0;
        }

        private void SetControlsEnabled(bool enabled)
        {
            PlayRandomButton.IsEnabled = enabled;
            InstrumentPicker.IsEnabled = enabled;
            if (StartNotePicker != null)
                StartNotePicker.IsEnabled = enabled;
            DurationSlider.IsEnabled = enabled;
            DirectionAscendingButton.IsEnabled = enabled;
            DirectionDescendingButton.IsEnabled = enabled;
            DirectionRandomButton.IsEnabled = enabled;
            foreach (var btn in _intervalButtons.Values)
                btn.IsEnabled = enabled;
        }

        private async Task<(int Low, int High)?> TryResolveWrittenRangeAsync()
        {
            var range = TryResolveWrittenRange();
            if (range is null)
            {
                await DisplayAlertAsync("Note range", "Could not read Lowest/Highest notes from Settings.", "OK");
                return null;
            }

            return range;
        }

        private async Task PlayPitchesAsync(IntervalEarTrainingLogic.IntervalPitches pitches)
        {
            CancelPlayback();
            int generation = ++_playGeneration;
            _playCts = new CancellationTokenSource();
            var ct = _playCts.Token;

            int offset = _session.InstrumentTransposeOffset;
            double hz1 = NoteSessionService.MidiToFreqPublic(
                TunerReferenceNoteCatalog.ToConcertMidi(pitches.StartWrittenMidi, offset));
            double hz2 = NoteSessionService.MidiToFreqPublic(
                TunerReferenceNoteCatalog.ToConcertMidi(pitches.EndWrittenMidi, offset));

            double noteSeconds = IntervalEarTrainingLogic.ResolvePlaybackNoteSeconds(_noteDurationMs);
            try
            {
                // gapSeconds = 0: second note starts as soon as the first finishes.
                await _player.PlayAsync(new[] { hz1, hz2 }, noteSeconds, gapSeconds: 0, volume: 0.4f, ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Utils.Log($"[EarTraining] Play error: {ex}");
            }

            // Ignore completion of superseded playback requests.
            if (generation != _playGeneration)
                return;
        }

        private void CancelPlayback()
        {
            try
            {
                _playCts?.Cancel();
                _player.CancelPlayback();
            }
            catch { }
        }

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

        /// <summary>
        /// Clears sounded notes but keeps the side panel (empty staff lines) so layout stays balanced.
        /// </summary>
        private void HideStaffReveal()
        {
            EnsureEmptyStaffPanel();
        }

        private void ApplyStaffForCurrentInteraction()
        {
            if (!IntervalEarTrainingLogic.ShouldShowIntervalNotes(_interaction)
                || _lastPitches is not { } pitches)
            {
                HideStaffReveal();
                return;
            }

            ShowStaffForPitches(pitches);
        }

        private void EnsureEmptyStaffPanel()
        {
            EnsureStaffDrawable();
            ApplyEarTrainingStaffKey();
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

        /// <summary>
        /// Engraves the exact stored pitches that were sounded. Does not re-pick intervals.
        /// Always notates in C Major on this page (no inherited Music/Settings key signature).
        /// </summary>
        private void ShowStaffForPitches(IntervalEarTrainingLogic.IntervalPitches pitches)
        {
            EnsureStaffDrawable();
            ApplyEarTrainingStaffKey();

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

        /// <summary>Interval Ear Training staff is always C Major — never the session key.</summary>
        private void ApplyEarTrainingStaffKey()
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
            ApplyEarTrainingStaffKey();
            _staffDrawable.AvailableHeight = h;
            _staffDrawable.InvalidateLayoutCache();
            StaffGraphicsView.Invalidate();
        }
    }
}
