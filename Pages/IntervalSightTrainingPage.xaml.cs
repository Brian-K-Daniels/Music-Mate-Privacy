using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;
using musicmate.Utilities;

namespace musicmate.Pages
{
    public partial class IntervalSightTrainingPage : ContentPage
    {
        private readonly NoteSessionService _session;
        private readonly ThemeService _theme;
        private readonly IOrientationService _orientation;
        private readonly ISafeAreaService? _safeArea;

        private StaffDrawable? _staffDrawable;
        private IntervalSightTrainingLogic? _logic;
        private List<GeneratedNote> _notes = new();
        private CancellationTokenSource? _feedbackCts;
        private bool _busy;
        private readonly Dictionary<int, Button> _buttons = new();
        private Button? _newTuneButton;
        private int? _excludeFirstMagnitude;
        /// <summary>Sight Training level only; never Music <see cref="NoteSessionService.ChildLevel"/>.</summary>
        private int _sightLevel = IntervalSightTrainingLogic.DefaultLevel;
        private int _levelSliderSyncSuppress;

        public IntervalSightTrainingPage()
        {
            InitializeComponent();
            Utils.DisableIosSafeArea(this);
            _session = ServiceHelper.GetService<NoteSessionService>()!;
            _theme = ServiceHelper.GetService<ThemeService>()!;
            _orientation = ServiceHelper.GetService<IOrientationService>()!;
            _safeArea = ServiceHelper.GetService<ISafeAreaService>();

            BuildIntervalButtons();
            ApplySafeAreaPadding();
            SizeChanged += OnPageSizeChanged;
            StaffGraphicsView.SizeChanged += (_, _) => SyncStaffAvailableHeight();
        }

        private async void OnNavigateMusicClicked(object? sender, EventArgs e)
        {
            try
            {
                await Shell.Current.GoToAsync("//MusicPage");
            }
            catch (Exception ex)
            {
                Utils.Log($"[SightTraining] Navigate to Music ERROR: {ex.Message}");
            }
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            _orientation?.ForceLandscape();
            FeedbackOverlay.IsVisible = false;
            ApplySafeAreaPadding();
            EnsureStaffDrawable();
            SyncStaffAvailableHeight();
            InitSightLevelFromPreferences();
            _excludeFirstMagnitude = null;
            try
            {
                await StartNewSessionAsync();
            }
            catch (Exception ex)
            {
                Utils.Log($"[SightTraining] OnAppearing: {ex}");
                SetStatus("Could not start Sight Training. Try opening the page again.");
                SetButtonsEnabled(false);
            }

            await ScrollStaffIntoViewAsync();

            if (Shell.Current is AppShell shell)
                shell.EnsureFlyoutItemsVisiblePublic();
        }

        protected override void OnDisappearing()
        {
            _feedbackCts?.Cancel();
            FeedbackOverlay.IsVisible = false;
            base.OnDisappearing();
            if (_staffDrawable != null)
            {
                _staffDrawable.NotationKeyOverride = null;
                _staffDrawable.NotationScaleOverride = null;
            }
            if (Shell.Current is AppShell shell)
                shell.EnsureFlyoutItemsVisiblePublic();
        }

        private void InitSightLevelFromPreferences()
        {
            _sightLevel = IntervalSightTrainingLogic.LoadPersistedLevel();
            _levelSliderSyncSuppress++;
            try
            {
                LevelSlider.Value = _sightLevel;
                LevelValueLabel.Text = _sightLevel.ToString();
            }
            finally
            {
                if (_levelSliderSyncSuppress > 0)
                    _levelSliderSyncSuppress--;
            }
        }

        private void OnLevelSliderValueChanged(object? sender, ValueChangedEventArgs e)
        {
            if (_levelSliderSyncSuppress > 0)
                return;

            int next = IntervalSightTrainingLogic.ClampLevel((int)Math.Round(e.NewValue));
            if (next == _sightLevel)
                return;

            _sightLevel = next;
            LevelValueLabel.Text = _sightLevel.ToString();
            IntervalSightTrainingLogic.PersistLevel(_sightLevel);
        }

        private async void OnNewTuneClicked(object? sender, EventArgs e)
        {
            if (_busy)
                return;

            _busy = true;
            try
            {
                _feedbackCts?.Cancel();
                FeedbackOverlay.IsVisible = false;
                _excludeFirstMagnitude = null;
                await StartNewSessionAsync();
            }
            catch (Exception ex)
            {
                Utils.Log($"[SightTraining] NewTune: {ex}");
                SetStatus("Could not generate a new tune. Try again.");
            }
            finally
            {
                _busy = false;
            }
        }

        private void OnPageSizeChanged(object? sender, EventArgs e)
        {
            ApplySafeAreaPadding();
            SyncStaffAvailableHeight();
        }

