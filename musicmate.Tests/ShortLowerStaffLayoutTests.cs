using System.Reflection;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Short final/lower staff policy: independent readable width, no full-staff stretch,
/// ragged-right end bar (not yanked to the upper staff's right edge).
/// </summary>
public class ShortLowerStaffLayoutTests
{
    private const float CanvasWidth = 835f;
    private const float CanvasHeight = 480f;

    [Theory]
    [InlineData(16.0, 4.0, true)]   // 4+1
    [InlineData(16.0, 8.0, true)]   // 4+2
    [InlineData(12.0, 4.0, true)]   // 3+1
    [InlineData(16.0, 16.0, false)] // equal
    [InlineData(16.0, 12.0, false)] // 4+3 still comparable
    public void IsShortLowerStaff_DetectsSparseFinalLines(
        double upperBeats, double lowerBeats, bool expectShort)
    {
        Assert.Equal(expectShort, StaffDrawable.IsShortLowerStaff(upperBeats, lowerBeats));
    }

    [Fact]
    public void MatchedWidth_ShortLower_IsWiderThanTinyMatched_ButNotFullStaff()
    {
        const float peerContent = 650f;
        const float selfUsable = 700f;
        const double peerBeats = 16.0;
        const double selfBeats = 4.0; // 4+1

        float matchedTiny = (float)(selfBeats / peerBeats * peerContent); // ~162.5
        float planned = StaffDrawable.ComputeMatchedStaffUsableWidth(
            peerContent, peerBeats, selfBeats, selfUsable);

        Assert.True(planned > matchedTiny * 1.25f,
            $"Short lower plan {planned:F0} should exceed tiny matched {matchedTiny:F0}");
        Assert.True(planned <= selfUsable * 0.58f + 1f,
            $"Short lower plan {planned:F0} must not fill the whole staff ({selfUsable:F0})");
        Assert.True(planned >= 64f);
    }

    [Fact]
    public void MatchedWidth_EqualBeats_UsesFullUsable()
    {
        float planned = StaffDrawable.ComputeMatchedStaffUsableWidth(
            peerContentWidth: 650f,
            peerTotalBeats: 16.0,
            selfTotalBeats: 16.0,
            selfUsableWidth: 700f);
        Assert.Equal(700f, planned);
    }

    [Fact]
    public void MatchedWidth_ComparableShorterStaff_StillMatchesPxPerBeat()
    {
        // 4+3 → ratio 0.75 ≥ 0.55 → keep classic matched width
        float planned = StaffDrawable.ComputeMatchedStaffUsableWidth(
            peerContentWidth: 650f,
            peerTotalBeats: 16.0,
            selfTotalBeats: 12.0,
            selfUsableWidth: 700f);
        float expected = 12f / 16f * 650f;
        Assert.Equal(expected, planned, precision: 1);
    }

    [Theory]
    [InlineData(700f, 200f, 800f, true)]   // giant gap → ragged
    [InlineData(700f, 680f, 800f, false)]  // nearly aligned → align OK
    [InlineData(700f, 700f, 800f, false)]
    public void ShouldKeepRaggedLowerEndBar_OnlyWhenSubstantiallyShort(
        float upperEnd, float lowerEnd, float layoutLimit, bool expectRagged)
    {
        Assert.Equal(
            expectRagged,
            StaffDrawable.ShouldKeepRaggedLowerEndBar(upperEnd, lowerEnd, layoutLimit));
    }

