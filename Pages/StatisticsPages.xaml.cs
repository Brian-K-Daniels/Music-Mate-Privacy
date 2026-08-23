using System;
using System.Diagnostics;
using musicmate.Drawables;
using musicmate.Models;
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
        private readonly SessionResultDatabase _sessionResultDatabase;
        private readonly IOrientationService _orientationService;
        private readonly NoteSessionService _session;
        private readonly ThemeService _themeService;
        private readonly StatisticsCacheService _statisticsCache;
        private readonly NoteMasteryService _noteMasteryService;
        private readonly MasteryStaffDrawable _masteryDrawable = new();

        public StatisticsPages()
        {
            InitializeComponent();

            _orientationService = ServiceHelper.GetService<IOrientationService>()!;

            _noteDatabase = ServiceHelper.GetService<NoteDatabase>()!;
            _sessionDatabase = ServiceHelper.GetService<SessionDatabase>()!;
            _sessionResultDatabase = ServiceHelper.GetService<SessionResultDatabase>()!;
            _session = ServiceHelper.GetService<NoteSessionService>()!;
            _themeService = ServiceHelper.GetService<ThemeService>()!;
            _statisticsCache = ServiceHelper.GetService<StatisticsCacheService>()!;
            _noteMasteryService = ServiceHelper.GetService<NoteMasteryService>()!;

            _viewModel = new NoteStatisticsViewModel(
                _noteDatabase, _sessionDatabase, _themeService, _session,
                _statisticsCache, _noteMasteryService);
            BindingContext = _viewModel;
            _viewModel.MasteryNotesChanged += OnMasteryNotesChanged;
            _themeService.PropertyChanged += OnThemeServicePropertyChanged;
        }

        private void OnThemeServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_viewModel.IsMasteryDatabase)
                RefreshMasteryStaff();
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
                    _statisticsCache.InvalidateSessionStats();
                    await _viewModel.LoadAsync(forceRefresh: true);
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
            await NavigationBusyService.Instance.RunAsync(async () =>
            {
                await _noteDatabase.InitializeAsync();
                await _sessionDatabase.InitializeAsync();
                await _sessionResultDatabase.InitializeAsync();
                await _viewModel.LoadAsync();
            });
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
                await _noteDatabase.InitializeAsync();
                await _sessionDatabase.InitializeAsync();
                await _sessionResultDatabase.InitializeAsync();

                if (vm.IsNoteDatabase)
                {
                    await _noteDatabase.ClearAllAsync();
                    _statisticsCache.InvalidateNoteStats();
                }
                else if (vm.IsSessionDatabase)
                {
                    await _sessionDatabase.ClearAllAsync();
                    await _sessionResultDatabase.ClearAllAsync();
                    _statisticsCache.InvalidateSessionStats();
                    LevelUpService.MarkCountSinceNow();
                }
                await _viewModel.LoadAsync(forceRefresh: true);
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
                    _statisticsCache.InvalidateNoteStats();
                }
                else if (vm.IsSessionDatabase)
                {
                    await _sessionDatabase.DeleteDatabaseAsync();
                    await _sessionDatabase.InitializeAsync(); // recreate tables
                    await _sessionResultDatabase.ClearAllAsync();
                    _statisticsCache.InvalidateSessionStats();
                    LevelUpService.MarkCountSinceNow();
                }
                await _viewModel.LoadAsync(forceRefresh: true);
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

        private async void OnNavigatePracticeClicked(object sender, EventArgs e)
        {
            await NavigationBusyService.GoToAsync("//MusicPage");
        }

        private void OnMasteryNotesChanged(object? sender, EventArgs e)
            => RefreshMasteryStaff();

        private void RefreshMasteryStaff()
        {
            if (MasteryStaffView is null)
                return;

            Color panelBg = _themeService.PanelBackgroundColor;
            Color mastered = NoteMasteryStatusColors.ForState(NoteMasteryState.Mastered, panelBg);
            Color improving = NoteMasteryStatusColors.ForState(NoteMasteryState.Improving, panelBg);
            Color needsPractice = NoteMasteryStatusColors.ForState(NoteMasteryState.NeedsPractice, panelBg);
            Color notTried = NoteMasteryStatusColors.ForState(NoteMasteryState.NotYetAttempted, panelBg);

            ApplyMasteryStatusLabelColors(
                mastered, improving, needsPractice, notTried);

            _masteryDrawable.Notes = _viewModel.MasteryNotes.ToList();
            _masteryDrawable.PreferBassClef = _viewModel.PreferBassClef;
            _masteryDrawable.StaffInk = _themeService.ContrastingTextColor;
            _masteryDrawable.MasteredColor = mastered;
            _masteryDrawable.ImprovingColor = improving;
            _masteryDrawable.NeedsPracticeColor = needsPractice;
            _masteryDrawable.NotAttemptedColor = notTried;
            _masteryDrawable.SelectedHaloColor = _themeService.PickerBorderColor;
            float width = (float)(MasteryStaffView.Width > 0 ? MasteryStaffView.Width : 640);
            MasteryStaffView.Drawable = _masteryDrawable;
            MasteryStaffView.HeightRequest = Math.Max(120, _masteryDrawable.ComputeDesiredHeight(width) + 8);
            MasteryStaffView.Invalidate();
        }

        private void ApplyMasteryStatusLabelColors(
            Color mastered, Color improving, Color needsPractice, Color notTried)
        {
            ApplyStatusChrome(
                MasteryLegendMastered, MasteryCountMastered,
                mastered, NoteMasteryState.Mastered);
            ApplyStatusChrome(
                MasteryLegendImproving, MasteryCountImproving,
                improving, NoteMasteryState.Improving);
            ApplyStatusChrome(
                MasteryLegendPracticeNext, MasteryCountPracticeNext,
                needsPractice, NoteMasteryState.NeedsPractice);
            ApplyStatusChrome(
                MasteryLegendNotTried, MasteryCountNotTried,
                notTried, NoteMasteryState.NotYetAttempted);

            Color detailBg = _themeService.SecondPanelBackgroundColor;
            Color detailStatus = _viewModel.SelectedMasteryNote is null
                ? notTried
                : NoteMasteryStatusColors.ForState(
                    _viewModel.SelectedMasteryNote.MasteryState, detailBg);
            if (MasteryDetailNoteName is not null)
                MasteryDetailNoteName.TextColor = detailStatus;
            if (MasteryDetailState is not null)
            {
                MasteryDetailState.TextColor = detailStatus;
                string tip = _viewModel.SelectedMasteryStateDescription;
                ToolTipProperties.SetText(MasteryDetailState, tip);
                SemanticProperties.SetHint(MasteryDetailState, tip);
            }
        }

        private static void ApplyStatusChrome(
            Label? legend,
            Label? count,
            Color color,
            NoteMasteryState state)
        {
            string tip = NoteMasteryStateLabels.Description(state);
            if (legend is not null)
            {
                legend.TextColor = color;
                ToolTipProperties.SetText(legend, tip);
                SemanticProperties.SetHint(legend, tip);
            }

            if (count is not null)
            {
                count.TextColor = color;
                ToolTipProperties.SetText(count, tip);
                SemanticProperties.SetHint(count, tip);
            }
        }

        private void OnMasteryStaffStartInteraction(object? sender, TouchEventArgs e)
        {
            if (e.Touches.Length == 0)
                return;
            var touch = e.Touches[0];
            var hit = _masteryDrawable.HitTest((float)touch.X, (float)touch.Y);
            if (hit is not null)
            {
                _viewModel.SelectMasteryNoteCommand.Execute(hit);
                RefreshMasteryStaff();
            }
        }

    }
}
