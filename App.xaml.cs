using Application = Microsoft.Maui.Controls.Application;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using musicmate.Diagnostics;
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
            StartupTiming.Mark("App.Ctor:begin");
            AppLifecycleLog.Write("App", "Ctor");
            InitializeComponent();

            StartupTiming.Time("PrefSchemaMigration.ApplyIfNeeded", PrefSchemaMigration.ApplyIfNeeded);

            // Premium status is async and must not block first paint.
            _ = Task.Run(async () =>
            {
                try
                {
                    StartupTiming.Mark("InitializePremiumStatus:begin");
                    await InitializePremiumStatusCoreAsync().ConfigureAwait(false);
                    StartupTiming.Mark("InitializePremiumStatus:end");
                }
                catch (Exception ex)
                {
                    StartupTiming.Mark("InitializePremiumStatus:error", ex.Message);
                }
            });

            StartupTiming.Time("LoadSavedThemeColors", LoadSavedThemeColors);
            StartupTiming.Mark("App.Ctor:end");
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

        private async Task InitializePremiumStatusCoreAsync()
        {
#if !DEBUG && !LOCAL_RELEASE
            // Play Release: clear backup-/DEBUG-restored local flags, then start non-premium.
            // A successful Play purchase query may then grant or keep false; a failed
            // query must not be treated as proof of non-ownership (leave false + retry later).
            ClearLocalPremiumCache();
            await MainThread.InvokeOnMainThreadAsync(ForceNonPremium);
#endif

            var storeService = Services.ServiceHelper.GetService<Services.IStoreService>();
            if (storeService != null)
            {
                try
                {
                    await storeService.InitializeAsync().ConfigureAwait(false);
                    var purchased = await storeService.IsPurchasedAsync(PremiumProduct.Id).ConfigureAwait(false);
#if !DEBUG && !LOCAL_RELEASE
                    // null = billing query failed/disconnected — do not change entitlement.
                    if (purchased is true)
                    {
                        await MainThread.InvokeOnMainThreadAsync(() =>
                            Services.StatusService.Instance.IsPremiumUser = true);
                    }
                    else if (purchased is false)
                    {
                        await MainThread.InvokeOnMainThreadAsync(ForceNonPremium);
                    }
#else
                    if (purchased is bool known)
                    {
                        await MainThread.InvokeOnMainThreadAsync(() =>
                            Services.StatusService.Instance.IsPremiumUser = known);
                    }
#endif
                }
                catch
                {
#if DEBUG || LOCAL_RELEASE
                    // Stub builds: restore persisted state so testers don't lose premium on restart.
                    var val = Preferences.Get(PremiumProduct.PreferenceKey, false);
                    await MainThread.InvokeOnMainThreadAsync(() =>
                        Services.StatusService.Instance.IsPremiumUser = val);
#else
                    // Exception during billing setup — leave the cleared non-premium state;
                    // page OnAppearing CheckPremiumStatusAsync will retry.
#endif
                }
            }
            else
            {
#if DEBUG || LOCAL_RELEASE
                var val = Preferences.Get(PremiumProduct.PreferenceKey, false);
                await MainThread.InvokeOnMainThreadAsync(() =>
                    Services.StatusService.Instance.IsPremiumUser = val);
#else
                await MainThread.InvokeOnMainThreadAsync(ForceNonPremium);
#endif
            }
        }

#if !DEBUG && !LOCAL_RELEASE
        private static void ForceNonPremium()
        {
            ClearLocalPremiumCache();
            // Setter no-ops when already false — clear the field directly too.
            if (StatusService.Instance.IsPremiumUser)
                StatusService.Instance.IsPremiumUser = false;
        }

        private static void ClearLocalPremiumCache()
        {
            try { Preferences.Remove(PremiumProduct.PreferenceKey); } catch { }
            try { SessionPreferences.Remove(PremiumProduct.PreferenceKey); } catch { }
        }
#endif

        protected override Window CreateWindow(IActivationState? activationState)
        {
            StartupTiming.Mark("App.CreateWindow:begin");
            AppLifecycleLog.WriteAlways("App", "CreateWindow");
            var window = StartupTiming.Time("new Window(AppShell)", () => new Window(new AppShell()));
            // Swipe-away / close / background: stop Count-In and metronome clicks.
            window.Stopped += (_, _) =>
            {
                AppLifecycleLog.WriteAlways("App", "Window.Stopped");
                Services.AppCueAudioGate.NotifyAppSuspended();
            };
            window.Destroying += (_, _) =>
            {
                AppLifecycleLog.WriteAlways("App", "Window.Destroying");
                Services.AppCueAudioGate.NotifyAppSuspended();
            };
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                Services.ServiceHelper.GetService<Services.ThemeService>()?.ApplyToShellIfAvailable();
            });
            StartupTiming.Mark("App.CreateWindow:end");
            return window;
        }

        protected override void OnStart()
        {
            StartupTiming.Mark("App.OnStart");
            AppLifecycleLog.WriteAlways("App", "OnStart");
            base.OnStart();
        }

        protected override void OnResume()
        {
            AppLifecycleLog.WriteAlways("App", "OnResume");
            base.OnResume();
            Services.AppCueAudioGate.NotifyAppResumed();
        }

        protected override void OnSleep()
        {
            AppLifecycleLog.WriteAlways("App", "OnSleep");
            base.OnSleep();
            // Suspend mic / cue audio after a short grace. A microphone plug-in pauses and
            // resumes immediately; screen-off and background stay paused and still stop audio.
            Services.AppCueAudioGate.NotifyAppSuspended();
        }
    }
}
