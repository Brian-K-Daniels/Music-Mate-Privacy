using musicmate.Services;
using musicmate.Utilities;

namespace musicmate.Pages
{
    /// <summary>
    /// Simple child-friendly Home page.  Shows an instrument picker, a level
    /// control (1 to 100, default 1), and a Start button.
    ///
    /// Instrument is persisted via NoteSessionService.Instrument (which writes
    /// Preferences automatically).  Level is persisted under "ChildHome.Level".
    ///
    /// FUTURE: When DifficultyLevelMapper is implemented, call it from
    /// OnStartClicked before navigating to MainPage:
    ///     var p = DifficultyLevelMapper.GetSessionParameters(_selectedLevel);
    ///     _session.ApplyDifficultyParameters(p);
    ///
    /// FUTURE: Level-up / congratulations logic should be triggered from
    /// MainPage (or a dedicated service) after a session completes successfully,
    /// then navigate back here with a celebratory overlay.
    /// </summary>
    public partial class ChildHomePage : ContentPage
    {
        private const string PrefLevelKey = "ChildHome.Level";

        private readonly NoteSessionService _session = null!;
        private readonly IOrientationService _orientation = null!;

        private int _selectedLevel = 1;

        private static readonly string[] _instrumentOptions =
            NoteSessionService.InstrumentOptions.Cast<string>().ToArray();

        private int _selectedInstrumentIndex = -1;

