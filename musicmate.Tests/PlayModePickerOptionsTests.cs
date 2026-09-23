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
            new[] { "Assortment by Level", "Random" },
            PlayModePickerOptions.OtherOptions);

        Assert.DoesNotContain(PlayModePickerOptions.HalfThroughSixteenthNotes, PlayModePickerOptions.OtherOptions);
        Assert.DoesNotContain("Fixed Tune", PlayModePickerOptions.OtherOptions);
        Assert.DoesNotContain(PlayModePickerOptions.Tuner, PlayModePickerOptions.OtherOptions);
    }

    [Fact]
    public void ApplyOtherSelection_Tuner_DoesNotReplaceSavedWhatToPlayChoice()
    {
        var session = new NoteSessionService
        {
            Tune = "Selected Scale",
            IsRandomMode = true,
            RepeatSameTune = true,
        };
        Assert.True(session.TryApplyScalePickerSelection(
            NoteSessionService.ScaleSelectionByLevel, out _));

        string? persisted = "untouched";
        PlayModePickerOptions.ApplyOtherSelection(
            session, PlayModePickerOptions.Tuner, value => persisted = value);

        Assert.Equal(PlayModePickerOptions.Tuner, session.Tune);
        Assert.False(session.IsRandomMode);
        Assert.False(session.RepeatSameTune);
        Assert.Equal("untouched", persisted);

        var (category, selection) = PlayModePickerOptions.ResolveDisplayedPicker(
            session, layoutTestTuneEnabled: false, selectedTunePreference: "Selected Scale");
        Assert.Equal(PlayModePickerCategory.Other, category);
        Assert.Equal(NoteSessionService.ScaleSelectionByLevel, selection);

        Assert.True(PracticeCompositionSelector.IsUserExplicitPlayMode(
            session.Tune ?? string.Empty,
            session.ScaleSelectionMode,
            session.IsRandomMode,
            persisted));
    }

    [Fact]
    public void ResolveDisplayedPicker_SessionTuner_KeepsSavedWhatToPlayChoice()
    {
        var session = new NoteSessionService { Tune = PlayModePickerOptions.Tuner };

        var (category, selection) = PlayModePickerOptions.ResolveDisplayedPicker(
            session,
            layoutTestTuneEnabled: false,
            selectedTunePreference: "Selected Scale");

        Assert.Equal(PlayModePickerCategory.Other, category);
        Assert.Equal(NoteSessionService.ScaleSelectionByLevel, selection);
    }

    [Fact]
    public void TunerEntry_FirstMusicPageLoad_KeepsTheMarkOnlyOnce()
    {
        PlayModePickerOptions.MarkTunerSelectedBeforeMusicPageLoad();
        Assert.True(PlayModePickerOptions.ConsumeTunerSelectedBeforeMusicPageLoad());
        Assert.False(PlayModePickerOptions.ConsumeTunerSelectedBeforeMusicPageLoad());
    }

    [Fact]
    public void MusicPageConstruction_KeepsTunerAfterTheLoadMarkWasAlreadyConsumed()
    {
        PlayModePickerOptions.MarkTunerSelectedBeforeMusicPageLoad();
        Assert.True(PlayModePickerOptions.ConsumeTunerSelectedBeforeMusicPageLoad());

        Assert.True(PlayModePickerOptions.ShouldPreserveTunerOnMusicPageConstruction(
            PlayModePickerOptions.Tuner));
        Assert.False(PlayModePickerOptions.ShouldPreserveTunerOnMusicPageConstruction(
            "Selected Scale"));
        Assert.False(PlayModePickerOptions.ShouldPreserveTunerOnMusicPageConstruction(null));
    }

    [Fact]
    public void ApplyPersistedSelection_LegacyTunerPreference_OpensAssortmentByLevel()
    {
        string? pref = PlayModePickerOptions.Tuner;
        var session = new NoteSessionService { Tune = PlayModePickerOptions.Tuner };

        PlayModePickerOptions.ApplyPersistedSelection(
            session,
            () => pref,
            value => pref = value);

        Assert.Equal(NoteSessionService.ScaleSelectionByLevel, pref);
        Assert.NotEqual(PlayModePickerOptions.Tuner, session.Tune);
    }

    [Fact]
    public void EnsureAssortmentByLevelForSightTraining_FromTuner_PersistsAssortmentWithoutChangingLevels()
    {
        var session = new NoteSessionService
        {
            Tune = PlayModePickerOptions.Tuner,
            ChildLevel = 17,
            RepeatSameTune = false,
            IsRandomMode = false,
        };

        string persisted = PlayModePickerOptions.Tuner;
        bool changed = PlayModePickerOptions.EnsureAssortmentByLevelForSightTraining(
            session,
            value => persisted = value,
            () => persisted);

        Assert.True(changed);
        Assert.Equal(NoteSessionService.ScaleSelectionByLevel, persisted);
        Assert.NotEqual(PlayModePickerOptions.Tuner, session.Tune);
        Assert.Equal(ScaleSelectionMode.ByLevel, session.ScaleSelectionMode);
        Assert.False(session.IsRandomMode);
        Assert.Equal(17, session.ChildLevel);

        var (category, selection) = PlayModePickerOptions.ResolveDisplayedPicker(
            session, layoutTestTuneEnabled: false, selectedTunePreference: persisted);
        Assert.Equal(PlayModePickerCategory.Other, category);
        Assert.Equal(NoteSessionService.ScaleSelectionByLevel, selection);
    }

    [Fact]
    public void EnsureAssortmentByLevelForSightTraining_FromRandomOther_SwitchesToAssortment()
    {
        var session = new NoteSessionService
        {
            Tune = "Selected Scale",
            IsRandomMode = true,
            ChildLevel = 8,
        };
        string persisted = PlayModePickerOptions.RandomMelodic;
        bool changed = PlayModePickerOptions.EnsureAssortmentByLevelForSightTraining(
            session,
            value => persisted = value,
            () => persisted);

        Assert.True(changed);
        Assert.False(session.IsRandomMode);
        Assert.Equal(NoteSessionService.ScaleSelectionByLevel, persisted);
        Assert.Equal(8, session.ChildLevel);
        Assert.Equal(ScaleSelectionMode.ByLevel, session.ScaleSelectionMode);
    }

    [Fact]
    public void EnsureAssortmentByLevelForSightTraining_WhenAlreadyAssortment_IsNoOp()
    {
        var session = new NoteSessionService
        {
            Tune = "Selected Scale",
            IsRandomMode = false,
            RepeatSameTune = true,
            ChildLevel = 12,
        };
        Assert.True(session.TryApplyScalePickerSelection(
            NoteSessionService.ScaleSelectionByLevel, out _));

        string persisted = NoteSessionService.ScaleSelectionByLevel;
        bool changed = PlayModePickerOptions.EnsureAssortmentByLevelForSightTraining(
            session,
            value => persisted = value,
            () => persisted);

        Assert.False(changed);
        Assert.Equal(NoteSessionService.ScaleSelectionByLevel, persisted);
        Assert.True(session.RepeatSameTune);
        Assert.Equal(12, session.ChildLevel);
        Assert.Equal(ScaleSelectionMode.ByLevel, session.ScaleSelectionMode);
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

        Assert.False(PlayModePickerOptions.UsesOtherPicker(
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
            NoteSessionService.ScaleSelectionByLevel,
            PlayModePickerOptions.ResolveOtherSelection(
                layoutTestTuneEnabled: false,
                tune: PlayModePickerOptions.Tuner,
                isRandomMode: false,
                scaleSelectionMode: ScaleSelectionMode.ByLevel));
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
            selectedTunePreference: "Major triad"));
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
    public void ApplyPersistedSelection_RestoresRandomMelodic()
    {
        string? pref = PlayModePickerOptions.RandomMelodic;
        var session = new NoteSessionService
        {
            Tune = "Selected Scale",
            IsRandomMode = false,
            ScaleSelectionMode = ScaleSelectionMode.ByLevel,
            ChildLevel = 20,
        };

        PlayModePickerOptions.ApplyPersistedSelection(session, () => pref, value => pref = value);

        Assert.True(session.IsRandomMode);
        Assert.Equal(ScaleSelectionMode.Random, session.ScaleSelectionMode);
        Assert.Equal("Selected Scale", session.Tune);
        Assert.Equal(PlayModePickerOptions.RandomMelodic, pref);
        Assert.StartsWith("Random —", session.EffectiveScaleDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain("Assortment by Level", session.EffectiveScaleDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyOtherSelection_Random_SetsScaleSelectionModeAndLabel()
    {
        var session = new NoteSessionService
        {
            ChildLevel = 24,
            Tune = "Selected Scale",
            IsRandomMode = false,
            Key = "C",
        };
        Assert.True(session.TryApplyScalePickerSelection(
            NoteSessionService.ScaleSelectionByLevel, out _));
        Assert.Contains("Assortment by Level", session.EffectiveScaleDisplay, StringComparison.Ordinal);

        string? persisted = null;
        PlayModePickerOptions.ApplyOtherSelection(
            session, PlayModePickerOptions.RandomMelodic, value => persisted = value);

        Assert.True(session.IsRandomMode);
        Assert.Equal(ScaleSelectionMode.Random, session.ScaleSelectionMode);
        Assert.Equal(PlayModePickerOptions.RandomMelodic, persisted);
        Assert.StartsWith("Random —", session.EffectiveScaleDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain("Assortment by Level", session.EffectiveScaleDisplay, StringComparison.Ordinal);

        var (category, selection) = PlayModePickerOptions.ResolveDisplayedPicker(
            session, layoutTestTuneEnabled: false, selectedTunePreference: persisted);
        Assert.Equal(PlayModePickerCategory.Other, category);
        Assert.Equal(PlayModePickerOptions.RandomMelodic, selection);
    }

    [Fact]
    public void ApplyOtherSelection_ByLevel_ShowsAssortmentLabelNotRandom()
    {
        var session = new NoteSessionService
        {
            ChildLevel = 24,
            Tune = "Selected Scale",
            IsRandomMode = true,
            Key = "G",
        };
        string? persisted = PlayModePickerOptions.RandomMelodic;
        PlayModePickerOptions.ApplyOtherSelection(
            session, PlayModePickerOptions.RandomMelodic, value => persisted = value);

        PlayModePickerOptions.ApplyOtherSelection(
            session, NoteSessionService.ScaleSelectionByLevel, value => persisted = value);

        Assert.False(session.IsRandomMode);
        Assert.Equal(ScaleSelectionMode.ByLevel, session.ScaleSelectionMode);
        Assert.Equal(NoteSessionService.ScaleSelectionByLevel, persisted);
        Assert.Contains("Assortment by Level", session.EffectiveScaleDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain("Random —", session.EffectiveScaleDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyOtherSelection_SwitchingRandomAndByLevel_UpdatesLabelEachTime()
    {
        var session = new NoteSessionService { ChildLevel = 30, Key = "D" };
        string? pref = null;

        PlayModePickerOptions.ApplyOtherSelection(
            session, PlayModePickerOptions.RandomMelodic, value => pref = value);
        Assert.StartsWith("Random —", session.EffectiveScaleDisplay, StringComparison.Ordinal);

        PlayModePickerOptions.ApplyOtherSelection(
            session, NoteSessionService.ScaleSelectionByLevel, value => pref = value);
        Assert.Contains("Assortment by Level", session.EffectiveScaleDisplay, StringComparison.Ordinal);

        PlayModePickerOptions.ApplyOtherSelection(
            session, PlayModePickerOptions.RandomMelodic, value => pref = value);
        Assert.StartsWith("Random —", session.EffectiveScaleDisplay, StringComparison.Ordinal);
        Assert.Equal(PlayModePickerOptions.RandomMelodic, pref);
        Assert.Equal(ScaleSelectionMode.Random, session.ScaleSelectionMode);
    }

    [Fact]
    public void RandomMode_GMajorPick_DoesNotRelabelAsAssortmentByLevel()
    {
        var session = new NoteSessionService
        {
            ChildLevel = 40,
            Key = "C",
        };
        string? pref = null;
        PlayModePickerOptions.ApplyOtherSelection(
            session, PlayModePickerOptions.RandomMelodic, value => pref = value);

        session.RestoreRepeatSameGenerationContext(
            key: "G",
            selectedScale: "Major",
            effectiveScale: "Major",
            scaleMode: ScaleSelectionMode.Random,
            isRandomMode: true,
            tune: "Selected Scale");

        Assert.Equal(ScaleSelectionMode.Random, session.ScaleSelectionMode);
        Assert.True(session.IsRandomMode);
        Assert.Equal("Random — G Major", session.EffectiveScaleDisplay);
        Assert.Equal(
            "Random — G Major",
            PlayModePickerOptions.ResolveExerciseStatusLabel(
                layoutTestTuneEnabled: false,
                tune: session.Tune ?? string.Empty,
                scaleSelectionMode: session.ScaleSelectionMode,
                isRandomMode: session.IsRandomMode,
                practiceTuneTitle: null,
                arpeggioDisplay: null,
                key: session.Key,
                effectiveScale: session.EffectiveScale,
                selectedScale: session.SelectedScale,
                selectedTunePreference: pref));
    }

    [Fact]
    public void ApplyPersistedSelection_RoundTrip_RandomThenByLevel_SurvivesRestart()
    {
        string? pref = null;
        var session = new NoteSessionService { ChildLevel = 18, Key = "F" };

        PlayModePickerOptions.ApplyOtherSelection(
            session, PlayModePickerOptions.RandomMelodic, value => pref = value);
        Assert.Equal(ScaleSelectionMode.Random, session.ScaleSelectionMode);

        // Simulate app restart: new session with prefs restored only via ApplyPersistedSelection.
        var restarted = new NoteSessionService
        {
            ChildLevel = 18,
            ScaleSelectionMode = ScaleSelectionMode.ByLevel,
            IsRandomMode = false,
            Key = "C",
        };
        PlayModePickerOptions.ApplyPersistedSelection(restarted, () => pref, value => pref = value);
        Assert.True(restarted.IsRandomMode);
        Assert.Equal(ScaleSelectionMode.Random, restarted.ScaleSelectionMode);
        Assert.StartsWith("Random —", restarted.EffectiveScaleDisplay, StringComparison.Ordinal);

        PlayModePickerOptions.ApplyOtherSelection(
            restarted, NoteSessionService.ScaleSelectionByLevel, value => pref = value);
        var restartedAgain = new NoteSessionService
        {
            ChildLevel = 18,
            ScaleSelectionMode = ScaleSelectionMode.Random,
            IsRandomMode = true,
        };
        PlayModePickerOptions.ApplyPersistedSelection(restartedAgain, () => pref, value => pref = value);
        Assert.False(restartedAgain.IsRandomMode);
        Assert.Equal(ScaleSelectionMode.ByLevel, restartedAgain.ScaleSelectionMode);
        Assert.Contains("Assortment by Level", restartedAgain.EffectiveScaleDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public void AssortmentComposition_RandomExercise_KeepsAssortmentModeLabel()
    {
        var session = new NoteSessionService
        {
            ChildLevel = 20,
            Key = "G",
            Tune = "Selected Scale",
            IsRandomMode = false,
        };
        Assert.True(session.TryApplyScalePickerSelection(
            NoteSessionService.ScaleSelectionByLevel, out _));

        // Composition Random flips IsRandomMode but must not change ScaleSelectionMode.
        session.IsRandomMode = true;

        Assert.True(session.IsRandomMode);
        Assert.Equal(ScaleSelectionMode.ByLevel, session.ScaleSelectionMode);
        Assert.Contains("Assortment by Level", session.EffectiveScaleDisplay, StringComparison.Ordinal);
        Assert.DoesNotContain("Random —", session.EffectiveScaleDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyPersistedSelection_RestoresAssortmentByLevel_FromLegacySelectedScale()
    {
        string? pref = "Selected Scale";
        var session = new NoteSessionService
        {
            Tune = "Selected Scale",
            IsRandomMode = false,
            ScaleSelectionMode = ScaleSelectionMode.ByLevel,
        };

        PlayModePickerOptions.ApplyPersistedSelection(session, () => pref, value => pref = value);

        Assert.Equal(ScaleSelectionMode.ByLevel, session.ScaleSelectionMode);
        Assert.Equal(NoteSessionService.ScaleSelectionByLevel, pref);
        Assert.Equal(
            (PlayModePickerCategory.Other, NoteSessionService.ScaleSelectionByLevel),
            PlayModePickerOptions.ResolveDisplayedPicker(
                session, layoutTestTuneEnabled: false, selectedTunePreference: pref));
    }

    [Fact]
    public void ApplyPersistedSelection_RestoresNamedScale()
    {
        string? pref = "Dorian";
        var session = new NoteSessionService
        {
            Tune = "Selected Scale",
            IsRandomMode = true,
            ChildLevel = 100,
        };

        PlayModePickerOptions.ApplyPersistedSelection(session, () => pref, value => pref = value);

        Assert.False(session.IsRandomMode);
        Assert.Equal(ScaleSelectionMode.Named, session.ScaleSelectionMode);
        Assert.Equal("Dorian", session.SelectedScale);
        Assert.Equal("Dorian", pref);
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

    [Fact]
    public void ResolveExerciseStatusLabel_ByLevelCompositionTune_NamesTheTuneNotLevelDefaultScale()
    {
        // L62 default scale is Blues; composition assigned Mary Had a Little Lamb.
        Assert.Equal(
            "Mary Had a Little Lamb (Assortment by Level)",
            PlayModePickerOptions.ResolveExerciseStatusLabel(
                layoutTestTuneEnabled: false,
                tune: "Practice Tune",
                scaleSelectionMode: ScaleSelectionMode.ByLevel,
                isRandomMode: false,
                practiceTuneTitle: "Mary Had a Little Lamb",
                arpeggioDisplay: null,
                key: "C",
                effectiveScale: "Blues",
                selectedScale: "Blues",
                selectedTunePreference: NoteSessionService.ScaleSelectionByLevel));
    }

    [Fact]
    public void ResolveExerciseStatusLabel_ByLevelScaleExercise_StillShowsEffectiveScale()
    {
        Assert.Equal(
            "G Blues (Assortment by Level)",
            PlayModePickerOptions.ResolveExerciseStatusLabel(
                layoutTestTuneEnabled: false,
                tune: "Selected Scale",
                scaleSelectionMode: ScaleSelectionMode.ByLevel,
                isRandomMode: false,
                practiceTuneTitle: null,
                arpeggioDisplay: null,
                key: "G",
                effectiveScale: "Blues",
                selectedScale: "Blues",
                selectedTunePreference: NoteSessionService.ScaleSelectionByLevel));
    }

    [Fact]
    public void ResolveExerciseStatusLabel_ByLevelCompositionArpeggio_NamesTheArpeggio()
    {
        Assert.Equal(
            "Major triad (Assortment by Level)",
            PlayModePickerOptions.ResolveExerciseStatusLabel(
                layoutTestTuneEnabled: false,
                tune: "Arpeggio",
                scaleSelectionMode: ScaleSelectionMode.ByLevel,
                isRandomMode: false,
                practiceTuneTitle: null,
                arpeggioDisplay: "Major triad",
                key: "C",
                effectiveScale: "Blues",
                selectedScale: "Blues",
                selectedTunePreference: NoteSessionService.ScaleSelectionByLevel));
    }

    [Fact]
    public void ResolveExerciseStatusLabel_UserPickedTune_OmitsAssortmentSuffix()
    {
        Assert.Equal(
            "Mary Had a Little Lamb",
            PlayModePickerOptions.ResolveExerciseStatusLabel(
                layoutTestTuneEnabled: false,
                tune: "Practice Tune",
                scaleSelectionMode: ScaleSelectionMode.ByLevel,
                isRandomMode: false,
                practiceTuneTitle: "Mary Had a Little Lamb",
                arpeggioDisplay: null,
                key: "C",
                effectiveScale: "Blues",
                selectedScale: "Blues",
                selectedTunePreference: "Mary Had a Little Lamb"));
    }

    [Theory]
    [InlineData(PlayModePickerCategory.Other, "Assortment by Level", "ByLvl")]
    [InlineData(PlayModePickerCategory.Other, "Random", "Rnd")]
    [InlineData(PlayModePickerCategory.Other, "Tuner", "Tuner")]
    [InlineData(PlayModePickerCategory.Tunes, "Half through Sixteenth Notes", "Rhythm")]
    [InlineData(PlayModePickerCategory.Scales, "Natural Minor", "Nat Min")]
    [InlineData(PlayModePickerCategory.Scales, "Major", "Major")]
    [InlineData(PlayModePickerCategory.Tunes, "Mary Had a Little Lamb", "Mary Had a Li…")]
    [InlineData(PlayModePickerCategory.Arpeggios, "Major triad", "Maj tri")]
    [InlineData(PlayModePickerCategory.Arpeggios, "C major triad", "Maj tri")]
    public void AbbreviateDisplayedSelection_UsesShortLabels(
        PlayModePickerCategory category,
        string selection,
        string expected)
    {
        Assert.Equal(expected, PlayModePickerOptions.AbbreviateDisplayedSelection(category, selection));
    }
}
