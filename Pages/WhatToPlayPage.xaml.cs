using musicmate.Models;

using musicmate.Services;

using musicmate.Utilities;

using musicmate.LayoutDebug;
using musicmate.Diagnostics;

using System.ComponentModel;

using System.Diagnostics;



namespace musicmate.Pages

{

    /// "What to Play" page — Tunes / Scales / Arpeggios / Other pickers plus repeat
    /// and background-color controls. Instrument and key are managed elsewhere.

    public partial class WhatToPlayPage : ContentPage, INotifyPropertyChanged

    {

        private readonly NoteSessionService _session = null!;

        private readonly ThemeService _theme = null!;



        private static readonly HashSet<string> FreeScales = new() { "Major", "Harmonic Minor" };



        private int _lastValidScaleIndex = 0;

        private bool _suppressPickerSync = false;

        private bool _localPlayModeChange = false;

        private bool _isPageVisible = false;



        private string[] _tuneTitles = Array.Empty<string>();

        private string[] _scaleOptions = Array.Empty<string>();

        private ArpeggioPickerChoice[] _arpeggioOptions = Array.Empty<ArpeggioPickerChoice>();



        private sealed record ArpeggioPickerChoice(

            string PickerLabel,

            string DisplayLabel,

            ArpeggioPattern Pattern,

            string RootNote,

            string KeySignature);



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



        public WhatToPlayPage()

        {

            try

            {

                InitializeComponent();



                _session = ServiceHelper.GetService<NoteSessionService>()!;

                _theme = ServiceHelper.GetService<ThemeService>()!;



                BindingContext = _session;



                InitializePlayModePickers();

                UpdatePlayModePickersFromSession(suppressClear: true);



                UpdateRepeatButtonsVisibility();

                UpdateRepeatButtonColors();

                PanelBackgroundColor = _theme.PanelBackgroundColor;

                _theme.PropertyChanged += (s, e) =>

                {

                    if (e.PropertyName == nameof(ThemeService.PanelBackgroundColor))

                        MainThread.BeginInvokeOnMainThread(() => PanelBackgroundColor = _theme.PanelBackgroundColor);

                };



                _session.PropertyChanged += OnSessionPropertyChanged;

            }

            catch (Exception ex)

            {

                Utils.Log($"[WhatToPlayPage Constructor] ERROR: {ex}");

            }

        }



        protected override void OnAppearing()

        {

            base.OnAppearing();

            _isPageVisible = true;

            SyncPickersFromSession();

            UpdateRepeatButtonsVisibility();

            UpdateRepeatButtonColors();

            UpdateRandomModeWarning();

        }



        protected override void OnDisappearing()

        {

            _isPageVisible = false;

            base.OnDisappearing();

        }



        private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)

