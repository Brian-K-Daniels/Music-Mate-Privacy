using System.Diagnostics;
using musicmate.Diagnostics;
using Plugin.Maui.Audio;
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
        private readonly Dictionary<ClickCacheKey, AudioTrack> _tracks = new();
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
            double accentHz = WaitingCountInSettings.ClampPitchHz(accentedPitchHz);
            float accentVol = WaitingCountInSettings.ClampVolume(accentedVolume);
            double plainHz = WaitingCountInSettings.ClampPitchHz(unaccentedPitchHz);
            float plainVol = WaitingCountInSettings.ClampVolume(unaccentedVolume);
#if ANDROID
            EnsureAndroidClicksReady(accentHz, accentVol, plainHz, plainVol, dur);
#else
            PrepareCached(accentHz, accentVol, dur);
            PrepareCached(plainHz, plainVol, dur);
#endif
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

            _ = accented;
            int epochAtStart = Volatile.Read(ref _playEpoch);

            double hz = WaitingCountInSettings.ClampPitchHz(frequencyHz);
            int dur = Math.Clamp(durationMs, 20, 2000);
            float vol = WaitingCountInSettings.ClampVolume(volume);
            var key = ClickCacheKey.From(hz, vol, dur);

            try
            {
                if (!MayPlay(epochAtStart, ct))
                    return Task.CompletedTask;

#if ANDROID
                PlayPreparedAndroid(key, hz, vol, dur, epochAtStart, ct, schedule);
#else
                PlayWindowsCached(key, hz, vol, dur, epochAtStart, ct, schedule);
#endif
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CountInClick] play failed: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        public void Stop()
        {
            Interlocked.Increment(ref _playEpoch);
            lock (_gate)
            {
#if ANDROID
                ReleaseTracks_NoLock();
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
                var cached = GetOrCreateCached_NoLock(key, hz, volume, durationMs);
#if !ANDROID
                EnsureWindowsPlayer_NoLock(cached);
#else
                _ = cached;
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
        /// <summary>
        /// One static track per click. SoundPool is not used: a pool created on the
        /// main looper can start the same sample again when play() is called off that
        /// looper, which is a second click that drifts against the beat.
        /// </summary>
        private void EnsureAndroidClicksReady(
            double accentHz,
            float accentVol,
            double plainHz,
            float plainVol,
            int durationMs)
        {
            int epoch = Volatile.Read(ref _playEpoch);
            bool pin = false;
            try
            {
                AndroidPlaybackRoute.Apply("count-in");
                pin = OperatingSystem.IsAndroidVersionAtLeast(23) && AndroidPlaybackRoute.HasPinnedOutput;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CountInClick] route failed: {ex.Message}");
            }

            PrimeTrack(ClickCacheKey.From(accentHz, accentVol, durationMs), accentHz, accentVol, durationMs, epoch, pin);
            PrimeTrack(ClickCacheKey.From(plainHz, plainVol, durationMs), plainHz, plainVol, durationMs, epoch, pin);
        }

        private void PrimeTrack(
            ClickCacheKey key,
            double hz,
            float volume,
            int durationMs,
            int epoch,
            bool pinOutput)
        {
            if (Volatile.Read(ref _playEpoch) != epoch)
                return;

            short[] pcm;
            lock (_gate)
            {
                if (Volatile.Read(ref _playEpoch) != epoch || _tracks.ContainsKey(key))
                    return;
                var cached = GetOrCreateCached_NoLock(key, hz, volume, durationMs);
                if (cached.Pcm.Length == 0)
                    return;
                pcm = PadWithSilence(cached.Pcm);
            }

            AudioTrack? track = CreateStaticTrack(pcm, pinOutput);
            if (track == null)
                return;

            lock (_gate)
            {
                if (Volatile.Read(ref _playEpoch) != epoch || _tracks.ContainsKey(key))
                {
                    ReleaseTrack(track);
                    return;
                }

                _tracks[key] = track;
            }
        }

        private static AudioTrack? CreateStaticTrack(short[] pcm, bool pinOutput)
        {
            AudioTrack? track = null;
            try
            {
                int bytes = pcm.Length * 2;
#pragma warning disable CS0618
                track = new AudioTrack(
                    Android.Media.Stream.Music,
                    MetronomeClickPcm.SampleRate,
                    ChannelOut.Mono,
                    Android.Media.Encoding.Pcm16bit,
                    bytes,
                    AudioTrackMode.Static);
#pragma warning restore CS0618
                if (track.State == AudioTrackState.Uninitialized)
                {
                    ReleaseTrack(track);
                    return null;
                }

                if (pinOutput && OperatingSystem.IsAndroidVersionAtLeast(23))
                    AndroidPlaybackRoute.ApplyTo(track);

                int written = track.Write(pcm, 0, pcm.Length);
                if (written < pcm.Length)
                {
                    ReleaseTrack(track);
                    return null;
                }

                track.SetVolume(1f);
                return track;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CountInClick] static track failed: {ex.Message}");
                if (track != null)
                    ReleaseTrack(track);
                return null;
            }
        }

        /// <summary>
        /// One Play() for this beat. Any other prepared track is paused first so
        /// accent and unaccent cannot sound together.
        /// </summary>
        private void PlayPreparedAndroid(
            ClickCacheKey key,
            double hz,
            float volume,
            int durationMs,
            int epochAtStart,
            CancellationToken ct,
            MetronomeClickScheduleInfo? schedule)
        {
            _ = (hz, volume, durationMs);
            if (!MayPlay(epochAtStart, ct))
                return;

            AudioTrack? track = null;
            var others = new List<AudioTrack>();
            lock (_gate)
            {
                if (!MayPlay(epochAtStart, ct))
                    return;
                if (!_tracks.TryGetValue(key, out track) || track == null)
                {
                    LogBeat(schedule, Stopwatch.GetTimestamp(), "unavailable", 0);
                    return;
                }

                foreach (var candidate in _tracks.Values)
                {
                    if (!ReferenceEquals(candidate, track))
                        others.Add(candidate);
                }
            }

            foreach (var other in others)
            {
                try { other.Pause(); } catch { }
            }

            long entered = Stopwatch.GetTimestamp();
            if (!ReplayOnce(track, out long playedAt, out bool reloaded))
                return;

            double prepMs = (playedAt - entered) * 1000.0 / Stopwatch.Frequency;
            LogBeat(schedule, playedAt, reloaded ? "AudioTrackReload" : "AudioTrack", prepMs);
        }

        /// <summary>
        /// The click is shorter than a beat, so a static track reaches the end and
        /// releases its buffer. Reloading that buffer on the beat blocks for a
        /// variable time and bunches the next click. Silence after the click keeps
        /// the track playing until the next beat, which only pauses and restarts it.
        /// </summary>
        private static short[] PadWithSilence(short[] click)
        {
            // Longer than the slowest beat (30 BPM = 2000 ms).
            int samples = MetronomeClickPcm.SampleRate * 32 / 10;
            if (click.Length >= samples)
                return click;

            var padded = new short[samples];
            Buffer.BlockCopy(click, 0, padded, 0, click.Length * sizeof(short));
            return padded;
        }

        private static bool ReplayOnce(AudioTrack track, out long playedAt, out bool reloaded)
        {
            reloaded = false;
            try
            {
                try { track.Pause(); } catch { }
                track.SetPlaybackHeadPosition(0);
                playedAt = Stopwatch.GetTimestamp();
                track.Play();
                return true;
            }
            catch (Exception first)
            {
                try
                {
                    reloaded = true;
                    try { track.Stop(); } catch { }
                    track.ReloadStaticData();
                    track.SetPlaybackHeadPosition(0);
                    playedAt = Stopwatch.GetTimestamp();
                    track.Play();
                    return true;
                }
                catch (Exception ex)
                {
                    playedAt = Stopwatch.GetTimestamp();
                    Debug.WriteLine($"[CountInClick] replay failed: {first.Message}; {ex.Message}");
                    return false;
                }
            }
        }

        private void ReleaseTracks_NoLock()
        {
            foreach (var track in _tracks.Values)
                ReleaseTrack(track);
            _tracks.Clear();
        }

        private static void ReleaseTrack(AudioTrack track)
        {
            try { track.Stop(); } catch { }
            try { track.Release(); } catch { }
            try { track.Dispose(); } catch { }
        }
