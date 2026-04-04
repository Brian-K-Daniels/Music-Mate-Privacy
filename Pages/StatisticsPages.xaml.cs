using System;
using System.Diagnostics;
using musicmate.Utilities;
using Microsoft.Maui.Controls;
using musicmate.Services;
using musicmate.ViewModels;

namespace musicmate.Pages
{
    public partial class StatisticsPages : ContentPage
    {
        private readonly NoteStatisticsViewModel _viewModel;
        private readonly NoteDatabase _noteDatabase;
        private readonly SessionDatabase _sessionDatabase;
        private readonly IOrientationService _orientationService;
        private readonly NoteSessionService _session;
        private readonly ThemeService _themeService;

        public StatisticsPages()
        {
            InitializeComponent();

#if DEBUG
            DeleteAllButton.IsVisible = true;
#else
            DeleteAllButton.IsVisible = false;
#endif

            _orientationService = ServiceHelper.GetService<IOrientationService>()!;

            _noteDatabase = ServiceHelper.GetService<NoteDatabase>()!;
            _sessionDatabase = ServiceHelper.GetService<SessionDatabase>()!;
            _session = ServiceHelper.GetService<NoteSessionService>()!;
            _themeService = ServiceHelper.GetService<ThemeService>()!;

            _viewModel = new NoteStatisticsViewModel(_noteDatabase, _sessionDatabase, _themeService, _session);
            BindingContext = _viewModel;
        }

        private async void OnDeleteSessionStatClicked(object sender, EventArgs e)
        {
            if (sender is Button btn && btn.CommandParameter is int id)
            {
                var stat = (await _sessionDatabase.GetAllAsync()).FirstOrDefault(s => s.Id == id);
                if (stat == null) return;

                bool confirm = await DisplayAlertAsync("Confirm", $"Delete session on {stat.Dt:yyyy-MM-dd HH:mm}?", "Yes", "No");
                if (!confirm) return;

                try
                {
                    await _sessionDatabase.DeleteByIdAsync(id);
                    await _viewModel.LoadAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Delete session error: {ex}");
                    await DisplayAlertAsync("Error", $"Failed to delete session: {ex.Message}", "OK");
                }
            }
        }

        protected override async void OnAppearing()
        {
            _orientationService?.ForceLandscape();
            base.OnAppearing();
            // Set 9mm left margin on the outermost VerticalStackLayout
            //var mainLayout = this.FindByName<VerticalStackLayout>("StatisticsMainLayout");
            //if (mainLayout != null)
            //    musicmate.Utilities.MarginUtils.SetLeftMarginMM(mainLayout, 9, 0, 0, 0);  //  2026.04.02 1726  block out
            await _noteDatabase.InitializeAsync();
            await _sessionDatabase.InitializeAsync();
            await _viewModel.LoadAsync();
        }

        private async void OnDeleteDataInSelectedDatabase(object sender, EventArgs e)
        {
            var vm = BindingContext as NoteStatisticsViewModel;
            if (vm == null) return;

            // If a session is in progress, stop it gracefully and continue
            if (_session is not null && !_session.SessionCompleted)
            {
                try
                {
                    // Stop audio capture if running
                    var audio = ServiceHelper.GetService<IAudioCaptureService>();
                    audio?.StopCapture();

                    // Cancel any playback in progress
                    var player = ServiceHelper.GetService<IAudioPlaybackService>();
                    player?.CancelPlayback();

                    // Use NoteSessionService API to stop the session gracefully
                    _session.StopSession();

                    // Update status for visibility
                    StatusService.Instance.StatusMessage = "Stopped session and playback to allow database operation.";
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to stop session: {ex}");
                    // Fallback: still show message if stop failed
                    await DisplayAlertAsync("Action Blocked", "Could not stop active session. Please stop it manually and try again.", "OK");
                    return;
                }
            }

            bool confirm = await DisplayAlertAsync(
                "Confirm",
                $"Clear all data in {vm.SelectedDatabase} database?",
                "Yes",
                "No");
            if (!confirm) return;

            try
            {
                if (vm.IsNoteDatabase)
                {
                    await _noteDatabase.ClearAllAsync();
                }
                else if (vm.IsSessionDatabase)
                {
                    await _sessionDatabase.ClearAllAsync();
                }
                await _viewModel.LoadAsync();
                await DisplayAlertAsync("Success", "All data cleared.", "OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Clear all error: {ex}");
                await DisplayAlertAsync("Error", $"Failed to clear data: {ex.Message}", "OK");
            }
        }

        private async void OnDeleteAllDataInSelectedDatabase(object sender, EventArgs e)
        {
#if DEBUG
            var vm = BindingContext as NoteStatisticsViewModel;
            if (vm == null) return;

            // If a session is in progress, stop it gracefully and continue
            if (_session is not null && !_session.SessionCompleted)
            {
                try
                {
                    var audio = ServiceHelper.GetService<IAudioCaptureService>();
                    audio?.StopCapture();
                    var player = ServiceHelper.GetService<IAudioPlaybackService>();
                    player?.CancelPlayback();
                    _session.StopSession();
                    StatusService.Instance.StatusMessage = "Stopped session and playback to allow database deletion.";
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to stop session: {ex}");
                    await DisplayAlertAsync("Action Blocked", "Could not stop active session. Please stop it manually and try again.", "OK");
                    return;
                }
            }

            bool confirm = await DisplayAlertAsync(
                "Confirm",
                $"Delete ALL data in {vm.SelectedDatabase} database? This cannot be undone.",
                "Yes",
                "No");
            if (!confirm) return;

            try
            {
                if (vm.IsNoteDatabase)
                {
                    await _noteDatabase.DeleteDatabaseAsync();
                    await _noteDatabase.InitializeAsync(); // recreate tables
                }
                else if (vm.IsSessionDatabase)
                {
                    await _sessionDatabase.DeleteDatabaseAsync();
                    await _sessionDatabase.InitializeAsync(); // recreate tables
                }
                await _viewModel.LoadAsync();
                foreach (var stat in _viewModel.NoteStats)
                    stat.ContrastingTextColor = _themeService!.ContrastingTextColor;
                foreach (var stat in _viewModel.SessionStats)
                    stat.ContrastingTextColor = _themeService!.ContrastingTextColor;
                await DisplayAlertAsync("Success", "Database deleted. The app will recreate it on next use.", "OK");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Delete database error: {ex}");
                await DisplayAlertAsync("Error", $"Failed to delete database: {ex.Message}", "OK");
            }
#else
            await DisplayAlertAsync("Not Available", "Database deletion is only available in DEBUG builds.", "OK");
#endif
        }

        private async void OnNavigateHomeClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//MainPage");
        }
    }
}
