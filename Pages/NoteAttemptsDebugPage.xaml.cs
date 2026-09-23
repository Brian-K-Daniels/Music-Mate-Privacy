using musicmate.Services;
using musicmate.ViewModels;

namespace musicmate.Pages;

public partial class NoteAttemptsDebugPage : ContentPage
{
    private readonly IOrientationService _orientation;
    private readonly NoteAttemptsDebugPageViewModel _viewModel;

    public NoteAttemptsDebugPage()
    {
        InitializeComponent();
        _orientation = ServiceHelper.GetService<IOrientationService>()!;
        var db = ServiceHelper.GetService<NoteAttemptDatabase>()!;
        var theme = ServiceHelper.GetService<ThemeService>()!;
        _viewModel = new NoteAttemptsDebugPageViewModel(db, theme);
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        _orientation?.ForceLandscape();
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }

    private async void OnNavigatePracticeClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//MusicPage");

    private async void OnClearDatabaseClicked(object? sender, EventArgs e)
    {
        bool confirm = await DisplayAlertAsync(
            "Clear Note Attempts",
            "Delete all rows in note_attempts.db3? This cannot be undone.",
            "Clear",
            "Cancel");
        if (!confirm)
            return;

        await _viewModel.ClearAsync();
    }
}
