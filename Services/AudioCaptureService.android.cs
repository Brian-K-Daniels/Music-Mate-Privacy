using musicmate.Utilities;
#if ANDROID
using Android;
using Android.Media;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using System.Diagnostics;

namespace musicmate.Services
{
    public class AudioCaptureService : IAudioCaptureService
    {
        public event Action? MaxBlocksReached;

        private readonly object _gate = new();
        private AudioRecord? _rec;
        private CancellationTokenSource? _cts;
        private Action<short[]>? _callback;
        private int _generation;
        private int _loopRunning;

        public int SampleRate { get; } = 44100;
        public int BufferSize { get; } = 1024;

        public bool IsCapturing
        {
            get
            {
                lock (_gate)
                    return _rec != null && Volatile.Read(ref _loopRunning) > 0;
            }
        }

        public async Task EnsurePermissionAsync()
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity is null)
                return;

            var granted = ContextCompat.CheckSelfPermission(activity, Manifest.Permission.RecordAudio)
                == (int)Android.Content.PM.Permission.Granted;
            if (!granted)
            {
                ActivityCompat.RequestPermissions(activity, new[] { Manifest.Permission.RecordAudio }, 1001);
                await Task.Delay(500).ConfigureAwait(false);
            }
        }

        public void StartCapture(Action<short[]> onBlock)
        {
            if (!TryStartCapture(onBlock, out var error))
                throw new InvalidOperationException(error ?? "AudioRecord failed to start.");
        }

        public bool TryStartCapture(Action<short[]> onBlock, out string? error)
        {
            error = null;
            ArgumentNullException.ThrowIfNull(onBlock);

            lock (_gate)
            {
                _callback = onBlock;
                StopCapture_NoLock(waitForLoop: true);

                Exception? lastEx = null;
                for (int attempt = 1; attempt <= 4; attempt++)
                {
                    try
                    {
                        var minBuf = AudioRecord.GetMinBufferSize(
                            SampleRate, ChannelIn.Mono, Android.Media.Encoding.Pcm16bit);
                        if (minBuf <= 0)
                        {
                            lastEx = new InvalidOperationException($"GetMinBufferSize returned {minBuf}");
                            Thread.Sleep(40 * attempt);
                            continue;
                        }

                        var frameBytes = BufferSize * 2;
                        var useBuf = Math.Max(minBuf, frameBytes * 4);

                        var rec = new AudioRecord(
                            AudioSource.Mic, SampleRate, ChannelIn.Mono,
                            Android.Media.Encoding.Pcm16bit, useBuf);

                        if (rec.State != State.Initialized)
                        {
                            try { rec.Release(); } catch { }
                            try { rec.Dispose(); } catch { }
                            lastEx = new InvalidOperationException($"AudioRecord state={rec.State}");
                            Thread.Sleep(50 * attempt);
                            continue;
                        }

                        rec.StartRecording();

                        _rec = rec;
                        _cts = new CancellationTokenSource();
                        int gen = Interlocked.Increment(ref _generation);
                        var localCts = _cts;
                        Volatile.Write(ref _loopRunning, 1);

                        Task.Run(() => CaptureLoop(rec, localCts.Token, gen));
                        return true;
                    }
                    catch (Exception ex)
                    {
                        lastEx = ex;
                        Debug.WriteLine($"[AudioCapture] Start attempt {attempt} failed: {ex.Message}");
                        Thread.Sleep(50 * attempt);
                    }
                }

                error = lastEx?.Message ?? "AudioRecord failed to start.";
                Debug.WriteLine($"[AudioCapture] StartCapture failed: {error}");
                return false;
            }
        }

        private void CaptureLoop(AudioRecord rec, CancellationToken token, int gen)
        {
            try
            {
                var buf = new short[BufferSize];
                int blockCount = 0;
                const int MaxBlocks = 3000;

                while (!token.IsCancellationRequested && Volatile.Read(ref _generation) == gen)
                {
                    int read;
                    try
                    {
                        read = rec.Read(buf, 0, buf.Length);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AudioCapture] Read error: {ex.Message}");
                        break;
                    }

                    if (read > 0)
                    {
                        var block = new short[read];
                        Array.Copy(buf, block, read);
                        try { _callback?.Invoke(block); }
                        catch (Exception cbEx)
                        {
                            Debug.WriteLine($"[AudioCapture] callback error: {cbEx.Message}");
                        }

                        blockCount++;
                        if (blockCount >= MaxBlocks)
                        {
                            Utils.Log($"[AudioCapture] MaxBlocks ({MaxBlocks}) reached, exiting capture loop.");
                            try { MaxBlocksReached?.Invoke(); } catch { }
                            break;
                        }
                    }
                    else if (read < 0)
                    {
                        Debug.WriteLine($"[AudioCapture] Read returned {read}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CaptureLoop error: {ex}");
            }
            finally
            {
                Volatile.Write(ref _loopRunning, 0);
            }
        }

        public void StopCapture()
        {
            lock (_gate)
                StopCapture_NoLock(waitForLoop: true);
        }

        private void StopCapture_NoLock(bool waitForLoop)
        {
            Interlocked.Increment(ref _generation);

            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
            }
            catch { }
            _cts = null;

            var rec = _rec;
            _rec = null;

            if (rec is not null)
            {
                try
                {
                    if (rec.RecordingState == RecordState.Recording)
                        rec.Stop();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"StopCapture Stop error: {ex.Message}");
                }

                try { rec.Release(); } catch { }
                try { rec.Dispose(); } catch { }
            }

            if (waitForLoop)
            {
                // Brief wait so the prior CaptureLoop exits before a new AudioRecord is created.
                for (int i = 0; i < 20 && Volatile.Read(ref _loopRunning) > 0; i++)
                    Thread.Sleep(10);
            }
        }
    }
}
#endif
