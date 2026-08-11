using System.Globalization;
using musicmate.Services;
using musicmate.Utilities;

namespace musicmate.Pages
{
    public partial class IntervalEarTrainingPage : ContentPage
    {
        private readonly NoteSessionService _session;
        private readonly IAudioPlaybackService _player;
        private readonly IOrientationService _orientation;
        private readonly ISafeAreaService? _safeArea;

        private CancellationTokenSource? _playCts;
        private CancellationTokenSource? _feedbackCts;
        private IntervalEarTrainingLogic.IntervalPitches? _lastPitches;
        private bool _awaitingAnswer;
        private int _expectedSemitones;
        private bool _showingFeedback;
        private int _noteDurationMs = IntervalEarTrainingLogic.DefaultNoteDurationMs;
        private IntervalDirectionMode _directionMode = IntervalEarTrainingLogic.DefaultDirectionMode;
        private readonly Dictionary<int, Button> _intervalButtons = new();
        private int _playGeneration;

        public IntervalEarTrainingPage()
        {
            InitializeComponent();
            Utils.DisableIosSafeArea(this);
            _session = ServiceHelper.GetService<NoteSessionService>()!;
            _player = ServiceHelper.GetService<IAudioPlaybackService>()!;
            _orientation = ServiceHelper.GetService<IOrientationService>()!;
            _safeArea = ServiceHelper.GetService<ISafeAreaService>();

            BuildIntervalButtons();
            LoadPreferences();
            UpdatePlayAgainEnabled();
            ApplySafeAreaPadding();
            SizeChanged += (_, _) => ApplySafeAreaPadding();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            _orientation?.ForceLandscape();
            LoadPreferences();
            ApplySafeAreaPadding();
        }

        protected override void OnDisappearing()
        {
            CancelPlayback();
            _feedbackCts?.Cancel();
            base.OnDisappearing();
        }

        private void ApplySafeAreaPadding()
        {
            var insets = _safeArea?.GetSafeAreaInsets() ?? (0f, 0f, 0f, 0f);
            // Extra right padding addresses landscape cutout / gesture-bar clipping seen elsewhere.
            const double basePad = 12;
            const double rightExtra = 8;
            MainScroll.Padding = new Thickness(
                basePad + insets.Left,
                8 + insets.Top,
                basePad + insets.Right + rightExtra,
                16 + insets.Bottom);

            // Keep enough trailing space that IntervalsSection can land in the viewport center.
            double viewport = MainScroll.Height;
            if (viewport > 0)
                ScrollBottomSpacer.HeightRequest = Math.Max(180, viewport * 0.55);
        }

