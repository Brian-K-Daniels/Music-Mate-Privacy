using System.Collections.Concurrent;
using System.Linq;
using System.Text;

namespace musicmate.Drawables;

/// <summary>
/// Temporary horizontal-layout diagnostics (execution counts + stage snapshots).
/// Enable only from diagnostic tests; no-op when <see cref="Enabled"/> is false.
/// </summary>
public static class StaffLayoutDiag
{
    public static bool Enabled { get; set; }

    private static readonly ConcurrentDictionary<string, int> Counts = new(StringComparer.Ordinal);
    private static readonly StringBuilder Log = new();
    private static readonly object Gate = new();

    public static void Reset()
    {
        Counts.Clear();
        lock (Gate) Log.Clear();
    }

    public static void Count(string method)
    {
        if (!Enabled)
            return;
        Counts.AddOrUpdate(method, 1, (_, n) => n + 1);
    }

    public static int GetCount(string method)
        => Counts.TryGetValue(method, out int n) ? n : 0;

    public static string FormatCounts()
    {
        var sb = new StringBuilder();
        foreach (var kv in Counts.OrderBy(k => k.Key))
            sb.AppendLine($"{kv.Key} = {kv.Value}");
        return sb.ToString();
    }

    public static void Append(string line)
    {
        if (!Enabled)
            return;
        lock (Gate) Log.AppendLine(line);
    }

    public static string GetLog()
    {
        lock (Gate) return Log.ToString();
    }
}
