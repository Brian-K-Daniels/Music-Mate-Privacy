using System.Diagnostics;
using System.Threading;
using musicmate.Diagnostics;

namespace musicmate.Services
{
    /// <summary>
    /// Schedules metronome-like count-in clicks until cancelled. Single active loop at a time.
    /// Uses <see cref="ICountInClickService"/> for short sine clicks on all platforms.
    /// Beat times come from an absolute Stopwatch grid — never chained from prior click completion.
    /// </summary>
    public sealed class WaitingCountInPlayer
    {
        private readonly ICountInClickService _clicks;
        private readonly object _gate = new();
        private CancellationTokenSource? _cts;
        private int _generation;
        private int _activeLoops;
        private int _firstSoundNotified;
        private int _nextLoopId;
        private int _activeLoopId;
        private string _activeSource = "COUNTIN";

        public WaitingCountInPlayer(ICountInClickService clicks)
        {
            _clicks = clicks;
        }

        public bool IsActive => Volatile.Read(ref _activeLoops) > 0;

        public int Generation => Volatile.Read(ref _generation);

        public void Stop()
        {
            CancellationTokenSource? toCancel;
            int retiredLoopId;
            string retiredSource;
            lock (_gate)
            {
                toCancel = _cts;
                _cts = null;
                retiredLoopId = Volatile.Read(ref _activeLoopId);
                retiredSource = _activeSource;
                Volatile.Write(ref _activeLoopId, 0);
                Interlocked.Increment(ref _generation);
            }

            if (retiredLoopId != 0)
            {
                ListeningStartupLog.Write($"{retiredSource} session={retiredLoopId} event=stop");
                ListeningStartupLog.Write("COUNTIN: player.Stop");
            }

            // Cancel only — RunAsync's finally disposes the linked CTS it created.
            try { toCancel?.Cancel(); } catch { }
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
            Func<int>? getTempoBpm = null,
            Action? onFirstClickSounded = null,
            string source = "COUNTIN")
        {
            if (string.IsNullOrWhiteSpace(source))
                source = "COUNTIN";

            // Retire any loop already playing, then publish this loop's id and
            // generation together so a second start cannot keep the same id.
            Stop();
            Interlocked.Exchange(ref _firstSoundNotified, 0);
            CancellationTokenSource? replaced;
            int gen;
            int loopId;
            CancellationTokenSource linked;
            lock (_gate)
            {
                replaced = _cts;
                _cts = null;
                gen = Interlocked.Increment(ref _generation);
                loopId = Interlocked.Increment(ref _nextLoopId);
                _activeSource = source;
                Volatile.Write(ref _activeLoopId, loopId);
                linked = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
                _cts = linked;
            }

            try { replaced?.Cancel(); } catch { }

            var ct = linked.Token;
            Interlocked.Exchange(ref _activeLoops, 1);
            ListeningStartupLog.Write(
                $"{source} session={loopId} event=start bpm={tempoBpm} conductorAudible=false");

            try
            {
                beatsPerMeasure = Math.Max(1, beatsPerMeasure);
                int activeTempo = Math.Clamp(
                    getTempoBpm?.Invoke() ?? tempoBpm,
                    NoteSessionService.MinTempo,
                    NoteSessionService.MaxTempo);

                // Leave the UI thread before warmup. SoundPool's load callback has to
                // run on the main looper, and this wait must not block that looper.
                await Task.Delay(1, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested || Volatile.Read(ref _generation) != gen)
                    return;

                // Finish click preparation before the clock starts so load time is not
                // part of beat 0, and so later beats do not rebuild audio.
                WarmupClicks(
                    activeTempo,
                    beatDurationPercent,
                    accentedVolume,
                    unaccentedVolume,
                    accentedPitchHz,
                    unaccentedPitchHz);

                if (ct.IsCancellationRequested || Volatile.Read(ref _generation) != gen)
                    return;

                long gridOrigin = Stopwatch.GetTimestamp();
                var clock = Stopwatch.StartNew();
                long beatIndex = 0;

                while (!ct.IsCancellationRequested
                    && Volatile.Read(ref _generation) == gen
                    && Volatile.Read(ref _activeLoopId) == loopId)
                {
                    int tempo = Math.Clamp(
                        getTempoBpm?.Invoke() ?? tempoBpm,
                        NoteSessionService.MinTempo,
                        NoteSessionService.MaxTempo);
                    if (tempo != activeTempo)
                    {
                        activeTempo = tempo;
                        WarmupClicks(
                            activeTempo,
                            beatDurationPercent,
                            accentedVolume,
                            unaccentedVolume,
                            accentedPitchHz,
                            unaccentedPitchHz);
                        if (ct.IsCancellationRequested || Volatile.Read(ref _generation) != gen)
                            break;
                        clock.Restart();
                        gridOrigin = Stopwatch.GetTimestamp();
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
                        int selfSoundMs = WaitingCountInLogic.ResolveClickSelfSoundDurationMs(durationMs);
                        long schedulerTick = Stopwatch.GetTimestamp();
                        var schedule = new MetronomeClickScheduleInfo(
                            beatIndex, measureNumber, beatNumber, intendedMs, actualMs, schedulerTick, gridOrigin,
                            loopId, activeTempo, source);

                        try
                        {
                            if (beforeClickAsync != null)
                                await beforeClickAsync(selfSoundMs, ct).ConfigureAwait(false);

                            if (ct.IsCancellationRequested
                                || Volatile.Read(ref _generation) != gen
                                || Volatile.Read(ref _activeLoopId) != loopId)
                                break;

                            // Submit on this thread. Play is already prepared, so it must not
                            // wait for the previous beep, the UI thread, or the microphone.
                            TriggerClick(
                                gen,
                                loopId,
                                click.IsAccented,
                                durationMs,
                                click.Volume,
                                click.FrequencyHz,
                                ct,
                                schedule,
                                onFirstClickSounded);

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

                bool owned = Volatile.Read(ref _generation) == gen
                    && Volatile.Read(ref _activeLoopId) == loopId;
                ListeningStartupLog.Write(
                    $"{source} session={loopId} event={(ct.IsCancellationRequested || !owned ? "cancelled" : "stop")} " +
                    $"bpm={activeTempo}");
                ListeningStartupLog.Write(
                    $"COUNTIN: loop exit cancelled={ct.IsCancellationRequested} " +
                    $"generationMatch={Volatile.Read(ref _generation) == gen} session={loopId}");
            }
            catch (OperationCanceledException)
            {
                ListeningStartupLog.Write($"{source} session={loopId} event=cancelled");
                ListeningStartupLog.Write("COUNTIN: loop cancelled");
            }
            finally
            {
                ListeningStartupLog.Write($"{source} session={loopId} event=disposed");
                if (Volatile.Read(ref _generation) == gen && Volatile.Read(ref _activeLoopId) == loopId)
                {
                    Volatile.Write(ref _activeLoops, 0);
                    Volatile.Write(ref _activeLoopId, 0);
                }

                lock (_gate)
                {
                    if (ReferenceEquals(_cts, linked))
                        _cts = null;
                }

                try { linked.Dispose(); } catch { }
            }
        }

        private void WarmupClicks(
            int tempoBpm,
            int beatDurationPercent,
            float accentedVolume,
            float unaccentedVolume,
            double accentedPitchHz,
            double unaccentedPitchHz)
        {
            try
            {
                double msPerBeat = WaitingCountInLogic.MsPerBeat(tempoBpm);
                int durationMs = Math.Max(
                    20,
                    (int)Math.Round(
                        WaitingCountInLogic.ResolveClickDurationSeconds(beatDurationPercent, msPerBeat)
                        * 1000.0));
                _clicks.Warmup(
                    accentedPitchHz,
                    accentedVolume,
                    unaccentedPitchHz,
                    unaccentedVolume,
                    durationMs);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CountIn] warmup failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Wait until the monotonic clock reaches the absolute beat time.
        /// Uses coarse delay plus spin/yield finish — Task.Delay alone quantizes to ~15 ms on Windows.
        /// Remaining time is always recomputed from the Stopwatch (never chained from prior wakeups).
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
                    // Re-read remaining after each delay so OS timer jitter does not accumulate.
                    int delayMs = Math.Max(1, (int)Math.Floor(remaining - 12.0));
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
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
        /// Submit a prepared click immediately. The next beat is not scheduled from this return.
        /// </summary>
        private void TriggerClick(
            int generation,
            int loopId,
            bool accented,
            int durationMs,
            float volume,
            double frequencyHz,
            CancellationToken ct,
            MetronomeClickScheduleInfo schedule,
            Action? onFirstClickSounded)
        {
            if (ct.IsCancellationRequested
                || Volatile.Read(ref _generation) != generation
                || Volatile.Read(ref _activeLoopId) != loopId)
                return;

            try
            {
                _clicks.PlayClickAsync(accented, durationMs, volume, frequencyHz, ct, schedule)
                    .GetAwaiter()
                    .GetResult();
                if (onFirstClickSounded == null
                    || ct.IsCancellationRequested
                    || Volatile.Read(ref _generation) != generation
                    || Volatile.Read(ref _activeLoopId) != loopId)
                    return;
                if (Interlocked.Exchange(ref _firstSoundNotified, 1) != 0)
                    return;
                if (Volatile.Read(ref _generation) != generation)
                {
                    Interlocked.Exchange(ref _firstSoundNotified, 0);
                    return;
                }

                onFirstClickSounded();
            }
            catch (OperationCanceledException)
            {
                // Stopped.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CountIn] click failed: {ex.Message}");
            }
        }
    }
}
