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
