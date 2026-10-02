using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Regression coverage for the note-grade pipeline: early/transient frames must not
/// leave a correct on-time accept marked wrong-frequency, and hadEarlyCandidate must
/// not leak across notes or be persisted as a failure label on a successful accept.
/// </summary>
public class NoteGradePipelineTests
{
    [Fact]
    public void CleanOnTimeCorrectPitch_FinalOnTimeNoPenalty()
    {
        double elapsed = 0;
        var session = CreateTwoNoteSession(60, 62, bpm: 60, () => elapsed);

        elapsed = 0;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), session.Evaluate(Freq(60))));

        var o0 = Assert.Single(session.GetSessionAttemptOutcomes(), o => o.NoteIndex == 0);
        Assert.True(o0.PitchCorrect);
        Assert.True(o0.OverallCorrect);
        Assert.False(o0.HadEarlyCandidate);
        Assert.True(string.IsNullOrEmpty(o0.WrongReason));
        Assert.Equal(0, o0.PitchErrorCents);
        Assert.Equal(
            "onTime",
            NoteAttemptTimingDiagnostics.ClassifyAcceptedOnset(o0.TimingErrorMs ?? 0));
    }

    [Fact]
    public void EarlyNoisyThenCorrectOnTime_FinalStillCorrectOnTime()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateTwoNoteSession(60, 62, bpm, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);
        double earlyTol = ConductorOnsetTiming.EarlyToleranceMs(bpm);

        elapsed = 0;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), session.Evaluate(Freq(60))));

        // Meaningful early candidate (correct pitch, before window).
        elapsed = msPerBeat - earlyTol - 40;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(62), session.Evaluate(Freq(62))));
        Assert.Contains(
            session.GetSessionAttemptOutcomes(),
            o => o.NoteIndex == 1 && o.WrongReason == "Early");

        // Valid on-time accept.
        elapsed = msPerBeat;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(62), session.Evaluate(Freq(62))));

        var accepted = Assert.Single(session.GetSessionAttemptOutcomes(), o => o.NoteIndex == 1);
        Assert.True(accepted.PitchCorrect);
        Assert.True(accepted.OverallCorrect);
        Assert.True(accepted.HadEarlyCandidate);
        Assert.True(string.IsNullOrEmpty(accepted.WrongReason),
            "Successful accept must not persist hadEarlyCandidate as WrongReason");
        Assert.Equal(
            "onTime",
            NoteAttemptTimingDiagnostics.ClassifyAcceptedOnset(accepted.TimingErrorMs ?? 0));
        Assert.DoesNotContain(
            session.GetSessionAttemptOutcomes(),
            o => o.NoteIndex == 1 && o.WrongReason is "Early" or "WrongPitch");
    }

    [Fact]
    public void SeveralRawFramesFromOneSustain_TreatedAsOneMusicalAttempt()
    {
        double elapsed = 0;
        var session = CreateTwoNoteSession(60, 62, bpm: 60, () => elapsed);
        session.CooldownMs = 0;
        session.WrongDebounceMs = 300;

        elapsed = 0;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), session.Evaluate(Freq(60))));

        // Sustained tone: many frames while cooldown / after accept should not add more note-0 rows.
        for (int i = 0; i < 8; i++)
        {
            elapsed = 5 * i;
            session.UpdateFeedbackForCurrent(Freq(60), session.Evaluate(Freq(60)));
        }

        Assert.Single(session.GetSessionAttemptOutcomes(), o => o.NoteIndex == 0);
        Assert.Equal(1, session.CurrentNoteIndex);
    }

    [Fact]
    public void PreviousNoteHadEarly_NextCleanOnTime_DoesNotInheritHadEarly()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateThreeNoteSession(60, 62, 64, bpm, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);
        double earlyTol = ConductorOnsetTiming.EarlyToleranceMs(bpm);

        elapsed = 0;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), session.Evaluate(Freq(60))));

        // Note 1: early then accept (hadEarlyCandidate on note 1 only).
        elapsed = msPerBeat - earlyTol - 40;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(62), session.Evaluate(Freq(62))));
        elapsed = msPerBeat;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(62), session.Evaluate(Freq(62))));
        var note1 = Assert.Single(session.GetSessionAttemptOutcomes(), o => o.NoteIndex == 1);
        Assert.True(note1.HadEarlyCandidate);

        // Note 2: clean on-time — must not inherit hadEarlyCandidate.
        elapsed = 2 * msPerBeat;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(64), session.Evaluate(Freq(64))));
        var note2 = Assert.Single(session.GetSessionAttemptOutcomes(), o => o.NoteIndex == 2);
        Assert.False(note2.HadEarlyCandidate);
        Assert.True(note2.OverallCorrect);
        Assert.True(string.IsNullOrEmpty(note2.WrongReason));
    }

    [Fact]
    public void CorrectTimingWrongPitch_WrongFrequencyOnlyForCurrentNote()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateTwoNoteSession(60, 62, bpm, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), session.Evaluate(Freq(60))));

        elapsed = msPerBeat;
        var wrong = session.Evaluate(Freq(64)); // E vs expected D
        Assert.False(wrong.correct);
        Assert.True(session.UpdateFeedbackForCurrent(Freq(64), wrong));

        var wrongRow = Assert.Single(
            session.GetSessionAttemptOutcomes(),
            o => o.NoteIndex == 1 && o.WrongReason == "WrongPitch");
        Assert.False(wrongRow.PitchCorrect);
        Assert.False(wrongRow.OverallCorrect);
        Assert.True(Math.Abs(wrongRow.PitchErrorCents) > session.Tolerance);

        // Later correct accept supersedes WrongPitch for this note.
        Assert.True(session.UpdateFeedbackForCurrent(Freq(62), session.Evaluate(Freq(62))));
        var accepted = Assert.Single(session.GetSessionAttemptOutcomes(), o => o.NoteIndex == 1);
        Assert.True(accepted.PitchCorrect);
        Assert.True(accepted.OverallCorrect);
        Assert.True(string.IsNullOrEmpty(accepted.WrongReason));
        Assert.DoesNotContain(
            session.GetSessionAttemptOutcomes(),
            o => o.NoteIndex == 1 && o.WrongReason == "WrongPitch");
    }

    [Fact]
    public void CorrectPitchGenuinelyEarly_EarlyClassification()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateTwoNoteSession(60, 62, bpm, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);
        double earlyTol = ConductorOnsetTiming.EarlyToleranceMs(bpm);

        elapsed = 0;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), session.Evaluate(Freq(60))));

        elapsed = msPerBeat - earlyTol - 50;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(62), session.Evaluate(Freq(62))));

        var early = Assert.Single(
            session.GetSessionAttemptOutcomes(),
            o => o.NoteIndex == 1 && o.WrongReason == "Early");
        Assert.True(early.PitchCorrect);
        Assert.False(early.TimingCorrect == true);
        Assert.True(early.TimingErrorMs < 0);
        Assert.Equal(1, session.CurrentNoteIndex);
    }

    [Fact]
    public void UnstableAttackThenStableOnTime_BestValidCandidateWins()
    {
        const int bpm = 60;
        double elapsed = 0;
        var session = CreateTwoNoteSession(60, 62, bpm, () => elapsed);
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);

        elapsed = 0;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), session.Evaluate(Freq(60))));

        // Transient wrong pitch at onset window, then stable correct.
        elapsed = msPerBeat;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(64), session.Evaluate(Freq(64))));
        Assert.Contains(
            session.GetSessionAttemptOutcomes(),
            o => o.NoteIndex == 1 && o.WrongReason == "WrongPitch");

        Assert.True(session.UpdateFeedbackForCurrent(Freq(62), session.Evaluate(Freq(62))));
        var final = Assert.Single(session.GetSessionAttemptOutcomes(), o => o.NoteIndex == 1);
        Assert.True(final.PitchCorrect);
        Assert.True(final.OverallCorrect);
        Assert.True(string.IsNullOrEmpty(final.WrongReason));
        Assert.Equal(
            "onTime",
            NoteAttemptTimingDiagnostics.ClassifyAcceptedOnset(final.TimingErrorMs ?? 0));
    }

    [Fact]
    public void NoMeaningfulCandidate_DoesNotFalselyMarkWrongFrequency()
    {
        double elapsed = 0;
        var session = CreateTwoNoteSession(60, 62, bpm: 60, () => elapsed);

        elapsed = 0;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), session.Evaluate(Freq(60))));

        // Simulate quiet / meaningless frame telemetry.
        session.SetLastDetectionTelemetry(session.RmsThreshold * 0.25f);
        elapsed = ConductorOnsetTiming.MsPerBeat(60);
        Assert.False(session.UpdateFeedbackForCurrent(Freq(64), session.Evaluate(Freq(64))));

        Assert.DoesNotContain(
            session.GetSessionAttemptOutcomes(),
            o => o.NoteIndex == 1 && o.WrongReason == "WrongPitch");
    }

    private static NoteSessionService CreateTwoNoteSession(
        int midi0, int midi1, int bpm, Func<double> elapsed)
        => CreateSession([midi0, midi1], bpm, elapsed);

    private static NoteSessionService CreateThreeNoteSession(
        int midi0, int midi1, int midi2, int bpm, Func<double> elapsed)
        => CreateSession([midi0, midi1, midi2], bpm, elapsed);

    private static NoteSessionService CreateSession(int[] midis, int bpm, Func<double> elapsed)
    {
        var session = new NoteSessionService
        {
            Instrument = "concert-pitch",
            Tune = "Selected Scale",
            CooldownMs = 0,
            Tempo = bpm,
            ShowConductorCues = false,
            ChildLevel = 1,
        };
        session.Reset();
        session.Tune = "Selected Scale";
        session.Instrument = "concert-pitch";
        session.Tempo = bpm;
        session.CooldownMs = 0;
        session.WrongDebounceMs = 0;
        session.SessionElapsedMsOverride = elapsed;

        for (int i = 0; i < midis.Length; i++)
        {
            int midi = midis[i];
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
        return session;
    }

    private static double Freq(int midi) => NoteSessionService.MidiToFreqPublic(midi);
}
