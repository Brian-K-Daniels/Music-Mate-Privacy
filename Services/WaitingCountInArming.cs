namespace musicmate.Services
{
    /// <summary>
    /// Generation-gated Count-In start/stop so a delayed start cannot revive Count-In after Stop.
    /// MusicPage arms a generation before fire-and-forget; Stop invalidates it.
    /// </summary>
    public static class WaitingCountInArming
    {
        /// <summary>Call when intentionally starting Count-In (Go / OnAppearing auto-start).</summary>
        public static int Arm(ref int generation)
            => Interlocked.Increment(ref generation);

        /// <summary>Call on Stop so any pending or in-flight Count-In start is obsolete.</summary>
        public static int Invalidate(ref int generation)
            => Interlocked.Increment(ref generation);

        /// <summary>
        /// True when the armed start may create CTS / run the click loop.
        /// Session must still be running and the generation must not have been invalidated.
        /// </summary>
        public static bool MayBegin(
            int armedGeneration,
            int currentGeneration,
            bool sessionIsRunning)
            => sessionIsRunning && armedGeneration == currentGeneration;

        /// <summary>
        /// True when Count-In may continue after an await (delay, click loop segment).
        /// </summary>
        public static bool MayContinue(
            int armedGeneration,
            int currentGeneration,
            bool sessionIsRunning,
            bool cancellationRequested)
            => MayBegin(armedGeneration, currentGeneration, sessionIsRunning)
               && !cancellationRequested;
    }
}
