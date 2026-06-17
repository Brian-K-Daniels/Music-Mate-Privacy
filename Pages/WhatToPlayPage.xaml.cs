using musicmate.Models;
using musicmate.Services;
using musicmate.Utilities;
using System.ComponentModel;
using System.Diagnostics;

namespace musicmate.Pages
{
    /// <summary>
    /// "What to Play" page — hosts the instrument/key/scale pickers and the
    /// Repeat / Background Color buttons that were previously on the Home page.
    /// All persistent state is owned by the shared <see cref="NoteSessionService"/>
    /// singleton so changes here are immediately reflected on every other page.
    /// </summary>
    public partial class WhatToPlayPage : ContentPage, INotifyPropertyChanged
    {
        private readonly NoteSessionService  _session = null!;
        private readonly ThemeService        _theme = null!;
        private readonly IOrientationService _orientation = null!;

        private static readonly HashSet<string> FreeKeys   = new() { "C", "F", "Bb", "G", "D" };
        private static readonly HashSet<string> FreeScales = new() { "Major", "Harmonic Minor" };

        private int  _lastFreeKeyIndex    = 0;
        private int  _lastValidScaleIndex = 0;
        private bool _autoRepeat          = false;
        private bool _repeatSameTune      = false;
        private bool _suppressPickerSync  = false;
        private bool _localPlayModeChange = false;
        private List<NoteInfo>? _savedNotesToRepeat = null;

        private string[] _tuneTitles = Array.Empty<string>();
        private string[] _scaleOptions = Array.Empty<string>();
        private static readonly string[] RandomTunerOptions = { "Random", "Tuner" };

        // ── Bindable UI-state properties ─────────────────────────────────────────

        private string _selectedInstrumentShort = "";
        public string SelectedInstrumentShort
        {
            get => _selectedInstrumentShort;
            set { if (_selectedInstrumentShort != value) { _selectedInstrumentShort = value; RaisePropertyChanged(); } }
        }

        private bool _isInstrumentPickerVisible = false;
        public bool IsInstrumentPickerVisible
        {
            get => _isInstrumentPickerVisible;
            set { if (_isInstrumentPickerVisible != value) { _isInstrumentPickerVisible = value; RaisePropertyChanged(); } }
        }

        private bool _isInstrumentLabelVisible = true;
        public bool IsInstrumentLabelVisible
        {
            get => _isInstrumentLabelVisible;
            set { if (_isInstrumentLabelVisible != value) { _isInstrumentLabelVisible = value; RaisePropertyChanged(); } }
        }

        private Color _panelBackgroundColor = Colors.White;
        public Color PanelBackgroundColor
        {
            get => _panelBackgroundColor;
            set { if (_panelBackgroundColor != value) { _panelBackgroundColor = value; RaisePropertyChanged(); } }
        }

        private bool _isRandomRepeatButtonsVisible;
        public bool IsRandomRepeatButtonsVisible
        {
            get => _isRandomRepeatButtonsVisible;
            set { if (_isRandomRepeatButtonsVisible != value) { _isRandomRepeatButtonsVisible = value; RaisePropertyChanged(); } }
        }

        private bool _isScaleRepeatButtonVisible;
        public bool IsScaleRepeatButtonVisible
        {
            get => _isScaleRepeatButtonVisible;
            set { if (_isScaleRepeatButtonVisible != value) { _isScaleRepeatButtonVisible = value; RaisePropertyChanged(); } }
        }

        // ── Constructor ──────────────────────────────────────────────────────────

