using System;
using musicmate.Diagnostics;
using musicmate.Utilities;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using musicmate.Services;
using musicmate.ViewModels;

namespace musicmate.Pages
{
    public partial class SettingsPage : ContentPage
    {
        private int                             _lastFreeHighestIndex = 0;
        private int                             _lastFreeLowestIndex = 0;
        private readonly IOrientationService    _orientation;
        private bool                            _premiumDialogOpen = false;
        private readonly NoteSessionService     _session;
        private readonly ThemeService           _themeService;
        private readonly SettingsPageViewModel  _viewModel;
        public Color                            ContrastingTextColor => _themeService.ContrastingTextColor;
        public Color                            PanelBackgroundColor => _themeService.PanelBackgroundColor;
        public NoteSessionService               Session => _session;

        private static int                      FreeRangeLowMidi => NoteSessionService.NoteNameToMidi("C4");
        private static int                      FreeRangeHighMidi => NoteSessionService.NoteNameToMidi("F5");

        private static bool                     IsOutsideFreeNoteRange(string? noteName)
        {
            if (string.IsNullOrWhiteSpace(noteName))
                return false;
            int midi = NoteSessionService.NoteNameToMidi(noteName);
            return midi < FreeRangeLowMidi || midi > FreeRangeHighMidi;
        }

        /// <summary>
        /// Play Billing query only — must never drive the navigation spinner.
        /// Shell already shows busy during navigation; nesting RunAsync(billing) kept
        /// the overlay up for the entire billing round-trip on Release.
        /// </summary>
        private static async Task               CheckPremiumStatusAsync()
        {
            try
            {
                await SettingsLoadTiming.TimeAsync("CheckPremiumStatusAsync", async () =>
                {
                    var store = ServiceHelper.GetService<IStoreService>();
                    if (store != null)
                        await store.CheckPremiumStatusAsync().ConfigureAwait(true);
                });
            }
            catch (Exception ex)
            {
                SettingsLoadTiming.Mark("CheckPremiumStatusAsync:error", ex.Message);
            }
        }

        private async void                      OnAccidentalPercentDragCompleted(object? sender, EventArgs e)
        {
            if (_premiumDialogOpen) return;

            await CheckPremiumStatusAsync();
            if (StatusService.Instance.IsPremiumUser) return;
            if (_viewModel.AccidentalPercent <= 0) return;

            _premiumDialogOpen = true;

            var ownedOrPurchased = await PremiumPromptHelper.ShowAsync(this, onDecline: () =>
            {
                _viewModel.AccidentalPercent = 0;
                if (sender is Slider slider)
                    slider.Value = 0;
            });

            // If Play reported already-owned during the prompt, keep the accidental %.
            if (!ownedOrPurchased && !StatusService.Instance.IsPremiumUser)
            {
                _viewModel.AccidentalPercent = 0;
                if (sender is Slider slider)
                    slider.Value = 0;
            }

            _premiumDialogOpen = false;
        }

        protected override void                 OnAppearing()
        {
            SettingsLoadTiming.Mark("OnAppearing:START");
            try
            {
                _orientation?.ForceLandscape();
                base.OnAppearing();

                // Do not wrap billing in NavigationBusyService.RunAsync — that nested under
                // Shell navigation busy and held the spinner until Play Billing finished.
                _ = CheckPremiumStatusAsync();
            }
            finally
            {
                SettingsLoadTiming.Mark("OnAppearing:END");
            }
        }

