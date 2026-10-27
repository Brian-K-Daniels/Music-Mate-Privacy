using System.Xml.Linq;

namespace musicmate.Tests;

public class AndroidNativeDebugSymbolsConfigTests
{
    [Fact]
    public void Csproj_ImportsNativeDebugSymbolsTargets()
    {
        string text = File.ReadAllText(FindRepoFile("musicmate.csproj"));
        Assert.Contains("<Import Project=\"Platforms\\Android\\AndroidNativeDebugSymbols.targets\" />", text);
        Assert.Contains("<ApplicationDisplayVersion>1.33</ApplicationDisplayVersion>", text);
        Assert.Contains("<ApplicationVersion>33</ApplicationVersion>", text);
    }

    [Fact]
    public void ReleaseAndroid_PacksSymbolsWithoutChangingRuntimeDebugSettings()
    {
        string csproj = File.ReadAllText(FindRepoFile("musicmate.csproj"));
        string targets = File.ReadAllText(FindRepoFile(Path.Combine("Platforms", "Android", "AndroidNativeDebugSymbols.targets")));

        Assert.Contains("AndroidPackNativeDebugSymbolsArchive", targets);
        Assert.Contains("AfterTargets=\"_Sign;SignAndroidPackage\"", targets);
        Assert.Contains("pack-native-debug-symbols.ps1", targets);

        Assert.DoesNotContain("AndroidIncludeDebugSymbols>true", targets);
        Assert.DoesNotContain("AndroidStripNativeLibraries>true", targets);
        Assert.DoesNotContain("AndroidPreferNativeLibrariesWithDebugSymbols>true", targets);
        Assert.DoesNotContain("AndroidR8ObfuscationMode", targets);
        Assert.DoesNotContain("RunAOTCompilation>false", targets);

        string releaseBlock = csproj.Split("<PropertyGroup Condition=\"'$(Configuration)' == 'Debug'\"")[0];
        Assert.Contains("<AndroidIncludeDebugSymbols>false</AndroidIncludeDebugSymbols>", releaseBlock);
        Assert.DoesNotContain("AndroidIncludeDebugSymbols>true", releaseBlock);
    }

    [Fact]
    public void PackScript_ExcludesR8MappingAndUsesPlayAbiFolders()
    {
        string script = File.ReadAllText(FindRepoFile(Path.Combine("scripts", "pack-native-debug-symbols.ps1")));
        Assert.Contains("mapping.txt is Java/Kotlin deobfuscation and is intentionally excluded", script);
        Assert.Contains("arm64-v8a|armeabi-v7a|x86_64", script);
        Assert.Contains("libxamarin-app.dbg.so", script);
        Assert.DoesNotContain("mapping.txt", script.Split("intentionally excluded")[1].Split('\n')[0]);
    }

    [Fact]
    public void Csproj_ReleaseAndroid_StillEnablesPrivateMembersR8()
    {
        var doc = XDocument.Load(FindRepoFile("musicmate.csproj"));
        var releaseAndroid = doc.Root!.Elements()
            .Where(e => e.Name.LocalName == "PropertyGroup")
            .Single(g => (string?)g.Attribute("Condition")
                == "'$(TargetFramework)' == 'net10.0-android' and '$(Configuration)' == 'Release'");

        Assert.Equal("private-members", releaseAndroid.Element("AndroidR8ObfuscationMode")?.Value);
        Assert.Equal("aab", releaseAndroid.Element("AndroidPackageFormat")?.Value);
        Assert.Null(releaseAndroid.Element("AndroidIncludeDebugSymbols"));
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
