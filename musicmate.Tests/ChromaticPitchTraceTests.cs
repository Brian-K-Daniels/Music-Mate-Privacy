using System.Text;
using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;
using Xunit.Abstractions;

namespace musicmate.Tests;

/// <summary>
/// Chromatic scale pitch completeness: every semitone in order, including C→C#→D,
/// survives generation, conversion, measure packing, staff split, layout, and playback.
/// </summary>
public class ChromaticPitchTraceTests
{
    private readonly ITestOutputHelper _out;

    public ChromaticPitchTraceTests(ITestOutputHelper output) => _out = output;

    // ── Root cause regression: mastery omission must not filter scale-walk pools ──

    [Fact]
    public void ScaleWalk_WithMasteredCSharp_StillIncludesEverySemitone()
    {
        var gen = CreateGen("A", "A3", "C6", 24);
        gen.ExcludedMidiNumbers = new HashSet<int> { 61, 73 };
        var flat = Flatten(gen);
        AssertNoSemitoneGaps(flat);
        Assert.Contains(flat.Where(n => !n.IsRest), n => n.MidiNumber == 61);
        Assert.Contains(flat.Where(n => !n.IsRest), n => n.MidiNumber == 73);
    }

    [Fact]
    public void ScaleWalk_MasteredCSharp_NotDroppedByPoolFiltering()
    {
        var gen = CreateGen("A", "A3", "C6", 24);
        gen.ExcludedMidiNumbers = new HashSet<int> { 61 };
        var flat = Flatten(gen);
        int iC = Midis(flat).IndexOf(60);
        Assert.Equal(new[] { 60, 61, 62 }, Midis(flat).Skip(iC).Take(3));
    }

    // ── Semitone completeness (ascending / descending / boundaries) ──

    [Theory]
    [InlineData("C", "A3", "C6")]
    [InlineData("A", "A3", "C6")]
    [InlineData("C", "A3", "C5")]
    public void AscendingChromatic_EverySemitoneOnce_InOrder(string key, string lo, string hi)
    {
        var flat = Flatten(CreateGen(key, lo, hi, 24));
        var ascending = AscendingMidis(flat);
        AssertConsecutiveSemitones(ascending);
        Assert.Equal(ascending.Count, ascending.Distinct().Count());
    }

    [Fact]
    public void DescendingChromatic_EverySemitoneOnce_InOrder()
    {
        var flat = Flatten(CreateGen("A", "A3", "C6", 24));
        var descending = DescendingMidis(flat);
        for (int i = 1; i < descending.Count; i++)
            Assert.Equal(1, descending[i - 1] - descending[i]);
        Assert.Equal(descending.Count, descending.Distinct().Count());
    }

    [Fact]
    public void Chromatic_B3ToC4ToCSharp4_OctaveBoundaryPreserved()
    {
        var flat = Flatten(CreateGen("A", "A3", "C6", 24));
        var midis = Midis(flat);
        int iB = midis.IndexOf(59);
        Assert.Equal(new[] { 59, 60, 61, 62 }, midis.Skip(iB).Take(4));
    }

    [Fact]
    public void Chromatic_E4ToF4ToFSharp4_PreservesSemitones()
    {
        var flat = Flatten(CreateGen("A", "A3", "C6", 24));
        var midis = Midis(flat);
        int iE = midis.IndexOf(64);
        Assert.Equal(new[] { 64, 65, 66, 67 }, midis.Skip(iE).Take(4));
    }

    // ── C vs C# identity / conversion ──

    [Fact]
    public void CSharp_NotEqualToCNatural_ByMidiAndSpelling()
    {
        var flat = Flatten(CreateGen("A", "A3", "C6", 24));
        var cNat = flat.First(n => n.MidiNumber == 60);
        var cSharp = flat.First(n => n.MidiNumber == 61);
        Assert.NotEqual(cNat.MidiNumber, cSharp.MidiNumber);
        Assert.NotEqual(cNat.SpelledName, cSharp.SpelledName);
    }

