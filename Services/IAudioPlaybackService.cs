namespace musicmate.Services
{
    public interface IAudioPlaybackService
    {
        Task PlayAsync(IEnumerable<double> freqs, double noteSeconds, double gapSeconds, float volume, CancellationToken ct);
        /// <summary>
        /// Play one frequency until <paramref name="ct"/> is cancelled (looped PCM, no inter-note gaps).
        /// </summary>
        Task PlaySustainedAsync(double freq, float volume, CancellationToken ct);
        /// <summary>
        /// Request cancellation of any currently playing audio started by PlayAsync.
        /// </summary>
        void CancelPlayback();
    }
}
