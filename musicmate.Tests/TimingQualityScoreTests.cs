using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Tmg is how close each attack was to its expected onset.
/// T+ / T− only count whether it fell inside the accept window.
/// </summary>
public class TimingQualityScoreTests
{
    [Theory]
    [InlineData(0, 500, 100)]
    [InlineData(-150, 300, 75)]       // halfway to the early edge
    [InlineData(250, 500, 75)]        // halfway to the late edge
    [InlineData(-300, 300, 50)]       // early accept edge
    [InlineData(500, 500, 50)]        // late accept edge
    [InlineData(-450, 300, 25)]       // 1.5× the early window
    [InlineData(750, 500, 25)]        // 1.5× the late window
    [InlineData(1000, 500, 0)]        // twice the window
    [InlineData(4000, 500, 0)]        // far outside
    public void Score_FollowsWindowRelativeScale(double errorMs, double toleranceMs, double expected)
    {
        Assert.Equal(expected, TimingQualityScore.FromErrorAndTolerance(errorMs, toleranceMs), precision: 6);
    }

    [Fact]
    public void AttacksInsideTheWindow_ScoreAtLeast50()
    {
        double early = ConductorOnsetTiming.EarlyToleranceMs(60);
        double late = ConductorOnsetTiming.LateToleranceMs(60);

        Assert.True(TimingQualityScore.FromErrorAndTolerance(-early, early) >= 50);
        Assert.True(TimingQualityScore.FromErrorAndTolerance(late, late) >= 50);
        Assert.True(TimingQualityScore.FromErrorAndTolerance(-early * 0.25, early) >= 85);
        Assert.True(TimingQualityScore.FromErrorAndTolerance(late * 0.25, late) >= 85);
    }

    [Fact]
    public void WorseErrors_DecreaseTheScoreUntilTheFloor()
    {
        double tolerance = 400;
        double previous = double.PositiveInfinity;
        foreach (double fraction in new[] { 0, 0.25, 0.5, 0.75, 1.0, 1.5, 2.0 })
        {
            double score = TimingQualityScore.FromErrorAndTolerance(fraction * tolerance, tolerance);
            Assert.True(score < previous);
            previous = score;
        }

        Assert.Equal(0, TimingQualityScore.FromErrorAndTolerance(3 * tolerance, tolerance), precision: 6);
        Assert.Equal(0, previous, precision: 6);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(40)]
    [InlineData(90)]
    [InlineData(120)]
    [InlineData(160)]
    public void SameFractionOfTheWindow_ScoresTheSameAtSlowAndFastTempos(int bpm)
    {
        double early = ConductorOnsetTiming.EarlyToleranceMs(bpm);
        double late = ConductorOnsetTiming.LateToleranceMs(bpm);
        double earlyAt60 = ConductorOnsetTiming.EarlyToleranceMs(60);

        Assert.NotEqual(earlyAt60, early);
        Assert.Equal(90, TimingQualityScore.FromErrorAndTolerance(-0.2 * early, early), precision: 6);
        Assert.Equal(90, TimingQualityScore.FromErrorAndTolerance(0.2 * late, late), precision: 6);
    }

    [Fact]
    public void EasyWindow_DoesNotTurnAnAcceptedAttackIntoANearZeroScore()
    {
        var easy = MakeItEasyMode.TimingProfile;
        double late = ConductorOnsetTiming.LateToleranceMs(60, easy);
        Assert.Equal(2000, late, precision: 6);

        // 400 ms late is inside Easy's 2000 ms window (and inside the normal 500 ms window).
        // The old sixteenth-note scale treated anything past 250 ms at 60 BPM as 0.
        double score = TimingQualityScore.FromErrorAndTolerance(400, late);
        Assert.Equal(90, score, precision: 6);
        Assert.InRange(OldSixteenthScore(errorMs: 400, bpm: 60), 0, 0.01);
    }

    [Fact]
    public void OldSixteenthScale_ScoresNearZeroWhileStillInsideTheAcceptWindow()
    {
        // Fitted 60 BPM line: a sixteenth is 250 ms, and "good" was a quarter of that (62.5 ms).
        // 240 ms is inside the normal late window (500 ms) but almost off the old scale.
        double oldScore = OldSixteenthScore(errorMs: 240, bpm: 60);
        Assert.InRange(oldScore, 1, 8);

        double late = ConductorOnsetTiming.LateToleranceMs(60);
        double score = TimingQualityScore.FromErrorAndTolerance(240, late);
        Assert.InRange(score, 70, 80);
    }