#else
        private void EnsureWindowsPlayer_NoLock(CachedClickSound cached)
        {
            if (cached.Player != null)
                return;

            cached.WavStream.Position = 0;
            cached.Player = _audioManager.CreatePlayer(cached.WavStream);
            if (cached.Player != null)
                cached.Player.Volume = 1f;
        }

        private void PlayWindowsCached(
            ClickCacheKey key,
            double hz,
            float volume,
            int durationMs,
            int epochAtStart,
            CancellationToken ct,
            MetronomeClickScheduleInfo? schedule)
        {
            IAudioPlayer? player;
            lock (_gate)
            {
                if (!MayPlay(epochAtStart, ct))
                    return;

                var cached = GetOrCreateCached_NoLock(key, hz, volume, durationMs);
                EnsureWindowsPlayer_NoLock(cached);
                player = cached.Player;
                if (player == null || !MayPlay(epochAtStart, ct))
                    return;
            }

            long entered = Stopwatch.GetTimestamp();
            long playedAt;
            try
            {
                try { player.Pause(); } catch { }
                try { player.Seek(0); } catch { }
                player.Volume = 1f;
                playedAt = Stopwatch.GetTimestamp();
                player.Play();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CountInClick] play failed: {ex.Message}");
                return;
            }

            double prepMs = (playedAt - entered) * 1000.0 / Stopwatch.Frequency;
            LogBeat(schedule, playedAt, "WindowsPlayer", prepMs);
        }
#endif

        private static void LogBeat(MetronomeClickScheduleInfo? schedule, long playedAt, string path, double prepMs)
        {
            if (schedule is not { } info || info.GridOriginTimestamp == 0)
                return;

            ListeningStartupLog.Write(
                info.FormatAudibleBeat(info.ElapsedMsAt(playedAt), path) + $" prep={prepMs:F1}");
        }

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
