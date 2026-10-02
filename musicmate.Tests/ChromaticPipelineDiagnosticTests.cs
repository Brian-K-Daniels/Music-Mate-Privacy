using System.Text;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;
using Xunit.Abstractions;

namespace musicmate.Tests;

/// <summary>
/// Evidence-only pipeline dump for chromatic C# disappearance.
/// Does not assert fix behavior — reports first semitone gap at each stage.
/// </summary>
public class ChromaticPipelineDiagnosticTests
{
    private readonly ITestOutputHelper _out;

    public ChromaticPipelineDiagnosticTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Screenshot-visible upper staff (first 8 quarter notes): A3..F4 without C#4.
    /// </summary>
    private static readonly int[] ScreenshotUpperMidis = [57, 58, 59, 60, 62, 63, 64, 65];

    [Fact]
    public void Diagnose_ScreenshotCase_FullPipelineDump()
    {
        var report = RunFullPipelineDiagnostic(
            key: "A",
            lowest: "A3",
            highest: "C6",
            excludedMidis: [61, 73],
            seed: 19,
            label: "Screenshot-like (A, A3-C6, mastered C#4+C#5)");

        _out.WriteLine(report);
        EmitFirstGapReport(report);
    }

    [Fact]
    public void Diagnose_NoExclusions_FullPipelineDump()
    {
        var report = RunFullPipelineDiagnostic(
            key: "A",
            lowest: "A3",
            highest: "C6",
            excludedMidis: [],
            seed: 19,
            label: "Same params, no mastery exclusions");

        _out.WriteLine(report);
        EmitFirstGapReport(report);
    }

    [Fact]
    public void Diagnose_PreFixPoolFilter_ReproducesScreenshotUpperSequence()
    {
        // Simulates BuildPitchPool BEFORE UseScaleOrder bypass: mastery filters the walk pool.
        var gen = BaseGen("A", "A3", "C6", 24, [61, 73], 19);
        int minMidi = NoteSessionService.NoteNameToMidi("A3");
        int maxMidi = NoteSessionService.NoteNameToMidi("C6");
        var fullPool = Enumerable.Range(minMidi, maxMidi - minMidi + 1).ToList();
        var filtered = MasteredNoteOmission.Apply(fullPool, gen.ExcludedMidiNumbers, minDistinctPitches: 2).Pool.ToList();

        var sortedPool = filtered.OrderBy(m => m).ToList();
        var walk = new List<int>(sortedPool);
        for (int d = sortedPool.Count - 2; d >= 0; d--)
            walk.Add(sortedPool[d]);

        var midis = walk.Take(8).ToList();
        _out.WriteLine($"Pre-fix simulated walk[0..7]: [{string.Join(", ", midis)}]");
        var gap = FirstGap(midis, "A-preFix-simulated");
        if (gap != null) _out.WriteLine(FormatGap(gap));

        Assert.Equal(ScreenshotUpperMidis, midis);
        Assert.NotNull(gap);
        Assert.Equal(61, gap!.MissingMidi);
    }

    [Fact]
    public void Diagnose_Minimal_C4ToD4_Ascending()
    {
        var gen = BaseGen("C", "C4", "D4", 1);
        var flat = Flatten(gen);
        var midis = Midis(flat);
        _out.WriteLine($"C4-D4 minimal (first 3 ascending): [{string.Join(", ", midis.Take(3))}]");
        Assert.Equal(new[] { 60, 61, 62 }, midis.Take(3));
    }

    [Fact]
    public void Diagnose_B3C4CSharp4D4_OctaveBoundary()
    {
        var gen = BaseGen("A", "B3", "D4", 1);
        var flat = Flatten(gen);
        var midis = Midis(flat);
        _out.WriteLine($"B3-D4: [{string.Join(", ", midis)}]");
        var gap = FirstGap(midis, "StageA-B3-D4");
        if (gap != null) _out.WriteLine(FormatGap(gap));
        Assert.Equal(new[] { 59, 60, 61, 62 }, midis);
    }

    [Fact]
    public void GeneratedNote_Equality_UsesReferenceNotMidi()
    {
        var a = Note(60, "C4", Accidental.Natural);
        var b = Note(61, "C#4", Accidental.Sharp);
        Assert.NotEqual(a, b);
        Assert.False(ReferenceEquals(a, b));
        Assert.NotEqual(a.GetHashCode(), b.GetHashCode()); // reference type default
    }

    [Fact]
    public void Diagnose_MatchScreenshotUpperSequence_FindScenario()
    {
        var scenarios = new (string label, string key, int[] excluded)[]
        {
            ("A + mastered 61,73", "A", [61, 73]),
            ("A + mastered 61", "A", [61]),
            ("A + no exclusions", "A", []),
            ("C + mastered 61", "C", [61]),
            ("D + mastered 61", "D", [61]),
        };

        foreach (var (label, key, excluded) in scenarios)
        {
            var split = SplitPage(Flatten(BaseGen(key, "A3", "C6", 24, excluded, 19)));
            var upper = Midis(split.UpperNotes).Take(8).ToArray();
            bool match = upper.SequenceEqual(ScreenshotUpperMidis);
            _out.WriteLine($"{label}: upper[0..7]=[{string.Join(", ", upper)}] matchScreenshot={match}");
        }
    }

