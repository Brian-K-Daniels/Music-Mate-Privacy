using musicmate.Services;

namespace musicmate.Tests;

public class PracticeCompositionSelectorTests
{
    [Theory]
    [InlineData("Practice Tune", ScaleSelectionMode.ByLevel, false, "Selected Scale", false)]
    [InlineData("Selected Scale", ScaleSelectionMode.ByLevel, false, "Selected Scale", false)]
    [InlineData("Selected Scale", ScaleSelectionMode.ByLevel, true, "Random", true)]
    [InlineData("Selected Scale", ScaleSelectionMode.ByLevel, true, "Selected Scale", false)]
    [InlineData("Practice Tune", ScaleSelectionMode.ByLevel, false, "Mary Had a Little Lamb", true)]
    public void IsUserExplicitPlayMode_DistinguishesCompositionFromUserPicks(
        string tune,
        ScaleSelectionMode scaleMode,
        bool isRandomMode,
        string selectedTunePref,
        bool expectedExplicit)
    {
        Assert.Equal(
            expectedExplicit,
            PracticeCompositionSelector.IsUserExplicitPlayMode(
                tune, scaleMode, isRandomMode, selectedTunePref));
    }

    [Fact]
    public void CompositionAssignedPracticeTune_IsNotUserExplicit()
    {
        Assert.False(PracticeCompositionSelector.IsUserExplicitPlayMode(
            "Practice Tune",
            ScaleSelectionMode.ByLevel,
            isRandomMode: false,
            selectedTunePreference: "Selected Scale"));
    }

    [Fact]
    public void TunerMode_IsUserExplicit_EvenWhenByLevelModeRemains()
    {
        Assert.True(PracticeCompositionSelector.IsUserExplicitPlayMode(
            PlayModePickerOptions.Tuner,
            ScaleSelectionMode.ByLevel,
            isRandomMode: false,
            selectedTunePreference: "Selected Scale"));

        Assert.True(PracticeCompositionSelector.IsUserExplicitPlayMode(
            "Selected Scale",
            ScaleSelectionMode.ByLevel,
            isRandomMode: false,
            selectedTunePreference: PlayModePickerOptions.Tuner));
    }

    [Fact]
    public void CompositionAssignedPracticeTune_DoesNotImplyTunesPickerSelection()
    {
        // Simulates state after composition picks a tune: session.Tune is Practice Tune
        // but SelectedTune preference must remain the Assortment by Level marker, not the tune title.
        Assert.True(PlayModePickerOptions.UsesOtherPicker(
            layoutTestTuneEnabled: false,
            tune: "Practice Tune",
            isRandomMode: false,
            scaleSelectionMode: ScaleSelectionMode.ByLevel,
            selectedTunePreference: "Selected Scale"));

        Assert.False(PlayModePickerOptions.IsUserSelectedPracticeTuneTitle("Selected Scale"));
    }
}
