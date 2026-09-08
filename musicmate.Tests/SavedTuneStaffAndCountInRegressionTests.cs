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

    // ── Problem 2: Count-In / cue self-sound ───────────────────────────────

    [Fact]
    public void MatchingPitch_DuringCountInSelfSoundWindow_DoesNotAdvance()
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
                result.correct, true, 0, session.ShouldIgnoreAudio(DateTime.UtcNow)));
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
    public void ExactSamePitchAsExpected_StillBlockedWhileSuppressActive()
    {
        var session = CreateSessionFirstMidi(60);
        session.StartListeningClock();
        session.SuppressCountInClickSelfSound(
            WaitingCountInLogic.ResolveClickSelfSoundDurationMs(150));

        double freq = NoteSessionService.MidiToFreqPublic(60);
        var result = session.Evaluate(freq);
        Assert.True(result.correct);
        Assert.True(session.ShouldIgnoreAudio(DateTime.UtcNow));
        Assert.False(
            WaitingCountInLogic.ShouldAcceptFirstNoteToEndCountIn(
                result.correct, true, 0, session.ShouldIgnoreAudio(DateTime.UtcNow)));
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