    [Fact]
    public void BuildNote_CSharp4_PreservesMidi61()
    {
        string spelled = NoteSessionService.SpellWrittenPitch(61, "A", "Chromatic", prevMidi: 60);
        char letter = char.ToUpperInvariant(spelled[0]);
        int octave = NoteSessionService.ParseOctaveFromSpelledName(spelled);
        var (acc, final) = NoteSessionService.ResolveAccidentalAndSpelling(
            spelled, 61, letter, octave, "A", "Chromatic");
        Assert.Equal(61, NoteSessionService.NoteNameToMidi(final));
        Assert.Equal(Accidental.Sharp, acc);
    }

    [Fact]
    public void ResolveTargetPitch_CSharp4_ReturnsMidi61()
    {
        var flat = Flatten(CreateGen("A", "A3", "C6", 24));
        var cSharp = flat.First(n => n.MidiNumber == 61);
        var (midi, name) = NoteSessionService.ResolveTargetPitch(cSharp, "A", "Chromatic");
        Assert.Equal(61, midi);
        Assert.Contains('#', name);
    }

    // ── Pipeline preservation (measures, staff split, playback) ──

    [Fact]
    public void MeasureConstruction_PreservesEveryChromaticPitch()
    {
        var gen = CreateGen("A", "A3", "C6", 24);
        gen.ExcludedMidiNumbers = new HashSet<int> { 61, 73 };
        var measures = gen.GenerateSequence();
        var notes = measures.SelectMany(m => m.GeneratedNotes).Where(n => !n.IsRest).ToList();
        AssertNoSemitoneGaps(notes);
    }

    [Fact]
    public void StaffSplit_VisibleUpperStaff_PreservesCSharp4()
    {
        var page = Flatten(CreateGen("A", "A3", "C6", 24));
        var split = SplitPage(page);
        var upperMidis = Midis(split.UpperNotes);
        Assert.Contains(60, upperMidis);
        Assert.Contains(61, upperMidis);
        Assert.Contains(62, upperMidis);
        int i = upperMidis.IndexOf(60);
        Assert.Equal(61, upperMidis[i + 1]);
    }

    [Fact]
    public void PlaybackSequence_MatchesDisplayedStaffPitches()
    {
        var page = Flatten(CreateGen("A", "A3", "C6", 24));
        var split = SplitPage(page);
        var displayed = split.UpperNotes.Concat(split.LowerNotes).Where(n => !n.IsRest).Select(n => n.MidiNumber).ToList();
        var playback = DisplayedPlaybackSync.BuildDisplayedRhythmSequence(split.UpperNotes, split.LowerNotes)
            .Where(n => !n.IsRest).Select(n => n.MidiNumber).ToList();
        Assert.Equal(displayed, playback);
        Assert.Contains(61, playback);
    }

    [Fact]
    public void StaffSplit_LowerStaff_PreservesCSharp5()
    {
        var page = Flatten(CreateGen("A", "A3", "C6", 24));
        var split = SplitPage(page);
        var lowerMidis = Midis(split.LowerNotes);
        if (lowerMidis.Contains(72))
        {
            int i = lowerMidis.IndexOf(72);
            if (i + 2 < lowerMidis.Count)
                Assert.Equal(73, lowerMidis[i + 1]);
        }
    }

    [Fact]
    public void Trace_FullPipeline_LogsCompleteSequence()
    {
        var gen = CreateGen("A", "A3", "C6", 24);
        gen.ExcludedMidiNumbers = new HashSet<int> { 61, 73 };
        var flat = Flatten(gen);
        DumpStage("Generator/GeneratedNote", flat);
        var split = SplitPage(flat);
        DumpStage("Staff upper", split.UpperNotes);
        DumpStage("Staff lower", split.LowerNotes);
        AssertNoSemitoneGaps(flat);
    }

    // ── helpers ──

