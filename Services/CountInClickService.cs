using System.Diagnostics;
using Plugin.Maui.Audio;
#if DEBUG
using musicmate.Diagnostics;
#endif
#if ANDROID
using Android.Media;
#endif

namespace musicmate.Services
{
    /// <summary>
    /// Metronome clicks via cached PCM/WAV buffers and a dedicated playback path.
    /// Does not share <see cref="AudioPlaybackService"/>'s lock/cancel pipeline.
    /// </summary>
    public sealed class CountInClickService : ICountInClickService
    {
        private readonly IAudioManager _audioManager;
        private readonly object _gate = new();
        private readonly Dictionary<ClickCacheKey, CachedClickSound> _cache = new();
        private int _playEpoch;

#if ANDROID
        private SoundPool? _soundPool;
        private readonly Dictionary<ClickCacheKey, int> _soundIds = new();
#endif

        public CountInClickService(IAudioManager audioManager)
        {
            _audioManager = audioManager;
        }

        public void Warmup(
            double accentedPitchHz,
            float accentedVolume,
            double unaccentedPitchHz,
            float unaccentedVolume,
            int durationMs)
        {
            int dur = Math.Clamp(durationMs, 20, 2000);
            PrepareCached(
                WaitingCountInSettings.ClampPitchHz(accentedPitchHz),
                WaitingCountInSettings.ClampVolume(accentedVolume),
                dur);
            PrepareCached(
                WaitingCountInSettings.ClampPitchHz(unaccentedPitchHz),
                WaitingCountInSettings.ClampVolume(unaccentedVolume),
                dur);
        }

        public Task PlayClickAsync(
            bool accented,
            int durationMs,
            float volume,
            double frequencyHz,
            CancellationToken ct,
            MetronomeClickScheduleInfo? schedule = null)
        {
            if (ct.IsCancellationRequested)
                return Task.CompletedTask;

            int epochAtStart = Volatile.Read(ref _playEpoch);
            long triggerTick = Stopwatch.GetTimestamp();

            double hz = WaitingCountInSettings.ClampPitchHz(frequencyHz);
            int dur = Math.Clamp(durationMs, 20, 2000);
            float vol = WaitingCountInSettings.ClampVolume(volume);
            var key = ClickCacheKey.From(hz, vol, dur);

            try
            {
                if (!MayPlay(epochAtStart, ct))
                    return Task.CompletedTask;

#if ANDROID
                PlayAndroidCached(key, hz, vol, dur, epochAtStart, ct);
#else
                PlayWindowsCached(key, hz, vol, dur, epochAtStart, ct);
#endif
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CountInClick] play failed: {ex.Message}");
            }

#if DEBUG
            if (schedule.HasValue)
            {
                var s = schedule.Value;
                double audioLatencyMs = (triggerTick - s.SchedulerTick) * 1000.0 / Stopwatch.Frequency;
                MetronomeBeatDiagnostics.LogAudioTrigger(
                    s.MeasureNumber,
                    s.BeatNumber,
                    s.IntendedMs,
                    s.SchedulerMs,
                    audioLatencyMs,
                    accented);
            }
#endif

            return Task.CompletedTask;
        }

        public void Stop()
        {
            Interlocked.Increment(ref _playEpoch);
            lock (_gate)
            {
#if ANDROID
                ReleaseSoundPool_NoLock();
#endif
                foreach (var cached in _cache.Values)
                    cached.DisposePlayer();
                // Keep PCM/WAV buffers so the next Count-In does not re-decode from scratch.
            }
        }

        private void PrepareCached(double hz, float volume, int durationMs)
        {
            var key = ClickCacheKey.From(hz, volume, durationMs);
            lock (_gate)
            {
                _ = GetOrCreateCached_NoLock(key, hz, volume, durationMs);
#if ANDROID
                _ = GetOrLoadSoundId_NoLock(key, _cache[key].Pcm);
#endif
            }
        }

        private bool MayPlay(int epochAtStart, CancellationToken ct)
            => !ct.IsCancellationRequested
               && Volatile.Read(ref _playEpoch) == epochAtStart;

        private CachedClickSound GetOrCreateCached_NoLock(ClickCacheKey key, double hz, float volume, int durationMs)
        {
            if (_cache.TryGetValue(key, out var existing))
                return existing;

            double seconds = durationMs / 1000.0;
            short[] pcm = MetronomeClickPcm.Build(hz, seconds, volume);
            var wav = BuildWavStream(pcm);
            var cached = new CachedClickSound(pcm, wav);
            _cache[key] = cached;
            return cached;
        }

#if ANDROID
        private void PlayAndroidCached(
            ClickCacheKey key,
            double hz,
            float volume,
            int durationMs,
            int epochAtStart,
            CancellationToken ct)
        {
            int soundId;
            SoundPool? pool;
            lock (_gate)
            {
                if (!MayPlay(epochAtStart, ct))
                    return;

                var cached = GetOrCreateCached_NoLock(key, hz, volume, durationMs);
                soundId = GetOrLoadSoundId_NoLock(key, cached.Pcm);
                pool = _soundPool;
            }

            if (soundId == 0 || pool == null)
                return;
            if (!MayPlay(epochAtStart, ct))
                return;

            // Non-blocking — suitable for 150+ BPM metronome clicks.
            pool.Play(soundId, volume, volume, 1, 0, 1f);
        }

