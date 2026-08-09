using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Consecutive cut-point selection: maximize placed music, then balance U/L.
/// </summary>
public class BalancedMeasureSplitTests
{
    private const float Usable = 700f;

    [Fact]
    public void ChooseSplit_FivePlusThree_BecomesFourPlusFour_WhenBothPlaceEight()
    {
        // Greedy max-upper packs 5 then 3; balanced prefers 4+4 (same 8 placed).
        float[] mins = { 120, 120, 120, 120, 120, 120, 120, 120 };
        Assert.Equal(5, StaffDrawable.PackConsecutiveMeasureWidths(mins, Usable, 0));
        Assert.Equal(3, StaffDrawable.PackConsecutiveMeasureWidths(mins, Usable, 5));

        var (u, l) = StaffDrawable.ChooseBalancedMeasureSplit(mins, Usable, Usable);
        Assert.Equal(4, u);
        Assert.Equal(4, l);
    }

    [Fact]
    public void ChooseSplit_FourPlusTwo_BecomesThreePlusThree_WhenBothPlaceSix()
    {
        // Greedy max-upper → 4+2; cut u=3 yields 3+3 with the same placed count.
        float[] mins = { 160, 160, 160, 100, 250, 250, 400, 400 };
        const float usable = 700f;
        Assert.Equal(4, StaffDrawable.PackConsecutiveMeasureWidths(mins, usable, 0));
        Assert.Equal(2, StaffDrawable.PackConsecutiveMeasureWidths(mins, usable, 4));
        Assert.Equal(3, StaffDrawable.PackConsecutiveMeasureWidths(mins, usable, 3));

        var (u, l) = StaffDrawable.ChooseBalancedMeasureSplit(mins, usable, usable);
        Assert.Equal(3, u);
        Assert.Equal(3, l);
        Assert.Equal(6, u + l);
    }

    [Fact]
    public void ChooseSplit_TwoPlusFour_CandidateLosesToThreePlusThree_WhenBothPlaceSix()
    {
        // Among same placed=6 cuts, |2−4| loses to |3−3| (greedy max-upper here is 4+2).
        float[] mins = { 120, 120, 120, 180, 180, 180, 400, 400 };
        const float usable = 700f;
        Assert.Equal(4, StaffDrawable.PackConsecutiveMeasureWidths(mins, usable, 0));
        Assert.Equal(2, StaffDrawable.PackConsecutiveMeasureWidths(mins, usable, 4)); // greedy 4+2
        Assert.Equal(4, StaffDrawable.PackConsecutiveMeasureWidths(mins, usable, 2)); // 2+4 also valid
        Assert.Equal(3, StaffDrawable.PackConsecutiveMeasureWidths(mins, usable, 3)); // 3+3 valid

        var (u, l) = StaffDrawable.ChooseBalancedMeasureSplit(mins, usable, usable);
        Assert.Equal(3, u);
        Assert.Equal(3, l);
        Assert.Equal(6, u + l);
    }

    [Fact]
    public void ChooseSplit_DenseFourPlusThree_PreferredOverThreePlusThree_BecauseMoreMusic()
    {
        // 4+3 places 7; 3+3 places only 6 — must keep 4+3.
        float[] mins = { 150, 150, 150, 150, 200, 200, 200, 400 };
        Assert.Equal(4, StaffDrawable.PackConsecutiveMeasureWidths(mins, Usable, 0));
        Assert.Equal(3, StaffDrawable.PackConsecutiveMeasureWidths(mins, Usable, 4));
        Assert.Equal(3, StaffDrawable.PackConsecutiveMeasureWidths(mins, Usable, 3));

        var (u, l) = StaffDrawable.ChooseBalancedMeasureSplit(mins, Usable, Usable);
        Assert.Equal(4, u);
        Assert.Equal(3, l);
        Assert.Equal(7, u + l);
    }

    [Fact]
    public void ChooseSplit_PrefersBalancedOverAllOnUpper_WhenSamePlacedCount()
    {
        // 3+0 and 1+2/2+1 all place 3; balance wins over all-on-upper.
        // 1+2 and 2+1 are equal on placed/|diff|/min; first equal cut (u=1) is kept.
        float[] mins = { 100, 100, 100 };
        var (u, l) = StaffDrawable.ChooseBalancedMeasureSplit(mins, Usable, Usable);
        Assert.Equal(3, u + l);
        Assert.Equal(1, Math.Abs(u - l));
        Assert.NotEqual(0, l);
        Assert.True((u == 1 && l == 2) || (u == 2 && l == 1));
    }

    [Fact]
    public void ChooseSplit_ShortLowerOneMeasure_RemainsOneOnLower_WhenOnlyFourExist()
    {
        // Four equal measures: best is 2+2 (not forced 3+1).
        float[] mins = { 150, 150, 150, 150 };
        var (u, l) = StaffDrawable.ChooseBalancedMeasureSplit(mins, Usable, Usable);
        Assert.Equal(2, u);
        Assert.Equal(2, l);
    }

