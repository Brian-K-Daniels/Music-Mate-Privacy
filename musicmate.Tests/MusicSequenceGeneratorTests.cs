using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

public class MusicSequenceGeneratorTests
{
    [Theory]
    [InlineData(32)]
    [InlineData(40)]
    public void MotifPhraseMeasures_FillEveryBar(int level)
    {
        for (int seed = 0; seed < 100; seed++)
        {
            var gen = CreateLevelGenerator(level, seed);
            var measures = gen.GenerateSequence();
            Assert.NotEmpty(measures);

            foreach (var measure in measures)
            {
                Assert.InRange(
                    measure.BeatsUsed,
                    measure.BeatsAvailable - 1e-6,
                    measure.BeatsAvailable + 1e-6);
                Assert.True(
                    measure.BeatsUsed <= measure.BeatsAvailable + 1e-9,
                    $"Seed {seed}: measure overflow ({measure.BeatsUsed}/{measure.BeatsAvailable} beats)");
            }
        }
    }

    /// <summary>
    /// Regression: motif B used to turn an eighth rest into a quarter note without a capacity
    /// check, producing 4.5 beats in 4/4. Stress Random motif generation with rests + eighths.
    /// </summary>
    [Theory]
    [InlineData(21)]
    [InlineData(32)]
    [InlineData(45)]
    public void RandomMotifMeasures_NeverOverflowMeter_AcrossManySeeds(int level)
    {
        const double eps = 1e-6;
        for (int seed = 0; seed < 400; seed++)
        {
            var gen = CreateLevelGenerator(level, seed);
            gen.MeasureCount = 8; // ensures motif B measures are emitted
            gen.SmallestDuration = NoteDuration.Eighth;
            gen.RestChancePercent = Math.Max(gen.RestChancePercent, 25);
            gen.RhythmVarietyPercent = Math.Max(gen.RhythmVarietyPercent, 40);
            gen.UseMotifPhrases = true;

            var measures = gen.GenerateSequence();
            Assert.NotEmpty(measures);

            double expected = gen.TimeSignature.TotalBeats;
            var flat = MusicSequenceGenerator.Flatten(measures);

            // No lost/duplicated events: flatten count matches measure note counts.
            Assert.Equal(measures.Sum(m => m.GeneratedNotes.Count), flat.Count);

            for (int mi = 0; mi < measures.Count; mi++)
            {
                var measure = measures[mi];
                Assert.True(
                    measure.BeatsUsed <= expected + eps,
                    $"Level {level} seed {seed} measure {mi}: overflow {measure.BeatsUsed}/{expected}");
                Assert.InRange(measure.BeatsUsed, expected - eps, expected + eps);

                // Beat positions stay inside this measure's span and preserve order.
                double start = mi * expected;
                double end = start + expected;
                double prev = start - 1e-6;
                foreach (var n in measure.GeneratedNotes)
                {
                    double bp = n.BeatPosition ?? -1;
                    Assert.InRange(bp, start - eps, end - eps);
                    Assert.True(bp >= prev - eps, $"Level {level} seed {seed} measure {mi}: beat order broken");
                    prev = bp;
                }
            }

            // Flattened beat spans cover each bar exactly once (no cross-bar overlap from overflow).
            for (int mi = 0; mi < measures.Count; mi++)
            {
                double start = mi * expected;
                double end = start + expected;
                var inMeasure = flat.Where(n =>
                {
                    double bp = n.BeatPosition ?? 0;
                    return bp >= start - eps && bp < end - eps;
                }).ToList();
                double sum = inMeasure.Sum(n => n.BeatDuration);
                Assert.InRange(sum, expected - eps, expected + eps);
            }
        }
    }

    [Fact]
    public void FlattenedBeatPositions_AlignToMeterGrid()
    {
        var gen = CreateLevelGenerator(32, 7919);
        gen.MeasureCount = 4;
        var flat = MusicSequenceGenerator.Flatten(gen.GenerateSequence());
        const double measureBeats = 4.0;

        for (int mi = 0; mi < 4; mi++)
        {
            double start = mi * measureBeats;
            double end = start + measureBeats;
            var inMeasure = flat.Where(n =>
            {
                double bp = n.BeatPosition ?? 0;
                return bp >= start - 1e-6 && bp < end - 1e-6;
            }).ToList();

            double sum = inMeasure.Sum(n => n.BeatDuration);
            Assert.InRange(sum, measureBeats - 1e-6, measureBeats + 1e-6);
        }
    }

    [Fact]
    public void MasteryExclusion_KeepsSmallUnmasteredPool_WithoutReintroducingMastered()
    {
        int g4 = NoteSessionService.NoteNameToMidi("G4");
        int a4 = NoteSessionService.NoteNameToMidi("A4");
        var excludeAllButGa = new HashSet<int>();
        for (int midi = NoteSessionService.NoteNameToMidi("C4"); midi <= NoteSessionService.NoteNameToMidi("B4"); midi++)
        {
            if (midi != g4 && midi != a4)
                excludeAllButGa.Add(midi);
        }

        var gen = CreateRandomGenerator(4242, excludeAllButGa, maxIntervalSemitones: 2);
        var pitches = FlattenPitches(gen);

        Assert.Equal(
            MasteredNoteOmission.FallbackKind.UsedSmallUnmasteredPool,
            gen.LastMasteryFallback);
        Assert.All(pitches, m => Assert.True(m == g4 || m == a4));
        Assert.DoesNotContain(NoteSessionService.NoteNameToMidi("C4"), pitches);
    }