    [Theory]
    [InlineData(4, 1)]
    [InlineData(4, 2)]
    [InlineData(3, 1)]
    public void ShortLower_FinalBarStaysNearLastNote_NotYankedToUpperRight(
        int upperMeasures, int lowerMeasures)
    {
        var (upper, lower, result) = LayoutQuarterStaves(upperMeasures, lowerMeasures, childLevel: 28);
        AssertNoLoss(result, upperMeasures + lowerMeasures);

        float layoutLimit = CanvasWidth - 2f;
        float upperEnd = result.UpperEndBarX;
        float lowerEnd = result.LowerEndBarX;
        float lowerNoteRight = result.LowerLastNoteRight;

        Assert.True(lowerEnd >= lowerNoteRight - 1f,
            $"Lower end bar {lowerEnd:F0} must clear last note {lowerNoteRight:F0}");
        Assert.True(
            StaffDrawable.ShouldKeepRaggedLowerEndBar(upperEnd, lowerEnd, layoutLimit)
            || lowerEnd < upperEnd - 40f,
            $"Short lower end {lowerEnd:F0} must not be forced to upper end {upperEnd:F0}");
        Assert.True(lowerEnd < layoutLimit - 80f,
            $"Lower end bar {lowerEnd:F0} must not sit at far right ({layoutLimit:F0}) with a giant blank gap");
        Assert.True(result.LowerPlanWidth <= result.LowerUsableWidth * 0.58f + 8f,
            $"Lower plan width {result.LowerPlanWidth:F0} must stay within short-fill cap");
    }

    [Fact]
    public void EqualStaves_EndBarsRemainAligned()
    {
        var (_, _, result) = LayoutQuarterStaves(4, 4, childLevel: 28);
        AssertNoLoss(result, 8);

        float gap = Math.Abs(result.UpperEndBarX - result.LowerEndBarX);
        Assert.True(gap < 8f,
            $"Equal staves should align end bars (gap={gap:F1}, U={result.UpperEndBarX:F0} L={result.LowerEndBarX:F0})");
    }

    [Fact]
    public void ShortScaleWalkLine_PackAndLayout_KeepsReadableLowerWithoutFarRightBar()
    {
        // B Natural Minor-style: 4 upper + 1 lower quarters.
        var (_, _, laid) = LayoutQuarterStaves(4, 1, childLevel: 28);
        Assert.True(laid.LowerEndBarX < laid.UpperEndBarX - 48f,
            $"Scale-walk lower end {laid.LowerEndBarX:F0} should stay left of upper {laid.UpperEndBarX:F0}");
        Assert.True(laid.LowerEndBarX < CanvasWidth - 100f);
        Assert.True(laid.LowerEndBarX >= laid.LowerLastNoteRight - 1f);
        Assert.True(laid.LowerPlanWidth > (4f / 16f) * 600f,
            "Short lower must be planned wider than tiny matched px/beat strip");
    }

    [Fact]
    public void DenseRandomTune_NoEventLoss()
    {
        var gen = new MusicSequenceGenerator
        {
            Key = "D",
            Scale = "Major",
            LowestNote = "A3",
            HighestNote = "E5",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 8,
            RhythmVarietyPercent = 70,
            SmallestDuration = NoteDuration.Sixteenth,
            RestChancePercent = 18,
            AccidentalPercent = 10,
            SyncopationLevel = SyncopationLevel.None,
            MaxMelodicIntervalSemitones = 4,
            UseScaleOrder = false,
            UseMotifPhrases = true,
            ChildLevel = 45,
            RandomSeed = 47,
        };
        var measures = gen.GenerateSequence();
        var flat = MusicSequenceGenerator.Flatten(measures);
        var bars = BarsFor(8);
        var session = new NoteSessionService
        {
            Key = "D",
            SelectedScale = "Major",
            MeterTimeSignature = "4/4",
            ChildLevel = 45,
            IsRandomMode = true,
        };
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        var split = drawable.SplitMeasuresAcrossStaves(flat, bars, CanvasWidth, CanvasHeight);
        Assert.Equal(8, split.TotalMeasureCount);
        Assert.Equal(
            split.UpperMeasureCount + split.LowerMeasureCount + split.UnplacedMeasureCount,
            split.TotalMeasureCount);
        Assert.Equal(
            flat.Count,
            split.UpperNotes.Count + split.LowerNotes.Count + split.UnplacedNotes.Count);
    }

    [Fact]
    public void SparseQuarterTune_ShortLower_NotCompressedToMatchedStrip()
    {
        float peer = 650f;
        float oldMatched = 4f / 16f * peer;
        float planned = StaffDrawable.ComputeMatchedStaffUsableWidth(peer, 16, 4, 700f);
        Assert.True(planned > oldMatched + 40f);
    }

