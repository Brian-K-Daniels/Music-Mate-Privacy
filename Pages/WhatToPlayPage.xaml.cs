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

        private int  _lastFreeKeyIndex        = 0;
        private int  _lastValidScaleTuneIndex = 0;
        private bool _autoRepeat              = false;
        private bool _repeatSameTune          = false;
        private List<NoteInfo>? _savedNotesToRepeat = null;

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

                // ── Scale / Tune picker ──────────────────────────────────────────
                var practiceTuneTitles = TuneLibrary.All.Select(t => t.Title).ToArray();
                var scaleTuneOptions   = new[] { "Tuner" }
                    .Concat(practiceTuneTitles)
                    .Concat(NoteSessionService.AvailableScales)
                    .ToArray();
                ScaleTunePicker.ItemsSource = scaleTuneOptions;

                var initialSelection = _session.Tune == "Tuner"        ? "Tuner"
                    : _session.Tune == "Practice Tune"                 ? (_session.CurrentTune?.Title ?? practiceTuneTitles[0])
                    : _session.SelectedScale;
                var stIdx = Array.IndexOf(scaleTuneOptions, initialSelection);
                ScaleTunePicker.SelectedIndex    = stIdx >= 0 ? stIdx : 0;
                _lastValidScaleTuneIndex         = ScaleTunePicker.SelectedIndex;

                ScaleTunePicker.SelectedIndexChanged += OnScaleTunePickerChanged;

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
            // Sync Random checkbox and warning from session state
            RandomModeCheckBox.IsChecked = _session.IsRandomMode;
            RandomModeWarningLabel.IsVisible = _session.IsRandomMode && _session.Tune == "Practice Tune";
        }

        // ── Session → UI sync ────────────────────────────────────────────────────

        private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                switch (e.PropertyName)
                {
                    case nameof(NoteSessionService.Tune):
                        UpdateRepeatButtonsVisibility();
                        UpdateKeyPickerVisibility();
                        UpdateScaleTunePickerSelection();
                        break;
                    case nameof(NoteSessionService.CurrentTune):
                        UpdateScaleTunePickerSelection();
                        break;
                    case nameof(NoteSessionService.Key):
                        UpdateKeyPickerSelection();
                        UpdateConcertKeyLabel();
                        break;
                    case nameof(NoteSessionService.Instrument):
                        UpdateInstrumentPickerSelection();
                        break;
                    case nameof(NoteSessionService.SelectedScale):
                        UpdateScaleTunePickerSelection();
                        UpdateConcertKeyLabel();
                        break;
                    case nameof(NoteSessionService.IsRandomMode):
                        UpdateRepeatButtonsVisibility();
                        RandomModeCheckBox.IsChecked = _session.IsRandomMode;
                        RandomModeWarningLabel.IsVisible = _session.IsRandomMode && _session.Tune == "Practice Tune";
                        break;
                }
            });
        }

        private void SyncPickersFromSession()
        {
            UpdateInstrumentPickerSelection();
            UpdateKeyPickerSelection();
            UpdateScaleTunePickerSelection();
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

        private void UpdateScaleTunePickerSelection()
        {
            if (ScaleTunePicker.ItemsSource is not string[] items) return;
            var selection = _session.Tune == "Tuner"        ? "Tuner"
                : _session.Tune == "Practice Tune"          ? (_session.CurrentTune?.Title ?? string.Empty)
                : _session.SelectedScale;
            var idx = Array.IndexOf(items, selection);
            if (idx >= 0 && ScaleTunePicker.SelectedIndex != idx)
                ScaleTunePicker.SelectedIndex = idx;
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

        // ── Scale / Tune picker handler ──────────────────────────────────────────

        private async void OnScaleTunePickerChanged(object? sender, EventArgs e)
        {
            if (ScaleTunePicker.SelectedItem is not string selected) return;

            var practiceTune = TuneLibrary.All.FirstOrDefault(t => t.Title == selected);
            if (practiceTune != null)
            {
                _lastValidScaleTuneIndex = ScaleTunePicker.SelectedIndex;
                _session.SelectPracticeTune(practiceTune);
                Preferences.Default.Set("SelectedTune", selected);
                UpdateKeyPickerVisibility();
                return;
            }

            if (selected == "Tuner")
            {
                _lastValidScaleTuneIndex = ScaleTunePicker.SelectedIndex;
                _session.Tune = selected;
                Preferences.Default.Set("SelectedTune", selected);
                UpdateKeyPickerVisibility();
                return;
            }

            // Scale — premium check for non-free scales
            if (!FreeScales.Contains(selected) && !StatusService.Instance.IsPremiumUser)
            {
                var purchased = await PremiumPromptHelper.ShowAsync(this,
                    onDecline: () => ScaleTunePicker.SelectedIndex = _lastValidScaleTuneIndex);
                if (!purchased) return;
            }

            _lastValidScaleTuneIndex = ScaleTunePicker.SelectedIndex;
            _session.Tune            = "Selected Scale";
            _session.SelectedScale   = selected;
            Preferences.Default.Set("SelectedTune", selected);
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

        // ── Random checkbox handlers ─────────────────────────────────────────────

        private void OnRandomModeCheckBoxChanged(object? sender, CheckedChangedEventArgs e)
        {
            ApplyRandomModeChange(e.Value);
        }

        private void OnRandomModeLabelTapped(object? sender, EventArgs e)
        {
            RandomModeCheckBox.IsChecked = !RandomModeCheckBox.IsChecked;
        }

        private void ApplyRandomModeChange(bool isRandom)
        {
            _session.IsRandomMode = isRandom;
            bool isPracticeTune = _session.Tune == "Practice Tune";
            RandomModeWarningLabel.IsVisible = isRandom && isPracticeTune;
            UpdateRepeatButtonsVisibility();
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
