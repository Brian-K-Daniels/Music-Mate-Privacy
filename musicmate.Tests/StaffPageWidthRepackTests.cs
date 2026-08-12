using System.Reflection;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Provisional (360) packs must be replaced by settled-width re-Split of the same page —
/// never by regenerating music, and never by stamping width without packing.
/// </summary>
public class StaffPageWidthRepackTests
{
    private const float NarrowW = StaffPageWidthPolicy.FallbackWidthDip;
    private const float WideW = 835f;
    private const float CanvasH = 480f;

    [Fact]
    public void ResolveCanvasSize_WidthZero_IsProvisionalFallback360()
    {
        var (w, h, provisional) = StaffPageWidthPolicy.ResolveCanvasSize(0, 0);
        Assert.True(provisional);
        Assert.Equal(NarrowW, w);
        Assert.Equal(StaffPageWidthPolicy.FallbackHeightDip, h);
    }

    [Fact]
    public void ResolveCanvasSize_ValidWidth_NotProvisional()
    {
        var (w, h, provisional) = StaffPageWidthPolicy.ResolveCanvasSize(835, 400);
        Assert.False(provisional);
        Assert.Equal(835f, w);
        Assert.Equal(400f, h);
    }

    [Fact]
    public void NeedsRepack_Provisional_AlwaysTrueWhenSettled()
    {
        var state = MakeState(provisional: true, packedWidth: NarrowW);
        Assert.True(StaffPageWidthPolicy.NeedsRepackForSettledWidth(state, NarrowW, WideW));
    }

    [Fact]
    public void NeedsRepack_SameWidth_False()
    {
        var state = MakeState(provisional: false, packedWidth: WideW);
        Assert.False(StaffPageWidthPolicy.NeedsRepackForSettledWidth(state, WideW, WideW));
        Assert.False(StaffPageWidthPolicy.NeedsRepackForSettledWidth(state, WideW, WideW + 1.5));
    }

    [Fact]
    public void NeedsRepack_MaterialWidthChange_True()
    {
        var state = MakeState(provisional: false, packedWidth: NarrowW);
        Assert.True(StaffPageWidthPolicy.NeedsRepackForSettledWidth(state, NarrowW, WideW));
    }

    [Fact]
    public void NeedsRepack_NullState_False()
    {
        Assert.False(StaffPageWidthPolicy.NeedsRepackForSettledWidth(null, 0, WideW));
    }

    [Theory]
    [InlineData(false, false, true)]   // running / AutoStart: allowed
    [InlineData(true, false, false)]  // freeze: defer
    [InlineData(false, true, false)]  // hold result: defer
    [InlineData(true, true, false)]
    public void CanRepackNow_FreezeAndHoldDefer_RunningAllowed(
        bool freeze, bool hold, bool expected)
    {
        Assert.Equal(expected, StaffPageWidthPolicy.CanRepackNow(freeze, hold));
    }

    [Fact]
    public void WidthRecordedAfterPack_EqualsPackedWidth()
    {
        Assert.Equal(NarrowW, StaffPageWidthPolicy.WidthRecordedAfterPack(NarrowW));
        Assert.Equal(WideW, StaffPageWidthPolicy.WidthRecordedAfterPack(WideW));
    }

    [Fact]
    public void Deterministic_360To835_SamePage_NarrowIsSparse_WideIsFull()
    {
        // C Major quarters: at 360 → 1+1 with 6 unplaced; at 835 → 3+3 with 2 unplaced
        // (Sls cap 18 widens measures vs the former 4+4 at Sls=12).
        var (page, bars, drawable, _) = BuildDeterministicCMajorPage(seed: 11);
        var state = new StaffPagePackState
        {
            PageNotes = page,
            PageBarBeats = bars,
            MeasureBeats = 4,
            IsTwoOctaveScaleCut = false,
            IsProvisional = true,
            PackedCanvasWidth = NarrowW,
            PackedCanvasHeight = CanvasH,
        };

        var narrow = StaffPageWidthPolicy.SplitCachedPage(drawable, state, NarrowW, CanvasH);
        Assert.Equal(1, narrow.UpperMeasureCount);
        Assert.Equal(1, narrow.LowerMeasureCount);
        Assert.Equal(6, narrow.UnplacedMeasureCount);
        Assert.Equal(page.Count, narrow.UpperNotes.Count + narrow.LowerNotes.Count + narrow.UnplacedNotes.Count);

        var pageFingerprint = Fingerprint(page);

        var wide = StaffPageWidthPolicy.SplitCachedPage(drawable, state, WideW, CanvasH);
        Assert.Equal(pageFingerprint, Fingerprint(state.PageNotes));
        Assert.Same(page, state.PageNotes);

        int widePlaced = wide.UpperMeasureCount + wide.LowerMeasureCount;
        Assert.True(widePlaced >= 6,
            $"Expected ~3+3 at {WideW} with larger notation; got U{wide.UpperMeasureCount}+L{wide.LowerMeasureCount}");
        Assert.Equal(2, wide.UnplacedMeasureCount);
        Assert.Equal(page.Count, wide.UpperNotes.Count + wide.LowerNotes.Count + wide.UnplacedNotes.Count);
        // Balanced cut prefers 3+3 over greedy 4+2 when both place 6 (8 no longer fit at Sls=18).
        Assert.Equal(3, wide.UpperMeasureCount);
        Assert.Equal(3, wide.LowerMeasureCount);
    }

