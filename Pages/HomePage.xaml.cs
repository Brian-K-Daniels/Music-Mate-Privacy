using System.ComponentModel;
using musicmate.Services;
using musicmate.Utilities;

namespace musicmate.Pages
{
    /// <summary>
    /// Simple child-friendly Home page.  Shows an instrument picker, a level
    /// control (1 to 100, default 1), and a Start button.
    ///
    /// Instrument is persisted via NoteSessionService.Instrument (which writes
    /// Preferences automatically).  Level is persisted under "ChildPractice.Level".
    ///    ///
    /// FUTURE: Level-up / congratulations logic should be triggered from
    /// MusicPage (or a dedicated service) after a session completes successfully,
    /// then navigate back here with a celebratory overlay.
    /// </summary>
    public partial class HomePage : ContentPage
    {
        private static readonly string[]        _instrumentOptions =
            NoteSessionService.InstrumentOptions.Cast<string>().ToArray();  

        // Level controls — single tap changes by 1; holding repeats automatically.
        // Initial delay before repeat begins; interval while held.
        private const int                       LevelRepeatInitialDelayMs = 450;
        private const int                       LevelRepeatIntervalMs = 90;
        private CancellationTokenSource?        _levelRepeatCts;
        private readonly IOrientationService    _orientation = null!;
        private const string                    PrefLevelKey = "ChildPractice.Level";
        private readonly NoteSessionService     _session = null!;
        private int                             _selectedLevel = 1;    
        private int                             _selectedInstrumentIndex = -1;
        private void                            AdjustLevel(int delta)
        {
            var next = Math.Clamp(_selectedLevel + delta, 1, 100);
            if (next == _selectedLevel) return;
            _selectedLevel = next;
            Preferences.Default.Set(PrefLevelKey, _selectedLevel);
            UpdateLevelDisplay();
        }
        public HomePage()
        {
            try
            {
                InitializeComponent();

                _session = ServiceHelper.GetService<NoteSessionService>()!;
                _orientation = ServiceHelper.GetService<IOrientationService>()!;

                RefreshInstrumentFromSession();

                // Level: restore saved value
                _selectedLevel = Math.Clamp(
                    Preferences.Default.Get(PrefLevelKey, 1), 1, 100);
                UpdateLevelDisplay();
            }
            catch (Exception ex)
            {
                Utils.Log($"[HomePage] Constructor ERROR: {ex}");
            }
        }

        protected override void                 OnAppearing()
        {
            base.OnAppearing();
            _orientation?.AllowAutorotate();
            _session.PropertyChanged -= OnSessionPropertyChanged;
            _session.PropertyChanged += OnSessionPropertyChanged;
            RefreshInstrumentFromSession();

            // Refresh the level from preferences in case LevelUpService advanced it
            // while the user was on MusicPage.  The Preferences write happens in
            // LevelUpService before this page becomes visible, so the value is stable.
            var savedLevel = Math.Clamp(Preferences.Default.Get(PrefLevelKey, 1), 1, 100);
            if (savedLevel != _selectedLevel)
            {
                _selectedLevel = savedLevel;
                UpdateLevelDisplay();
            }
            Utils.Log($"[HomePage] OnAppearing: savedLevel={savedLevel}, _selectedLevel={_selectedLevel}");
        }

        protected override void                 OnDisappearing()
        {
            _session.PropertyChanged -= OnSessionPropertyChanged;
            base.OnDisappearing();
        }