        private void ApplySafeAreaPadding()
        {
            var insets = _safeArea?.GetSafeAreaInsets() ?? (0f, 0f, 0f, 0f);
            // Extra top pad keeps ledger lines / stems clear of the nav bar and cutout.
            const double leftPad = 4;
            const double rightPad = 4;
            const double topPad = 12;
            const double bottomPad = 12;
            MainScroll.Padding = new Thickness(
                leftPad + insets.Left,
                topPad + insets.Top,
                rightPad + insets.Right,
                bottomPad + insets.Bottom);
            MainLayout.Padding = new Thickness(0, 4, 0, 8);
        }

        private async Task ScrollStaffIntoViewAsync()
        {
            try
            {
                await MainScroll.ScrollToAsync(0, 0, animated: false);
            }
            catch
            {
                // Ignore if the scroll view is not ready yet.
            }
        }

        private void EnsureStaffDrawable()
        {
            if (_staffDrawable != null)
            {
                if (StaffGraphicsView.Drawable != _staffDrawable)
                    StaffGraphicsView.Drawable = _staffDrawable;
                return;
            }

            float staffH = ResolveStaffHeightPx();
            _staffDrawable = new StaffDrawable(_session, _theme, _safeArea)
            {
                SingleStaffLayout = true,
                AvailableHeight = staffH,
            };
            StaffGraphicsView.Drawable = _staffDrawable;
        }

        /// <summary>
        /// Staff height leaves the Level row and interval buttons on-screen without scrolling.
        /// </summary>
        private float ResolveStaffHeightPx()
        {
            double pageH = Height;
            if (pageH <= 1)
            {
                var win = Application.Current?.Windows?.FirstOrDefault();
                if (win != null)
                    pageH = win.Height;
            }

            // Buttons: 3×36 + 2×2 row gaps; Level: 32; stack spacing/padding cushion.
            const double buttonsBlock = 3 * 36 + 2 * 2;
            const double levelRow = 32;
            const double stackChrome = 8 + 8 + 2 + 12; // spacing + margins under staff
            const double bottomControls = buttonsBlock + levelRow + stackChrome;

            double h;
            if (pageH > 1)
            {
                double topChrome = MainScroll.Padding.VerticalThickness + 44; // title / safe area
                h = Math.Max(96, pageH - topChrome - bottomControls);
            }
            else if (StaffGraphicsView.Height > 1)
            {
                h = StaffGraphicsView.Height;
            }
            else
            {
                h = 120;
            }

            if (Math.Abs(StaffGraphicsView.HeightRequest - h) > 0.5)
                StaffGraphicsView.HeightRequest = h;

            return (float)h;
        }

        private void SyncStaffAvailableHeight()
        {
            float avail = ResolveStaffHeightPx();
            if (_staffDrawable == null)
                return;

            _staffDrawable.SingleStaffLayout = true;
            _staffDrawable.AvailableHeight = avail;
            _staffDrawable.InvalidateLayoutCache();
            StaffGraphicsView.Invalidate();
        }

        private void BuildIntervalButtons()
        {
            IntervalButtonsHost.Children.Clear();
            IntervalButtonsHost.RowDefinitions.Clear();
            IntervalButtonsHost.ColumnDefinitions.Clear();
            _buttons.Clear();

            const int columns = 5;
            const int rows = 3;
            for (int c = 0; c < columns; c++)
                IntervalButtonsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            for (int r = 0; r < rows; r++)
                IntervalButtonsHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var playStyle = (Style)Resources["SightTrainButton"];
            var intervalStyle = (Style)Resources["SightTrainIntervalButton"];

            // [0,0] = New
            _newTuneButton = new Button
            {
                Text = "New",
                Style = playStyle,
            };
            SemanticProperties.SetDescription(_newTuneButton, "Generate a new random Sight Training tune");
            _newTuneButton.Clicked += OnNewTuneClicked;
            Grid.SetColumn(_newTuneButton, 0);
            Grid.SetRow(_newTuneButton, 0);
            IntervalButtonsHost.Children.Add(_newTuneButton);

            // [1,0] = blank spacer (keeps column width)
            var spacer = new BoxView
            {
                Color = Colors.Transparent,
                InputTransparent = true,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
            };
            Grid.SetColumn(spacer, 1);
            Grid.SetRow(spacer, 0);
            IntervalButtonsHost.Children.Add(spacer);

            // Buttons 0–12 start at [2,0] and end at [4,2]
            const int maxAbs = IntervalSightTrainingLogic.MaxAbsoluteSemitones;
            const int firstCell = 2; // skip New + blank
            for (int semitones = 0; semitones <= maxAbs; semitones++)
            {
                int cell = firstCell + semitones;
                int col = cell % columns;
                int row = cell / columns;

                string label = $"{semitones} - {IntervalEarTrainingCatalog.GetName(semitones)}";
                var btn = new Button
                {
                    Text = label,
                    Style = intervalStyle,
                };
                ApplyFamilyColor(btn, semitones);
                SemanticProperties.SetDescription(btn, label);
                int captured = semitones;
                btn.Clicked += async (_, _) => await OnIntervalClickedAsync(captured);
                Grid.SetColumn(btn, col);
                Grid.SetRow(btn, row);
                IntervalButtonsHost.Children.Add(btn);
                _buttons[semitones] = btn;
            }
        }

