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

        // Note 1 expected ~1765ms; try at 200ms (far before early tolerance ~30% of a beat).
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
    public void Tempo30_FourQuarterNotes_RushedAt120BpmPace_DoesNotAcceptNotesTwoThroughFour()
    {
        const int bpm = 30;
        int[] midi = [60, 62, 64, 65]; // C D E F
        double elapsed = 0;
        var session = CreateSession(midi, bpm, showConductorCues: false, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);
        double rushInterval = ConductorOnsetTiming.MsPerBeat(120); // 500 ms

        // Expected onsets: 0, 2000, 4000, 6000 ms
        Assert.Equal(0, ConductorOnsetTiming.ExpectedOnsetMs(0, 0, bpm));
        Assert.Equal(msPerBeat, ConductorOnsetTiming.ExpectedOnsetMs(0, 1, bpm), precision: 3);
        Assert.Equal(2 * msPerBeat, ConductorOnsetTiming.ExpectedOnsetMs(0, 2, bpm), precision: 3);
        Assert.Equal(3 * msPerBeat, ConductorOnsetTiming.ExpectedOnsetMs(0, 3, bpm), precision: 3);

        elapsed = 0;
        AssertAccepted(session, Freq(midi[0]), expectedIndex: 0);

        // Player races through all four pitches at ~120 BPM (500 ms apart).
        int[] arrivalMs = [500, 1000, 1500];
        for (int i = 0; i < arrivalMs.Length; i++)
        {
            elapsed = arrivalMs[i];
            var freq = Freq(midi[i + 1]);
            var result = session.Evaluate(freq);
            session.UpdateFeedbackForCurrent(freq, result);
            session.NotifySilence();
        }

        Assert.Equal(1, session.CurrentNoteIndex);
        Assert.Single(session.CorrectNoteIndices);
    }

    [Fact]
    public void Tempo30_FourQuarterNotes_AtWrittenTempo_AcceptsAll()
    {
        const int bpm = 30;
        int[] midi = [60, 62, 64, 65];
        double elapsed = 0;
        var session = CreateSession(midi, bpm, showConductorCues: false, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        for (int i = 0; i < midi.Length; i++)
        {
            elapsed = i * msPerBeat;
            AssertAccepted(session, Freq(midi[i]), expectedIndex: i);
        }

        Assert.Equal(midi.Length, session.CurrentNoteIndex);
        Assert.Equal(midi.Length, session.CorrectNoteIndices.Count);
    }

    [Fact]
    public void EighthNotes_At60Bpm_RequireHalfBeatSpacing()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateRhythmSession(
            bpm,
            showConductorCues: false,
            () => elapsed,
            new RhythmNote(60, 0.5, 0),
            new RhythmNote(62, 0.5, 0.5),
            new RhythmNote(64, 0.5, 0.5));

        double eighthMs = ConductorOnsetTiming.MsPerBeat(bpm) / 2; // 500 ms
        double earlyTol = ConductorOnsetTiming.EarlyToleranceMs(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        // Clearly before beat 0.5 — outside the early window (earliest ≈ 500 − earlyTol).
        elapsed = Math.Max(1, eighthMs - earlyTol - 50);
        AssertNotAdvanced(session, Freq(62), expectedIndex: 1);

        elapsed = eighthMs;
        AssertAccepted(session, Freq(62), expectedIndex: 1);

        elapsed = Math.Max(eighthMs + 1, 2 * eighthMs - earlyTol - 50);
        AssertNotAdvanced(session, Freq(64), expectedIndex: 2);

        elapsed = 2 * eighthMs;
        AssertAccepted(session, Freq(64), expectedIndex: 2);
    }

    [Fact]
    public void HalfNotes_At30Bpm_SecondNoteExpectedAt4000Ms()
    {
        const int bpm = 30;
        double elapsed = 0;
        var session = CreateRhythmSession(
            bpm,
            showConductorCues: false,
            () => elapsed,
            new RhythmNote(60, 2, 0),
            new RhythmNote(62, 1, 2));

        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);
        Assert.Equal(2 * msPerBeat, ConductorOnsetTiming.ExpectedOnsetMs(0, 2, bpm), precision: 3);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        elapsed = msPerBeat; // 2000 ms — still within first half note
        AssertNotAdvanced(session, Freq(62), expectedIndex: 1);

        elapsed = 2 * msPerBeat;
        AssertAccepted(session, Freq(62), expectedIndex: 1);
    }

    [Fact]
    public void RestBetweenNotes_PushesExpectedOnsetByRestBeats()
    {
        const int bpm = 60;
        double elapsed = 0;
        // Quarter, quarter rest (gate=2), quarter
        var session = CreateRhythmSession(
            bpm,
            showConductorCues: false,
            () => elapsed,
            new RhythmNote(60, 1, 0),
            new RhythmNote(62, 1, 2));

        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);
        Assert.Equal(2 * msPerBeat, ConductorOnsetTiming.ExpectedOnsetMs(0, 2, bpm), precision: 3);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        elapsed = msPerBeat; // beat 1 — rest; note 2 expected at beat 2
        AssertNotAdvanced(session, Freq(62), expectedIndex: 1);

        elapsed = 2 * msPerBeat;
        AssertAccepted(session, Freq(62), expectedIndex: 1);
    }

    [Fact]
    public void TempoChange_MidSession_UsesNewBpmForRemainingNotes()
    {
        const int startBpm = 30;
        double elapsed = 0;
        var session = CreateSession([60, 62, 64], startBpm, showConductorCues: false, () => elapsed);
        double ms30 = ConductorOnsetTiming.MsPerBeat(30);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        session.Tempo = 60;
        double ms60 = ConductorOnsetTiming.MsPerBeat(60);

        // Note 1 still at beat 1; now 1000 ms at 60 BPM
        elapsed = ms60;
        AssertAccepted(session, Freq(62), expectedIndex: 1);

        // Note 2 at beat 2 under 60 BPM = 2000 ms
        elapsed = 2 * ms60;
        AssertAccepted(session, Freq(64), expectedIndex: 2);

        Assert.Equal(3, session.CurrentNoteIndex);
    }

    [Fact]
    public void EarlyTolerance_AllowsAboutQuarterBeatEarly()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateSession([60, 62], bpm, showConductorCues: false, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);
        double earlyTol = ConductorOnsetTiming.EarlyToleranceMs(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        // Just inside early tolerance (beat 1 minus a hair)
        elapsed = msPerBeat - earlyTol + 1;
        AssertAccepted(session, Freq(62), expectedIndex: 1);
    }

    [Fact]
    public void LateTolerance_AllowsSlightlyLateWithinHalfBeat()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateSession([60, 62], bpm, showConductorCues: false, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);
        double lateTol = ConductorOnsetTiming.LateToleranceMs(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        elapsed = msPerBeat + lateTol - 1;
        AssertAccepted(session, Freq(62), expectedIndex: 1);
    }

    [Fact]
    public void OnTimeNote_IsAcceptedAsCorrect()
    {
        const int bpm = 52;
        double elapsed = 0;
        var session = CreateSession([60, 62], bpm, showConductorCues: false, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);
        AssertNoWrongFeedback(session, 0);

        elapsed = msPerBeat;
        AssertAccepted(session, Freq(62), expectedIndex: 1);
        AssertNoWrongFeedback(session, 1);
    }

    [Theory]
    [InlineData(0.20)]
    [InlineData(0.25)]
    public void AboutTwentyToTwentyFivePercentBeatEarly_IsAccepted(double earlyFractionOfBeat)
    {
        const int bpm = 52;
        double elapsed = 0;
        var session = CreateSession([60, 62], bpm, showConductorCues: false, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        elapsed = msPerBeat - earlyFractionOfBeat * msPerBeat;
        Assert.True(
            elapsed >= msPerBeat - ConductorOnsetTiming.EarlyToleranceMs(bpm),
            "test offset must lie inside the configured early window");
        AssertAccepted(session, Freq(62), expectedIndex: 1);
        AssertNoWrongFeedback(session, 1);
    }

    [Theory]
    [InlineData(0.20)]
    [InlineData(0.25)]
    public void AboutTwentyToTwentyFivePercentBeatLate_IsAccepted(double lateFractionOfBeat)
    {
        const int bpm = 52;
        double elapsed = 0;
        var session = CreateSession([60, 62], bpm, showConductorCues: false, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        elapsed = msPerBeat + lateFractionOfBeat * msPerBeat;
        Assert.True(
            elapsed <= msPerBeat + ConductorOnsetTiming.LateToleranceMs(bpm),
            "test offset must lie inside the configured late window");
        AssertAccepted(session, Freq(62), expectedIndex: 1);
        AssertNoWrongFeedback(session, 1);
    }

    [Fact]
    public void SubstantiallyEarlierThanTolerance_IsWrongAndDoesNotAdvance()
    {
        const int bpm = 52;
        double elapsed = 0;
        var session = CreateSession([60, 62], bpm, showConductorCues: false, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);
        double earlyTol = ConductorOnsetTiming.EarlyToleranceMs(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        // ~55% of a beat early — beyond the ~30% early window.
        elapsed = msPerBeat - earlyTol - 0.25 * msPerBeat;
        Assert.True(elapsed > 0);
        AssertNotAdvanced(session, Freq(62), expectedIndex: 1);
        Assert.Equal(1, session.CurrentNoteIndex);
        Assert.DoesNotContain(1, session.CorrectNoteIndices);
        Assert.True(session.NoteFeedbacks.TryGetValue(1, out var fb) && fb.Wrong > 0);
    }

    [Fact]
    public void Tempo52_CMajorScale_WithOrdinaryTimingJitter_StaysCorrect()
    {
        const int bpm = 52;
        double elapsed = 0;
        var session = CreateSession(CMajorScaleMidi, bpm, showConductorCues: false, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);
        // Ordinary human variation: alternate ~±20% of a beat around each onset.
        double[] jitterFractions = [0, -0.20, 0.18, -0.15, 0.22, -0.10, 0.20, -0.12];

        for (int i = 0; i < CMajorScaleMidi.Length; i++)
        {
            elapsed = i * msPerBeat + jitterFractions[i] * msPerBeat;
            AssertAccepted(session, Freq(CMajorScaleMidi[i]), expectedIndex: i);
            AssertNoWrongFeedback(session, i);
            session.NotifySilence();
        }

        Assert.Equal(CMajorScaleMidi.Length, session.CorrectNoteIndices.Count);
    }

    [Fact]
    public void Tempo52_PlayingNear120Bpm_DoesNotMarkEntireScaleGreen()
    {
        const int sessionBpm = 52;
        const int playedApproxBpm = 120;
        double elapsed = 0;
        var session = CreateSession(CMajorScaleMidi, sessionBpm, showConductorCues: false, () => elapsed);
        double playedMsPerBeat = ConductorOnsetTiming.MsPerBeat(playedApproxBpm);

        for (int i = 0; i < CMajorScaleMidi.Length; i++)
        {
            elapsed = i * playedMsPerBeat;
            var freq = Freq(CMajorScaleMidi[i]);
            session.UpdateFeedbackForCurrent(freq, session.Evaluate(freq));
            session.NotifySilence();
        }

        Assert.True(
            session.CorrectNoteIndices.Count <= 2,
            $"Rush at ~{playedApproxBpm} BPM against {sessionBpm} BPM accepted {session.CorrectNoteIndices.Count} notes");
        Assert.True(
            session.CurrentNoteIndex < CMajorScaleMidi.Length,
            "Entire scale must not complete when played far ahead of the selected tempo");
    }

    [Fact]
    public void RepeatedPitchOnConsecutiveBeats_RequiresTimingAndNoteOn()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateSession([60, 60], bpm, showConductorCues: false, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);
        double freq = Freq(60);

        elapsed = 0;
        AssertAccepted(session, freq, expectedIndex: 0);
        Assert.True(session.IsAwaitingNoteOn);

        // Same pitch before beat 1 — blocked (timing + note-on)
        elapsed = msPerBeat / 2;
        AssertNotAdvanced(session, freq, expectedIndex: 1);

        session.NotifySilenceFor(session.SamePitchSilenceMs);
        session.NotifyNoteAttack();
        elapsed = msPerBeat;
        AssertAccepted(session, freq, expectedIndex: 1);
    }

    [Fact]
    public void EarlyAttemptThenCorrectAtOnset_AcceptsOnRetry()
    {
        const int bpm = 30;
        double elapsed = 0;
        var session = CreateSession([60, 62], bpm, showConductorCues: false, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        elapsed = 500;
        AssertNotAdvanced(session, Freq(62), expectedIndex: 1);
        Assert.True(session.NoteFeedbacks.TryGetValue(1, out var fb) && fb.Wrong > 0);

        session.NotifySilence();
        elapsed = msPerBeat;
        AssertAccepted(session, Freq(62), expectedIndex: 1);

        // Early row is superseded — one final attempt with hadEarlyCandidate detail.
        var forNote1 = session.GetSessionAttemptOutcomes()
            .Where(o => o.NoteIndex == 1)
            .ToList();
        Assert.Single(forNote1);
        Assert.True(forNote1[0].OverallCorrect);
        Assert.True(forNote1[0].HadEarlyCandidate);
        Assert.Equal(NoteAttemptTimingDiagnostics.HadEarlyCandidateReason, forNote1[0].WrongReason);
        Assert.DoesNotContain(
            session.GetSessionAttemptOutcomes(),
            o => o.NoteIndex == 1 && o.WrongReason == "Early");
        // Accepted onset classification follows TimingErrorMs, not HadEarlyCandidate.
        Assert.Equal(
            NoteAttemptTimingDiagnostics.ClassifyAcceptedOnset(forNote1[0].TimingErrorMs),
            forNote1[0].TimingErrorMs < 0 ? "early"
                : forNote1[0].TimingErrorMs > 0 ? "late" : "onTime");
    }

    [Fact]
    public void FirstNote_LateCatchUp_RebasesOriginAndAcceptsOnTime()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateSession([60, 62], bpm, showConductorCues: false, () => elapsed);
        double lateTol = ConductorOnsetTiming.LateToleranceMs(bpm);

        // Clock already running; first matching pitch arrives after the late window.
        elapsed = lateTol + 50;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        var note0 = session.GetSessionAttemptOutcomes()
            .Where(o => o.NoteIndex == 0)
            .ToList();
        Assert.Single(note0);
        Assert.True(note0[0].OverallCorrect);
        Assert.True(note0[0].PitchCorrect);
        Assert.NotEqual("Late", note0[0].WrongReason);
        Assert.True(
            note0[0].TimingCorrect == true
            || Math.Abs(note0[0].TimingErrorMs ?? 999) < 1.0,
            "First-note origin rebase should yield on-time timing");

        // Next note still due one beat after the rebased first onset.
        elapsed = lateTol + 50 + ConductorOnsetTiming.MsPerBeat(bpm);
        AssertAccepted(session, Freq(62), expectedIndex: 1);
    }

    [Fact]
    public void Tempo30_EarlyCorrectPitch_MarksWrongAndDoesNotAdvance()
    {
        const int bpm = 30;
        double elapsed = 0;
        var session = CreateSession([60, 62, 64], bpm, showConductorCues: false, () => elapsed);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        elapsed = 400; // well before beat 1 at 2000ms
        var result = session.Evaluate(Freq(62));
        Assert.True(result.correct);
        Assert.True(session.UpdateFeedbackForCurrent(Freq(62), result));
        Assert.Equal(1, session.CurrentNoteIndex);
        Assert.True(session.NoteFeedbacks.TryGetValue(1, out var fb) && fb.Wrong > 0);
    }

    [Fact]
    public void Tempo30_ConductorCuesOff_BlocksNotePlayedAt120BpmPace()
    {
        const int bpm = 30;
        double elapsed = 0;
        var session = CreateSession([60, 62], bpm, showConductorCues: false, () => elapsed);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        // ~120 BPM ≈ 500ms per quarter; written tempo is 30 BPM (2000ms per quarter).
        elapsed = 500;
        AssertNotAdvanced(session, Freq(62), expectedIndex: 1);
    }

    [Fact]
    public void Tempo30_ConductorCuesOff_AcceptsNotesAtWrittenTempo()
    {
        const int bpm = 30;
        double elapsed = 0;
        var session = CreateSession(CMajorScaleMidi, bpm, showConductorCues: false, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        for (int i = 0; i < CMajorScaleMidi.Length; i++)
        {
            elapsed = i * msPerBeat;
            AssertAccepted(session, Freq(CMajorScaleMidi[i]), expectedIndex: i);
        }

        Assert.Equal(CMajorScaleMidi.Length, session.CurrentNoteIndex);
    }

    [Fact]
    public void ConductorOff_FreeTiming_Unchanged_RapidPitchesAdvance()
    {
        double elapsed = 0;
        // Tuner mode skips conductor onset gating entirely.
        var session = CreateSession(CMajorScaleMidi, bpm: 34, showConductorCues: false, () => elapsed);
        session.Tune = "Tuner";

        for (int i = 0; i < CMajorScaleMidi.Length; i++)
        {
            elapsed = i * 50; // rapid
            AssertAccepted(session, Freq(CMajorScaleMidi[i]), expectedIndex: i);
            session.NotifySilence();
        }

        Assert.Equal(CMajorScaleMidi.Length, session.CorrectNoteIndices.Count);
    }

    [Fact]
    public void LateCorrectPitch_MarksWrongAndAdvances()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateSession([60, 62], bpm, showConductorCues: false, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);
        double lateTol = ConductorOnsetTiming.LateToleranceMs(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        elapsed = msPerBeat + lateTol + 100;
        var result = session.Evaluate(Freq(62));
        Assert.True(result.correct);
        Assert.True(session.UpdateFeedbackForCurrent(Freq(62), result));
        Assert.Equal(2, session.CurrentNoteIndex);
        Assert.DoesNotContain(1, session.CorrectNoteIndices);
        Assert.True(session.NoteFeedbacks.TryGetValue(1, out var fb) && fb.Wrong > 0);
    }

    [Fact]
    public void AfterLateNote_NextNoteCanBePlayedOnTime()
    {
        const int bpm = 30;
        double elapsed = 0;
        var session = CreateSession([60, 62, 64], bpm, showConductorCues: false, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);
        double lateTol = ConductorOnsetTiming.LateToleranceMs(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        elapsed = msPerBeat + lateTol + 50;
        session.UpdateFeedbackForCurrent(Freq(62), session.Evaluate(Freq(62)));
        Assert.Equal(2, session.CurrentNoteIndex);

        elapsed = 2 * msPerBeat;
        session.NotifySilenceFor(session.SamePitchSilenceMs);
        session.NotifyNoteAttack();
        AssertAccepted(session, Freq(64), expectedIndex: 2);
    }

    [Fact]
    public void SilentMiss_PausesAtCurrentNoteWithoutAdvancing()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateSession([60, 62, 64], bpm, showConductorCues: false, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);
        double lateTol = ConductorOnsetTiming.LateToleranceMs(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        elapsed = msPerBeat + lateTol + 200;
        Assert.False(session.AdvanceTimelineForExpiredNotes());
        Assert.True(session.IsMusicalTimelinePaused);
        Assert.Equal(1, session.CurrentNoteIndex);
        AssertNoWrongFeedback(session, 1);
        AssertNoWrongFeedback(session, 2);

        elapsed = 2 * msPerBeat + lateTol + 200;
        session.NotifySilenceFor(session.SamePitchSilenceMs);
        session.NotifyNoteAttack();
        AssertAccepted(session, Freq(62), expectedIndex: 1);
        Assert.False(session.IsMusicalTimelinePaused);
        AssertNoWrongFeedback(session, 2);
    }

    [Fact]
    public void TwoMissedNotes_SilencePausesAtFirstUnplayedNote()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateSession([60, 62, 64, 65], bpm, showConductorCues: false, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        elapsed = 3 * msPerBeat;
        Assert.False(session.AdvanceTimelineForExpiredNotes());
        Assert.True(session.IsMusicalTimelinePaused);
        Assert.Equal(1, session.CurrentNoteIndex);
        AssertNoWrongFeedback(session, 1);
        AssertNoWrongFeedback(session, 2);
        AssertNoWrongFeedback(session, 3);
    }

    [Fact]
    public void LongSilenceBeforeNextPitch_RebasesRatherThanKeepingOriginalOnsets()
    {
        const int bpm = 30;
        double elapsed = 0;
        var session = CreateSession([60, 62, 64], bpm, showConductorCues: false, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        elapsed = 5000;
        AssertAccepted(session, Freq(62), expectedIndex: 1);
        Assert.False(session.IsMusicalTimelinePaused);

        elapsed = 5000 + msPerBeat;
        session.NotifySilenceFor(session.SamePitchSilenceMs);
        session.NotifyNoteAttack();
        AssertAccepted(session, Freq(64), expectedIndex: 2);
    }

    [Fact]
    public void CatchUp_WithRest_SilencePausesAtNextPitchedNote()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateRhythmSession(
            bpm,
            showConductorCues: false,
            () => elapsed,
            new RhythmNote(60, 1, 0),
            new RhythmNote(62, 1, 2));

        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);
        double lateTol = ConductorOnsetTiming.LateToleranceMs(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        elapsed = 2 * msPerBeat + lateTol + 100;
        Assert.False(session.AdvanceTimelineForExpiredNotes());
        Assert.True(session.IsMusicalTimelinePaused);
        Assert.Equal(1, session.CurrentNoteIndex);
        AssertNoWrongFeedback(session, 1);
    }

    [Fact]
    public void FinalExpiredNote_PausesInsteadOfAutoCompleting()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateSession([60, 62], bpm, showConductorCues: false, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);
        double lateTol = ConductorOnsetTiming.LateToleranceMs(bpm);

        elapsed = 0;
        AssertAccepted(session, Freq(60), expectedIndex: 0);

        elapsed = msPerBeat + lateTol + 100;
        Assert.False(session.AdvanceTimelineForExpiredNotes());
        Assert.True(session.IsMusicalTimelinePaused);
        Assert.Equal(1, session.CurrentNoteIndex);
        Assert.False(session.SessionCompleted);
        Assert.Single(session.CorrectNoteIndices);
        AssertNoWrongFeedback(session, 1);
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
    public void TolerancesAt52Bpm_MatchBeatFractionsWithClamp()
    {
        const int bpm = 52;
        double msPerBeat = 60000.0 / 52;
        Assert.Equal(msPerBeat, ConductorOnsetTiming.MsPerBeat(bpm), precision: 6);
        Assert.Equal(
            ConductorOnsetTiming.ClampToleranceMs(
                ConductorOnsetTiming.EarlyToleranceBeats * msPerBeat),
            ConductorOnsetTiming.EarlyToleranceMs(bpm),
            precision: 6);
        Assert.Equal(
            ConductorOnsetTiming.ClampToleranceMs(
                ConductorOnsetTiming.LateToleranceBeats * msPerBeat),
            ConductorOnsetTiming.LateToleranceMs(bpm),
            precision: 6);
        // At 52 BPM the raw beat fractions sit inside the clamp range.
        Assert.InRange(
            ConductorOnsetTiming.EarlyToleranceMs(bpm),
            0.29 * msPerBeat,
            0.31 * msPerBeat);
        Assert.InRange(
            ConductorOnsetTiming.LateToleranceMs(bpm),
            0.49 * msPerBeat,
            0.51 * msPerBeat);
    }

    [Fact]
    public void TolerancesAt34Bpm_MatchBeatFractionsWithClamp()
    {
        const int bpm = 34;
        double msPerBeat = 60000.0 / 34;
        Assert.Equal(msPerBeat, ConductorOnsetTiming.MsPerBeat(bpm), precision: 6);
        Assert.Equal(
            ConductorOnsetTiming.ClampToleranceMs(
                ConductorOnsetTiming.EarlyToleranceBeats * msPerBeat),
            ConductorOnsetTiming.EarlyToleranceMs(bpm),
            precision: 6);
        Assert.Equal(
            ConductorOnsetTiming.ClampToleranceMs(
                ConductorOnsetTiming.LateToleranceBeats * msPerBeat),
            ConductorOnsetTiming.LateToleranceMs(bpm),
            precision: 6);
    }

    private readonly record struct RhythmNote(
        int Midi,
        double DurationBeats,
        double GateBeatsAfterPrevious);

    private static NoteSessionService CreateRhythmSession(
        int bpm,
        bool showConductorCues,
        Func<double> elapsedMs,
        params RhythmNote[] notes)
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

        for (int i = 0; i < notes.Length; i++)
        {
            var spec = notes[i];
            session.NotesToDraw.Add(new NoteInfo
            {
                Midi = spec.Midi,
                Name = NoteSessionService.MidiToNoteName(spec.Midi, flats: false),
                TargetFreq = NoteSessionService.MidiToFreqPublic(spec.Midi),
                Duration = DurationFromBeats(spec.DurationBeats),
                DurationBeats = spec.DurationBeats,
                StartBeat = ConductorOnsetTiming.GetAnchoredBeatPosition(
                    BuildNoteListUpTo(notes, i), i),
                GateBeatsAfterPrevious = spec.GateBeatsAfterPrevious,
            });
            session.FeedbackViewModels.Add(new FeedbackItem(i, 0, 0, false));
        }

        session.ConfigureRhythmStartGates();
        session.StartListeningClock();
        session.SessionElapsedMsOverride = elapsedMs;
        return session;
    }

    private static List<NoteInfo> BuildNoteListUpTo(RhythmNote[] specs, int index)
    {
        var list = new List<NoteInfo>();
        for (int i = 0; i <= index; i++)
        {
            list.Add(new NoteInfo
            {
                DurationBeats = specs[i].DurationBeats,
                GateBeatsAfterPrevious = specs[i].GateBeatsAfterPrevious,
            });
        }
        return list;
    }

    private static NoteDuration DurationFromBeats(double beats) => beats switch
    {
        >= 4 => NoteDuration.Whole,
        >= 2 => NoteDuration.Half,
        >= 1 => NoteDuration.Quarter,
        >= 0.5 => NoteDuration.Eighth,
        _ => NoteDuration.Sixteenth,
    };

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

    [Fact]
    public void SessionRelativeOnset_FirstNoteWithScoreStartBeat_AcceptsAtClockZero()
    {
        const int bpm = 98;
        double elapsed = 0;
        var session = new NoteSessionService
        {
            Instrument = "concert-pitch",
            Tune = "Selected Scale",
            CooldownMs = 0,
            Tempo = bpm,
            ShowConductorCues = true,
            ChildLevel = 1,
        };
        session.Reset();
        session.Tune = "Selected Scale";
        session.Instrument = "concert-pitch";
        session.CooldownMs = 0;
        session.Tempo = bpm;
        session.ShowConductorCues = true;
        session.SamePitchSilenceMs = NoteSessionService.DefaultSamePitchSilenceMs;
        session.SessionElapsedMsOverride = () => elapsed;

        session.NotesToDraw.Add(new NoteInfo
        {
            Midi = 67,
            Name = NoteSessionService.MidiToNoteName(67, flats: false),
            TargetFreq = Freq(67),
            Duration = NoteDuration.Quarter,
            DurationBeats = 1,
            StartBeat = 4,
            GateBeatsAfterPrevious = 0,
        });
        session.FeedbackViewModels.Add(new FeedbackItem(0, 0, 0, false));
        session.NotesToDraw.Add(new NoteInfo
        {
            Midi = 69,
            Name = NoteSessionService.MidiToNoteName(69, flats: false),
            TargetFreq = Freq(69),
            Duration = NoteDuration.Quarter,
            DurationBeats = 1,
            StartBeat = 5,
            GateBeatsAfterPrevious = 1,
        });
        session.FeedbackViewModels.Add(new FeedbackItem(1, 0, 0, false));
        session.ConfigureRhythmStartGates();
        session.StartListeningClock();

        elapsed = 0;
        AssertAccepted(session, Freq(67), expectedIndex: 0);
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

    private static void AssertNoWrongFeedback(NoteSessionService session, int index)
    {
        if (session.NoteFeedbacks.TryGetValue(index, out var fb))
            Assert.Equal(0, fb.Wrong);
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