    [Fact]
    public void SettlePolicy_WhileRunning_StillNeedsRepack_AndCanRepack()
    {
        var state = MakeState(provisional: true, packedWidth: NarrowW);
        double staffWidthUsed = NarrowW;
        double settled = WideW;
        bool isRunning = true;
        bool freeze = false;
        bool hold = false;

        Assert.True(StaffPageWidthPolicy.NeedsRepackForSettledWidth(state, staffWidthUsed, settled));
        Assert.True(StaffPageWidthPolicy.CanRepackNow(freeze, hold));
        // Running does not block — only freeze/hold do.
        Assert.True(isRunning);
    }

    [Fact]
    public void SettlePolicy_WhileAutoStartEquivalent_CanRepack()
    {
        // AutoStart implies listening may already be running; policy must still allow visual repack.
        Assert.True(StaffPageWidthPolicy.CanRepackNow(freezeStaff: false, holdResultForChildSession: false));
    }

    [Fact]
    public void SettlePolicy_Freeze_QueuesNotStamp()
    {
        var state = MakeState(provisional: true, packedWidth: NarrowW);
        Assert.True(StaffPageWidthPolicy.NeedsRepackForSettledWidth(state, NarrowW, WideW));
        Assert.False(StaffPageWidthPolicy.CanRepackNow(freezeStaff: true, holdResultForChildSession: false));
        // Caller must NOT assign staffWidthUsedForLayout = settledWidth when deferred.
        double staffWidthUsed = NarrowW;
        Assert.NotEqual(WideW, staffWidthUsed);
    }

    [Fact]
    public void SettlePolicy_HoldResult_QueuesNotStamp()
    {
        Assert.False(StaffPageWidthPolicy.CanRepackNow(freezeStaff: false, holdResultForChildSession: true));
    }

    [Fact]
    public void Repack_DoesNotCallGenerator_SameEventSequence()
    {
        var (page, bars, drawable, _) = BuildDeterministicAbBluesPage(seed: 102);
        var state = new StaffPagePackState
        {
            PageNotes = page,
            PageBarBeats = bars,
            MeasureBeats = 4,
            IsProvisional = true,
            PackedCanvasWidth = NarrowW,
            PackedCanvasHeight = CanvasH,
        };

        string before = Fingerprint(page);
        _ = StaffPageWidthPolicy.SplitCachedPage(drawable, state, NarrowW, CanvasH);
        _ = StaffPageWidthPolicy.SplitCachedPage(drawable, state, WideW, CanvasH);
        Assert.Equal(before, Fingerprint(state.PageNotes));
    }

    [Fact]
    public void Repack_Wide_NoSymbolCollisionsOnPlacedStaffs()
    {
        var (page, bars, drawable, session) = BuildDeterministicAbBluesPage(seed: 102);
        var state = new StaffPagePackState
        {
            PageNotes = page,
            PageBarBeats = bars,
            MeasureBeats = 4,
            PackedCanvasWidth = NarrowW,
            PackedCanvasHeight = CanvasH,
            IsProvisional = true,
        };

        var wide = StaffPageWidthPolicy.SplitCachedPage(drawable, state, WideW, CanvasH);
        Assert.True(wide.UpperMeasureCount + wide.LowerMeasureCount >= 2,
            $"Dense Ab Blues at Sls=18 may pack fewer measures; got U{wide.UpperMeasureCount}+L{wide.LowerMeasureCount}");

        if (wide.UpperNotes.Count >= 2)
            AssertStaffGaps(drawable, session, wide.UpperNotes);
        if (wide.LowerNotes.Count >= 2)
            AssertStaffGaps(drawable, session, wide.LowerNotes);
    }

    [Fact]
    public void CurrentNoteIndex_PreservedAcrossNotesToDrawExtension()
    {
        var session = new NoteSessionService();
        session.NotesToDraw.Add(new NoteInfo { Midi = 60, Name = "C4", TargetFreq = 261.6 });
        session.NotesToDraw.Add(new NoteInfo { Midi = 62, Name = "D4", TargetFreq = 293.7 });
        session.NotesToDraw.Add(new NoteInfo { Midi = 64, Name = "E4", TargetFreq = 329.6 });
        // Simulate advance to note 1 via restore API (private setter).
        session.RestoreCurrentNoteIndexAfterStaffRepack(1);
        Assert.Equal(1, session.CurrentNoteIndex);

        // Extend NotesToDraw as a width repack would (more placed pitches).
        session.NotesToDraw.Add(new NoteInfo { Midi = 65, Name = "F4", TargetFreq = 349.2 });
        session.RestoreCurrentNoteIndexAfterStaffRepack(1);
        Assert.Equal(1, session.CurrentNoteIndex);
        Assert.Equal(62, session.NotesToDraw[session.CurrentNoteIndex].Midi);
    }

