using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using musicmate.Diagnostics;
using musicmate.Services;

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
            StartupTiming.Mark("MainActivity.OnCreate:begin",
                $"savedInstance={(savedInstanceState != null)} pid={Android.OS.Process.MyPid()}");
            AppLifecycleLog.WriteAlways("MainActivity", "OnCreate",
                $"savedInstance={(savedInstanceState != null)} pid={Android.OS.Process.MyPid()}");
            base.OnCreate(savedInstanceState);

            // Reinforce landscape as soon as the activity exists (before first page OnAppearing).
            RequestedOrientation = ScreenOrientation.SensorLandscape;

            if (Window != null)
            {
                Window.AddFlags(WindowManagerFlags.Fullscreen);
                Window.ClearFlags(WindowManagerFlags.ForceNotFullscreen);
            }
            StartupTiming.Mark("MainActivity.OnCreate:end");
        }

        public override bool DispatchTouchEvent(MotionEvent? e)
        {
            // Any finger-down counts as user activity for inactivity idle (does not steal the gesture).
            if (e?.ActionMasked == MotionEventActions.Down)
                UserInteractionProbe.NotifyInteraction();
            return base.DispatchTouchEvent(e);
        }

        protected override void OnStart()
        {
            StartupTiming.Mark("MainActivity.OnStart");
            AppLifecycleLog.WriteAlways("MainActivity", "OnStart");
            base.OnStart();
        }

        protected override void OnResume()
        {
            StartupTiming.Mark("MainActivity.OnResume");
            AppLifecycleLog.WriteAlways("MainActivity", "OnResume");
            base.OnResume();
            // Screen-on / return to foreground — Music page may soft-resume listening.
            AppCueAudioGate.NotifyAppResumed();
        }

        protected override void OnPause()
        {
            AppLifecycleLog.WriteAlways("MainActivity", "OnPause",
                $"IsFinishing={IsFinishing}");
            // Screen-off and backgrounding hit OnPause before (or instead of) full navigation hide.
            // A headset or USB microphone also pauses the activity for a moment. The gate waits
            // briefly so that plug-in does not stop Count-In or Play; a pause that stays paused still does.
            AppCueAudioGate.NotifyAppSuspended();
            base.OnPause();
        }

        protected override void OnStop()
        {
            AppLifecycleLog.WriteAlways("MainActivity", "OnStop",
                $"IsFinishing={IsFinishing}");
            AppCueAudioGate.NotifyAppSuspended();
            base.OnStop();
        }

        protected override void OnDestroy()
        {
            AppLifecycleLog.WriteAlways("MainActivity", "OnDestroy",
                $"IsFinishing={IsFinishing} IsChangingConfigurations={IsChangingConfigurations} pid={Android.OS.Process.MyPid()}");
            base.OnDestroy();
        }
    }
}
