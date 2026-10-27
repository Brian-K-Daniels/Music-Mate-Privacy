using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace musicmate.Tests;

public class AndroidReleaseR8ConfigTests
{
    [Fact]
    public void Csproj_ReleaseAndroid_EnablesPrivateMembersObfuscation()
    {
        var groups = PropertyGroups();

        var releaseAndroid = groups.Single(g =>
            ConditionIs(g, "'$(TargetFramework)' == 'net10.0-android' and '$(Configuration)' == 'Release'"));

        Assert.Equal("private-members", releaseAndroid.Element("AndroidR8ObfuscationMode")?.Value);
    }

    [Fact]
    public void Csproj_DebugAndLocalRelease_DoNotEnableR8Obfuscation()
    {
        foreach (var group in PropertyGroups())
        {
            string condition = (string?)group.Attribute("Condition") ?? string.Empty;
            bool isDebug = condition.Contains("'$(Configuration)' == 'Debug'", StringComparison.Ordinal);
            bool isLocalRelease = condition.Contains("'$(Configuration)' == 'LocalRelease'", StringComparison.Ordinal)
                || condition.Contains("'$(Configuration)' == 'Release' or '$(Configuration)' == 'LocalRelease'", StringComparison.Ordinal);

            if (isDebug || isLocalRelease)
                Assert.Null(group.Element("AndroidR8ObfuscationMode"));
        }
    }

    [Fact]
    public void Csproj_PreservesStage1PackagingSettingsAndVersion()
    {
        string text = File.ReadAllText(FindRepoFile("musicmate.csproj"));

        Assert.Contains("<AndroidLinkMode>SdkOnly</AndroidLinkMode>", text);
        Assert.Contains("<AndroidLinkTool>r8</AndroidLinkTool>", text);
        Assert.Contains("<ApplicationDisplayVersion>1.33</ApplicationDisplayVersion>", text);
        Assert.Contains("<ApplicationVersion>33</ApplicationVersion>", text);
        Assert.Contains("<RuntimeIdentifiers Condition=\"$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'\">android-arm;android-arm64;android-x64</RuntimeIdentifiers>", text);
        Assert.DoesNotContain("AndroidLinkMode>Full", text);
        Assert.DoesNotContain("RunAOTCompilation>false", text);
        Assert.DoesNotContain("AndroidIncludeDebugSymbols>true</AndroidIncludeDebugSymbols>", text.Split("<PropertyGroup Condition=\"'$(Configuration)' == 'Debug'\"")[0]);
    }

    [Fact]
    public void ProguardCfg_StillKeepsOnlyTheTwoFitWindowsClasses()
    {
        string text = File.ReadAllText(FindRepoFile(Path.Combine("Platforms", "Android", "proguard.cfg")));

        Assert.Contains("-keep class androidx.appcompat.widget.FitWindowsFrameLayout { *; }", text);
        Assert.Contains("-keep class androidx.appcompat.widget.FitWindowsLinearLayout { *; }", text);
        Assert.DoesNotContain("androidx.appcompat.widget.**", text);
        Assert.Equal(2, Regex.Matches(text, @"^-keep\b", RegexOptions.Multiline).Count);
    }

    [Fact]
    public void LinkerXml_StillPreservesBillingClient()
    {
        string text = File.ReadAllText(FindRepoFile(Path.Combine("Platforms", "Android", "Linker.xml")));

        Assert.Contains("Xamarin.Android.Google.BillingClient", text);
        Assert.Contains("<type fullname=\"*\" preserve=\"all\" />", text);
    }

    private static IEnumerable<XElement> PropertyGroups()
    {
        var doc = XDocument.Load(FindRepoFile("musicmate.csproj"));
        return doc.Root!.Elements().Where(e => e.Name.LocalName == "PropertyGroup");
    }

    private static bool ConditionIs(XElement group, string expected)
        => string.Equals((string?)group.Attribute("Condition"), expected, StringComparison.Ordinal);

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
