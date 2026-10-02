using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Stress-tests Random generation: every measure must sum to exactly the meter
/// capacity in sixteenth-note ticks (exact integer arithmetic).
/// </summary>
public class RandomMeasureDurationInvariantTests
{
    private const int SixteenthsPerQuarter = 4;

    public static IEnumerable<object[]> AllGeneratorConfigs()
    {
        var meters = TimeSignature.CommonDisplayOptions
            .Select(TimeSignature.FromDisplayString)
            .ToArray();
        var smallests = new[]
        {
            NoteDuration.Quarter,
            NoteDuration.Eighth,
            NoteDuration.Sixteenth,
        };
        var syncs = new[]
        {
            SyncopationLevel.None,
            SyncopationLevel.Simple,
            SyncopationLevel.Full,
        };

        foreach (var ts in meters)
        foreach (var smallest in smallests)
        foreach (var sync in syncs)
        foreach (bool motif in new[] { true, false })
        {
            yield return new object[] { ts.ToString(), smallest, sync, motif };
        }
    }

    [Theory]
    [MemberData(nameof(AllGeneratorConfigs))]
    public void EveryGeneratedMeasure_ExactTickDuration(
        string meterDisplay,
        NoteDuration smallest,
        SyncopationLevel sync,
        bool useMotif)
    {
        var ts = TimeSignature.FromDisplayString(meterDisplay);
        int expectedTicks = ToTicks(ts.TotalBeats);
        const int seedsPerConfig = 120;

        for (int seed = 0; seed < seedsPerConfig; seed++)
        {
            var gen = new MusicSequenceGenerator
            {
                Key = "C",
                Scale = "Major",
                LowestNote = "C4",
                HighestNote = "C5",
                TimeSignature = ts,
                MeasureCount = 8,
                RhythmVarietyPercent = 100,
                SmallestDuration = smallest,
                RestChancePercent = 40,
                SyncopationLevel = sync,
                UseMotifPhrases = useMotif,
                UseScaleOrder = false,
                AccidentalPercent = 0,
                RandomSeed = seed ^ (meterDisplay.GetHashCode() * 397)
                    ^ ((int)smallest * 17) ^ ((int)sync * 31) ^ (useMotif ? 64 : 0),
            };

            var measures = gen.GenerateSequence();
            Assert.NotEmpty(measures);

            var flat = MusicSequenceGenerator.Flatten(measures);
            Assert.Equal(measures.Sum(m => m.GeneratedNotes.Count), flat.Count);

            for (int mi = 0; mi < measures.Count; mi++)
            {
                var measure = measures[mi];
                int ticks = measure.GeneratedNotes.Sum(n => ToTicks(n.Duration.ToBeatValue()));
                if (ticks != expectedTicks)
                {
                    string events = string.Join(
                        ", ",
                        measure.GeneratedNotes.Select(n =>
                            $"{(n.IsRest ? "rest" : "note")}:{n.Duration}({ToTicks(n.Duration.ToBeatValue())}t)"));

                    Assert.Fail(
                        $"Meter {meterDisplay} smallest={smallest} sync={sync} motif={useMotif} " +
                        $"seed={seed} measure={mi}: ticks {ticks} != {expectedTicks}. Events: [{events}]");
                }

                double start = mi * ts.TotalBeats;
                double end = start + ts.TotalBeats;
                double prev = start - 1e-9;
                foreach (var n in measure.GeneratedNotes)
                {
                    double bp = n.BeatPosition ?? -1;
                    Assert.True(
                        bp >= start - 1e-9 && bp < end - 1e-9,
                        $"Meter {meterDisplay} seed={seed} measure={mi}: BeatPosition {bp} outside [{start},{end})");
                    Assert.True(bp >= prev - 1e-9);
                    prev = bp;
                    Assert.True(
                        bp + n.BeatDuration <= end + 1e-9,
                        $"Meter {meterDisplay} seed={seed} measure={mi}: event crosses bar ({bp}+{n.BeatDuration} > {end})");
                }
            }
        }
    }

    private static int ToTicks(double quarterBeats)
        => (int)Math.Round(quarterBeats * SixteenthsPerQuarter);
}
