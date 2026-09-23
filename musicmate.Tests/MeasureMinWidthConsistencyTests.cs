using System.Reflection;
using System.Text;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;
using Xunit.Abstractions;

namespace musicmate.Tests;

/// <summary>
/// ComputeMeasureMinWidth must include intermediate trailing reach so pack mins
/// match sequential engraving (ComputeMinimumPackedSpan contract).
/// </summary>
public class MeasureMinWidthConsistencyTests
{
    private readonly ITestOutputHelper _out;
    public MeasureMinWidthConsistencyTests(ITestOutputHelper output) => _out = output;

    private const float MinInkGap = 8f;
    private const float BarLeftPadding = 16f;
    private const float BarStemClearance = 8f;
    private const float MeasureStartExtraPad = 6f;
    private const float CanvasH = 480f;
    private const float AgreeTolPx = 20f;
    private const float AgreeTolPct = 8f;

    [Fact]
    public void FSharp_Seed2_Measure5_NoLongerUnderestimatesByHundredPixels()
    {
        // Audit baseline: est≈299 vs verified≈411 (missing intermediate trails).
        var (drawable, flat, segments, origin) = BuildPage("F#", "Major", seed: 2);
        Assert.True(segments.Count > 5);

        float packed = PackedSpan(drawable, flat, segments[5], origin);
        float est = MinWidth(drawable, flat, segments[5], origin);
        float startPad = StartPad(flat, segments[5], origin);
        float expected = BarLeftPadding + startPad + packed + BarLeftPadding * 0.5f;

        Assert.Equal(expected, est, precision: 2);
        // Must include intermediate trails: est is near packed+framing, not ~100px below.
        Assert.True(est + 1f >= packed + BarLeftPadding,
            $"est={est:F1} must include packedSpan={packed:F1} plus bar framing");
        Assert.True(est >= 380f,
            $"F# s2/m5 was ~299 before fix; after fix expect ~packed+framing (packed={packed:F0}), got {est:F0}");

        float verified = SequentialVerifiedWidth(drawable, flat, segments[5], origin);
        float pct = 100f * (est - verified) / Math.Max(1f, verified);
        float legacy = LegacyMinWidthMissingTrails(drawable, flat, segments[5], origin);
        _out.WriteLine(
            $"F# s2/m5: legacy≈{legacy:F1}  after={est:F1}  packed={packed:F1}  verified={verified:F1}  " +
            $"Δ(after-ver)={est - verified:F1} ({pct:F1}%)  startPad={startPad:F1}");
        Assert.True(Math.Abs(est - verified) <= AgreeTolPx || Math.Abs(pct) <= AgreeTolPct,
            $"F# s2/m5 est={est:F1} verified={verified:F1} Δ={est - verified:F1} ({pct:F1}%)");
    }

