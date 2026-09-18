using System.Diagnostics;

namespace musicmate.Diagnostics;

/// <summary>
/// Elapsed-time breadcrumbs for Settings page open (logcat tag MusicMateSettings).
/// </summary>
public static class SettingsLoadTiming
{
    public const string AndroidTag = "MusicMateSettings";

    private static long _openOriginMs;

    public static void BeginOpen()
    {
        _openOriginMs = Environment.TickCount64;
        Write("OPEN:begin");
    }

    public static void Mark(string phase, string? detail = null)
    {
        long elapsed = _openOriginMs == 0 ? 0 : Environment.TickCount64 - _openOriginMs;
        Write(string.IsNullOrEmpty(detail)
            ? $"{phase} +{elapsed}ms"
            : $"{phase} +{elapsed}ms {detail}");
    }

    public static void Time(string phase, Action action)
    {
        Mark($"{phase}:START");
        long start = Environment.TickCount64;
        try
        {
            action();
        }
        finally
        {
            Mark($"{phase}:END", $"took={Environment.TickCount64 - start}ms");
        }
    }

    public static async Task TimeAsync(string phase, Func<Task> action)
    {
        Mark($"{phase}:START");
        long start = Environment.TickCount64;
        try
        {
            await action().ConfigureAwait(true);
        }
        finally
        {
            Mark($"{phase}:END", $"took={Environment.TickCount64 - start}ms");
        }
    }

    private static void Write(string message)
    {
        string line = $"[SettingsLoad] {message}";
        Debug.WriteLine(line);
#if ANDROID
        try { global::Android.Util.Log.Info(AndroidTag, line); } catch { }
#endif
    }
}
