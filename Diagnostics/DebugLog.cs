using System.Diagnostics;

namespace musicmate.Diagnostics;

/// <summary>DEBUG-only log output gated by <see cref="DebugLogSettings"/>.</summary>
public static class DebugLog
{    [Conditional("DEBUG")]
    public static void WriteLine(string message)
    {
        if (!DebugLogSettings.IsEnabled(DebugLogSettings.ResolveFromMessage(message)))
            return;
        Debug.WriteLine(message);
    }

    [Conditional("DEBUG")]
    public static void WriteLine(DebugLogCategory category, string message)
    {
        if (!DebugLogSettings.IsEnabled(category))
            return;
        Debug.WriteLine(message);
    }

    [Conditional("DEBUG")]
    public static void RunIfEnabled(DebugLogCategory category, Action action)
    {
        if (!DebugLogSettings.IsEnabled(category))
            return;
        action();
    }
}