    [Fact]
    public void EarlyLevelIntervalCap_ProducesVariedSequencesAcrossSeeds()
    {
        var signatures = new HashSet<string>();
        for (int seed = 0; seed < 24; seed++)
        {
            var gen = CreateRandomGenerator(seed, new HashSet<int>(), maxIntervalSemitones: 2);
            signatures.Add(string.Join(',', FlattenPitches(gen)));
        }

        Assert.True(signatures.Count > 1, "Identical output across seeds indicates a locked oscillation.");
    }

    [Fact]
    public void EarlyLevelMotif_DoesNotRepeatIdenticalContourAcrossPhrases()
    {
        var gen = CreateRandomGenerator(9001, new HashSet<int>(), maxIntervalSemitones: 2);
        gen.MeasureCount = 8;
        var flat = MusicSequenceGenerator.Flatten(gen.GenerateSequence())
            .Where(n => !n.IsRest)
            .Select(n => n.MidiNumber)
            .ToList();

        Assert.True(flat.Count >= 8);
        var firstFour = flat.Take(4).ToList();
        var nextFour = flat.Skip(4).Take(4).ToList();
        Assert.NotEqual(firstFour, nextFour);
    }

    [Fact]
    public void MotifContourReuse_ReRollsAccidentalsPerPhraseInsteadOfCopyingChromaticPattern()
    {
        // Phrase A′ and A-return reuse rhythm + diatonic contour, but each slot should
        // get its own accidental roll — not inherit A's chromatic spellings measure-for-measure.
        int identicalPatterns = 0;
        const int trials = 40;

        for (int seed = 0; seed < trials; seed++)
        {
            var gen = CreateRandomGenerator(seed, new HashSet<int>(), maxIntervalSemitones: 4);
            gen.Key = "G";
            gen.Scale = "Major";
            gen.LowestNote = "C4";
            gen.HighestNote = "E5";
            gen.AccidentalPercent = 50;
            gen.MeasureCount = 8;

            var measures = gen.GenerateSequence();
            var phraseA = ChromaticPatternForPhrase(measures, phraseIndex: 0);
            var phraseAprime = ChromaticPatternForPhrase(measures, phraseIndex: 1);

            if (phraseA.Count > 0
                && phraseA.Count == phraseAprime.Count
                && phraseA.SequenceEqual(phraseAprime))
            {
                identicalPatterns++;
            }
        }

        Assert.True(
            identicalPatterns < trials - 2,
            $"Expected independent per-phrase accidental rolls, but {identicalPatterns}/{trials} A/A′ chromatic patterns matched exactly.");
    }

    [Fact]
    public void MotifContourReuse_WithZeroAccidentals_UsesDiatonicOutlineOnly()
    {
        var gen = CreateRandomGenerator(1234, new HashSet<int>(), maxIntervalSemitones: 4);
        gen.Key = "G";
        gen.Scale = "Major";
        gen.AccidentalPercent = 0;
        gen.MeasureCount = 8;

        var pitches = FlattenPitches(gen);
        var scalePcs = NoteSessionService.GetScalePitchClasses("G", "Major");

        Assert.All(pitches, midi =>
            Assert.Contains(((midi % 12) + 12) % 12, scalePcs));
    }

    private static List<bool> ChromaticPatternForPhrase(List<Measure> measures, int phraseIndex)
    {
        int startMeasure = phraseIndex * 2;
        var scalePcs = NoteSessionService.GetScalePitchClasses("G", "Major");
        return MusicSequenceGenerator.Flatten(measures)
            .Where(n => !n.IsRest
                        && n.MeasureIndex >= startMeasure
                        && n.MeasureIndex < startMeasure + 2)
            .Select(n => !scalePcs.Contains(((n.MidiNumber % 12) + 12) % 12))
            .ToList();
    }

    private static List<int> FlattenPitches(MusicSequenceGenerator gen)
        => MusicSequenceGenerator.Flatten(gen.GenerateSequence())
            .Where(n => !n.IsRest)
            .Select(n => n.MidiNumber)
            .ToList();

    private static MusicSequenceGenerator CreateRandomGenerator(
        int seed, HashSet<int> excluded, int maxIntervalSemitones = 0)
        => new()
        {
            Key = "C",
            Scale = "Major",
            LowestNote = "C4",
            HighestNote = "B4",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 8,
            RhythmVarietyPercent = 0,
            SmallestDuration = NoteDuration.Quarter,
            UseScaleOrder = false,
            UseMotifPhrases = true,
            AccidentalPercent = 0,
            RestChancePercent = 0,
            ExcludedMidiNumbers = excluded,
            MaxMelodicIntervalSemitones = maxIntervalSemitones,
            RandomSeed = seed,
        };

    private static MusicSequenceGenerator CreateLevelGenerator(int level, int seed)
    {
        int variety = ChildLevelProgression.RhythmVarietyPercentForLevel(level);
        return new MusicSequenceGenerator
        {
            Key = "C",
            Scale = "Harmonic Minor",
            LowestNote = "G3",
            HighestNote = "B4",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 4,
            RhythmVarietyPercent = variety,
            SmallestDuration = NoteDuration.Eighth,
            RestChancePercent = ChildLevelProgression.RestChancePercentForLevel(level),
            SyncopationLevel = SyncopationLevelHelper.Parse(
                ChildLevelProgression.SyncopationForLevel(level)),
            UseScaleOrder = false,
            UseMotifPhrases = true,
            RandomSeed = seed ^ (level * 7919),
        };
    }
}
