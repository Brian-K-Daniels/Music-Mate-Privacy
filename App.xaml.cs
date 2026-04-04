using Application = Microsoft.Maui.Controls.Application;
#if WINDOWS
using Microsoft.Maui.Platform;
using Microsoft.UI.Windowing;
using Windows.Graphics;
#endif

namespace musicmate
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();

            // Initialize premium status at app startup
            InitializePremiumStatus();
        // Deploy saved panel background color early so pages bind to ThemeService with the right color
        DeploySavedPanelBackground();
        }

    private void DeploySavedPanelBackground()
    {
        try
        {
            var theme = Services.ServiceHelper.GetService<Services.ThemeService>();
            if (theme == null)
                return;

            var savedColorHex = Microsoft.Maui.Storage.Preferences.Default.Get<string?>("StaffPanelColor", null);
            if (!string.IsNullOrEmpty(savedColorHex))
            {
                var savedColor = Microsoft.Maui.Graphics.Color.FromArgb(savedColorHex);
                theme.PanelBackgroundColor = savedColor;
                return;
            }

            // No saved color: instantiate a ColorPickerDialog to get its defaults (non-visual use)
            try
            {
                var dialog = new musicmate.Controls.ColorPickerDialog();
                dialog.ResetToDefaults();
                var preview = dialog.PreviewColor;
                theme.PanelBackgroundColor = preview;
                Microsoft.Maui.Storage.Preferences.Default.Set("StaffPanelColor", preview.ToHex());
            }
            catch
            {
                // Fallback to white if any error occurs
                theme.PanelBackgroundColor = Microsoft.Maui.Graphics.Colors.White;
            }
        }
        catch { }
    }

        private async void InitializePremiumStatus()
        {
            var storeService = Services.ServiceHelper.GetService<Services.IStoreService>();
            if (storeService != null)
            {
                try
                {
                    await storeService.InitializeAsync();
                    var purchased = await storeService.IsPurchasedAsync("premium");
                    Services.StatusService.Instance.IsPremiumUser = purchased;
                }
                catch
                {
                    // fallback to preferences if store fails
                    var val = Microsoft.Maui.Storage.Preferences.Get("IsPremium", false);
                    Services.StatusService.Instance.IsPremiumUser = val;
                }
            }
            else
            {
                var val = Microsoft.Maui.Storage.Preferences.Get("IsPremium", false);
                Services.StatusService.Instance.IsPremiumUser = val;
            }
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new AppShell());
        }
    }
}
