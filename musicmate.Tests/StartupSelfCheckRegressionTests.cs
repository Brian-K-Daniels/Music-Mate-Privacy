namespace musicmate.Tests;

/// <summary>
/// Guards against reintroducing expensive DEBUG self-checks into app startup.
/// Those used to flood the debugger (thousands of PASS lines) and stall Android.
/// </summary>
public class StartupSelfCheckRegressionTests
{
    [Fact]
    public void MauiProgram_DoesNotInvokeInAppScaleOrStaffSelfChecks()
    {
        string path = FindRepoFile("MauiProgram.cs");
        string text = File.ReadAllText(path);

        Assert.DoesNotContain("ScheduleDebugSelfChecks", text);
        Assert.DoesNotContain("ScaleKeyRandomTests.RunSelfChecks", text);
        Assert.DoesNotContain("ChildLevelScaleSelectionTests.RunSelfChecks", text);
        Assert.DoesNotContain("RunKeySignatureTests()", text);
        Assert.DoesNotContain("RunMeasureLayoutTests()", text);
    }

    [Fact]
    public void MusicPage_DoesNotInvokeStaffSelfChecksOnAppearing()
    {
        string path = FindRepoFile(Path.Combine("Pages", "MusicPage.xaml.cs"));
        string text = File.ReadAllText(path);

        Assert.DoesNotContain("RunKeySignatureTests()", text);
        Assert.DoesNotContain("RunMeasureLayoutTests()", text);
        Assert.DoesNotContain("ScaleKeyRandomTests", text);
        Assert.DoesNotContain("ChildLevelScaleSelectionTests", text);
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;

            string nested = Path.Combine(dir.FullName, "musicmate", relativePath);
            if (File.Exists(nested))
                return nested;

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate {relativePath} from {AppContext.BaseDirectory}");
    }
}
