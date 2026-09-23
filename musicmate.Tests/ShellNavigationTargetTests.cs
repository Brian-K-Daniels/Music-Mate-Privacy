using musicmate.Services;

namespace musicmate.Tests;

public class ShellNavigationTargetTests
{
    [Theory]
    [InlineData("//Music/MusicPage", "//Music")]
    [InlineData("//Music/MusicPage", "//MusicPage")]
    [InlineData("//Music", "//Music/MusicPage")]
    [InlineData("//EarTraining/IntervalEarTrainingPage", "//EarTraining")]
    [InlineData("//IntervalSightTraining/IntervalSightTrainingPage", "//IntervalSightTraining")]
    [InlineData("//SingingTraining/IntervalSingingTrainingPage", "//SingingTraining")]
    [InlineData("//Home/HomePage", "//Home")]
    [InlineData("//About/AboutPage", "//About")]
    [InlineData("//WhatToPlay/WhatToPlayPage", "//WhatToPlay")]
    [InlineData("//Statistics/StatisticsPages", "//Statistics")]
    [InlineData("//Settings/SettingsPage", "//Settings")]
    [InlineData("//SettingsAdvanced/AdvancedPage", "//SettingsAdvanced")]
    [InlineData("//SettingsReset/ResetOptionsPage", "//SettingsReset")]
    public void IsSameDestination_True_ForSameFlyoutPage(string current, string target)
        => Assert.True(ShellNavigationTarget.IsSameDestination(current, target));

    [Theory]
    [InlineData("//Music/MusicPage", "//EarTraining/IntervalEarTrainingPage")]
    [InlineData("//Music/MusicPage", "//TunerEntry/TunerEntryPage")]
    [InlineData("//Settings/SettingsPage", "//SettingsAdvanced/AdvancedPage")]
    [InlineData("//Home/HomePage", "//Music/MusicPage")]
    [InlineData("//IntervalSightTraining/IntervalSightTrainingPage", "//EarTraining")]
    [InlineData("//SingingTraining/IntervalSingingTrainingPage", "//EarTraining")]
    [InlineData("//SingingTraining/IntervalSingingTrainingPage", "//IntervalSightTraining")]
    public void IsSameDestination_False_ForDifferentPages(string current, string target)
        => Assert.False(ShellNavigationTarget.IsSameDestination(current, target));

    [Fact]
    public void CanonicalKey_TunerEntry_IsNotMusic()
    {
        Assert.Equal("Tuner", ShellNavigationTarget.CanonicalKey("//TunerEntry/TunerEntryPage"));
        Assert.Equal("Music", ShellNavigationTarget.CanonicalKey("//Music/MusicPage"));
        Assert.False(ShellNavigationTarget.IsSameDestination(
            "//Music/MusicPage", "//TunerEntry/TunerEntryPage"));
    }

    [Fact]
    public void IsSameDestination_Empty_IsFalse()
    {
        Assert.False(ShellNavigationTarget.IsSameDestination(null, "//Music"));
        Assert.False(ShellNavigationTarget.IsSameDestination("//Music", ""));
    }
}
