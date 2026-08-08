#if !ANDROID
namespace musicmate.Services
{
    /// <summary>
    /// Non-Android fallback: sine clicks via the shared playback service.
    /// </summary>
    public sealed class CountInClickService : ICountInClickService
    {
        private readonly IAudioPlaybackService _player;
        private const double AccentHz = 1760.0;
        private const double UnaccentHz = 1318.5;

        public CountInClickService(IAudioPlaybackService player)
        {
            _player = player;
        }

        public Task PlayClickAsync(bool accented, int durationMs, float volume, CancellationToken ct)
        {
            double hz = accented ? AccentHz : UnaccentHz;
            double seconds = Math.Clamp(durationMs, 20, 200) / 1000.0;
            return _player.PlayAsync(new[] { hz }, seconds, gapSeconds: 0, volume, ct);
        }

        public void Stop()
        {
            try { _player.CancelPlayback(); } catch { }
        }
    }
}
#endif
