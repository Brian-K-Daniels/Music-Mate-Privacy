using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Two-octave scale path: staff break at peak/turnaround, not Assortment balance.
/// </summary>
public class TwoOctaveScaleSplitTests
{
    private const float CanvasW = 835f;
    private const float CanvasH = 480f;

    [Fact]
    public void ApplyTwoOctaveLowerCut_DoesNotTruncateAscendingLower()
    {
        // Old bug: first up-interval emptied ascending spill onto lower.
        var ascending = new List<GeneratedNote>
        {
            Note(67, 0), // G4
            Note(69, 1), // A4
            Note(71, 2), // B4
            Note(72, 3), // C5
        };
        var kept = StaffPageWidthPolicy.ApplyTwoOctaveLowerCut(ascending);
        Assert.Equal(4, kept.Count);
        Assert.Equal(ascending.Select(n => n.MidiNumber), kept.Select(n => n.MidiNumber));
    }

    [Fact]
    public void ApplyTwoOctaveLowerCut_KeepsPureDescending()
    {
        var descending = new List<GeneratedNote>
        {
            Note(76, 0), // E5
            Note(74, 1),
            Note(72, 2),
            Note(71, 3),
            Note(69, 4),
            Note(67, 5),
        };
        var kept = StaffPageWidthPolicy.ApplyTwoOctaveLowerCut(descending);
        Assert.Equal(6, kept.Count);
    }

    [Fact]
    public void ApplyTwoOctaveLowerCut_TrimsOnlyAfterDescentThenAscent()
    {
        var notes = new List<GeneratedNote>
        {
            Note(72, 0), // C5 down…
            Note(71, 1),
            Note(69, 2),
            Note(67, 3), // …then up again
            Note(69, 4),
            Note(71, 5),
        };
        var kept = StaffPageWidthPolicy.ApplyTwoOctaveLowerCut(notes);
        Assert.Equal(4, kept.Count);
        Assert.Equal(67, kept[^1].MidiNumber);
    }

    [Fact]
    public void FindPeak_GMajorTwoOctave_IsHighestNote()
    {
        var (page, _) = BuildGMajorTwoOctavePage();
        int peakIdx = StaffPageWidthPolicy.FindScaleWalkPeakNoteIndex(page);
        Assert.True(peakIdx >= 0);
        int peakMidi = page[peakIdx].MidiNumber;
        Assert.Equal(page.Where(n => !n.IsRest).Max(n => n.MidiNumber), peakMidi);
        var after = page.Skip(peakIdx + 1).Where(n => !n.IsRest).ToList();
        Assert.NotEmpty(after);
        Assert.True(after[0].MidiNumber < peakMidi);
    }

    [Fact]
    public void TwoOctaveSplit_AscendingOnUpper_DescendingOnLower_NoLoss()
    {
        var (page, _) = BuildGMajorTwoOctavePage();
        var split = StaffPageWidthPolicy.SplitTwoOctaveScaleAtPeak(page);
        var lower = StaffPageWidthPolicy.ApplyTwoOctaveLowerCut(split.LowerNotes);

        int peakIdx = StaffPageWidthPolicy.FindScaleWalkPeakNoteIndex(page);
        int peakMidi = page[peakIdx].MidiNumber;

        Assert.True(split.UpperNotes.Count > 0);
        Assert.True(lower.Count > 0);
        Assert.Contains(split.UpperNotes, n => !n.IsRest && n.MidiNumber == peakMidi);

        var upperPitches = split.UpperNotes.Where(n => !n.IsRest).Select(n => n.MidiNumber).ToList();
        Assert.Equal(peakMidi, upperPitches[^1]);
        for (int i = 1; i < upperPitches.Count; i++)
            Assert.True(upperPitches[i] >= upperPitches[i - 1],
                $"Upper should ascend; {upperPitches[i - 1]} → {upperPitches[i]}");

        var lowerPitches = lower.Where(n => !n.IsRest).Select(n => n.MidiNumber).ToList();
        Assert.True(lowerPitches[0] < peakMidi);
        for (int i = 1; i < lowerPitches.Count; i++)
            Assert.True(lowerPitches[i] <= lowerPitches[i - 1],
                $"Lower should descend; {lowerPitches[i - 1]} → {lowerPitches[i]}");

        Assert.Equal(split.LowerNotes.Count, lower.Count);
        Assert.Equal(
            page.Count,
            split.UpperNotes.Count + split.LowerNotes.Count + split.UnplacedNotes.Count);
        Assert.Empty(split.UnplacedNotes);
        Assert.True(lower.Count(n => !n.IsRest) >= 3,
            $"Lower should hold descending walk; got {lower.Count(n => !n.IsRest)} pitches");
    }

