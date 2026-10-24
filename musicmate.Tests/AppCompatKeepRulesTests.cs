namespace musicmate.Tests;

public class AppCompatKeepRulesTests
{
    [Fact]
    public void ProguardCfg_KeepsTheTwoFitWindowsClassesOnly()
    {
        string text = File.ReadAllText(FindRepoFile(Path.Combine("Platforms", "Android", "proguard.cfg")));

        Assert.Contains("-keep class androidx.appcompat.widget.FitWindowsFrameLayout { *; }", text);
        Assert.Contains("-keep class androidx.appcompat.widget.FitWindowsLinearLayout { *; }", text);
        Assert.DoesNotContain("androidx.appcompat.widget.**", text);
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate {relativePath} from {AppContext.BaseDirectory}");
    }
}
