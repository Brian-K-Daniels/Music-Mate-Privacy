using System.Diagnostics;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Tests;

[Collection("SessionPreferences")]
public class WaitingCountInLogicTests
{
    [Theory]
    [InlineData("2/4", 2)]
    [InlineData("3/4", 3)]
    [InlineData("4/4", 4)]
    [InlineData("5/4", 5)]
    [InlineData("2/2", 2)]
    [InlineData("3/8", 3)]
    [InlineData("6/8", 2)]  // compound: dotted-quarter beats
    [InlineData("9/8", 3)]
    [InlineData("12/8", 4)]
    public void GetBeatsPerMeasure_MatchesConductedBeats(string display, int expected)
        => Assert.Equal(expected, WaitingCountInLogic.GetBeatsPerMeasure(display));

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public void IsAccentedBeat_OnlyFirstBeatOfMeasure(int beatInMeasure, bool accented)
        => Assert.Equal(accented, WaitingCountInLogic.IsAccentedBeat(beatInMeasure));

    [Fact]
    public void AbsoluteBeatIndex_AccentsEveryMeasureDownbeat()
    {
        const int beats = 4;
        for (long i = 0; i < 16; i++)
        {
            bool accent = WaitingCountInLogic.IsAccentedBeat(i, beats);
            Assert.Equal(i % beats == 0, accent);
        }
    }

    [Fact]
    public void BuildClick_UsesAccentedParamsOnDownbeat()
    {
        var click = WaitingCountInLogic.BuildClick(
            absoluteBeatIndex: 0,
            beatsPerMeasure: 4,
            accentedVolume: 0.5f,
            unaccentedVolume: 0.2f,
            accentedPitchHz: 1760,
            unaccentedPitchHz: 880,
            beatDurationPercent: 20,
            tempoBpm: 100);

        Assert.True(click.IsAccented);
        Assert.Equal(0.5f, click.Volume);
        Assert.Equal(1760, click.FrequencyHz);
        // 60_000/100 = 600 ms/beat × 20% = 120 ms
        Assert.Equal(0.120, click.DurationSeconds, 3);
    }

    [Fact]
    public void BuildClick_UsesUnaccentedParamsOffDownbeat()
    {
        var click = WaitingCountInLogic.BuildClick(
            absoluteBeatIndex: 1,
            beatsPerMeasure: 4,
            accentedVolume: 0.5f,
            unaccentedVolume: 0.2f,
            accentedPitchHz: 1760,
            unaccentedPitchHz: 880,
            beatDurationPercent: 20,
            tempoBpm: 100);

        Assert.False(click.IsAccented);
        Assert.Equal(0.2f, click.Volume);
        Assert.Equal(880, click.FrequencyHz);
    }

    [Theory]
    [InlineData(true, true, 0, true)]
    [InlineData(false, true, 0, false)]
    [InlineData(true, false, 0, false)]
    [InlineData(true, true, 1, false)]
    public void ShouldStopForFirstNote(
        bool evaluateCorrect, bool countInActive, int currentNoteIndex, bool expected)
        => Assert.Equal(
            expected,
            WaitingCountInLogic.ShouldStopForFirstNote(evaluateCorrect, countInActive, currentNoteIndex));

    [Fact]
    public void ResolveClickDuration_IsPercentOfBeat()
    {
        double msPerBeat = WaitingCountInLogic.MsPerBeat(120); // 500 ms
        double seconds = WaitingCountInLogic.ResolveClickDurationSeconds(20, msPerBeat);
        Assert.Equal(0.100, seconds, 3);
    }

    [Fact]
    public void ResolveClickDuration_ClampsPercentRange()
    {
        double msPerBeat = WaitingCountInLogic.MsPerBeat(60); // 1000 ms
        Assert.Equal(0.050, WaitingCountInLogic.ResolveClickDurationSeconds(1, msPerBeat), 3);
        Assert.Equal(0.500, WaitingCountInLogic.ResolveClickDurationSeconds(99, msPerBeat), 3);
    }
}

