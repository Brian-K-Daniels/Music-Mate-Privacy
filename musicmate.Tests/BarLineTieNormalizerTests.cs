using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Regression: notes that cross / overflow a bar must become tied segments whose
/// durations sum to the original — never an extra eighth in the start bar.
/// </summary>
public class BarLineTieNormalizerTests
{
    [Fact]
    public void QuarterCrossingBar_SplitsIntoTiedEighths_NoExtraDuration()
    {
        // Exact rhythm type from the bug report: a quarter that starts on the "&" of
        // beat 4 and must be split at the bar into two tied eighths.
        var notes = new List<GeneratedNote>
        {
            Note(60, "C4", NoteDuration.Quarter, 0, 0),
            Note(62, "D4", NoteDuration.Quarter, 0, 1),
            Note(64, "E4", NoteDuration.Quarter, 0, 2),
            Note(65, "F4", NoteDuration.Eighth, 0, 3),
            Note(67, "G4", NoteDuration.Quarter, 0, 3.5), // crosses into bar 2
            Note(69, "A4", NoteDuration.Quarter, 1, 4.5),
            Note(71, "B4", NoteDuration.Quarter, 1, 5.5),
            Note(72, "C5", NoteDuration.Half, 1, 6.5),
        };

        var normalized = BarLineTieNormalizer.Normalize(notes, 4.0);

        AssertMeasureDurationsExactly(normalized, 4.0);

        var gTied = normalized.Where(n => n.SpelledName == "G4" && n.TieGroupId.HasValue).ToList();
        Assert.Equal(2, gTied.Count);
        Assert.All(gTied, n => Assert.Equal(NoteDuration.Eighth, n.Duration));
        Assert.Equal(gTied[0].TieGroupId, gTied[1].TieGroupId);
        Assert.False(gTied[0].IsTieContinuation);
        Assert.True(gTied[1].IsTieContinuation);
        Assert.Equal(3.5, gTied[0].BeatPosition!.Value, 3);
        Assert.Equal(4.0, gTied[1].BeatPosition!.Value, 3);
        Assert.Equal(1.0, gTied.Sum(n => n.BeatDuration), 3);

        // Playback targets: continuations are not separate notes.
        int playbackTargets = normalized.Count(n => !n.IsRest && !n.IsTieContinuation);
        Assert.Equal(notes.Count(n => !n.IsRest), playbackTargets);
    }

    [Fact]
    public void OverfullHalfPlusEighthsPlusQuarter_SplitsOverflowAtBar_NoDuplicatedDuration()
    {
        // Same shape as bars 1/3 in the reported example: half + three eighths + quarter
        // (= 4.5) with the quarter starting at beat 3.5 so it crosses the bar. After
        // normalize, bar 1 holds exactly 4 beats and the overflow is a tied eighth.
        var notes = new List<GeneratedNote>
        {
            Note(58, "Bb3", NoteDuration.Half, 0, 0),
            Note(57, "A3", NoteDuration.Eighth, 0, 2),
            Note(57, "A3", NoteDuration.Eighth, 0, 2.5),
            Note(58, "Bb3", NoteDuration.Eighth, 0, 3),
            Note(57, "A3", NoteDuration.Quarter, 0, 3.5),
        };

        double originalTotal = notes.Sum(n => n.BeatDuration);
        var normalized = BarLineTieNormalizer.Normalize(notes, 4.0);

        AssertMeasureDurationsExactly(normalized, 4.0);
        Assert.Equal(originalTotal, normalized.Sum(n => n.BeatDuration), 3);

        var bar1 = NotesInMeasure(normalized, 0, 4.0);
        Assert.Equal(4.0, bar1.Sum(n => n.BeatDuration), 3);
        Assert.DoesNotContain(bar1, n => n.Duration == NoteDuration.Quarter);

        Assert.Contains(normalized, n =>
            n.TieGroupId.HasValue
            && !n.IsTieContinuation
            && Math.Abs((n.BeatPosition ?? 0) - 3.5) < 1e-6
            && n.Duration == NoteDuration.Eighth);
        Assert.Contains(normalized, n =>
            n.IsTieContinuation
            && Math.Abs((n.BeatPosition ?? 0) - 4.0) < 1e-6
            && n.Duration == NoteDuration.Eighth);
    }

    [Fact]
    public void LegalFourFourSequence_Unchanged()
    {
        var notes = new List<GeneratedNote>
        {
            Note(60, "C4", NoteDuration.Quarter, 0, 0),
            Note(62, "D4", NoteDuration.Quarter, 0, 1),
            Note(64, "E4", NoteDuration.Half, 0, 2),
            Note(65, "F4", NoteDuration.Whole, 1, 4),
        };

        var normalized = BarLineTieNormalizer.Normalize(notes, 4.0);
        Assert.Equal(notes.Count, normalized.Count);
        Assert.All(normalized, n => Assert.Null(n.TieGroupId));
        AssertMeasureDurationsExactly(normalized, 4.0);
    }

    private static void AssertMeasureDurationsExactly(
        IReadOnlyList<GeneratedNote> notes, double beatsPerMeasure)
    {
        var sums = BarLineTieNormalizer.MeasureRenderedDurations(notes, beatsPerMeasure);
        Assert.NotEmpty(sums);
        for (int i = 0; i < sums.Count; i++)
        {
            bool isLast = i == sums.Count - 1;
            if (isLast)
            {
                Assert.True(
                    sums[i] <= beatsPerMeasure + 1e-6,
                    $"Final measure duration {sums[i]} exceeds {beatsPerMeasure}");
            }
            else
            {
                Assert.Equal(beatsPerMeasure, sums[i], 3);
            }
        }
    }

    private static List<GeneratedNote> NotesInMeasure(
        IReadOnlyList<GeneratedNote> notes, int measureIndex, double beatsPerMeasure)
    {
        double start = measureIndex * beatsPerMeasure;
        double end = start + beatsPerMeasure;
        return notes
            .Where(n =>
            {
                double bp = n.BeatPosition ?? -1;
                return bp >= start - 1e-9 && bp < end - 1e-9;
            })
            .ToList();
    }

    private static GeneratedNote Note(
        int midi, string name, NoteDuration dur, int measure, double beat)
        => new()
        {
            MidiNumber = midi,
            SpelledName = name,
            Letter = name[0],
            Octave = int.Parse(name[^1].ToString()),
            Duration = dur,
            MeasureIndex = measure,
            BeatPosition = beat,
        };
}
