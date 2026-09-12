using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using musicmate.Diagnostics;

namespace musicmate
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
        LaunchMode = LaunchMode.SingleTop,
        ScreenOrientation = ScreenOrientation.SensorLandscape,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            AppLifecycleLog.Write("MainActivity", "OnCreate");
            // SPECIAL DEBUG FOR ANDROID LOG IN RELEASE MODE
            FirstNoteAndroidReleaseLog.WriteAlways("main-activity", "OnCreate");
            base.OnCreate(savedInstanceState);

            // Reinforce landscape as soon as the activity exists (before first page OnAppearing).
            RequestedOrientation = ScreenOrientation.SensorLandscape;

            if (Window != null)
            {
                Window.AddFlags(WindowManagerFlags.Fullscreen);
                Window.ClearFlags(WindowManagerFlags.ForceNotFullscreen);
            }
        }

        protected override void OnStart()
        {
            AppLifecycleLog.Write("MainActivity", "OnStart");
            base.OnStart();
        }

        protected override void OnResume()
        {
            AppLifecycleLog.Write("MainActivity", "OnResume");
            base.OnResume();
        }

        protected override void OnPause()
        {
            AppLifecycleLog.Write("MainActivity", "OnPause");
            base.OnPause();
        }

        protected override void OnStop()
        {
            AppLifecycleLog.Write("MainActivity", "OnStop");
            base.OnStop();
        }

        protected override void OnDestroy()
        {
            AppLifecycleLog.Write("MainActivity", "OnDestroy");
            base.OnDestroy();
        }
    }
}