        private void EnsureSoundPool_NoLock()
        {
            if (_soundPool != null)
                return;

            var attrsBuilder = new AudioAttributes.Builder();
            attrsBuilder.SetUsage(AudioUsageKind.Media);
            attrsBuilder.SetContentType(AudioContentType.Music);
            var attrs = attrsBuilder.Build()
                ?? throw new InvalidOperationException("AudioAttributes.Builder.Build returned null.");

            var poolBuilder = new SoundPool.Builder();
            poolBuilder.SetMaxStreams(6);
            poolBuilder.SetAudioAttributes(attrs);
            _soundPool = poolBuilder.Build();
        }

        private int GetOrLoadSoundId_NoLock(ClickCacheKey key, short[] pcm)
        {
            if (_soundIds.TryGetValue(key, out int existing) && existing != 0)
                return existing;

            EnsureSoundPool_NoLock();
            byte[] wav = BuildWavBytes(pcm);
            string cacheDir = Path.Combine(FileSystem.CacheDirectory, "metronome_clicks");
            Directory.CreateDirectory(cacheDir);
            string path = Path.Combine(cacheDir, $"{key.HzMilli}_{key.VolumeMilli}_{key.DurationMs}.wav");
            if (!File.Exists(path))
                File.WriteAllBytes(path, wav);
            int soundId = _soundPool!.Load(path, 1);
            if (soundId != 0)
                _soundIds[key] = soundId;
            return soundId;
        }

        private void ReleaseSoundPool_NoLock()
        {
            _soundIds.Clear();
            if (_soundPool == null)
                return;
            try { _soundPool.Release(); } catch { }
            try { _soundPool.Dispose(); } catch { }
            _soundPool = null;
        }
#else
        private void PlayWindowsCached(
            ClickCacheKey key,
            double hz,
            float volume,
            int durationMs,
            int epochAtStart,
            CancellationToken ct)
        {
            lock (_gate)
            {
                if (!MayPlay(epochAtStart, ct))
                    return;

                var cached = GetOrCreateCached_NoLock(key, hz, volume, durationMs);
                cached.WavStream.Position = 0;
                cached.DisposePlayer();
                if (!MayPlay(epochAtStart, ct))
                    return;

                cached.Player = _audioManager.CreatePlayer(cached.WavStream);
                if (cached.Player == null)
                    return;

                cached.Player.Volume = 1f;
                cached.Player.Play();
            }
        }
#endif

        private static byte[] BuildWavBytes(short[] pcm)
        {
            var wav = new byte[44 + pcm.Length * 2];
            WriteWavHeader(wav, pcm.Length);
            for (int i = 0; i < pcm.Length; i++)
                System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(
                    wav.AsSpan(44 + i * 2), pcm[i]);
            return wav;
        }

        private static MemoryStream BuildWavStream(short[] pcm)
            => new MemoryStream(BuildWavBytes(pcm), writable: false);

        private static void WriteWavHeader(byte[] buffer, int samples)
        {
            int dataSize = samples * 2;
            int fileSize = 36 + dataSize;
            buffer[0] = (byte)'R'; buffer[1] = (byte)'I'; buffer[2] = (byte)'F'; buffer[3] = (byte)'F';
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), fileSize);
            buffer[8] = (byte)'W'; buffer[9] = (byte)'A'; buffer[10] = (byte)'V'; buffer[11] = (byte)'E';
            buffer[12] = (byte)'f'; buffer[13] = (byte)'m'; buffer[14] = (byte)'t'; buffer[15] = (byte)' ';
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(16), 16);
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(20), 1);
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(22), 1);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(24), MetronomeClickPcm.SampleRate);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(28), MetronomeClickPcm.SampleRate * 2);
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(32), 2);
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(34), 16);
            buffer[36] = (byte)'d'; buffer[37] = (byte)'a'; buffer[38] = (byte)'t'; buffer[39] = (byte)'a';
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(40), dataSize);
        }

        private readonly record struct ClickCacheKey(int HzMilli, int VolumeMilli, int DurationMs)
        {
            public static ClickCacheKey From(double hz, float volume, int durationMs)
                => new(
                    (int)Math.Round(hz * 10.0),
                    (int)Math.Round(volume * 1000.0f),
                    durationMs);
        }

        private sealed class CachedClickSound : IDisposable
        {
            public short[] Pcm { get; }
            public MemoryStream WavStream { get; }
            public IAudioPlayer? Player { get; set; }

            public CachedClickSound(short[] pcm, MemoryStream wavStream)
            {
                Pcm = pcm;
                WavStream = wavStream;
            }

            public void DisposePlayer()
            {
                if (Player == null)
                    return;
                try { Player.Stop(); } catch { }
                try { Player.Dispose(); } catch { }
                Player = null;
            }

            public void Dispose()
            {
                DisposePlayer();
                try { WavStream.Dispose(); } catch { }
            }
        }
    }
}
