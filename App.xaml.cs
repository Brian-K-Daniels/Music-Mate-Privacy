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
            // Release: clear backup-/DEBUG-restored local flags, then start non-premium.
            // A successful Play purchase query may then grant or keep false; a failed
            // query must not be treated as proof of non-ownership (leave false + retry later).
            ClearLocalPremiumCache();
            ForceNonPremium();
#endif

            var storeService = Services.ServiceHelper.GetService<Services.IStoreService>();
            if (storeService != null)
            {
                try
                {
                    await storeService.InitializeAsync();
                    var purchased = await storeService.IsPurchasedAsync(PremiumProduct.Id);
#if !DEBUG
                    // null = billing query failed/disconnected — do not change entitlement.
                    if (purchased is true)
                        Services.StatusService.Instance.IsPremiumUser = true;
                    else if (purchased is false)
                        ForceNonPremium();
#else
                    if (purchased is bool known)
                        Services.StatusService.Instance.IsPremiumUser = known;
#endif
                }
                catch
                {
#if DEBUG
                    // Debug: restore persisted state so testers don't lose premium on restart.
                    var val = Preferences.Get(PremiumProduct.PreferenceKey, false);
                    Services.StatusService.Instance.IsPremiumUser = val;
#else
                    // Exception during billing setup — leave the cleared non-premium state;
                    // page OnAppearing CheckPremiumStatusAsync will retry.
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