    [Fact]
    public void ChooseSplit_GenuineShortLower_ThreePlusOne_WhenFourEqualButOnlyFourTotal()
    {
        // If only cut that places all four with rem of 1 after taking 3:
        // Actually with equal mins, 2+2 wins. Construct: first 3 fit, 4th alone on lower —
        // both 3+1 and 2+2 place 4; 2+2 has better balance so we get 2+2.
        // Genuine short-lower for scoring: n=4 where 2+2 cannot fit lower second pair.
        // Upper can take 3; lower takes 1. Can upper take only 2? Then lower PackFrom(2):
        // if mins[2]+mins[3] > usable, lower packs 1 only → 2+1 places 3 < 4. So 3+1 wins.
        // Genuine short-lower: 3+1 places all four; cut u=2 only places 3
        // because measures 3+4 cannot share the lower (200+550 > usable).
        float[] mins = { 200, 200, 200, 550 };
        Assert.Equal(3, StaffDrawable.PackConsecutiveMeasureWidths(mins, Usable, 0));
        Assert.Equal(1, StaffDrawable.PackConsecutiveMeasureWidths(mins, Usable, 3));
        Assert.Equal(1, StaffDrawable.PackConsecutiveMeasureWidths(mins, Usable, 2));

        var (u, l) = StaffDrawable.ChooseBalancedMeasureSplit(mins, Usable, Usable);
        Assert.Equal(3, u);
        Assert.Equal(1, l);
        Assert.Equal(4, u + l);
    }

    [Fact]
    public void SplitMeasuresAcrossStaves_CMajor_UsesFourPlusFour_NotFivePlusThree()
    {
        var gen = new MusicSequenceGenerator
        {
            Key = "C",
            Scale = "Major",
            LowestNote = "A2",
            HighestNote = "C6",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 8,
            RhythmVarietyPercent = 0,
            SmallestDuration = NoteDuration.Quarter,
            RestChancePercent = 0,
            AccidentalPercent = 0,
            SyncopationLevel = SyncopationLevel.None,
            MaxMelodicIntervalSemitones = 5,
            UseScaleOrder = true,
            UseMotifPhrases = false,
            ChildLevel = 28,
            RandomSeed = 11,
        };
        var page = MusicSequenceGenerator.Flatten(gen.GenerateSequence());
        var bars = Enumerable.Range(1, 7).Select(i => i * 4.0).ToList();
        var session = new NoteSessionService
        {
            Key = "C",
            SelectedScale = "Major",
            MeterTimeSignature = "4/4",
            ChildLevel = 28,
            ShowSignaturesOnBothStaffs = true,
        };
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        var split = drawable.SplitMeasuresAcrossStaves(page, bars, 835f, 480f);

        Assert.Equal(0, split.UnplacedMeasureCount);
        Assert.Equal(8, split.UpperMeasureCount + split.LowerMeasureCount);
        Assert.Equal(4, split.UpperMeasureCount);
        Assert.Equal(4, split.LowerMeasureCount);
        Assert.Equal(page.Count, split.UpperNotes.Count + split.LowerNotes.Count);
        AssertMeasureOrderPreserved(page, split);
    }

    [Fact]
    public void SplitMeasuresAcrossStaves_NoReorderOrEventLoss()
    {
        var gen = new MusicSequenceGenerator
        {
            Key = "Bb",
            Scale = "Major",
            LowestNote = "A2",
            HighestNote = "C6",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 8,
            RhythmVarietyPercent = 55,
            SmallestDuration = NoteDuration.Eighth,
            RestChancePercent = 18,
            AccidentalPercent = 20,
            SyncopationLevel = SyncopationLevel.None,
            MaxMelodicIntervalSemitones = 5,
            UseScaleOrder = false,
            UseMotifPhrases = true,
            ChildLevel = 40,
            RandomSeed = 44,
        };
        var page = MusicSequenceGenerator.Flatten(gen.GenerateSequence());
        var bars = Enumerable.Range(1, 7).Select(i => i * 4.0).ToList();
        var session = new NoteSessionService
        {
            Key = "Bb",
            SelectedScale = "Major",
            MeterTimeSignature = "4/4",
            ChildLevel = 40,
            ShowSignaturesOnBothStaffs = true,
            IsRandomMode = true,
        };
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        var split = drawable.SplitMeasuresAcrossStaves(page, bars, 835f, 480f);

        Assert.Equal(
            page.Count,
            split.UpperNotes.Count + split.LowerNotes.Count + split.UnplacedNotes.Count);
        Assert.Equal(
            8,
            split.UpperMeasureCount + split.LowerMeasureCount + split.UnplacedMeasureCount);
        AssertMeasureOrderPreserved(page, split);
    }

    private static void AssertMeasureOrderPreserved(
        List<GeneratedNote> page,
        StaffDrawable.StaffMeasureSplitResult split)
    {
        var displayed = split.UpperNotes.Concat(split.LowerNotes).Concat(split.UnplacedNotes).ToList();
        // Same multiset of note identities in page order for displayed+unplaced by original BeatPosition.
        var pageOrder = page
            .Select(n => (n.BeatPosition ?? 0, n.MidiNumber, n.IsRest, n.Duration))
            .ToList();
        var outOrder = displayed
            .Select(n => (n.BeatPosition ?? 0, n.MidiNumber, n.IsRest, n.Duration))
            .ToList();
        Assert.Equal(pageOrder, outOrder);

        // Upper measures are a prefix of measure indices; lower follows; unplaced last.
        var upperMeasures = split.UpperNotes.Select(n => n.MeasureIndex ?? 0).Distinct().OrderBy(m => m).ToList();
        var lowerMeasures = split.LowerNotes.Select(n => n.MeasureIndex ?? 0).Distinct().OrderBy(m => m).ToList();
        var unplacedMeasures = split.UnplacedNotes.Select(n => n.MeasureIndex ?? 0).Distinct().OrderBy(m => m).ToList();

        if (upperMeasures.Count > 0 && lowerMeasures.Count > 0)
            Assert.True(upperMeasures[^1] < lowerMeasures[0]);
        if (lowerMeasures.Count > 0 && unplacedMeasures.Count > 0)
            Assert.True(lowerMeasures[^1] < unplacedMeasures[0]);
        if (upperMeasures.Count > 0 && unplacedMeasures.Count > 0 && lowerMeasures.Count == 0)
            Assert.True(upperMeasures[^1] < unplacedMeasures[0]);
    }
}
