using Microsoft.Maui.Storage;

namespace musicmate.Diagnostics;

/// <summary>Categories of DEBUG-only logging and diagnostic checks.</summary>
public enum DebugLogCategory
{
    General,
    Timing,
    SessionStartProfiler,
    LevelUp,
    Picker,
    LayoutTestTune,
    StaffSelfTests,
    ChildLevel,
    Statistics,
    StaffAndSequence,
    StaffLog,
    MusicPageFlow,
    Autoplay,
    ResetOptions,
    SmuFL,
}

/// <summary>
/// Central DEBUG log toggles. All categories default to enabled; persisted per category.
/// </summary>
public static class DebugLogSettings
{
    private const string PreferencePrefix = "debug.log.";

    /// <summary>In-memory store for unit tests (no MAUI Preferences required).</summary>
    internal static Dictionary<string, bool>? TestStore { get; set; }

    public static IReadOnlyList<DebugLogCategory> AllCategories { get; } =
        Enum.GetValues<DebugLogCategory>();

    public static string GetDisplayName(DebugLogCategory category) => category switch
    {
        DebugLogCategory.General => "General (MusicMate log)",
        DebugLogCategory.Timing => "Timing diagnostics and Mastery",
        DebugLogCategory.SessionStartProfiler => "Session start profiler",
        DebugLogCategory.LevelUp => "Level up",
        DebugLogCategory.Picker => "Picker debug",
        DebugLogCategory.LayoutTestTune => "Layout test tune",
        DebugLogCategory.StaffSelfTests => "Staff self-tests",
        DebugLogCategory.ChildLevel => "Child level progression",
        DebugLogCategory.Statistics => "Statistics cache",
        DebugLogCategory.StaffAndSequence => "Staff & sequence generation",
        DebugLogCategory.StaffLog => "Staff layout log",
        DebugLogCategory.MusicPageFlow => "Music page session flow",
        DebugLogCategory.Autoplay => "Autoplay rhythm",
        DebugLogCategory.ResetOptions => "Reset options test",
        DebugLogCategory.SmuFL => "SmuFL font loading",
        _ => category.ToString(),
    };

    public static string GetDescription(DebugLogCategory category) => category switch
    {
        DebugLogCategory.LayoutTestTune =>
            "Fixed layout test tune selection and layout-tune log output.",
        DebugLogCategory.StaffSelfTests =>
            "Key signature, measure layout, and startup self-test output.",
        DebugLogCategory.StaffLog =>
            "Verbose staff drawing, layout, and validation output from StaffLog.",
        DebugLogCategory.SessionStartProfiler =>
            "Phase timing lines when a practice session starts.",
        _ => "Debug log messages for this category.",
    };

    public static bool IsEnabled(DebugLogCategory category)
        => GetPreference(PreferenceKey(category), true);

    public static void SetEnabled(DebugLogCategory category, bool enabled)
    {
        SetPreference(PreferenceKey(category), enabled);
        SyncLegacyFlags(category, enabled);
    }

    public static void LoadAll()
    {
        foreach (var category in AllCategories)
            SyncLegacyFlags(category, IsEnabled(category));
    }

    /// <summary>Maps a log line's [Tag] prefix to a category.</summary>
    public static DebugLogCategory ResolveFromMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return DebugLogCategory.General;

        if (message.StartsWith("[LevelUpDebug]", StringComparison.Ordinal)
            || message.StartsWith("[LevelUp]", StringComparison.Ordinal)
            || message.StartsWith("[LevelUpService]", StringComparison.Ordinal))
            return DebugLogCategory.LevelUp;

        if (message.StartsWith("[TimingWrong]", StringComparison.Ordinal)
            || message.StartsWith("[RestTimingWrong]", StringComparison.Ordinal)
            || message.StartsWith("[TimingSummary]", StringComparison.Ordinal)
            || message.StartsWith("[Mastery]", StringComparison.Ordinal))
            return DebugLogCategory.Timing;

        if (message.StartsWith("[SessionStart]", StringComparison.Ordinal))
            return DebugLogCategory.SessionStartProfiler;

        if (message.StartsWith("[PickerDBG]", StringComparison.Ordinal)
            || message.StartsWith("[PickerTest]", StringComparison.Ordinal))
            return DebugLogCategory.Picker;

        if (message.StartsWith("[LayoutTestTune]", StringComparison.Ordinal)
            || message.StartsWith("[LayoutTest]", StringComparison.Ordinal))
            return DebugLogCategory.LayoutTestTune;

        if (message.StartsWith("[KeySigTest]", StringComparison.Ordinal)
            || message.StartsWith("[MeasureLayoutTest]", StringComparison.Ordinal)
            || message.StartsWith("[TransposeTest]", StringComparison.Ordinal))
            return DebugLogCategory.StaffSelfTests;