    [Fact]
    public void SparseAndEqualLayouts_PreserveReadableInkGaps()
    {
        var session = new NoteSessionService
        {
            Key = "C",
            SelectedScale = "Major",
            MeterTimeSignature = "4/4",
            ChildLevel = 28,
        };
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        var (upper, lower, _) = LayoutQuarterStaves(4, 1, childLevel: 28);
        AssertNoInkOverlapOnStaff(drawable, upper);
        AssertNoInkOverlapOnStaff(drawable, lower);

        var (u2, l2, _) = LayoutQuarterStaves(4, 4, childLevel: 28);
        AssertNoInkOverlapOnStaff(drawable, u2);
        AssertNoInkOverlapOnStaff(drawable, l2);
    }

    private static void AssertNoLoss(LaidStaffs result, int expectedMeasures)
    {
        Assert.Equal(expectedMeasures, result.UpperMeasureCount + result.LowerMeasureCount);
        Assert.Equal(0, result.UnplacedMeasureCount);
    }

    private static (List<GeneratedNote> upper, List<GeneratedNote> lower, LaidStaffs result)
        LayoutQuarterStaves(int upperMeasures, int lowerMeasures, int childLevel)
    {
        var session = new NoteSessionService
        {
            Key = "C",
            SelectedScale = "Major",
            MeterTimeSignature = "4/4",
            ChildLevel = childLevel,
            Tune = "Selected Scale",
            ShowSignaturesOnBothStaffs = true,
        };
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        int total = upperMeasures + lowerMeasures;
        var all = MakeQuarterNotes(total, startMidi: 60);
        var bars = BarsFor(total);
        var split = drawable.SplitMeasuresAcrossStaves(all, bars, CanvasWidth, CanvasHeight);

        // Force the requested split when packing places differently on this width.
        var upper = all.Where(n => (n.MeasureIndex ?? 0) < upperMeasures).ToList();
        var lower = all.Where(n => (n.MeasureIndex ?? 0) >= upperMeasures).ToList();
        ShiftBeatsToZero(lower);

        var laid = LayoutPreparedStaves(drawable, session, upper, lower);
        laid.UpperMeasureCount = upperMeasures;
        laid.LowerMeasureCount = lowerMeasures;
        laid.UnplacedMeasureCount = 0;
        return (upper, lower, laid);
    }

