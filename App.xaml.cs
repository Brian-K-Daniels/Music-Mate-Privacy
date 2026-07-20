using Application = Microsoft.Maui.Controls.Application;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using musicmate.Services;
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
                theme?.PushToApplicationResources();
            }
            catch { }
        }

        private async void InitializePremiumStatus()
        {
#if !DEBUG
            // Release: always start non-premium. Clear backup-/DEBUG-restored local flags.
            // Play Store installs may then restore ownership; VS/adb sideloads will not
            // (see GooglePlayStoreService.IsPurchasedAsync).
            ClearLocalPremiumCache();
            ForceNonPremium();
#endif

            var storeService = Services.ServiceHelper.GetService<Services.IStoreService>();
            if (storeService != null)
            {
                try
                {
                    await storeService.InitializeAsync();
#if !DEBUG
                    var purchased = await storeService.IsPurchasedAsync(PremiumProduct.Id);
                    if (purchased)
                        Services.StatusService.Instance.IsPremiumUser = true;
                    else
                        ForceNonPremium();
#else
                    var purchased = await storeService.IsPurchasedAsync(PremiumProduct.Id);
                    Services.StatusService.Instance.IsPremiumUser = purchased;
#endif
                }
                catch
                {
#if DEBUG
                    // Debug: restore persisted state so testers don't lose premium on restart.
                    var val = Preferences.Get(PremiumProduct.PreferenceKey, false);
                    Services.StatusService.Instance.IsPremiumUser = val;
#else
                    ForceNonPremium();
#endif
                }
            }
            else
            {
#if DEBUG
                var val = Preferences.Get(PremiumProduct.PreferenceKey, false);
                Services.StatusService.Instance.IsPremiumUser = val;
#else
                ForceNonPremium();
#endif
            }
        }

#if !DEBUG
        private static void ForceNonPremium()
        {
            ClearLocalPremiumCache();
            // Setter no-ops when already false — clear the field directly too.
            if (StatusService.Instance.IsPremiumUser)
                StatusService.Instance.IsPremiumUser = false;
        }
#endif

#if !DEBUG
        private static void ClearLocalPremiumCache()
        {
            try { Preferences.Remove(PremiumProduct.PreferenceKey); } catch { }
            try { SessionPreferences.Remove(PremiumProduct.PreferenceKey); } catch { }
        }
#endif

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
