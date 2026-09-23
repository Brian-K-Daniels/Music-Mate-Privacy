namespace musicmate.Services
{
    /// <summary>
    /// Pure decisions for pausing/resuming listening and Count-In when the Music page
    /// is obscured or returns to the foreground. Keeps navigation behavior out of
    /// page-specific NoteAttempts special-cases.
    /// </summary>
    public static class MusicListeningVisibility
    {
        /// <summary>
        /// True when leaving Music should soft-pause (keep session running intent)
        /// rather than treat the hide as a user Stop.
        /// </summary>
        public static bool ShouldPauseListeningForHide(
            bool isRunning,
            bool waitingCountInActive,
            bool userStoppedListening)
            => !userStoppedListening && (isRunning || waitingCountInActive);

        /// <summary>
        /// True when returning to Music should restart a fresh Count-In sequence
        /// (still waiting for the first accepted note).
        /// </summary>
        public static bool ShouldRestartCountInOnResume(
            bool countInEnabled,
            int noteCount,
            int currentNoteIndex,
            bool firstPitchAlreadyDetected)
            => countInEnabled
               && noteCount > 0
               && currentNoteIndex == 0
               && !firstPitchAlreadyDetected;

        /// <summary>
        /// True when OnAppearing should run the normal AutoStart / Count-In-enabled
        /// listen schedule (not the soft-pause resume path).
        /// </summary>
        public static bool ShouldScheduleAutoStartOnAppear(
            bool listeningPausedForHide,
            bool userStoppedListening,
            bool autoStart,
            bool countInEnabled,
            bool isTuner,
            bool holdResult)
        {
            if (holdResult || isTuner)
                return false;
            if (listeningPausedForHide)
                return false;
            if (userStoppedListening)
                return false;
            return autoStart || countInEnabled;
        }

        /// <summary>
        /// True when OnAppearing should resume a soft-paused listening session
        /// (at most one Count-In arm / capture restart).
        /// </summary>
        public static bool ShouldResumeListeningOnAppear(
            bool listeningPausedForHide,
            bool isRunning,
            bool userStoppedListening,
            bool pageIsVisible)
            => listeningPausedForHide
               && isRunning
               && !userStoppedListening
               && pageIsVisible;

        /// <summary>
        /// OnNavigatedTo must not schedule AutoStart when a soft-paused session is still
        /// marked running — that shares the AutoStart CTS and would cancel the Count-In
        /// resume scheduled from OnAppearing (Music → My Progress → Music).
        /// </summary>
        public static bool ShouldScheduleAutoStartOnNavigatedTo(
            bool holdResult,
            bool isTuner,
            bool userStoppedListening,
            bool isRunning,
            bool autoStart,
            bool countInEnabled)
        {
            if (holdResult || isTuner || userStoppedListening || isRunning)
                return false;
            return autoStart || countInEnabled;
        }

        /// <summary>
        /// Music Count-In must never continue while the Tuner UI is the active surface
        /// (same MusicPage, Tune == Tuner — OnDisappearing does not run).
        /// </summary>
        public static bool ShouldStopMusicCountInForTunerDisplay(bool tunerIsVisible)
            => tunerIsVisible;

        /// <summary>
        /// Soft-pause Music listening when switching the visible surface to Tuner so
        /// return to Music can resume Count-In without treating Tuner as Stop.
        /// </summary>
        public static bool ShouldPauseMusicListeningForTuner(
            bool enteringTuner,
            bool isRunning,
            bool waitingCountInActive,
            bool userStoppedListening)
            => enteringTuner
               && ShouldPauseListeningForHide(isRunning, waitingCountInActive, userStoppedListening);

        /// <summary>
        /// Leaving Tuner may resume Music Count-In when a soft-pause is still pending
        /// and the user has not Stopped.
        /// </summary>
        public static bool ShouldResumeMusicListeningAfterLeavingTuner(
            bool leavingTuner,
            bool listeningPaused,
            bool userStoppedListening)
            => leavingTuner && listeningPaused && !userStoppedListening;

        /// <summary>
        /// Appearing already in Tuner with a soft-paused Music session: keep the pause
        /// flag and do not arm Music Count-In until Tuner is left.
        /// </summary>
        public static bool ShouldDeferResumeWhileTunerVisible(
            bool resumeListening,
            bool tunerIsVisible)
            => resumeListening && tunerIsVisible;
    }
}
