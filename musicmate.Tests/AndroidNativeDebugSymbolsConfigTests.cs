using System.Diagnostics;
using System.IO.Compression;
using System.Text;
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
    public void PackScript_IncludesOnlyUnstrippedLibxamarinAppPerAbi()
    {
        string script = File.ReadAllText(FindRepoFile(Path.Combine("scripts", "pack-native-debug-symbols.ps1")));
        Assert.Contains("arm64-v8a/libxamarin-app.so", script);
        Assert.Contains("armeabi-v7a/libxamarin-app.so", script);
        Assert.Contains("x86_64/libxamarin-app.so", script);
        Assert.Contains("libxamarin-app.dbg.so", script);
        Assert.Contains("AOT libaot-*.dll.so", script);
        Assert.Contains("mapping.txt", script);
        Assert.DoesNotContain("Get-AbiFromEntry", script);
    }

    [Fact]
    public void PackScript_ProducesExactlyThreeLibxamarinAppSoFiles_FromSameBuildDbgSo()
    {
        string script = FindRepoFile(Path.Combine("scripts", "pack-native-debug-symbols.ps1"));
        string root = Path.Combine(Path.GetTempPath(), "mm-symbols-pack-" + Guid.NewGuid().ToString("N"));
        string shared = Path.Combine(root, "app_shared_libraries");
        string aab = Path.Combine(root, "com.bkdaniels.musicmate-Signed.aab");
        string zip = Path.Combine(root, "native-debug-symbols.zip");
        string[] abis = ["arm64-v8a", "armeabi-v7a", "x86_64"];

        try
        {
            foreach (string abi in abis)
            {
                string dir = Path.Combine(shared, abi);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "libxamarin-app.dbg.so"), "DBG-" + abi);
                File.WriteAllText(Path.Combine(dir, "libxamarin-app.so"), "STRIPPED-" + abi);
                File.WriteAllText(Path.Combine(dir, "assembly-store.so"), "STORE-" + abi);
                File.WriteAllText(Path.Combine(dir, "assembly-store.so.manifest"), "MANIFEST-" + abi);
            }

            using (var package = ZipFile.Open(aab, ZipArchiveMode.Create))
            {
                foreach (string abi in abis)
                {
                    WriteZipEntry(package, $"base/lib/{abi}/libxamarin-app.so", "STRIPPED-" + abi);
                    WriteZipEntry(package, $"base/lib/{abi}/libaot-musicmate.dll.so", "AOT-" + abi);
                    WriteZipEntry(package, $"base/lib/{abi}/libarc.bin.so", "ARC-" + abi);
                    WriteZipEntry(package, $"base/lib/{abi}/libassembly-store.so", "STORE-" + abi);
                    WriteZipEntry(package, $"base/lib/{abi}/libmonosgen-2.0.so", "MONO-" + abi);
                }
            }

            RunPacker(script, aab, zip, shared);

            using var symbols = ZipFile.OpenRead(zip);
            var files = symbols.Entries
                .Where(e => !e.FullName.EndsWith('/') && !e.FullName.EndsWith('\\'))
                .Select(e => (Path: e.FullName.Replace('\\', '/'), Entry: e))
                .OrderBy(x => x.Path, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(
                new[]
                {
                    "arm64-v8a/libxamarin-app.so",
                    "armeabi-v7a/libxamarin-app.so",
                    "x86_64/libxamarin-app.so",
                },
                files.Select(x => x.Path).ToArray());

            foreach (var file in files)
            {
                string abi = file.Path.Split('/')[0];
                using var reader = new StreamReader(file.Entry.Open(), Encoding.UTF8);
                Assert.Equal("DBG-" + abi, reader.ReadToEnd());
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackScript_Fails_WhenDbgSoForSignedPackageAbiIsMissing()
    {
        string script = FindRepoFile(Path.Combine("scripts", "pack-native-debug-symbols.ps1"));
        string root = Path.Combine(Path.GetTempPath(), "mm-symbols-missing-" + Guid.NewGuid().ToString("N"));
        string shared = Path.Combine(root, "app_shared_libraries");
        string aab = Path.Combine(root, "com.bkdaniels.musicmate-Signed.aab");
        string zip = Path.Combine(root, "native-debug-symbols.zip");

        try
        {
            foreach (string abi in new[] { "arm64-v8a", "armeabi-v7a" })
            {
                Directory.CreateDirectory(Path.Combine(shared, abi));
                File.WriteAllText(Path.Combine(shared, abi, "libxamarin-app.dbg.so"), "DBG-" + abi);
            }

            using (var package = ZipFile.Open(aab, ZipArchiveMode.Create))
            {
                foreach (string abi in new[] { "arm64-v8a", "armeabi-v7a", "x86_64" })
                    WriteZipEntry(package, $"base/lib/{abi}/libxamarin-app.so", "STRIPPED-" + abi);
            }

            var ex = Assert.Throws<InvalidOperationException>(
                () => RunPacker(script, aab, zip, shared));
            Assert.Contains("libxamarin-app.dbg.so", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(zip));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
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

    private static void WriteZipEntry(ZipArchive archive, string name, string contents)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(contents);
    }

    private static void RunPacker(string script, string packagePath, string outputZip, string sharedDir)
    {
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments =
                "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" " +
                "-PackagePath \"" + packagePath + "\" " +
                "-OutputZip \"" + outputZip + "\" " +
                "-AppSharedLibrariesDir \"" + sharedDir + "\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start powershell.exe to run the packer.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("pack-native-debug-symbols.ps1 timed out.");
        }

        string stdout = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "pack-native-debug-symbols.ps1 failed (exit " + process.ExitCode + "): " +
                stderr + stdout);
        }
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
