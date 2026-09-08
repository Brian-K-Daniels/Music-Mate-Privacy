namespace musicmate.Services
{
    /// <summary>
    /// Single source of truth for Music-page title Play visibility and GO/Stop chrome in Tuner mode.
    /// </summary>
    public static class TunerTitleChrome
    {
        public enum TitleAction
        {
            Go,
            Stop
        }

        /// <summary>
        /// Obsolete green Play must never appear on Tuner; Music/Sight keep prior visibility rules.
        /// </summary>
        public static bool IsPlayButtonVisible(string? tune, bool isRunning, bool isPlaying)
            => !IsTuner(tune) && (!isRunning || isPlaying);

        /// <summary>
        /// While the Tuner metronome runs, the top button stays green Go (metronome has its own Stop).
        /// Otherwise Stop when listening, Go when idle.
        /// </summary>
        public static TitleAction ResolveTitleAction(
            string? tune,
            bool isListeningRunning,
            bool isMetronomePlaying)
        {
            if (IsTuner(tune) && isMetronomePlaying)
                return TitleAction.Go;

            return isListeningRunning ? TitleAction.Stop : TitleAction.Go;
        }

        private static bool IsTuner(string? tune)
            => string.Equals(tune, "Tuner", StringComparison.Ordinal);
    }
}