    [Fact]
    public void TimingQuality_IsNotThePassRate()
    {
        // Three attacks halfway to the edge (still T+) and one at twice the window (T−).
        // Pass rate is 75. Quality is (75 + 75 + 75 + 0) / 4 = 56.25.
        double? quality = TimingQualityScore.Average(
        [
            (250, 500),
            (250, 500),
            (250, 500),
            (1000, 500),
        ]);

        Assert.Equal(56.25, quality!.Value, precision: 6);
        Assert.NotEqual(75, quality.Value);
    }

    [Fact]
    public void NoTimedNotes_LeavesQualityNull()
    {
        Assert.Null(TimingQualityScore.Average([]));
    }

    /// <summary>
    /// Previous Tmg scale at a steady tempo: 100 at or inside 1/16 of a sixteenth,
    /// 0 at or beyond one sixteenth, linear between.
    /// </summary>
    private static double OldSixteenthScore(double errorMs, int bpm)
    {
        double sixteenthMs = ConductorOnsetTiming.MsPerBeat(bpm) * 0.25;
        double good = sixteenthMs * 0.25;
        double bad = sixteenthMs;
        if (errorMs <= good)
            return 100;
        if (errorMs >= bad)
            return 0;
        return 100.0 * (1.0 - (errorMs - good) / (bad - good));
    }
}