        if (message.StartsWith("[ChildLevel]", StringComparison.Ordinal)
            || message.StartsWith("[ChildPractice]", StringComparison.Ordinal))
            return DebugLogCategory.ChildLevel;

        if (message.StartsWith("[NoteStatisticsViewModel]", StringComparison.Ordinal)
            || message.StartsWith("[StatisticsCacheService]", StringComparison.Ordinal))
            return DebugLogCategory.Statistics;

        if (message.StartsWith("[StaffGen]", StringComparison.Ordinal)
            || message.StartsWith("[Staff Standard]", StringComparison.Ordinal)
            || message.StartsWith("[StaffPool]", StringComparison.Ordinal)
            || message.StartsWith("[Staff]", StringComparison.Ordinal)
            || message.StartsWith("[Staff Draw]", StringComparison.Ordinal)
            || message.StartsWith("[Random]", StringComparison.Ordinal)
            || message.StartsWith("[MotifPhrase]", StringComparison.Ordinal)
            || message.StartsWith("[Composition]", StringComparison.Ordinal)
            || message.StartsWith("[Arpeggio]", StringComparison.Ordinal)
            || message.StartsWith("[ScaleRootTest]", StringComparison.Ordinal))
            return DebugLogCategory.StaffAndSequence;

        if (message.StartsWith("[Staff Validation]", StringComparison.Ordinal)
            || message.StartsWith("[StaffLayout]", StringComparison.Ordinal))
            return DebugLogCategory.StaffLog;

        if (message.StartsWith("[ScaleKeyRandom]", StringComparison.Ordinal)
            || message.StartsWith("[ScaleLevel]", StringComparison.Ordinal))
            return DebugLogCategory.ChildLevel;

        if (message.StartsWith("[MusicPage]", StringComparison.Ordinal)
            || message.StartsWith("[Start]", StringComparison.Ordinal)
            || message.StartsWith("[Audio]", StringComparison.Ordinal)
            || message.StartsWith("[Restart]", StringComparison.Ordinal)
            || message.StartsWith("[Play]", StringComparison.Ordinal)
            || message.StartsWith("[AutoStart]", StringComparison.Ordinal)
            || message.StartsWith("[RepeatSame]", StringComparison.Ordinal)
            || message.StartsWith("[FirstSound]", StringComparison.Ordinal)
            || message.StartsWith("[OnAppearing]", StringComparison.Ordinal)
            || message.StartsWith("[OnNavigatedTo]", StringComparison.Ordinal)
            || message.StartsWith("[Stats]", StringComparison.Ordinal)
            || message.StartsWith("[NoteAttempts]", StringComparison.Ordinal)
            || message.StartsWith("[PlayDisplayedAsync]", StringComparison.Ordinal)
            || message.StartsWith("[ApplyTunerHeight]", StringComparison.Ordinal)
            || message.StartsWith("[Session]", StringComparison.Ordinal)
            || message.StartsWith("[DEBUG]", StringComparison.Ordinal))
            return DebugLogCategory.MusicPageFlow;

        if (message.StartsWith("[Autoplay]", StringComparison.Ordinal))
            return DebugLogCategory.Autoplay;

        if (message.StartsWith("[ResetOptionsTest]", StringComparison.Ordinal))
            return DebugLogCategory.ResetOptions;

        if (message.StartsWith("[SmuFLFont]", StringComparison.Ordinal)
            || message.StartsWith("[SmuFLRestRaster]", StringComparison.Ordinal))
            return DebugLogCategory.SmuFL;

        return DebugLogCategory.General;
    }

    private static string PreferenceKey(DebugLogCategory category)
        => PreferencePrefix + category;

    private static bool GetPreference(string key, bool defaultValue)
    {
        if (TestStore != null)
            return TestStore.TryGetValue(key, out var value) ? value : defaultValue;

        try
        {
            return Preferences.Default.Get(key, defaultValue);
        }
        catch
        {
            // Unit tests and early startup may run without a MAUI preferences host.
            return defaultValue;
        }
    }

    private static void SetPreference(string key, bool value)
    {
        if (TestStore != null)
        {
            TestStore[key] = value;
            return;
        }

        try
        {
            Preferences.Default.Set(key, value);
        }
        catch
        {
            // best-effort when preferences are unavailable
        }
    }

    internal static void RemovePreference(string key)
    {
        if (TestStore != null)
        {
            TestStore.Remove(key);
            return;
        }

        try
        {
            Preferences.Default.Remove(key);
        }
        catch
        {
            // best-effort when preferences are unavailable
        }
    }

    private static void SyncLegacyFlags(DebugLogCategory category, bool enabled)
    {
        if (category == DebugLogCategory.Timing)
            TimingDiagnostics.EnableTimingDiagnostics = enabled;
        if (category == DebugLogCategory.LayoutTestTune && !enabled)
            LayoutDebug.LayoutTestTune.SetEnabled(false);
    }
}
