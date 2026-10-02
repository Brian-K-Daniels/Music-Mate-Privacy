using System.Reflection;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Notes drawn between two bar lines must be exactly one measure of rhythm.
/// </summary>
public class RenderedMeasureBoundaryTests
{
    [Theory]
    [InlineData(43)]
    [InlineData(100)]
    public void DenseStaff_BarLinesMatchMeasureDurations(int level)
    {
        for (int seed = 0; seed < 25; seed++)
            AssertOneStaff(level, seed, canvasW: 220f);
    }

    private void AssertOneStaff(int level, int seed, float canvasW)
    {
        var range = ChildLevelProgression.NoteRangeForLevel(level);
        var smallest = ChildLevelProgression.SmallestNoteForLevel(level) switch
        {
            "Eighth" => NoteDuration.Eighth,
            "Sixteenth" => NoteDuration.Sixteenth,
            _ => NoteDuration.Quarter,
        };
        var gen = new MusicSequenceGenerator
        {
            Key = "C",
            Scale = "Natural Minor",
            LowestNote = range.Lo,
            HighestNote = range.Hi,
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 8,
            RhythmVarietyPercent = 100,
            SmallestDuration = smallest,
            RestChancePercent = ChildLevelProgression.RestChancePercentForLevel(level),
            SyncopationLevel = SyncopationLevelHelper.Parse(ChildLevelProgression.SyncopationForLevel(level)),
            UseMotifPhrases = true,
            UseScaleOrder = false,
            AccidentalPercent = 80,
            MaxMelodicIntervalSemitones = ChildLevelProgression.MaxIntervalForLevel(level),
            ChildLevel = level,
            RandomSeed = seed,
        };
        var notes = MusicSequenceGenerator.Flatten(gen.GenerateSequence());
        var session = new NoteSessionService
        {
            Instrument = "concert-pitch",
            Key = "C",
            Tune = "Practice Tune",
            ChildLevel = level,
            LowestNote = range.Lo,
            HighestNote = range.Hi,
            MeterTimeSignature = "4/4",
        };
        // Practice Tune display meter follows CurrentTune; keep generated meter on the session.
        session.Tune = "Selected Scale";

        var (drawable, noteLayouts, barLayouts) = Plan(session, notes, canvasW);
        AssertBarIntervalsSumToMeter(notes, noteLayouts, barLayouts, 4.0, level, seed);
        AssertNoNoteheadStraddlesBar(drawable, noteLayouts, barLayouts, level, seed);
    }

    private static void AssertNoNoteheadStraddlesBar(
        StaffDrawable drawable, Array noteLayouts, Array barLayouts, int level, int seed)
    {
        var layout = typeof(StaffDrawable)
            .GetField("_layout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(drawable)!;
        float headR = (float)layout.GetType().GetField("NoteHeadR")!.GetValue(layout)!;
        var bars = new List<float>();
        for (int i = 0; i < barLayouts.Length; i++)
            bars.Add((float)barLayouts.GetValue(i)!.GetType().GetField("X")!.GetValue(barLayouts.GetValue(i)!)!);

        for (int i = 0; i < noteLayouts.Length; i++)
        {
            float x = (float)noteLayouts.GetValue(i)!.GetType().GetField("X")!.GetValue(noteLayouts.GetValue(i)!)!;
            foreach (float bar in bars)
            {
                if (x > bar && x - headR < bar - 0.5f)
                {
                    Assert.Fail(
                        $"Level {level} seed {seed} note {i} center {x:F1} straddles bar {bar:F1} (headR {headR:F1})");
                }
            }
        }
    }

    private static void AssertBarIntervalsSumToMeter(
        List<GeneratedNote> notes,
        Array noteLayouts,
        Array barLayouts,
        double meter,
        int level,
        int seed)
    {
        var bars = new List<float>();
        for (int i = 0; i < barLayouts.Length; i++)
            bars.Add((float)barLayouts.GetValue(i)!.GetType().GetField("X")!.GetValue(barLayouts.GetValue(i)!)!);
        bars.Sort();

        var centers = new List<(float x, double dur, int index)>();
        for (int i = 0; i < notes.Count; i++)
        {
            float x = (float)noteLayouts.GetValue(i)!.GetType().GetField("X")!.GetValue(noteLayouts.GetValue(i)!)!;
            centers.Add((x, notes[i].BeatDuration, i));
        }

        // Staff content starts at the first note; each internal/final bar closes one measure.
        float left = centers.Count > 0 ? centers.Min(c => c.x) - 1f : 0f;
        var edges = new List<float> { left };
        edges.AddRange(bars);

        for (int b = 1; b < edges.Count; b++)
        {
            float a = edges[b - 1];
            float z = edges[b];
            double sum = centers.Where(c => c.x >= a && c.x < z - 0.01f).Sum(c => c.dur);
            // The final bar can close a short last system; internal spans must be exact.
            if (b < edges.Count - 1 || Math.Abs(sum) > 0.01)
            {
                Assert.True(Math.Abs(sum - meter) < 0.05,
                    $"Level {level} seed {seed} span {b - 1} sum={sum:F2} between x {a:F0} and {z:F0}, expected {meter}");
            }
        }
    }

    private static (StaffDrawable drawable, Array noteLayouts, Array barLayouts) Plan(
        NoteSessionService session, List<GeneratedNote> notes, float canvasW)
    {
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        typeof(StaffDrawable)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .First(m => m.Name == "ComputeLayout" && m.GetParameters().Length == 3)
            .Invoke(drawable, new object[]
            {
                480f,
                (IReadOnlyList<GeneratedNote>)notes,
                (IReadOnlyList<GeneratedNote>)Array.Empty<GeneratedNote>(),
            });

        float safeLeft = 12f;
        object header = typeof(StaffDrawable)
            .GetMethod("ComputeHeaderMetrics", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[] { safeLeft })!;
        float leftMargin = (float)header.GetType().GetProperty("LeftMargin")!.GetValue(header)!;
        double origin = notes.Min(n => n.BeatPosition ?? 0);
        double end = notes.Max(n => (n.BeatPosition ?? 0) + n.BeatDuration);
        var barBeats = new List<double>();
        for (double bar = origin + 4; bar < end - 1e-6; bar += 4)
            barBeats.Add(bar);

        float available = Math.Max(64f, canvasW - 24f - safeLeft - leftMargin);
        object plan = typeof(StaffDrawable)
            .GetMethod("PlanHorizontalLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(drawable, new object[]
            {
                notes, barBeats, available, leftMargin, true, true, false
            })!;
        return (
            drawable,
            (Array)plan.GetType().GetField("Item1")!.GetValue(plan)!,
            (Array)plan.GetType().GetField("Item2")!.GetValue(plan)!);
    }
}
