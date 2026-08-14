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
        private bool _awaitingAnswer;
        private int _expectedSemitones;
        private bool _showingFeedback;
        private int _noteDurationMs = IntervalEarTrainingLogic.DefaultNoteDurationMs;
        private IntervalDirectionMode _directionMode = IntervalEarTrainingLogic.DefaultDirectionMode;
        private readonly Dictionary<int, Button> _intervalButtons = new();
        private int _playGeneration;
        private StaffDrawable? _staffDrawable;

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
            UpdatePlayAgainEnabled();
            HideStaffReveal();
            ApplySafeAreaPadding();
            SizeChanged += (_, _) =>
            {
                ApplySafeAreaPadding();
                SyncScrollContentWidth();
                SyncStaffPanelToButtons();
            };
            MainScroll.SizeChanged += (_, _) => SyncScrollContentWidth();
            IntervalButtonsHost.SizeChanged += (_, _) => SyncStaffPanelToButtons();
            StaffGraphicsView.SizeChanged += (_, _) => SyncStaffLayout();
        }

        private async void OnNavigateMusicClicked(object? sender, EventArgs e)
        {
            try
            {
                await Shell.Current.GoToAsync("//MusicPage");
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
            ApplySafeAreaPadding();
            EnsureEmptyStaffPanel();
            SyncStaffPanelToButtons();
            if (Shell.Current is AppShell shell)
                shell.EnsureFlyoutItemsVisiblePublic();
        }

        protected override void OnDisappearing()
        {
            CancelPlayback();
            _feedbackCts?.Cancel();
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

        private void ApplySafeAreaPadding()
        {
            var insets = _safeArea?.GetSafeAreaInsets() ?? (0f, 0f, 0f, 0f);
            const double basePad = 8;
            const double rightExtra = 6;
            // Play row sits outside the ScrollView — pad it for left/right cutouts.
            PlayControlsSection.Margin = new Thickness(
                basePad + insets.Left,
                4,
                basePad + insets.Right + rightExtra,
                4);
            // Shell already clears the nav bar; only use side/bottom insets on the scroller.
            MainScroll.Padding = new Thickness(
                basePad + insets.Left,
                0,
                basePad + insets.Right + rightExtra,
                6 + insets.Bottom);
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

            var gridStyle = (Style)Resources["EarTrainIntervalButton"];
            for (int i = 0; i < intervals.Count; i++)
            {
                int semitones = intervals[i].Semitones;
                var btn = CreateGridButton(
                    IntervalEarTrainingCatalog.FormatButtonLabel(semitones),
                    gridStyle,
                    $"Interval {IntervalEarTrainingCatalog.FormatButtonLabel(semitones)}");
                ApplyFamilyColor(btn, semitones);
                int captured = semitones;
                btn.Clicked += async (_, _) => await OnIntervalClickedAsync(captured);

                Grid.SetColumn(btn, i % columns);
                Grid.SetRow(btn, i / columns);
                IntervalButtonsHost.Children.Add(btn);
                _intervalButtons[semitones] = btn;
            }
        }

        private static void ApplyFamilyColor(Button button, int semitones)
        {
            button.BackgroundColor = IntervalEarTrainingButtonColors.FamilyBackground(semitones);
            button.TextColor = IntervalEarTrainingButtonColors.LabelText;
            button.BorderColor = IntervalEarTrainingButtonColors.Border;
        }

        private void RestoreAllIntervalFamilyColors()
        {
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
                // Play buttons are pinned above the ScrollView; bring scroll content to top
                // so status / direction stay reachable under them after feedback.
                await Task.Delay(50);
                await MainScroll.ScrollToAsync(0, 0, animated: true);
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

            IntervalEarTrainingLogic.IntervalPitches pitches;
            if (_referenceStartWrittenMidi is not int reference)
            {
                await DisplayAlertAsync(
                    "Choose a starting note",
                    "Tap Play Random Interval first. That picks a starting note; then each interval button uses the same starting note until you tap Play Random Interval again.",
                    "OK");
                return;
            }

            if (!IntervalEarTrainingLogic.TryBuildIntervalFromReference(
                    reference, low, high, semitones, _directionMode, Random.Shared, out pitches))
            {
                await DisplayAlertAsync(
                    "Range too narrow",
                    $"The current starting note cannot form a {IntervalEarTrainingCatalog.GetName(semitones)} ({semitones} semitones) within your Settings note range. Tap Play Random Interval for a new starting note, or widen Lowest note / Highest note in Settings.",
                    "OK");
                return;
            }

            _lastPitches = pitches;
            _awaitingAnswer = false;
            UpdatePlayAgainEnabled();
            UpdateStatus($"Playing {FormatPlayedLabel(pitches)}…");
            await PlayPitchesAsync(pitches);
            // Instructional interval taps: user chose the interval — show the sounded pitches.
            ShowStaffForPitches(pitches);
            await ScrollToIntervalsCenteredAsync();
            UpdateStatus("Tap an interval to hear it, or Play Random to quiz yourself.");
        }

        private async Task PlayRandomQuizAsync()
        {
            if (_showingFeedback)
                return;

            RestoreAllIntervalFamilyColors();

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

            // Hide previous notation so the quiz answer is not revealed visually.
            HideStaffReveal();

            // New random starting note becomes the fixed reference for interval-button explores.
            _referenceStartWrittenMidi = pitches.StartWrittenMidi;
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

            // Reveal the exact pitches that were sounded (correct or incorrect answer).
            if (_lastPitches is { } revealed)
                ShowStaffForPitches(revealed);

            string expectedLabel = _lastPitches is { } last
                ? FormatPlayedLabel(last)
                : IntervalEarTrainingCatalog.FormatButtonLabel(_expectedSemitones);
            string answeredLabel = IntervalEarTrainingCatalog.FormatButtonLabel(answeredSemitones);

            await ShowFeedbackAsync(correct, expectedLabel, answeredLabel, answeredSemitones, _expectedSemitones);
            UpdateStatus("Tap an interval to hear it, or Play Random to quiz yourself.");
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

        private void EnsureEmptyStaffPanel()
        {
            EnsureStaffDrawable();
            _staffDrawable!.UpperNotes = new List<GeneratedNote>();
            _staffDrawable.LowerNotes = new List<GeneratedNote>();
            _staffDrawable.UpperBarBeats = new List<double>();
            _staffDrawable.LowerBarBeats = new List<double>();
            _staffDrawable.UpperNoteStates = Array.Empty<StaffNoteState>();
            _staffDrawable.ActiveNoteIndex = -1;
            _staffDrawable.UpperHasEndBar = false;
            _staffDrawable.InvalidateLayoutCache();
            StaffRevealBorder.IsVisible = true;
            SyncStaffPanelToButtons();
            StaffGraphicsView.Invalidate();
        }

        /// <summary>
        /// Engraves the exact stored pitches that were sounded. Does not re-pick intervals.
        /// </summary>
        private void ShowStaffForPitches(IntervalEarTrainingLogic.IntervalPitches pitches)
        {
            EnsureStaffDrawable();
            string key = string.IsNullOrWhiteSpace(_session.Key) ? "C" : _session.Key;
            string scale = string.IsNullOrWhiteSpace(_session.SelectedScale) ? "Major" : _session.SelectedScale;

            var notes = IntervalEarTrainingNotation.BuildDisplayNotes(pitches, key, scale);
            _staffDrawable!.SingleStaffLayout = true;
            _staffDrawable.OmitStaffHeader = true;
            _staffDrawable.NotationKeyOverride = key;
            _staffDrawable.NotationScaleOverride = scale;
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
            SyncStaffPanelToButtons();
            SyncStaffLayout();
            StaffGraphicsView.Invalidate();
        }

        /// <summary>
        /// ScrollView children often lay out to intrinsic width on Android; force the
        /// content stack to the viewport width so intervals + staff use the full row.
        /// </summary>
        private void SyncScrollContentWidth()
        {
            double w = MainScroll.Width;
            if (w < 8)
                w = Width;
            if (w < 8)
                return;

            double pad = MainScroll.Padding.Left + MainScroll.Padding.Right;
            double contentW = Math.Max(8, w - pad);
            if (Math.Abs(MainContent.WidthRequest - contentW) > 0.5)
                MainContent.WidthRequest = contentW;
        }

        /// <summary>Match the compact staff panel height to the interval button grid.</summary>
        private void SyncStaffPanelToButtons()
        {
            SyncScrollContentWidth();

            double buttonH = IntervalButtonsHost.Height;
            if (buttonH < 8)
            {
                // 5 rows × 48px + 4 gaps × 4px before first layout pass.
                buttonH = 5 * 48 + 4 * 4;
            }

            double staffH = Math.Max(96, buttonH);
            if (Math.Abs(StaffGraphicsView.HeightRequest - staffH) > 0.5)
                StaffGraphicsView.HeightRequest = staffH;

            // Keep the staff narrow so the three interval columns can show full labels.
            // Cap by remaining page width so we never starve the button grid.
            double pageW = MainContent.WidthRequest > 8 ? MainContent.WidthRequest : Width;
            double maxStaffFromPage = pageW > 8 ? Math.Max(110, pageW * 0.28) : 128;
            double staffW = Math.Clamp(Math.Min(128, maxStaffFromPage), 110, 132);
            if (Math.Abs(StaffRevealBorder.WidthRequest - staffW) > 0.5)
                StaffRevealBorder.WidthRequest = staffW;

            SyncStaffLayout();
        }

        private void SyncStaffLayout()
        {
            if (_staffDrawable == null)
                return;

            float h = (float)Math.Max(96, StaffGraphicsView.Height > 1
                ? StaffGraphicsView.Height
                : StaffGraphicsView.HeightRequest);
            _staffDrawable.SingleStaffLayout = true;
            _staffDrawable.OmitStaffHeader = true;
            _staffDrawable.AvailableHeight = h;
            _staffDrawable.InvalidateLayoutCache();
            StaffGraphicsView.Invalidate();
        }
    }
}
