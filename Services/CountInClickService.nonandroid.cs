#if !ANDROID
namespace musicmate.Services
{
    /// <summary>
    /// Non-Android fallback: sine clicks via the shared playback service.
    /// Uses Waiting Count-In pitch (Hz) for accented / unaccented beats.
    /// </summary>
    public sealed class CountInClickService : ICountInClickService
    {
        private readonly IAudioPlaybackService _player;

        public CountInClickService(IAudioPlaybackService player)
        {
            _player = player;
        }

        public Task PlayClickAsync(
            bool accented,
            int durationMs,
            float volume,
            double frequencyHz,
            CancellationToken ct)
        {
            _ = accented;
            double hz = WaitingCountInSettings.ClampPitchHz(frequencyHz);
            double seconds = Math.Clamp(durationMs, 20, 2000) / 1000.0;
            return _player.PlayAsync(new[] { hz }, seconds, gapSeconds: 0, volume, ct);
        }

        public void Stop()
        {
            try { _player.CancelPlayback(); } catch { }
        }
    }
}
#endif
