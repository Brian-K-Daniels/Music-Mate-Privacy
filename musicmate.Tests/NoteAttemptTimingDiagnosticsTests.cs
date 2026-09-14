using musicmate.Models;
using musicmate.Services;
using musicmate.ViewModels;

namespace musicmate.Tests;

public class NoteAttemptTimingDiagnosticsTests
{
    [Theory]
    [InlineData(-227.0, "early")]
    [InlineData(-0.001, "early")]
    [InlineData(0.0, "onTime")]
    [InlineData(3.0, "late")]
    [InlineData(313.0, "late")]
    public void ClassifyAcceptedOnset_UsesSignedDeltaMs(double deltaMs, string expected)
        => Assert.Equal(expected, NoteAttemptTimingDiagnostics.ClassifyAcceptedOnset(deltaMs));

    [Fact]
    public void ClassifyAcceptedOnset_Null_ReturnsNull()
        => Assert.Null(NoteAttemptTimingDiagnostics.ClassifyAcceptedOnset((double?)null));

    [Theory]
    [InlineData(NoteAttemptTimingDiagnostics.HadEarlyCandidateReason, true)]
    [InlineData(NoteAttemptTimingDiagnostics.LegacyWasEarlyReason, true)]
    [InlineData("Early", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsHadEarlyCandidateReason_RecognizesCurrentAndLegacy(string? reason, bool expected)
        => Assert.Equal(expected, NoteAttemptTimingDiagnostics.IsHadEarlyCandidateReason(reason));

    [Fact]
    public void DisplayDetail_Plus3MsWithHadEarlyCandidate_ShowsOnTimeWhenTimingOk()
    {
        var attempt = new NoteAttempt
        {
            AttemptId = 1,
            SessionId = "test",
            ExpectedWrittenNoteName = "C4",
            WrittenNoteName = "C4",
            ActualDetectedNoteName = "C4",
            ExpectedDuration = "Quarter",
            PitchCorrect = true,
            TimingCorrect = true,
            OverallCorrect = true,
            WrongReason = NoteAttemptTimingDiagnostics.HadEarlyCandidateReason,
            PitchErrorCents = 26,
            TimingErrorMs = 3,
            ExpectedStartMs = 11706,
            ActualDetectedMs = 11709,
        };

        var detail = new NoteAttemptRowViewModel(attempt).Detail;

        Assert.Contains("hadEarlyCandidate", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("wasEarly", detail, StringComparison.Ordinal);
        Assert.Contains("onset=onTime", detail, StringComparison.Ordinal);
        Assert.Contains("Δ3ms", detail, StringComparison.Ordinal);
        Assert.Contains("t=11706/11709", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("onset=late", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("onset=early", detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-137.0, true, "onset=onTime", "Δ-137ms")]
    [InlineData(0.0, true, "onset=onTime", "Δ0ms")]
    [InlineData(3.0, true, "onset=onTime", "Δ3ms")]
    [InlineData(-137.0, false, "onset=early", "Δ-137ms")]
    [InlineData(3.0, false, "onset=late", "Δ3ms")]
    public void DisplayDetail_OnsetUsesTimingOkForOnTime(
        double deltaMs, bool timingCorrect, string onsetToken, string deltaToken)
    {
        var attempt = new NoteAttempt
        {
            AttemptId = 2,
            ExpectedWrittenNoteName = "D4",
            WrittenNoteName = "D4",
            ActualDetectedNoteName = "D4",
            PitchCorrect = true,
            TimingCorrect = timingCorrect,
            OverallCorrect = timingCorrect,
            WrongReason = timingCorrect ? string.Empty : (deltaMs < 0 ? "Early" : "Late"),
            TimingErrorMs = deltaMs,
            ExpectedStartMs = 600,
            ActualDetectedMs = 600 + deltaMs,
        };

        var detail = new NoteAttemptRowViewModel(attempt).Detail;
        Assert.Contains(onsetToken, detail, StringComparison.Ordinal);
        Assert.Contains(deltaToken, detail, StringComparison.Ordinal);
    }

    [Fact]
    public void EarlyThenAccept_WithPositiveAcceptedDelta_HadEarlyCandidate_OnsetLate()
    {
        const int bpm = 60;
        double elapsed = 0;
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
        session.SessionElapsedMsOverride = () => elapsed;

        session.NotesToDraw.Add(new NoteInfo
        {
            Midi = 60,
            Name = NoteSessionService.MidiToNoteName(60, flats: false),
            TargetFreq = NoteSessionService.MidiToFreqPublic(60),
            Duration = NoteDuration.Quarter,
            DurationBeats = 1,
            StartBeat = 0,
            GateBeatsAfterPrevious = 0,
        });
        session.FeedbackViewModels.Add(new FeedbackItem(0, 0, 0, false));
        session.NotesToDraw.Add(new NoteInfo
        {
            Midi = 62,
            Name = NoteSessionService.MidiToNoteName(62, flats: false),
            TargetFreq = NoteSessionService.MidiToFreqPublic(62),
            Duration = NoteDuration.Quarter,
            DurationBeats = 1,
            StartBeat = 1,
            GateBeatsAfterPrevious = 1,
        });
        session.FeedbackViewModels.Add(new FeedbackItem(1, 0, 0, false));
        session.ConfigureRhythmStartGates();
        session.StartListeningClock();

        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);
        double earlyTol = ConductorOnsetTiming.EarlyToleranceMs(bpm);

        elapsed = 0;
        var first = session.Evaluate(Freq(60));
        Assert.True(session.UpdateFeedbackForCurrent(Freq(60), first));

        // Too early for note 1.
        elapsed = msPerBeat - earlyTol - 50;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(62), session.Evaluate(Freq(62))));
        Assert.Equal(1, session.CurrentNoteIndex);
        Assert.Contains(
            session.GetSessionAttemptOutcomes(),
            o => o.NoteIndex == 1 && o.WrongReason == "Early");

        // Accept slightly late (+3 ms) inside the window — mirrors the reported diagnostic.
        elapsed = msPerBeat + 3;
        Assert.True(session.UpdateFeedbackForCurrent(Freq(62), session.Evaluate(Freq(62))));
        Assert.Contains(1, session.CorrectNoteIndices);

        var accepted = Assert.Single(session.GetSessionAttemptOutcomes(), o => o.NoteIndex == 1);
        Assert.True(accepted.OverallCorrect);
        Assert.True(accepted.HadEarlyCandidate);
        Assert.Equal(NoteAttemptTimingDiagnostics.HadEarlyCandidateReason, accepted.WrongReason);
        Assert.True(accepted.TimingErrorMs is > 0);
        Assert.Equal("late", NoteAttemptTimingDiagnostics.ClassifyAcceptedOnset(accepted.TimingErrorMs));
        Assert.InRange(accepted.TimingErrorMs!.Value, 2.5, 3.5);
    }

    private static double Freq(int midi) => NoteSessionService.MidiToFreqPublic(midi);
}
