using System.Diagnostics;

namespace musicmate.Diagnostics;

/// <summary>
/// Lightweight lifecycle breadcrumbs. Helps distinguish app-initiated exit vs
/// debugger/adb detach (MonoVsDbg stop + exit code 0 + "Requested dump for pid").
/// </summary>
public static class AppLifecycleLog
{
    public const string AndroidTag = "MusicMateLifecycle";

    private static int _hooksRegistered;

    [Conditional("DEBUG")]
    public static void Write(string source, string eventName, string? detail = null)
        => WriteCore(source, eventName, detail);

    /// <summary>
    /// Always emits (Debug and Release) — use immediately before any path that can
    /// end the Activity/process, and on destroy/stop so logcat survives debugger detach.
    /// </summary>
    public static void WriteAlways(string source, string eventName, string? detail = null)
        => WriteCore(source, eventName, detail);

    /// <summary>
    /// Call immediately before any intentional Activity/process termination.
    /// There are currently no such Music Mate call sites; keep this for any future path.
    /// </summary>
    public static void WriteTerminationIntent(string source, string api, string? detail = null)
    {
        string message = string.IsNullOrEmpty(detail)
            ? $"[Lifecycle] TERMINATION_INTENT {source} api={api}"
            : $"[Lifecycle] TERMINATION_INTENT {source} api={api} {detail}";
        Debug.WriteLine(message);
#if ANDROID
        try { global::Android.Util.Log.Warn(AndroidTag, message); } catch { }
#endif
    }

    private static void WriteCore(string source, string eventName, string? detail)
    {
        string line = string.IsNullOrEmpty(detail)
            ? $"[Lifecycle] {source}.{eventName}"
            : $"[Lifecycle] {source}.{eventName} {detail}";
        Debug.WriteLine(line);
#if ANDROID
        try { global::Android.Util.Log.Info(AndroidTag, line); } catch { }
#endif
    }

    [Conditional("DEBUG")]
    public static void RegisterUnhandledExceptionHooks()
    {
        if (Interlocked.Exchange(ref _hooksRegistered, 1) != 0)
            return;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            WriteAlways("AppDomain", "UnhandledException",
                e.ExceptionObject is Exception ex ? ex.ToString() : $"{e.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteAlways("TaskScheduler", "UnobservedTaskException", e.Exception.ToString());
            e.SetObserved();
        };
    }
}
