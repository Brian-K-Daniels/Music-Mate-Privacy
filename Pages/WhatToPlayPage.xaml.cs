using musicmate.Models;
using musicmate.Services;
using musicmate.Utilities;
using musicmate.V3LayoutDebug;
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
        private ArpeggioPickerChoice[] _arpeggioOptions = Array.Empty<ArpeggioPickerChoice>();
        private static readonly string[] RandomTunerOptions = { "Random", "Tuner", "Fixed Tune" };

        private sealed record ArpeggioPickerChoice(
            string PickerLabel,
            string DisplayLabel,
            ArpeggioPattern Pattern,
            string RootNote,
            string KeySignature);

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
                var instrumentShort = _session.InstrumentDisplayName;
                var selIdx = Array.FindIndex(instrumentOptions, s => s == instrumentShort);
                InstrumentPicker.SelectedIndex = selIdx >= 0 ? selIdx : 0;
                SelectedInstrumentShort = _session.InstrumentDisplayName;

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
                if (_localPlayModeChange) return;

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
                        RefreshArpeggioPickerOptions();
                        UpdatePlayModePickersFromSession();
                        break;
                    case nameof(NoteSessionService.Instrument):
                        UpdateInstrumentPickerSelection();
                        break;
                    case nameof(NoteSessionService.SelectedScale):
                    case nameof(NoteSessionService.SelectedArpeggioDisplay):
                    case nameof(NoteSessionService.ChildLevel):
                        RefreshArpeggioPickerOptions();
                        UpdatePlayModePickersFromSession();
                        UpdateKeyPickerVisibility();
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
            var idx = Array.FindIndex(items, s => s == _session.InstrumentDisplayName);
            if (idx >= 0 && InstrumentPicker.SelectedIndex != idx)
                InstrumentPicker.SelectedIndex = idx;
            SelectedInstrumentShort = _session.InstrumentDisplayName;
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
            RefreshArpeggioPickerOptions();
            RandomTunerPicker.ItemsSource  = RandomTunerOptions;

            TunesPicker.SelectedIndexChanged       += OnTunesPickerChanged;
            ScalesPicker.SelectedIndexChanged      += OnScalesPickerChanged;
            RandomTunerPicker.SelectedIndexChanged += OnRandomTunerPickerChanged;
        }

        private void RefreshArpeggioPickerOptions()
        {
            int level = _session.ChildLevel > 0 ? _session.ChildLevel : 1;
            var availability = ArpeggioCatalog.GetAvailabilityForLevel(level);
            var choices = new List<ArpeggioPickerChoice>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string selectedKey = NormalizeMajorKeyName(_session.Key);

            foreach (string rootKey in GetArpeggioRootKeys())
            {
                foreach (var pattern in availability.Patterns)
                {
                    string rootNote = ChooseArpeggioRootInRange(rootKey);
                    string displayLabel = $"{TrimOctave(rootNote)} {pattern.DisplayName.ToLowerInvariant()}";
                    if (!seen.Add($"{rootNote}|{pattern.Id}"))
                        continue;

                    var choice = new ArpeggioPickerChoice(
                        PickerLabel: displayLabel,
                        DisplayLabel: displayLabel,
                        Pattern: pattern,
                        RootNote: rootNote,
                        KeySignature: GetArpeggioKeySignature(pattern, rootNote));

                    bool matchesSelectedKey = choice.KeySignature == selectedKey;
                    choices.Add(choice with
                    {
                        PickerLabel = matchesSelectedKey ? $"★ {displayLabel}" : displayLabel
                    });
                }
            }

            _arpeggioOptions = choices
                .OrderByDescending(choice => choice.KeySignature == selectedKey)
                .ThenBy(choice => TrimOctave(choice.RootNote), StringComparer.Ordinal)
                .ThenBy(choice => choice.Pattern.FirstAvailableLevel)
                .ThenBy(choice => choice.Pattern.DisplayName, StringComparer.Ordinal)
                .ToArray();
            ArpeggiosPicker.ItemsSource = _arpeggioOptions.Select(o => o.PickerLabel).ToArray();
            if (_session.Tune == "Arpeggio")
            {
                var wasSuppressed = _suppressPickerSync;
                _suppressPickerSync = true;
                try
                {
                    SetArpeggioPickerSelection();
                }
                finally
                {
                    _suppressPickerSync = wasSuppressed;
                }
            }
        }

        private static IEnumerable<string> GetArpeggioRootKeys()
        {
            yield return "C";
            yield return "G";
            yield return "D";
            yield return "A";
            yield return "E";
            yield return "B";
            yield return "F#";
            yield return "C#";
            yield return "F";
            yield return "Bb";
            yield return "Eb";
            yield return "Ab";
            yield return "Db";
            yield return "Gb";
            yield return "Cb";
        }

        private string ChooseArpeggioRootInRange(string rootKey)
        {
            const int baseOctave = 4;
            int rootMidi = NoteSessionService.NoteNameToMidi($"{rootKey}{baseOctave}");
            int minMidi = NoteSessionService.NoteNameToMidi(_session.LowestNote);
            int maxMidi = NoteSessionService.NoteNameToMidi(_session.HighestNote);
            if (minMidi < 0 || maxMidi < minMidi)
                return $"{rootKey}{baseOctave}";

            int candidate = rootMidi;
            while (candidate < minMidi)
                candidate += 12;
            while (candidate > maxMidi)
                candidate -= 12;

            int octave = baseOctave + ((candidate - rootMidi) / 12);
            return $"{rootKey}{octave}";
        }

        private static int GetScaleDegreeMidi(string key, string scale, int degree)
        {
            int[] intervals = scale switch
            {
                "Natural Minor" or "Aeolian" => new[] { 0, 2, 3, 5, 7, 8, 10 },
                "Harmonic Minor" => new[] { 0, 2, 3, 5, 7, 8, 11 },
                "Melodic Minor" or "Jazz Melodic Minor" => new[] { 0, 2, 3, 5, 7, 9, 11 },
                "Dorian" => new[] { 0, 2, 3, 5, 7, 9, 10 },
                "Phrygian" => new[] { 0, 1, 3, 5, 7, 8, 10 },
                "Lydian" => new[] { 0, 2, 4, 6, 7, 9, 11 },
                "Mixolydian" => new[] { 0, 2, 4, 5, 7, 9, 10 },
                "Locrian" => new[] { 0, 1, 3, 5, 6, 8, 10 },
                "Major Pentatonic" => new[] { 0, 2, 4, 7, 9, 12, 14 },
                "Minor Pentatonic" => new[] { 0, 3, 5, 7, 10, 12, 15 },
                "Blues" or "Minor Blues" => new[] { 0, 3, 5, 6, 7, 10, 12 },
                _ => new[] { 0, 2, 4, 5, 7, 9, 11 }
            };

            int tonicMidi = NoteSessionService.NoteNameToMidi($"{key}4");
            int idx = Math.Clamp(degree, 1, 7) - 1;
            return tonicMidi + intervals[idx % intervals.Length];
        }

        private static bool KeyPrefersFlats(string key)
            => key is "F" or "Bb" or "Eb" or "Ab" or "Db" or "Gb" or "Cb";

        private static string TrimOctave(string noteName)
            => new(noteName.TakeWhile(c => !char.IsDigit(c)).ToArray());

        private static string GetArpeggioKeySignature(ArpeggioPickerChoice choice)
            => choice.KeySignature;

        private static string GetArpeggioKeySignature(ArpeggioPattern pattern, string rootNote)
        {
            var root = NormalizeMajorKeyName(TrimOctave(rootNote));

            // The app currently stores major key-signature names only. Minor-family
            // arpeggios therefore use their relative major signature until minor keys are modeled.
            if (UsesMinorFamilyKeySignature(pattern))
                return RelativeMajorKeyForMinorRoot(root);

            return root;
        }

        private static bool UsesMinorFamilyKeySignature(ArpeggioPattern pattern)
            => pattern.SemitoneIntervals.Contains(3) && !pattern.SemitoneIntervals.Contains(4);

        private static string RelativeMajorKeyForMinorRoot(string minorRoot) => minorRoot switch
        {
            "A" => "C",
            "E" => "G",
            "B" => "D",
            "F#" => "A",
            "C#" => "E",
            "G#" => "B",
            "D#" => "F#",
            "A#" => "C#",
            "D" => "F",
            "G" => "Bb",
            "C" => "Eb",
            "F" => "Ab",
            "Bb" => "Db",
            "Eb" => "Gb",
            "Ab" => "Cb",
            _ => minorRoot
        };

        private static string NormalizeMajorKeyName(string key) => key switch
        {
            "A#" => "Bb",
            "D#" => "Eb",
            "G#" => "Ab",
            "E#" => "F",
            "B#" => "C",
            "Fb" => "E",
            _ => key
        };

        private void UpdatePlayModePickersFromSession(bool suppressClear = false)
        {
            if (_suppressPickerSync) return;

            _suppressPickerSync = true;
            try
            {
                Picker? activePicker = GetActivePlayModePicker();
                ClearInactivePlayModePickerSelections(activePicker);

                if (V3LayoutTestTune.IsEnabled)
                {
                    SetPickerSelection(RandomTunerPicker, "Fixed Tune", RandomTunerOptions);
                }
                else if (_session.Tune == "Tuner")
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
                else if (_session.Tune == "Arpeggio")
                {
                    SetArpeggioPickerSelection();
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

        private Picker? GetActivePlayModePicker()
        {
            if (V3LayoutTestTune.IsEnabled || _session.Tune == "Tuner" || _session.IsRandomMode)
                return RandomTunerPicker;
            if (_session.Tune == "Practice Tune")
                return TunesPicker;
            if (_session.Tune == "Arpeggio")
                return ArpeggiosPicker;
            return ScalesPicker;
        }

        private void ClearPlayModePickerSelections()
        {
            ClearPicker(TunesPicker);
            ClearPicker(ScalesPicker);
            ClearPicker(ArpeggiosPicker);
            ClearPicker(RandomTunerPicker);
        }

        private void ClearInactivePlayModePickerSelections(Picker? activePicker)
        {
            if (activePicker != TunesPicker)       ClearPicker(TunesPicker);
            if (activePicker != ScalesPicker)      ClearPicker(ScalesPicker);
            if (activePicker != ArpeggiosPicker)   ClearPicker(ArpeggiosPicker);
            if (activePicker != RandomTunerPicker) ClearPicker(RandomTunerPicker);
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

        private void SetArpeggioPickerSelection()
        {
            var idx = Array.FindIndex(_arpeggioOptions, choice =>
                choice.Pattern.Id == _session.SelectedArpeggioId
                && choice.RootNote == _session.SelectedArpeggioRoot);

            if (idx < 0)
                idx = Array.FindIndex(_arpeggioOptions, choice =>
                    choice.DisplayLabel == _session.SelectedArpeggioDisplay);

            if (idx >= 0 && ArpeggiosPicker.SelectedIndex != idx)
                ArpeggiosPicker.SelectedIndex = idx;
        }

        private void ClearOtherPlayModePickers(Picker activePicker)
        {
            _suppressPickerSync = true;
            try
            {
                if (activePicker != TunesPicker)       ClearPicker(TunesPicker);
                if (activePicker != ScalesPicker)      ClearPicker(ScalesPicker);
                if (activePicker != ArpeggiosPicker)   ClearPicker(ArpeggiosPicker);
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
            var show = _session.Tune != "Tuner" && _session.Tune != "Arpeggio";
            KeyPicker.IsVisible       = show;
            KeyPicker.IsEnabled       = show;
            KeyLabel.IsVisible        = show;
            KeyBorder.IsVisible       = show;
            ConcertKeyLabel.IsVisible = show;
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

        private static Task NavigateToPracticePageAsync()
            => Shell.Current.GoToAsync("//MainPage");

        // ── Instrument picker handlers ───────────────────────────────────────────

        private void InstrumentPicker_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (InstrumentPicker.SelectedItem is string s)
            {
                _session.Instrument       = s;
                SelectedInstrumentShort   = _session.InstrumentDisplayName;
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
            if (_suppressPickerSync) return;

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
            await NavigateToPracticePageAsync();
        }

        // ── Play-mode picker handlers ────────────────────────────────────────────

        private async void OnTunesPickerChanged(object? sender, EventArgs e)
        {
            if (_suppressPickerSync) return;

            var idx = TunesPicker.SelectedIndex;
            if (idx < 0 || idx >= _tuneTitles.Length) return;
            var selected = _tuneTitles[idx];

            var practiceTune = TuneLibrary.All.FirstOrDefault(t => t.Title == selected);
            if (practiceTune == null) return;

            ClearOtherPlayModePickers(TunesPicker);
            V3LayoutTestTune.SetEnabled(false);

            ApplyPlayModeSessionChange(() =>
            {
                _session.IsRandomMode = false;
                _session.SelectPracticeTune(practiceTune);
            });
            Preferences.Default.Set("SelectedTune", selected);
            UpdateKeyPickerVisibility();
            UpdateRepeatButtonsVisibility();
            UpdateRandomModeWarning();
            await NavigateToPracticePageAsync();
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
            V3LayoutTestTune.SetEnabled(false);

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
            await NavigateToPracticePageAsync();
        }

        private async void OnArpeggiosPickerChanged(object? sender, EventArgs e)
        {
            if (_suppressPickerSync) return;

            var idx = ArpeggiosPicker.SelectedIndex;
            if (idx < 0 || idx >= _arpeggioOptions.Length) return;
            var selected = _arpeggioOptions[idx];

            ClearOtherPlayModePickers(ArpeggiosPicker);
            V3LayoutTestTune.SetEnabled(false);

            ApplyPlayModeSessionChange(() =>
            {
                _session.IsRandomMode = false;
                _session.Key = GetArpeggioKeySignature(selected);
                _session.SelectArpeggio(selected.Pattern, selected.RootNote, selected.DisplayLabel);
            });
            Preferences.Default.Set("SelectedTune", selected.DisplayLabel);
            UpdateKeyPickerSelection();
            UpdateConcertKeyLabel();
            UpdateKeyPickerVisibility();
            UpdateRepeatButtonsVisibility();
            UpdateRandomModeWarning();
            await NavigateToPracticePageAsync();
        }

        private async void OnRandomTunerPickerChanged(object? sender, EventArgs e)
        {
            if (_suppressPickerSync) return;

            var idx = RandomTunerPicker.SelectedIndex;
            if (idx < 0 || idx >= RandomTunerOptions.Length) return;
            var selected = RandomTunerOptions[idx];

            ClearOtherPlayModePickers(RandomTunerPicker);

            if (selected == "Tuner")
            {
                V3LayoutTestTune.SetEnabled(false);
                ApplyPlayModeSessionChange(() =>
                {
                    _session.Tune = "Tuner";
                    _session.IsRandomMode = false;
                });
                Preferences.Default.Set("SelectedTune", "Tuner");
            }
            else if (selected == "Random")
            {
                V3LayoutTestTune.SetEnabled(false);
                ApplyPlayModeSessionChange(() =>
                {
                    _session.Tune = "Selected Scale";
                    _session.IsRandomMode = true;
                });
                Preferences.Default.Set("SelectedTune", "Random");
            }
            else if (selected == "Fixed Tune")
            {
                V3LayoutTestTune.SetEnabled(true);
                ApplyPlayModeSessionChange(() =>
                {
                    _session.IsRandomMode = false;
                    if (_session.Tune == "Tuner")
                        _session.Tune = "Selected Scale";
                });
                Preferences.Default.Set("SelectedTune", "Fixed Tune");
            }

            UpdatePlayModePickersFromSession(suppressClear: true);
            UpdateKeyPickerVisibility();
            UpdateRepeatButtonsVisibility();
            UpdateRandomModeWarning();
            await NavigateToPracticePageAsync();
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