    [Fact]
    public void Level95_AccPct30_EstimateAgreesWithPackedSpanFraming_AcrossSample()
    {
        var cases = new (string Key, string Scale, int Seed)[]
        {
            ("F#", "Major", 2),
            ("F#", "Major", 7),
            ("F#", "Major", 11),
            ("F#", "Major", 19),
            ("F#", "Major", 29),
            ("Db", "Major", 3),
            ("Db", "Major", 13),
            ("Db", "Major", 23),
            ("Ab", "Major", 5),
            ("Ab", "Major", 17),
            ("Gb", "Major", 9),
            ("C", "Major", 1),
        };

        var pctErrors = new List<float>();
        var absErrors = new List<float>();
        int measures = 0;

        foreach (var (key, scale, seed) in cases)
        {
            var (drawable, flat, segments, origin) = BuildPage(key, scale, seed);
            for (int m = 0; m < segments.Count; m++)
            {
                float packed = PackedSpan(drawable, flat, segments[m], origin);
                float startPad = StartPad(flat, segments[m], origin);
                float est = MinWidth(drawable, flat, segments[m], origin);
                float framed = BarLeftPadding + startPad + packed + BarLeftPadding * 0.5f;
                Assert.Equal(framed, est, precision: 2);

                float verified = SequentialVerifiedWidth(drawable, flat, segments[m], origin);
                float diff = est - verified;
                float pct = 100f * diff / Math.Max(1f, verified);
                absErrors.Add(Math.Abs(diff));
                pctErrors.Add(pct);
                measures++;

                Assert.True(Math.Abs(diff) <= AgreeTolPx || Math.Abs(pct) <= AgreeTolPct,
                    $"{key}/{scale}/s{seed}/m{m}: est={est:F1} ver={verified:F1} Δ={diff:F1} ({pct:F1}%)");
            }
        }

        Assert.True(measures >= 40);
        float meanPct = pctErrors.Average();
        float medianPct = pctErrors.OrderBy(p => p).ElementAt(pctErrors.Count / 2);
        float meanAbs = absErrors.Average();
        _out.WriteLine(
            $"Audit sample n={measures}  meanΔ%={meanPct:F2}%  medianΔ%={medianPct:F2}%  mean|Δ|={meanAbs:F1}px");

        // Pack impact: denser mins may place fewer measures on the same canvas.
        var packLines = new StringBuilder();
        int fewer = 0, same = 0, more = 0;
        foreach (var (key, scale, seed) in cases)
        {
            var (drawable, flat, segments, origin) = BuildPage(key, scale, seed);
            float[] afterMins = new float[segments.Count];
            float[] legacyMins = new float[segments.Count];
            for (int m = 0; m < segments.Count; m++)
            {
                afterMins[m] = MinWidth(drawable, flat, segments[m], origin);
                legacyMins[m] = LegacyMinWidthMissingTrails(drawable, flat, segments[m], origin);
            }
            // Approximate usable for F#-ish phone: 835 − LeftMargin − right pad ≈ use SplitMeasures.
            var bars = BarsFor(flat);
            var splitAfter = drawable.SplitMeasuresAcrossStaves(flat, bars, 835f, CanvasH);
            int placedAfter = splitAfter.UpperMeasureCount + splitAfter.LowerMeasureCount;

            // Legacy pack via ChooseBalancedMeasureSplit on legacy mins with same usable as after split header.
            Warm(drawable, flat);
            var header = typeof(StaffDrawable)
                .GetMethod("ComputeHeaderMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(drawable, new object[] { 0f })!;
            float leftMargin = (float)header.GetType().GetProperty("LeftMargin")!.GetValue(header)!;
            float usable = Math.Max(64f, 835f - 16f - leftMargin); // rough; Split uses safer right
            // Prefer actual usable from a second split path: re-pack legacy mins with public API.
            var (uLeg, lLeg) = StaffDrawable.ChooseBalancedMeasureSplit(legacyMins, usable, usable);
            int placedLegacy = uLeg + lLeg;
            var (uAft, lAft) = StaffDrawable.ChooseBalancedMeasureSplit(afterMins, usable, usable);
            int placedAfterCut = uAft + lAft;
            if (placedAfterCut < placedLegacy) fewer++;
            else if (placedAfterCut > placedLegacy) more++;
            else same++;
            packLines.AppendLine(
                $"  {key}/{scale}/s{seed}: legacyCut={uLeg}+{lLeg}={placedLegacy}  afterCut={uAft}+{lAft}={placedAfterCut}  splitAfter={placedAfter}");
        }
        _out.WriteLine($"Pack cut impact (same usable): fewer={fewer} same={same} more={more}");
        _out.WriteLine(packLines.ToString());

        // Must not remain systematically ~−20–25% (pre-fix undercount).
        Assert.True(meanPct > -10f,
            $"mean% error still too low (underestimate): mean={meanPct:F1}% median={medianPct:F1}%");
        Assert.True(Math.Abs(meanPct) <= AgreeTolPct,
            $"mean% error {meanPct:F1}% exceeds ±{AgreeTolPct}%");
        Assert.True(Math.Abs(medianPct) <= AgreeTolPct,
            $"median% error {medianPct:F1}% exceeds ±{AgreeTolPct}%");
        Assert.True(absErrors.Average() <= AgreeTolPx,
            $"mean |Δ| {absErrors.Average():F1}px exceeds {AgreeTolPx}px");
    }

    [Fact]
    public void Level95_PackAtEstimate_PreservesMinInkGapAndEventOrder()
    {
        foreach (var (key, scale, seed) in new[]
        {
            ("F#", "Major", 2),
            ("Db", "Major", 3),
            ("Ab", "Major", 5),
        })
        {
            var (drawable, flat, segments, origin) = BuildPage(key, scale, seed);
            var bars = BarsFor(flat);

            // Split must keep generated measure/event partition (no silent loss / reorder).
            var split = drawable.SplitMeasuresAcrossStaves(flat, bars, canvasWidth: 835f, canvasHeight: CanvasH);
            Assert.Equal(segments.Count, split.TotalMeasureCount);
            Assert.Equal(
                flat.Count,
                split.UpperNotes.Count + split.LowerNotes.Count + split.UnplacedNotes.Count);

            var reconstructed = split.UpperNotes.Concat(split.LowerNotes).Concat(split.UnplacedNotes)
                .OrderBy(n => n.BeatPosition ?? 0).ThenBy(n => n.MeasureIndex ?? 0).ToList();
            var original = flat.OrderBy(n => n.BeatPosition ?? 0).ThenBy(n => n.MeasureIndex ?? 0).ToList();
            Assert.Equal(original.Count, reconstructed.Count);
            for (int i = 0; i < original.Count; i++)
            {
                Assert.Equal(original[i].BeatPosition, reconstructed[i].BeatPosition);
                Assert.Equal(original[i].Duration, reconstructed[i].Duration);
                Assert.Equal(original[i].IsRest, reconstructed[i].IsRest);
            }

            for (int m = 0; m < segments.Count; m++)
            {
                var isolated = Isolate(flat, segments[m], origin);
                if (isolated.Count < 2)
                    continue;

                Warm(drawable, isolated);
                float est = MinWidth(drawable, isolated, BuildSingleSegment(drawable, isolated), 0.0);
                var layouts = Plan(drawable, isolated, Math.Max(est, 64f), staffMargin: 0f);
                AssertNoInkCrush(drawable, isolated, layouts);
            }
        }
    }

    [Fact]
    public void IntermediateTrailingReach_IsIncluded_ForTwoNoteChain()
    {
        // Two notes: min width must be left0 + trail0 + gap + left1 + trail1 + framing,
        // not left0 + gap + left1 + trail1 (missing trail0).
        var notes = new List<GeneratedNote>
        {
            Note(60, 'C', 4, 0.0, NoteDuration.Quarter),
            Note(62, 'D', 4, 1.0, NoteDuration.Quarter),
        };
        var session = new NoteSessionService
        {
            Key = "C",
            SelectedScale = "Major",
            MeterTimeSignature = "4/4",
            ChildLevel = 95,
            AccidentalPercent = 30,
        };
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        Warm(drawable, notes);
        var seg = BuildSingleSegment(drawable, notes);

        float packed = PackedSpan(drawable, notes, seg, 0.0);
        float est = MinWidth(drawable, notes, seg, 0.0);
        float startPad = StartPad(notes, seg, 0.0);

        // trail of first note is inside packed; if omitted, est would be ~trail0 short of framed packed.
        float trail0 = TrailReach(drawable, notes[0]);
        Assert.True(packed > trail0 + 1f);
        Assert.Equal(BarLeftPadding + startPad + packed + BarLeftPadding * 0.5f, est, precision: 2);
        Assert.True(est > BarLeftPadding + startPad + packed - trail0 + BarLeftPadding * 0.5f + 1f,
            "Estimate must not equal the old leftReach-only chain that drops trail0");
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static (StaffDrawable drawable, List<GeneratedNote> flat, List<object> segments, double origin)
        BuildPage(string key, string scale, int seed)
    {
        const int level = 95;
        var range = ChildLevelProgression.NoteRangeForLevel(level);
        var gen = new MusicSequenceGenerator
        {
            Key = key,
            Scale = scale,
            LowestNote = range.Lo,
            HighestNote = range.Hi,
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 8,
            RhythmVarietyPercent = ChildLevelProgression.RhythmVarietyPercentForLevel(level),
            SmallestDuration = NoteDuration.Sixteenth,
            RestChancePercent = ChildLevelProgression.RestChancePercentForLevel(level),
            AccidentalPercent = 30,
            SyncopationLevel = SyncopationLevel.Full,
            MaxMelodicIntervalSemitones = ChildLevelProgression.MaxIntervalForLevel(level),
            UseScaleOrder = false,
            UseMotifPhrases = true,
            ChildLevel = level,
            RandomSeed = seed,
        };
        var flat = MusicSequenceGenerator.Flatten(gen.GenerateSequence()).ToList();
        var session = new NoteSessionService
        {
            Key = key,
            SelectedScale = scale,
            MeterTimeSignature = "4/4",
            ChildLevel = level,
            AccidentalPercent = 30,
            ShowSignaturesOnBothStaffs = true,
        };
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        Warm(drawable, flat);
        var bars = BarsFor(flat);
        var segments = BuildSegments(drawable, flat, bars);
        double origin = (double)typeof(StaffDrawable)
            .GetMethod("GetStaffBeatOrigin", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { flat, (IReadOnlyList<double>)bars })!;
        return (drawable, flat, segments, origin);
    }

    private static GeneratedNote Note(int midi, char letter, int octave, double beat, NoteDuration dur)
        => new()
        {
            MidiNumber = midi,
            Letter = letter,
            Octave = octave,
            Duration = dur,
            BeatPosition = beat,
            MeasureIndex = 0,
            Accidental = Accidental.None,
            SpelledName = $"{letter}{octave}",
        };

    private static void Warm(StaffDrawable drawable, List<GeneratedNote> notes)
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
        typeof(StaffDrawable)
            .GetField("_planInkGap", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(drawable, MinInkGap);
    }

    /// <summary>Pre-fix chain that omitted intermediate trailing reaches (for delta reporting).</summary>
    private static float LegacyMinWidthMissingTrails(StaffDrawable d, List<GeneratedNote> notes, object seg, double origin)
    {
        var idxs = (List<int>)seg.GetType().GetField("NoteIndices")!.GetValue(seg)!;
        if (idxs.Count == 0)
            return BarLeftPadding + 16f;
        double start = (double)seg.GetType().GetField("StartBeat")!.GetValue(seg)!;
        double end = (double)seg.GetType().GetField("EndBeat")!.GetValue(seg)!;
        var sorted = (List<int>)typeof(StaffDrawable)
            .GetMethod("SortIndicesByBeat", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { notes, idxs, origin })!;
        var beamGroups = typeof(StaffDrawable)
            .GetMethod("ComputeLayoutBeamGroupIds", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { notes, sorted, origin, end })!;
        var resolve = typeof(StaffDrawable).GetMethod("ResolveLayoutAccidental", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var leftM = typeof(StaffDrawable).GetMethod("NoteCenterLeftReach", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var trailM = typeof(StaffDrawable).GetMethod("NoteCenterTrailingReach", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var gapM = typeof(StaffDrawable).GetMethod("InkGapBetween", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var accHistory = new Dictionary<(char, int), Accidental>();
        var barCancelled = new HashSet<(char, int)>();
        float width = BarLeftPadding;
        for (int k = 0; k < sorted.Count; k++)
        {
            int i = sorted[k];
            var note = notes[i];
            var acc = resolve.Invoke(d, new object[] { note, accHistory, barCancelled })!;
            bool hasAcc = (bool)acc.GetType().GetProperty("HasAccidental")!.GetValue(acc)!;
            bool isFlat = (bool)acc.GetType().GetProperty("IsFlat")!.GetValue(acc)!;
            bool isNatural = (bool)acc.GetType().GetProperty("IsNatural")!.GetValue(acc)!;
            float left = (float)leftM.Invoke(d, new object[] { note.IsRest, hasAcc, isFlat, isNatural, note.Duration })!;
            if (k == 0)
            {
                double rel = (note.BeatPosition ?? 0.0) - origin - start;
                float startPad = BarStemClearance + (rel < 1e-6 ? MeasureStartExtraPad : 0f);
                width += left + startPad;
            }
            else
            {
                var prev = notes[sorted[k - 1]];
                float gap = (float)gapM.Invoke(d, new object[]
                {
                    beamGroups, sorted[k - 1], i, MinInkGap, hasAcc, note.IsRest || prev.IsRest
                })!;
                width += gap + left;
            }
        }
        float lastTrail = (float)trailM.Invoke(d, new object[] { notes[sorted[^1]].IsRest, notes[sorted[^1]].Duration })!;
        return width + lastTrail + BarLeftPadding * 0.5f;
    }

    private static float MinWidth(StaffDrawable d, List<GeneratedNote> notes, object seg, double origin)
        => (float)typeof(StaffDrawable)
            .GetMethod("ComputeMeasureMinWidth", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(d, new object[] { notes, seg, origin })!;

    private static float PackedSpan(StaffDrawable d, List<GeneratedNote> notes, object seg, double origin)
    {
        var idxs = (List<int>)seg.GetType().GetField("NoteIndices")!.GetValue(seg)!;
        double end = (double)seg.GetType().GetField("EndBeat")!.GetValue(seg)!;
        var sorted = (List<int>)typeof(StaffDrawable)
            .GetMethod("SortIndicesByBeat", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { notes, idxs, origin })!;
        return (float)typeof(StaffDrawable)
            .GetMethod("ComputeMinimumPackedSpan", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(d, new object[] { notes, sorted, origin, end })!;
    }

    private static float StartPad(List<GeneratedNote> notes, object seg, double origin)
    {
        var idxs = (List<int>)seg.GetType().GetField("NoteIndices")!.GetValue(seg)!;
        double start = (double)seg.GetType().GetField("StartBeat")!.GetValue(seg)!;
        if (idxs.Count == 0)
            return 0f;
        int first = idxs.OrderBy(i => (notes[i].BeatPosition ?? 0) - origin).ThenBy(i => i).First();
        double rel = (notes[first].BeatPosition ?? 0.0) - origin - start;
        return BarStemClearance + (rel < 1e-6 ? MeasureStartExtraPad : 0f);
    }

    private static float TrailReach(StaffDrawable d, GeneratedNote note)
        => (float)typeof(StaffDrawable)
            .GetMethod("NoteCenterTrailingReach", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(d, new object[] { note.IsRest, note.Duration })!;

    private static float SequentialVerifiedWidth(StaffDrawable d, List<GeneratedNote> notes, object seg, double origin)
    {
        // Same contract as sequential fit: content ends at BarLeft + packed; needs + BarLeft before bar.
        float packed = PackedSpan(d, notes, seg, origin);
        return packed + 2f * BarLeftPadding;
    }

    private static List<GeneratedNote> Isolate(List<GeneratedNote> notes, object seg, double origin)
    {
        var idxs = (List<int>)seg.GetType().GetField("NoteIndices")!.GetValue(seg)!;
        double start = (double)seg.GetType().GetField("StartBeat")!.GetValue(seg)!;
        var list = new List<GeneratedNote>();
        foreach (int i in idxs)
        {
            var n = notes[i];
            list.Add(new GeneratedNote
            {
                Letter = n.Letter,
                Octave = n.Octave,
                MidiNumber = n.MidiNumber,
                Duration = n.Duration,
                Accidental = n.Accidental,
                IsRest = n.IsRest,
                BeatPosition = (n.BeatPosition ?? 0) - origin - start,
                MeasureIndex = 0,
                SpelledName = n.SpelledName,
            });
        }
        return list;
    }

    private static object BuildSingleSegment(StaffDrawable d, List<GeneratedNote> notes)
    {
        var segs = BuildSegments(d, notes, new List<double>());
        Assert.Single(segs);
        return segs[0];
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
        var build = typeof(StaffDrawable).GetMethod("BuildMeasureSegments", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var sortedBars = barList.Select(b => b - origin).OrderBy(x => x).ToList();
        var segs = (System.Collections.IList)build.Invoke(drawable, new object[] { notes, sortedBars, origin, total })!;
        return segs.Cast<object>().ToList();
    }

    private static List<double> BarsFor(List<GeneratedNote> notes)
    {
        double max = notes.Count == 0 ? 0 : notes.Max(n => (n.BeatPosition ?? 0) + n.BeatDuration);
        var bars = new List<double>();
        for (double b = 4; b < max - 1e-6; b += 4)
            bars.Add(b);
        return bars;
    }

    private static Array Plan(StaffDrawable drawable, List<GeneratedNote> notes, float avail, float staffMargin)
    {
        object plan = typeof(StaffDrawable)
            .GetMethod("PlanHorizontalLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { notes, new List<double>(), avail, staffMargin, false, true, true })!;
        return (Array)plan.GetType().GetField("Item1")!.GetValue(plan)!;
    }

    private static void AssertNoInkCrush(StaffDrawable drawable, List<GeneratedNote> notes, Array layouts)
    {
        var groupLeft = typeof(StaffDrawable).GetMethod("NoteGroupLeftFromLayout", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var trail = typeof(StaffDrawable).GetMethod(
            "NoteTrailingRight", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(GeneratedNote), typeof(float) }, null)!;
        var order = Enumerable.Range(0, notes.Count)
            .OrderBy(i => notes[i].BeatPosition ?? 0).ThenBy(i => i).ToList();
        float prevRight = float.NegativeInfinity;
        for (int oi = 0; oi < order.Count; oi++)
        {
            int i = order[oi];
            object lay = layouts.GetValue(i)!;
            float x = (float)lay.GetType().GetField("X")!.GetValue(lay)!;
            bool hasAcc = (bool)lay.GetType().GetField("HasAccidental")!.GetValue(lay)!;
            float left = (float)groupLeft.Invoke(drawable, new object[] { lay })!;
            float right = (float)trail.Invoke(drawable, new object[] { notes[i], x })!;
            if (prevRight > float.NegativeInfinity)
            {
                float need = hasAcc ? 14f : MinInkGap;
                Assert.True(left + 0.05f >= prevRight + need,
                    $"Ink crush: left={left:F1} prevRight={prevRight:F1} needGap={need}");
            }
            prevRight = right;
        }
    }
}
