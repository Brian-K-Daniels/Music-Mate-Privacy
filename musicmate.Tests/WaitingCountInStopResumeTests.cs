using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Count-In Stop must kill delayed starts; Go/OnAppearing may arm again.
/// </summary>
[Collection("SessionPreferences")]
public class WaitingCountInArmingTests
{
    [Fact]
    public void Stop_InvalidatesArmedGeneration_SoDelayedStartCannotBegin()
    {
        int generation = 0;
        int armed = WaitingCountInArming.Arm(ref generation);
        Assert.True(WaitingCountInArming.MayBegin(armed, generation, sessionIsRunning: true));

        WaitingCountInArming.Invalidate(ref generation);

        Assert.False(WaitingCountInArming.MayBegin(armed, generation, sessionIsRunning: true));
        Assert.False(WaitingCountInArming.MayContinue(
            armed, generation, sessionIsRunning: true, cancellationRequested: false));
    }

    [Fact]
    public void Stop_WhileSessionStillMarkedRunning_StillBlocksCountIn()
    {
        // Stop invalidates generation before or as _isRunning clears; either must block.
        int generation = 0;
        int armed = WaitingCountInArming.Arm(ref generation);
        WaitingCountInArming.Invalidate(ref generation);
        Assert.False(WaitingCountInArming.MayBegin(armed, generation, sessionIsRunning: true));
    }

    [Fact]
    public void Go_AfterStop_CanArmFreshGeneration()
    {
        int generation = 0;
        int first = WaitingCountInArming.Arm(ref generation);
        WaitingCountInArming.Invalidate(ref generation);

        int second = WaitingCountInArming.Arm(ref generation);
        Assert.NotEqual(first, second);
        Assert.True(WaitingCountInArming.MayBegin(second, generation, sessionIsRunning: true));
        Assert.False(WaitingCountInArming.MayBegin(first, generation, sessionIsRunning: true));
    }

    [Fact]
    public void OnAppearing_AfterStop_CanArmFreshGeneration()
    {
        // Same arming path as Go — OnAppearing ScheduleAutoStart eventually Arms again.
        int generation = 0;
        WaitingCountInArming.Arm(ref generation);
        WaitingCountInArming.Invalidate(ref generation);

        int appearGen = WaitingCountInArming.Arm(ref generation);
        Assert.True(WaitingCountInArming.MayBegin(appearGen, generation, sessionIsRunning: true));
    }

    [Fact]
    public void MayContinue_FalseWhenSessionStoppedOrCancelled()
    {
        int generation = 0;
        int armed = WaitingCountInArming.Arm(ref generation);
        Assert.False(WaitingCountInArming.MayContinue(
            armed, generation, sessionIsRunning: false, cancellationRequested: false));
        Assert.False(WaitingCountInArming.MayContinue(
            armed, generation, sessionIsRunning: true, cancellationRequested: true));
    }
}