[Collection("SessionPreferences")]
public class TimingQualitySessionTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public TimingQualitySessionTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Fact]
    public void ExactOnsets_Score100_AndCountAsTimingCorrect()
    {
        var session = SessionWith(Repeat(0, 500, timingCorrect: true, count: 15));

        Assert.Equal(100, session.GetTimingAccuracyPercent()!.Value, precision: 6);
        var counts = session.GetSessionSummaryCounts();
        Assert.Equal(15, counts.TimingRight);
        Assert.Equal(0, counts.TimingWrong);
    }

    [Fact]
    public void SlightlyEarlyInsideTheWindow_StaysTimingCorrect_WithAHighScore()
    {
        double early = ConductorOnsetTiming.EarlyToleranceMs(60);
        double error = -0.25 * early;
        var session = SessionWith(Repeat(error, early, timingCorrect: true, count: 8));

        Assert.Equal(0, session.GetSessionSummaryCounts().TimingWrong);
        Assert.Equal(8, session.GetSessionSummaryCounts().TimingRight);
        Assert.Equal(87.5, session.GetTimingAccuracyPercent()!.Value, precision: 6);
    }

    [Fact]
    public void SlightlyLateInsideTheWindow_StaysTimingCorrect_WithAHighScore()
    {
        double late = ConductorOnsetTiming.LateToleranceMs(72);
        double error = 0.25 * late;
        var session = SessionWith(Repeat(error, late, timingCorrect: true, count: 8));

        Assert.Equal(0, session.GetSessionSummaryCounts().TimingWrong);
        Assert.Equal(87.5, session.GetTimingAccuracyPercent()!.Value, precision: 6);
    }

    [Fact]
    public void OutsideTheWindow_CountsTimingWrong_AndLowersTheScore()
    {
        double late = ConductorOnsetTiming.LateToleranceMs(60);
        var session = SessionWith(
        [
            Timed(0, 0, late, timingCorrect: true),
            Timed(1, late * 1.5, late, timingCorrect: false),
            Timed(2, late * 2.0, late, timingCorrect: false),
        ]);

        var counts = session.GetSessionSummaryCounts();
        Assert.Equal(1, counts.TimingRight);
        Assert.Equal(2, counts.TimingWrong);
        // (100 + 25 + 0) / 3
        Assert.Equal(41.666666, session.GetTimingAccuracyPercent()!.Value, precision: 4);
    }

    [Fact]
    public void CountInWallClock_DoesNotChangeTimingQuality()
    {
        var notes = Repeat(0, 500, timingCorrect: true, count: 4);
        var without = SessionWith(notes);
        var withCountIn = new NoteSessionService();
        foreach (var note in notes)
            withCountIn.RecordAttemptOutcome(note);
        // Four beats of count-in at 60 BPM, then even attacks. Stored errors are already
        // measured from the conductor onset, so the lead-in is not a timing penalty.
        for (int i = 0; i < 4; i++)
            withCountIn.AddOnsetSampleForTests(4000 + i * 1000, i);
        withCountIn.FinalizeSessionStats();

        Assert.Equal(100, without.GetTimingAccuracyPercent()!.Value, precision: 6);
        Assert.Equal(100, withCountIn.GetTimingAccuracyPercent()!.Value, precision: 6);
    }

    [Fact]
    public void PauseGapInOnsetSamples_DoesNotLowerTimingQuality()
    {
        var session = new NoteSessionService();
        foreach (var note in Repeat(-40, 300, timingCorrect: true, count: 4))
            session.RecordAttemptOutcome(note);
        session.AddOnsetSampleForTests(0, 0);
        session.AddOnsetSampleForTests(1000, 1);
        session.AddOnsetSampleForTests(9000, 2); // multi-beat pause the old fit could not absorb
        session.AddOnsetSampleForTests(10000, 3);
        session.FinalizeSessionStats();

        Assert.Equal(0, session.GetSessionSummaryCounts().TimingWrong);
        // r = 40/300; score = 100 * (1 - 0.5 * 40/300)
        Assert.Equal(100.0 * (1.0 - 0.5 * 40.0 / 300.0), session.GetTimingAccuracyPercent()!.Value, precision: 6);
    }

    [Fact]
    public void RestMistakes_StayOutOfTimingPitchAndOverallCounts()
    {
        var session = new NoteSessionService();
        foreach (var note in Repeat(0, 500, timingCorrect: true, count: 4))
            session.RecordAttemptOutcome(note);
        session.RecordAttemptOutcome(new NoteAttemptOutcome
        {
            NoteIndex = -1,
            IsRest = true,
            ExpectedWrittenNoteName = "",
            OverallCorrect = false,
            TimingCorrect = false,
            TimingErrorMs = 5000,
            TimingToleranceMs = 200,
            WrongReason = "SoundDuringRest",
        });
        session.FinalizeSessionStats();

        var counts = session.GetSessionSummaryCounts();
        Assert.Equal(4, counts.TimingRight);
        Assert.Equal(0, counts.TimingWrong);
        Assert.Equal(4, counts.OverallRight);
        Assert.Equal(0, counts.OverallWrong);
        Assert.Equal(0, counts.PitchWrong);
        Assert.Equal(1, counts.RestWrong);
        Assert.Equal(100, session.GetTimingAccuracyPercent()!.Value, precision: 6);

        var stat = PracticeSessionPersistence.BuildSessionStat(session);
        Assert.Equal(0, stat.OverallWrongCount);
        Assert.Equal(1, stat.RestWrongCount);
        Assert.Equal(100, stat.Tmg, precision: 6);
    }

    [Fact]
    public void Overall_IsTheAverageOfPitchCompletionAndTimingQuality()
    {
        var session = SessionWith(Repeat(0, 500, timingCorrect: true, count: 4));
        var stat = PracticeSessionPersistence.BuildSessionStat(session);

        Assert.Equal(100, stat.Tmg, precision: 6);
        Assert.Equal((stat.Pc + stat.Tmg) / 2.0, stat.Ovrl, precision: 6);
    }

    [Fact]
    public void NoTimingComparison_StoresZeroTimingAndLeavesOverallAsPitchCompletion()
    {
        var session = new NoteSessionService();
        session.RecordAttemptOutcome(new NoteAttemptOutcome
        {
            NoteIndex = 0,
            ExpectedWrittenNoteName = "C4",
            PitchCorrect = false,
            TimingCorrect = true,
            OverallCorrect = false,
            WrongReason = "WrongPitch",
        });
        session.FinalizeSessionStats();

        Assert.Null(session.GetTimingAccuracyPercent());
        var stat = PracticeSessionPersistence.BuildSessionStat(session);
        Assert.Equal(0, stat.Tmg, precision: 6);
        Assert.Equal(stat.Pc, stat.Ovrl, precision: 6);
    }

    private static NoteSessionService SessionWith(IReadOnlyList<NoteAttemptOutcome> outcomes)
    {
        var session = new NoteSessionService();
        foreach (var outcome in outcomes)
            session.RecordAttemptOutcome(outcome);
        session.FinalizeSessionStats();
        return session;
    }

    private static NoteAttemptOutcome[] Repeat(double errorMs, double toleranceMs, bool timingCorrect, int count)
    {
        var notes = new NoteAttemptOutcome[count];
        for (int i = 0; i < count; i++)
            notes[i] = Timed(i, errorMs, toleranceMs, timingCorrect);
        return notes;
    }

    private static NoteAttemptOutcome Timed(int index, double errorMs, double toleranceMs, bool timingCorrect)
        => new()
        {
            NoteIndex = index,
            ExpectedWrittenNoteName = "C4",
            PitchCorrect = true,
            TimingCorrect = timingCorrect,
            OverallCorrect = timingCorrect,
            TimingErrorMs = errorMs,
            TimingToleranceMs = toleranceMs,
            WrongReason = timingCorrect ? "" : "Late",
        };
}
