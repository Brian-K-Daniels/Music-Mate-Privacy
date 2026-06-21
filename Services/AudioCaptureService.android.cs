using musicmate.Utilities;
#if ANDROID
using Android;
using Android.Content.PM;
using Android.Media;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using System.Diagnostics;

namespace musicmate.Services
{
  public class AudioCaptureService: IAudioCaptureService
  {
    public event Action? MaxBlocksReached;
    private AudioRecord? _rec;
    private CancellationTokenSource? _cts;
    private Action<short[]>? _callback;

    public int SampleRate { get; } = 44100;
    public int BufferSize { get; } = 1024;

    public async Task EnsurePermissionAsync()
    {
      var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
      if (activity is null)
      {
        return;
      }

      var granted = ContextCompat.CheckSelfPermission(activity, Manifest.Permission.RecordAudio) == (int)Permission.Granted;
      if (!granted)
      {
        ActivityCompat.RequestPermissions(activity, new[] { Manifest.Permission.RecordAudio }, 1001);
        await Task.Delay(500);
      }
    }

    public void StartCapture(Action<short[]> onBlock)
    {
      _callback = onBlock;
      StopCapture();

      var minBuf = AudioRecord.GetMinBufferSize(SampleRate, ChannelIn.Mono, Encoding.Pcm16bit);
      var frameBytes = BufferSize * 2;
      var useBuf = Math.Max(minBuf, frameBytes * 4);

      _rec = new AudioRecord(AudioSource.Mic, SampleRate, ChannelIn.Mono, Encoding.Pcm16bit, useBuf);
      if (_rec.State != State.Initialized)
      {
        throw new InvalidOperationException("AudioRecord not initialized");
      }

        _cts = new CancellationTokenSource();
        var localCts = _cts;
        _rec.StartRecording();

        Task.Run(() => CaptureLoop(localCts.Token));
    }

    private void CaptureLoop(CancellationToken token)
    {
      try
      {
        var buf = new short[BufferSize];
        int blockCount = 0;
        const int MaxBlocks = 3000;
        while (!token.IsCancellationRequested)
        {
          if (_rec == null || _rec.State != State.Initialized)
            break;

          var read = _rec.Read(buf, 0, buf.Length);
          if (read > 0)
          {
            var block = new short[read];
            Array.Copy(buf, block, read);
            _callback?.Invoke(block);
            blockCount++;
            if (blockCount >= MaxBlocks)
            {
              Utils.Log($"[AudioCapture] MaxBlocks ({MaxBlocks}) reached, exiting capture loop.");
              MaxBlocksReached?.Invoke();
              break;
            }
          }
          else if (read < 0)
          {
            // Error or end of stream
            break;
          }
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"CaptureLoop error: {ex}");
      }
    }

    public void StopCapture()
    {
      try
      {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        if (_rec is not null)
        {
          _rec.Stop();
          _rec.Release();
          _rec.Dispose();
        }
      }
      catch (Exception ex)
      {
        System.Diagnostics.Debug.WriteLine($"StopCapture error: {ex}");
      }
      finally
      {
        _rec = null;
      }
    }
  }
}
#endif
