using System;
using System.Diagnostics;
using musicmate.Services;
using musicmate.ViewModels;

namespace musicmate.Pages
{
    public partial class AdvancedPage : ContentPage
    {
        private readonly IOrientationService _orientationService;
        private readonly NoteSessionService? _session;
        private readonly ThemeService _themeService;
        private AdvancedPageViewModel _viewModel;
        public NoteSessionService Session => _session!;

        public AdvancedPage()
        {
            InitializeComponent();
            _orientationService = ServiceHelper.GetService<IOrientationService>()!;
            _session = ServiceHelper.GetService<NoteSessionService>();
            _themeService = ServiceHelper.GetService<ThemeService>()!;
            _viewModel = new AdvancedPageViewModel(_themeService, _session!);
            BindingContext = _viewModel;
           
        }

        protected async override void OnAppearing()
        {
            _orientationService?.ForceLandscape();
            base.OnAppearing();
            // Set 9mm left margin on the outermost ScrollView
            var mainLayout = this.FindByName<VerticalStackLayout>("AdvancedMainLayout");
            //if (mainLayout != null)
            //    musicmate.Utilities.MarginUtils.SetLeftMarginMM(mainLayout, 9, 0, 0, 0);  //  2026.04.02 1724  block out

#if ANDROID
            var window = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window;
            if (window != null)
            {
                window.AddFlags(Android.Views.WindowManagerFlags.Fullscreen);
                window.ClearFlags(Android.Views.WindowManagerFlags.ForceNotFullscreen);
            }
#endif
        }

        private async void OnNavigateHomeClicked(object sender, EventArgs e)
        {
            // Navigate to the root (home) page
            await Shell.Current.GoToAsync("//MainPage");
        }

        /// <summary>
        /// Reset to defaults and optionally clear persisted note/session data.
        /// Asks the user to confirm resetting settings; then asks whether to
        /// delete Note data, Session data, both, or neither. Clearing uses the
        /// services' ClearAllAsync methods so tables remain present and usable.
        /// </summary>
        private async void OnResetClicked(object sender, EventArgs e)
        {
            try
            {
                // Confirm resetting settings to defaults
                var doReset = await DisplayAlertAsync(
                    "Reset all to defaults",
                    "This will reset all advanced settings to their default values. Continue?",
                    "Yes",
                    "No");
                if (!doReset)
                    return;

                // Perform settings reset
                _viewModel.ResetToDefaults();

                // Ask whether to delete stored data
                // Use DisplayActionSheetAsync with a cancel "Don't delete" and a destructive "Delete both"
                var choice = await DisplayActionSheetAsync(
                    "Also delete stored data?",
                    "Don't delete",          // cancel
                    "Delete both",           // destructive
                    "Delete note data only",
                    "Delete session data only");

                // If user chose cancel, do nothing more
                if (choice == "Don't delete" || string.IsNullOrEmpty(choice))
                {
                    await DisplayAlertAsync("Reset complete", "Settings reset to defaults.", "OK");
                    return;
                }

                // Execute clears as requested
                if (choice == "Delete both" || choice == "Delete note data only")
                {
                    var noteDb = ServiceHelper.GetService<NoteDatabase>();
                    if (noteDb != null)
                    {
                        await noteDb.InitializeAsync(); // ensure table exists
                        await noteDb.ClearAllAsync();
                    }
                }

                if (choice == "Delete both" || choice == "Delete session data only")
                {
                    var sessionDb = ServiceHelper.GetService<SessionDatabase>();
                    if (sessionDb != null)
                    {
                        await sessionDb.InitializeAsync(); // ensure table exists / migrations applied
                        await sessionDb.ClearAllAsync();
                    }
                }

                await DisplayAlertAsync("Reset complete", "Settings reset and selected data cleared.", "OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AdvancedPage] OnResetClicked ERROR: {ex}");
                await DisplayAlertAsync("Error", $"Failed to reset/clear data: {ex.Message}", "OK");
            }
        }
    }
}