        // Instrument selection via action sheet
        private async void                      OnInstrumentTapped(object? sender, TappedEventArgs e)
        {
            try
            {
                var result = await DisplayActionSheetAsync(
                    "Pick your instrument", "Cancel", null, _instrumentOptions);
                if (result == null || result == "Cancel") return;
                var idx = Array.IndexOf(_instrumentOptions, result);
                if (idx >= 0)
                    SetInstrumentSelection(idx);
            }
            catch (Exception ex)
            {
                Utils.Log($"[HomePage] OnInstrumentTapped ERROR: {ex}");
            }
        }
        // Start button
        private async void                      OnStartClicked(object? sender, EventArgs e)
        {
            try
            {
                var idx = _selectedInstrumentIndex;
                if (idx >= 0)
                    _session.Instrument = _instrumentOptions[idx];
                _session.ChildLevel = _selectedLevel;

                // Apply difficulty settings derived from the selected level.
                // DifficultyLevelMapper translates level 1–100 into concrete session
                // parameters (BPM, note range, rhythm complexity, accidental %).
                var difficulty = DifficultyLevelMapper.PickAndApplyToSession(
                    _selectedLevel, _session);

                Utils.Log($"[ChildPractice] Level={_selectedLevel}, " +
                          $"Stage={difficulty.StageLabel}, Scale={difficulty.SuggestedScale}, " +
                          $"Range={difficulty.LowestNote}–{difficulty.HighestNote}, " +
                          $"Key={difficulty.ForceKey}, Notes≈{difficulty.SuggestedNoteCount}, " +
                          $"Rhythm={difficulty.SmallestRhythmNote} variety={difficulty.RhythmVarietyPercent}% " +
                          $"rests={difficulty.RestChancePercent}%, Sync={difficulty.SyncopationSetting}");

                Utils.Log($"[ChildPractice] Session after apply → Level={_session.ChildLevel}, " +
                          $"LowestNote={_session.LowestNote}, HighestNote={_session.HighestNote}, " +
                          $"Key={_session.Key}, IsRandomMode={_session.IsRandomMode}");

                LevelUpService.MarkCountSinceNow();

                // Fresh practice from Home: listen immediately with Repeat New Each Time.
                _session.EnableAutoStartWithRepeatNew();

                // FUTURE: level-up / congratulations logic will be triggered from
                //   MusicPage after a session completes successfully, then navigate
                //   back here with a celebratory overlay.

                await NavigationBusyService.GoToAsync("//MusicPage");
            }
            catch (Exception ex)
            {
                Utils.Log($"[HomePage] OnStartClicked ERROR: {ex}");
            }
        }
        private void                            OnLevelDown(object? sender, EventArgs e)
        {
            if (_selectedLevel > 1)
            {
                _selectedLevel--;
                Preferences.Default.Set(PrefLevelKey, _selectedLevel);
                UpdateLevelDisplay();
            }
        }
        private void                            OnLevelUp(object? sender, EventArgs e)
        {
            if (_selectedLevel < 100)
            {
                _selectedLevel++;
                Preferences.Default.Set(PrefLevelKey, _selectedLevel);
                UpdateLevelDisplay();
            }
        }
        private void                            OnLevelDownPressed(object? sender, EventArgs e)
        {
            StartLevelRepeat(delta: -1);
        }
        private void                            OnLevelUpPressed(object? sender, EventArgs e)
        {
            StartLevelRepeat(delta: +1);
        }
        private void                            OnLevelButtonReleased(object? sender, EventArgs e)
        {
            StopLevelRepeat();
        }
        private void                            SetInstrumentSelection(int idx)
        {
            ApplyInstrumentDisplay(idx);
            _session.Instrument = _instrumentOptions[idx];
        }

        private void                            RefreshInstrumentFromSession()
        {
            int idx = InstrumentCatalog.IndexOfOption(_session.Instrument);
            if (idx >= 0)
                ApplyInstrumentDisplay(idx);
        }

        private void                            ApplyInstrumentDisplay(int idx)
        {
            _selectedInstrumentIndex = idx;
            InstrumentPickerLabel.Text = _instrumentOptions[idx];
            InstrumentPickerLabel.TextColor = Colors.Black;
        }

        private void                            OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NoteSessionService.Instrument)
                || e.PropertyName == nameof(NoteSessionService.InstrumentDisplayName)
                || e.PropertyName == nameof(NoteSessionService.InstrumentKey))
            {
                MainThread.BeginInvokeOnMainThread(RefreshInstrumentFromSession);
            }
        }
        private void                            StartLevelRepeat(int delta)
        {
            // Apply the first step immediately (mirrors the Clicked behaviour for a simple tap).
            AdjustLevel(delta);

            // Cancel any previous repeat that may still be running.
            StopLevelRepeat();
            var cts = new CancellationTokenSource();
            _levelRepeatCts = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(LevelRepeatInitialDelayMs, cts.Token);
                    while (!cts.Token.IsCancellationRequested)
                    {
                        await MainThread.InvokeOnMainThreadAsync(() => AdjustLevel(delta));
                        await Task.Delay(LevelRepeatIntervalMs, cts.Token);
                    }
                }
                catch (OperationCanceledException) { /* normal on release */ }
            });
        }
        private void                            StopLevelRepeat()
        {
            var cts = _levelRepeatCts;
            _levelRepeatCts = null;
            cts?.Cancel();
            cts?.Dispose();
        }
        private void                            UpdateLevelDisplay()
        {
            LevelLabel.Text = _selectedLevel.ToString();

            var stage = DifficultyLevelMapper.GetStageLabel(_selectedLevel);
            var focus = DifficultyLevelMapper.GetMainFocus(_selectedLevel);
            // Keep "C Major" intact on the second line (Beginner band).
            LevelDescLabel.Text = _selectedLevel <= 10
                ? $"{stage} — More notes,\nC Major, quarter notes"
                : $"{stage} — {focus}";

            LevelDownButton.IsEnabled = _selectedLevel > 1;
            LevelUpButton.IsEnabled = _selectedLevel < 100;
        }

    }
}
