using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;
using Xunit.Abstractions;

namespace musicmate.Tests;

/// <summary>
/// Playback must use exactly the musical events assigned to displayed staves.
/// </summary>
public class DisplayedPlaybackSyncTests
{
    private readonly ITestOutputHelper _out;
    public DisplayedPlaybackSyncTests(ITestOutputHelper output) => _out = output;

    private const float CanvasH = 480f;
    private const float TypicalWidth = 835f;

    [Fact]
    public void DiminishedSeventh_LegacyHardcodedSplit_WouldPlayUnplacedMeasures()
    {
        var page = BuildFDiminishedSeventh();
        var drawable = NewArpeggioDrawable("F", "Major");

        var widthSplit = SplitPage(drawable, page, TypicalWidth);
        var legacy = LegacyHardcodedArpeggioSplit(page);

        var legacyPlayback = DisplayedPlaybackSync.BuildDisplayedRhythmSequence(
            legacy.Upper, legacy.Lower);
        var audit = DisplayedPlaybackSync.AuditStaffAssignment(
            page, widthSplit.UpperNotes, widthSplit.LowerNotes, widthSplit.UnplacedNotes,
            legacyPlayback);

        _out.WriteLine(audit.FormatStageDump(page, widthSplit.UpperNotes, widthSplit.LowerNotes, legacyPlayback));
        _out.WriteLine(audit.FormatStageDump(page, legacy.Upper, legacy.Lower, legacyPlayback));

        Assert.True(widthSplit.UnplacedMeasureCount > 0,
            "Regression pre-condition: F dim 7th must not fit all 8 measures at 835 DIP.");
        Assert.True(audit.UnplacedPitchedCount > 0);
        Assert.False(audit.IsSynchronized);
        Assert.Equal(audit.UnplacedPitchedCount, audit.PlaybackOnlyEvents.Count);

        var unplacedNames = DisplayedPlaybackSync.ExtractPitchedMusicalEvents(widthSplit.UnplacedNotes)
            .Select(e => e.SpelledName)
            .ToList();
        var extraNames = audit.PlaybackOnlyEvents.Select(e => e.Event.SpelledName).ToList();
        Assert.Equal(unplacedNames, extraNames);
    }

    [Fact]
    public void DiminishedSeventh_PackedStaff_PlaybackMatchesDisplayed()
    {
        var page = BuildFDiminishedSeventh();
        var split = SplitPage(NewArpeggioDrawable("F", "Major"), page, TypicalWidth);
        Assert.True(split.UnplacedMeasureCount > 0,
            "F dim 7th should leave overflow measures unplaced at 835 DIP.");
        AssertPlaybackMatchesDisplayed(page, split);
    }

    [Fact]
    public void DiminishedTriad_PackedStaff_PlaybackMatchesDisplayed()
    {
        AssertPlaybackMatchesDisplayed(
            BuildCDiminishedTriad(),
            SplitPage(NewArpeggioDrawable("C", "Natural Minor"), BuildCDiminishedTriad(), TypicalWidth));
    }

    [Fact]
    public void MajorTriadArpeggio_PackedStaff_PlaybackMatchesDisplayed()
    {
        var page = new ArpeggioSequenceBuilder
        {
            Key = "C",
            Scale = "Major",
            LowestNote = "A3",
            HighestNote = "C6",
        }.Build(ArpeggioCatalog.MajorTriad, "C4");

        AssertPlaybackMatchesDisplayed(
            page,
            SplitPage(NewArpeggioDrawable("C", "Major"), page, TypicalWidth));
    }

    [Fact]
    public void Scale_PackedStaff_PlaybackMatchesDisplayed()
    {
        var gen = new MusicSequenceGenerator
        {
            Key = "C",
            Scale = "Major",
            LowestNote = "C4",
            HighestNote = "C5",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 8,
            RhythmVarietyPercent = 0,
            SmallestDuration = NoteDuration.Quarter,
            UseScaleOrder = true,
            ChildLevel = 31,
            RandomSeed = 17,
        };
        var page = MusicSequenceGenerator.Flatten(gen.GenerateSequence());
        var session = new NoteSessionService
        {
            Key = "C",
            SelectedScale = "Major",
            MeterTimeSignature = "4/4",
            ChildLevel = 31,
        };
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
        var split = SplitPage(drawable, page, TypicalWidth);

        AssertPlaybackMatchesDisplayed(page, split);
    }

