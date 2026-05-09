using System.Buffers.Binary;
using Plugin.Maui.Audio;

namespace musicmate.Services
{
    public sealed class AudioPlaybackService : IAudioPlaybackService
    {
        private readonly IAudioManager _audioManager;
        private CancellationTokenSource? _internalCts;
        private const int SampleRate = 44100;

        public AudioPlaybackService(IAudioManager audioManager)
        {
            _audioManager = audioManager;
        }

        public async Task PlayAsync(IEnumerable<double> freqs, double noteSeconds, double gapSeconds, float volume, CancellationToken ct)
        {
            // Cancel any previous internal token and create a new linked token
            try
            {
                _internalCts?.Cancel();
                _internalCts?.Dispose();
            }
            catch { }
            _internalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _internalCts.Token;

            try
            {
                foreach (var f in freqs)
                {
                    token.ThrowIfCancellationRequested();

                    await using var tone = BuildToneStream(f, noteSeconds, volume);
                    using var player = _audioManager.CreatePlayer(tone);
                    player.Play();

                    await Task.Delay(TimeSpan.FromSeconds(noteSeconds), token);

                    if (gapSeconds > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(gapSeconds), token);
                    }
                }
            }
            finally
            {
                try
                {
                    _internalCts?.Dispose();
                }
                catch { }
                _internalCts = null;
            }
        }

        private static Stream BuildToneStream(double freq, double durationSeconds, float volume)
        {
            int samples = (int)(SampleRate * durationSeconds);
            var pcm = new byte[44 + samples * 2];
            WriteWavHeader(pcm, samples);

            // Fade in/out over 10 ms to eliminate click transients at note onset and release
            int fadeSamples = Math.Min((int)(SampleRate * 0.010), samples / 4);

            for (int i = 0; i < samples; i++)
            {
                double t = (double)i / SampleRate;
                double envelope = 1.0;
                if (i < fadeSamples)
                    envelope = (double)i / fadeSamples;
                else if (i >= samples - fadeSamples)
                    envelope = (double)(samples - 1 - i) / fadeSamples;

                var sample = (short)(Math.Sin(2 * Math.PI * freq * t) * short.MaxValue * volume * envelope);
                BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(44 + i * 2), sample);
            }
            return new MemoryStream(pcm);
        }

        private static void WriteWavHeader(byte[] buffer, int samples)
        {
            int dataSize = samples * 2;
            int fileSize = 36 + dataSize;

            // RIFF
            buffer[0] = (byte)'R'; buffer[1] = (byte)'I'; buffer[2] = (byte)'F'; buffer[3] = (byte)'F';
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), fileSize);
            buffer[8] = (byte)'W'; buffer[9] = (byte)'A'; buffer[10] = (byte)'V'; buffer[11] = (byte)'E';

            // fmt chunk
            buffer[12] = (byte)'f'; buffer[13] = (byte)'m'; buffer[14] = (byte)'t'; buffer[15] = (byte)' ';
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(16), 16); // PCM header size
            BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(20), 1);  // PCM
            BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(22), 1);  // mono
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(24), SampleRate);
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(28), SampleRate * 2); // byte rate
            BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(32), 2);  // block align
            BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(34), 16); // bits/sample

            // data chunk
            buffer[36] = (byte)'d'; buffer[37] = (byte)'a'; buffer[38] = (byte)'t'; buffer[39] = (byte)'a';
            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(40), dataSize);
        }

        public void CancelPlayback()
        {
            try
            {
                _internalCts?.Cancel();
            }
            catch { }
        }
    }
}
