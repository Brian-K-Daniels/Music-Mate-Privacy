#if WINDOWS
using NAudio.Wave;
using System.Diagnostics;
using musicmate.Diagnostics;

namespace musicmate.Services
{
    public class AudioCaptureService : IAudioCaptureService
    {
        public event Action? MaxBlocksReached;
        public event Action? CaptureRouteLost;
        private WaveInEvent? _waveIn;
        private int _intentionalStop;
        private Action<short[]>? _callback;
        private int _blockCount;
        private const int MaxBlocks = 3000;

        public int SampleRate { get; } = 44100;
        public int BufferSize { get; } = 1024;
        public bool IsCapturing => _waveIn != null;

        public Task EnsurePermissionAsync()
        {
            // WASAPI/MME typically doesn't need runtime permission on Windows
            return Task.CompletedTask;
        }

        public void StartCapture(Action<short[]> onBlock)
        {
            if (!TryStartCapture(onBlock, out var error))
                throw new InvalidOperationException(error ?? "WaveIn failed to start.");
        }

        public bool TryStartCapture(Action<short[]> onBlock, out string? error)
        {
            error = null;
            try
            {
                ListeningStartupLog.Write("AUDIO: StartListening requested");
                _callback = onBlock;
                StopCapture();
                _blockCount = 0;

                _waveIn = new WaveInEvent
                {
                    DeviceNumber = 0,
                    WaveFormat = new WaveFormat(SampleRate, 16, 1),
                    BufferMilliseconds = (int)(1000.0 * BufferSize / (double)SampleRate)
                };
                ListeningStartupLog.Write("AUDIO: recorder created");
                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;
                ListeningStartupLog.Write("AUDIO: recorder.StartRecording called");
                Interlocked.Exchange(ref _intentionalStop, 0);
                _waveIn.StartRecording();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                ListeningStartupLog.Exception("AUDIO", ex);
                Debug.WriteLine($"StartCapture error: {ex}");
                return false;
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            try
            {
                // NAudio delivers byte[] PCM 16
                var samples = e.BytesRecorded / 2;
                var outBuf = new short[samples];
                Buffer.BlockCopy(e.Buffer, 0, outBuf, 0, e.BytesRecorded);
                _callback?.Invoke(outBuf);

                _blockCount++;
                if (_blockCount >= MaxBlocks)
                {
                    _blockCount = 0;
                    StopCapture();
                    MaxBlocksReached?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DataAvailable error: {ex}");
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (Volatile.Read(ref _intentionalStop) != 0)
                return;
            if (!ReferenceEquals(sender, _waveIn))
                return;

            Debug.WriteLine($"[AudioCapture] recording stopped: {e.Exception?.Message ?? "device change"}");
            _waveIn = null;
            try { CaptureRouteLost?.Invoke(); } catch { }
        }

        public void StopCapture()
        {
            ListeningStartupLog.Write($"AUDIO: StopCapture caller={ListeningStartupLog.Caller()}");
            Interlocked.Exchange(ref _intentionalStop, 1);
            try
            {
                if (_waveIn is not null)
                {
                    _waveIn.DataAvailable -= OnDataAvailable;
                    _waveIn.RecordingStopped -= OnRecordingStopped;
                    _waveIn.StopRecording();
                    _waveIn.Dispose();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"StopCapture error: {ex}");
            }
            finally
            {
                _waveIn = null;
            }
        }
    }
}
#endif
