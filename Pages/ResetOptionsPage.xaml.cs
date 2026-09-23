using musicmate.Services;
using Microsoft.Maui.Graphics;

namespace musicmate.Pages
{
    public partial class ResetOptionsPage : ContentPage
    {
        private static readonly Color ActiveBackground = Colors.Green;
        private static readonly Color ActiveText = Colors.White;
        private static readonly Color InactiveBackground = Colors.White;
        private static readonly Color InactiveText = Colors.Black;

        private readonly SettingsResetService _resetService;
        private readonly ThemeService _themeService;
        private readonly IOrientationService _orientation;
        private bool _defaultsUiReady;

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
            _resetService.PropertyChanged += OnResetServicePropertyChanged;
        }

        private void OnResetServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(SettingsResetService.AreFactoryDefaultsApplied)
                or nameof(SettingsResetService.AreCustomDefaultsApplied)
                or nameof(SettingsResetService.IsCurrentSavedAsCustomDefaults)
                or nameof(SettingsResetService.HasCustomDefaults))
            {
                UpdateCustomDefaultsButtonState();
                UpdateActiveDefaultsButtonHighlight();
            }
        }

        protected override void OnAppearing()
        {
            _orientation?.ForceLandscape();
            base.OnAppearing();
            _resetService.EvaluateDefaultsButtonHighlightState();
            _defaultsUiReady = true;
            UpdateCustomDefaultsButtonState();
            UpdateActiveDefaultsButtonHighlight();
        }

        private void UpdateCustomDefaultsButtonState()
        {
            RestoreCustomDefaultsButton.IsEnabled = _resetService.HasCustomDefaults;
        }

        private void UpdateActiveDefaultsButtonHighlight()
        {
            if (!_defaultsUiReady)
                return;

            var state = _resetService.EvaluateDefaultsButtonHighlightState();

            ApplyDefaultsButtonHighlight(FactoryResetButton, state.FactoryActive);
            ApplyDefaultsButtonHighlight(RestoreCustomDefaultsButton, state.CustomActive);
            ApplyDefaultsButtonHighlight(SaveCustomDefaultsButton, state.SaveCustomActive);
        }

        private static void ApplyDefaultsButtonHighlight(Button button, bool active)
        {
            ApplyDefaultsButtonHighlight(
                button,
                active ? ActiveBackground : InactiveBackground,
                active ? ActiveText : InactiveText);
        }

        private static void ApplyDefaultsButtonHighlight(Button button, Color background, Color text)
        {
            button.BackgroundColor = background;
            button.TextColor = text;
        }

        private async void OnNavigatePracticeClicked(object? sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//MusicPage");
        }

        private async void OnFactoryResetClicked(object? sender, EventArgs e)
        {
            bool continueReset = await DisplayAlertAsync(
                "Factory Reset",
                "Factory Reset will permanently erase all your progress, practice history, statistics, and settings. Premium purchases will not be affected. Continue?",
                "Continue",
                "Cancel");
            if (!continueReset)
                return;

            bool eraseEverything = await DisplayAlertAsync(
                "Confirm Factory Reset",
                "This cannot be undone.",
                "Yes, erase everything.",
                "Cancel");
            if (!eraseEverything)
                return;

            try
            {
                await _resetService.PerformFullFactoryResetAsync(
                    ServiceHelper.GetService<NoteDatabase>(),
                    ServiceHelper.GetService<SessionDatabase>(),
                    ServiceHelper.GetService<SessionResultDatabase>(),
                    ServiceHelper.GetService<NoteAttemptDatabase>(),
                    ServiceHelper.GetService<StatisticsCacheService>());

                UpdateCustomDefaultsButtonState();
                UpdateActiveDefaultsButtonHighlight();
                await DisplayAlertAsync(
                    "Reset Complete",
                    "Progress, practice history, statistics, and settings have been erased. Premium purchases were not affected.",
                    "OK");
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", $"Factory reset failed: {ex.Message}", "OK");
            }
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
            UpdateActiveDefaultsButtonHighlight();
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
            UpdateCustomDefaultsButtonState();
            UpdateActiveDefaultsButtonHighlight();
            await DisplayAlertAsync("Restored", "Settings have been restored from your custom defaults.", "OK");
        }
    }
}