        private void BuildIntervalButtons()
        {
            IntervalButtonsHost.Children.Clear();
            IntervalButtonsHost.RowDefinitions.Clear();
            _intervalButtons.Clear();

            const int columns = 3;
            var intervals = IntervalEarTrainingCatalog.Intervals;
            int rows = (intervals.Count + columns - 1) / columns;
            for (int r = 0; r < rows; r++)
                IntervalButtonsHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var gridStyle = (Style)Resources["EarTrainButton"];
            for (int i = 0; i < intervals.Count; i++)
            {
                int semitones = intervals[i].Semitones;
                var btn = CreateGridButton(
                    IntervalEarTrainingCatalog.FormatButtonLabel(semitones),
                    gridStyle,
                    $"Interval {IntervalEarTrainingCatalog.FormatButtonLabel(semitones)}");
                int captured = semitones;
                btn.Clicked += async (_, _) => await OnIntervalClickedAsync(captured);

                Grid.SetColumn(btn, i % columns);
                Grid.SetRow(btn, i / columns);
                IntervalButtonsHost.Children.Add(btn);
                _intervalButtons[semitones] = btn;
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

        private async Task ScrollToIntervalsCenteredAsync()
        {
            try
            {
                await Task.Delay(50);
                await MainScroll.ScrollToAsync(IntervalsSection, ScrollToPosition.Center, animated: true);
            }
            catch (Exception ex)
            {
                Utils.Log($"[EarTraining] Scroll to intervals: {ex}");
            }
        }

        private async Task ScrollToPlayControlsAsync()
        {
            try
            {
                await Task.Delay(50);
                await MainScroll.ScrollToAsync(PlayControlsSection, ScrollToPosition.Start, animated: true);
            }
            catch (Exception ex)
            {
                Utils.Log($"[EarTraining] Scroll to play controls: {ex}");
            }
        }

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

        private void OnDurationChanged(object? sender, ValueChangedEventArgs e)
        {
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
            DurationLabel.Text = $"Note duration: {_noteDurationMs} ms";
            DurationValueLabel.Text = _noteDurationMs.ToString(CultureInfo.InvariantCulture);
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
            => await PlayRandomQuizAsync();

        private async void OnPlayAgainClicked(object? sender, EventArgs e)
            => await PlayAgainAsync();

        private async Task OnIntervalClickedAsync(int semitones)
        {
            if (_showingFeedback)
                return;

            if (_awaitingAnswer)
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

            if (!IntervalEarTrainingLogic.TryPickInterval(
                    low, high, semitones, _directionMode, Random.Shared, out var pitches))
            {
                await DisplayAlertAsync(
                    "Range too narrow",
                    $"Your Settings note range is too small for a {IntervalEarTrainingCatalog.GetName(semitones)} ({semitones} semitones). Widen Lowest note / Highest note in Settings.",
                    "OK");
                return;
            }

            _lastPitches = pitches;
            _awaitingAnswer = false;
            UpdatePlayAgainEnabled();
            UpdateStatus($"Playing {FormatPlayedLabel(pitches)}…");
            await PlayPitchesAsync(pitches);
            await ScrollToIntervalsCenteredAsync();
            UpdateStatus("Tap an interval to hear it, or Play Random to quiz yourself.");
        }

        private async Task PlayRandomQuizAsync()
        {
            if (_showingFeedback)
                return;

            var range = await TryResolveWrittenRangeAsync();
            if (range is null)
                return;
            var (low, high) = range.Value;

            if (!IntervalEarTrainingLogic.TryPickRandomInterval(
                    low, high, _directionMode, Random.Shared, out var pitches))
            {
                await DisplayAlertAsync(
                    "Range too narrow",
                    "Your Settings note range cannot fit any interval. Check Lowest note and Highest note in Settings.",
                    "OK");
                return;
            }

            _lastPitches = pitches;
            _expectedSemitones = pitches.Semitones;
            _awaitingAnswer = true;
            UpdatePlayAgainEnabled();
            UpdateStatus("Listen… then tap the interval you heard.");
            await PlayPitchesAsync(pitches);
            await ScrollToIntervalsCenteredAsync();
            if (_awaitingAnswer)
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

            // Replaying always restores quiz mode: the next interval tap is an answer, not direct play.
            _expectedSemitones = pitches.Semitones;
            _awaitingAnswer = true;
            UpdatePlayAgainEnabled();

            UpdateStatus("Playing again… then tap your answer.");
            await PlayPitchesAsync(pitches);
            await ScrollToIntervalsCenteredAsync();
            if (_awaitingAnswer)
                UpdateStatus("Which interval was that? Tap your answer (or Play Again).");
        }

        private async Task SubmitAnswerAsync(int answeredSemitones)
        {
            if (!_awaitingAnswer || _showingFeedback)
                return;

            bool correct = IntervalEarTrainingLogic.IsAnswerCorrect(_expectedSemitones, answeredSemitones);
            _awaitingAnswer = false;
            UpdatePlayAgainEnabled();

            string expectedLabel = _lastPitches is { } last
                ? FormatPlayedLabel(last)
                : IntervalEarTrainingCatalog.FormatButtonLabel(_expectedSemitones);
            string answeredLabel = IntervalEarTrainingCatalog.FormatButtonLabel(answeredSemitones);

            await ShowFeedbackAsync(correct, expectedLabel, answeredLabel);
            UpdateStatus("Tap an interval to hear it, or Play Random to quiz yourself.");
        }

        private static string FormatPlayedLabel(IntervalEarTrainingLogic.IntervalPitches pitches)
        {
            string name = IntervalEarTrainingCatalog.FormatButtonLabel(pitches.Semitones);
            if (pitches.Semitones == 0)
                return name;
            return $"{name} ({IntervalEarTrainingLogic.FormatDirectionLabel(pitches.IsAscending)})";
        }

        private async Task ShowFeedbackAsync(bool correct, string expectedLabel, string answeredLabel)
        {
            _showingFeedback = true;
            SetControlsEnabled(false);
            UpdatePlayAgainEnabled();

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

            _feedbackCts?.Cancel();
            _feedbackCts = new CancellationTokenSource();
            try
            {
                await Task.Delay(1600, _feedbackCts.Token);
            }
            catch (OperationCanceledException) { }

            try
            {
                await FeedbackOverlay.FadeToAsync(0, 150);
            }
            catch { }

            FeedbackOverlay.IsVisible = false;
            _showingFeedback = false;
            SetControlsEnabled(true);
            UpdatePlayAgainEnabled();
            await ScrollToPlayControlsAsync();
        }

        private void SetControlsEnabled(bool enabled)
        {
            PlayRandomButton.IsEnabled = enabled;
            DurationSlider.IsEnabled = enabled;
            DirectionAscendingButton.IsEnabled = enabled;
            DirectionDescendingButton.IsEnabled = enabled;
            DirectionRandomButton.IsEnabled = enabled;
            foreach (var btn in _intervalButtons.Values)
                btn.IsEnabled = enabled;
        }

        private async Task<(int Low, int High)?> TryResolveWrittenRangeAsync()
        {
            int lowMidi = NoteSessionService.NoteNameToMidi(_session.LowestNote);
            int highMidi = NoteSessionService.NoteNameToMidi(_session.HighestNote);
            if (lowMidi <= 0 || highMidi <= 0)
            {
                await DisplayAlertAsync("Note range", "Could not read Lowest/Highest notes from Settings.", "OK");
                return null;
            }

            if (highMidi < lowMidi)
                (lowMidi, highMidi) = (highMidi, lowMidi);
            return (lowMidi, highMidi);
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
    }
}
