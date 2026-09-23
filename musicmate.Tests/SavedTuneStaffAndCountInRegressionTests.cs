using musicmate.Drawables;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Saved Practice Tune staff partitioning must not engrave duplicate chrome,
/// and Count-In / cue self-sound must not advance the first note.
/// </summary>
[Collection("SessionPreferences")]
public class SavedTuneStaffAndCountInRegressionTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public SavedTuneStaffAndCountInRegressionTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    // ── Problem 1: staff layout uniqueness ─────────────────────────────────

    [Fact]
    public void OneMeasureSavedTune_ProducesSingleUpperStaffOnly()
    {
        var notes = BuildOneMeasureTuneNotes();
        var (upper, lower) = PracticeTuneStaffSplit.PartitionByMeasureHalf(
            notes, measureCount: 1, splitBeat: 0.0, ShiftIdentity);

        Assert.Equal(notes.Count, upper.Count);
        Assert.Empty(lower);
        Assert.Equal(1, PracticeTuneStaffSplit.CountEngravedStaffSystems(upper.Count, lower.Count));
        Assert.True(PracticeTuneStaffSplit.ShouldEngraveStaff(upper.Count));
        Assert.False(PracticeTuneStaffSplit.ShouldEngraveStaff(lower.Count));
    }

    [Fact]
    public void OneMeasureSavedTune_RePartitionDoesNotDuplicateNotes()
    {
        var notes = BuildOneMeasureTuneNotes();
        var first = PracticeTuneStaffSplit.PartitionByMeasureHalf(
            notes, measureCount: 1, splitBeat: 0.0, ShiftIdentity);
        var second = PracticeTuneStaffSplit.PartitionByMeasureHalf(
            notes, measureCount: 1, splitBeat: 0.0, ShiftIdentity);

        Assert.Equal(first.Upper.Count, second.Upper.Count);
        Assert.Empty(first.Lower);
        Assert.Empty(second.Lower);
        Assert.Equal(
            first.Upper.Sum(n => n.MidiNumber),
            second.Upper.Sum(n => n.MidiNumber));
        Assert.Equal(1, PracticeTuneStaffSplit.CountEngravedStaffSystems(
            second.Upper.Count, second.Lower.Count));
    }

    [Fact]
    public void CountInStart_DoesNotChangeStaffPartition()
    {
        // Starting Count-In must not append another layout; partition is pure and stable.
        var notes = BuildOneMeasureTuneNotes();
        var before = PracticeTuneStaffSplit.PartitionByMeasureHalf(
            notes, measureCount: 1, splitBeat: 0.0, ShiftIdentity);

        // Simulate Count-In arming (no staff rebuild).
        int generation = 0;
        WaitingCountInArming.Arm(ref generation);
        Assert.True(WaitingCountInSettings.Enabled || !WaitingCountInSettings.Enabled);

        var after = PracticeTuneStaffSplit.PartitionByMeasureHalf(
            notes, measureCount: 1, splitBeat: 0.0, ShiftIdentity);

        Assert.Equal(before.Upper.Count, after.Upper.Count);
        Assert.Empty(after.Lower);
        Assert.Equal(1, PracticeTuneStaffSplit.CountEngravedStaffSystems(
            after.Upper.Count, after.Lower.Count));
    }

    [Fact]
    public void TwoMeasureTune_SplitsAcrossBothStaves_WithoutEmptyUpper()
    {
        var notes = BuildTwoMeasureTuneNotes(measureBeats: 4.0);
        double splitBeat = 4.0;
        var (upper, lower) = PracticeTuneStaffSplit.PartitionByMeasureHalf(
            notes, measureCount: 2, splitBeat, ShiftBy);

        Assert.NotEmpty(upper);
        Assert.NotEmpty(lower);
        Assert.Equal(notes.Count, upper.Count + lower.Count);
        Assert.Equal(2, PracticeTuneStaffSplit.CountEngravedStaffSystems(upper.Count, lower.Count));
    }

    [Fact]
    public void EmptyUpperSafety_FallsBackToSingleUpperSystem()
    {
        // All notes at beat >= splitBeat would previously leave upper empty.
        var notes = new List<GeneratedNote>
        {
            NoteAt(60, beat: 0.0, measure: 0),
            NoteAt(62, beat: 1.0, measure: 0),
        };
        var (upper, lower) = PracticeTuneStaffSplit.PartitionByMeasureHalf(
            notes, measureCount: 2, splitBeat: 0.0, ShiftBy);

        Assert.Equal(2, upper.Count);
        Assert.Empty(lower);
        Assert.Equal(1, PracticeTuneStaffSplit.CountEngravedStaffSystems(upper.Count, lower.Count));
    }

    [Fact]
    public void OneMeasure_PartitionForDisplay_StaysUpperOnly()
    {
        var notes = BuildOneMeasureTuneNotes();
        var bars = new List<double>();
        var drawable = new StaffDrawable(CreateSessionFirstMidi(60), new ThemeService(), safeArea: null);
        var split = PracticeTuneStaffSplit.PartitionForDisplay(
            drawable, notes, bars, measureCount: 1, canvasWidth: 400f, canvasHeight: 480f);

        Assert.Equal(notes.Count, split.UpperNotes.Count);
        Assert.Empty(split.LowerNotes);
        Assert.Equal(0, split.UnplacedMeasureCount);
        Assert.Equal(notes.Count, split.UpperNotes.Count + split.LowerNotes.Count);
    }

    [Fact]
    public void LongDenseTune_PartitionForDisplay_KeepsAllNotes_AndPrefersFillingUpper()
    {
        var page = BuildDenseEightMeasurePage();
        var bars = Enumerable.Range(1, 7).Select(i => i * 4.0).ToList();
        var session = new NoteSessionService
        {
            Instrument = "Concert Pitch",
            Key = "F#",
            SelectedScale = "Major",
            MeterTimeSignature = "4/4",
            ChildLevel = 40,
            ShowSignaturesOnBothStaffs = true,
            Tune = "Practice Tune",
        };
        var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);

        // Blind half-split would put 4+4 regardless of width.
        var halfUpper = page.Where(n => (n.MeasureIndex ?? 0) < 4).ToList();
        var halfLower = page.Where(n => (n.MeasureIndex ?? 0) >= 4).ToList();
        Assert.Equal(4, halfUpper.Select(n => n.MeasureIndex ?? 0).Distinct().Count());
        Assert.Equal(4, halfLower.Select(n => n.MeasureIndex ?? 0).Distinct().Count());

        var split = PracticeTuneStaffSplit.PartitionForDisplay(
            drawable, page, bars, measureCount: 8, canvasWidth: 520f, canvasHeight: 480f);

        Assert.Equal(0, split.UnplacedMeasureCount);
        Assert.Equal(page.Count, split.UpperNotes.Count + split.LowerNotes.Count);
        Assert.True(split.UpperNotes.Count > 0);
        Assert.True(split.LowerNotes.Count > 0);

        // Width-aware fill-upper-first should place at least as many measures on upper
        // as a blind half-split would (4), when the canvas can hold them — otherwise
        // upper still receives a greedy full-width pack before lower.
        var widthOnly = drawable.SplitMeasuresAcrossStaves(
            page, bars, 520f, 480f, StaffDrawable.StaffMeasureSplitMode.FillUpperFirst);
        Assert.True(widthOnly.UpperMeasureCount >= widthOnly.LowerMeasureCount,
            $"Before keep-all append, upper should be filled first " +
            $"(got {widthOnly.UpperMeasureCount}+{widthOnly.LowerMeasureCount}+u{widthOnly.UnplacedMeasureCount})");

        // Order preserved across the wrap.
        var placed = split.UpperNotes.Concat(split.LowerNotes).ToList();
        for (int i = 0; i < page.Count; i++)
            Assert.Equal(page[i].SpelledName, placed[i].SpelledName);
    }

    [Fact]
    public void KeepAllNotesVisible_MovesUnplacedOntoLower()
    {
        var split = new StaffDrawable.StaffMeasureSplitResult
        {
            UpperMeasureCount = 2,
            LowerMeasureCount = 1,
            UnplacedMeasureCount = 1,
            TotalMeasureCount = 4,
        };
        split.UpperNotes.Add(NoteAt(60, 0, 0));
        split.LowerNotes.Add(NoteAt(62, 0, 1));
        split.UnplacedNotes.Add(NoteAt(64, 0, 2));

        var kept = PracticeTuneStaffSplit.KeepAllNotesVisible(split);
        Assert.Equal(0, kept.UnplacedMeasureCount);
        Assert.Empty(kept.UnplacedNotes);
        Assert.Equal(2, kept.LowerNotes.Count);
        Assert.Equal(2, kept.LowerMeasureCount);
    }

    private static List<GeneratedNote> BuildDenseEightMeasurePage()
    {
        var gen = new MusicSequenceGenerator
        {
            Key = "F#",
            Scale = "Major",
            LowestNote = "A3",
            HighestNote = "E5",
            TimeSignature = TimeSignature.FourFour,
            MeasureCount = 8,
            RhythmVarietyPercent = 55,
            SmallestDuration = NoteDuration.Eighth,
            RestChancePercent = 10,
            AccidentalPercent = 40,
            SyncopationLevel = SyncopationLevel.None,
            MaxMelodicIntervalSemitones = 5,
            UseScaleOrder = false,
            UseMotifPhrases = true,
            ChildLevel = 40,
            RandomSeed = 2,
        };
        return MusicSequenceGenerator.Flatten(gen.GenerateSequence());
    }

    // ── Problem 2: Count-In / cue self-sound ───────────────────────────────

    [Fact]
    public void MatchingPitch_DuringCountInSelfSoundWindow_BlocksWhenSamePitchClassAsClick()
    {
        var session = CreateSessionFirstMidi(64); // E4 — same pitch class as default unaccented E6 click
        session.StartListeningClock();
        int audible = WaitingCountInLogic.ResolveClickSelfSoundDurationMs(100);
        session.SuppressCountInClickSelfSound(audible);
        Assert.True(session.ShouldIgnoreAudio(DateTime.UtcNow));

        double freq = NoteSessionService.MidiToFreqPublic(64);
        var result = session.Evaluate(freq);
        Assert.True(result.correct);
        Assert.False(
            WaitingCountInLogic.ShouldAcceptFirstNoteToEndCountIn(
                result.correct, true, 0, session.ShouldIgnoreAudio(DateTime.UtcNow),
                heardHz: freq,
                accentedClickHz: WaitingCountInSettings.DefaultAccentedPitchHz,
                unaccentedClickHz: WaitingCountInSettings.DefaultUnaccentedPitchHz));
        Assert.Equal(0, session.CurrentNoteIndex);
    }

    [Fact]
    public void MatchingPitch_DuringConductorCueVisuals_StillRequiresUserNote_CountInOff()
    {
        // Conductor cues are visual-only; with Count-In OFF, a genuine matching pitch advances once.
        var session = CreateSessionFirstMidi(60);
        session.ShowConductorCues = true;
        WaitingCountInSettings.Enabled = false;
        session.StartListeningClock();

        double freq = NoteSessionService.MidiToFreqPublic(60);
        var result = session.Evaluate(freq);
        Assert.True(result.correct);
        Assert.True(session.UpdateFeedbackForCurrent(freq, result));
        Assert.Equal(1, session.CurrentNoteIndex);
        Assert.Contains(0, session.CorrectNoteIndices);
    }

    [Fact]
    public void ResidualAfterCue_SuppressZero_DoesNotAdvance()
    {
        var session = CreateSessionFirstMidi(60);
        session.StartListeningClock();
        session.SuppressCountInClickSelfSound(0); // guard-only residual flush
        Assert.True(session.ShouldIgnoreAudio(DateTime.UtcNow));

        double freq = NoteSessionService.MidiToFreqPublic(60);
        var result = session.Evaluate(freq);
        Assert.True(result.correct);
        Assert.False(
            WaitingCountInLogic.ShouldAcceptFirstNoteToEndCountIn(
                result.correct, true, 0, withinSelfSoundSuppressWindow: true));
        Assert.Equal(0, session.CurrentNoteIndex);
    }

    [Fact]
    public void GenuineFirstNote_AfterProtection_AdvancesExactlyOne()
    {
        var session = CreateSessionFirstMidi(60);
        session.StartListeningClock();
        Assert.False(session.ShouldIgnoreAudio(DateTime.UtcNow));

        double freq = NoteSessionService.MidiToFreqPublic(60);
        var result = session.Evaluate(freq);
        Assert.True(
            WaitingCountInLogic.ShouldAcceptFirstNoteToEndCountIn(
                result.correct, true, 0, withinSelfSoundSuppressWindow: false));
        Assert.True(session.UpdateFeedbackForCurrent(freq, result));
        Assert.Equal(1, session.CurrentNoteIndex);
        Assert.Contains(0, session.CorrectNoteIndices);
    }

    [Fact]
    public void SubsequentNote_AfterFirst_BehavesNormally()
    {
        double elapsed = 0;
        var session = CreateSessionFirstMidi(60, secondMidi: 62);
        session.SessionElapsedMsOverride = () => elapsed;
        session.StartListeningClock();
        session.SessionElapsedMsOverride = () => elapsed;

        double f0 = NoteSessionService.MidiToFreqPublic(60);
        Assert.True(session.UpdateFeedbackForCurrent(f0, session.Evaluate(f0)));
        Assert.Equal(1, session.CurrentNoteIndex);

        session.NotifySilence();
        elapsed = 2000; // past next note onset at 60 BPM

        double f1 = NoteSessionService.MidiToFreqPublic(62);
        var r1 = session.Evaluate(f1);
        Assert.True(r1.correct);
        Assert.True(session.UpdateFeedbackForCurrent(f1, r1));
        Assert.Equal(2, session.CurrentNoteIndex);
        Assert.Contains(1, session.CorrectNoteIndices);
    }

    [Fact]
    public void CountInOff_MatchingFirstPitch_AdvancesWithoutCountInGate()
    {
        WaitingCountInSettings.Enabled = false;
        var session = CreateSessionFirstMidi(60);
        session.StartListeningClock();

        double freq = NoteSessionService.MidiToFreqPublic(60);
        Assert.True(session.UpdateFeedbackForCurrent(freq, session.Evaluate(freq)));
        Assert.Equal(1, session.CurrentNoteIndex);
    }

    [Fact]
    public void ConductorCuesOff_MatchingFirstPitch_AdvancesNormally()
    {
        var session = CreateSessionFirstMidi(60);
        session.ShowConductorCues = false;
        session.StartListeningClock();

        double freq = NoteSessionService.MidiToFreqPublic(60);
        Assert.True(session.UpdateFeedbackForCurrent(freq, session.Evaluate(freq)));
        Assert.Equal(1, session.CurrentNoteIndex);
    }

    [Fact]
    public void SelfSoundDuration_IncludesReleaseAndLatencyBeyondNominalClick()
    {
        int nominal = 100;
        int selfSound = WaitingCountInLogic.ResolveClickSelfSoundDurationMs(nominal);
        Assert.True(selfSound > nominal);
        // release (15 or 80) + 60 latency
        Assert.Equal(100 + 80 + 60, selfSound);
    }

    [Fact]
    public void ExactSamePitchAsExpected_FarFromClick_MayEndCountInDuringSuppress()
    {
        var session = CreateSessionFirstMidi(60);
        session.StartListeningClock();
        session.SuppressCountInClickSelfSound(
            WaitingCountInLogic.ResolveClickSelfSoundDurationMs(150));

        double freq = NoteSessionService.MidiToFreqPublic(60);
        var result = session.Evaluate(freq);
        Assert.True(result.correct);
        Assert.True(session.ShouldIgnoreAudio(DateTime.UtcNow));
        Assert.True(
            WaitingCountInLogic.ShouldAcceptFirstNoteToEndCountIn(
                result.correct, true, 0, session.ShouldIgnoreAudio(DateTime.UtcNow),
                heardHz: freq,
                accentedClickHz: WaitingCountInSettings.DefaultAccentedPitchHz,
                unaccentedClickHz: WaitingCountInSettings.DefaultUnaccentedPitchHz));
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private static List<GeneratedNote> ShiftIdentity(
        IReadOnlyList<GeneratedNote> notes, double _) => notes.ToList();

    private static List<GeneratedNote> ShiftBy(
        IReadOnlyList<GeneratedNote> notes, double beatShift)
    {
        var list = new List<GeneratedNote>(notes.Count);
        foreach (var n in notes)
        {
            list.Add(new GeneratedNote
            {
                MidiNumber = n.MidiNumber,
                SpelledName = n.SpelledName,
                Duration = n.Duration,
                IsRest = n.IsRest,
                MeasureIndex = n.MeasureIndex,
                BeatPosition = (n.BeatPosition ?? 0.0) - beatShift,
            });
        }
        return list;
    }

    private static GeneratedNote NoteAt(int midi, double beat, int measure)
        => new()
        {
            MidiNumber = midi,
            SpelledName = $"N{midi}",
            Duration = NoteDuration.Quarter,
            IsRest = false,
            MeasureIndex = measure,
            BeatPosition = beat,
        };

    private static List<GeneratedNote> BuildOneMeasureTuneNotes()
        => new()
        {
            NoteAt(64, 0.0, 0), // E4 — Mary / Ode opener
            NoteAt(62, 1.0, 0),
            NoteAt(60, 2.0, 0),
            NoteAt(62, 3.0, 0),
        };

    private static List<GeneratedNote> BuildTwoMeasureTuneNotes(double measureBeats)
        => new()
        {
            NoteAt(60, 0.0, 0),
            NoteAt(62, 1.0, 0),
            NoteAt(64, 2.0, 0),
            NoteAt(65, 3.0, 0),
            NoteAt(67, measureBeats + 0.0, 1),
            NoteAt(65, measureBeats + 1.0, 1),
            NoteAt(64, measureBeats + 2.0, 1),
            NoteAt(62, measureBeats + 3.0, 1),
        };

    private static NoteSessionService CreateSessionFirstMidi(int firstMidi, int secondMidi = 62)
    {
        int[] midis = [firstMidi, secondMidi, firstMidi + 4];
        var session = new NoteSessionService
        {
            Instrument = "concert-pitch",
            Tune = "Practice Tune",
            CooldownMs = 0,
            Tempo = 60,
            ShowConductorCues = true,
            ChildLevel = 1,
            SampleRate = 44100,
            PitchWindowSize = 4096,
        };
        session.Reset();
        session.Tune = "Practice Tune";
        session.Instrument = "concert-pitch";
        session.CooldownMs = 0;
        session.Tempo = 60;
        session.MeterTimeSignature = "4/4";
        session.SampleRate = 44100;
        session.PitchWindowSize = 4096;
        session.ShowConductorCues = true;

        session.NotesToDraw.Clear();
        session.FeedbackViewModels.Clear();
        for (int i = 0; i < midis.Length; i++)
        {
            int midi = midis[i];
            session.NotesToDraw.Add(new NoteInfo
            {
                Midi = midi,
                Name = NoteSessionService.MidiToNoteName(midi, flats: false),
                TargetFreq = NoteSessionService.MidiToFreqPublic(midi),
                DurationBeats = 1,
                Duration = NoteDuration.Quarter,
                StartBeat = i,
                GateBeatsAfterPrevious = i == 0 ? 0 : 1,
            });
            session.FeedbackViewModels.Add(new FeedbackItem(i, 0, 0, false));
        }

        return session;
    }
}
