using musicmate.Services;

namespace musicmate.Tests;

public class PlayModePickerOptionsTests
{
    [Fact]
    public void ScalePickerOptions_ContainsOnlyNamedScales()
    {
        var options = NoteSessionService.ScalePickerOptions;

        Assert.DoesNotContain(NoteSessionService.ScaleSelectionByLevel, options);
        Assert.DoesNotContain(NoteSessionService.ScaleSelectionRandom, options);
        Assert.DoesNotContain(PlayModePickerOptions.FixedTune, options);
        Assert.DoesNotContain(PlayModePickerOptions.Tuner, options);
        Assert.Contains("Major", options);
        Assert.Contains("Natural Minor", options);
        Assert.Equal(NoteSessionService.AvailableScales.Length, options.Length);
    }

    [Fact]
    public void OtherOptions_ContainsExpectedItemsInOrder()
    {
        Assert.Equal(
            new[] { "By Level", "Fixed Tune", "Random", "Tuner" },
            PlayModePickerOptions.OtherOptions);
    }

    [Fact]
    public void IsNamedScaleOption_RejectsByLevelAndRandom()
    {
        Assert.False(NoteSessionService.IsNamedScaleOption(NoteSessionService.ScaleSelectionByLevel));
        Assert.False(NoteSessionService.IsNamedScaleOption(NoteSessionService.ScaleSelectionRandom));
        Assert.True(NoteSessionService.IsNamedScaleOption("Major"));
    }

    [Fact]
    public void UsesOtherPicker_ForByLevelAndExplicitRandom()
    {
        Assert.True(PlayModePickerOptions.UsesOtherPicker(
            layoutTestTuneEnabled: false,
            tune: "Selected Scale",
            isRandomMode: false,
            scaleSelectionMode: ScaleSelectionMode.ByLevel));

        Assert.True(PlayModePickerOptions.UsesOtherPicker(
            layoutTestTuneEnabled: false,
            tune: "Selected Scale",
            isRandomMode: true,
            scaleSelectionMode: ScaleSelectionMode.ByLevel));

        Assert.False(PlayModePickerOptions.UsesOtherPicker(
            layoutTestTuneEnabled: false,
            tune: "Selected Scale",
            isRandomMode: false,
            scaleSelectionMode: ScaleSelectionMode.Named));
    }

    [Fact]
    public void UsesOtherPicker_ForTunerAndFixedTuneLayout()
    {
        Assert.True(PlayModePickerOptions.UsesOtherPicker(
            layoutTestTuneEnabled: true,
            tune: "Selected Scale",
            isRandomMode: false,
            scaleSelectionMode: ScaleSelectionMode.Named));

        Assert.True(PlayModePickerOptions.UsesOtherPicker(
            layoutTestTuneEnabled: false,
            tune: PlayModePickerOptions.Tuner,
            isRandomMode: false,
            scaleSelectionMode: ScaleSelectionMode.Named));
    }

    [Fact]
    public void ResolveOtherSelection_ReturnsCorrectLabels()
    {
        Assert.Equal(
            NoteSessionService.ScaleSelectionByLevel,
            PlayModePickerOptions.ResolveOtherSelection(
                layoutTestTuneEnabled: false,
                tune: "Selected Scale",
                isRandomMode: false));

        Assert.Equal(
            PlayModePickerOptions.RandomMelodic,
            PlayModePickerOptions.ResolveOtherSelection(
                layoutTestTuneEnabled: false,
                tune: "Selected Scale",
                isRandomMode: true));

        Assert.Equal(
            PlayModePickerOptions.Tuner,
            PlayModePickerOptions.ResolveOtherSelection(
                layoutTestTuneEnabled: false,
                tune: PlayModePickerOptions.Tuner,
                isRandomMode: false));

        Assert.Equal(
            PlayModePickerOptions.FixedTune,
            PlayModePickerOptions.ResolveOtherSelection(
                layoutTestTuneEnabled: true,
                tune: "Selected Scale",
                isRandomMode: false));
    }

    [Fact]
    public void UsesOtherPicker_ForCompositionAssignedTunesAndArpeggios()
    {
        // By Level composition may assign Practice Tune internally; picker stays on Other.
        Assert.True(PlayModePickerOptions.UsesOtherPicker(
            layoutTestTuneEnabled: false,
            tune: "Practice Tune",
            isRandomMode: false,
            scaleSelectionMode: ScaleSelectionMode.ByLevel,
            selectedTunePreference: "Selected Scale"));

        Assert.True(PlayModePickerOptions.UsesOtherPicker(
            layoutTestTuneEnabled: false,
            tune: "Arpeggio",
            isRandomMode: false,
            scaleSelectionMode: ScaleSelectionMode.ByLevel,
            selectedTunePreference: "Selected Scale"));

        // User explicitly picked from Tunes/Arpeggios pickers — show those pickers instead.
        Assert.False(PlayModePickerOptions.UsesOtherPicker(
            layoutTestTuneEnabled: false,
            tune: "Practice Tune",
            isRandomMode: false,
            scaleSelectionMode: ScaleSelectionMode.ByLevel,
            selectedTunePreference: "Mary Had a Little Lamb"));

        Assert.False(PlayModePickerOptions.UsesOtherPicker(
            layoutTestTuneEnabled: false,
            tune: "Arpeggio",
            isRandomMode: false,
            scaleSelectionMode: ScaleSelectionMode.ByLevel,
            selectedTunePreference: "C major arpeggio"));
    }

    [Fact]
    public void ByLevel_RepeatedGeneration_PreservesOtherPickerWhenNotUserExplicit()
    {
        const string byLevelPref = "Selected Scale";
        for (int i = 0; i < 5; i++)
        {
            Assert.False(PracticeCompositionSelector.IsUserExplicitPlayMode(
                "Practice Tune",
                ScaleSelectionMode.ByLevel,
                isRandomMode: false,
                selectedTunePreference: byLevelPref));

            Assert.True(PlayModePickerOptions.UsesOtherPicker(
                layoutTestTuneEnabled: false,
                tune: "Practice Tune",
                isRandomMode: false,
                scaleSelectionMode: ScaleSelectionMode.ByLevel,
                selectedTunePreference: byLevelPref));
        }
    }

    [Fact]
    public void IsOtherOption_RecognizesOtherPickerEntries()
    {
        foreach (var option in PlayModePickerOptions.OtherOptions)
            Assert.True(PlayModePickerOptions.IsOtherOption(option));

        Assert.False(PlayModePickerOptions.IsOtherOption("Major"));
    }
}