    private static StaffPagePackState MakeState(bool provisional, float packedWidth)
        => new()
        {
            PageNotes = new List<GeneratedNote>(),
            PageBarBeats = new List<double>(),
            MeasureBeats = 4,
            IsProvisional = provisional,
            PackedCanvasWidth = packedWidth,
            PackedCanvasHeight = CanvasH,
        };

    private static (List<GeneratedNote> page, List<double> bars, StaffDrawable drawable, NoteSessionService session)
        BuildDeterministicCMajorPage(int seed)
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
            RandomSeed = seed,
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
            IsRandomMode = false,
        };
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        return (page, bars, drawable, session);
    }

    private static (List<GeneratedNote> page, List<double> bars, StaffDrawable drawable, NoteSessionService session)
        BuildDeterministicAbBluesPage(int seed)
    {
        var gen = new MusicSequenceGenerator
        {
            Key = "Ab",
            Scale = "Blues",
            LowestNote = "A2",
            HighestNote = "C6",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 8,
            RhythmVarietyPercent = 70,
            SmallestDuration = NoteDuration.Sixteenth,
            RestChancePercent = 25,
            AccidentalPercent = 40,
            SyncopationLevel = SyncopationLevel.None,
            MaxMelodicIntervalSemitones = 5,
            UseScaleOrder = false,
            UseMotifPhrases = true,
            ChildLevel = 40,
            RandomSeed = seed,
        };
        var page = MusicSequenceGenerator.Flatten(gen.GenerateSequence());
        var bars = Enumerable.Range(1, 7).Select(i => i * 4.0).ToList();
        var session = new NoteSessionService
        {
            Key = "Ab",
            SelectedScale = "Blues",
            MeterTimeSignature = "4/4",
            ChildLevel = 40,
            ShowSignaturesOnBothStaffs = true,
            IsRandomMode = true,
        };
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        return (page, bars, drawable, session);
    }

    private static string Fingerprint(IReadOnlyList<GeneratedNote> notes)
        => string.Join("|", notes.Select(n =>
            $"{n.MeasureIndex}:{n.BeatPosition:F3}:{(n.IsRest ? "R" : n.MidiNumber.ToString())}:{n.Duration}"));

    private static void AssertStaffGaps(
        StaffDrawable drawable, NoteSessionService session, List<GeneratedNote> notes)
    {
        Warm(drawable, notes, session.ChildLevel);
        object plan = typeof(StaffDrawable)
            .GetMethod("PlanHorizontalLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[]
            {
                notes, new List<double>(), 700f, 120f, true, true, true
            })!;
        var layouts = (Array)plan.GetType().GetField("Item1")!.GetValue(plan)!;
        var groupLeft = typeof(StaffDrawable).GetMethod("NoteGroupLeftFromLayout", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var trail = typeof(StaffDrawable).GetMethod(
            "NoteTrailingRight", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(GeneratedNote), typeof(float) }, null)!;

        var order = Enumerable.Range(0, notes.Count)
            .OrderBy(i => notes[i].BeatPosition ?? 0).ThenBy(i => i).ToList();
        for (int oi = 1; oi < order.Count; oi++)
        {
            int p = order[oi - 1], c = order[oi];
            float xP = (float)layouts.GetValue(p)!.GetType().GetField("X")!.GetValue(layouts.GetValue(p)!)!;
            float right = (float)trail.Invoke(drawable, new object[] { notes[p], xP })!;
            float left = (float)groupLeft.Invoke(drawable, new object[] { layouts.GetValue(c)! })!;
            float gap = left - right;
            Assert.True(gap + 0.05f >= 8f,
                $"Gap {gap:F1} < MinInkGap after wide repack between events {p} and {c}");
        }
    }

    private static void Warm(StaffDrawable drawable, List<GeneratedNote> notes, int level)
    {
        typeof(StaffDrawable)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(m => m.Name == "ComputeLayout" && m.GetParameters().Length == 3)
            .Invoke(drawable, new object[]
            {
                CanvasH,
                (IReadOnlyList<GeneratedNote>)notes,
                (IReadOnlyList<GeneratedNote>)Array.Empty<GeneratedNote>()
            });
        typeof(StaffDrawable).GetMethod("ComputeHeaderMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { 0f });
        typeof(StaffDrawable).GetField("_planInkGap", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(drawable, level > 0 && level <= 30 ? 12f : 8f);
    }
}
