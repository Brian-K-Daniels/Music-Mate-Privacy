#if ANDROID
using System.Diagnostics;
using Android.Media;

namespace musicmate.Services
{
    /// <summary>
    /// Android ToneGenerator clicks — audible while AudioRecord is open (unlike AudioTrack/MediaPlayer).
    /// </summary>
    public sealed class CountInClickService : ICountInClickService
    {
        private readonly object _gate = new();
        private ToneGenerator? _accented;
        private ToneGenerator? _unaccented;
        private int _accentedVol = -1;
        private int _unaccentedVol = -1;

        public async Task PlayClickAsync(bool accented, int durationMs, float volume, CancellationToken ct)
        {
            int dur = Math.Clamp(durationMs, 20, 200);
            int vol = Math.Clamp((int)Math.Round(Math.Clamp(volume, 0f, 1f) * 100), 1, 100);

            lock (_gate)
            {
                var tg = EnsureGenerator(accented, vol);
                // Distinct tones so beat 1 is recognizable without needing arbitrary Hz.
                var tone = accented ? Tone.CdmaAlertCallGuard : Tone.PropBeep;
                try
                {
                    tg.StartTone(tone, dur);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CountInClick] StartTone failed: {ex.Message}");
                }
            }

            try
            {
                await Task.Delay(dur, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                StopToneOnly();
                throw;
            }
        }

        public void Stop()
        {
            lock (_gate)
            {
                StopToneOnly_NoLock();
                Release_NoLock(ref _accented);
                Release_NoLock(ref _unaccented);
                _accentedVol = -1;
                _unaccentedVol = -1;
            }
        }

        private ToneGenerator EnsureGenerator(bool accented, int vol)
        {
            // Fully qualify Android.Media.Stream — System.IO.Stream also exists in scope.
            // Prefer ToneGenerator(Stream, int); the (Stream, Volume) overload is obsolete.
            if (accented)
            {
                if (_accented == null || _accentedVol != vol)
                {
                    Release_NoLock(ref _accented);
                    _accented = new ToneGenerator(Android.Media.Stream.Music, vol);
                    _accentedVol = vol;
                }
                return _accented;
            }

            if (_unaccented == null || _unaccentedVol != vol)
            {
                Release_NoLock(ref _unaccented);
                _unaccented = new ToneGenerator(Android.Media.Stream.Music, vol);
                _unaccentedVol = vol;
            }
            return _unaccented;
        }

        private void StopToneOnly()
        {
            lock (_gate)
                StopToneOnly_NoLock();
        }

        private void StopToneOnly_NoLock()
        {
            try { _accented?.StopTone(); } catch { }
            try { _unaccented?.StopTone(); } catch { }
        }

        private static void Release_NoLock(ref ToneGenerator? tg)
        {
            if (tg == null)
                return;
            try { tg.StopTone(); } catch { }
            try { tg.Release(); } catch { }
            try { tg.Dispose(); } catch { }
            tg = null;
        }
    }
}
#endif
