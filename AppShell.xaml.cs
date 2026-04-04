using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using musicmate.Services;
using musicmate.Utilities;

namespace musicmate
{
    public partial class AppShell : Shell
    {
        public ICommand? GoHomeCommand { get; }

        public AppShell()
        {   try
            {
                InitializeComponent();

                BindingContext = this;
                GoHomeCommand = new Command(async () => await GoToAsync("//MainPage"));

                // Register routes for navigation
                Routing.RegisterRoute("SettingsPage", typeof(Pages.SettingsPage));
                Routing.RegisterRoute("StatisticsPages", typeof(Pages.StatisticsPages));
                Routing.RegisterRoute("AboutPage", typeof(Pages.AboutPage));
                // Apply 9mm left margin when navigating and when orientation changes
                //this.Navigated += AppShell_Navigated;  
               // DeviceDisplay.MainDisplayInfoChanged += DeviceDisplay_MainDisplayInfoChanged;  //  2026.04.02 1723  block out


                // Apply once for initial page
                //var initialPage = Shell.Current?.CurrentPage;
                //if (initialPage != null)
                //    ApplyLeftMarginIfLandscape(initialPage);  //  2026.04.02 1724  block out
            }
            catch (Exception ex)
            {
                Utils.Log($"Error initializing AppShell: {ex.Message}");
            }
        }

        //private void DeviceDisplay_MainDisplayInfoChanged(object? sender, DisplayInfoChangedEventArgs e)
        //{
        //    try
        //    {
        //        var page = Shell.Current?.CurrentPage;
        //        if (page != null)
        //            ApplyLeftMarginIfLandscape(page);
        //    }
        //    catch (Exception ex)
        //    {
        //        Utils.Log($"Error applying left margin on orientation change: {ex.Message}");
        //    }
        //}  //  2026.04.02 1722   block out

        //private void AppShell_Navigated(object? sender, ShellNavigatedEventArgs e)
        //{
        //    try
        //    {
        //        var page = Shell.Current?.CurrentPage;
        //        if (page != null)
        //            ApplyLeftMarginIfLandscape(page);
        //    }
        //    catch (Exception ex)
        //    {
        //        Utils.Log($"Error applying left margin after navigation: {ex.Message}");
        //    }
        //}  //  2026.04.02 1722   block out

        //private void ApplyLeftMarginIfLandscape(Page page)
        //{
        //    // Only enforce left margin in landscape
        //    var orientation = DeviceDisplay.MainDisplayInfo.Orientation;
        //    if (orientation != DisplayOrientation.Landscape)
        //        return;

        //    // Try common content container types. Preserve existing top/right/bottom padding.
        //    Layout? layout = null;
        //    if (page is ContentPage cp)
        //    {
        //        if (cp.Content is Layout l)
        //            layout = l;
        //        else if (cp.Content is ScrollView sv && sv.Content is Layout l2)
        //            layout = l2;
        //    }

        //    if (layout != null)
        //    {
        //        var p = layout.Padding;
        //        // Set left padding to 9 mm while preserving other paddings (values are in device-independent pixels)
        //        MarginUtils.SetLeftMarginMM(layout, 9, p.Top, p.Right, p.Bottom);
        //    }
        //}  //  2026.04.02 1721   block out
    }
}
