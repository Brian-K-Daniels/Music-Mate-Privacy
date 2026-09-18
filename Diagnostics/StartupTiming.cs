using System.Diagnostics;

namespace musicmate.Diagnostics;

/// <summary>
/// Timestamped startup breadcrumbs (logcat tag MusicMateStartup) to find main-thread stalls.
/// </summary>
public static class StartupTiming
{
    public const string AndroidTag = "MusicMateStartup";

    private static readonly long s_originMs = Environment.TickCount64;
    private static long s_lastMarkMs = s_originMs;

    public static long ElapsedMs => Environment.TickCount64 - s_originMs;

    public static void Mark(string phase, string? detail = null)
    {
        long now = Environment.TickCount64;
        long sinceOrigin = now - s_originMs;
        long sinceLast = now - Interlocked.Exchange(ref s_lastMarkMs, now);
        string line = string.IsNullOrEmpty(detail)
            ? $"[Startup] +{sinceOrigin}ms Δ{sinceLast}ms {phase}"
            : $"[Startup] +{sinceOrigin}ms Δ{sinceLast}ms {phase} {detail}";
        Debug.WriteLine(line);
#if ANDROID
        try { global::Android.Util.Log.Info(AndroidTag, line); } catch { }
#endif
    }

    /// <summary>Time a synchronous block; logs begin and end with duration.</summary>
    public static void Time(string phase, Action action)
    {
        Mark($"{phase}:begin");
        long start = Environment.TickCount64;
        try
        {
            action();
        }
        finally
        {
            Mark($"{phase}:end", $"took={Environment.TickCount64 - start}ms");
        }
    }

    public static T Time<T>(string phase, Func<T> func)
    {
        Mark($"{phase}:begin");
        long start = Environment.TickCount64;
        try
        {
            return func();
        }
        finally
        {
            Mark($"{phase}:end", $"took={Environment.TickCount64 - start}ms");
        }
    }
}
