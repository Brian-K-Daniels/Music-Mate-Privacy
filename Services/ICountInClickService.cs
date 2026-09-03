namespace musicmate.Services
{
    /// <summary>
    /// Short metronome clicks for waiting count-in and the Tuner metronome.
    /// </summary>
    public interface ICountInClickService
    {
        Task PlayClickAsync(
            bool accented,
            int durationMs,
            float volume,
            double frequencyHz,
            CancellationToken ct,
            MetronomeClickScheduleInfo? schedule = null);

        void Stop();
    }
}
