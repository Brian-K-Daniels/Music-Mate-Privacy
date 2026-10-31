using Microsoft.Maui.ApplicationModel;

namespace musicmate.Diagnostics;

/// <summary>
/// Visible build fingerprint for proving which binary is installed on a device.
/// Git commit and build timestamp are injected at compile time via MSBuild.
/// </summary>
public static class BuildIdentification
{
    public static string VersionName => AppInfo.Current.VersionString;

    public static string VersionCode => AppInfo.Current.BuildString;

    public static string GitCommit => BuildIdentificationMetadata.GitCommit;

    public static string BuildTimestampUtc => BuildIdentificationMetadata.BuildTimestampUtc;

#if DEBUG
    public static string ConfigurationLabel => "Debug";
#else
    public static string ConfigurationLabel => "Release";
#endif

    /// <summary>
    /// True when <see cref="Services.MusicSequenceGenerator.BuildPitchPool"/> scale-order
    /// mastery bypass is compiled into this binary.
    /// </summary>
    public static bool ChromaticScaleWalkBypass =>
        Services.MusicSequenceGenerator.ScaleWalkMasteryBypassCompiled;

    public static string ScaleWalkBypassLabel =>
        ChromaticScaleWalkBypass ? "Chromatic scale-walk bypass: ON" : "Chromatic scale-walk bypass: OFF";

    public static string ShortLine =>
        $"v{VersionName} ({VersionCode}) {GitCommit} {ConfigurationLabel}";

    public static string FullMultiline =>
        $"Version name: {VersionName}\n" +
        $"Version code: {VersionCode}\n" +
        $"Git commit: {GitCommit}\n" +
        $"Build timestamp: {BuildTimestampUtc}\n" +
        $"{ConfigurationLabel}\n" +
        ScaleWalkBypassLabel;
}
