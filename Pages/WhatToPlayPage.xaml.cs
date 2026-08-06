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
        private readonly IOrientationService _orientation = null!;
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


                _orientation = ServiceHelper.GetService<IOrientationService>()!;

                // Keep landscape locked until OnAppearing ForceLandscape. Calling
                // AllowAutorotate in the ctor unlocks portrait during Shell flyout navigation.

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

            if (Shell.Current is musicmate.AppShell shell)

                shell.ResetMusicNavigationGuard();

            _orientation?.ForceLandscape();

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

            // Tune can change from the main-menu Tuner action while this page is hidden;
            // still sync pickers so Other shows Tuner when the user returns.
            var allowWhenHidden = e.PropertyName == nameof(NoteSessionService.Tune);

            if (_localPlayModeChange || (!_isPageVisible && !allowWhenHidden)) return;



            MainThread.BeginInvokeOnMainThread(() =>

            {

                if (_localPlayModeChange || (!_isPageVisible && !allowWhenHidden)) return;



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

                        KeySignature: NoteSessionService.ResolveArpeggioWrittenKeySignature(
                            pattern, rootNote, _session.InstrumentTransposeOffset));



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

            var displayed = PlayModePickerOptions.ResolveDisplayedPicker(_session, LayoutTestTune.IsEnabled);

            if (displayed.Category == PlayModePickerCategory.Arpeggios)

            {

                var wasSuppressed = _suppressPickerSync;

                _suppressPickerSync = true;

                try

                {

                    SetArpeggioPickerSelectionFromPreference(displayed.Selection);

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

                var (category, selection) = PlayModePickerOptions.ResolveDisplayedPicker(

                    _session, LayoutTestTune.IsEnabled);



                Picker? activePicker = category switch

                {

                    PlayModePickerCategory.Tunes => TunesPicker,

                    PlayModePickerCategory.Scales => ScalesPicker,

                    PlayModePickerCategory.Arpeggios => ArpeggiosPicker,

                    _ => OtherPicker

                };



                ClearInactivePlayModePickerSelections(activePicker);



                switch (category)

                {

                    case PlayModePickerCategory.Tunes:

                        SetPickerSelection(TunesPicker, selection, _tuneTitles);

                        break;

                    case PlayModePickerCategory.Scales:

                        SetPickerSelection(ScalesPicker, selection, _scaleOptions);

                        var scaleIdx = Array.IndexOf(_scaleOptions, selection);

                        if (scaleIdx >= 0)

                            _lastValidScaleIndex = scaleIdx;

                        break;

                    case PlayModePickerCategory.Arpeggios:

                        SetArpeggioPickerSelectionFromPreference(selection);

                        break;

                    default:

                        SetPickerSelection(OtherPicker, selection, PlayModePickerOptions.OtherOptions);

                        break;

                }

            }

            finally

            {

                _suppressPickerSync = false;

            }



            if (!suppressClear)

                UpdateRandomModeWarning();

        }
        //private Picker? GetActivePlayModePicker()  //  2026.08.01 1542  method out

        //{

        //    return PlayModePickerOptions.ResolveDisplayedPicker(_session, LayoutTestTune.IsEnabled).Category switch

        //    {

        //        PlayModePickerCategory.Tunes => TunesPicker,

        //        PlayModePickerCategory.Scales => ScalesPicker,

        //        PlayModePickerCategory.Arpeggios => ArpeggiosPicker,

        //        _ => OtherPicker

        //    };

        //}
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
        private void SetArpeggioPickerSelectionFromPreference(string selection)

        {

            var idx = Array.FindIndex(_arpeggioOptions, choice =>

                string.Equals(choice.DisplayLabel, selection, StringComparison.Ordinal)

                || string.Equals(choice.PickerLabel, selection, StringComparison.Ordinal)

                || string.Equals(choice.PickerLabel.TrimStart('★', ' '), selection, StringComparison.Ordinal));



            if (idx < 0)

            {

                SetArpeggioPickerSelection();

                return;

            }



            if (ArpeggiosPicker.SelectedIndex != idx)

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

            var previousSelection = PlayModePickerOptions.ResolvePlaySelectionKey(

                _session, LayoutTestTune.IsEnabled);



            ClearOtherPlayModePickers(TunesPicker);



            if (PlayModePickerOptions.IsRhythmNoteTuneSelection(selected)

                || selected == PlayModePickerOptions.HalfThroughSixteenthNotes)

            {

                LayoutTestTune.SetEnabled(true);

                ApplyPlayModeSessionChange(() =>

                    PlayModePickerOptions.ApplyRhythmNoteTuneSelection(_session));

                UpdateRepeatButtonsVisibility();

                UpdateRandomModeWarning();

                await NavigateIfNewSelectionAsync(

                    previousSelection,

                    PlayModePickerOptions.ResolvePlaySelectionKey(

                        PlayModePickerCategory.Tunes, PlayModePickerOptions.HalfThroughSixteenthNotes));

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

            await NavigateIfNewSelectionAsync(

                previousSelection,

                PlayModePickerOptions.ResolvePlaySelectionKey(PlayModePickerCategory.Tunes, selected));

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



            var previousSelection = PlayModePickerOptions.ResolvePlaySelectionKey(

                _session, LayoutTestTune.IsEnabled);



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

                    Preferences.Default.Set("SelectedTune", selected);

                }

            });

            // Always resync after apply — an awaited premium prompt can leave the
            // picker visually stale even when session state updated correctly.
            UpdatePlayModePickersFromSession(suppressClear: true);