        {

            if (_localPlayModeChange || !_isPageVisible) return;



            MainThread.BeginInvokeOnMainThread(() =>

            {

                if (_localPlayModeChange || !_isPageVisible) return;



                switch (e.PropertyName)

                {

                    case nameof(NoteSessionService.Tune):

                        UpdateRepeatButtonsVisibility();

                        UpdatePlayModePickersFromSession();

                        UpdateRandomModeWarning();

                        break;

                    case nameof(NoteSessionService.CurrentTune):

                        UpdatePlayModePickersFromSession();

                        break;

                    case nameof(NoteSessionService.Key):

                    case nameof(NoteSessionService.SelectedScale):

                    case nameof(NoteSessionService.ScaleSelectionMode):

                    case nameof(NoteSessionService.SelectedArpeggioDisplay):

                    case nameof(NoteSessionService.ChildLevel):

                        RefreshArpeggioPickerOptions();

                        UpdatePlayModePickersFromSession();

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

            UpdatePlayModePickersFromSession();

            UpdateRandomModeWarning();

        }



        private void InitializePlayModePickers()

        {

            PlayModePickerOptions.MigrateLegacySessionSelection(_session);



            _tuneTitles = PlayModePickerOptions.BuildTunePickerOptions();

            _scaleOptions = NoteSessionService.ScalePickerOptions;



            TunesPicker.ItemsSource = _tuneTitles;

            ScalesPicker.ItemsSource = _scaleOptions;

            RefreshArpeggioPickerOptions();

            OtherPicker.ItemsSource = PlayModePickerOptions.OtherOptions;



            // SelectedIndexChanged handlers are wired in WhatToPlayPage.xaml.

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



        private static string TrimOctave(string noteName)

            => new(noteName.TakeWhile(c => !char.IsDigit(c)).ToArray());



        private static string GetArpeggioKeySignature(ArpeggioPickerChoice choice)

            => choice.KeySignature;



        private static string GetArpeggioKeySignature(ArpeggioPattern pattern, string rootNote)

        {

            var root = NormalizeMajorKeyName(TrimOctave(rootNote));



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



                if (PlayModePickerOptions.UsesOtherPicker(_session, LayoutTestTune.IsEnabled))

                {

                    var otherSelection = PlayModePickerOptions.ResolveOtherSelection(

                        _session, LayoutTestTune.IsEnabled);

                    SetPickerSelection(OtherPicker, otherSelection, PlayModePickerOptions.OtherOptions);

                }

                else if (LayoutTestTune.IsEnabled

                         || PlayModePickerOptions.IsRhythmNoteTuneSelection(

                             Preferences.Default.Get<string?>("SelectedTune", null)))

                {

                    SetPickerSelection(

                        TunesPicker,

                        PlayModePickerOptions.HalfThroughSixteenthNotes,

                        _tuneTitles);

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

                else if (_session.ScaleSelectionMode == ScaleSelectionMode.Named)

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

            if (LayoutTestTune.IsEnabled

                || PlayModePickerOptions.IsRhythmNoteTuneSelection(

                    Preferences.Default.Get<string?>("SelectedTune", null)))

                return TunesPicker;

            if (PlayModePickerOptions.UsesOtherPicker(_session, LayoutTestTune.IsEnabled))

                return OtherPicker;

            if (_session.Tune == "Practice Tune")

                return TunesPicker;

            if (_session.Tune == "Arpeggio")

                return ArpeggiosPicker;

            return ScalesPicker;

        }



        private void ClearInactivePlayModePickerSelections(Picker? activePicker)

        {

            if (activePicker != TunesPicker) ClearPicker(TunesPicker);

            if (activePicker != ScalesPicker) ClearPicker(ScalesPicker);

            if (activePicker != ArpeggiosPicker) ClearPicker(ArpeggiosPicker);

            if (activePicker != OtherPicker) ClearPicker(OtherPicker);

        }



        private void ClearPicker(Picker picker)

        {

            if (picker.SelectedIndex < 0)

                return;

            picker.SelectedIndex = -1;

        }



        private void SetPickerSelection(Picker picker, string value, string[] options)

        {

            var idx = Array.IndexOf(options, value);

            if (idx < 0 || picker.SelectedIndex == idx)

                return;

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

                if (activePicker != TunesPicker) ClearPicker(TunesPicker);

                if (activePicker != ScalesPicker) ClearPicker(ScalesPicker);

                if (activePicker != ArpeggiosPicker) ClearPicker(ArpeggiosPicker);

                if (activePicker != OtherPicker) ClearPicker(OtherPicker);

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

                if (_session.ScaleSelectionMode == ScaleSelectionMode.Named

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



        private void UpdateRepeatButtonsVisibility()

        {

            var isRandom = _session.IsRandomMode;

            var isTuner = _session.Tune == "Tuner";

            IsRandomRepeatButtonsVisible = !isTuner && isRandom;

            IsScaleRepeatButtonVisible = !isTuner && !isRandom;

        }



        private void UpdateRepeatButtonColors()

        {

            MainThread.BeginInvokeOnMainThread(() =>

            {

                AutoRepeatNewButton.BackgroundColor = _session.AutoRepeat && !_session.RepeatSameTune

                    ? Color.FromArgb("#008000") : Color.FromArgb("#8B4513");

                AutoRepeatSameButton.BackgroundColor = _session.AutoRepeat && _session.RepeatSameTune

                    ? Color.FromArgb("#008000") : Color.FromArgb("#8B4513");

                AutoRepeatScaleButton.BackgroundColor = _session.AutoRepeat

                    ? Color.FromArgb("#008000") : Color.FromArgb("#8B4513");

            });

        }



        private async void OnTunesPickerChanged(object? sender, EventArgs e)

        {

            if (_suppressPickerSync) return;



            var idx = TunesPicker.SelectedIndex;

            if (idx < 0 || idx >= _tuneTitles.Length) return;

            var selected = _tuneTitles[idx];



            ClearOtherPlayModePickers(TunesPicker);



            if (PlayModePickerOptions.IsRhythmNoteTuneSelection(selected)

                || selected == PlayModePickerOptions.HalfThroughSixteenthNotes)

            {

                LayoutTestTune.SetEnabled(true);

                ApplyPlayModeSessionChange(() =>

                    PlayModePickerOptions.ApplyRhythmNoteTuneSelection(_session));

                UpdateRepeatButtonsVisibility();

                UpdateRandomModeWarning();

                return;

            }



            var practiceTune = TuneLibrary.All.FirstOrDefault(t => t.Title == selected);

            if (practiceTune == null) return;



            LayoutTestTune.SetEnabled(false);



            ApplyPlayModeSessionChange(() =>

            {

                _session.IsRandomMode = false;

                _session.SelectPracticeTune(practiceTune);

            });

            Preferences.Default.Set("SelectedTune", selected);

            UpdateRepeatButtonsVisibility();

            UpdateRandomModeWarning();

        }



        private async void OnScalesPickerChanged(object? sender, EventArgs e)

        {

            if (_suppressPickerSync) return;



            var idx = ScalesPicker.SelectedIndex;

            if (idx < 0 || idx >= _scaleOptions.Length) return;

            var selected = _scaleOptions[idx];



            if (!NoteSessionService.IsNamedScaleOption(selected))

                return;



            if (!FreeScales.Contains(selected)

                && !StatusService.Instance.IsPremiumUser)

            {

                var purchased = await PremiumPromptHelper.ShowAsync(this,

                    onDecline: RevertScalesPickerSelection);

                if (!purchased)

                    return;

            }



            ClearOtherPlayModePickers(ScalesPicker);

            LayoutTestTune.SetEnabled(false);



            string? rejectionReason = null;

            ApplyPlayModeSessionChange(() =>

            {

                _session.IsRandomMode = false;

                _session.RepeatSameTune = false;

                _session.Tune = "Selected Scale";

                if (!_session.TryApplyScalePickerSelection(selected, out rejectionReason))

                {

                    var fallbackIdx = Array.IndexOf(_scaleOptions, _session.SelectedScale);

                    if (fallbackIdx >= 0)

                        _lastValidScaleIndex = fallbackIdx;

                }

                else

                {

                    _lastValidScaleIndex = idx;

                }

            });

            Preferences.Default.Set("SelectedTune", selected);

#if DEBUG

            DebugLog.WriteLine($"[PickerTest] Scales/{selected}: mode={_session.ScaleSelectionMode} scale={_session.SelectedScale} Key={_session.Key}");

#endif

            UpdateRepeatButtonsVisibility();

            UpdateRandomModeWarning();

        }



        private async void OnArpeggiosPickerChanged(object? sender, EventArgs e)

        {

            if (_suppressPickerSync || !_isPageVisible) return;



            var idx = ArpeggiosPicker.SelectedIndex;

            if (idx < 0 || idx >= _arpeggioOptions.Length) return;

            var selected = _arpeggioOptions[idx];



            if (_session.Tune == "Arpeggio"

                && selected.Pattern.Id == _session.SelectedArpeggioId

                && selected.RootNote == _session.SelectedArpeggioRoot

                && string.Equals(selected.DisplayLabel, _session.SelectedArpeggioDisplay, StringComparison.Ordinal))

                return;



            ClearOtherPlayModePickers(ArpeggiosPicker);

            LayoutTestTune.SetEnabled(false);



            ApplyPlayModeSessionChange(() =>

            {

                _session.IsRandomMode = false;

                _session.SelectArpeggio(selected.Pattern, selected.RootNote, selected.DisplayLabel);

                _session.Key = GetArpeggioKeySignature(selected);

            });

            Preferences.Default.Set("SelectedTune", selected.DisplayLabel);

            UpdateRepeatButtonsVisibility();

            UpdateRandomModeWarning();

        }



        private async void OnOtherPickerChanged(object? sender, EventArgs e)

        {

            if (_suppressPickerSync) return;



            var idx = OtherPicker.SelectedIndex;

            if (idx < 0 || idx >= PlayModePickerOptions.OtherOptions.Length) return;

            var selected = PlayModePickerOptions.OtherOptions[idx];



            ClearOtherPlayModePickers(OtherPicker);

            LayoutTestTune.SetEnabled(false);



            ApplyPlayModeSessionChange(() => PlayModePickerOptions.ApplyOtherSelection(_session, selected));



            UpdatePlayModePickersFromSession(suppressClear: true);

            UpdateRepeatButtonsVisibility();

            UpdateRandomModeWarning();

        }



        private void OnAutoRepeatScaleClicked(object? sender, EventArgs e)

        {

            _session.AutoRepeat = !_session.AutoRepeat;

            _session.RepeatSameTune = false;

            UpdateRepeatButtonColors();

        }



        private void OnAutoRepeatNewClicked(object? sender, EventArgs e)

        {

            if (_session.AutoRepeat && !_session.RepeatSameTune)

            {

                _session.AutoRepeat = false;

                _session.RepeatSameTune = false;

            }

            else

            {

                _session.AutoRepeat = true;

                _session.RepeatSameTune = false;

            }

            UpdateRepeatButtonColors();

        }



        private void OnAutoRepeatSameClicked(object? sender, EventArgs e)

        {

            if (_session.AutoRepeat && _session.RepeatSameTune)

            {

                _session.AutoRepeat = false;

                _session.RepeatSameTune = false;

            }

            else

            {

                _session.AutoRepeat = true;

                _session.RepeatSameTune = true;

            }

            UpdateRepeatButtonColors();

        }



        private async void OnNavigatePracticeClicked(object? sender, EventArgs e)

        {

            await Shell.Current.GoToAsync("//MusicPage");

        }


        public new event PropertyChangedEventHandler? PropertyChanged;



        private void RaisePropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)

            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    }

}


