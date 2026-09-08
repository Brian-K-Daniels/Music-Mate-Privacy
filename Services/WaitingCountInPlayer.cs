using System.Diagnostics;
using System.Threading;
#if DEBUG
using musicmate.Diagnostics;
#endif

namespace musicmate.Services
{
    /// <summary>
    /// Schedules metronome-like count-in clicks until cancelled. Single active loop at a time.
    /// Uses <see cref="ICountInClickService"/> for short sine clicks on all platforms.
    /// </summary>
    public sealed class WaitingCountInPlayer
    {
        private readonly ICountInClickService _clicks;
        private CancellationTokenSource? _cts;
        private int _generation;
        private int _activeLoops;

        public WaitingCountInPlayer(ICountInClickService clicks)
        {
            _clicks = clicks;
        }

        public bool IsActive => Volatile.Read(ref _activeLoops) > 0;

        public int Generation => Volatile.Read(ref _generation);

        public void Stop()
        {
            Interlocked.Increment(ref _generation);
            try { _cts?.Cancel(); } catch { }
            try { _clicks.Stop(); } catch { }
            Volatile.Write(ref _activeLoops, 0);
        }

        public async Task RunAsync(
            int tempoBpm,
            int beatsPerMeasure,
            float accentedVolume,
            float unaccentedVolume,
            double accentedPitchHz,
            double unaccentedPitchHz,
            int beatDurationPercent,
            CancellationToken externalCt,
            Func<int, CancellationToken, Task>? beforeClickAsync = null,
            Func<CancellationToken, Task>? afterClickAsync = null,
            Func<int>? getTempoBpm = null)
        {
            Stop();
            int gen = Volatile.Read(ref _generation);
            _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            var ct = _cts.Token;
            Interlocked.Exchange(ref _activeLoops, 1);

            try
            {
                beatsPerMeasure = Math.Max(1, beatsPerMeasure);
                var clock = Stopwatch.StartNew();
                long beatIndex = 0;
                int activeTempo = Math.Clamp(
                    getTempoBpm?.Invoke() ?? tempoBpm,
                    NoteSessionService.MinTempo,
                    NoteSessionService.MaxTempo);

                while (!ct.IsCancellationRequested && Volatile.Read(ref _generation) == gen)
                {
                    int tempo = Math.Clamp(
                        getTempoBpm?.Invoke() ?? tempoBpm,
                        NoteSessionService.MinTempo,
                        NoteSessionService.MaxTempo);
                    if (tempo != activeTempo)
                    {
                        activeTempo = tempo;
                        clock.Restart();
                        beatIndex = 0;
                    }

                    double msPerBeat = WaitingCountInLogic.MsPerBeat(activeTempo);
                    double intendedMs = WaitingCountInLogic.GetAbsoluteBeatStartMs(beatIndex, msPerBeat);

                    await WaitUntilAsync(clock, intendedMs, ct).ConfigureAwait(false);

                    if (ct.IsCancellationRequested || Volatile.Read(ref _generation) != gen)
                        break;

                    double actualMs = clock.Elapsed.TotalMilliseconds;
                    double errorMs = actualMs - intendedMs;
                    var click = WaitingCountInLogic.BuildClick(
                        beatIndex,
                        beatsPerMeasure,
                        accentedVolume,
                        unaccentedVolume,
                        accentedPitchHz,
                        unaccentedPitchHz,
                        beatDurationPercent,
                        activeTempo);

                    var (measureNumber, beatNumber) = WaitingCountInLogic.GetMeasureBeatNumbers(
                        beatIndex, beatsPerMeasure);
#if DEBUG
                    MetronomeBeatDiagnostics.LogBeat(
                        measureNumber, beatNumber, intendedMs, actualMs, errorMs, click.IsAccented);
#endif

                    bool tooLate = WaitingCountInLogic.IsTooLateToSound(actualMs, intendedMs, msPerBeat);
                    if (!tooLate)
                    {
                        int durationMs = Math.Max(20, (int)Math.Round(click.DurationSeconds * 1000.0));
                        // Suppress window covers audible length + release + device start latency;
                        // pitch-window guard is added inside SuppressCountInClickSelfSound.
                        int selfSoundMs = WaitingCountInLogic.ResolveClickSelfSoundDurationMs(durationMs);
                        long schedulerTick = Stopwatch.GetTimestamp();
                        var schedule = new MetronomeClickScheduleInfo(
                            beatIndex, measureNumber, beatNumber, intendedMs, actualMs, schedulerTick);

                        try
                        {
                            // Arm self-sound suppress before the click reaches the speaker
                            // so mic/pitch evaluation cannot score the first note from bleed.
                            if (beforeClickAsync != null)
                                await beforeClickAsync(selfSoundMs, ct).ConfigureAwait(false);

                            if (ct.IsCancellationRequested || Volatile.Read(ref _generation) != gen)
                                break;

                            TriggerClick(
                                click.IsAccented,
                                durationMs,
                                click.Volume,
                                click.FrequencyHz,
                                ct,
                                schedule);

                            if (afterClickAsync != null
                                && Volatile.Read(ref _generation) == gen
                                && !ct.IsCancellationRequested)
                                _ = afterClickAsync(ct);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested
                            || Volatile.Read(ref _generation) != gen)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[CountIn] click failed: {ex}");
                        }
                    }

                    beatIndex++;
                }
            }
            catch (OperationCanceledException)
            {
                // expected
            }
            finally
            {
                if (Volatile.Read(ref _generation) == gen)
                    Volatile.Write(ref _activeLoops, 0);
            }
        }

        /// <summary>
        /// Wait until the monotonic clock reaches the absolute beat time.
        /// Uses coarse delay plus spin/yield finish — Task.Delay alone quantizes to ~15 ms on Windows.
        /// </summary>
        internal static async Task WaitUntilAsync(Stopwatch clock, double targetMs, CancellationToken ct)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                double remaining = targetMs - clock.Elapsed.TotalMilliseconds;
                if (remaining <= 0.5)
                    return;

                if (remaining > 25.0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(remaining - 15.0), ct).ConfigureAwait(false);
                    continue;
                }

                var spinBudget = Stopwatch.StartNew();
                while (targetMs - clock.Elapsed.TotalMilliseconds > 0.5)
                {
                    ct.ThrowIfCancellationRequested();
                    if (spinBudget.Elapsed.TotalMilliseconds >= 4.0)
                    {
                        await Task.Yield();
                        spinBudget.Restart();
                    }
                    else
                    {
                        Thread.SpinWait(50);
                    }
                }

                return;
            }
        }

        /// <summary>
        /// Fire the click without blocking the beat grid on click length.
        /// </summary>
        private void TriggerClick(
            bool accented,
            int durationMs,
            float volume,
            double frequencyHz,
            CancellationToken ct,
            MetronomeClickScheduleInfo schedule)
        {
            _ = _clicks.PlayClickAsync(accented, durationMs, volume, frequencyHz, ct, schedule)
                .ContinueWith(
                    t =>
                    {
                        if (t.IsFaulted && t.Exception != null)
                            Debug.WriteLine($"[CountIn] click failed: {t.Exception.GetBaseException().Message}");
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
        }
    }
}
