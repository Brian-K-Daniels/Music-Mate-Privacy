using System.Diagnostics;

namespace musicmate.Diagnostics;

/// <summary>
/// Timestamped Go → Count-In → microphone startup trace.
/// Always written (Debug output and Android logcat), not gated on DEBUG.
/// </summary>
public static class ListeningStartupLog
{
    public static void Write(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        FirstNoteAndroidReleaseLog.WriteAlways("startup", line);
    }

    public static void Exception(string stage, Exception ex)
        => Write($"{stage} exception {ex.GetType().Name}: {ex.Message}");

    /// <summary>Declaring type and method of the caller of the caller.</summary>
    public static string Caller()
    {
        var method = new StackTrace(2, false).GetFrame(0)?.GetMethod();
        if (method == null)
            return "?";
        var typeName = method.DeclaringType?.Name ?? "?";
        return $"{typeName}.{method.Name}";
    }
}