        public WhatToPlayPage()
        {
            try
            {
                InitializeComponent();

                _session     = ServiceHelper.GetService<NoteSessionService>()!;
                _theme       = ServiceHelper.GetService<ThemeService>()!;
                _orientation = ServiceHelper.GetService<IOrientationService>()!;

                BindingContext = _session;

                // ── Instrument picker ────────────────────────────────────────────
                var instrumentOptions = NoteSessionService.InstrumentOptions.Cast<string>().ToArray();
                InstrumentPicker.ItemsSource = instrumentOptions;
                var instrumentShort = _session.Instrument?.Split(',')[0].Trim()
                                      ?? instrumentOptions[0].Split(',')[0].Trim();
                var selIdx = Array.FindIndex(instrumentOptions, s => s.Split(',')[0].Trim() == instrumentShort);
                InstrumentPicker.SelectedIndex = selIdx >= 0 ? selIdx : 0;
                SelectedInstrumentShort = instrumentOptions[InstrumentPicker.SelectedIndex].Split(',')[0].Trim();

                InstrumentPicker.SelectedIndexChanged += InstrumentPicker_SelectedIndexChanged;

                // ── Key picker ───────────────────────────────────────────────────
                KeyPicker.ItemsSource = new[]
                {
                    "C", "F", "Bb", "G", "D", "A", "E", "B", "F#", "C#",
                    "Eb", "Ab", "Db", "Gb", "Cb"
                };
                KeyPicker.SelectedIndex = Array.IndexOf((string[])KeyPicker.ItemsSource, _session.Key);
                if (KeyPicker.SelectedIndex < 0) KeyPicker.SelectedIndex = 0;
                _lastFreeKeyIndex = KeyPicker.SelectedIndex;

                KeyPicker.SelectedIndexChanged += OnKeyPickerChangedWithPrompt;

                // ── Play-mode pickers (Tunes | Scales | Arpeggios | Random/Tuner) ──
                InitializePlayModePickers();
                UpdatePlayModePickersFromSession(suppressClear: true);

                // ── Initial derived UI state ─────────────────────────────────────
                UpdateConcertKeyLabel();
                UpdateKeyPickerVisibility();
                UpdateRepeatButtonsVisibility();
                UpdateRepeatButtonColors();

                // ── ColorPickerDialog ────────────────────────────────────────────
                ColorPickerDialog.ColorPicked += (_, color) =>
                {
                    _theme.PanelBackgroundColor = color;
                    Preferences.Default.Set("StaffPanelColor", color.ToHex());
                };

                // ── Sync background color with ThemeService ──────────────────────
                PanelBackgroundColor = _theme.PanelBackgroundColor;
                _theme.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(ThemeService.PanelBackgroundColor))
                        MainThread.BeginInvokeOnMainThread(() => PanelBackgroundColor = _theme.PanelBackgroundColor);
                };