#if DEBUG

            DebugLog.WriteLine($"[PickerTest] Scales/{selected}: mode={_session.ScaleSelectionMode} scale={_session.SelectedScale} Key={_session.Key} rejected={rejectionReason}");

#endif

            UpdateRepeatButtonsVisibility();

            UpdateRandomModeWarning();

            if (rejectionReason == null)

                await NavigateIfNewSelectionAsync(

                    previousSelection,

                    PlayModePickerOptions.ResolvePlaySelectionKey(PlayModePickerCategory.Scales, selected));

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



            var previousSelection = PlayModePickerOptions.ResolvePlaySelectionKey(

                _session, LayoutTestTune.IsEnabled);



            ClearOtherPlayModePickers(ArpeggiosPicker);

            LayoutTestTune.SetEnabled(false);



            ApplyPlayModeSessionChange(() =>

            {

                _session.IsRandomMode = false;

                _session.SelectArpeggio(selected.Pattern, selected.RootNote, selected.DisplayLabel);

                _session.Key = selected.KeySignature;

            });

            Preferences.Default.Set("SelectedTune", selected.DisplayLabel);

            UpdateRepeatButtonsVisibility();

            UpdateRandomModeWarning();

            await NavigateIfNewSelectionAsync(

                previousSelection,

                PlayModePickerOptions.ResolvePlaySelectionKey(

                    PlayModePickerCategory.Arpeggios, selected.DisplayLabel));

        }
        private async void OnOtherPickerChanged(object? sender, EventArgs e)

        {

            if (_suppressPickerSync) return;



            var idx = OtherPicker.SelectedIndex;

            if (idx < 0 || idx >= PlayModePickerOptions.OtherOptions.Length) return;

            var selected = PlayModePickerOptions.OtherOptions[idx];

            var previousSelection = PlayModePickerOptions.ResolvePlaySelectionKey(

                _session, LayoutTestTune.IsEnabled);

            var nextSelection = PlayModePickerOptions.ResolvePlaySelectionKey(

                PlayModePickerCategory.Other, selected);



            ClearOtherPlayModePickers(OtherPicker);

            LayoutTestTune.SetEnabled(false);



            if (string.Equals(selected, PlayModePickerOptions.Tuner, StringComparison.Ordinal))

            {

                if (!PlayModePickerOptions.IsNewPlaySelection(previousSelection, nextSelection))

                {

                    ApplyPlayModeSessionChange(() =>

                        PlayModePickerOptions.ApplyOtherSelection(_session, selected));

                    UpdatePlayModePickersFromSession(suppressClear: true);

                    UpdateRepeatButtonsVisibility();

                    UpdateRandomModeWarning();

                    return;

                }



                if (Shell.Current is musicmate.AppShell shell)

                    await shell.SelectTunerAndOpenMusicAsync();

                else

                {

                    ApplyPlayModeSessionChange(() =>

                        PlayModePickerOptions.ApplyOtherSelection(_session, selected));

                    await Shell.Current.GoToAsync("//MusicPage");

                }

                return;

            }



            ApplyPlayModeSessionChange(() => PlayModePickerOptions.ApplyOtherSelection(_session, selected));



            UpdatePlayModePickersFromSession(suppressClear: true);

            UpdateRepeatButtonsVisibility();

            UpdateRandomModeWarning();

            await NavigateIfNewSelectionAsync(previousSelection, nextSelection);

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

            await NavigateToMusicAsync();

        }

        /// <summary>
        /// Opens Music only when the user picked a different What To Play item than before
        /// (including a different item in the same picker). Skips init/programmatic restores.
        /// </summary>
        private async Task NavigateIfNewSelectionAsync(string previousSelectionKey, string nextSelectionKey)

        {

            if (!_isPageVisible) return;

            if (!PlayModePickerOptions.IsNewPlaySelection(previousSelectionKey, nextSelectionKey))

                return;

            await NavigateToMusicAsync();

        }

        private async Task NavigateToMusicAsync()

        {

            if (Shell.Current is musicmate.AppShell shell)

                await shell.OpenMusicPageAsync();

            else

                await Shell.Current.GoToAsync("//MusicPage");

        }

        public new event PropertyChangedEventHandler? PropertyChanged;
        private void RaisePropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)

            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}


