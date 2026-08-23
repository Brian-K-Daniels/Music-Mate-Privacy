using musicmate.Diagnostics;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Conductor-cue onset gating: expected times are anchored to session start × Music BPM,
/// not to the player's rush tempo. Uses an injected elapsed-ms clock (no Thread.Sleep).
/// </summary>
[Collection("SessionPreferences")]
public class ConductorTimingTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();
    private readonly bool _diagWasEnabled;

    // C major ascending scale (one octave): C D E F G A B C
    private static readonly int[] CMajorScaleMidi = [60, 62, 64, 65, 67, 69, 71, 72];

    public ConductorTimingTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
        _diagWasEnabled = TimingDiagnostics.EnableTimingDiagnostics;
        TimingDiagnostics.EnableTimingDiagnostics = false;
    }

    public void Dispose()
    {
        TimingDiagnostics.EnableTimingDiagnostics = _diagWasEnabled;
        SessionPreferences.TestStore = null;
    }

    [Theory]
    [InlineData(34)]
    [InlineData(60)]
    [InlineData(120)]
    public void ConductorOn_NotesNearScheduledTimes_AreAccepted(int bpm)
    {
        double elapsed = 0;
        var session = CreateConductorSession(CMajorScaleMidi, bpm, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        for (int i = 0; i < CMajorScaleMidi.Length; i++)
        {
            elapsed = i * msPerBeat;
            AssertAccepted(session, Freq(CMajorScaleMidi[i]), expectedIndex: i);
        }

        Assert.Equal(CMajorScaleMidi.Length, session.CurrentNoteIndex);
        Assert.Equal(CMajorScaleMidi.Length, session.CorrectNoteIndices.Count);
    }

    [Theory]
    [InlineData(34)]
    [InlineData(60)]
    [InlineData(120)]
    public void ConductorOn_RapidScaleWithinTwoSeconds_DoesNotAcceptAllAsTimed(int bpm)
    {
        double elapsed = 0;
        var session = CreateConductorSession(CMajorScaleMidi, bpm, () => elapsed);

        // Fire every pitch within ~1.6s — far faster than written quarters at 34/60/120 BPM.
        for (int i = 0; i < CMajorScaleMidi.Length; i++)
        {
            elapsed = i * (1600.0 / (CMajorScaleMidi.Length - 1));
            var freq = Freq(CMajorScaleMidi[i]);
            var result = session.Evaluate(freq);
            session.UpdateFeedbackForCurrent(freq, result);
            // Pitch lock / note-on: unlock residual so the next different pitch can be heard.
            session.NotifySilence();
        }

        // At most the first note (beat 0) can land inside the conductor window.
        Assert.True(
            session.CorrectNoteIndices.Count <= 2,
            $"Rapid rush accepted {session.CorrectNoteIndices.Count} notes at {bpm} BPM");
        Assert.True(
            session.CurrentNoteIndex < CMajorScaleMidi.Length,
            "Entire scale must not complete when rushed against the conductor");
    }

    [Fact]
    public void ConductorOn_NoteSubstantiallyBeforeWindow_DoesNotAdvance()
    {
        const int bpm = 34;
        double elapsed = 0;
        var session = CreateConductorSession([60, 62], bpm, () => elapsed);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        // Note 1 expected ~1765ms; try at 200ms (far before early tolerance ~441ms).
        elapsed = 200;
        AssertNotAdvanced(session, Freq(62), expectedIndex: 1);
        Assert.Equal(1, session.CurrentNoteIndex);
        Assert.DoesNotContain(1, session.CorrectNoteIndices);
    }

    [Fact]
    public void ConductorOn_NoteWithinWindow_AdvancesExactlyOnce()
    {
        const int bpm = 34;
        double elapsed = 0;
        var session = CreateConductorSession([60, 62], bpm, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        elapsed = msPerBeat;
        AssertAccepted(session, Freq(62), expectedIndex: 1);

        // Same pitch event / residual must not advance further (only two notes).
        Assert.Equal(2, session.CurrentNoteIndex);
        AssertRejected(session, Freq(62));
        Assert.Equal(2, session.CurrentNoteIndex);
    }

    [Fact]
    public void ConductorOn_OnePitchEvent_CannotSatisfyTwoSuccessiveNotes()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateConductorSession([60, 62, 64], bpm, () => elapsed);

        elapsed = 0;
        var result = session.Evaluate(Freq(60));
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), result));
        Assert.Equal(1, session.CurrentNoteIndex);

        // Same callback / residual C cannot mark D even if we jump the clock into D's window.
        elapsed = ConductorOnsetTiming.MsPerBeat(bpm);
        Assert.False(session.UpdateFeedbackForCurrent(Freq(60), result));
        Assert.Equal(1, session.CurrentNoteIndex);
        Assert.DoesNotContain(1, session.CorrectNoteIndices);
    }

    [Fact]
    public void ConductorOn_FutureExpectedTimes_StayAnchoredAfterEarlyAttempt()
    {
        const int bpm = 34;
        double elapsed = 0;
        var session = CreateConductorSession([60, 62, 64], bpm, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        // Rush note 1 early — rejected; expected time for note 1 must remain beat 1.
        elapsed = 100;
        AssertNotAdvanced(session, Freq(62), expectedIndex: 1);

        double expectedNote1 = ConductorOnsetTiming.ExpectedOnsetMs(0, 1.0, bpm);
        double expectedNote2 = ConductorOnsetTiming.ExpectedOnsetMs(0, 2.0, bpm);
        Assert.Equal(msPerBeat, expectedNote1, precision: 3);
        Assert.Equal(2 * msPerBeat, expectedNote2, precision: 3);

        // Accept note 1 at its original conductor time (not earlier because of the rush).
        elapsed = expectedNote1;
        AssertAccepted(session, Freq(62), expectedIndex: 1);

        // Note 2 still anchored at beat 2 — still blocked just after note 1.
        elapsed = expectedNote1 + 50;
        AssertNotAdvanced(session, Freq(64), expectedIndex: 2);

        elapsed = expectedNote2;
        AssertAccepted(session, Freq(64), expectedIndex: 2);
    }

    [Fact]
    public void ConductorOff_FreeTiming_Unchanged_RapidPitchesAdvance()
    {
        double elapsed = 0;
        var session = CreateSession(CMajorScaleMidi, bpm: 34, showConductorCues: false, () => elapsed);

        for (int i = 0; i < CMajorScaleMidi.Length; i++)
        {
            elapsed = i * 50; // rapid
            AssertAccepted(session, Freq(CMajorScaleMidi[i]), expectedIndex: i);
            session.NotifySilence();
        }

        Assert.Equal(CMajorScaleMidi.Length, session.CorrectNoteIndices.Count);
    }

    [Fact]
    public void GetAnchoredBeatPosition_UsesGateSpacingIncludingRests()
    {
        var notes = new List<NoteInfo>
        {
            new() { Midi = 60, DurationBeats = 1, GateBeatsAfterPrevious = 0, StartBeat = 0 },
            new() { Midi = 62, DurationBeats = 1, GateBeatsAfterPrevious = 2, StartBeat = 2 }, // quarter + rest
        };

        Assert.Equal(0, ConductorOnsetTiming.GetAnchoredBeatPosition(notes, 0));
        Assert.Equal(2, ConductorOnsetTiming.GetAnchoredBeatPosition(notes, 1));
    }

    [Fact]
    public void GetAnchoredBeatPosition_StaffBoundaryNegativeGate_FallsBackToDuration()
    {
        var notes = new List<NoteInfo>
        {
            new() { Midi = 60, DurationBeats = 1, GateBeatsAfterPrevious = 0, StartBeat = 7 },
            new() { Midi = 62, DurationBeats = 1, GateBeatsAfterPrevious = -7, StartBeat = 0 },
        };

        Assert.Equal(7, ConductorOnsetTiming.GetAnchoredBeatPosition(notes, 0));
        Assert.Equal(8, ConductorOnsetTiming.GetAnchoredBeatPosition(notes, 1));
    }

    [Fact]
    public void TolerancesAt34Bpm_MatchQuarterBeatFractions()
    {
        const int bpm = 34;
        double msPerBeat = 60000.0 / 34;
        Assert.Equal(msPerBeat, ConductorOnsetTiming.MsPerBeat(bpm), precision: 6);
        Assert.Equal(0.25 * msPerBeat, ConductorOnsetTiming.EarlyToleranceMs(bpm), precision: 6);
        Assert.Equal(0.50 * msPerBeat, ConductorOnsetTiming.LateToleranceMs(bpm), precision: 6);
    }

    private static NoteSessionService CreateConductorSession(
        int[] midiNotes,
        int bpm,
        Func<double> elapsedMs)
        => CreateSession(midiNotes, bpm, showConductorCues: true, elapsedMs);

    private static NoteSessionService CreateSession(
        int[] midiNotes,
        int bpm,
        bool showConductorCues,
        Func<double> elapsedMs)
    {
        var session = new NoteSessionService
        {
            Instrument = "concert-pitch",
            Tune = "Selected Scale",
            CooldownMs = 0,
            Tempo = bpm,
            ShowConductorCues = showConductorCues,
            ChildLevel = 1,
        };
        session.Reset();
        session.Tune = "Selected Scale";
        session.Instrument = "concert-pitch";
        session.CooldownMs = 0;
        session.Tempo = bpm;
        session.ShowConductorCues = showConductorCues;
        session.SamePitchSilenceMs = NoteSessionService.DefaultSamePitchSilenceMs;
        session.SessionElapsedMsOverride = elapsedMs;

        for (int i = 0; i < midiNotes.Length; i++)
        {
            int midi = midiNotes[i];
            session.NotesToDraw.Add(new NoteInfo
            {
                Midi = midi,
                Name = NoteSessionService.MidiToNoteName(midi, flats: false),
                TargetFreq = NoteSessionService.MidiToFreqPublic(midi),
                Duration = NoteDuration.Quarter,
                DurationBeats = 1,
                StartBeat = i,
                GateBeatsAfterPrevious = i == 0 ? 0 : 1,
            });
            session.FeedbackViewModels.Add(new FeedbackItem(i, 0, 0, false));
        }

        session.ConfigureRhythmStartGates();
        session.StartListeningClock();
        // Keep injected clock authoritative after StartListeningClock.
        session.SessionElapsedMsOverride = elapsedMs;
        return session;
    }

    private static double Freq(int midi) => NoteSessionService.MidiToFreqPublic(midi);

    private static void AssertAccepted(NoteSessionService session, double freq, int expectedIndex)
    {
        var result = session.Evaluate(freq);
        Assert.True(result.correct, $"Expected pitch match at index {expectedIndex}");
        Assert.True(
            session.UpdateFeedbackForCurrent(freq, result),
            $"Expected note {expectedIndex} to be accepted at elapsed override");
        Assert.Contains(expectedIndex, session.CorrectNoteIndices);
        Assert.Equal(expectedIndex + 1, session.CurrentNoteIndex);
    }

    private static void AssertNotAdvanced(NoteSessionService session, double freq, int expectedIndex)
    {
        int indexBefore = session.CurrentNoteIndex;
        Assert.Equal(expectedIndex, indexBefore);
        var correctBefore = session.CorrectNoteIndices.ToHashSet();
        var result = session.Evaluate(freq);
        // May return true when Early wrong feedback is recorded, but must not advance.
        session.UpdateFeedbackForCurrent(freq, result);
        Assert.Equal(indexBefore, session.CurrentNoteIndex);
        Assert.Equal(correctBefore, session.CorrectNoteIndices);
        Assert.DoesNotContain(expectedIndex, session.CorrectNoteIndices);
    }

    private static void AssertRejected(NoteSessionService session, double freq)
    {
        int indexBefore = session.CurrentNoteIndex;
        var correctBefore = session.CorrectNoteIndices.ToHashSet();
        var result = session.Evaluate(freq);
        Assert.False(session.UpdateFeedbackForCurrent(freq, result));
        Assert.Equal(indexBefore, session.CurrentNoteIndex);
        Assert.Equal(correctBefore, session.CorrectNoteIndices);
    }
}
