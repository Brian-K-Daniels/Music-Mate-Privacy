namespace musicmate.Services
{
    /// <summary>
    /// Short metronome clicks for waiting count-in and the Tuner metronome.
    /// </summary>
    public interface ICountInClickService
    {
        /// <summary>
        /// Prebuild/load click buffers so the first scheduled beats are not delayed by I/O.
        /// Safe to call repeatedly; must not block the beat scheduler during playback.
        /// </summary>
        void Warmup(
            double accentedPitchHz,
            float accentedVolume,
            double unaccentedPitchHz,
            float unaccentedVolume,
            int durationMs);

        Task PlayClickAsync(
            bool accented,
            int durationMs,
            float volume,
            double frequencyHz,
            CancellationToken ct,
            MetronomeClickScheduleInfo? schedule = null);

        /// <summary>Silence immediately and invalidate any in-flight play requests.</summary>
        void Stop();
    }
}