[Collection("SessionPreferences")]
public class WaitingCountInStopPreventsRestartTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();
    private readonly RecordingClickService _clicks = new();

    public WaitingCountInStopPreventsRestartTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Fact]
    public async Task StopDuringCountIn_PreventsLaterSoundOrRestart()
    {
        var player = new WaitingCountInPlayer(_clicks);
        int generation = 0;
        bool isRunning = true;
        CancellationTokenSource? cts = null;

        int armed = WaitingCountInArming.Arm(ref generation);
        var startTask = RunArmedCountInAsync(
            player, armed, () => generation, () => isRunning, g => cts = g);

        await Task.Delay(40);
        Assert.True(_clicks.PlayCount >= 1 || player.IsActive);

        // Stop: invalidate + cancel + stop player (MusicPage StopWaitingCountIn + _isRunning=false)
        WaitingCountInArming.Invalidate(ref generation);
        isRunning = false;
        try { cts?.Cancel(); } catch { }
        player.Stop();

        int playsAtStop = _clicks.PlayCount;
        await Task.Delay(200);
        await startTask;

        Assert.False(player.IsActive);
        Assert.Equal(playsAtStop, _clicks.PlayCount);

        // Stale delayed start with old generation must not revive clicks.
        var stale = RunArmedCountInAsync(
            player, armed, () => generation, () => true, _ => { });
        await stale;
        Assert.False(player.IsActive);
        Assert.Equal(playsAtStop, _clicks.PlayCount);
    }

    [Fact]
    public async Task GoAfterStop_CanStartCountInAgain()
    {
        var player = new WaitingCountInPlayer(_clicks);
        int generation = 0;
        bool isRunning = true;
        CancellationTokenSource? cts = null;

        int first = WaitingCountInArming.Arm(ref generation);
        var firstRun = RunArmedCountInAsync(
            player, first, () => generation, () => isRunning, g => cts = g);
        await Task.Delay(30);

        WaitingCountInArming.Invalidate(ref generation);
        isRunning = false;
        try { cts?.Cancel(); } catch { }
        player.Stop();
        await firstRun;

        int playsAfterStop = _clicks.PlayCount;
        isRunning = true;
        int second = WaitingCountInArming.Arm(ref generation);
        var secondRun = RunArmedCountInAsync(
            player, second, () => generation, () => isRunning, g => cts = g);
        await Task.Delay(80);
        Assert.True(_clicks.PlayCount > playsAfterStop);

        WaitingCountInArming.Invalidate(ref generation);
        isRunning = false;
        try { cts?.Cancel(); } catch { }
        player.Stop();
        await secondRun;
    }

    [Fact]
    public async Task OnAppearingAfterStop_CanStartCountInAgain()
    {
        // Mirrors ScheduleAutoStartOnAppear -> StartListening -> Arm after a prior Stop.
        var player = new WaitingCountInPlayer(_clicks);
        int generation = 0;
        bool isRunning = false;

        int goGen = WaitingCountInArming.Arm(ref generation);
        isRunning = true;
        CancellationTokenSource? cts = null;
        var run = RunArmedCountInAsync(
            player, goGen, () => generation, () => isRunning, g => cts = g);
        await Task.Delay(30);
        WaitingCountInArming.Invalidate(ref generation);
        isRunning = false;
        try { cts?.Cancel(); } catch { }
        player.Stop();
        await run;

        int plays = _clicks.PlayCount;
        // OnAppearing auto-start
        isRunning = true;
        int appearGen = WaitingCountInArming.Arm(ref generation);
        var appearRun = RunArmedCountInAsync(
            player, appearGen, () => generation, () => isRunning, g => cts = g);
        await Task.Delay(80);
        Assert.True(_clicks.PlayCount > plays);
        WaitingCountInArming.Invalidate(ref generation);
        isRunning = false;
        try { cts?.Cancel(); } catch { }
        player.Stop();
        await appearRun;
    }

    /// <summary>Mirrors MusicPage StartWaitingCountInAsync generation gates.</summary>
    private static async Task RunArmedCountInAsync(
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
            await Task.Delay(15, cts.Token);
            if (!WaitingCountInArming.MayContinue(
                    armedGeneration, currentGeneration(), isRunning(), cts.IsCancellationRequested))
                return;

            await player.RunAsync(
                tempoBpm: 180,
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

    private sealed class RecordingClickService : ICountInClickService
    {
        public int PlayCount { get; private set; }
        public int StopCount { get; private set; }

        public Task PlayClickAsync(
            bool accented,
            int durationMs,
            float volume,
            double frequencyHz,
            CancellationToken ct,
            MetronomeClickScheduleInfo? schedule = null)
        {
            _ = (accented, durationMs, volume, frequencyHz, schedule);
            PlayCount++;
            return Task.CompletedTask;
        }

        public void Stop() => StopCount++;
    }
}
