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
                Assert.True(
                    measure.IsFull,
                    $"Seed {seed}: measure not full ({measure.BeatsUsed}/{measure.BeatsAvailable} beats)");
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
