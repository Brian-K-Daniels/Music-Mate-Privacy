using System.Diagnostics;

namespace musicmate.Diagnostics;

/// <summary>
/// Lightweight DEBUG lifecycle breadcrumbs. One short line per event — never runs
/// expensive validation. Helps distinguish app exit vs debugger detach.
/// </summary>
public static class AppLifecycleLog
{
    private static int _hooksRegistered;

    [Conditional("DEBUG")]
    public static void Write(string source, string eventName, string? detail = null)
    {
        string line = string.IsNullOrEmpty(detail)
            ? $"[Lifecycle] {source}.{eventName}"
            : $"[Lifecycle] {source}.{eventName} {detail}";
        Debug.WriteLine(line);
#if ANDROID
        try { global::Android.Util.Log.Info("MusicMate", line); } catch { }
#endif
    }

    [Conditional("DEBUG")]
    public static void RegisterUnhandledExceptionHooks()
    {
        if (Interlocked.Exchange(ref _hooksRegistered, 1) != 0)
            return;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Write("AppDomain", "UnhandledException",
                e.ExceptionObject is Exception ex ? ex.ToString() : $"{e.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("TaskScheduler", "UnobservedTaskException", e.Exception.ToString());
            e.SetObserved();
        };
    }
}
