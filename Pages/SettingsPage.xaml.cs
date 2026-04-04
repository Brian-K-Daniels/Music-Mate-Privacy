using System;
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
        private int _lastFreeScaleIndex = 0;
        private static readonly HashSet<string> FreeScales = new() { "Major", "Harmonic Minor" };

        private int _lastFreeLowestIndex = 0;
        private int _lastFreeHighestIndex = 0;

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

            // Initialize view-model from live services
            _viewModel.PanelBackgroundColor = _themeService.PanelBackgroundColor;
            _viewModel.SelectedScale = _session.SelectedScale;
            _viewModel.LowestNote = _session.LowestNote;
            _viewModel.HighestNote = _session.HighestNote;
            _viewModel.PlaybackBpm = _session.PlaybackBpm;
            _viewModel.AccidentalPercent = _session.AccidentalPercent;
            _viewModel.CorrectThreshold = _session.CorrectThreshold;
            _viewModel.MinCorrectCount = _session.MinCorrectCount;
            _viewModel.OmitMsAvgThreshold = _session.OmitMsAvgThreshold;
            _viewModel.AutoStart = _session.AutoStart;

            // Find initial free scale index
            var scales = _viewModel.AvailableScalesForBinding?.ToList();
            if (scales != null)
            {
                var idx = scales.IndexOf(_viewModel.SelectedScale);
                _lastFreeScaleIndex = FreeScales.Contains(_viewModel.SelectedScale) ? idx : 0;
            }

            // Find initial free note indices
            var notes = _viewModel.WhiteKeyNoteNames?.ToList();
            if (notes != null)
            {
                _lastFreeLowestIndex = notes.IndexOf("C4");
                _lastFreeHighestIndex = notes.IndexOf("F5");
            }

            ScalePicker.SelectedIndexChanged += OnScalePickerChangedWithPrompt;
            LowestNotePicker.SelectedIndexChanged += OnLowestNotePickerChangedWithPrompt;
            HighestNotePicker.SelectedIndexChanged += OnHighestNotePickerChangedWithPrompt;
        }

        protected override void OnAppearing()
        {
            _orientation?.ForceLandscape();
            base.OnAppearing();
            // Set 9mm left margin on the main layout inside the ScrollView
            //var mainLayout = this.FindByName<VerticalStackLayout>("SettingsMainLayout");
            //if (mainLayout != null)
            //    musicmate.Utilities.MarginUtils.SetLeftMarginMM(mainLayout, 9, 0, 0, 0);  //  2026.04.02 1725  block out
        }

        private async void OnNavigateHomeClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//MainPage");
        }

        private async void OnScalePickerChangedWithPrompt(object? sender, EventArgs e)
        {
            var picker = ScalePicker;
            var selectedScale = picker.SelectedItem?.ToString();
            if (selectedScale == null)
                return;

            if (!FreeScales.Contains(selectedScale) && !StatusService.Instance.IsPremiumUser)
            {
                bool upgrade = await DisplayAlertAsync("Premium Feature", $"The scale '{selectedScale}' is a premium feature. Upgrade to access.", "Upgrade", "Cancel");
                if (!upgrade)
                {
                    picker.SelectedIndex = _lastFreeScaleIndex;
                    return;
                }
                // Optionally, trigger upgrade flow here
            }
            else
            {
                _lastFreeScaleIndex = picker.SelectedIndex;
            }
            // Let view-model push this change into NoteSessionService
            _viewModel.SelectedScale = selectedScale;
        }
        private async void OnLowestNotePickerChangedWithPrompt(object? sender, EventArgs e)
        {
            var picker = LowestNotePicker;
            var selectedNote = picker.SelectedItem?.ToString();
            if (selectedNote == null)
                return;

            // Only restrict for free users
            if (!StatusService.Instance.IsPremiumUser)
            {
                // Allowed range: C4 (inclusive) and above
                int minIdx = _viewModel.WhiteKeyNoteNames.ToList().IndexOf("C4");
                int maxIdx = _viewModel.WhiteKeyNoteNames.ToList().IndexOf("F5");
                int selIdx = picker.SelectedIndex;
                if (selIdx < minIdx || selIdx > maxIdx)
                {
                    bool upgrade = await DisplayAlertAsync("Premium Feature", $"Lowest note '{selectedNote}' is a premium feature. Upgrade to access.", "Upgrade", "Cancel");
                    picker.SelectedIndex = _lastFreeLowestIndex;
                    return;
                }
                _lastFreeLowestIndex = selIdx;
            }
            // Let view-model push this change into NoteSessionService
            _viewModel.LowestNote = selectedNote;
        }

        private async void OnHighestNotePickerChangedWithPrompt(object? sender, EventArgs e)
        {
            var picker = HighestNotePicker;
            var selectedNote = picker.SelectedItem?.ToString();
            if (selectedNote == null)
                return;

            // Only restrict for free users
            if (!StatusService.Instance.IsPremiumUser)
            {
                // Allowed range: F5 (inclusive) and below
                int minIdx = _viewModel.WhiteKeyNoteNames.ToList().IndexOf("C4");
                int maxIdx = _viewModel.WhiteKeyNoteNames.ToList().IndexOf("F5");
                int selIdx = picker.SelectedIndex;
                if (selIdx < minIdx || selIdx > maxIdx)
                {
                    bool upgrade = await DisplayAlertAsync("Premium Feature", $"Highest note '{selectedNote}' is a premium feature. Upgrade to access.", "Upgrade", "Cancel");
                    picker.SelectedIndex = _lastFreeHighestIndex;
                    return;
                }
                _lastFreeHighestIndex = selIdx;
            }
            // Let view-model push this change into NoteSessionService
            _viewModel.HighestNote = selectedNote;
        }

    }
}