                // ── React to session changes from other pages ────────────────────
                _session.PropertyChanged += OnSessionPropertyChanged;
            }
            catch (Exception ex)
            {
                Utils.Log($"[WhatToPlayPage Constructor] ERROR: {ex}");
            }
        }

        // ── Page lifecycle ───────────────────────────────────────────────────────

        protected override void OnAppearing()
        {
            base.OnAppearing();
            SyncPickersFromSession();
            UpdateKeyPickerVisibility();
            UpdateConcertKeyLabel();
            UpdateRepeatButtonsVisibility();
            UpdateRepeatButtonColors();
            UpdateRandomModeWarning();
        }

        // ── Session → UI sync ────────────────────────────────────────────────────

        private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_localPlayModeChange) return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                switch (e.PropertyName)
                {
                    case nameof(NoteSessionService.Tune):
                        UpdateRepeatButtonsVisibility();
                        UpdateKeyPickerVisibility();
                        UpdatePlayModePickersFromSession();
                        UpdateRandomModeWarning();
                        break;
                    case nameof(NoteSessionService.CurrentTune):
                        UpdatePlayModePickersFromSession();
                        break;
                    case nameof(NoteSessionService.Key):
                        UpdateKeyPickerSelection();
                        UpdateConcertKeyLabel();
                        break;
                    case nameof(NoteSessionService.Instrument):
                        UpdateInstrumentPickerSelection();
                        break;
                    case nameof(NoteSessionService.SelectedScale):
                        UpdatePlayModePickersFromSession();
                        UpdateConcertKeyLabel();
                        break;
                    case nameof(NoteSessionService.IsRandomMode):
                        UpdateRepeatButtonsVisibility();
                        UpdatePlayModePickersFromSession();
                        UpdateRandomModeWarning();
                        break;
                }
            });
        }

        private void SyncPickersFromSession()
        {
            UpdateInstrumentPickerSelection();
            UpdateKeyPickerSelection();
            UpdatePlayModePickersFromSession();
            UpdateRandomModeWarning();
        }

        private void UpdateInstrumentPickerSelection()
        {
            if (InstrumentPicker.ItemsSource is not string[] items) return;
            var idx = Array.FindIndex(items, s => s == _session.Instrument);
            if (idx >= 0 && InstrumentPicker.SelectedIndex != idx)
                InstrumentPicker.SelectedIndex = idx;
            SelectedInstrumentShort = _session.Instrument?.Split(',')[0].Trim() ?? string.Empty;
        }

        private void UpdateKeyPickerSelection()
        {
            if (KeyPicker.ItemsSource is not string[] items) return;
            var idx = Array.IndexOf(items, _session.Key);
            if (idx >= 0 && KeyPicker.SelectedIndex != idx)
                KeyPicker.SelectedIndex = idx;
        }

        private void InitializePlayModePickers()
        {
            _tuneTitles   = TuneLibrary.All.Select(t => t.Title).ToArray();
            _scaleOptions = NoteSessionService.AvailableScales.ToArray();

            TunesPicker.ItemsSource        = _tuneTitles;
            ScalesPicker.ItemsSource       = _scaleOptions;
            ArpeggiosPicker.ItemsSource    = Array.Empty<string>();
            RandomTunerPicker.ItemsSource  = RandomTunerOptions;

            TunesPicker.SelectedIndexChanged       += OnTunesPickerChanged;
            ScalesPicker.SelectedIndexChanged      += OnScalesPickerChanged;
            RandomTunerPicker.SelectedIndexChanged += OnRandomTunerPickerChanged;
        }

        private void UpdatePlayModePickersFromSession(bool suppressClear = false)
        {
            if (_suppressPickerSync) return;

            _suppressPickerSync = true;
            try
            {
                ClearPlayModePickerSelections();

                if (_session.Tune == "Tuner")
                {
                    SetPickerSelection(RandomTunerPicker, "Tuner", RandomTunerOptions);
                }
                else if (_session.IsRandomMode)
                {
                    SetPickerSelection(RandomTunerPicker, "Random", RandomTunerOptions);
                }
                else if (_session.Tune == "Practice Tune")
                {
                    var title = _session.CurrentTune?.Title;
                    if (!string.IsNullOrEmpty(title))
                        SetPickerSelection(TunesPicker, title, _tuneTitles);
                }
                else
                {
                    SetPickerSelection(ScalesPicker, _session.SelectedScale, _scaleOptions);
                    var scaleIdx = Array.IndexOf(_scaleOptions, _session.SelectedScale);
                    if (scaleIdx >= 0)
                        _lastValidScaleIndex = scaleIdx;
                }
            }
            finally
            {
                _suppressPickerSync = false;
            }

            if (!suppressClear)
                UpdateRandomModeWarning();
        }

        private void ClearPlayModePickerSelections()
        {
            ClearPicker(TunesPicker);
            ClearPicker(ScalesPicker);
            ClearPicker(RandomTunerPicker);
        }

        private void ClearPicker(Picker picker)
        {
            if (picker.SelectedIndex < 0) return;
            picker.SelectedIndex = -1;
        }

        private void SetPickerSelection(Picker picker, string value, string[] options)
        {
            var idx = Array.IndexOf(options, value);
            if (idx >= 0 && picker.SelectedIndex != idx)
                picker.SelectedIndex = idx;
        }

        private void ClearOtherPlayModePickers(Picker activePicker)
        {
            _suppressPickerSync = true;
            try
            {
                if (activePicker != TunesPicker)       ClearPicker(TunesPicker);
                if (activePicker != ScalesPicker)      ClearPicker(ScalesPicker);
                if (activePicker != RandomTunerPicker) ClearPicker(RandomTunerPicker);
            }
            finally
            {
                _suppressPickerSync = false;
            }
        }

        private void RevertScalesPickerSelection()
        {
            _suppressPickerSync = true;
            try
            {
                if (_session.Tune == "Selected Scale" && !_session.IsRandomMode
                    && _lastValidScaleIndex >= 0 && _lastValidScaleIndex < _scaleOptions.Length)
                {
                    ScalesPicker.SelectedIndex = _lastValidScaleIndex;
                    return;
                }

                UpdatePlayModePickersFromSession();
            }
            finally
            {
                _suppressPickerSync = false;
            }
        }

        private void ApplyPlayModeSessionChange(Action apply)
        {
            _localPlayModeChange = true;
            try { apply(); }
            finally { _localPlayModeChange = false; }
        }

        private void UpdateRandomModeWarning()
        {
            RandomModeWarningLabel.IsVisible = _session.IsRandomMode && _session.Tune == "Practice Tune";
        }

        private void UpdateConcertKeyLabel()
        {
            ConcertKeyLabel.Text = $"(Concert {_session.GetConcertKey()})";
        }

        private void UpdateKeyPickerVisibility()
        {
            var hide = _session.Tune == "Tuner";
            KeyPicker.IsVisible       = !hide;
            KeyLabel.IsVisible        = !hide;
            KeyBorder.IsVisible       = !hide;
            ConcertKeyLabel.IsVisible = !hide;
        }

        private void UpdateRepeatButtonsVisibility()
        {
            var isRandom = _session.IsRandomMode;
            var isTuner  = _session.Tune == "Tuner";
            IsRandomRepeatButtonsVisible = !isTuner && isRandom;
            IsScaleRepeatButtonVisible   = !isTuner && !isRandom;
        }

        private void UpdateRepeatButtonColors()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                AutoRepeatNewButton.BackgroundColor  = _autoRepeat && !_repeatSameTune
                    ? Color.FromArgb("#008000") : Color.FromArgb("#8B4513");
                AutoRepeatSameButton.BackgroundColor = _autoRepeat && _repeatSameTune
                    ? Color.FromArgb("#008000") : Color.FromArgb("#8B4513");
                AutoRepeatScaleButton.BackgroundColor = _autoRepeat
                    ? Color.FromArgb("#008000") : Color.FromArgb("#8B4513");
            });
        }

        // ── Instrument picker handlers ───────────────────────────────────────────

        private void InstrumentPicker_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (InstrumentPicker.SelectedItem is string s)
            {
                SelectedInstrumentShort   = s.Split(',')[0].Trim();
                _session.Instrument       = s;
                IsInstrumentPickerVisible = false;
                IsInstrumentLabelVisible  = true;
                InstrumentPicker.Unfocus();
            }
        }

        private void InstrumentPicker_Unfocused(object? sender, EventArgs e)
        {
            IsInstrumentPickerVisible = false;
            IsInstrumentLabelVisible  = true;
        }

        private async void OnInstrumentLabelTapped(object? sender, EventArgs e)
        {
            try
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    IsInstrumentLabelVisible  = false;
                    IsInstrumentPickerVisible = true;
                    await Task.Delay(80);
                    InstrumentPicker.Focus();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WhatToPlayPage] OnInstrumentLabelTapped error: {ex}");
            }
        }

        // ── Key picker handler ───────────────────────────────────────────────────

        private async void OnKeyPickerChangedWithPrompt(object? sender, EventArgs e)
        {
            var selectedKey = KeyPicker.SelectedItem?.ToString();
            if (selectedKey == null) return;
            var shortKey = selectedKey.Split(',')[0].Trim();

            if (!FreeKeys.Contains(shortKey) && !StatusService.Instance.IsPremiumUser)
            {
                var purchased = await PremiumPromptHelper.ShowAsync(this,
                    onDecline: () => KeyPicker.SelectedIndex = _lastFreeKeyIndex);
                if (!purchased) return;
            }
            else
            {
                _lastFreeKeyIndex = KeyPicker.SelectedIndex;
            }

            _session.Key = shortKey;
        }

        // ── Play-mode picker handlers ────────────────────────────────────────────

        private void OnTunesPickerChanged(object? sender, EventArgs e)
        {
            if (_suppressPickerSync) return;

            var idx = TunesPicker.SelectedIndex;
            if (idx < 0 || idx >= _tuneTitles.Length) return;
            var selected = _tuneTitles[idx];

            var practiceTune = TuneLibrary.All.FirstOrDefault(t => t.Title == selected);
            if (practiceTune == null) return;

            ClearOtherPlayModePickers(TunesPicker);

            ApplyPlayModeSessionChange(() =>
            {
                _session.IsRandomMode = false;
                _session.SelectPracticeTune(practiceTune);
            });
            Preferences.Default.Set("SelectedTune", selected);
            UpdateKeyPickerVisibility();
            UpdateRepeatButtonsVisibility();
            UpdateRandomModeWarning();
        }

        private async void OnScalesPickerChanged(object? sender, EventArgs e)
        {
            if (_suppressPickerSync) return;

            var idx = ScalesPicker.SelectedIndex;
            if (idx < 0 || idx >= _scaleOptions.Length) return;
            var selected = _scaleOptions[idx];

            if (!FreeScales.Contains(selected) && !StatusService.Instance.IsPremiumUser)
            {
                var purchased = await PremiumPromptHelper.ShowAsync(this,
                    onDecline: RevertScalesPickerSelection);
                if (!purchased)
                    return;
            }

            ClearOtherPlayModePickers(ScalesPicker);

            _lastValidScaleIndex = idx;
            ApplyPlayModeSessionChange(() =>
            {
                _session.IsRandomMode  = false;
                _session.Tune          = "Selected Scale";
                _session.SelectedScale = selected;
            });
            Preferences.Default.Set("SelectedTune", selected);
            UpdateKeyPickerVisibility();
            UpdateRepeatButtonsVisibility();
            UpdateRandomModeWarning();
        }

        private void OnRandomTunerPickerChanged(object? sender, EventArgs e)
        {
            if (_suppressPickerSync) return;

            var idx = RandomTunerPicker.SelectedIndex;
            if (idx < 0 || idx >= RandomTunerOptions.Length) return;
            var selected = RandomTunerOptions[idx];

            ClearOtherPlayModePickers(RandomTunerPicker);

            if (selected == "Tuner")
            {
                ApplyPlayModeSessionChange(() =>
                {
                    _session.IsRandomMode = false;
                    _session.Tune = "Tuner";
                });
                Preferences.Default.Set("SelectedTune", "Tuner");
                UpdateKeyPickerVisibility();
            }
            else if (selected == "Random")
            {
                ApplyPlayModeSessionChange(() =>
                {
                    _session.IsRandomMode = true;
                    if (_session.Tune != "Practice Tune" && _session.Tune != "Tuner")
                        _session.Tune = "Selected Scale";
                });
                Preferences.Default.Set("SelectedTune", "Random");
            }

            UpdateRepeatButtonsVisibility();
            UpdateRandomModeWarning();
        }

        // ── Repeat button handlers ───────────────────────────────────────────────

        private void OnAutoRepeatScaleClicked(object? sender, EventArgs e)
        {
            _autoRepeat     = !_autoRepeat;
            _repeatSameTune = false;
            UpdateRepeatButtonColors();
        }

        private void OnAutoRepeatNewClicked(object? sender, EventArgs e)
        {
            if (_autoRepeat && !_repeatSameTune)
            {
                _autoRepeat     = false;
                _repeatSameTune = false;
            }
            else
            {
                _autoRepeat     = true;
                _repeatSameTune = false;
            }
            UpdateRepeatButtonColors();
        }

        private void OnAutoRepeatSameClicked(object? sender, EventArgs e)
        {
            if (_autoRepeat && _repeatSameTune)
            {
                _autoRepeat     = false;
                _repeatSameTune = false;
            }
            else
            {
                _autoRepeat     = true;
                _repeatSameTune = true;
                if (_session.NotesToDraw != null)
                    _savedNotesToRepeat = new List<NoteInfo>(_session.NotesToDraw);
            }
            UpdateRepeatButtonColors();
        }

        // ── Navigation ───────────────────────────────────────────────────────────

        private async void OnNavigateHomeClicked(object? sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//MainPage");
        }

        // ── Background Color handler ─────────────────────────────────────────────

        private async void OnColorButtonClicked(object? sender, EventArgs e)
        {
            await Task.Yield();
            ColorPickerDialog.Show(_theme.PanelBackgroundColor);
        }

        // ── INotifyPropertyChanged ───────────────────────────────────────────────

        public new event PropertyChangedEventHandler? PropertyChanged;

        private void RaisePropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
