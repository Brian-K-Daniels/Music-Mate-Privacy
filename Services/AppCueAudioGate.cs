namespace musicmate.Services
{
    /// <summary>
    /// Notifies Music-page cue audio (Count-In / Tuner metronome) to stop when the app
    /// is suspended or the window is closing — OnDisappearing alone does not cover swipe-away.
    /// </summary>
    public static class AppCueAudioGate
    {
        public static event Action? SuspendRequested;

        public static void NotifyAppSuspended()
        {
            try { SuspendRequested?.Invoke(); }
            catch { /* best-effort */ }
        }
    }
}
