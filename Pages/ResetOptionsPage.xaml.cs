using musicmate.Services;
using Microsoft.Maui.Graphics;

namespace musicmate.Pages
{
    public partial class ResetOptionsPage : ContentPage
    {
        private readonly SettingsResetService _resetService;
        private readonly ThemeService _themeService;
        private readonly IOrientationService _orientation;

        public Color PanelBackgroundColor => _themeService.PanelBackgroundColor;
        public Color ContrastingTextColor => _themeService.ContrastingTextColor;

        public ResetOptionsPage()
        {
            InitializeComponent();
            _resetService = ServiceHelper.GetService<SettingsResetService>()!;
            _themeService = ServiceHelper.GetService<ThemeService>()!;
            _orientation = ServiceHelper.GetService<IOrientationService>()!;
            BindingContext = this;
            _themeService.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ThemeService.PanelBackgroundColor))
                {
                    OnPropertyChanged(nameof(PanelBackgroundColor));
                    OnPropertyChanged(nameof(ContrastingTextColor));
                }
            };
        }

        protected override void OnAppearing()
        {
            _orientation?.ForceLandscape();
            base.OnAppearing();
            UpdateCustomDefaultsButtonState();
        }

        private void UpdateCustomDefaultsButtonState()
        {
            RestoreCustomDefaultsButton.IsEnabled = _resetService.HasCustomDefaults;
        }

        private async void OnNavigatePracticeClicked(object? sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//MusicPage");
        }

        private async void OnFactoryResetClicked(object? sender, EventArgs e)
        {
            bool confirmed = await DisplayAlertAsync(
                "Reset Settings",
                "Reset all settings to factory defaults? This includes advanced audio, level-up criteria, and practice options.",
                "Reset", "Cancel");

            if (!confirmed)
                return;

            _resetService.ResetToFactoryDefaults();
            await DisplayAlertAsync("Reset Complete", "Settings have been restored to factory defaults.", "OK");
        }

        private async void OnSaveCustomDefaultsClicked(object? sender, EventArgs e)
        {
            bool confirmed = await DisplayAlertAsync(
                "Save Custom Defaults",
                "Save the current settings as your custom defaults?",
                "Save", "Cancel");

            if (!confirmed)
                return;

            _resetService.SaveCustomDefaultsFromCurrent();
            UpdateCustomDefaultsButtonState();
            await DisplayAlertAsync("Saved", "Current settings have been saved as your custom defaults.", "OK");
        }

        private async void OnRestoreCustomDefaultsClicked(object? sender, EventArgs e)
        {
            if (!_resetService.HasCustomDefaults)
            {
                await DisplayAlertAsync(
                    "No Custom Defaults",
                    "No custom defaults have been saved yet. Use \"Set Current Settings to Custom Defaults\" first.",
                    "OK");
                return;
            }

            bool confirmed = await DisplayAlertAsync(
                "Restore Custom Defaults",
                "Restore all settings from your saved custom defaults?",
                "Restore", "Cancel");

            if (!confirmed)
                return;

            _resetService.RestoreCustomDefaults();
            await DisplayAlertAsync("Restored", "Settings have been restored from your custom defaults.", "OK");
        }
    }
}
