using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Every completed generated measure must equal its meter. Level 43 is the first
/// syncopation band with sixteenths, which is where overfull 4/4 bars showed up.
/// </summary>
public class LevelMeasureCapacityTests
{
    private const int SixteenthsPerQuarter = 4;

    [Fact]
    public void Level43_FourFour_OneThousandMeasures_AreExact()
    {
        var settings = SettingsFor(43);
        var ts = TimeSignature.FourFour;
        int expected = ToTicks(ts.TotalBeats);
        int measuresSeen = 0;
        int seed = 43;

        while (measuresSeen < 1000)
        {
            var measures = Generate(settings, ts, seed, measureCount: 8);
            AssertMeasuresExact(measures, ts, expected, seed, allowShortFinal: false);
            measuresSeen += measures.Count;
            seed++;
        }

        Assert.True(measuresSeen >= 1000);
    }

    [Fact]
    public void Level100_FourFour_OneThousandMeasures_AreExact()
    {
        var settings = SettingsFor(100);
        var ts = TimeSignature.FourFour;
        int expected = ToTicks(ts.TotalBeats);
        int measuresSeen = 0;
        int seed = 100;

        while (measuresSeen < 1000)
        {
            var measures = Generate(settings, ts, seed, measureCount: 8);
            AssertMeasuresExact(measures, ts, expected, seed, allowShortFinal: false);
            measuresSeen += measures.Count;
            seed++;
        }

        Assert.True(measuresSeen >= 1000);
    }

    [Fact]
    public void Levels1Through100_EveryMeter_MeasuresMatchTimeSignature()
    {
        foreach (string meter in TimeSignature.CommonDisplayOptions)
        {
            var ts = TimeSignature.FromDisplayString(meter);
            int expected = ToTicks(ts.TotalBeats);
            for (int level = 1; level <= 100; level++)
            {
                var settings = SettingsFor(level);
                var measures = Generate(settings, ts, seed: level * 1000 + meter.GetHashCode(), measureCount: 8);
                AssertMeasuresExact(measures, ts, expected, level, allowShortFinal: false);
            }
        }
    }

    private static void AssertMeasuresExact(
        List<Measure> measures,
        TimeSignature ts,
        int expectedTicks,
        int seed,
        bool allowShortFinal)
    {
        Assert.NotEmpty(measures);
        var flat = BarLineTieNormalizer.Normalize(
            MusicSequenceGenerator.Flatten(measures), ts.TotalBeats);
        var sums = BarLineTieNormalizer.MeasureRenderedDurations(flat, ts.TotalBeats);

        for (int i = 0; i < measures.Count; i++)
        {
            int ticks = measures[i].GeneratedNotes.Sum(n => ToTicks(n.Duration.ToBeatValue()));
            Assert.Equal(expectedTicks, ticks);
        }

        for (int i = 0; i < sums.Count; i++)
        {
            bool last = i == sums.Count - 1;
            int ticks = ToTicks(sums[i]);
            if (last && allowShortFinal && ticks < expectedTicks)
                continue;
            Assert.Equal(expectedTicks, ticks);
        }

        _ = seed;
    }

    private static List<Measure> Generate(
        (NoteDuration Smallest, int Variety, int Rests, SyncopationLevel Sync, string Lo, string Hi, int Interval) settings,
        TimeSignature ts,
        int seed,
        int measureCount)
    {
        var gen = new MusicSequenceGenerator
        {
            Key = "C",
            Scale = "Major",
            LowestNote = settings.Lo,
            HighestNote = settings.Hi,
            TimeSignature = ts,
            MeasureCount = measureCount,
            RhythmVarietyPercent = settings.Variety,
            SmallestDuration = settings.Smallest,
            RestChancePercent = settings.Rests,
            SyncopationLevel = settings.Sync,
            UseMotifPhrases = settings.Sync != SyncopationLevel.Full,
            UseScaleOrder = false,
            AccidentalPercent = 0,
            MaxMelodicIntervalSemitones = settings.Interval,
            RandomSeed = seed,
        };
        return gen.GenerateSequence();
    }

    private static (NoteDuration Smallest, int Variety, int Rests, SyncopationLevel Sync, string Lo, string Hi, int Interval) SettingsFor(int level)
    {
        var smallest = ChildLevelProgression.SmallestNoteForLevel(level) switch
        {
            "Eighth" => NoteDuration.Eighth,
            "Sixteenth" => NoteDuration.Sixteenth,
            _ => NoteDuration.Quarter,
        };
        var range = ChildLevelProgression.NoteRangeForLevel(level);
        return (
            smallest,
            ChildLevelProgression.RhythmVarietyPercentForLevel(level),
            ChildLevelProgression.RestChancePercentForLevel(level),
            SyncopationLevelHelper.Parse(ChildLevelProgression.SyncopationForLevel(level)),
            range.Lo,
            range.Hi,
            ChildLevelProgression.MaxIntervalForLevel(level));
    }

    private static int ToTicks(double quarterBeats)
        => (int)Math.Round(quarterBeats * SixteenthsPerQuarter);
}
