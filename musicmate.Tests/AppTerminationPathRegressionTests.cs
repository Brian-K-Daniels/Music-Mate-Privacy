namespace musicmate.Tests;

/// <summary>
/// Guards against introducing intentional process/Activity kill APIs.
/// Clean debug exits (code 0 + "Requested dump for pid" + MonoVsDbg stopped) have
/// historically been debugger/adb detach, not Music Mate calling Finish/Exit.
/// </summary>
public class AppTerminationPathRegressionTests
{
    private static readonly string[] ForbiddenSnippets =
    [
        "Environment.Exit(",
        "Environment.FailFast(",
        "Process.GetCurrentProcess().Kill(",
        "Process.Kill(",
        "JavaSystem.Exit(",
        "FinishAffinity(",
        "FinishAndRemoveTask(",
        "Android.OS.Process.KillProcess(",
        "Activity.Finish(",
    ];

    [Fact]
    public void Source_HasNoIntentionalProcessOrActivityKillApis()
    {
        var roots = new[]
        {
            FindRepoDir(),
            Path.Combine(FindRepoDir(), "Platforms"),
            Path.Combine(FindRepoDir(), "Pages"),
            Path.Combine(FindRepoDir(), "Services"),
            Path.Combine(FindRepoDir(), "Diagnostics"),
        };

        var hits = new List<string>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    || file.Contains("musicmate.Tests", StringComparison.OrdinalIgnoreCase))
                    continue;

                string text = File.ReadAllText(file);
                foreach (var snippet in ForbiddenSnippets)
                {
                    if (text.Contains(snippet, StringComparison.Ordinal))
                        hits.Add($"{Path.GetRelativePath(FindRepoDir(), file)}: {snippet}");
                }

                // Bare Finish() on Activity is harder; flag ".Finish()" only in Android platform code.
                if (file.Contains($"{Path.DirectorySeparatorChar}Platforms{Path.DirectorySeparatorChar}Android{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    && text.Contains(".Finish()", StringComparison.Ordinal)
                    && !text.Contains("OnBillingSetupFinished", StringComparison.Ordinal))
                {
                    // Allow comments mentioning Finish; require word-ish call.
                    if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\bFinish\s*\("))
                        hits.Add($"{Path.GetRelativePath(FindRepoDir(), file)}: Finish(");
                }
            }
        }

        Assert.True(hits.Count == 0,
            "Intentional termination APIs found (add AppLifecycleLog.WriteTerminationIntent before any real use):\n"
            + string.Join("\n", hits));
    }

    [Fact]
    public void AppLifecycleLog_ExposesTerminationIntentHelper()
    {
        string path = Path.Combine(FindRepoDir(), "Diagnostics", "AppLifecycleLog.cs");
        string text = File.ReadAllText(path);
        Assert.Contains("WriteTerminationIntent", text);
        Assert.Contains("TERMINATION_INTENT", text);
    }

    private static string FindRepoDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "musicmate.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate musicmate.csproj");
    }
}