        private static void ApplyFamilyColor(Button button, int semitones)
        {
            button.BackgroundColor = IntervalEarTrainingButtonColors.FamilyBackground(semitones);
            button.TextColor = IntervalEarTrainingButtonColors.LabelText;
        }

        private static void SetStatus(string message)
            => StatusService.Instance.StatusMessage = message;

        private async Task StartNewSessionAsync()
        {
            SetStatus("Generating Sight Training tune…");
            SetButtonsEnabled(false);
            EnsureStaffDrawable();
            SyncStaffAvailableHeight();

            // Match Music: wait for Shell/orientation to give the GraphicsView a real width.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (StaffGraphicsView.Width <= 0 && sw.ElapsedMilliseconds < 1500)
                await Task.Delay(40);
            SyncStaffAvailableHeight();

            int level = Math.Clamp(_sightLevel, 1, 100);
            int? exclude = _excludeFirstMagnitude;
            var exercise = await Task.Run(() =>
                IntervalSightTrainingSequenceBuilder.Generate(
                    _session,
                    measureCount: null,
                    randomSeed: null,
                    excludeFirstMagnitude: exclude,
                    childLevelOverride: level));

            _notes = exercise.Notes;
            _logic = new IntervalSightTrainingLogic(_notes, exercise.Key, exercise.Scale);
            _excludeFirstMagnitude = exercise.FinalIntervalMagnitude;

            float staffH = ResolveStaffHeightPx();
            EnsureStaffDrawable();
            _staffDrawable!.SingleStaffLayout = true;
            _staffDrawable.AvailableHeight = staffH;
            _staffDrawable.NotationKeyOverride = exercise.Key;
            _staffDrawable.NotationScaleOverride = exercise.Scale;
            _staffDrawable.InvalidateLayoutCache();
            _staffDrawable.UpperNotes = _notes;
            _staffDrawable.LowerNotes = new List<GeneratedNote>();
            _staffDrawable.UpperBarBeats = exercise.BarBeats;
            _staffDrawable.LowerBarBeats = new List<double>();
            _staffDrawable.IsUpperActive = true;
            _staffDrawable.UpperAlpha = 1f;
            _staffDrawable.LowerAlpha = 0f;
            _staffDrawable.UpperHasEndBar = false;
            StaffGraphicsView.Drawable = _staffDrawable;

            RefreshStaffStates();
            StaffGraphicsView.Invalidate();
            // Post-layout pass: first Invalidate can run while Height is still 0 after ForceLandscape.
            await Task.Delay(50);
            SyncStaffAvailableHeight();
            StaffGraphicsView.Invalidate();
            await ScrollStaffIntoViewAsync();

            if (_logic.IsSessionComplete || !_logic.HasCurrentPair)
            {
                SetStatus("No testable intervals — tap New to try again.");
                SetButtonsEnabled(false);
                return;
            }

            SetStatus($"Level {level} — tap the yellow interval.");
            SetButtonsEnabled(true);
        }

        private void RefreshStaffStates()
        {
            if (_staffDrawable == null || _logic == null)
                return;
            _staffDrawable.UpperNoteStates = _logic.BuildNoteStates();
            if (_logic.CurrentPair is { } pair)
                _staffDrawable.ActiveNoteIndex = pair.AnchorFlatIndex;
        }

        private async Task OnIntervalClickedAsync(int absoluteSemitones)
        {
            if (_busy || _logic == null || !_logic.HasCurrentPair)
                return;

            _busy = true;
            try
            {
                bool correct = _logic.SubmitAnswer(absoluteSemitones);
                if (!correct)
                {
                    RefreshStaffStates();
                    StaffGraphicsView.Invalidate();
                    SetStatus("Incorrect — both notes are red. Try again.");
                    return;
                }

                await ShowCorrectOverlayAsync();

                if (_logic.IsSessionComplete)
                {
                    // Continue indefinitely: next exercise avoids repeating the last magnitude.
                    _excludeFirstMagnitude = absoluteSemitones;
                    await StartNewSessionAsync();
                    return;
                }

                RefreshStaffStates();
                StaffGraphicsView.Invalidate();
                SetStatus("Correct — next yellow pair.");
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task ShowCorrectOverlayAsync()
        {
            _feedbackCts?.Cancel();
            _feedbackCts = new CancellationTokenSource();
            var ct = _feedbackCts.Token;
            FeedbackTitleLabel.Text = "Correct!";
            FeedbackOverlay.IsVisible = true;
            try
            {
                await Task.Delay(650, ct);
            }
            catch (OperationCanceledException)
            {
                // ignored
            }
            finally
            {
                if (!ct.IsCancellationRequested)
                    FeedbackOverlay.IsVisible = false;
            }
        }

        private void SetButtonsEnabled(bool enabled)
        {
            foreach (var btn in _buttons.Values)
                btn.IsEnabled = enabled;
            if (_newTuneButton != null)
                _newTuneButton.IsEnabled = true;
            LevelSlider.IsEnabled = true;
        }
    }
}
