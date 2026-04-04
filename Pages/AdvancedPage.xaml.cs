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

        private void OnResetClicked(object sender, EventArgs e)
        {
            _viewModel.ResetToDefaults();
        }
    }
}
