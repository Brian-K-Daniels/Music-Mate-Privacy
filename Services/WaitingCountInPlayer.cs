using System.Diagnostics;

namespace musicmate.Services
{
    /// <summary>
    /// Schedules metronome-like count-in clicks until cancelled. Single active loop at a time.
    /// Uses <see cref="ICountInClickService"/> so Android can click via ToneGenerator while the mic stays open.
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
            Func<CancellationToken, Task>? beforeClickAsync = null,
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
                double nextBeatStartMs = 0;
                long beatIndex = 0;

                while (!ct.IsCancellationRequested && Volatile.Read(ref _generation) == gen)
                {
                    int tempo = getTempoBpm?.Invoke() ?? tempoBpm;
                    double msPerBeat = WaitingCountInLogic.MsPerBeat(tempo);
                    var click = WaitingCountInLogic.BuildClick(
                        beatIndex,
                        beatsPerMeasure,
                        accentedVolume,
                        unaccentedVolume,
                        accentedPitchHz,
                        unaccentedPitchHz,
                        beatDurationPercent,
                        tempo);

                    double waitMs = nextBeatStartMs - clock.Elapsed.TotalMilliseconds;
                    if (waitMs > 1)
                        await Task.Delay(TimeSpan.FromMilliseconds(waitMs), ct).ConfigureAwait(false);

                    if (ct.IsCancellationRequested || Volatile.Read(ref _generation) != gen)
                        break;

                    try
                    {
                        if (beforeClickAsync != null)
                            await beforeClickAsync(ct).ConfigureAwait(false);

                        if (ct.IsCancellationRequested || Volatile.Read(ref _generation) != gen)
                            break;

                        int durationMs = Math.Max(20, (int)Math.Round(click.DurationSeconds * 1000.0));
                        await _clicks.PlayClickAsync(
                            click.IsAccented,
                            durationMs,
                            click.Volume,
                            click.FrequencyHz,
                            ct).ConfigureAwait(false);
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
                    finally
                    {
                        if (afterClickAsync != null
                            && Volatile.Read(ref _generation) == gen
                            && !ct.IsCancellationRequested)
                        {
                            try { await afterClickAsync(ct).ConfigureAwait(false); }
                            catch (OperationCanceledException) { }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[CountIn] afterClick failed: {ex.Message}");
                            }
                        }
                    }

                    nextBeatStartMs += msPerBeat;
                    double nowMs = clock.Elapsed.TotalMilliseconds;
                    if (nowMs - nextBeatStartMs > msPerBeat)
                        nextBeatStartMs = nowMs;

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
    }
}
