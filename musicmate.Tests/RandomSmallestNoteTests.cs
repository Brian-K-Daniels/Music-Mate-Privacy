using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Random generation must honor Settings → Music Smallest Note (especially under Fixed),
/// and Eighth must appear in the allowed set with nonzero probability across seeds.
/// </summary>
[Collection("SessionPreferences")]
public class RandomSmallestNoteTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public RandomSmallestNoteTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Fact]
    public void Random_SmallestQuarter_ProducesNoEighthNotes()
    {
        var session = CreateFixedRandomSession(
            smallest: "Quarter", rhythmMode: "Mixed", level: 40);
        Assert.Equal("Quarter", session.SmallestRhythmNote);

        var gen = BuildGeneratorLikeMusicPage(session, seed: 7);
        Assert.Equal(NoteDuration.Quarter, gen.SmallestDuration);
        Assert.False(gen.GetDurationWeights().ContainsKey(NoteDuration.Eighth));

        for (int seed = 0; seed < 60; seed++)
        {
            gen.RandomSeed = seed;
            var notes = MusicSequenceGenerator.Flatten(gen.GenerateSequence());
            Assert.DoesNotContain(notes, n => n.Duration == NoteDuration.Eighth);
            Assert.DoesNotContain(notes, n => n.Duration == NoteDuration.Sixteenth);
        }
    }

    [Fact]
    public void Random_SmallestEighth_IncludesEighthInAllowedDurationSet()
    {
        // Reproduces the reported bug path: Fixed + Simple + variety unset + Eighth.
        var session = CreateFixedRandomSession(
            smallest: "Eighth", rhythmMode: "Simple", level: 5);
        session.RhythmVarietyPercent = -1;

        Assert.Equal("Eighth", session.SmallestRhythmNote);
        Assert.Equal("Simple", session.RhythmMode);

        int resolvedVariety = RhythmSettingsResolver.ResolveVarietyPercent(session);
        Assert.Equal(RhythmSettingsResolver.SubQuarterVarietyFloor, resolvedVariety);

        var gen = BuildGeneratorLikeMusicPage(session, seed: 3);
        Assert.Equal(NoteDuration.Eighth, gen.SmallestDuration);
        Assert.True(
            gen.GetDurationWeights().TryGetValue(NoteDuration.Eighth, out int eighthW) && eighthW > 0,
            "Eighth must have nonzero weight when Smallest Note = Eighth");
        Assert.False(gen.GetDurationWeights().ContainsKey(NoteDuration.Sixteenth));
    }

    [Fact]
    public void Random_SmallestEighth_ProducesEighthNotesAcrossSeededSample()
    {
        var session = CreateFixedRandomSession(
            smallest: "Eighth", rhythmMode: "Simple", level: 8);
        session.RhythmVarietyPercent = -1;

        int eighthCount = 0;
        int total = 0;
        for (int seed = 0; seed < 80; seed++)
        {
            var gen = BuildGeneratorLikeMusicPage(session, seed);
            foreach (var n in MusicSequenceGenerator.Flatten(gen.GenerateSequence()))
            {
                total++;
                if (n.Duration == NoteDuration.Eighth)
                    eighthCount++;
            }
        }

        Assert.True(total > 200, $"Expected many notes, got {total}");
        Assert.True(
            eighthCount > 0,
            $"Expected some eighth notes across seeded Random tunes; got 0/{total}");
    }

    [Fact]
    public void Fixed_PreventsLevelFromAlteringSmallestNote()
    {
        var session = CreateFixedRandomSession(
            smallest: "Eighth", rhythmMode: "Mixed", level: 10);
        Assert.Equal("Eighth", session.SmallestRhythmNote);
        Assert.False(session.AllowsLevelToChangeMusicSettings);

        // Level 50 would set Sixteenth under MayBeChanged.
        Assert.Equal("Sixteenth", ChildLevelProgression.SmallestNoteForLevel(50));
        DifficultyLevelMapper.ApplyLevelSettings(50, session, preserveUserPracticeSettings: false);
        Assert.Equal("Eighth", session.SmallestRhythmNote);

        var gen = BuildGeneratorLikeMusicPage(session);
        Assert.Equal(NoteDuration.Eighth, gen.SmallestDuration);
    }

    [Fact]
    public void MayBeChangedByLevel_StillAppliesLevelSmallestNote()
    {
        var session = new NoteSessionService
        {
            ChildLevel = 10,
            IsRandomMode = true,
            ScaleSelectionMode = ScaleSelectionMode.Random,
            MusicSettingsLevelPolicy =
                NoteSessionService.MusicSettingsLevelPolicyMayBeChangedByLevel,
            SmallestRhythmNote = "Quarter",
            RhythmMode = "Simple",
            RhythmVarietyPercent = -1,
        };
        Assert.True(session.AllowsLevelToChangeMusicSettings);

        DifficultyLevelMapper.ApplyLevelSettings(40, session, preserveUserPracticeSettings: false);
        Assert.Equal(ChildLevelProgression.SmallestNoteForLevel(40), session.SmallestRhythmNote);
        Assert.Equal(
            ChildLevelProgression.RhythmVarietyPercentForLevel(40),
            session.RhythmVarietyPercent);

        var gen = BuildGeneratorLikeMusicPage(session);
        Assert.Equal(
            RhythmSettingsResolver.ParseSmallestDuration(session.SmallestRhythmNote),
            gen.SmallestDuration);
    }

    [Fact]
    public void ResolveVarietyPercent_QuarterSmallest_KeepsSimpleAtZero()
    {
        int variety = RhythmSettingsResolver.ResolveVarietyPercent(
            rhythmVarietyPercent: -1,
            rhythmMode: "Simple",
            smallestRhythmNote: "Quarter");
        Assert.Equal(0, variety);
    }

    private static NoteSessionService CreateFixedRandomSession(
        string smallest, string rhythmMode, int level)
    {
        var session = new NoteSessionService
        {
            ChildLevel = level,
            IsRandomMode = true,
            ScaleSelectionMode = ScaleSelectionMode.Random,
            SelectedScale = "Major",
            Key = "C",
            MusicSettingsLevelPolicy = NoteSessionService.MusicSettingsLevelPolicyFixed,
            SmallestRhythmNote = smallest,
            RhythmMode = rhythmMode,
            RhythmVarietyPercent = -1,
            AccidentalPercent = 0,
        };
        session.RestoreRepeatSameGenerationContext(
            "C", "Major", "Major", ScaleSelectionMode.Random,
            isRandomMode: true, tune: "Selected Scale");
        session.MusicSettingsLevelPolicy = NoteSessionService.MusicSettingsLevelPolicyFixed;
        session.SmallestRhythmNote = smallest;
        session.RhythmMode = rhythmMode;
        session.RhythmVarietyPercent = -1;
        return session;
    }

    /// <summary>Mirrors MusicPage.BuildSequenceGenerator rhythm wiring for Random.</summary>
    private static MusicSequenceGenerator BuildGeneratorLikeMusicPage(
        NoteSessionService session, int? seed = null)
    {
        bool randomStyle = session.IsRandomMode;
        return new MusicSequenceGenerator
        {
            Key = session.Key,
            Scale = session.GenerationScale,
            LowestNote = "C4",
            HighestNote = "C5",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 8,
            RhythmVarietyPercent = RhythmSettingsResolver.ResolveVarietyPercent(session),
            SmallestDuration = RhythmSettingsResolver.ParseSmallestDuration(session.SmallestRhythmNote),
            UseScaleOrder = !randomStyle,
            AccidentalPercent = 0,
            MaxMelodicIntervalSemitones = 5,
            SyncopationLevel = SyncopationLevel.None,
            RestChancePercent = 10,
            UseMotifPhrases = true,
            ChildLevel = session.ChildLevel,
            RandomSeed = seed ?? 11,
        };
    }
}
