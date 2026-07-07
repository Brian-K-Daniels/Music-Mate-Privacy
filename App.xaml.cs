using Application = Microsoft.Maui.Controls.Application;
using Microsoft.Maui.ApplicationModel;
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

            Services.PrefSchemaMigration.ApplyIfNeeded();

            // Initialize premium status at app startup
            InitializePremiumStatus();
            LoadSavedThemeColors();
        }

        private void LoadSavedThemeColors()
        {
            try
            {
                var theme = Services.ServiceHelper.GetService<Services.ThemeService>();
                theme?.LoadFromPreferences();
            }
            catch { }
        }

        private async void InitializePremiumStatus()
        {
#if !DEBUG
            // One-time migration: clear any stale IsPremium preference left by a DEBUG install.
            // The sentinel key records the last app version that ran this wipe, so it only
            // fires once per version upgrade rather than on every cold start.
            const string WipeSentinelKey = "PremiumWipedForVersion";
            string currentVersion = AppInfo.Current.VersionString;
            string lastWipedVersion = Microsoft.Maui.Storage.Preferences.Get(WipeSentinelKey, "");
            if (lastWipedVersion != currentVersion)
            {
                Microsoft.Maui.Storage.Preferences.Remove("IsPremium");
                Microsoft.Maui.Storage.Preferences.Set(WipeSentinelKey, currentVersion);
            }
#endif

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
#if DEBUG
                    // Debug: restore persisted state so testers don't lose premium on restart.
                    var val = Microsoft.Maui.Storage.Preferences.Get("IsPremium", false);
                    Services.StatusService.Instance.IsPremiumUser = val;
#else
                    // Release: store failed — default to non-premium; user can restore purchase.
                    Services.StatusService.Instance.IsPremiumUser = false;
#endif
                }
            }
            else
            {
#if DEBUG
                var val = Microsoft.Maui.Storage.Preferences.Get("IsPremium", false);
                Services.StatusService.Instance.IsPremiumUser = val;
#else
                Services.StatusService.Instance.IsPremiumUser = false;
#endif
            }
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new AppShell());
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                Services.ServiceHelper.GetService<Services.ThemeService>()?.ApplyToShellIfAvailable();
            });
            return window;
        }
    }
}