[Collection("SessionPreferences")]
public class WaitingCountInSettingsTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public WaitingCountInSettingsTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Fact]
    public void FactoryDefault_EnabledIsOn()
    {
        Assert.True(WaitingCountInSettings.DefaultEnabled);
        Assert.True(WaitingCountInSettings.Enabled);
    }

    [Fact]
    public void Defaults_AreRestoredAfterFactoryReset()
    {
        WaitingCountInSettings.Enabled = true;
        WaitingCountInSettings.AccentedVolume = 0.9f;
        WaitingCountInSettings.UnaccentedVolume = 0.1f;
        WaitingCountInSettings.AccentedPitchHz = 2000;
        WaitingCountInSettings.UnaccentedPitchHz = 1000;
        WaitingCountInSettings.BeatDurationPercent = 40;

        WaitingCountInSettings.ResetToFactoryDefaults();

        Assert.Equal(WaitingCountInSettings.DefaultEnabled, WaitingCountInSettings.Enabled);
        Assert.Equal(WaitingCountInSettings.DefaultAccentedVolume, WaitingCountInSettings.AccentedVolume);
        Assert.Equal(WaitingCountInSettings.DefaultUnaccentedVolume, WaitingCountInSettings.UnaccentedVolume);
        Assert.Equal(WaitingCountInSettings.DefaultAccentedPitchHz, WaitingCountInSettings.AccentedPitchHz);
        Assert.Equal(WaitingCountInSettings.DefaultUnaccentedPitchHz, WaitingCountInSettings.UnaccentedPitchHz);
        Assert.Equal(WaitingCountInSettings.DefaultBeatDurationPercent, WaitingCountInSettings.BeatDurationPercent);
    }

    [Fact]
    public void Values_PersistAndRestore()
    {
        WaitingCountInSettings.Enabled = true;
        WaitingCountInSettings.AccentedVolume = 0.33f;
        WaitingCountInSettings.UnaccentedVolume = 0.11f;
        WaitingCountInSettings.AccentedPitchHz = 1500;
        WaitingCountInSettings.UnaccentedPitchHz = 900;
        WaitingCountInSettings.BeatDurationPercent = 25;

        Assert.True(WaitingCountInSettings.Enabled);
        Assert.Equal(0.33f, WaitingCountInSettings.AccentedVolume, 3);
        Assert.Equal(0.11f, WaitingCountInSettings.UnaccentedVolume, 3);
        Assert.Equal(1500, WaitingCountInSettings.AccentedPitchHz, 0);
        Assert.Equal(900, WaitingCountInSettings.UnaccentedPitchHz, 0);
        Assert.Equal(25, WaitingCountInSettings.BeatDurationPercent);
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(2f, 1f)]
    [InlineData(0.5f, 0.5f)]
    public void ClampVolume(float input, float expected)
        => Assert.Equal(expected, WaitingCountInSettings.ClampVolume(input));

    [Theory]
    [InlineData(100, 440)]
    [InlineData(5000, 4000)]
    [InlineData(880, 880)]
    public void ClampPitchHz(double input, double expected)
        => Assert.Equal(expected, WaitingCountInSettings.ClampPitchHz(input));

    [Theory]
    [InlineData(1, 5)]
    [InlineData(200, 50)]
    [InlineData(20, 20)]
    public void ClampDurationPercent(int input, int expected)
        => Assert.Equal(expected, WaitingCountInSettings.ClampDurationPercent(input));

    [Fact]
    public void ResolveClickPlayback_IncludesReleaseTail()
    {
        double msPerBeat = WaitingCountInLogic.MsPerBeat(30);
        double playbackMs = WaitingCountInLogic.ResolveClickPlaybackMs(20, msPerBeat);
        Assert.Equal(480.0, playbackMs, 0);
    }

    [Theory]
    [InlineData(0, 2000, 2000, false)]
    [InlineData(1500, 2000, 2000, false)]
    [InlineData(2900, 2000, 2000, false)]
    [InlineData(3000, 2000, 2000, true)]
    [InlineData(3500, 2000, 2000, true)]
    public void IsTooLateToSound_DetectsMissedGridSlot(
        double nowMs,
        double intendedMs,
        double msPerBeat,
        bool expected)
        => Assert.Equal(
            expected,
            WaitingCountInLogic.IsTooLateToSound(nowMs, intendedMs, msPerBeat));

    [Theory]
    [InlineData(0, 1000, 0)]
    [InlineData(3, 1000, 3000)]
    [InlineData(5, 500, 2500)]
    public void GetAbsoluteBeatStartMs_UsesStableGrid(long beatIndex, double msPerBeat, double expected)
        => Assert.Equal(expected, WaitingCountInLogic.GetAbsoluteBeatStartMs(beatIndex, msPerBeat));

    [Theory]
    [InlineData(0, 4, 1, 1)]
    [InlineData(3, 4, 1, 4)]
    [InlineData(4, 4, 2, 1)]
    [InlineData(7, 4, 2, 4)]
    public void GetMeasureBeatNumbers_AreOneBased(
        long beatIndex,
        int beatsPerMeasure,
        int expectedMeasure,
        int expectedBeat)
    {
        var (measure, beat) = WaitingCountInLogic.GetMeasureBeatNumbers(beatIndex, beatsPerMeasure);
        Assert.Equal(expectedMeasure, measure);
        Assert.Equal(expectedBeat, beat);
    }
}

