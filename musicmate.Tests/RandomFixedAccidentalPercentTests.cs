using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Random generation must honor Settings → Music Fixed Accidental % (not Level defaults),
/// and Accidental % 0 must not introduce discretionary chromatics beyond the scale.
/// </summary>
[Collection("SessionPreferences")]
public class RandomFixedAccidentalPercentTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public RandomFixedAccidentalPercentTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Fact]
    public void Fixed_Random_AccidentalPercentZero_PassesZeroIntoGeneratorAndEmitsNoChromatics()
    {
        var session = CreateFixedRandomSession(accidentalPercent: 0, level: 80, key: "Bb", scale: "Major");
        Assert.Equal(0, session.AccidentalPercent);
        Assert.False(session.AllowsLevelToChangeMusicSettings);

        // Level apply must not raise Accidental %.
        DifficultyLevelMapper.ApplyLevelSettings(90, session, preserveUserPracticeSettings: false);
        Assert.Equal(0, session.AccidentalPercent);

        var gen = BuildGeneratorLikeMusicPage(session);
        Assert.Equal(0, gen.AccidentalPercent);

        var scalePcs = NoteSessionService.GetScalePitchClasses(session.Key, session.GenerationScale);
        var pitched = MusicSequenceGenerator.Flatten(gen.GenerateSequence()).Where(n => !n.IsRest).ToList();
        Assert.NotEmpty(pitched);
        Assert.All(pitched, n =>
        {
            int pc = ((n.MidiNumber % 12) + 12) % 12;
            Assert.Contains(pc, scalePcs);
        });
    }

    [Fact]
    public void Fixed_Random_AccidentalPercent25_UsesConfiguredRateNotLevelRate()
    {
        var session = CreateFixedRandomSession(accidentalPercent: 25, level: 20, key: "C", scale: "Major");
        // Level 20 AccidentalPercentForLevel is 0 — Fixed must keep 25.
        Assert.Equal(0, ChildLevelProgression.AccidentalPercentForLevel(20));
        Assert.Equal(25, session.AccidentalPercent);

        DifficultyLevelMapper.ApplyLevelSettings(20, session, preserveUserPracticeSettings: false);
        Assert.Equal(25, session.AccidentalPercent);

        var gen = BuildGeneratorLikeMusicPage(session);
        Assert.Equal(25, gen.AccidentalPercent);

        var scalePcs = NoteSessionService.GetScalePitchClasses("C", "Major");
        int chromatic = 0, total = 0;
        for (int seed = 0; seed < 80; seed++)
        {
            gen.RandomSeed = seed;
            foreach (var n in MusicSequenceGenerator.Flatten(gen.GenerateSequence()).Where(x => !x.IsRest))
            {
                total++;
                int pc = ((n.MidiNumber % 12) + 12) % 12;
                if (!scalePcs.Contains(pc))
                    chromatic++;
            }
        }

        Assert.True(total > 100);
        double rate = (double)chromatic / total;
        Assert.True(rate > 0.05, $"Expected chromatics at AccPct=25, got rate={rate:F3}");
        Assert.True(rate < 0.55, $"AccPct=25 rate too high: {rate:F3}");
    }

    [Fact]
    public void Fixed_ChangingLevel_DoesNotChangeAccidentalPercent()
    {
        var session = CreateFixedRandomSession(accidentalPercent: 0, level: 10, key: "G", scale: "Major");
        for (int level = 10; level <= 100; level += 15)
        {
            DifficultyLevelMapper.ApplyLevelSettings(level, session, preserveUserPracticeSettings: false);
            Assert.Equal(0, session.AccidentalPercent);
            Assert.Equal(
                NoteSessionService.MusicSettingsLevelPolicyFixed,
                session.MusicSettingsLevelPolicy);
        }
    }

    [Fact]
    public void MayBeChangedByLevel_StillAppliesLevelAccidentalPercent()
    {
        var session = CreateFixedRandomSession(accidentalPercent: 0, level: 10, key: "C", scale: "Major");
        session.MusicSettingsLevelPolicy =
            NoteSessionService.MusicSettingsLevelPolicyMayBeChangedByLevel;

        DifficultyLevelMapper.ApplyLevelSettings(80, session, preserveUserPracticeSettings: false);
        Assert.Equal(ChildLevelProgression.AccidentalPercentForLevel(80), session.AccidentalPercent);
        Assert.True(session.AccidentalPercent > 0);
    }

    [Fact]
    public void Fixed_AccidentalPercentZero_BbMajor_NoBodyAccidentalsAgainstKeySignature()
    {
        var session = CreateFixedRandomSession(accidentalPercent: 0, level: 50, key: "Bb", scale: "Major");
        session.ScaleSelectionMode = ScaleSelectionMode.Named;
        session.SelectedScale = "Major";
        session.RestoreRepeatSameGenerationContext(
            "Bb", "Major", "Major", ScaleSelectionMode.Named, isRandomMode: true, tune: "Selected Scale");
        session.MusicSettingsLevelPolicy = NoteSessionService.MusicSettingsLevelPolicyFixed;
        session.AccidentalPercent = 0;

        var scalePcs = NoteSessionService.GetScalePitchClasses("Bb", "Major");
        for (int seed = 0; seed < 60; seed++)
        {
            var gen = BuildGeneratorLikeMusicPage(session, seed);
            Assert.Equal(0, gen.AccidentalPercent);
            foreach (var n in MusicSequenceGenerator.Flatten(gen.GenerateSequence()).Where(x => !x.IsRest))
            {
                int pc = ((n.MidiNumber % 12) + 12) % 12;
                Assert.Contains(pc, scalePcs);
                Assert.NotEqual(Accidental.Natural, n.Accidental);
            }
        }
    }

    [Fact]
    public void AccidentalPercentZero_FiltersOutScaleTonesNeedingBodyAccidentals()
    {
        // Even if Scale is Lydian, Accidental % 0 must stay inside the Bb key signature
        // (no E♮ discretionary accidentals).
        var gen = new MusicSequenceGenerator
        {
            Key = "Bb",
            Scale = "Lydian",
            LowestNote = "Bb3",
            HighestNote = "Bb5",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 8,
            RhythmVarietyPercent = 40,
            SmallestDuration = NoteDuration.Eighth,
            UseScaleOrder = false,
            AccidentalPercent = 0,
            UseMotifPhrases = false,
            RestChancePercent = 0,
            RandomSeed = 3,
        };
        var keySigPcs = NoteSessionService.GetKeySignaturePitchClasses("Bb", "Lydian");
        var pitched = MusicSequenceGenerator.Flatten(gen.GenerateSequence()).Where(n => !n.IsRest).ToList();
        Assert.NotEmpty(pitched);
        Assert.All(pitched, n =>
        {
            int pc = ((n.MidiNumber % 12) + 12) % 12;
            Assert.Contains(pc, keySigPcs);
            Assert.NotEqual(Accidental.Natural, n.Accidental);
        });
    }

    [Fact]
    public void AccidentalPercent25_AllowsPitchesOutsideKeySignature()
    {
        var keySigPcs = NoteSessionService.GetKeySignaturePitchClasses("C", "Major");
        int outside = 0;
        for (int seed = 0; seed < 80; seed++)
        {
            var gen = new MusicSequenceGenerator
            {
                Key = "C",
                Scale = "Major",
                LowestNote = "C4",
                HighestNote = "C6",
                TimeSignature = TimeSignature.FourFour,
                MeasureCount = 6,
                RhythmVarietyPercent = 30,
                SmallestDuration = NoteDuration.Eighth,
                UseScaleOrder = false,
                AccidentalPercent = 25,
                UseMotifPhrases = false,
                RestChancePercent = 0,
                RandomSeed = seed,
            };
            foreach (var n in MusicSequenceGenerator.Flatten(gen.GenerateSequence()).Where(x => !x.IsRest))
            {
                int pc = ((n.MidiNumber % 12) + 12) % 12;
                if (!keySigPcs.Contains(pc))
                    outside++;
            }
        }

        Assert.True(outside > 0, "AccPct=25 should introduce pitches outside the key signature");
    }

    [Fact]
    public void BuildSequenceGeneratorPath_FixedRandom_DoesNotInjectLevelAccidentalPercent()
    {
        int levelAcc = ChildLevelProgression.AccidentalPercentForLevel(85);
        Assert.True(levelAcc > 0);

        var session = CreateFixedRandomSession(accidentalPercent: 0, level: 85, key: "Bb", scale: "Major");
        Assert.Equal(0, session.AccidentalPercent);

        // Simulate MusicPage regenerate hooks that call level-derived settings.
        session.EnsureRandomModeGenerationSettings();
        DifficultyLevelMapper.ApplyLevelDerivedSettings(85, session);
        Assert.Equal(0, session.AccidentalPercent);

        var gen = BuildGeneratorLikeMusicPage(session);
        Assert.Equal(0, gen.AccidentalPercent);
        Assert.NotEqual(levelAcc, gen.AccidentalPercent);
    }

    [Fact]
    public void KeySignatureFlats_AreNotConfusedWithDiscretionaryAccidentals()
    {
        // Bb/Eb from the signature should spell as Accidental.None (covered by key sig).
        var note = new GeneratedNote
        {
            MidiNumber = 70,
            Letter = 'B',
            Octave = 4,
            SpelledName = "Bb4",
            Accidental = Accidental.None,
            Duration = NoteDuration.Quarter,
        };
        var (midi, name) = NoteSessionService.ResolveTargetPitch(note, "Bb", "Major");
        Assert.Equal(70, midi);
        Assert.Equal("Bb4", name);
        Assert.Equal(Accidental.None, note.Accidental);

        var (acc, spelled) = NoteSessionService.ResolveAccidentalAndSpelling(
            "B4", 70, 'B', 4, "Bb", "Major");
        Assert.Equal(Accidental.None, acc);
        Assert.Equal("Bb4", spelled);
    }

    private static NoteSessionService CreateFixedRandomSession(
        int accidentalPercent, int level, string key, string scale)
    {
        var session = new NoteSessionService
        {
            ChildLevel = level,
            Key = key,
            Tune = "Selected Scale",
            IsRandomMode = true,
            ScaleSelectionMode = ScaleSelectionMode.Random,
            SelectedScale = scale,
            MusicSettingsLevelPolicy = NoteSessionService.MusicSettingsLevelPolicyFixed,
            AccidentalPercent = accidentalPercent,
        };
        session.RestoreRepeatSameGenerationContext(
            key, scale, scale, ScaleSelectionMode.Random, isRandomMode: true, tune: "Selected Scale");
        session.MusicSettingsLevelPolicy = NoteSessionService.MusicSettingsLevelPolicyFixed;
        session.AccidentalPercent = accidentalPercent;
        return session;
    }

    /// <summary>Mirrors MusicPage.BuildSequenceGenerator AccidentalPercent wiring for Random.</summary>
    private static MusicSequenceGenerator BuildGeneratorLikeMusicPage(
        NoteSessionService session, int? seed = null)
    {
        bool randomStyle = session.IsRandomMode;
        return new MusicSequenceGenerator
        {
            Key = session.Key,
            Scale = session.GenerationScale,
            LowestNote = "E3",
            HighestNote = "C6",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 6,
            RhythmVarietyPercent = 50,
            SmallestDuration = NoteDuration.Eighth,
            UseScaleOrder = !randomStyle,
            AccidentalPercent = randomStyle ? session.AccidentalPercent : 0,
            MaxMelodicIntervalSemitones = 7,
            SyncopationLevel = SyncopationLevel.Simple,
            RestChancePercent = 15,
            UseMotifPhrases = true,
            ChildLevel = session.ChildLevel,
            RandomSeed = seed ?? 11,
        };
    }
}
