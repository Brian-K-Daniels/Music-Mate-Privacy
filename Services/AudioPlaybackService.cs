using System.Buffers.Binary;
using System.Diagnostics;
using Plugin.Maui.Audio;
#if ANDROID
using Android.Media;
#endif

namespace musicmate.Services
{
    public sealed class AudioPlaybackService : IAudioPlaybackService
    {
        private readonly IAudioManager _audioManager;
        private CancellationTokenSource? _internalCts;
        private readonly object _gate = new();
        private const int SampleRate = 44100;
        /// <summary>Android MediaPlayer is unreliable below ~20 ms of PCM.</summary>
        private const double MinToneSeconds = 0.020;
        /// <summary>Short tones use AudioTrack on Android — MediaPlayer often stays silent for metronome clicks.</summary>
        private const double AndroidAudioTrackMaxSeconds = 0.20;

#if ANDROID
        private AudioTrack? _activeTrack;
#endif

        public AudioPlaybackService(IAudioManager audioManager)
        {
            _audioManager = audioManager;
        }

        public async Task PlayAsync(IEnumerable<double> freqs, double noteSeconds, double gapSeconds, float volume, CancellationToken ct)
        {
            double duration = Math.Max(MinToneSeconds, noteSeconds);
            float gain = Math.Clamp(volume, 0f, 1f);

            CancellationTokenSource linked;
            lock (_gate)
            {
                try
                {
                    _internalCts?.Cancel();
                    _internalCts?.Dispose();
                }
                catch { }

                StopActiveAndroidTrack_NoLock();

                linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _internalCts = linked;
            }

            var token = linked.Token;

            try
            {
                foreach (var f in freqs)
                {
                    token.ThrowIfCancellationRequested();
                    if (f <= 0 || double.IsNaN(f) || double.IsInfinity(f))
                        continue;

                    await PlayOneToneAsync(f, duration, gain, token).ConfigureAwait(false);

                    if (gapSeconds > 0)
                        await Task.Delay(TimeSpan.FromSeconds(gapSeconds), token).ConfigureAwait(false);
                }
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_internalCts, linked))
                        _internalCts = null;
                }

                try { linked.Dispose(); } catch { }
            }
        }

        private async Task PlayOneToneAsync(double freq, double durationSeconds, float volume, CancellationToken ct)
        {
#if ANDROID
            if (durationSeconds <= AndroidAudioTrackMaxSeconds)
            {
                await PlayOneToneAudioTrackAsync(freq, durationSeconds, volume, ct).ConfigureAwait(false);
                return;
            }
#endif
            await PlayOneToneMediaPlayerAsync(freq, durationSeconds, volume, ct).ConfigureAwait(false);
        }

        private async Task PlayOneToneMediaPlayerAsync(double freq, double durationSeconds, float volume, CancellationToken ct)
        {
            var tone = BuildToneWavStream(freq, durationSeconds, volume);
            IAudioPlayer? player = null;
            try
            {
                tone.Position = 0;
                player = _audioManager.CreatePlayer(tone);
                if (player == null)
                    throw new InvalidOperationException("CreatePlayer returned null.");

                player.Volume = 1f;
                player.Play();

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(durationSeconds), ct).ConfigureAwait(false);
                }
                finally
                {
                    try { player.Stop(); } catch { }
                }
            }
            finally
            {
                try { player?.Dispose(); } catch { }
                try { await tone.DisposeAsync().ConfigureAwait(false); } catch { try { tone.Dispose(); } catch { } }
            }
        }