    // ── pipeline runner ──

    private sealed record StageDump(string Name, IReadOnlyList<GeneratedNote> Notes);

    private sealed record GapReport(
        string Stage,
        int Index,
        int PrevMidi,
        int NextMidi,
        int Delta,
        int MissingMidi);

    private string RunFullPipelineDiagnostic(
        string key,
        string lowest,
        string highest,
        int[] excludedMidis,
        int seed,
        string label)
    {
        var gen = BaseGen(key, lowest, highest, 24, excludedMidis, seed);
        var measures = gen.GenerateSequence();
        var stageA = FlattenFromMeasures(measures);
        var stageB = stageA; // GeneratedNote is built inside generator — same list

        var stageC = stageA; // flatten is measure order
        var page = stageA.ToList();
        var split = SplitPage(page);
        var lowerShifted = ShiftLower(split.LowerNotes);
        var stageDUpper = split.UpperNotes;
        var stageDLower = lowerShifted;
        var stageDOverflow = split.UnplacedNotes;

        var stageE = stageDUpper.Concat(stageDLower).ToList();
        var stageF = DisplayedPlaybackSync.BuildDisplayedRhythmSequence(stageDUpper, stageDLower);

        var (noteKey, noteScale) = (key, "Chromatic");
        var notesToDraw = BuildNotesToDraw(stageE, noteKey, noteScale);

        var sb = new StringBuilder();
        sb.AppendLine($"=== {label} ===");
        sb.AppendLine($"ExcludedMidis: [{string.Join(", ", excludedMidis)}]");
        sb.AppendLine();

        DumpStage(sb, "Stage A — generator flatten", stageA);
        DumpMeasures(sb, measures);
        DumpStage(sb, "Stage D — upper staff", stageDUpper);
        DumpStage(sb, "Stage D — lower staff", stageDLower);
        if (stageDOverflow.Count > 0)
            DumpStage(sb, "Stage D — overflow/unplaced", stageDOverflow);
        DumpStage(sb, "Stage E — renderer input (upper+lower)", stageE);
        DumpStage(sb, "Stage F — playback rhythm sequence", stageF);
        DumpNotesToDraw(sb, notesToDraw);

        foreach (var stage in new[]
        {
            new StageDump("A", stageA),
            new StageDump("B", stageB),
            new StageDump("C", stageC),
            new StageDump("D-upper", stageDUpper),
            new StageDump("D-lower", stageDLower),
            new StageDump("D-overflow", stageDOverflow),
            new StageDump("E", stageE),
            new StageDump("F", stageF),
        })
        {
            var gap = FirstGap(Midis(stage.Notes), stage.Name);
            if (gap != null)
                sb.AppendLine(FormatGap(gap));
        }

        var upperFirst8 = Midis(stageDUpper).Take(8).ToArray();
        sb.AppendLine($"Screenshot upper match: {upperFirst8.SequenceEqual(ScreenshotUpperMidis)} " +
                      $"actual=[{string.Join(",", upperFirst8)}] expected=[{string.Join(",", ScreenshotUpperMidis)}]");

        return sb.ToString();
    }

    private static List<(int Midi, string Name)> BuildNotesToDraw(
        List<GeneratedNote> rhythmOrder,
        string noteKey,
        string noteScale)
    {
        var slots = RhythmStartGate.BuildSlots(rhythmOrder);
        var result = new List<(int, string)>();
        int pitchIdx = 0;
        foreach (var gn in rhythmOrder)
        {
            if (gn.IsRest) continue;
            _ = slots[pitchIdx++];
            var (midi, name) = NoteSessionService.ResolveTargetPitch(gn, noteKey, noteScale);
            result.Add((midi, name));
        }
        return result;
    }

    private void EmitFirstGapReport(string report)
    {
        foreach (var line in report.Split('\n'))
        {
            if (line.StartsWith("First chromatic gap:", StringComparison.Ordinal))
                _out.WriteLine(line.Trim());
        }
    }

    private static GapReport? FirstGap(IReadOnlyList<int> midis, string stage)
    {
        for (int i = 1; i < midis.Count; i++)
        {
            int d = midis[i] - midis[i - 1];
            if (Math.Abs(d) != 1)
            {
                int missing = d > 0 ? midis[i - 1] + 1 : midis[i - 1] - 1;
                return new GapReport(stage, i - 1, midis[i - 1], midis[i], d, missing);
            }
        }
        return null;
    }

    private static string FormatGap(GapReport g)
        => $"First chromatic gap: Stage={g.Stage} index={g.Index} pitch[{g.Index}]={g.PrevMidi} " +
           $"pitch[{g.Index + 1}]={g.NextMidi} difference={Math.Abs(g.Delta)} semitones " +
           $"missingMidi={g.MissingMidi} ({NoteSessionService.MidiToNoteName(g.MissingMidi, false)})";

