namespace musicmate.Services
{
    public interface IAudioPlaybackService
    {
        Task PlayAsync(IEnumerable<double> freqs, double noteSeconds, double gapSeconds, float volume, CancellationToken ct);
        /// <summary>
        /// Request cancellation of any currently playing audio started by PlayAsync.
        /// </summary>
        void CancelPlayback();
    }
}
