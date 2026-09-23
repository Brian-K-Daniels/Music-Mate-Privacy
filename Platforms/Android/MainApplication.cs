using Android.App;
using Android.Runtime;
using musicmate.Diagnostics;

namespace musicmate
{
    [Application]
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
            StartupTiming.Mark("MainApplication.Ctor",
                $"pid={Android.OS.Process.MyPid()}");
            AppLifecycleLog.WriteAlways("MainApplication", "Ctor",
                $"pid={Android.OS.Process.MyPid()}");
        }

        public override void OnCreate()
        {
            StartupTiming.Mark("MainApplication.OnCreate:begin");
            AppLifecycleLog.WriteAlways("MainApplication", "OnCreate");
            base.OnCreate();
            StartupTiming.Mark("MainApplication.OnCreate:end");
        }

        public override void OnTerminate()
        {
            AppLifecycleLog.WriteTerminationIntent("MainApplication", "OnTerminate");
            AppLifecycleLog.WriteAlways("MainApplication", "OnTerminate");
            base.OnTerminate();
        }

        protected override MauiApp CreateMauiApp()
        {
            StartupTiming.Mark("MainApplication.CreateMauiApp:begin");
            var app = MauiProgram.CreateMauiApp();
            StartupTiming.Mark("MainApplication.CreateMauiApp:end");
            return app;
        }
    }
}
