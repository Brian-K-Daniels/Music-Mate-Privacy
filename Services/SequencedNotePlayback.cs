using System.Buffers.Binary;
using Plugin.Maui.Audio;
#if ANDROID
using Android.Media;
#endif

namespace musicmate.Services;

/// <summary>
/// One rendered buffer for a Play sequence. Starting it once keeps every later
/// note on the sample clock instead of rebuilding a player for each note.
/// </summary>
internal sealed class SequencedNotePlayback : IDisposable
{
    private readonly object _gate = new();
    private readonly MemoryStream _wav;
    private readonly IAudioPlayer? _player;
#if ANDROID
    private readonly AudioTrack? _track;
#endif
    private bool _disposed;
    private bool _started;

    private SequencedNotePlayback(MemoryStream wav, IAudioPlayer? player)
    {
        _wav = wav;
        _player = player;
#if ANDROID
        _track = null;
#endif
    }

#if ANDROID
    private SequencedNotePlayback(MemoryStream wav, IAudioPlayer? player, AudioTrack? track)
    {
        _wav = wav;
        _player = player;
        _track = track;
    }
#endif

    public static SequencedNotePlayback Prepare(
        IAudioManager audio,
        IReadOnlyList<PlaybackSlot> slots,
        float volume)
    {
        short[] pcm = PlaybackOnsetSchedule.RenderPcm(slots, volume, AudioTonePcm.SampleRate);
        var wav = CreateWav(pcm);

#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(23))
            AndroidPlaybackRoute.Apply("sequence-playback");

        AudioTrack? track = TryCreateStaticTrack(pcm);
        if (track != null)
            return new SequencedNotePlayback(wav, player: null, track);
#endif
        wav.Position = 0;
        IAudioPlayer? media = null;
        try
        {
            media = audio.CreatePlayer(wav);
            if (media != null)
                media.Volume = 1f;
        }
        catch
        {
            try { media?.Dispose(); } catch { }
            media = null;
        }

        return new SequencedNotePlayback(wav, media);
    }

    public void Play()
    {
        lock (_gate)
        {
            if (_disposed || _started)
                return;
            _started = true;
        }

#if ANDROID
        if (_track != null)
        {
            try
            {
                _track.SetPlaybackHeadPosition(0);
                _track.Play();
                return;
            }
            catch
            {
                // MediaPlayer below plays the same buffer.
            }
        }
#endif
        try
        {
            if (_player == null)
                return;
            try { _player.Seek(0); } catch { }
            _player.Volume = 1f;
            _player.Play();
        }
        catch
        {
            // The visual clock still runs; a failed device must not throw into Play.
        }
    }

    public void Pause()
    {
#if ANDROID
        try { _track?.Pause(); } catch { }
#endif
        try { _player?.Pause(); } catch { }
    }

    public void Resume()
    {
#if ANDROID
        try { _track?.Play(); } catch { }
#endif
        try { _player?.Play(); } catch { }
    }

    public void Stop()
    {
#if ANDROID
        try { _track?.Pause(); } catch { }
        try { _track?.SetVolume(0f); } catch { }
#endif
        try { _player?.Pause(); } catch { }
        try
        {
            if (_player != null)
                _player.Volume = 0f;
        }
        catch { }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        Stop();
#if ANDROID
        try { _track?.Stop(); } catch { }
        try { _track?.Release(); } catch { }
        try { _track?.Dispose(); } catch { }
#endif
        try { _player?.Stop(); } catch { }
        try { _player?.Dispose(); } catch { }
        try { _wav.Dispose(); } catch { }
    }

#if ANDROID
    private static AudioTrack? TryCreateStaticTrack(short[] pcm)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(23) || pcm.Length == 0)
            return null;

        AudioTrack? track = null;
        try
        {
            int bytes = pcm.Length * 2;
            track = OperatingSystem.IsAndroidVersionAtLeast(26)
                ? CreateStaticTrackApi26(bytes)
                : CreateStaticTrackLegacy(bytes);
            if (track.State == AudioTrackState.Uninitialized)
            {
                Release(track);
                return null;
            }

            if (OperatingSystem.IsAndroidVersionAtLeast(23))
                AndroidPlaybackRoute.ApplyTo(track);

            int written = track.Write(pcm, 0, pcm.Length);
            if (written < pcm.Length)
            {
                Release(track);
                return null;
            }

            track.SetVolume(1f);
            return track;
        }
        catch
        {
            if (track != null)
                Release(track);
            return null;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android26.0")]
    private static AudioTrack CreateStaticTrackApi26(int bytes)
    {
        var attrsBuilder = new AudioAttributes.Builder();
        attrsBuilder.SetUsage(AudioUsageKind.Media);
        attrsBuilder.SetContentType(AudioContentType.Music);
        var attrs = attrsBuilder.Build()
            ?? throw new InvalidOperationException("AudioAttributes.Builder.Build returned null.");

        var formatBuilder = new AudioFormat.Builder();
        formatBuilder.SetSampleRate(AudioTonePcm.SampleRate);
        formatBuilder.SetEncoding(Android.Media.Encoding.Pcm16bit);
        formatBuilder.SetChannelMask(ChannelOut.Mono);
        var format = formatBuilder.Build()
            ?? throw new InvalidOperationException("AudioFormat.Builder.Build returned null.");

        var trackBuilder = new AudioTrack.Builder();
        trackBuilder.SetAudioAttributes(attrs);
        trackBuilder.SetAudioFormat(format);
        trackBuilder.SetBufferSizeInBytes(Math.Max(bytes, 2));
        trackBuilder.SetTransferMode(AudioTrackMode.Static);
        return trackBuilder.Build()
            ?? throw new InvalidOperationException("AudioTrack.Builder.Build returned null.");
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("android26.0")]
    private static AudioTrack CreateStaticTrackLegacy(int bytes)
    {
#pragma warning disable CS0618
        return new AudioTrack(
            Android.Media.Stream.Music,
            AudioTonePcm.SampleRate,
            ChannelOut.Mono,
            Android.Media.Encoding.Pcm16bit,
            Math.Max(bytes, 2),
            AudioTrackMode.Static);
#pragma warning restore CS0618
    }

    private static void Release(AudioTrack track)
    {
        try { track.Stop(); } catch { }
        try { track.Release(); } catch { }
        try { track.Dispose(); } catch { }
    }
#endif

    private static MemoryStream CreateWav(short[] pcm)
    {
        int dataSize = pcm.Length * 2;
        var wav = new byte[44 + dataSize];
        wav[0] = (byte)'R'; wav[1] = (byte)'I'; wav[2] = (byte)'F'; wav[3] = (byte)'F';
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), 36 + dataSize);
        wav[8] = (byte)'W'; wav[9] = (byte)'A'; wav[10] = (byte)'V'; wav[11] = (byte)'E';
        wav[12] = (byte)'f'; wav[13] = (byte)'m'; wav[14] = (byte)'t'; wav[15] = (byte)' ';
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), AudioTonePcm.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), AudioTonePcm.SampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), 16);
        wav[36] = (byte)'d'; wav[37] = (byte)'a'; wav[38] = (byte)'t'; wav[39] = (byte)'a';
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), dataSize);
        for (int i = 0; i < pcm.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(44 + i * 2), pcm[i]);
        return new MemoryStream(wav, writable: false);
    }
}