    private static void DumpStage(StringBuilder sb, string label, IReadOnlyList<GeneratedNote> notes)
    {
        sb.AppendLine(label + ":");
        int p = 0;
        foreach (var n in notes.Where(x => !x.IsRest))
        {
            sb.AppendLine(
                $"  {p++} midi={n.MidiNumber} letter={n.Letter} acc={n.Accidental} oct={n.Octave} " +
                $"name={n.SpelledName} mi={n.MeasureIndex} beat={n.BeatPosition:0.##}");
        }
        sb.AppendLine($"  pitchedCount={p}");
        sb.AppendLine();
    }

    private static void DumpMeasures(StringBuilder sb, List<Measure> measures)
    {
        sb.AppendLine("Stage C — measures:");
        for (int m = 0; m < measures.Count; m++)
        {
            sb.AppendLine($"  Measure {m}:");
            int beat = 0;
            foreach (var n in measures[m].GeneratedNotes.Where(x => !x.IsRest))
            {
                sb.AppendLine(
                    $"    beat {beat++} = {n.SpelledName} (midi={n.MidiNumber} acc={n.Accidental})");
            }
        }
        sb.AppendLine();
    }

    private static void DumpNotesToDraw(StringBuilder sb, List<(int Midi, string Name)> notes)
    {
        sb.AppendLine("Stage F — NotesToDraw (playback target pitch):");
        for (int i = 0; i < notes.Count; i++)
            sb.AppendLine($"  {i} midi={notes[i].Midi} name={notes[i].Name}");
        sb.AppendLine();
    }

    // ── helpers ──

    private static MusicSequenceGenerator BaseGen(
        string key, string lo, string hi, int measures,
        int[]? excluded = null, int seed = 19)
    {
        var gen = new MusicSequenceGenerator
        {
            Key = key,
            Scale = "Chromatic",
            LowestNote = lo,
            HighestNote = hi,
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = measures,
            RhythmVarietyPercent = 0,
            SmallestDuration = NoteDuration.Quarter,
            RestChancePercent = 0,
            UseScaleOrder = true,
            ChildLevel = 31,
            RandomSeed = seed,
        };
        if (excluded is { Length: > 0 })
            gen.ExcludedMidiNumbers = new HashSet<int>(excluded);
        return gen;
    }

    private static List<GeneratedNote> Flatten(MusicSequenceGenerator gen)
        => MusicSequenceGenerator.Flatten(gen.GenerateSequence());

    private static List<GeneratedNote> FlattenFromMeasures(List<Measure> measures)
        => MusicSequenceGenerator.Flatten(measures);

    private static List<int> Midis(IEnumerable<GeneratedNote> notes)
        => notes.Where(n => !n.IsRest).Select(n => n.MidiNumber).ToList();

    private static StaffDrawable.StaffMeasureSplitResult SplitPage(List<GeneratedNote> page)
    {
        var bars = BuildBars(page);
        var drawable = new StaffDrawable(
            new NoteSessionService
            {
                Key = "A",
                SelectedScale = "Chromatic",
                MeterTimeSignature = "4/4",
                ChildLevel = 31,
                ShowSignaturesOnBothStaffs = true,
                LowestNote = "A3",
                HighestNote = "C6",
            },
            new ThemeService(),
            safeArea: null);
        return StaffPageWidthPolicy.SplitCachedPage(
            drawable,
            new StaffPagePackState { PageNotes = page, PageBarBeats = bars, MeasureBeats = 4 },
            835f,
            480f);
    }

    private static List<GeneratedNote> ShiftLower(IReadOnlyList<GeneratedNote> lower)
    {
        if (lower.Count == 0) return new List<GeneratedNote>();
        double shift = lower[0].BeatPosition ?? 0;
        return lower.Select(n => new GeneratedNote
        {
            MidiNumber = n.MidiNumber,
            Letter = n.Letter,
            Octave = n.Octave,
            Accidental = n.Accidental,
            SpelledName = n.SpelledName,
            Duration = n.Duration,
            MeasureIndex = n.MeasureIndex,
            BeatPosition = (n.BeatPosition ?? 0) - shift,
        }).ToList();
    }

    private static List<double> BuildBars(IReadOnlyList<GeneratedNote> notes)
    {
        var result = new List<double>();
        if (notes.Count == 0) return result;
        double origin = notes.Min(n => n.BeatPosition ?? 0);
        double end = notes.Max(n => (n.BeatPosition ?? 0) + n.BeatDuration);
        for (double bar = origin + 4; bar < end - 1e-6; bar += 4)
            result.Add(bar);
        return result;
    }

    private static GeneratedNote Note(int midi, string spelled, Accidental acc)
    {
        char letter = char.ToUpperInvariant(spelled[0]);
        int octave = int.Parse(spelled[^1].ToString());
        return new GeneratedNote
        {
            MidiNumber = midi,
            Letter = letter,
            Octave = octave,
            Accidental = acc,
            SpelledName = spelled,
        };
    }
}
