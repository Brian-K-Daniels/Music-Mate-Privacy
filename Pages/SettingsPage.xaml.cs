using System;
using musicmate.V3LayoutDebug;
using musicmate.Utilities;
using Microsoft.Maui.Controls;
using musicmate.Services;
using musicmate.ViewModels;

namespace musicmate.Pages
{
    public partial class SettingsPage : ContentPage
    {
        private readonly SettingsPageViewModel _viewModel;
        private readonly NoteSessionService _session;
        private readonly IOrientationService _orientation;
        private readonly ThemeService _themeService;

        private int _lastFreeLowestIndex = 0;
        private int _lastFreeHighestIndex = 0;
        private bool _premiumDialogOpen = false;

        public NoteSessionService Session => _session;
        public Color PanelBackgroundColor => _themeService.PanelBackgroundColor;
        public Color ContrastingTextColor => _themeService.ContrastingTextColor;

        public SettingsPage()
        {
            InitializeComponent();
            _viewModel = new SettingsPageViewModel();
            _session = ServiceHelper.GetService<NoteSessionService>()!;
            _orientation = ServiceHelper.GetService<IOrientationService>()!;
            _themeService = ServiceHelper.GetService<ThemeService>()!;
            BindingContext = _viewModel;

            _viewModel.PanelBackgroundColor = _themeService.PanelBackgroundColor;
            _viewModel.LowestNote = _session.LowestNote;
            _viewModel.HighestNote = _session.HighestNote;
            _viewModel.PlaybackBpm = _session.PlaybackBpm;
            _viewModel.MusicBpm = _session.MusicBpm;
            _viewModel.AccidentalPercent = _session.AccidentalPercent;
            _viewModel.CorrectThreshold = _session.CorrectThreshold;
            _viewModel.MinCorrectCount = _session.MinCorrectCount;
            _viewModel.OmitMsAvgThreshold = _session.OmitMsAvgThreshold;
            _viewModel.AutoStart = _session.AutoStart;
            _viewModel.MasteredMethod = _session.MasteredMethod;
            _viewModel.StreakCrit = _session.StreakCrit;

            var notes = _viewModel.WhiteKeyNoteNames?.ToList();
            if (notes != null)
            {
                _lastFreeLowestIndex = notes.IndexOf("C4");
                _lastFreeHighestIndex = notes.IndexOf("F5");
            }

            LowestNotePicker.SelectedIndexChanged += OnLowestNotePickerChangedWithPrompt;
            HighestNotePicker.SelectedIndexChanged += OnHighestNotePickerChangedWithPrompt;
        }

        protected override void OnAppearing()
        {
            _orientation?.ForceLandscape();
            base.OnAppearing();
            _viewModel?.RefreshStorageInfo();
            _ = CheckPremiumStatusAsync();
            if (UseFixedV3TestTuneSwitch != null)
                UseFixedV3TestTuneSwitch.IsToggled = V3LayoutTestTune.IsEnabled;
        }

        private void OnUseFixedV3TestTuneToggled(object? sender, ToggledEventArgs e)
        {
            V3LayoutTestTune.SetEnabled(e.Value);
        }

        private static async Task CheckPremiumStatusAsync()
        {
            try
            {
                var store = ServiceHelper.GetService<IStoreService>();
                if (store != null)
                    await store.CheckPremiumStatusAsync();
            }
            catch { }
        }

        private async void OnNavigateHomeClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//MainPage");
        }

        // ── Reset to defaults ─────────────────────────────────────────────────

        private async void OnResetSettingsClicked(object? sender, EventArgs e)
        {
            bool confirmed = await DisplayAlertAsync(
                "Reset Settings",
                "Reset all settings to factory defaults? This includes advanced audio, level-up criteria, and practice options.",
                "Reset", "Cancel");

            if (!confirmed) return;

            _viewModel.ResetToDefaults();

            // Re-sync note range pickers to the reset free-range indices
            var notes = _viewModel.WhiteKeyNoteNames?.ToList();
            if (notes != null)
            {
                _lastFreeLowestIndex  = notes.IndexOf(SettingsPageViewModel.DefaultLowestNote);
                _lastFreeHighestIndex = notes.IndexOf(SettingsPageViewModel.DefaultHighestNote);
            }
        }

        // ── Accidental % slider ───────────────────────────────────────────────

        private async void OnAccidentalPercentDragCompleted(object? sender, EventArgs e)
        {
            if (_premiumDialogOpen) return;
            if (StatusService.Instance.IsPremiumUser) return;
            if (_viewModel.AccidentalPercent <= 0) return;

            _premiumDialogOpen = true;

            await PremiumPromptHelper.ShowAsync(this, onDecline: () =>
            {
                _viewModel.AccidentalPercent = 0;
                if (sender is Slider slider)
                    slider.Value = 0;
            });

            _premiumDialogOpen = false;
        }

        // ── Lowest note picker ────────────────────────────────────────────────

        private async void OnLowestNotePickerChangedWithPrompt(object? sender, EventArgs e)
        {
            var picker = LowestNotePicker;
            var selectedNote = picker.SelectedItem?.ToString();
            if (selectedNote == null)
                return;

            if (!StatusService.Instance.IsPremiumUser)
            {
                int minIdx = _viewModel.WhiteKeyNoteNames.ToList().IndexOf("C4");
                int maxIdx = _viewModel.WhiteKeyNoteNames.ToList().IndexOf("F5");
                int selIdx = picker.SelectedIndex;

                if (selIdx < minIdx || selIdx > maxIdx)
                {
                    var purchased = await PremiumPromptHelper.ShowAsync(this,
                        onDecline: () => picker.SelectedIndex = _lastFreeLowestIndex);

                    if (!purchased)
                        return;

                    _lastFreeLowestIndex = selIdx;
                }
                else
                {
                    _lastFreeLowestIndex = selIdx;
                }
            }

            _viewModel.LowestNote = selectedNote;
        }

        // ── Highest note picker ───────────────────────────────────────────────

        private async void OnHighestNotePickerChangedWithPrompt(object? sender, EventArgs e)
        {
            var picker = HighestNotePicker;
            var selectedNote = picker.SelectedItem?.ToString();
            if (selectedNote == null)
                return;

            if (!StatusService.Instance.IsPremiumUser)
            {
                int minIdx = _viewModel.WhiteKeyNoteNames.ToList().IndexOf("C4");
                int maxIdx = _viewModel.WhiteKeyNoteNames.ToList().IndexOf("F5");
                int selIdx = picker.SelectedIndex;

                if (selIdx < minIdx || selIdx > maxIdx)
                {
                    var purchased = await PremiumPromptHelper.ShowAsync(this,
                        onDecline: () => picker.SelectedIndex = _lastFreeHighestIndex);

                    if (!purchased)
                        return;

                    _lastFreeHighestIndex = selIdx;
                }
                else
                {
                    _lastFreeHighestIndex = selIdx;
                }
            }

            _viewModel.HighestNote = selectedNote;
        }
    }
}
