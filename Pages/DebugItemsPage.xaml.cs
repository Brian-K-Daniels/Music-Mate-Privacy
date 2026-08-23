using musicmate.Services;
using musicmate.Utilities;
using musicmate.ViewModels;

namespace musicmate.Pages;

public partial class DebugItemsPage : ContentPage
{
    private readonly IOrientationService _orientation;

    public DebugItemsPage()
    {
        InitializeComponent();
        _orientation = ServiceHelper.GetService<IOrientationService>()!;
        var theme = ServiceHelper.GetService<ThemeService>()!;
        BindingContext = new DebugItemsPageViewModel(theme);
    }

    protected override void OnAppearing()
    {
        _orientation?.ForceLandscape();
        base.OnAppearing();
    }

    private async void OnNavigatePracticeClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("//MusicPage");
}