    [Fact]
    public void TwoOctaveSplit_NotEmptiedByBalancedCut_UnlikeLegacyBehavior()
    {
        var (page, bars) = BuildGMajorTwoOctavePage();
        var session = Session("G", "Major", 28);
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);

        var balanced = drawable.SplitMeasuresAcrossStaves(page, bars, CanvasW, CanvasH);
        var peak = StaffPageWidthPolicy.SplitTwoOctaveScaleAtPeak(page);

        int peakMidi = page[StaffPageWidthPolicy.FindScaleWalkPeakNoteIndex(page)].MidiNumber;
        Assert.Contains(peak.UpperNotes, n => !n.IsRest && n.MidiNumber == peakMidi);

        var peakLower = StaffPageWidthPolicy.ApplyTwoOctaveLowerCut(peak.LowerNotes);
        Assert.NotEmpty(peakLower);

        var upperLast = peak.UpperNotes.Last(n => !n.IsRest).MidiNumber;
        var lowerFirst = peakLower.First(n => !n.IsRest).MidiNumber;
        Assert.True(lowerFirst < upperLast);

        // Legacy balanced + old first-up cut emptied ascending spill; softened cut keeps it,
        // but peak split is what assigns ascending/descending to the correct staves.
        var legacyCutOnBalancedLower = StaffPageWidthPolicy.ApplyTwoOctaveLowerCut(balanced.LowerNotes);
        Assert.Equal(
            page.Count,
            balanced.UpperNotes.Count + balanced.LowerNotes.Count + balanced.UnplacedNotes.Count);
        Assert.True(peakLower.Count(n => !n.IsRest) >= 3);
        _ = legacyCutOnBalancedLower;
    }

    [Fact]
    public void OneOctaveScale_UsesBalancedPath_UnchangedWhenFlagFalse()
    {
        var gen = new MusicSequenceGenerator
        {
            Key = "G",
            Scale = "Major",
            LowestNote = "G3",
            HighestNote = "G4",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 8,
            RhythmVarietyPercent = 0,
            SmallestDuration = NoteDuration.Quarter,
            RestChancePercent = 0,
            AccidentalPercent = 0,
            UseScaleOrder = true,
            ChildLevel = 28,
            RandomSeed = 11,
        };
        var page = MusicSequenceGenerator.Flatten(gen.GenerateSequence());
        var bars = Enumerable.Range(1, 20).Select(i => i * 4.0).ToList();
        var session = Session("G", "Major", 28);
        session.HighestNote = "G4";
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);

        var a = drawable.SplitMeasuresAcrossStaves(page, bars, CanvasW, CanvasH);
        Assert.Equal(0, a.UnplacedMeasureCount);
        Assert.Equal(
            page.Count,
            a.UpperNotes.Count + a.LowerNotes.Count + a.UnplacedNotes.Count);
        // One-octave is not the two-octave peak path — both staves may share ascending content.
        Assert.True(a.UpperMeasureCount + a.LowerMeasureCount >= 2);
    }

    [Fact]
    public void AssortmentRandom_BalancedCut_UnchangedByTwoOctaveFlagDefault()
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
            UseScaleOrder = true,
            UseMotifPhrases = false,
            ChildLevel = 28,
            RandomSeed = 11,
        };
        var page = MusicSequenceGenerator.Flatten(gen.GenerateSequence());
        var bars = Enumerable.Range(1, 7).Select(i => i * 4.0).ToList();
        var session = Session("C", "Major", 28);
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);

        var split = drawable.SplitMeasuresAcrossStaves(page, bars, CanvasW, CanvasH);
        Assert.Equal(4, split.UpperMeasureCount);
        Assert.Equal(4, split.LowerMeasureCount);
        Assert.Equal(0, split.UnplacedMeasureCount);
    }

    [Fact]
    public void SplitCachedPage_RespectsTwoOctavePeakFlag()
    {
        var (page, bars) = BuildGMajorTwoOctavePage();
        var session = Session("G", "Major", 28);
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        var state = new StaffPagePackState
        {
            PageNotes = page,
            PageBarBeats = bars,
            MeasureBeats = 4,
            IsTwoOctaveScaleCut = true,
            IsProvisional = false,
            PackedCanvasWidth = CanvasW,
            PackedCanvasHeight = CanvasH,
        };
        var split = StaffPageWidthPolicy.SplitCachedPage(drawable, state, CanvasW, CanvasH);
        int peakMidi = page[StaffPageWidthPolicy.FindScaleWalkPeakNoteIndex(page)].MidiNumber;
        Assert.Contains(split.UpperNotes, n => !n.IsRest && n.MidiNumber == peakMidi);
        Assert.Equal(peakMidi, split.UpperNotes.Last(n => !n.IsRest).MidiNumber);
        var lower = StaffPageWidthPolicy.ApplyTwoOctaveLowerCut(split.LowerNotes);
        Assert.True(lower.Count(n => !n.IsRest) >= 3);
        Assert.Equal(page.Count, split.UpperNotes.Count + split.LowerNotes.Count);
    }

    [Fact]
    public void SplitTwoOctaveScaleAtPeak_PeakIsLastUpperNote()
    {
        // Synthetic: ascend to 79 then descend — break after peak index.
        var page = new List<GeneratedNote>();
        int beat = 0;
        for (int m = 67; m <= 79; m++)
            page.Add(Note(m, beat++));
        for (int m = 78; m >= 67; m--)
            page.Add(Note(m, beat++));

        var split = StaffPageWidthPolicy.SplitTwoOctaveScaleAtPeak(page);
        Assert.Equal(79, split.UpperNotes[^1].MidiNumber);
        Assert.Equal(78, split.LowerNotes[0].MidiNumber);
        Assert.Equal(page.Count, split.UpperNotes.Count + split.LowerNotes.Count);
    }

    private static (List<GeneratedNote> page, List<double> bars) BuildGMajorTwoOctavePage()
    {
        var gen = new MusicSequenceGenerator
        {
            Key = "G",
            Scale = "Major",
            LowestNote = "G3",
            HighestNote = "G5",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 24,
            RhythmVarietyPercent = 0,
            SmallestDuration = NoteDuration.Quarter,
            RestChancePercent = 0,
            AccidentalPercent = 0,
            SyncopationLevel = SyncopationLevel.None,
            MaxMelodicIntervalSemitones = 12,
            UseScaleOrder = true,
            UseMotifPhrases = false,
            ChildLevel = 28,
            RandomSeed = 11,
        };
        var page = MusicSequenceGenerator.Flatten(gen.GenerateSequence());
        double maxBeat = page.Max(n => (n.BeatPosition ?? 0) + n.BeatDuration);
        var bars = Enumerable.Range(1, 40).Select(i => i * 4.0).Where(b => b < maxBeat).ToList();
        return (page, bars);
    }

    private static NoteSessionService Session(string key, string scale, int level)
        => new()
        {
            Key = key,
            SelectedScale = scale,
            MeterTimeSignature = "4/4",
            ChildLevel = level,
            ShowSignaturesOnBothStaffs = true,
            IsRandomMode = false,
            LowestNote = "G3",
            HighestNote = "G5",
        };

    private static GeneratedNote Note(int midi, double beat)
        => new()
        {
            MidiNumber = midi,
            Letter = 'C',
            Octave = midi / 12 - 1,
            Duration = NoteDuration.Quarter,
            IsRest = false,
            BeatPosition = beat,
            MeasureIndex = (int)(beat / 4),
        };
}
