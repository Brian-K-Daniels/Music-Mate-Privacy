namespace musicmate.Services
{
    /// <summary>
    /// Short metronome clicks for waiting count-in. Prefer a path that stays audible
    /// while the microphone is open (ToneGenerator on Android).
    /// </summary>
    public interface ICountInClickService
    {
        Task PlayClickAsync(bool accented, int durationMs, float volume, CancellationToken ct);
        void Stop();
    }
}