    [Theory]
    [InlineData(835f)]
    [InlineData(520f)]
    [InlineData(StaffPageWidthPolicy.FallbackWidthDip)]
    public void NarrowScreenPacking_UnplacedEventsAreNotPlayed(float canvasW)
    {
        var page = BuildFDiminishedSeventh();
        var split = SplitPage(NewArpeggioDrawable("F", "Major"), page, canvasW);
        var playback = DisplayedPlaybackSync.BuildDisplayedRhythmSequence(
            split.UpperNotes, split.LowerNotes);

        var audit = DisplayedPlaybackSync.AuditStaffAssignment(
            page, split.UpperNotes, split.LowerNotes, split.UnplacedNotes, playback);

        _out.WriteLine(audit.FormatStageDump(page, split.UpperNotes, split.LowerNotes, playback));
        DisplayedPlaybackSync.AssertSynchronized(audit, $"F dim 7th @ {canvasW}");
    }

    [Fact]
    public void StaffWrapping_PreservesOrderWithoutDuplicatesOrOmissions()
    {
        var page = BuildFDiminishedSeventh();
        var split = SplitPage(NewArpeggioDrawable("F", "Major"), page, TypicalWidth);
        var reconstructed = split.UpperNotes
            .Concat(split.LowerNotes)
            .Concat(split.UnplacedNotes)
            .ToList();

        Assert.Equal(page.Count, reconstructed.Count);
        for (int i = 0; i < page.Count; i++)
        {
            Assert.Equal(page[i].SpelledName, reconstructed[i].SpelledName);
            Assert.Equal(page[i].MeasureIndex, reconstructed[i].MeasureIndex);
        }

        var displayed = split.UpperNotes.Concat(split.LowerNotes).ToList();
        Assert.Equal(
            displayed.Select(n => n.SpelledName).ToList(),
            DisplayedPlaybackSync.BuildDisplayedRhythmSequence(split.UpperNotes, split.LowerNotes)
                .Select(n => n.SpelledName)
                .ToList());
    }