    private static LaidStaffs LayoutPreparedStaves(
        StaffDrawable drawable,
        NoteSessionService session,
        List<GeneratedNote> upper,
        List<GeneratedNote> lower)
    {
        drawable.UpperNotes = upper;
        drawable.LowerNotes = lower;
        drawable.UpperBarBeats = BarsForMeasuresOf(upper);
        drawable.LowerBarBeats = BarsForMeasuresOf(lower);
        drawable.UpperHasEndBar = false;
        drawable.InvalidateLayoutCache();

        typeof(StaffDrawable)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(m => m.Name == "ComputeLayout" && m.GetParameters().Length == 3)
            .Invoke(drawable, new object[]
            {
                CanvasHeight,
                (IReadOnlyList<GeneratedNote>)upper,
                (IReadOnlyList<GeneratedNote>)lower,
            });

        var header = typeof(StaffDrawable)
            .GetMethod("ComputeHeaderMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { 0f })!;
        float leftMargin = (float)header.GetType().GetProperty("LeftMargin")!.GetValue(header)!;
        float clefOnly = (float)header.GetType().GetProperty("ClefOnlyLeftMargin")!.GetValue(header)!;
        bool lowerFull = session.ShowSignaturesOnBothStaffs;
        float lowerMargin = lowerFull ? leftMargin : clefOnly;

        const float RightMargin = 36f;
        const float LayoutRightPad = 2f;
        float safeWidth = CanvasWidth - LayoutRightPad;
        float upperUsable = safeWidth - leftMargin - RightMargin;
        float lowerUsable = safeWidth - lowerMargin - RightMargin;
        float layoutRightLimit = CanvasWidth - LayoutRightPad;

        var plan = typeof(StaffDrawable)
            .GetMethod("PlanHorizontalLayout", BindingFlags.Instance | BindingFlags.NonPublic)!;

        object upperPlan = plan.Invoke(drawable, new object[]
        {
            upper, drawable.UpperBarBeats, upperUsable, leftMargin, true, lower.Count == 0, false
        })!;
        var upperNotesLay = (Array)upperPlan.GetType().GetField("Item1")!.GetValue(upperPlan)!;
        var upperBarsLay = (Array)upperPlan.GetType().GetField("Item2")!.GetValue(upperPlan)!;
        float upperTotal = (float)upperPlan.GetType().GetField("Item3")!.GetValue(upperPlan)!;

        double upperBeats = upper.Sum(n => n.BeatDuration);
        double lowerBeats = lower.Sum(n => n.BeatDuration);
        float lowerPlanWidth = StaffDrawable.ComputeMatchedStaffUsableWidth(
            Math.Max(1f, upperTotal - leftMargin),
            upperBeats,
            lowerBeats,
            lowerUsable);
        bool lowerIsShort = StaffDrawable.IsShortLowerStaff(upperBeats, lowerBeats);
        bool expandLower = !lowerIsShort && lowerPlanWidth >= lowerUsable - 0.5f;

        object lowerPlan = plan.Invoke(drawable, new object[]
        {
            lower, drawable.LowerBarBeats, lowerPlanWidth, lowerMargin, lowerFull, true, false
        })!;
        var lowerNotesLay = (Array)lowerPlan.GetType().GetField("Item1")!.GetValue(lowerPlan)!;
        var lowerBarsLay = (Array)lowerPlan.GetType().GetField("Item2")!.GetValue(lowerPlan)!;
        float lowerTotal = (float)lowerPlan.GetType().GetField("Item3")!.GetValue(lowerPlan)!;

        typeof(StaffDrawable)
            .GetMethod("FinishBeginnerHorizontalLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[]
            {
                upper, upperNotesLay, upperBarsLay, upperTotal, upperUsable,
                lower, lowerNotesLay, lowerBarsLay, lowerTotal, lowerUsable,
                0f, leftMargin, lowerMargin, layoutRightLimit,
                40f, 60f, 80f, 140f, 160f, 180f,
                expandLower,
            });

        float upperEnd = ReadLastBarX(upperBarsLay);
        float lowerEnd = ReadLastBarX(lowerBarsLay);
        float lowerNoteRight = ReadMaxNoteRight(drawable, lower, lowerNotesLay);

        return new LaidStaffs
        {
            UpperEndBarX = upperEnd,
            LowerEndBarX = lowerEnd,
            LowerLastNoteRight = lowerNoteRight,
            LowerPlanWidth = lowerPlanWidth,
            LowerUsableWidth = lowerUsable,
        };
    }

    private static float ReadLastBarX(Array barLayouts)
    {
        if (barLayouts.Length == 0)
            return 0f;
        object last = barLayouts.GetValue(barLayouts.Length - 1)!;
        return (float)last.GetType().GetField("X")!.GetValue(last)!;
    }

    private static float ReadMaxNoteRight(StaffDrawable drawable, List<GeneratedNote> notes, Array layouts)
    {
        var trail = typeof(StaffDrawable).GetMethod(
            "NoteTrailingRight",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { typeof(bool), typeof(float), typeof(NoteDuration) },
            null)!;
        float max = float.NegativeInfinity;
        for (int i = 0; i < notes.Count; i++)
        {
            object lay = layouts.GetValue(i)!;
            float x = (float)lay.GetType().GetField("X")!.GetValue(lay)!;
            bool isRest = notes[i].IsRest;
            var dur = notes[i].Duration;
            float right = (float)trail.Invoke(drawable, new object[] { isRest, x, dur })!;
            if (right > max)
                max = right;
        }
        return max;
    }

    private static void AssertNoInkOverlapOnStaff(StaffDrawable drawable, List<GeneratedNote> notes)
    {
        if (notes.Count < 2)
            return;

        typeof(StaffDrawable)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(m => m.Name == "ComputeLayout" && m.GetParameters().Length == 3)
            .Invoke(drawable, new object[]
            {
                360f,
                (IReadOnlyList<GeneratedNote>)notes,
                (IReadOnlyList<GeneratedNote>)new List<GeneratedNote>(),
            });

        var bars = BarsForMeasuresOf(notes);
        object plan = typeof(StaffDrawable)
            .GetMethod("PlanHorizontalLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { notes, bars, 700f, 100f, true, true, true })!;
        var layouts = (Array)plan.GetType().GetField("Item1")!.GetValue(plan)!;
        var trail = typeof(StaffDrawable).GetMethod(
            "NoteTrailingRight",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { typeof(GeneratedNote), typeof(float) },
            null)!;
        var groupLeft = typeof(StaffDrawable).GetMethod(
            "NoteGroupLeftFromLayout",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        float prevRight = float.NegativeInfinity;
        foreach (int i in Enumerable.Range(0, notes.Count)
                     .OrderBy(i => notes[i].BeatPosition ?? 0)
                     .ThenBy(i => i))
        {
            var lay = layouts.GetValue(i)!;
            float x = (float)lay.GetType().GetField("X")!.GetValue(lay)!;
            float left = (float)groupLeft.Invoke(drawable, new object[] { lay })!;
            if (prevRight > float.NegativeInfinity)
                Assert.True(left + 0.5f >= prevRight, $"Ink overlap left={left:0.#} prevRight={prevRight:0.#}");
            prevRight = (float)trail.Invoke(drawable, new object[] { notes[i], x })!;
        }
    }

    private static List<GeneratedNote> MakeQuarterNotes(int measureCount, int startMidi)
    {
        var notes = new List<GeneratedNote>();
        int midi = startMidi;
        for (int m = 0; m < measureCount; m++)
        {
            for (int b = 0; b < 4; b++)
            {
                notes.Add(new GeneratedNote
                {
                    MidiNumber = midi,
                    Letter = "CDEFGAB"[((midi % 12) switch
                    {
                        0 or 1 => 0,
                        2 or 3 => 1,
                        4 => 2,
                        5 or 6 => 3,
                        7 or 8 => 4,
                        9 or 10 => 5,
                        _ => 6
                    })],
                    Octave = midi / 12 - 1,
                    SpelledName = $"N{midi}",
                    Duration = NoteDuration.Quarter,
                    MeasureIndex = m,
                    BeatPosition = m * 4.0 + b,
                });
                midi++;
            }
        }
        return notes;
    }

    private static List<double> BarsFor(int measureCount)
    {
        var bars = new List<double>();
        for (double b = 4.0; b < measureCount * 4.0 - 1e-6; b += 4.0)
            bars.Add(b);
        return bars;
    }

    private static List<double> BarsForMeasuresOf(List<GeneratedNote> notes)
    {
        if (notes.Count == 0)
            return new List<double>();
        double origin = notes.Min(n => n.BeatPosition ?? 0);
        double end = notes.Max(n => (n.BeatPosition ?? 0) + n.BeatDuration);
        var bars = new List<double>();
        for (double b = origin + 4.0; b < end - 1e-6; b += 4.0)
            bars.Add(b);
        return bars;
    }

    private static void ShiftBeatsToZero(List<GeneratedNote> notes)
    {
        if (notes.Count == 0)
            return;
        double shift = notes.Min(n => n.BeatPosition ?? 0);
        for (int i = 0; i < notes.Count; i++)
        {
            var n = notes[i];
            notes[i] = new GeneratedNote
            {
                MidiNumber = n.MidiNumber,
                Letter = n.Letter,
                Octave = n.Octave,
                Accidental = n.Accidental,
                SpelledName = n.SpelledName,
                Duration = n.Duration,
                IsRest = n.IsRest,
                MeasureIndex = n.MeasureIndex,
                BeatPosition = (n.BeatPosition ?? 0) - shift,
            };
        }
    }

    private sealed class LaidStaffs
    {
        public float UpperEndBarX { get; set; }
        public float LowerEndBarX { get; set; }
        public float LowerLastNoteRight { get; set; }
        public float LowerPlanWidth { get; set; }
        public float LowerUsableWidth { get; set; }
        public int UpperMeasureCount { get; set; }
        public int LowerMeasureCount { get; set; }
        public int UnplacedMeasureCount { get; set; }
    }
}
