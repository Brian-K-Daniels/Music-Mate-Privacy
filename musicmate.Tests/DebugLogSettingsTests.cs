using musicmate.Diagnostics;

namespace musicmate.Tests;

public class DebugLogSettingsTests : IDisposable
{
    private readonly Dictionary<string, bool> _store = new();

    public DebugLogSettingsTests()
    {
        DebugLogSettings.TestStore = _store;
        _store.Clear();
        foreach (var category in DebugLogSettings.AllCategories)
            DebugLogSettings.RemovePreference(DebugPreferenceKey(category));
    }

    public void Dispose()
    {
        DebugLogSettings.TestStore = null;
    }

    [Fact]
    public void IsEnabled_DefaultsToTrue_WhenNoPreferenceSaved()
    {
        foreach (var category in DebugLogSettings.AllCategories)
            Assert.True(DebugLogSettings.IsEnabled(category));
    }

    [Fact]
    public void SetEnabled_PersistsAndIsReadable()
    {
        DebugLogSettings.SetEnabled(DebugLogCategory.LevelUp, false);
        Assert.False(DebugLogSettings.IsEnabled(DebugLogCategory.LevelUp));

        DebugLogSettings.SetEnabled(DebugLogCategory.LevelUp, true);
        Assert.True(DebugLogSettings.IsEnabled(DebugLogCategory.LevelUp));
    }

    [Fact]
    public void ResolveFromMessage_MapsKnownPrefixes()
    {
        Assert.Equal(DebugLogCategory.LevelUp, DebugLogSettings.ResolveFromMessage("[LevelUpDebug] test"));
        Assert.Equal(DebugLogCategory.Picker, DebugLogSettings.ResolveFromMessage("[PickerDBG] test"));
        Assert.Equal(DebugLogCategory.Timing, DebugLogSettings.ResolveFromMessage("[TimingWrong] test"));
        Assert.Equal(DebugLogCategory.SessionStartProfiler, DebugLogSettings.ResolveFromMessage("[SessionStart] test"));
        Assert.Equal(DebugLogCategory.Statistics, DebugLogSettings.ResolveFromMessage("[StatisticsCacheService] test"));
        Assert.Equal(DebugLogCategory.ChildLevel, DebugLogSettings.ResolveFromMessage("[ChildPractice] test"));
        Assert.Equal(DebugLogCategory.MusicPageFlow, DebugLogSettings.ResolveFromMessage("[Session] error"));
        Assert.Equal(DebugLogCategory.StaffAndSequence, DebugLogSettings.ResolveFromMessage("[Staff Draw] error"));
        Assert.Equal(DebugLogCategory.StaffLog, DebugLogSettings.ResolveFromMessage("[StaffLayout] i=0"));
        Assert.Equal(DebugLogCategory.StaffLog, DebugLogSettings.ResolveFromMessage("[Staff Validation] Measure 1"));
    }

    [Fact]
    public void SetEnabled_Timing_SyncsLegacyTimingDiagnosticsFlag()
    {
        DebugLogSettings.SetEnabled(DebugLogCategory.Timing, false);
        Assert.False(TimingDiagnostics.EnableTimingDiagnostics);

        DebugLogSettings.SetEnabled(DebugLogCategory.Timing, true);
        Assert.True(TimingDiagnostics.EnableTimingDiagnostics);
    }

    private static string DebugPreferenceKey(DebugLogCategory category)
        => "debug.log." + category;
}