        public ChildHomePage()
        {
            try
            {
                InitializeComponent();

                _session     = ServiceHelper.GetService<NoteSessionService>()!;
                _orientation = ServiceHelper.GetService<IOrientationService>()!;

                // Restore saved instrument selection
                var savedInstrument = _session.Instrument ?? string.Empty;
                var idx = Array.IndexOf(_instrumentOptions, savedInstrument);
                if (idx < 0)
                {
                    var savedShort = savedInstrument.Split(',')[0].Trim();
                    idx = Array.FindIndex(_instrumentOptions, s => s.Split(',')[0].Trim() == savedShort);
                }
                if (idx >= 0)
                    SetInstrumentSelection(idx);

                // Level: restore saved value
                _selectedLevel = Math.Clamp(
                    Preferences.Default.Get(PrefLevelKey, 1), 1, 100);
                UpdateLevelDisplay();
            }
            catch (Exception ex)
            {
                Utils.Log($"[ChildHomePage] Constructor ERROR: {ex}");
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            _orientation?.AllowAutorotate();

            // Refresh the level from preferences in case LevelUpService advanced it
            // while the user was on MainPage.  The Preferences write happens in
            // LevelUpService before this page becomes visible, so the value is stable.
            var savedLevel = Math.Clamp(Preferences.Default.Get(PrefLevelKey, 1), 1, 100);
            if (savedLevel != _selectedLevel)
            {
                _selectedLevel = savedLevel;
                UpdateLevelDisplay();
            }
            Utils.Log($"[ChildHomePage] OnAppearing: savedLevel={savedLevel}, _selectedLevel={_selectedLevel}");
        }

        // Instrument selection via action sheet

        private async void OnInstrumentTapped(object? sender, TappedEventArgs e)
        {
            try
            {
                var result = await DisplayActionSheet(
                    "Pick your instrument", "Cancel", null, _instrumentOptions);
                if (result == null || result == "Cancel") return;
                var idx = Array.IndexOf(_instrumentOptions, result);
                if (idx >= 0)
                    SetInstrumentSelection(idx);
            }
            catch (Exception ex)
            {
                Utils.Log($"[ChildHomePage] OnInstrumentTapped ERROR: {ex}");
            }
        }

        private void SetInstrumentSelection(int idx)
        {
            _selectedInstrumentIndex = idx;
            // Show the full label in the picker row
            InstrumentPickerLabel.Text = _instrumentOptions[idx];
            InstrumentPickerLabel.TextColor = Colors.Black;
            // Store the full label so GetInstrumentTransposeOffset() can resolve it correctly
            _session.Instrument = _instrumentOptions[idx];
        }

        // Level controls — single tap changes by 1; holding repeats automatically.
        // Initial delay before repeat begins; interval while held.
        private const int LevelRepeatInitialDelayMs = 450;
        private const int LevelRepeatIntervalMs      = 90;
        private CancellationTokenSource? _levelRepeatCts;

        private void OnLevelDown(object? sender, EventArgs e)
        {
            if (_selectedLevel > 1)
            {
                _selectedLevel--;
                Preferences.Default.Set(PrefLevelKey, _selectedLevel);
                UpdateLevelDisplay();
            }
        }

        private void OnLevelUp(object? sender, EventArgs e)
        {
            if (_selectedLevel < 100)
            {
                _selectedLevel++;
                Preferences.Default.Set(PrefLevelKey, _selectedLevel);
                UpdateLevelDisplay();
            }
        }

        private void OnLevelDownPressed(object? sender, EventArgs e)
        {
            StartLevelRepeat(delta: -1);
        }

        private void OnLevelUpPressed(object? sender, EventArgs e)
        {
            StartLevelRepeat(delta: +1);
        }

        private void OnLevelButtonReleased(object? sender, EventArgs e)
        {
            StopLevelRepeat();
        }

        private void StartLevelRepeat(int delta)
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

        private void StopLevelRepeat()
        {
            var cts = _levelRepeatCts;
            _levelRepeatCts = null;
            cts?.Cancel();
            cts?.Dispose();
        }

        private void AdjustLevel(int delta)
        {
            var next = Math.Clamp(_selectedLevel + delta, 1, 100);
            if (next == _selectedLevel) return;
            _selectedLevel = next;
            Preferences.Default.Set(PrefLevelKey, _selectedLevel);
            UpdateLevelDisplay();
        }

        private void UpdateLevelDisplay()
        {
            LevelLabel.Text = _selectedLevel.ToString();

            // Simple tier labels - placeholder for DifficultyLevelMapper.GetDescription(level)
            LevelDescLabel.Text = _selectedLevel switch
            {
                <= 10 => "Beginner",
                <= 30 => "Getting started",
                <= 50 => "Intermediate",
                <= 70 => "Advanced",
                <= 90 => "Expert",
                _     => "Master"
            };

            LevelDownButton.IsEnabled = _selectedLevel > 1;
            LevelUpButton.IsEnabled   = _selectedLevel < 100;
        }

        // Start button

        private async void OnStartClicked(object? sender, EventArgs e)
        {
            try
            {
                //  2026.05.30 1825  TEMPORARY BLOCK TO TEST LEVEL UP
                // TEST ONLY — remove after testing
                //Microsoft.Maui.Storage.Preferences.Default.Set("LevelUp.SessionCount", 1);
                //Microsoft.Maui.Storage.Preferences.Default.Set("LevelUp.MinPitchPct", 1.0);
                //Microsoft.Maui.Storage.Preferences.Default.Set("LevelUp.MinOverallPct", 1.0);
                //Microsoft.Maui.Storage.Preferences.Default.Set("LevelUp.MinNotes", 1);
                //Microsoft.Maui.Storage.Preferences.Default.Set("LevelUp.MinTimingPct", 1.0);
                // END OF             TEMPORARY BLOCK
                var idx = _selectedInstrumentIndex;
                if (idx >= 0)
                    _session.Instrument = _instrumentOptions[idx]; // full label for transpose lookup

                // Extract the short key (e.g. "Bb") to pass into the difficulty mapper so it
                // can convert the level-based concert key to the correct written key.
                var shortInstrumentKey = idx >= 0
                    ? _instrumentOptions[idx].Split(',')[0].Trim()
                    : "C";

                // Apply difficulty settings derived from the selected level.
                // DifficultyLevelMapper translates level 1–100 into concrete session
                // parameters (BPM, note range, rhythm complexity, accidental %).
                var difficulty = DifficultyLevelMapper.GetSettingsForLevel(
                    _selectedLevel, shortInstrumentKey);

                Utils.Log($"[ChildHome] Level={_selectedLevel} → BPM={difficulty.PlaybackBpm}, " +
                          $"Range={difficulty.LowestNote}–{difficulty.HighestNote}, " +
                          $"Key={difficulty.ForceKey}, Accidentals={difficulty.AccidentalPercent}%, " +
                          $"V2Smallest={difficulty.V2SmallestNote}, RandomMode={difficulty.UseRandomMode}");

                // Apply difficulty and allow non-Classic staff modes so V2/V3 and
                // level-driven rhythm changes take effect on the Main page.
                DifficultyLevelMapper.ApplyToSession(difficulty, _session, forceClassicMode: false);

                Utils.Log($"[ChildHome] Session after apply → Level={_session.ChildLevel}, " +
                          $"LowestNote={_session.LowestNote}, HighestNote={_session.HighestNote}, " +
                          $"Key={_session.Key}, IsRandomMode={_session.IsRandomMode}");

                // Tell the session which child-home level started it so that
                // SaveSessionStatAsync (in MainPage) can record a SessionResult.
                _session.ChildLevel = _selectedLevel;

                // FUTURE: level-up / congratulations logic will be triggered from
                //   MainPage after a session completes successfully, then navigate
                //   back here with a celebratory overlay.

                await Shell.Current.GoToAsync("//MainPage");
            }
            catch (Exception ex)
            {
                Utils.Log($"[ChildHomePage] OnStartClicked ERROR: {ex}");
            }
        }
    }
}
