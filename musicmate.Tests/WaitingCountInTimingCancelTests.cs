using System.Diagnostics;
using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Absolute Count-In grid timing + Stop/Go cancellation contracts.
/// </summary>
[Collection("SessionPreferences")]
public class WaitingCountInTimingCancelTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public WaitingCountInTimingCancelTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

        [Theory]
        [InlineData(30)]
        [InlineData(52)]
        [InlineData(60)]
        [InlineData(100)]
        [InlineData(130)]
        [InlineData(180)]
        public async Task BeatIntervals_StayWithinTolerance_AcrossTempos(int tempoBpm)
    {
        var clicks = new RecordingInstantClicks();
        var player = new WaitingCountInPlayer(clicks);
        using var cts = new CancellationTokenSource();
        double msPerBeat = WaitingCountInLogic.MsPerBeat(tempoBpm);
        int beatCount = tempoBpm <= 52 ? 4 : 6;
        int toleranceMs = tempoBpm >= 150 ? 40 : 35;

        var run = player.RunAsync(tempoBpm, 4, 0.4f, 0.2f, 1760, 880, 15, cts.Token);
        await Task.Delay((int)Math.Round(msPerBeat * beatCount + msPerBeat * 0.75));
        player.Stop();
        await run;

        Assert.True(clicks.WarmupCount >= 1, "Warmup must run before the grid");
        Assert.InRange(clicks.PlayTimesMs.Count, beatCount - 1, beatCount + 1);

        for (int i = 0; i < clicks.PlayTimesMs.Count; i++)
        {
            double intended = WaitingCountInLogic.GetAbsoluteBeatStartMs(i, msPerBeat);
            double error = Math.Abs(clicks.PlayTimesMs[i] - intended);
            Assert.True(error <= toleranceMs,
                $"Beat {i} error {error:F1}ms exceeds {toleranceMs}ms at {tempoBpm} BPM");
        }

        for (int i = 1; i < clicks.PlayTimesMs.Count; i++)
        {
            double gap = clicks.PlayTimesMs[i] - clicks.PlayTimesMs[i - 1];
            Assert.InRange(gap, msPerBeat - toleranceMs, msPerBeat + toleranceMs);
        }

        double firstError = Math.Abs(
            clicks.PlayTimesMs[0] - WaitingCountInLogic.GetAbsoluteBeatStartMs(0, msPerBeat));
        int last = clicks.PlayTimesMs.Count - 1;
        double lastError = Math.Abs(
            clicks.PlayTimesMs[last] - WaitingCountInLogic.GetAbsoluteBeatStartMs(last, msPerBeat));
        Assert.True(lastError <= firstError + toleranceMs,
            $"Timing drifted from {firstError:F1}ms on beat 0 to {lastError:F1}ms on beat {last} at {tempoBpm} BPM");
    }

    [Theory]
    [InlineData(30)]
    [InlineData(52)]
    [InlineData(60)]
    [InlineData(100)]
    [InlineData(130)]
    [InlineData(180)]
    public void LateWake_DoesNotMoveTheNextAbsoluteTarget(int tempoBpm)
    {
        double interval = WaitingCountInLogic.MsPerBeat(tempoBpm);
        Assert.Equal(60000.0 / tempoBpm, interval, precision: 6);

        for (int n = 0; n < 32; n++)
        {
            double target = WaitingCountInLogic.GetAbsoluteBeatStartMs(n, interval);
            double lateActual = target + 18.0;
            double next = WaitingCountInLogic.GetAbsoluteBeatStartMs(n + 1, interval);
            double chainedFromLateBeep = lateActual + interval;

            Assert.Equal(target + interval, next, precision: 3);
            Assert.True(Math.Abs(next - chainedFromLateBeep) > 10,
                $"Beat {n + 1} target followed the late beep at {tempoBpm} BPM");
            Assert.False(WaitingCountInLogic.IsTooLateToSound(lateActual, target, interval));
        }
    }

    [Fact]
    public void BeatLog_ReportsTargetActualAndError()
    {
        var info = new MetronomeClickScheduleInfo(2, 1, 3, 2000, 2000, 0, 1);
        Assert.Equal(
            "Beat 3 target=2000.0 actual=2006.4 error=+6.4 ms",
            info.FormatBeatLine(2006.4));
        Assert.Equal(
            "Beat 1 target=0.0 actual=4.0 error=+4.0 ms",
            new MetronomeClickScheduleInfo(0, 1, 1, 0, 0, 0, 1).FormatBeatLine(4));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CountInAndMetronome_ScheduleTheSameAbsoluteTargets(bool countInBeforeClick)
    {
        var clicks = new RecordingInstantClicks();
        var player = new WaitingCountInPlayer(clicks);
        using var cts = new CancellationTokenSource();
        const int tempoBpm = 180;
        double msPerBeat = WaitingCountInLogic.MsPerBeat(tempoBpm);
        Func<int, CancellationToken, Task>? beforeClick = countInBeforeClick
            ? (_, _) => Task.CompletedTask
            : null;

        var run = player.RunAsync(
            tempoBpm, 4, 0.4f, 0.2f, 1760, 880, 15, cts.Token, beforeClickAsync: beforeClick);
        await Task.Delay((int)Math.Round(msPerBeat * 5));
        player.Stop();
        await run;

        Assert.True(clicks.IntendedMs.Count >= 4);
        for (int i = 0; i < clicks.IntendedMs.Count; i++)
        {
            Assert.Equal(
                WaitingCountInLogic.GetAbsoluteBeatStartMs(i, msPerBeat),
                clicks.IntendedMs[i],
                precision: 3);
        }
    }

    [Fact]
    public async Task Restart_DoesNotRunTwoTimingLoops()
    {
        var clicks = new RecordingInstantClicks();
        var player = new WaitingCountInPlayer(clicks);
        using var cts = new CancellationTokenSource();

        var first = player.RunAsync(120, 4, 0.4f, 0.2f, 1760, 880, 15, cts.Token);
        await Task.Delay(40);
        int stopsBeforeRestart = clicks.StopCount;
        var second = player.RunAsync(120, 4, 0.4f, 0.2f, 1760, 880, 15, cts.Token);
        await Task.Delay(40);
        Assert.True(clicks.StopCount > stopsBeforeRestart);
        Assert.True(player.IsActive);

        int atSecondLoop = clicks.PlayCount;
        await Task.Delay(1100);
        player.Stop();
        await Task.WhenAll(first, second);

        int added = clicks.PlayCount - atSecondLoop;
        Assert.InRange(added, 1, 4);
        Assert.False(player.IsActive);
    }

    [Fact]
    public async Task OverlappingStarts_PlayOnlyTheNewestLoop()
    {
        var clicks = new RecordingInstantClicks();
        var player = new WaitingCountInPlayer(clicks);
        using var cts = new CancellationTokenSource();

        var first = player.RunAsync(
            120, 4, 0.4f, 0.2f, 1760, 880, 15, cts.Token, source: "COUNTIN");
        var second = player.RunAsync(
            120, 4, 0.4f, 0.2f, 1760, 880, 15, cts.Token, source: "METRONOME");
        await Task.Delay(900);
        player.Stop();
        await Task.WhenAll(first, second);

        Assert.NotEmpty(clicks.LoopIds);
        Assert.Single(clicks.LoopIds.Distinct());
        Assert.InRange(clicks.PlayCount, 1, 4);
        Assert.False(player.IsActive);
    }

    [Fact]
    public async Task StopDuringCountIn_PreventsAllSubsequentBeats()
    {
        var clicks = new RecordingInstantClicks();
        var player = new WaitingCountInPlayer(clicks);
        using var cts = new CancellationTokenSource();

        var run = player.RunAsync(120, 4, 0.4f, 0.2f, 1760, 880, 15, cts.Token);
        await Task.Delay(80);
        Assert.True(clicks.PlayCount >= 1 || player.IsActive);

        int atStop = clicks.PlayCount;
        cts.Cancel();
        player.Stop();
        await Task.Delay(350);
        await run;

        Assert.False(player.IsActive);
        Assert.Equal(atStop, clicks.PlayCount);
        Assert.True(clicks.StopCount >= 1);
    }

    [Fact]
    public async Task StopJustBeforeNextBeat_DoesNotSoundThatBeat()
    {
        var clicks = new RecordingInstantClicks();
        var player = new WaitingCountInPlayer(clicks);
        using var cts = new CancellationTokenSource();
        const int tempo = 60; // 1000 ms/beat
        double msPerBeat = WaitingCountInLogic.MsPerBeat(tempo);

        var run = player.RunAsync(tempo, 4, 0.4f, 0.2f, 1760, 880, 15, cts.Token);

        await Task.Delay(40);
        Assert.True(clicks.PlayCount >= 1);
        int afterFirst = clicks.PlayCount;

        // Stop well before the next grid slot (not in the final few ms where the
        // scheduler has already committed the beat to the play queue).
        await Task.Delay((int)Math.Round(msPerBeat * 0.55));
        cts.Cancel();
        player.Stop();

        await Task.Delay((int)Math.Round(msPerBeat * 0.75));
        await run;

        Assert.Equal(afterFirst, clicks.PlayCount);
    }

    [Fact]
    public async Task GoAfterStop_StartsExactlyOneFreshCountIn()
    {
        var clicks = new RecordingInstantClicks();
        var player = new WaitingCountInPlayer(clicks);
        int generation = 0;
        bool isRunning = true;
        CancellationTokenSource? pageCts = null;

        int first = WaitingCountInArming.Arm(ref generation);
        var firstRun = RunArmedAsync(player, first, () => generation, () => isRunning, c => pageCts = c);
        await Task.Delay(50);

        WaitingCountInArming.Invalidate(ref generation);
        isRunning = false;
        try { pageCts?.Cancel(); } catch { }
        player.Stop();
        await firstRun;

        int playsAfterStop = clicks.PlayCount;
        clicks.RestartClock();

        isRunning = true;
        int second = WaitingCountInArming.Arm(ref generation);
        var secondRun = RunArmedAsync(player, second, () => generation, () => isRunning, c => pageCts = c);
        await Task.Delay(120);

        Assert.True(clicks.PlayCount > playsAfterStop);
        // Fresh grid: first audible beat of the new run lands near t=0 of the new clock.
        Assert.InRange(clicks.PlayTimesMs[0], 0, 45);

        WaitingCountInArming.Invalidate(ref generation);
        isRunning = false;
        try { pageCts?.Cancel(); } catch { }
        player.Stop();
        await secondRun;
        Assert.False(player.IsActive);
    }

    [Fact]
    public async Task RepeatedStopGo_NeverOverlapsCountInSequences()
    {
        var clicks = new GatingClickService();
        var player = new WaitingCountInPlayer(clicks);
        int generation = 0;
        bool isRunning = false;
        CancellationTokenSource? pageCts = null;
        var runs = new List<Task>();

        for (int i = 0; i < 5; i++)
        {
            WaitingCountInArming.Invalidate(ref generation);
            isRunning = false;
            try { pageCts?.Cancel(); } catch { }
            player.Stop();

            isRunning = true;
            int armed = WaitingCountInArming.Arm(ref generation);
            runs.Add(RunArmedAsync(player, armed, () => generation, () => isRunning, c => pageCts = c));
            await Task.Delay(25);
        }

        WaitingCountInArming.Invalidate(ref generation);
        isRunning = false;
        try { pageCts?.Cancel(); } catch { }
        player.Stop();
        await Task.WhenAll(runs);

        Assert.True(clicks.MaxConcurrentPlays <= 1,
            $"Overlapping Count-In plays detected: max concurrent={clicks.MaxConcurrentPlays}");
        Assert.False(player.IsActive);
    }

    [Fact]
    public async Task NavigateAwayPattern_CancelsActiveCountIn()
    {
        // Mirrors MusicPage.OnDisappearing: Invalidate + cancel page CTS + player.Stop + clicks.Stop.
        var clicks = new RecordingInstantClicks();
        var player = new WaitingCountInPlayer(clicks);
        int generation = 0;
        bool isRunning = true;
        CancellationTokenSource? pageCts = null;

        int armed = WaitingCountInArming.Arm(ref generation);
        var run = RunArmedAsync(player, armed, () => generation, () => isRunning, c => pageCts = c);
        await Task.Delay(60);
        Assert.True(player.IsActive || clicks.PlayCount >= 1);

        // OnDisappearing
        WaitingCountInArming.Invalidate(ref generation);
        isRunning = false;
        try { pageCts?.Cancel(); } catch { }
        player.Stop();
        clicks.Stop();

        int atNav = clicks.PlayCount;
        await Task.Delay(250);
        await run;

        Assert.False(player.IsActive);
        Assert.Equal(atNav, clicks.PlayCount);
    }

    [Fact]
    public async Task TimingStaysRegular_WhenClickPathIsBrieflyBusy()
    {
        var clicks = new BusyClickService(busyMs: 30);
        var player = new WaitingCountInPlayer(clicks);
        using var cts = new CancellationTokenSource();
        const int tempo = 100;
        double msPerBeat = WaitingCountInLogic.MsPerBeat(tempo);
        const int beatCount = 6;
        const int toleranceMs = 45;

        var run = player.RunAsync(tempo, 4, 0.4f, 0.2f, 1760, 880, 15, cts.Token);
        await Task.Delay((int)Math.Round(msPerBeat * beatCount + msPerBeat));
        player.Stop();
        await run;

        Assert.InRange(clicks.PlayTimesMs.Count, beatCount - 1, beatCount + 1);
        for (int i = 1; i < clicks.PlayTimesMs.Count; i++)
        {
            double gap = clicks.PlayTimesMs[i] - clicks.PlayTimesMs[i - 1];
            Assert.InRange(gap, msPerBeat - toleranceMs, msPerBeat + toleranceMs);
        }
    }

    [Fact]
    public async Task BusyClick_DoesNotCarryItsDelayIntoTheNextTarget()
    {
        const int busyMs = 80;
        var clicks = new BusyClickService(busyMs);
        var player = new WaitingCountInPlayer(clicks);
        using var cts = new CancellationTokenSource();
        const int tempo = 100;
        double msPerBeat = WaitingCountInLogic.MsPerBeat(tempo);
        const int beatCount = 5;
        const int toleranceMs = 30;

        var run = player.RunAsync(tempo, 4, 0.4f, 0.2f, 1760, 880, 15, cts.Token);
        await Task.Delay((int)Math.Round(msPerBeat * beatCount + msPerBeat));
        player.Stop();
        await run;

        Assert.InRange(clicks.PlayTimesMs.Count, beatCount - 1, beatCount + 1);
        for (int i = 1; i < clicks.PlayTimesMs.Count; i++)
        {
            double gap = clicks.PlayTimesMs[i] - clicks.PlayTimesMs[i - 1];
            Assert.InRange(gap, msPerBeat - toleranceMs, msPerBeat + toleranceMs);
            Assert.True(gap < msPerBeat + busyMs - 20,
                $"Beat {i} gap {gap:F1}ms includes the previous click's {busyMs}ms");
        }
    }

    private static async Task RunArmedAsync(
        WaitingCountInPlayer player,
        int armedGeneration,
        Func<int> currentGeneration,
        Func<bool> isRunning,
        Action<CancellationTokenSource> captureCts)
    {
        if (!WaitingCountInArming.MayBegin(armedGeneration, currentGeneration(), isRunning()))
            return;

        var cts = new CancellationTokenSource();
        captureCts(cts);
        try
        {
            if (!WaitingCountInArming.MayContinue(
                    armedGeneration, currentGeneration(), isRunning(), cts.IsCancellationRequested))
                return;

            await player.RunAsync(
                tempoBpm: 160,
                beatsPerMeasure: 4,
                accentedVolume: 0.4f,
                unaccentedVolume: 0.2f,
                accentedPitchHz: 1760,
                unaccentedPitchHz: 880,
                beatDurationPercent: 15,
                externalCt: cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    private sealed class RecordingInstantClicks : ICountInClickService
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly object _lock = new();
        private int _epoch;
            public List<double> PlayTimesMs { get; } = new();
            public List<double> IntendedMs { get; } = new();
            public List<int> LoopIds { get; } = new();
            public int PlayCount { get; private set; }
        public int StopCount { get; private set; }
        public int WarmupCount { get; private set; }

        public void RestartClock()
        {
            lock (_lock)
            {
                PlayTimesMs.Clear();
                IntendedMs.Clear();
                LoopIds.Clear();
                _clock.Restart();
            }
        }

        public void Warmup(
            double accentedPitchHz,
            float accentedVolume,
            double unaccentedPitchHz,
            float unaccentedVolume,
            int durationMs)
        {
            _ = (accentedPitchHz, accentedVolume, unaccentedPitchHz, unaccentedVolume, durationMs);
            WarmupCount++;
        }

        public Task PlayClickAsync(
            bool accented,
            int durationMs,
            float volume,
            double frequencyHz,
            CancellationToken ct,
            MetronomeClickScheduleInfo? schedule = null)
        {
            _ = (accented, durationMs, volume, frequencyHz);
            if (ct.IsCancellationRequested)
                return Task.CompletedTask;

            int epochAtStart = Volatile.Read(ref _epoch);
            if (ct.IsCancellationRequested || Volatile.Read(ref _epoch) != epochAtStart)
                return Task.CompletedTask;

            double atMs = schedule?.SchedulerMs
                ?? _clock.Elapsed.TotalMilliseconds;
            lock (_lock)
            {
                if (Volatile.Read(ref _epoch) != epochAtStart)
                    return Task.CompletedTask;
                PlayTimesMs.Add(atMs);
                if (schedule.HasValue)
                {
                    IntendedMs.Add(schedule.Value.IntendedMs);
                    LoopIds.Add(schedule.Value.LoopId);
                }
                PlayCount++;
            }

            return Task.CompletedTask;
        }

        public void Stop()
        {
            Interlocked.Increment(ref _epoch);
            StopCount++;
        }
    }

    private sealed class BusyClickService : ICountInClickService
    {
        private readonly int _busyMs;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly object _lock = new();
        public List<double> PlayTimesMs { get; } = new();

        public BusyClickService(int busyMs) => _busyMs = busyMs;

        public void Warmup(
            double accentedPitchHz,
            float accentedVolume,
            double unaccentedPitchHz,
            float unaccentedVolume,
            int durationMs)
        {
            _ = (accentedPitchHz, accentedVolume, unaccentedPitchHz, unaccentedVolume, durationMs);
            Thread.Sleep(_busyMs); // warmup may be sync; grid starts after
        }

        public Task PlayClickAsync(
            bool accented,
            int durationMs,
            float volume,
            double frequencyHz,
            CancellationToken ct,
            MetronomeClickScheduleInfo? schedule = null)
        {
            _ = (accented, durationMs, volume, frequencyHz);
            if (ct.IsCancellationRequested)
                return Task.CompletedTask;

            // Simulate device/UI busy work on the play path (must not stretch the absolute grid).
            Thread.Sleep(_busyMs);
            if (ct.IsCancellationRequested)
                return Task.CompletedTask;

            double atMs = schedule?.SchedulerMs ?? _clock.Elapsed.TotalMilliseconds;
            lock (_lock)
                PlayTimesMs.Add(atMs);
            return Task.CompletedTask;
        }

        public void Stop() { }
    }

    private sealed class GatingClickService : ICountInClickService
    {
        private int _inflight;
        public int MaxConcurrentPlays { get; private set; }

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
            _ = (accented, durationMs, volume, frequencyHz, schedule);
            if (ct.IsCancellationRequested)
                return;

            int now = Interlocked.Increment(ref _inflight);
            MaxConcurrentPlays = Math.Max(MaxConcurrentPlays, now);
            try
            {
                await Task.Delay(20, ct);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                Interlocked.Decrement(ref _inflight);
            }
        }

        public void Stop() { }
    }
}
