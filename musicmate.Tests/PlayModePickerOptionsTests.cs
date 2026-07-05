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
        Assert.DoesNotContain(PlayModePickerOptions.HalfThroughSixteenthNotes, options);
        Assert.DoesNotContain(PlayModePickerOptions.Tuner, options);
        Assert.Contains("Major", options);
        Assert.Contains("Natural Minor", options);
        Assert.Equal(NoteSessionService.AvailableScales.Length, options.Length);
    }

    [Fact]
    public void OtherOptions_ContainsExpectedItemsInOrder()
    {
        Assert.Equal(
            new[] { "By Level", "Random", "Tuner" },
            PlayModePickerOptions.OtherOptions);

        Assert.DoesNotContain(PlayModePickerOptions.HalfThroughSixteenthNotes, PlayModePickerOptions.OtherOptions);
        Assert.DoesNotContain("Fixed Tune", PlayModePickerOptions.OtherOptions);
    }

    [Fact]
    public void TunePickerOptions_ContainsRhythmNotesFirstThenLibraryTunes()
    {
        var options = PlayModePickerOptions.BuildTunePickerOptions();

        Assert.Equal(PlayModePickerOptions.HalfThroughSixteenthNotes, options[0]);
        Assert.Contains("Mary Had a Little Lamb", options);
        Assert.Contains("Ode to Joy", options);
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
    public void UsesOtherPicker_ForTunerAndRhythmNotesTune()
    {
        Assert.False(PlayModePickerOptions.UsesOtherPicker(
            layoutTestTuneEnabled: true,
            tune: "Selected Scale",
            isRandomMode: false,
            scaleSelectionMode: ScaleSelectionMode.Named));

        Assert.False(PlayModePickerOptions.UsesOtherPicker(
            layoutTestTuneEnabled: false,
            tune: "Selected Scale",
            isRandomMode: false,
            scaleSelectionMode: ScaleSelectionMode.Named,
            selectedTunePreference: PlayModePickerOptions.HalfThroughSixteenthNotes));

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
    }

    [Fact]
    public void UsesOtherPicker_ForCompositionAssignedTunesAndArpeggios()
    {
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
        Assert.False(PlayModePickerOptions.IsOtherOption(PlayModePickerOptions.HalfThroughSixteenthNotes));
    }

    [Fact]
    public void NormalizeRhythmNoteTunePreference_MigratesLegacyFixedTune()
    {
        Assert.Equal(
            PlayModePickerOptions.HalfThroughSixteenthNotes,
            PlayModePickerOptions.NormalizeRhythmNoteTunePreference("Fixed Tune"));
    }

    [Fact]
    public void IsRhythmNoteTuneSelection_AcceptsLegacyAndNewNames()
    {
        Assert.True(PlayModePickerOptions.IsRhythmNoteTuneSelection(
            PlayModePickerOptions.HalfThroughSixteenthNotes));
        Assert.True(PlayModePickerOptions.IsRhythmNoteTuneSelection("Fixed Tune"));
        Assert.False(PlayModePickerOptions.IsRhythmNoteTuneSelection("Mary Had a Little Lamb"));
    }

    [Fact]
    public void RhythmNoteTuneSelection_IsUserExplicitPlayMode()
    {
        Assert.True(PracticeCompositionSelector.IsUserExplicitPlayMode(
            "Selected Scale",
            ScaleSelectionMode.ByLevel,
            isRandomMode: false,
            selectedTunePreference: PlayModePickerOptions.HalfThroughSixteenthNotes));

        Assert.True(PracticeCompositionSelector.IsUserExplicitPlayMode(
            "Selected Scale",
            ScaleSelectionMode.ByLevel,
            isRandomMode: false,
            selectedTunePreference: "Fixed Tune"));
    }
}
