using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Ensures one detected / sustained pitch can mark only one displayed note correct;
/// each subsequent note needs a fresh note-on (silence, pitch stop, or attack).
/// </summary>
[Collection("SessionPreferences")]
public class NoteOnGatingTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public NoteOnGatingTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose()
    {
        SessionPreferences.TestStore = null;
    }

    [Fact]
    public void ThreeDifferentConsecutiveNotes_OneDetectionMarksOnlyCurrent()
    {
        var session = CreateSession(midiNotes: [60, 62, 64]); // C D E
        double cFreq = Freq(60);
        double dFreq = Freq(62);
        double eFreq = Freq(64);

        AssertAccepted(session, cFreq, expectedIndex: 0);
        Assert.Equal(1, session.CurrentNoteIndex);
        Assert.False(session.IsAwaitingNoteOn); // different next pitch: pitch-lock only

        // Sustained C must not advance past D
        AssertRejected(session, cFreq);
        Assert.Equal(1, session.CurrentNoteIndex);
        Assert.DoesNotContain(1, session.CorrectNoteIndices);

        session.NotifySilence();
        AssertAccepted(session, dFreq, expectedIndex: 1);
        Assert.Equal(2, session.CurrentNoteIndex);
        Assert.False(session.IsAwaitingNoteOn);

        // Holding D must not mark E
        AssertRejected(session, dFreq);
        Assert.Equal(2, session.CurrentNoteIndex);

        session.NotifySilence();
        AssertAccepted(session, eFreq, expectedIndex: 2);
        Assert.Equal(3, session.CurrentNoteIndex);
        Assert.Equal(new HashSet<int> { 0, 1, 2 }, session.CorrectNoteIndices);
    }

    [Fact]
    public void TwoIdenticalConsecutiveNotes_SustainedToneMarksOnlyFirst()
    {
        var session = CreateSession(midiNotes: [60, 60]);
        double freq = Freq(60);

        AssertAccepted(session, freq, expectedIndex: 0);
        Assert.True(session.IsAwaitingNoteOn);

        AssertRejected(session, freq);
        AssertRejected(session, freq);
        Assert.Equal(1, session.CurrentNoteIndex);
        Assert.Equal(new HashSet<int> { 0 }, session.CorrectNoteIndices);
    }

    [Fact]
    public void ThreeIdenticalConsecutiveNotes_SustainedToneMarksOnlyFirst()
    {
        var session = CreateSession(midiNotes: [64, 64, 64]);
        double freq = Freq(64);

        AssertAccepted(session, freq, expectedIndex: 0);

        for (int i = 0; i < 5; i++)
            AssertRejected(session, freq);

        Assert.Equal(1, session.CurrentNoteIndex);
        Assert.Equal(new HashSet<int> { 0 }, session.CorrectNoteIndices);
        Assert.DoesNotContain(1, session.CorrectNoteIndices);
        Assert.DoesNotContain(2, session.CorrectNoteIndices);
    }

    [Fact]
    public void SustainedNoteHeldAcrossSeveralDisplayedNotes_MarksOnlyOne()
    {
        var session = CreateSession(midiNotes: [60, 60, 62, 60]);
        double cFreq = Freq(60);

        AssertAccepted(session, cFreq, expectedIndex: 0);

        // Hold C across what would be several displayed slots — including amplitude wobble
        for (int i = 0; i < 8; i++)
        {
            session.ObserveLoudness(0.05f);
            session.ObserveLoudness(0.02f); // dip that must NOT unlock same-pitch
            session.ObserveLoudness(0.055f);
            AssertRejected(session, cFreq);
        }

        Assert.Equal(1, session.CurrentNoteIndex);
        Assert.Equal(new HashSet<int> { 0 }, session.CorrectNoteIndices);
        Assert.True(session.IsAwaitingNoteOn);
    }

    [Fact]
    public void SamePitchAsDistinctSeparateAttacks_MarksEachNote()
    {
        var session = CreateSession(midiNotes: [60, 60, 60]);
        double freq = Freq(60);

        AssertAccepted(session, freq, expectedIndex: 0);
        Assert.True(session.IsAwaitingNoteOn);

        // Separate attack: sustained silence, then new onset, then pitch
        CompleteSamePitchRetrigger(session);
        AssertAccepted(session, freq, expectedIndex: 1);

        CompleteSamePitchRetrigger(session);
        AssertAccepted(session, freq, expectedIndex: 2);

        Assert.Equal(3, session.CurrentNoteIndex);
        Assert.Equal(new HashSet<int> { 0, 1, 2 }, session.CorrectNoteIndices);
    }

    [Fact]
    public void SamePitch_BriefSilenceWithoutNewAttack_DoesNotMarkSecondNote()
    {
        var session = CreateSession(midiNotes: [60, 60]);
        double freq = Freq(60);

        AssertAccepted(session, freq, expectedIndex: 0);
        session.NotifySilenceFor(session.SamePitchSilenceMs);

        // Silence armed post-attack wait, but no NotifyNoteAttack yet
        Assert.True(session.IsAwaitingNoteOn);
        AssertRejected(session, freq);
        Assert.Equal(new HashSet<int> { 0 }, session.CorrectNoteIndices);

        session.NotifyNoteAttack();
        AssertAccepted(session, freq, expectedIndex: 1);
    }

    [Fact]
    public void SamePitch_PitchDropoutWithoutRmsDrop_UnlocksViaNotifyPitchStopped()
    {
        // Clarinet tonguing often zeros the pitch detector while RMS stays above threshold.
        // ObserveLoudness must not wipe the pitch-stop silence clock each audio block.
        var session = CreateSession(midiNotes: [64, 64]); // E4, E4
        double freq = Freq(64);
        float loud = Math.Max(session.RmsThreshold * 3f, 0.05f);

        AssertAccepted(session, freq, expectedIndex: 0);
        Assert.True(session.IsAwaitingSamePitchRetrigger);

        session.SamePitchSilenceMs = 30;
        var t0 = DateTime.UtcNow;
        while ((DateTime.UtcNow - t0).TotalMilliseconds < session.SamePitchSilenceMs + 20)
        {
            session.ObserveLoudness(loud); // stays loud — must not cancel pitch-stop silence
            session.NotifyPitchStopped();
            Thread.Sleep(5);
        }

        Assert.True(session.IsAwaitingNoteOn); // armed for post-silence attack
        session.NotifyPitchResumed(); // pitch returns without RMS dip
        Assert.False(session.IsAwaitingNoteOn);
        AssertAccepted(session, freq, expectedIndex: 1);
        Assert.Equal(new HashSet<int> { 0, 1 }, session.CorrectNoteIndices);
    }

    [Fact]
    public void SamePitch_AmplitudeDipRise_DoesNotUnlockRepeatedNote()
    {
        var session = CreateSession(midiNotes: [62, 62]);
        double freq = Freq(62);

        AssertAccepted(session, freq, expectedIndex: 0);
        Assert.True(session.IsAwaitingNoteOn);

        // Amplitude wobble that used to false-trigger same-pitch advances
        session.ObserveLoudness(0.06f);
        session.ObserveLoudness(0.02f);
        session.ObserveLoudness(0.055f);
        Assert.True(session.IsAwaitingNoteOn);
        AssertRejected(session, freq);
        Assert.Equal(new HashSet<int> { 0 }, session.CorrectNoteIndices);
    }

    [Fact]
    public void DifferentPitch_NoNoteOnWait_PitchLockBlocksResidual()
    {
        var session = CreateSession(midiNotes: [60, 62]); // C then D
        AssertAccepted(session, Freq(60), expectedIndex: 0);
        Assert.False(session.IsAwaitingNoteOn);

        // Residual C is ignored via pitch lock (not scored wrong, not advanced)
        AssertRejected(session, Freq(60));
        Assert.Equal(1, session.CurrentNoteIndex);

        AssertAccepted(session, Freq(62), expectedIndex: 1);
    }

    [Fact]
    public void SingleCallback_CannotAdvanceMultipleNotes()
    {
        var session = CreateSession(midiNotes: [60, 62, 64]);
        double cFreq = Freq(60);

        var result = session.Evaluate(cFreq);
        Assert.True(result.correct);
        Assert.True(session.UpdateFeedbackForCurrent(cFreq, result));

        // Same detection again must not advance further (pitch lock on residual C)
        Assert.False(session.UpdateFeedbackForCurrent(cFreq, result));
        Assert.Equal(1, session.CurrentNoteIndex);
        Assert.Equal(new HashSet<int> { 0 }, session.CorrectNoteIndices);
    }

    [Fact]
    public void AfterSilence_ResidualPreviousPitch_IsNotCountedWrong()
    {
        var session = CreateSession(midiNotes: [60, 62]); // C then D
        double cFreq = Freq(60);

        AssertAccepted(session, cFreq, expectedIndex: 0);
        session.NotifySilence();
        Assert.False(session.IsAwaitingNoteOn);

        // Residual C after unlock must be ignored — not WrongPitch on D
        AssertRejected(session, cFreq);
        AssertRejected(session, cFreq);
        Assert.Equal(1, session.CurrentNoteIndex);
        Assert.False(session.NoteFeedbacks.ContainsKey(1));
        Assert.Equal(0, session.FeedbackViewModels[1].WrongAttempts);
    }

    [Fact]
    public void AfterSilence_NewDifferentPitch_CanStillBeMarkedWrong()
    {
        var session = CreateSession(midiNotes: [60, 62]); // C then D
        AssertAccepted(session, Freq(60), expectedIndex: 0);
        session.NotifySilence();

        // E is neither residual C nor expected D — should count as wrong
        var wrongFreq = Freq(64);
        var result = session.Evaluate(wrongFreq);
        Assert.False(result.correct);
        Assert.True(session.UpdateFeedbackForCurrent(wrongFreq, result));
        Assert.Equal(1, session.CurrentNoteIndex);
        Assert.DoesNotContain(1, session.CorrectNoteIndices);
        Assert.True(session.FeedbackViewModels[1].WrongAttempts >= 1);
    }

    private static void CompleteSamePitchRetrigger(NoteSessionService session)
    {
        session.NotifySilenceFor(session.SamePitchSilenceMs);
        Assert.True(session.IsAwaitingNoteOn); // waiting for onset
        session.NotifyNoteAttack();
        Assert.False(session.IsAwaitingNoteOn);
    }

    private static NoteSessionService CreateSession(int[] midiNotes)
    {
        var session = new NoteSessionService
        {
            Instrument = "concert-pitch",
            Tune = "Random",
            CooldownMs = 0,
        };
        session.Reset();
        session.Tune = "Random";
        session.Instrument = "concert-pitch";
        session.CooldownMs = 0;
        session.SamePitchSilenceMs = NoteSessionService.DefaultSamePitchSilenceMs;

        for (int i = 0; i < midiNotes.Length; i++)
        {
            int midi = midiNotes[i];
            session.NotesToDraw.Add(new NoteInfo
            {
                Midi = midi,
                Name = NoteSessionService.MidiToNoteName(midi, flats: false),
                TargetFreq = NoteSessionService.MidiToFreqPublic(midi),
                DurationBeats = 1,
                GateBeatsAfterPrevious = i == 0 ? 0 : 1,
            });
            session.FeedbackViewModels.Add(new FeedbackItem(i, 0, 0, false));
        }

        session.ConfigureRhythmStartGates();
        return session;
    }

    private static double Freq(int midi) => NoteSessionService.MidiToFreqPublic(midi);

    private static void AssertAccepted(NoteSessionService session, double freq, int expectedIndex)
    {
        var result = session.Evaluate(freq);
        Assert.True(result.correct, $"Expected pitch match at index {expectedIndex}");
        Assert.True(
            session.UpdateFeedbackForCurrent(freq, result),
            $"Expected note {expectedIndex} to be accepted");
        Assert.Contains(expectedIndex, session.CorrectNoteIndices);
        Assert.Equal(expectedIndex + 1, session.CurrentNoteIndex);
    }

    private static void AssertRejected(NoteSessionService session, double freq)
    {
        int indexBefore = session.CurrentNoteIndex;
        var correctBefore = session.CorrectNoteIndices.ToHashSet();
        int wrongBefore = session.FeedbackViewModels[indexBefore].WrongAttempts;
        var result = session.Evaluate(freq);
        Assert.False(session.UpdateFeedbackForCurrent(freq, result));
        Assert.Equal(indexBefore, session.CurrentNoteIndex);
        Assert.Equal(correctBefore, session.CorrectNoteIndices);
        Assert.Equal(wrongBefore, session.FeedbackViewModels[indexBefore].WrongAttempts);
    }
}