        private async void                      OnHighestNotePickerChangedWithPrompt(object? sender, EventArgs e)
        {
            var picker = HighestNotePicker;
            var selectedNote = picker.SelectedItem?.ToString();
            if (selectedNote == null)
                return;

            if (!StatusService.Instance.IsPremiumUser)
            {
                int selIdx = picker.SelectedIndex;

                if (IsOutsideFreeNoteRange(selectedNote))
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

        private async void                      OnLowestNotePickerChangedWithPrompt(object? sender, EventArgs e)
        {
            var picker = LowestNotePicker;
            var selectedNote = picker.SelectedItem?.ToString();
            if (selectedNote == null)
                return;

            if (!StatusService.Instance.IsPremiumUser)
            {
                int selIdx = picker.SelectedIndex;

                if (IsOutsideFreeNoteRange(selectedNote))
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

        private void                            OnColorsSelectionClicked(object? sender, EventArgs e)
        {
            ColorPickerDialog.Show(_themeService.PanelBackgroundColor);
        }

        private async void                      OnNavigatePracticeClicked(object sender, EventArgs e)
        {
            await NavigationBusyService.GoToAsync("//MusicPage");
        }

        public SettingsPage()
        {
            SettingsLoadTiming.BeginOpen();
            SettingsLoadTiming.Mark("Ctor:START");

            var resetService = ServiceHelper.GetService<SettingsResetService>();
            resetService?.SuspendEvaluation();
            try
            {
                SettingsLoadTiming.Mark("InitializeComponent:START");
                InitializeComponent();
                SettingsLoadTiming.Mark("InitializeComponent:END");

                SettingsLoadTiming.Mark("new SettingsPageViewModel:START");
                _viewModel = new SettingsPageViewModel();
                SettingsLoadTiming.Mark("new SettingsPageViewModel:END");

                SettingsLoadTiming.Mark("ResolveServices:START");
                _session = ServiceHelper.GetService<NoteSessionService>()!;
                _orientation = ServiceHelper.GetService<IOrientationService>()!;
                _themeService = ServiceHelper.GetService<ThemeService>()!;
                SettingsLoadTiming.Mark("ResolveServices:END");

                BindingContext = _viewModel;

                SettingsLoadTiming.Mark("SyncViewModelFromSession:START");
                SyncViewModelFromSession();
                SettingsLoadTiming.Mark("SyncViewModelFromSession:END");

                ColorPickerDialog.AppColorPicked += (_, e) =>
                {
                    _themeService.SetColor(e.Target, e.Color);
                };

                SettingsLoadTiming.Mark("InitFreeNoteRangeIndices:START");
                InitFreeNoteRangeIndices();
                SettingsLoadTiming.Mark("InitFreeNoteRangeIndices:END");

                LowestNotePicker.SelectedIndexChanged += OnLowestNotePickerChangedWithPrompt;
                HighestNotePicker.SelectedIndexChanged += OnHighestNotePickerChangedWithPrompt;
            }
            finally
            {
                // One evaluation after bulk sync; always resume even if ctor throws mid-way.
                try
                {
                    SettingsLoadTiming.Mark("ResumeEvaluation:START");
                    resetService?.ResumeEvaluation(evaluateNow: true);
                    SettingsLoadTiming.Mark("ResumeEvaluation:END");
                }
                catch (Exception ex)
                {
                    SettingsLoadTiming.Mark("ResumeEvaluation:error", ex.Message);
                    try { resetService?.ResumeEvaluation(evaluateNow: false); } catch { }
                }

                SettingsLoadTiming.Mark("Ctor:END");
            }
        }

        private void SyncViewModelFromSession()
        {
            _viewModel.RefreshThemeColorBindings();
            _viewModel.LowestNote = _session.LowestNote;
            _viewModel.HighestNote = _session.HighestNote;
            _viewModel.Tempo = _session.Tempo;
            _viewModel.AccidentalPercent = _session.AccidentalPercent;
            _viewModel.CorrectThreshold = _session.CorrectThreshold;
            _viewModel.MinCorrectCount = _session.MinCorrectCount;
            _viewModel.OmitMsAvgThreshold = _session.OmitMsAvgThreshold;
            _viewModel.AutoStart = _session.AutoStart;
            _viewModel.ShowConductorCues = _session.ShowConductorCues;
            _viewModel.ShowSignaturesOnBothStaffs = _session.ShowSignaturesOnBothStaffs;
            _viewModel.MasteredMethod = _session.MasteredMethod;
            _viewModel.StreakCrit = _session.StreakCrit;
            _viewModel.UseNoteMasteryForGeneration = _session.UseNoteMasteryForGeneration;
        }

        private void InitFreeNoteRangeIndices()
        {
            var notes = _viewModel.NoteRangePickerNoteNames?.ToList();
            if (notes != null && notes.Count > 0)
            {
                int c4 = notes.IndexOf("C4");
                int f5 = notes.IndexOf("F5");
                _lastFreeLowestIndex = c4 >= 0 ? c4 : notes.Count - 1;
                _lastFreeHighestIndex = f5 >= 0 ? f5 : 0;
            }
        }
    }
}