#if ANDROID
        private async Task PlayOneToneAudioTrackAsync(double freq, double durationSeconds, float volume, CancellationToken ct)
        {
            short[] pcm = BuildPcm16(freq, durationSeconds, volume);
            int byteCount = pcm.Length * 2;
            int minBuf = AudioTrack.GetMinBufferSize(
                SampleRate, ChannelOut.Mono, Android.Media.Encoding.Pcm16bit);
            if (minBuf <= 0)
                minBuf = byteCount;

            AudioTrack? track = null;
            try
            {
                if (!OperatingSystem.IsAndroidVersionAtLeast(23))
                {
                    await PlayOneToneMediaPlayerAsync(freq, durationSeconds, volume, ct).ConfigureAwait(false);
                    return;
                }

                var attrsBuilder = new AudioAttributes.Builder();
                attrsBuilder.SetUsage(AudioUsageKind.Media);
                attrsBuilder.SetContentType(AudioContentType.Music);
                var attrs = attrsBuilder.Build()
                    ?? throw new InvalidOperationException("AudioAttributes.Builder.Build returned null.");

                var formatBuilder = new AudioFormat.Builder();
                formatBuilder.SetSampleRate(SampleRate);
                formatBuilder.SetEncoding(Android.Media.Encoding.Pcm16bit);
                formatBuilder.SetChannelMask(ChannelOut.Mono);
                var format = formatBuilder.Build()
                    ?? throw new InvalidOperationException("AudioFormat.Builder.Build returned null.");

                // STREAM: ready to Play immediately. STATIC starts as NoStaticData until Write —
                // we previously treated that as failure and never wrote, so every click was silent.
                var trackBuilder = new AudioTrack.Builder();
                trackBuilder.SetAudioAttributes(attrs);
                trackBuilder.SetAudioFormat(format);
                trackBuilder.SetBufferSizeInBytes(Math.Max(minBuf * 2, byteCount));
                trackBuilder.SetTransferMode(AudioTrackMode.Stream);
                track = trackBuilder.Build()
                    ?? throw new InvalidOperationException("AudioTrack.Builder.Build returned null.");

                if (track.State == AudioTrackState.Uninitialized)
                    throw new InvalidOperationException($"AudioTrack uninitialized (state={track.State}).");

                lock (_gate)
                {
                    StopActiveAndroidTrack_NoLock();
                    _activeTrack = track;
                }

                // PCM samples already include gain; keep track gain at unity.
                track.SetVolume(1f);
                track.Play();

                int offset = 0;
                while (offset < pcm.Length && !ct.IsCancellationRequested)
                {
                    int written = track.Write(pcm, offset, pcm.Length - offset);
                    if (written < 0)
                        throw new InvalidOperationException($"AudioTrack.Write failed ({written}).");
                    if (written == 0)
                        break;
                    offset += written;
                }

                // Hold for the tone length so the buffer can drain.
                await Task.Delay(TimeSpan.FromSeconds(durationSeconds), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AudioPlayback] AudioTrack click failed: {ex.Message}");
                try
                {
                    await PlayOneToneMediaPlayerAsync(freq, durationSeconds, volume, ct).ConfigureAwait(false);
                }
                catch (Exception mpEx)
                {
                    Debug.WriteLine($"[AudioPlayback] MediaPlayer fallback failed: {mpEx.Message}");
                }
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_activeTrack, track))
                        _activeTrack = null;
                }

                try { track?.Pause(); } catch { }
                try { track?.Stop(); } catch { }
                try { track?.Release(); } catch { }
                try { track?.Dispose(); } catch { }
            }
        }

        private void StopActiveAndroidTrack_NoLock()
        {
            var track = _activeTrack;
            _activeTrack = null;
            if (track == null)
                return;
            try { track.Pause(); } catch { }
            try { track.Stop(); } catch { }
            try { track.Release(); } catch { }
            try { track.Dispose(); } catch { }
        }
#else
        private void StopActiveAndroidTrack_NoLock() { }
#endif

        private static MemoryStream BuildToneWavStream(double freq, double durationSeconds, float volume)
        {
            short[] pcm = BuildPcm16(freq, durationSeconds, volume);
            var wav = new byte[44 + pcm.Length * 2];
            WriteWavHeader(wav, pcm.Length);
            for (int i = 0; i < pcm.Length; i++)
                BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(44 + i * 2), pcm[i]);
            return new MemoryStream(wav, writable: false);
        }

        private static short[] BuildPcm16(double freq, double durationSeconds, float volume)
        {
            int samples = Math.Max(1, (int)(SampleRate * durationSeconds));
            var pcm = new short[samples];

            // Fade in/out over up to 10 ms (or 1/4 of the tone) to avoid clicks.
            int fadeSamples = Math.Min((int)(SampleRate * 0.010), Math.Max(1, samples / 4));

            for (int i = 0; i < samples; i++)
            {
                double t = (double)i / SampleRate;
                double envelope = 1.0;
                if (i < fadeSamples)
                    envelope = (double)i / fadeSamples;
                else if (i >= samples - fadeSamples)
                    envelope = (double)(samples - 1 - i) / fadeSamples;

                pcm[i] = (short)(Math.Sin(2 * Math.PI * freq * t) * short.MaxValue * volume * envelope);
            }

            return pcm;
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
            lock (_gate)
            {
                try { _internalCts?.Cancel(); } catch { }
                StopActiveAndroidTrack_NoLock();
            }
        }
    }
}