[Collection("SessionPreferences")]
public class WaitingCountInPlayerTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();
    private readonly FakeCountInClickService _clicks = new();

    public WaitingCountInPlayerTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Fact]
    public async Task Stop_CancelsActiveLoop()
    {
        var player = new WaitingCountInPlayer(_clicks);
        using var cts = new CancellationTokenSource();
        var run = player.RunAsync(120, 4, 0.4f, 0.2f, 1760, 880, 20, cts.Token);
        await Task.Delay(30);
        Assert.True(player.IsActive);
        player.Stop();
        await Task.Delay(50);
        Assert.False(player.IsActive);
        await run;
    }

    [Fact]
    public async Task StartingAgain_DoesNotLeaveDuplicateActiveLoops()
    {
        var player = new WaitingCountInPlayer(_clicks);
        using var cts = new CancellationTokenSource();
        var first = player.RunAsync(180, 4, 0.4f, 0.2f, 1760, 880, 15, cts.Token);
        await Task.Delay(20);
        var second = player.RunAsync(180, 4, 0.4f, 0.2f, 1760, 880, 15, cts.Token);
        await Task.Delay(40);
        Assert.True(player.IsActive);
        player.Stop();
        await Task.WhenAll(first, second);
        Assert.False(player.IsActive);
        Assert.True(_clicks.StopCount >= 1);
        Assert.True(_clicks.PlayCount >= 1);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public async Task BeatSpacing_FollowsAbsoluteGrid(int tempoBpm)
    {
        var clicks = new InstantCountInClickService();
        var player = new WaitingCountInPlayer(clicks);
        using var cts = new CancellationTokenSource();
        double msPerBeat = WaitingCountInLogic.MsPerBeat(tempoBpm);
        const int beatsPerMeasure = 4;
        const int measureCount = 3;
        int beatCount = beatsPerMeasure * measureCount;

        var run = player.RunAsync(tempoBpm, beatsPerMeasure, 0.4f, 0.2f, 1760, 880, 20, cts.Token);
        await Task.Delay((int)Math.Round(msPerBeat * beatCount + msPerBeat * 0.5), cts.Token);
        player.Stop();
        await run;

        Assert.InRange(clicks.AllPlayTimesMs.Count, beatCount - 1, beatCount + 1);

        double maxError = 0;
        double prevError = 0;
        for (int i = 0; i < clicks.AllPlayTimesMs.Count; i++)
        {
            double intended = i * msPerBeat;
            double actual = clicks.AllPlayTimesMs[i];
            double error = Math.Abs(actual - intended);
            maxError = Math.Max(maxError, error);

            // Timing error must not accumulate beat-to-beat.
            if (i > 0)
                Assert.True(error <= prevError + 35,
                    $"Beat {i} error grew from {prevError:F1}ms to {error:F1}ms at {tempoBpm} BPM");

            prevError = error;
            Assert.InRange(error, 0, 35);
        }

        for (int i = 1; i < clicks.AllPlayTimesMs.Count; i++)
        {
            double gap = clicks.AllPlayTimesMs[i] - clicks.AllPlayTimesMs[i - 1];
            Assert.InRange(gap, msPerBeat - 35, msPerBeat + 35);
        }
    }

    [Fact]
    public async Task StopAndRestart_ResetsGridWithoutDuplicateLoops()
    {
        var clicks = new InstantCountInClickService();
        var player = new WaitingCountInPlayer(clicks);
        using var cts = new CancellationTokenSource();

        var first = player.RunAsync(120, 4, 0.4f, 0.2f, 1760, 880, 20, cts.Token);
        await Task.Delay(250);
        player.Stop();
        await first;

        int afterFirstStop = clicks.PlayCount;
        clicks.Clear();

        var second = player.RunAsync(120, 4, 0.4f, 0.2f, 1760, 880, 20, cts.Token);
        await Task.Delay(600);
        player.Stop();
        await second;

        Assert.True(afterFirstStop >= 1);
        Assert.InRange(clicks.AllPlayTimesMs.Count, 2, 4);
        Assert.InRange(clicks.AllPlayTimesMs[0], 0, 25);
        if (clicks.AllPlayTimesMs.Count >= 2)
        {
            double gap = clicks.AllPlayTimesMs[1] - clicks.AllPlayTimesMs[0];
            Assert.InRange(gap, 500 - 35, 500 + 35);
        }
    }

    [Theory]
    [InlineData(120)]
    [InlineData(130)]
    [InlineData(150)]
    [InlineData(180)]
    [InlineData(200)]
    public async Task HighTempos_BeatSpacing_StaysEven(int tempoBpm)
    {
        var clicks = new InstantCountInClickService();
        var player = new WaitingCountInPlayer(clicks);
        using var cts = new CancellationTokenSource();
        double msPerBeat = WaitingCountInLogic.MsPerBeat(tempoBpm);
        const int beatsPerMeasure = 4;
        const int measureCount = 3;
        int beatCount = beatsPerMeasure * measureCount;
        int toleranceMs = tempoBpm >= 150 ? 20 : 25;

        var run = player.RunAsync(tempoBpm, beatsPerMeasure, 0.4f, 0.2f, 1760, 880, 20, cts.Token);
        await Task.Delay((int)Math.Round(msPerBeat * beatCount + msPerBeat * 0.5), cts.Token);
        player.Stop();
        await run;

        Assert.InRange(clicks.AllPlayTimesMs.Count, beatCount - 1, beatCount + 1);

        double maxError = 0;
        for (int i = 0; i < clicks.AllPlayTimesMs.Count; i++)
        {
            double intended = i * msPerBeat;
            double error = Math.Abs(clicks.AllPlayTimesMs[i] - intended);
            maxError = Math.Max(maxError, error);
            Assert.InRange(error, 0, toleranceMs);
        }

        for (int i = 1; i < clicks.AllPlayTimesMs.Count; i++)
        {
            double gap = clicks.AllPlayTimesMs[i] - clicks.AllPlayTimesMs[i - 1];
            Assert.InRange(gap, msPerBeat - toleranceMs, msPerBeat + toleranceMs);
        }
    }

    [Theory]
    [InlineData(120)]
    [InlineData(130)]
    [InlineData(150)]
    [InlineData(180)]
    [InlineData(200)]
    public async Task HighTempos_DownbeatsAreNotDoublePulsed(int tempoBpm)
    {
        var player = new WaitingCountInPlayer(_clicks);
        using var cts = new CancellationTokenSource();
        double msPerBeat = WaitingCountInLogic.MsPerBeat(tempoBpm);
        int clickPlaybackMs = (int)Math.Round(
            WaitingCountInLogic.ResolveClickPlaybackMs(20, msPerBeat));

        var run = player.RunAsync(tempoBpm, 4, 0.4f, 0.2f, 1760, 880, 20, cts.Token);

        await Task.Delay((int)Math.Round(msPerBeat * 8.5), cts.Token);
        player.Stop();
        await run;

        var accented = _clicks.AccentedPlayTimesMs;
        Assert.True(accented.Count >= 2, $"Expected at least two downbeats at {tempoBpm} BPM");

        for (int i = 1; i < accented.Count; i++)
        {
            double gap = accented[i] - accented[i - 1];
            Assert.InRange(gap, msPerBeat * 3.5, msPerBeat * 4.5);
        }

        foreach (double downbeatMs in accented)
        {
            int nearby = _clicks.AllPlayTimesMs.Count(
                t => Math.Abs(t - downbeatMs) > 1 && Math.Abs(t - downbeatMs) < clickPlaybackMs);
            Assert.Equal(0, nearby);
        }
    }

    private sealed class InstantCountInClickService : ICountInClickService
    {
        public int StopCount;
        public int PlayCount;
        public int WarmupCount;
        public List<double> AllPlayTimesMs { get; } = new();
        public List<double> AccentedPlayTimesMs { get; } = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public void Clear()
        {
            lock (AllPlayTimesMs)
            {
                AllPlayTimesMs.Clear();
                AccentedPlayTimesMs.Clear();
            }

            PlayCount = 0;
            _clock.Restart();
        }

        public void Warmup(
            double accentedPitchHz,
            float accentedVolume,
            double unaccentedPitchHz,
            float unaccentedVolume,
            int durationMs)
        {
            _ = (accentedPitchHz, accentedVolume, unaccentedPitchHz, unaccentedVolume, durationMs);
            Interlocked.Increment(ref WarmupCount);
        }

        public Task PlayClickAsync(
            bool accented,
            int durationMs,
            float volume,
            double frequencyHz,
            CancellationToken ct,
            MetronomeClickScheduleInfo? schedule = null)
        {
            _ = schedule;
            if (ct.IsCancellationRequested)
                return Task.CompletedTask;
            Interlocked.Increment(ref PlayCount);
            double atMs = _clock.Elapsed.TotalMilliseconds;
            lock (AllPlayTimesMs)
            {
                AllPlayTimesMs.Add(atMs);
                if (accented)
                    AccentedPlayTimesMs.Add(atMs);
            }

            return Task.CompletedTask;
        }

        public void Stop() => Interlocked.Increment(ref StopCount);
    }

    private sealed class FakeCountInClickService : ICountInClickService
    {
        public int StopCount;
        public int PlayCount;
        public List<double> AllPlayTimesMs { get; } = new();
        public List<double> AccentedPlayTimesMs { get; } = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        public void Warmup(
            double accentedPitchHz,
            float accentedVolume,
            double unaccentedPitchHz,
            float unaccentedVolume,
            int durationMs)
        {
            _ = (accentedPitchHz, accentedVolume, unaccentedPitchHz, unaccentedVolume, durationMs);
        }

        public async Task PlayClickAsync(
            bool accented,
            int durationMs,
            float volume,
            double frequencyHz,
            CancellationToken ct,
            MetronomeClickScheduleInfo? schedule = null)
        {
            _ = schedule;
            if (ct.IsCancellationRequested)
                return;
            Interlocked.Increment(ref PlayCount);
            double atMs = _clock.Elapsed.TotalMilliseconds;
            lock (AllPlayTimesMs)
            {
                AllPlayTimesMs.Add(atMs);
                if (accented)
                    AccentedPlayTimesMs.Add(atMs);
            }

            await Task.Delay(Math.Max(1, durationMs), ct);
        }

        public void Stop() => Interlocked.Increment(ref StopCount);
    }
}
