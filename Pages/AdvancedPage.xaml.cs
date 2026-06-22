using System;
using System.Diagnostics;
using musicmate.Services;
using musicmate.ViewModels;
using Microsoft.Maui.Storage;
using System.ComponentModel;
using System.Runtime.CompilerServices;

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
            _viewModel.RefreshCurrentSessionMetrics();
            _viewModel.LoadCompositionPercentsFromSession();
            var mainLayout = this.FindByName<VerticalStackLayout>("AdvancedMainLayout");
        }

        private void OnPcTunesDragCompleted(object? sender, EventArgs e)
        {
            if (sender is Slider slider)
                _viewModel.CommitCompositionPercent(0, (int)Math.Round(slider.Value));
        }

        private void OnPcRandomDragCompleted(object? sender, EventArgs e)
        {
            if (sender is Slider slider)
                _viewModel.CommitCompositionPercent(1, (int)Math.Round(slider.Value));
        }

        private void OnPcScalesDragCompleted(object? sender, EventArgs e)
        {
            if (sender is Slider slider)
                _viewModel.CommitCompositionPercent(2, (int)Math.Round(slider.Value));
        }

        private void OnPcArpeggiosDragCompleted(object? sender, EventArgs e)
        {
            if (sender is Slider slider)
                _viewModel.CommitCompositionPercent(3, (int)Math.Round(slider.Value));
        }

        private async void OnNavigatePracticeClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//MusicPage");
        }
    }
}