    [Fact]
    public void CountInEvents_AreExcludedFromMusicalEventSequence()
    {
        var page = BuildFDiminishedSeventh();
        var split = SplitPage(NewArpeggioDrawable("F", "Major"), page, TypicalWidth);
        var playback = DisplayedPlaybackSync.BuildDisplayedRhythmSequence(
            split.UpperNotes, split.LowerNotes);

        Assert.DoesNotContain(playback, n => n.SpelledName.Contains("click", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            split.UpperNotes.Count(n => !n.IsRest) + split.LowerNotes.Count(n => !n.IsRest),
            DisplayedPlaybackSync.ExtractPitchedMusicalEvents(playback).Count);
    }

    [Fact]
    public void FinalSustainedRoot_DoesNotAddExtraPlaybackOnlyEvent()
    {
        var page = BuildFDiminishedSeventh();
        var m8 = page.Where(n => n.MeasureIndex == 7).OrderBy(n => n.BeatPosition).ToList();
        Assert.Equal(2, m8.Count);
        Assert.Equal(m8[0].SpelledName, m8[1].SpelledName);
        Assert.Equal(NoteDuration.Half, m8[0].Duration);
        Assert.Equal(NoteDuration.Half, m8[1].Duration);

        var split = SplitPage(NewArpeggioDrawable("F", "Major"), page, 1200f);
        var playback = DisplayedPlaybackSync.BuildDisplayedRhythmSequence(
            split.UpperNotes, split.LowerNotes);
        var m8Played = playback
            .Where(n => n.MeasureIndex == 7)
            .OrderBy(n => n.BeatPosition)
            .ToList();

        if (m8Played.Count == 2)
        {
            Assert.Equal(m8[0].SpelledName, m8Played[0].SpelledName);
            Assert.Equal(m8[1].SpelledName, m8Played[1].SpelledName);
        }
        else
        {
            Assert.Empty(m8Played);
        }

        var audit = DisplayedPlaybackSync.AuditStaffAssignment(
            page, split.UpperNotes, split.LowerNotes, split.UnplacedNotes, playback);
        DisplayedPlaybackSync.AssertSynchronized(audit, "M8 sustained root");
        Assert.Empty(audit.PlaybackOnlyEvents);
    }

    private void AssertPlaybackMatchesDisplayed(
        List<GeneratedNote> page,
        StaffDrawable.StaffMeasureSplitResult split)
    {
        var playback = DisplayedPlaybackSync.BuildDisplayedRhythmSequence(
            split.UpperNotes, split.LowerNotes);
        var audit = DisplayedPlaybackSync.AuditStaffAssignment(
            page, split.UpperNotes, split.LowerNotes, split.UnplacedNotes, playback);

        _out.WriteLine(audit.FormatStageDump(page, split.UpperNotes, split.LowerNotes, playback));
        DisplayedPlaybackSync.AssertSynchronized(audit, "packed staff");
        Assert.Equal(audit.DisplayedPitchedCount, audit.PlaybackPitchedCount);
    }

    private static StaffDrawable.StaffMeasureSplitResult SplitPage(
        StaffDrawable drawable,
        List<GeneratedNote> page,
        float canvasW)
        => StaffPageWidthPolicy.SplitCachedPage(
            drawable,
            new StaffPagePackState
            {
                PageNotes = page,
                PageBarBeats = BuildBarBeats(page, 4),
                MeasureBeats = 4,
                IsTwoOctaveScaleCut = false,
                PackedCanvasWidth = canvasW,
                PackedCanvasHeight = CanvasH,
            },
            canvasW,
            CanvasH);

    private static (List<GeneratedNote> Upper, List<GeneratedNote> Lower) LegacyHardcodedArpeggioSplit(
        List<GeneratedNote> allNotes)
    {
        const int arpeggioUpperMeasureCount = 4;
        return (
            allNotes.Where(n => (n.MeasureIndex ?? 0) < arpeggioUpperMeasureCount).ToList(),
            allNotes.Where(n => (n.MeasureIndex ?? 0) >= arpeggioUpperMeasureCount).ToList());
    }

    private static StaffDrawable NewArpeggioDrawable(string key, string scale)
        => new(new NoteSessionService
        {
            Key = key,
            SelectedScale = scale,
            MeterTimeSignature = "4/4",
            ChildLevel = 31,
            ShowSignaturesOnBothStaffs = true,
            Tune = "Arpeggio",
        }, new ThemeService(), safeArea: null);

    private static List<GeneratedNote> BuildFDiminishedSeventh()
        => new ArpeggioSequenceBuilder
        {
            Key = "F",
            Scale = "Major",
            LowestNote = "A3",
            HighestNote = "C6",
        }.Build(ArpeggioCatalog.DiminishedSeventh, "F4");

    private static List<GeneratedNote> BuildCDiminishedTriad()
        => new ArpeggioSequenceBuilder
        {
            Key = "C",
            Scale = "Natural Minor",
            LowestNote = "A3",
            HighestNote = "C6",
        }.Build(ArpeggioCatalog.DiminishedTriad, "C4");

    private static List<double> BuildBarBeats(IReadOnlyList<GeneratedNote> notes, double measureBeats)
    {
        var result = new List<double>();
        if (notes.Count == 0) return result;
        double origin = notes.Min(n => n.BeatPosition ?? 0);
        double end = notes.Max(n => (n.BeatPosition ?? 0) + n.BeatDuration);
        for (double bar = origin + measureBeats; bar < end - 1e-6; bar += measureBeats)
            result.Add(bar);
        return result;
    }
}
