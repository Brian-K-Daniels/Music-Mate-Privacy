using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

public class AccidentalNotationAndGenerationTests
{
    [Fact]
    public void NaturalAgainstKeySignature_CarriesWithinMeasure_WithoutRedraw()
    {
        // C Harmonic Minor: B♮ against Bb in the signature.
        Assert.True(MeasureAccidentalRules.ShouldDrawNatural("b", priorInBar: null));
        Assert.False(MeasureAccidentalRules.ShouldDrawNatural("b", priorInBar: Accidental.Natural));
    }

    [Fact]
    public void Natural_RedrawsAfterChromaticThenCancel()
    {
        Assert.True(MeasureAccidentalRules.ShouldDrawNatural(
            keySigAccidentalForLetter: null,
            priorInBar: Accidental.Sharp));
    }

    [Fact]
    public void Natural_ResetsAtNextMeasure()
    {
        // Cleared history at the bar line → draw again when letter is altered in the key sig.
        Assert.True(MeasureAccidentalRules.ShouldDrawNatural("b", priorInBar: null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public void AccidentalPercent_DoesNotCorruptMeasureDurations(int accPercent)
    {
        foreach (var meter in TimeSignature.CommonDisplayOptions)
        {
            var ts = TimeSignature.FromDisplayString(meter);
            int expectedTicks = (int)Math.Round(ts.TotalBeats * 4);
            for (int seed = 0; seed < 40; seed++)
            {
                var gen = CreateGenerator(seed, "C", "Harmonic Minor", ts, accPercent);
                var measures = gen.GenerateSequence();
                Assert.NotEmpty(measures);
                foreach (var m in measures)
                {
                    int ticks = m.GeneratedNotes.Sum(n => (int)Math.Round(n.Duration.ToBeatValue() * 4));
                    Assert.True(
                        ticks == expectedTicks,
                        $"meter={meter} acc%={accPercent} seed={seed}: {ticks} != {expectedTicks}");
                }
            }
        }
    }

    [Fact]
    public void AccidentalPercent_Zero_ProducesNoNonScaleChromatics()
    {
        var scalePcs = NoteSessionService.GetScalePitchClasses("C", "Harmonic Minor");
        for (int seed = 0; seed < 80; seed++)
        {
            var gen = CreateGenerator(seed, "C", "Harmonic Minor", TimeSignature.FourFour, accPercent: 0);
            var pitches = MusicSequenceGenerator.Flatten(gen.GenerateSequence())
                .Where(n => !n.IsRest)
                .Select(n => ((n.MidiNumber % 12) + 12) % 12)
                .ToList();
            Assert.NotEmpty(pitches);
            Assert.All(pitches, pc => Assert.Contains(pc, scalePcs));
        }
    }

    [Fact]
    public void AccidentalPercent_HigherSetting_IncreasesChromaticRate()
    {
        double Rate(int accPercent)
        {
            var scalePcs = NoteSessionService.GetScalePitchClasses("C", "Major");
            int chromatic = 0;
            int total = 0;
            for (int seed = 0; seed < 120; seed++)
            {
                var gen = CreateGenerator(seed, "C", "Major", TimeSignature.FourFour, accPercent);
                gen.MeasureCount = 8;
                gen.UseMotifPhrases = false;
                foreach (var n in MusicSequenceGenerator.Flatten(gen.GenerateSequence()).Where(x => !x.IsRest))
                {
                    total++;
                    int pc = ((n.MidiNumber % 12) + 12) % 12;
                    if (!scalePcs.Contains(pc))
                        chromatic++;
                }
            }

            Assert.True(total > 0);
            return (double)chromatic / total;
        }

        double r0 = Rate(0);
        double r50 = Rate(50);
        double r100 = Rate(100);

        Assert.True(r0 < 0.02, $"0% should be near zero chromatics, got {r0:P1}");
        Assert.True(r50 > r0 + 0.05, $"50% ({r50:P1}) should exceed 0% ({r0:P1})");
        Assert.True(r100 > r50, $"100% ({r100:P1}) should exceed 50% ({r50:P1})");
    }

    [Fact]
    public void BuildNote_MidiMatchesSoundingPitch_ForChromaticAndDiatonic()
    {
        var gen = CreateGenerator(42, "C", "Harmonic Minor", TimeSignature.FourFour, accPercent: 100);
        gen.MeasureCount = 8;
        foreach (var n in MusicSequenceGenerator.Flatten(gen.GenerateSequence()).Where(x => !x.IsRest))
        {
            int fromName = NoteSessionService.NoteNameToMidi(
                NoteSessionService.SpellWrittenPitch(n.MidiNumber, "C", "Harmonic Minor"));
            Assert.Equal(((n.MidiNumber % 12) + 12) % 12, ((fromName % 12) + 12) % 12);

            var (acc, spelled) = NoteSessionService.ResolveAccidentalAndSpelling(
                n.SpelledName, n.MidiNumber, n.Letter, n.Octave, "C", "Harmonic Minor");
            Assert.False(string.IsNullOrWhiteSpace(spelled));
            if (n.Accidental != Accidental.None)
                Assert.Equal(acc, n.Accidental);
        }
    }

    private static MusicSequenceGenerator CreateGenerator(
        int seed, string key, string scale, TimeSignature ts, int accPercent)
        => new()
        {
            Key = key,
            Scale = scale,
            LowestNote = "C4",
            HighestNote = "C5",
            TimeSignature = ts,
            MeasureCount = 4,
            RhythmVarietyPercent = 60,
            SmallestDuration = NoteDuration.Eighth,
            RestChancePercent = 15,
            AccidentalPercent = accPercent,
            UseScaleOrder = false,
            UseMotifPhrases = true,
            RandomSeed = seed,
        };
}
