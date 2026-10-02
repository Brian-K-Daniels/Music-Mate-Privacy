using System.Reflection;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Pack usable width must match draw; beginner engraving must never crush below MinInkGap
/// after the packer accepts a measure.
/// </summary>
public class PackRenderContractTests
{
    private const float MinInkGap = 8f;
    private const float CanvasW = 835f;
    private const float CanvasH = 480f;

    [Fact]
    public void SmokingGun_DenseSixteenthsWithSharp_PreservesMinInkGapWhenPacked()
    {
        var notes = SmokingGunMeasure();
        var session = Session("C", "Major", 40, showBoth: true);
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        WarmLayout(drawable, notes);

        float minW = ComputeMinWidth(drawable, notes);
        // Packer admits the measure into a slot at least minW wide.
        float avail = minW + 8f;
        var layouts = Plan(drawable, notes, avail, staffMargin: 120f);
        float gap = SmallestInkGap(drawable, notes, layouts);
        Assert.True(gap + 0.05f >= MinInkGap,
            $"Smoking-gun gap {gap:F1} must be >= MinInkGap {MinInkGap} when packed at min+8 ({avail:F0}); minW={minW:F0}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public void DenseBeginner_44_NoGapBelowMin_NoEventLoss(int accPct)
    {
        AssertDensePage("D", "Major", 40, 8, NoteDuration.Eighth, accPct, "4/4", seed: 77 + accPct);
    }

    [Fact]
    public void DenseBeginner_34_NoGapBelowMin_NoEventLoss()
    {
        AssertDensePage("G", "Major", 40, 6, NoteDuration.Eighth, 20, "3/4", seed: 55);
    }

    [Theory]
    [InlineData("Ab", "Major")]   // flat-heavy
    [InlineData("F#", "Major")]   // sharp-heavy
    public void DenseKeys_SignaturesOnBoth_NoGapBelowMin(string key, string scale)
    {
        AssertDensePage(key, scale, 45, 8, NoteDuration.Sixteenth, 15, "4/4", seed: 47);
    }

    [Fact]
    public void LowerPackUsable_EqualsDrawUsable_WhenSignaturesOnBoth()
    {
        foreach (var (key, scale, level) in new[]
        {
            ("C", "Major", 28),
            ("F", "Major", 40),
            ("F#", "Natural Minor", 45),
            ("Ab", "Major", 40),
        })
        {
            var session = Session(key, scale, level, showBoth: true);
            var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
            var notes = MakeQuarters(4);
            WarmLayout(drawable, notes);
            var header = ComputeHeader(drawable);
            float left = header.LeftMargin;
            float clef = header.ClefOnly;
            float packLowerHeader = StaffDrawable.ResolveLowerStaffHeaderMargin(left, clef, showSignaturesOnBothStaffs: true);
            float drawLowerHeader = left; // ShowSignaturesOnBothStaffs → full header
            Assert.Equal(drawLowerHeader, packLowerHeader);

            float safe = CanvasW - 2f;
            float packLower = safe - packLowerHeader - 36f;
            float drawLower = safe - drawLowerHeader - 36f;
            Assert.Equal(drawLower, packLower);
        }
    }

    [Fact]
    public void LowerPackUsable_UsesClefOnly_WhenSignaturesNotOnBoth()
    {
        float pack = StaffDrawable.ResolveLowerStaffHeaderMargin(180f, 70f, showSignaturesOnBothStaffs: false);
        Assert.Equal(70f, pack);
    }

    [Fact]
    public void ShortLower_4Plus1_StillRaggedRight_AndGapsOk()
    {
        var session = Session("C", "Major", 28, showBoth: true);
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        var all = MakeQuarters(5);
        var bars = BarsFor(5, 4);
        var split = drawable.SplitMeasuresAcrossStaves(all, bars, CanvasW, CanvasH);
        Assert.Equal(0, split.UnplacedMeasureCount);
        Assert.Equal(all.Count, split.UpperNotes.Count + split.LowerNotes.Count + split.UnplacedNotes.Count);

        // Force 4+1 layout for short-lower policy check.
        var upper = all.Where(n => (n.MeasureIndex ?? 0) < 4).ToList();
        var lower = all.Where(n => (n.MeasureIndex ?? 0) >= 4).ToList();
        ShiftToZero(lower);

        WarmLayout(drawable, upper.Concat(lower).ToList());
        var header = ComputeHeader(drawable);
        float upperUsable = CanvasW - 2f - header.LeftMargin - 36f;
        float lowerUsable = CanvasW - 2f - header.LeftMargin - 36f;

        var upperPlan = Plan(drawable, upper, upperUsable, header.LeftMargin);
        double upperBeats = upper.Sum(n => n.BeatDuration);
        double lowerBeats = lower.Sum(n => n.BeatDuration);
        float lowerPlanW = StaffDrawable.ComputeMatchedStaffUsableWidth(
            upperUsable, upperBeats, lowerBeats, lowerUsable);
        Assert.True(StaffDrawable.IsShortLowerStaff(upperBeats, lowerBeats));
        Assert.True(lowerPlanW <= lowerUsable * 0.58f + 1f);

        var lowerPlan = Plan(drawable, lower, lowerPlanW, header.LeftMargin);
        Assert.True(SmallestInkGap(drawable, upper, upperPlan) + 0.05f >= MinInkGap);
        Assert.True(SmallestInkGap(drawable, lower, lowerPlan) + 0.05f >= MinInkGap);
    }

    [Fact]
    public void Seed47_NoEventLoss_AndReadableGapsOnPlacedStaffs()
    {
        AssertDensePage("D", "Major", 45, 8, NoteDuration.Sixteenth, 10, "4/4", seed: 47);
    }

    private static void AssertDensePage(
        string key, string scale, int level, int measures, NoteDuration smallest,
        int accPct, string meter, int seed)
    {
        var timeSig = TimeSignature.FromDisplayString(meter);
        var gen = new MusicSequenceGenerator
        {
            Key = key,
            Scale = scale,
            LowestNote = "A3",
            HighestNote = "E5",
            TimeSignature = timeSig,
            MeasureCount = measures,
            RhythmVarietyPercent = 60,
            SmallestDuration = smallest,
            RestChancePercent = 18,
            AccidentalPercent = accPct,
            SyncopationLevel = SyncopationLevel.None,
            MaxMelodicIntervalSemitones = 4,
            UseScaleOrder = false,
            UseMotifPhrases = true,
            ChildLevel = level,
            RandomSeed = seed,
        };
        var page = gen.GenerateSequence();
        var flat = MusicSequenceGenerator.Flatten(page);
        double beats = timeSig.TotalBeats;
        var bars = BarsFor(measures, beats);

        // Duration invariant per measure.
        foreach (var m in page)
        {
            double sum = m.GeneratedNotes.Sum(n => n.Duration.ToBeatValue());
            Assert.True(Math.Abs(sum - beats) < 1e-6, $"measure duration {sum} != {beats}");
        }

        var session = Session(key, scale, level, showBoth: true);
        session.MeterTimeSignature = meter;
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        var split = drawable.SplitMeasuresAcrossStaves(flat, bars, CanvasW, CanvasH);

        Assert.Equal(measures, split.TotalMeasureCount);
        Assert.Equal(
            split.UpperMeasureCount + split.LowerMeasureCount + split.UnplacedMeasureCount,
            split.TotalMeasureCount);
        Assert.Equal(
            flat.Count,
            split.UpperNotes.Count + split.LowerNotes.Count + split.UnplacedNotes.Count);

        if (split.UpperNotes.Count >= 2 || split.LowerNotes.Count >= 2)
        {
            // Match draw: layout metrics from both staves together.
            WarmLayout(drawable, split.UpperNotes.Concat(split.LowerNotes).ToList());
            if (split.UpperNotes.Count >= 2)
                AssertStaffGaps(drawable, session, split.UpperNotes, isLower: false, rewarm: false);
            if (split.LowerNotes.Count >= 2)
                AssertStaffGaps(drawable, session, split.LowerNotes, isLower: true, rewarm: false,
                    peerBeats: split.UpperNotes.Sum(n => n.BeatDuration));
        }
    }

    private static void AssertStaffGaps(
        StaffDrawable drawable, NoteSessionService session, List<GeneratedNote> notes,
        bool isLower, bool rewarm = true, double peerBeats = 0)
    {
        if (rewarm)
            WarmLayout(drawable, notes);
        var header = ComputeHeader(drawable);
        float margin = isLower
            ? StaffDrawable.ResolveLowerStaffHeaderMargin(header.LeftMargin, header.ClefOnly, session.ShowSignaturesOnBothStaffs)
            : header.LeftMargin;
        float usable = CanvasW - 2f - margin - 36f;
        float planW = usable;
        if (isLower && peerBeats > 1e-6)
        {
            double self = notes.Sum(n => n.BeatDuration);
            planW = StaffDrawable.ComputeMatchedStaffUsableWidth(usable, peerBeats, self, usable);
        }

        var layouts = Plan(drawable, notes, planW, margin);
        float gap = SmallestInkGap(drawable, notes, layouts);
        Assert.True(gap + 0.05f >= MinInkGap,
            $"Staff gap {gap:F1} < MinInkGap on {(isLower ? "lower" : "upper")}");
    }

    private static List<GeneratedNote> SmokingGunMeasure()
        => new()
        {
            GeneratedNote.Rest(NoteDuration.Quarter, 0, 0),
            Note(0, 1.0, 71, 'B', NoteDuration.Sixteenth),
            Note(0, 1.25, 72, 'C', NoteDuration.Sixteenth, Accidental.Sharp),
            Note(0, 1.5, 74, 'D', NoteDuration.Sixteenth),
            Note(0, 1.75, 76, 'E', NoteDuration.Sixteenth),
            GeneratedNote.Rest(NoteDuration.Half, 0, 2.0),
        };

    private static GeneratedNote Note(int m, double beat, int midi, char letter, NoteDuration dur, Accidental acc = Accidental.None)
        => new()
        {
            MidiNumber = midi,
            Letter = letter,
            Octave = midi / 12 - 1,
            Accidental = acc,
            SpelledName = acc == Accidental.Sharp ? $"{letter}#" : $"{letter}",
            Duration = dur,
            MeasureIndex = m,
            BeatPosition = beat,
        };

    private static List<GeneratedNote> MakeQuarters(int measureCount)
    {
        var notes = new List<GeneratedNote>();
        int midi = 60;
        for (int m = 0; m < measureCount; m++)
            for (int b = 0; b < 4; b++)
            {
                notes.Add(Note(m, m * 4.0 + b, midi, 'C', NoteDuration.Quarter));
                midi++;
            }
        return notes;
    }

    private static List<double> BarsFor(int measureCount, double beatsPerMeasure)
    {
        var bars = new List<double>();
        for (double b = beatsPerMeasure; b < measureCount * beatsPerMeasure - 1e-6; b += beatsPerMeasure)
            bars.Add(b);
        return bars;
    }

    private static NoteSessionService Session(string key, string scale, int level, bool showBoth)
        => new()
        {
            Key = key,
            SelectedScale = scale,
            MeterTimeSignature = "4/4",
            ChildLevel = level,
            ShowSignaturesOnBothStaffs = showBoth,
            Tune = "Selected Scale",
        };

    private static void WarmLayout(StaffDrawable drawable, List<GeneratedNote> notes)
    {
        typeof(StaffDrawable)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(m => m.Name == "ComputeLayout" && m.GetParameters().Length == 3)
            .Invoke(drawable, new object[]
            {
                CanvasH,
                (IReadOnlyList<GeneratedNote>)notes,
                (IReadOnlyList<GeneratedNote>)Array.Empty<GeneratedNote>(),
            });
        typeof(StaffDrawable)
            .GetMethod("ComputeHeaderMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { 0f });
        var planInk = typeof(StaffDrawable).GetField("_planInkGap", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var session = (NoteSessionService)typeof(StaffDrawable)
            .GetField("_session", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(drawable)!;
        planInk.SetValue(drawable, session.ChildLevel > 0 && session.ChildLevel <= 30 ? 12f : 8f);
    }

    private static (float LeftMargin, float ClefOnly) ComputeHeader(StaffDrawable drawable)
    {
        var metrics = typeof(StaffDrawable)
            .GetMethod("ComputeHeaderMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { 0f })!;
        return (
            (float)metrics.GetType().GetProperty("LeftMargin")!.GetValue(metrics)!,
            (float)metrics.GetType().GetProperty("ClefOnlyLeftMargin")!.GetValue(metrics)!);
    }

    private static float ComputeMinWidth(StaffDrawable drawable, List<GeneratedNote> notes)
    {
        var segs = BuildSegments(drawable, notes, new List<double>());
        Assert.Single(segs);
        return (float)typeof(StaffDrawable)
            .GetMethod("ComputeMeasureMinWidth", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { notes, segs[0], 0.0 })!;
    }

    private static Array Plan(StaffDrawable drawable, List<GeneratedNote> notes, float avail, float staffMargin)
    {
        double origin = notes.Min(n => n.BeatPosition ?? 0);
        double end = notes.Max(n => (n.BeatPosition ?? 0) + n.BeatDuration);
        var bars = new List<double>();
        for (double b = origin + 4; b < end - 1e-6; b += 4) bars.Add(b);
        // Prefer meter from span when not 4/4-only
        if (bars.Count == 0 && end - origin > 3.5)
            bars = new List<double>();

        object plan = typeof(StaffDrawable)
            .GetMethod("PlanHorizontalLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { notes, bars, avail, staffMargin, true, true, true })!;
        return (Array)plan.GetType().GetField("Item1")!.GetValue(plan)!;
    }

    private static float SmallestInkGap(StaffDrawable drawable, List<GeneratedNote> notes, Array layouts)
    {
        var groupLeft = typeof(StaffDrawable).GetMethod("NoteGroupLeftFromLayout", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var trail = typeof(StaffDrawable).GetMethod(
            "NoteTrailingRight", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(GeneratedNote), typeof(float) }, null)!;
        float minGap = float.PositiveInfinity;
        var order = Enumerable.Range(0, notes.Count)
            .OrderBy(i => notes[i].BeatPosition ?? 0).ThenBy(i => i).ToList();
        for (int oi = 1; oi < order.Count; oi++)
        {
            int p = order[oi - 1], c = order[oi];
            object layP = layouts.GetValue(p)!;
            object layC = layouts.GetValue(c)!;
            float xP = (float)layP.GetType().GetField("X")!.GetValue(layP)!;
            float right = (float)trail.Invoke(drawable, new object[] { notes[p], xP })!;
            float left = (float)groupLeft.Invoke(drawable, new object[] { layC })!;
            minGap = Math.Min(minGap, left - right);
        }
        return minGap;
    }

    private static List<object> BuildSegments(StaffDrawable drawable, List<GeneratedNote> notes, List<double> bars)
    {
        var getOrigin = typeof(StaffDrawable).GetMethod("GetStaffBeatOrigin", BindingFlags.Static | BindingFlags.NonPublic)!;
        double origin = (double)getOrigin.Invoke(null, new object[] { notes, (IReadOnlyList<double>)bars })!;
        var resolve = typeof(StaffDrawable).GetMethod("ResolveStaffBarBeats", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var barList = (List<double>)resolve.Invoke(drawable, new object[] { notes, bars.ToList(), origin })!;
        double total = 0;
        foreach (var n in notes)
            total = Math.Max(total, (n.BeatPosition ?? 0) - origin + n.BeatDuration);
        var sorted = barList.Select(b => b - origin).OrderBy(b => b).ToList();
        var build = typeof(StaffDrawable).GetMethod("BuildMeasureSegments", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return ((System.Collections.IEnumerable)build.Invoke(drawable, new object[] { notes, sorted, origin, total })!)
            .Cast<object>().ToList();
    }

    private static void ShiftToZero(List<GeneratedNote> notes)
    {
        if (notes.Count == 0) return;
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
}