    private static MusicSequenceGenerator CreateGen(string key, string lo, string hi, int measures)
        => new()
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
            RandomSeed = 19,
        };

    private static List<GeneratedNote> Flatten(MusicSequenceGenerator gen)
        => MusicSequenceGenerator.Flatten(gen.GenerateSequence());

    private static List<int> Midis(IEnumerable<GeneratedNote> notes)
        => notes.Where(n => !n.IsRest).Select(n => n.MidiNumber).ToList();

    private static List<int> AscendingMidis(IReadOnlyList<GeneratedNote> flat)
    {
        int peak = FindPeakIndex(flat);
        return flat.Take(peak + 1).Where(n => !n.IsRest).Select(n => n.MidiNumber).ToList();
    }

    private static List<int> DescendingMidis(IReadOnlyList<GeneratedNote> flat)
    {
        int peak = FindPeakIndex(flat);
        return flat.Skip(peak + 1).Where(n => !n.IsRest).Select(n => n.MidiNumber).ToList();
    }

    private static void AssertConsecutiveSemitones(IReadOnlyList<int> midis)
    {
        for (int i = 1; i < midis.Count; i++)
            Assert.Equal(1, midis[i] - midis[i - 1]);
    }

    private static void AssertNoSemitoneGaps(IReadOnlyList<GeneratedNote> notes)
    {
        var gaps = FindSemitoneGaps(notes);
        Assert.True(gaps.Count == 0, string.Join(", ", gaps.Select(g => $"{g.PrevMidi}->{g.NextMidi}")));
    }

    private static StaffDrawable.StaffMeasureSplitResult SplitPage(List<GeneratedNote> page)
    {
        var bars = BuildPageBarBeats(page);
        var drawable = NewDrawable();
        return StaffPageWidthPolicy.SplitCachedPage(
            drawable,
            new StaffPagePackState { PageNotes = page, PageBarBeats = bars, MeasureBeats = 4 },
            835f,
            480f);
    }

    private static StaffDrawable NewDrawable()
        => new(new NoteSessionService
        {
            Key = "A",
            SelectedScale = "Chromatic",
            MeterTimeSignature = "4/4",
            ChildLevel = 31,
            ShowSignaturesOnBothStaffs = true,
            LowestNote = "A3",
            HighestNote = "C6",
        }, new ThemeService(), safeArea: null);

    private void DumpStage(string label, IReadOnlyList<GeneratedNote> notes)
    {
        var sb = new StringBuilder();
        sb.AppendLine(label + ":");
        int p = 0;
        foreach (var n in notes.Where(n => !n.IsRest))
            sb.AppendLine($"  {p++} midi={n.MidiNumber} {n.SpelledName} acc={n.Accidental} mi={n.MeasureIndex} beat={n.BeatPosition:0.##}");
        _out.WriteLine(sb.ToString());
    }

    private static List<(int Index, int PrevMidi, int NextMidi, int Gap)> FindSemitoneGaps(
        IReadOnlyList<GeneratedNote> notes)
    {
        var midis = Midis(notes);
        var gaps = new List<(int, int, int, int)>();
        for (int i = 1; i < midis.Count; i++)
        {
            int d = midis[i] - midis[i - 1];
            if (Math.Abs(d) != 1)
                gaps.Add((i, midis[i - 1], midis[i], d));
        }
        return gaps;
    }

    private static int FindPeakIndex(IReadOnlyList<GeneratedNote> notes)
    {
        int peak = 0, peakMidi = int.MinValue;
        for (int i = 0; i < notes.Count; i++)
        {
            if (notes[i].IsRest) continue;
            if (notes[i].MidiNumber >= peakMidi)
            {
                peakMidi = notes[i].MidiNumber;
                peak = i;
            }
        }
        return peak;
    }

    private static List<double> BuildPageBarBeats(IReadOnlyList<GeneratedNote> notes)
    {
        var result = new List<double>();
        if (notes.Count == 0) return result;
        double origin = notes.Min(n => n.BeatPosition ?? 0);
        double end = notes.Max(n => (n.BeatPosition ?? 0) + n.BeatDuration);
        for (double bar = origin + 4; bar < end - 1e-6; bar += 4)
            result.Add(bar);
        return result;
    }
}
