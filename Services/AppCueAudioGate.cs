namespace musicmate.Services
{
    /// <summary>
    /// Notifies Music-page cue audio (Count-In / Tuner metronome) and capture to stop when the app
    /// is suspended or the window is closing — OnDisappearing alone does not cover swipe-away /
    /// screen-off. <see cref="NotifyAppResumed"/> lets a still-visible Music page soft-resume.
    /// A headset or USB microphone pauses the activity and then resumes it. That pause must not
    /// stop Count-In or Play. A pause that stays paused (screen-off, background, swipe-away) still does.
    /// </summary>
    public static class AppCueAudioGate
    {
        internal static readonly TimeSpan DefaultSuspendGrace = TimeSpan.FromMilliseconds(700);

        /// <summary>
        /// How long a suspend notification waits for a matching resume before stopping cue audio.
        /// Zero invokes <see cref="SuspendRequested"/> immediately.
        /// </summary>
        internal static TimeSpan SuspendGrace { get; set; } = DefaultSuspendGrace;

        public static event Action? SuspendRequested;
        public static event Action? ResumeRequested;

        private static readonly object Gate = new();
        private static CancellationTokenSource? _pending;
        private static int _generation;

        public static void NotifyAppSuspended()
        {
            if (SuspendGrace <= TimeSpan.Zero)
            {
                CancelPendingSuspend();
                RaiseSuspend();
                return;
            }

            CancellationToken token;
            int generation;
            lock (Gate)
            {
                _pending?.Cancel();
                _pending = new CancellationTokenSource();
                token = _pending.Token;
                generation = ++_generation;
            }

            _ = RaiseSuspendAfterGraceAsync(generation, token);
        }

        public static void NotifyAppResumed()
        {
            CancelPendingSuspend();
            RaiseResume();
        }

        /// <summary>Drops a suspend that has not fired yet. Does not raise <see cref="ResumeRequested"/>.</summary>
        internal static void CancelPendingSuspend()
        {
            lock (Gate)
            {
                _generation++;
                try { _pending?.Cancel(); } catch { /* already cancelled */ }
                _pending = null;
            }
        }

        private static async Task RaiseSuspendAfterGraceAsync(int generation, CancellationToken token)
        {
            try
            {
                await Task.Delay(SuspendGrace, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            lock (Gate)
            {
                if (generation != _generation || token.IsCancellationRequested)
                    return;
                _pending = null;
            }

            RaiseSuspend();
        }

        private static void RaiseSuspend()
        {
            try { SuspendRequested?.Invoke(); }
            catch { /* best-effort */ }
        }

        private static void RaiseResume()
        {
            try { ResumeRequested?.Invoke(); }
            catch { /* best-effort */ }
        }
    }
}
