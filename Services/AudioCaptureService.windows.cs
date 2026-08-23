#if WINDOWS
using NAudio.Wave;
using System.Diagnostics;

namespace musicmate.Services
{
    public class AudioCaptureService : IAudioCaptureService
    {
        public event Action? MaxBlocksReached;
        private WaveInEvent? _waveIn;
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
                _callback = onBlock;
                StopCapture();
                _blockCount = 0;

                _waveIn = new WaveInEvent
                {
                    DeviceNumber = 0,
                    WaveFormat = new WaveFormat(SampleRate, 16, 1),
                    BufferMilliseconds = (int)(1000.0 * BufferSize / (double)SampleRate)
                };
                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.StartRecording();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
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

        public void StopCapture()
        {
            try
            {
                if (_waveIn is not null)
                {
                    _waveIn.DataAvailable -= OnDataAvailable;
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
