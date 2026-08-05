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
            new[] { "Assortment by Level", "Random", "Tuner" },
            PlayModePickerOptions.OtherOptions);

        Assert.DoesNotContain(PlayModePickerOptions.HalfThroughSixteenthNotes, PlayModePickerOptions.OtherOptions);
        Assert.DoesNotContain("Fixed Tune", PlayModePickerOptions.OtherOptions);
    }

    [Fact]
    public void ApplyOtherSelection_Tuner_OverridesByLevelAndSyncsPreference()
    {
        var session = new NoteSessionService
        {
            Tune = "Selected Scale",
            IsRandomMode = true,
            RepeatSameTune = true,
        };
        Assert.True(session.TryApplyScalePickerSelection(
            NoteSessionService.ScaleSelectionByLevel, out _));

        string? persisted = null;
        PlayModePickerOptions.ApplyOtherSelection(
            session, PlayModePickerOptions.Tuner, value => persisted = value);

        Assert.Equal(PlayModePickerOptions.Tuner, session.Tune);
        Assert.False(session.IsRandomMode);
        Assert.False(session.RepeatSameTune);
        Assert.Equal(PlayModePickerOptions.Tuner, persisted);

        var (category, selection) = PlayModePickerOptions.ResolveDisplayedPicker(
            session, layoutTestTuneEnabled: false, selectedTunePreference: "Selected Scale");
        Assert.Equal(PlayModePickerCategory.Other, category);
        Assert.Equal(PlayModePickerOptions.Tuner, selection);

        Assert.True(PracticeCompositionSelector.IsUserExplicitPlayMode(
            session.Tune ?? string.Empty,
            session.ScaleSelectionMode,
            session.IsRandomMode,
            persisted));
    }

    [Fact]
    public void ResolveDisplayedPicker_SessionTuner_WinsOverStaleByLevelPreference()
    {
        var session = new NoteSessionService { Tune = PlayModePickerOptions.Tuner };

        var (category, selection) = PlayModePickerOptions.ResolveDisplayedPicker(
            session,
            layoutTestTuneEnabled: false,
            selectedTunePreference: "Selected Scale");

        Assert.Equal(PlayModePickerCategory.Other, category);
        Assert.Equal(PlayModePickerOptions.Tuner, selection);
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
                isRandomMode: false,
                scaleSelectionMode: ScaleSelectionMode.ByLevel,
                selectedTunePreference: "Selected Scale"));

        Assert.Equal(
            PlayModePickerOptions.RandomMelodic,
            PlayModePickerOptions.ResolveOtherSelection(
                layoutTestTuneEnabled: false,
                tune: "Selected Scale",
                isRandomMode: true,
                scaleSelectionMode: ScaleSelectionMode.ByLevel,
                selectedTunePreference: PlayModePickerOptions.RandomMelodic));

        Assert.Equal(
            PlayModePickerOptions.Tuner,
            PlayModePickerOptions.ResolveOtherSelection(
                layoutTestTuneEnabled: false,
                tune: PlayModePickerOptions.Tuner,
                isRandomMode: false));
    }

    [Fact]
    public void ResolveDisplayedPicker_ByLevelComposition_StaysOnOtherByLevel()
    {
        var (category, selection) = PlayModePickerOptions.ResolveDisplayedPicker(
            layoutTestTuneEnabled: false,
            selectedTunePreference: "Selected Scale",
            scaleSelectionMode: ScaleSelectionMode.ByLevel,
            selectedScale: "Major");

        Assert.Equal(PlayModePickerCategory.Other, category);
        Assert.Equal(NoteSessionService.ScaleSelectionByLevel, selection);
    }

    [Fact]
    public void ResolveDisplayedPicker_ExplicitScale_IgnoresCompositionTune()
    {
        var (category, selection) = PlayModePickerOptions.ResolveDisplayedPicker(
            layoutTestTuneEnabled: false,
            selectedTunePreference: "Major",
            scaleSelectionMode: ScaleSelectionMode.Named,
            selectedScale: "Natural Minor");

        Assert.Equal(PlayModePickerCategory.Scales, category);
        Assert.Equal("Major", selection);
    }

    [Fact]
    public void ResolveDisplayedPicker_NamedScalePreference_WinsOverByLevelMode()
    {
        var (category, selection) = PlayModePickerOptions.ResolveDisplayedPicker(
            layoutTestTuneEnabled: false,
            selectedTunePreference: "Major",
            scaleSelectionMode: ScaleSelectionMode.ByLevel,
            selectedScale: "Natural Minor");

        Assert.Equal(PlayModePickerCategory.Scales, category);
        Assert.Equal("Major", selection);
        Assert.False(PlayModePickerOptions.IsUserSelectedArpeggioTitle("Major"));
        Assert.False(PlayModePickerOptions.UsesOtherPicker(
            layoutTestTuneEnabled: false,
            tune: "Selected Scale",
            isRandomMode: false,
            scaleSelectionMode: ScaleSelectionMode.ByLevel,
            selectedTunePreference: "Major"));
    }

    [Fact]
    public void ResolveDisplayedPicker_ExplicitTune_IgnoresSessionChanges()
    {
        var (category, selection) = PlayModePickerOptions.ResolveDisplayedPicker(
            layoutTestTuneEnabled: false,
            selectedTunePreference: "Mary Had a Little Lamb",
            scaleSelectionMode: ScaleSelectionMode.ByLevel,
            selectedScale: "Major");

        Assert.Equal(PlayModePickerCategory.Tunes, category);
        Assert.Equal("Mary Had a Little Lamb", selection);
    }

    [Fact]
    public void ResolveDisplayedPicker_ExplicitRandom_StaysOnOtherRandom()
    {
        var (category, selection) = PlayModePickerOptions.ResolveDisplayedPicker(
            layoutTestTuneEnabled: false,
            selectedTunePreference: PlayModePickerOptions.RandomMelodic,
            scaleSelectionMode: ScaleSelectionMode.ByLevel,
            selectedScale: "Major");

        Assert.Equal(PlayModePickerCategory.Other, category);
        Assert.Equal(PlayModePickerOptions.RandomMelodic, selection);
    }

    [Fact]
    public void ResolveOtherSelection_ByLevelCompositionRandom_KeepsByLevelPicker()
    {
        // Composition under Assortment by Level sets IsRandomMode but leaves SelectedTune as "Selected Scale".
        Assert.Equal(
            NoteSessionService.ScaleSelectionByLevel,
            PlayModePickerOptions.ResolveOtherSelection(
                layoutTestTuneEnabled: false,
                tune: "Selected Scale",
                isRandomMode: true,
                scaleSelectionMode: ScaleSelectionMode.ByLevel,
                selectedTunePreference: "Selected Scale"));
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

    [Fact]
    public void ResolvePlaySelectionKey_TreatsDifferentItemsInSamePickerAsNew()
    {
        var major = PlayModePickerOptions.ResolvePlaySelectionKey(
            PlayModePickerCategory.Scales, "Major");
        var dorian = PlayModePickerOptions.ResolvePlaySelectionKey(
            PlayModePickerCategory.Scales, "Dorian");

        Assert.False(PlayModePickerOptions.IsNewPlaySelection(major, major));
        Assert.True(PlayModePickerOptions.IsNewPlaySelection(major, dorian));

        var mary = PlayModePickerOptions.ResolvePlaySelectionKey(
            PlayModePickerCategory.Tunes, "Mary Had a Little Lamb");
        var ode = PlayModePickerOptions.ResolvePlaySelectionKey(
            PlayModePickerCategory.Tunes, "Ode to Joy");
        Assert.True(PlayModePickerOptions.IsNewPlaySelection(mary, ode));

        var byLevel = PlayModePickerOptions.ResolvePlaySelectionKey(
            PlayModePickerCategory.Other, NoteSessionService.ScaleSelectionByLevel);
        var tuner = PlayModePickerOptions.ResolvePlaySelectionKey(
            PlayModePickerCategory.Other, PlayModePickerOptions.Tuner);
        Assert.True(PlayModePickerOptions.IsNewPlaySelection(byLevel, tuner));
    }

    [Fact]
    public void TryApplyScalePickerSelection_NamedScale_SticksEvenWhenBelowUnlockLevel()
    {
        var session = new NoteSessionService
        {
            ChildLevel = 40, // Dorian unlocks at 76+
            Tune = "Selected Scale",
        };

        Assert.False(ChildLevelProgression.IsScaleAllowedAtLevel(40, "Dorian"));
        Assert.True(session.TryApplyScalePickerSelection("Dorian", out var rejection));
        Assert.Null(rejection);
        Assert.Equal(ScaleSelectionMode.Named, session.ScaleSelectionMode);
        Assert.Equal("Dorian", session.SelectedScale);
        Assert.Equal("Dorian", session.EffectiveScale);
    }

    [Theory]
    [InlineData(PlayModePickerCategory.Other, "Assortment by Level", "ByLvl")]
    [InlineData(PlayModePickerCategory.Other, "Random", "Rnd")]
    [InlineData(PlayModePickerCategory.Other, "Tuner", "Tuner")]
    [InlineData(PlayModePickerCategory.Tunes, "Half through Sixteenth Notes", "Rhythm")]
    [InlineData(PlayModePickerCategory.Scales, "Natural Minor", "Nat Min")]
    [InlineData(PlayModePickerCategory.Scales, "Major", "Major")]
    [InlineData(PlayModePickerCategory.Tunes, "Mary Had a Little Lamb", "Mary Had a Li…")]
    [InlineData(PlayModePickerCategory.Arpeggios, "C major triad", "C maj tri")]
    public void AbbreviateDisplayedSelection_UsesShortLabels(
        PlayModePickerCategory category,
        string selection,
        string expected)
    {
        Assert.Equal(expected, PlayModePickerOptions.AbbreviateDisplayedSelection(category, selection));
    }
}
