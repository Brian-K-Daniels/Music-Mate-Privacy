using musicmate.Services;

namespace musicmate.Tests;

/// <summary>
/// Music-page Count-In must follow page visibility (leave → stop, return → one fresh sequence).
/// </summary>
[Collection("SessionPreferences")]
public class MusicListeningVisibilityTests
{
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(true, true, false, true)]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, true, false)]
    public void ShouldPauseListeningForHide_MatchesRunningIntent(
        bool isRunning,
        bool waitingCountInActive,
        bool userStopped,
        bool expected)
    {
        Assert.Equal(
            expected,
            MusicListeningVisibility.ShouldPauseListeningForHide(
                isRunning, waitingCountInActive, userStopped));
    }

    [Fact]
    public void ShouldRestartCountInOnResume_OnlyWhileAwaitingFirstNote()
    {
        Assert.True(MusicListeningVisibility.ShouldRestartCountInOnResume(
            countInEnabled: true, noteCount: 4, currentNoteIndex: 0, firstPitchAlreadyDetected: false));

        Assert.False(MusicListeningVisibility.ShouldRestartCountInOnResume(
            countInEnabled: false, noteCount: 4, currentNoteIndex: 0, firstPitchAlreadyDetected: false));
        Assert.False(MusicListeningVisibility.ShouldRestartCountInOnResume(
            countInEnabled: true, noteCount: 0, currentNoteIndex: 0, firstPitchAlreadyDetected: false));
        Assert.False(MusicListeningVisibility.ShouldRestartCountInOnResume(
            countInEnabled: true, noteCount: 4, currentNoteIndex: 1, firstPitchAlreadyDetected: false));
        Assert.False(MusicListeningVisibility.ShouldRestartCountInOnResume(
            countInEnabled: true, noteCount: 4, currentNoteIndex: 0, firstPitchAlreadyDetected: true));
    }

    [Fact]
    public void ShouldScheduleAutoStartOnAppear_BlockedAfterUserStop()
    {
        Assert.False(MusicListeningVisibility.ShouldScheduleAutoStartOnAppear(
            listeningPausedForHide: false,
            userStoppedListening: true,
            autoStart: true,
            countInEnabled: true,
            isTuner: false,
            holdResult: false));
    }

    [Fact]
    public void ShouldScheduleAutoStartOnAppear_WhenCountInEnabledAndNotStopped()
    {
        Assert.True(MusicListeningVisibility.ShouldScheduleAutoStartOnAppear(
            listeningPausedForHide: false,
            userStoppedListening: false,
            autoStart: false,
            countInEnabled: true,
            isTuner: false,
            holdResult: false));
    }

    [Fact]
    public void ShouldResumeListeningOnAppear_RequiresSoftPauseAndRunning()
    {
        Assert.True(MusicListeningVisibility.ShouldResumeListeningOnAppear(
            listeningPausedForHide: true,
            isRunning: true,
            userStoppedListening: false,
            pageIsVisible: true));

        Assert.False(MusicListeningVisibility.ShouldResumeListeningOnAppear(
            listeningPausedForHide: true,
            isRunning: true,
            userStoppedListening: true,
            pageIsVisible: true));

        Assert.False(MusicListeningVisibility.ShouldResumeListeningOnAppear(
            listeningPausedForHide: false,
            isRunning: true,
            userStoppedListening: false,
            pageIsVisible: true));
    }

    [Fact]
    public void ShouldStopMusicCountInForTunerDisplay_WhenTunerVisible()
    {
        Assert.True(MusicListeningVisibility.ShouldStopMusicCountInForTunerDisplay(true));
        Assert.False(MusicListeningVisibility.ShouldStopMusicCountInForTunerDisplay(false));
    }

    [Fact]
    public void ShouldPauseMusicListeningForTuner_OnlyWhenEnteringWhileRunning()
    {
        Assert.True(MusicListeningVisibility.ShouldPauseMusicListeningForTuner(
            enteringTuner: true, isRunning: true, waitingCountInActive: false, userStoppedListening: false));
        Assert.False(MusicListeningVisibility.ShouldPauseMusicListeningForTuner(
            enteringTuner: false, isRunning: true, waitingCountInActive: true, userStoppedListening: false));
        Assert.False(MusicListeningVisibility.ShouldPauseMusicListeningForTuner(
            enteringTuner: true, isRunning: true, waitingCountInActive: true, userStoppedListening: true));
    }

    [Fact]
    public void ShouldResumeMusicListeningAfterLeavingTuner_RequiresSoftPause()
    {
        Assert.True(MusicListeningVisibility.ShouldResumeMusicListeningAfterLeavingTuner(
            leavingTuner: true, listeningPaused: true, userStoppedListening: false));
        Assert.False(MusicListeningVisibility.ShouldResumeMusicListeningAfterLeavingTuner(
            leavingTuner: true, listeningPaused: true, userStoppedListening: true));
        Assert.False(MusicListeningVisibility.ShouldResumeMusicListeningAfterLeavingTuner(
            leavingTuner: false, listeningPaused: true, userStoppedListening: false));
    }

    [Fact]
    public void ShouldDeferResumeWhileTunerVisible_KeepsPauseUntilLeave()
    {
        Assert.True(MusicListeningVisibility.ShouldDeferResumeWhileTunerVisible(
            resumeListening: true, tunerIsVisible: true));
        Assert.False(MusicListeningVisibility.ShouldDeferResumeWhileTunerVisible(
            resumeListening: true, tunerIsVisible: false));
        Assert.False(MusicListeningVisibility.ShouldDeferResumeWhileTunerVisible(
            resumeListening: false, tunerIsVisible: true));
    }

    [Fact]
    public void ShouldScheduleAutoStartOnNavigatedTo_BlockedWhenSoftPausedRunning()
    {
        // Soft-pause keeps isRunning true; OnNavigatedTo must not schedule AutoStart
        // (that cancelled Count-In resume after My Progress).
        Assert.False(MusicListeningVisibility.ShouldScheduleAutoStartOnNavigatedTo(
            holdResult: false,
            isTuner: false,
            userStoppedListening: false,
            isRunning: true,
            autoStart: true,
            countInEnabled: true));
    }

    [Fact]
    public void ShouldScheduleAutoStartOnNavigatedTo_WhenIdleAndCountInEnabled()
    {
        Assert.True(MusicListeningVisibility.ShouldScheduleAutoStartOnNavigatedTo(
            holdResult: false,
            isTuner: false,
            userStoppedListening: false,
            isRunning: false,
            autoStart: false,
            countInEnabled: true));
    }
}

/// <summary>
/// Integration-style Count-In hide/return cycles using the same arming + player path as MusicPage.
/// </summary>
[Collection("SessionPreferences")]
public class WaitingCountInPageVisibilityTests : IDisposable
{
    private readonly Dictionary<string, object?> _store = new();

    public WaitingCountInPageVisibilityTests()
    {
        SessionPreferences.TestStore = _store;
        _store.Clear();
    }

    public void Dispose() => SessionPreferences.TestStore = null;

    [Fact]
    public async Task MusicToNoteAttempts_StopsCountInImmediately()
    {
        var clicks = new RecordingInstantClicks();
        var player = new WaitingCountInPlayer(clicks);
        var state = new PageCountInState(player, clicks);

        state.SimulateMusicVisibleAndStartCountIn();
        await Task.Delay(50);
        Assert.True(player.IsActive || clicks.PlayCount >= 1);

        // Music → NoteAttempts (or any obscure): OnDisappearing pattern
        state.SimulatePageHide();
        int atHide = clicks.PlayCount;
        await Task.Delay(200);
        await state.DrainActiveRunAsync();

        Assert.False(player.IsActive);
        Assert.Equal(atHide, clicks.PlayCount);
        Assert.True(state.ListeningPausedForHide);
        Assert.True(state.IsRunning);
    }

    [Fact]
    public async Task NoteAttemptsToMusic_ResumesCountInFreshSequence()
    {
        var clicks = new RecordingInstantClicks();
        var player = new WaitingCountInPlayer(clicks);
        var state = new PageCountInState(player, clicks);

        state.SimulateMusicVisibleAndStartCountIn();
        await Task.Delay(40);
        state.SimulatePageHide();
        await state.DrainActiveRunAsync();
        int afterHide = clicks.PlayCount;

        state.SimulatePageAppearAndResumeCountIn();
        await Task.Delay(80);

        Assert.True(clicks.PlayCount > afterHide);
        Assert.True(player.IsActive || clicks.PlayCount > afterHide);
        Assert.Equal(1, state.ArmedResumeCount);

        state.SimulatePageHide();
        await state.DrainActiveRunAsync();
    }

    [Fact]
    public async Task MusicToAnotherPage_AlsoStopsCountIn()
    {
        var clicks = new RecordingInstantClicks();
        var player = new WaitingCountInPlayer(clicks);
        var state = new PageCountInState(player, clicks);

        state.SimulateMusicVisibleAndStartCountIn();
        await Task.Delay(40);
        // WhatToPlay / Settings / any non-NoteAttempts page — same lifecycle.
        state.SimulatePageHide();
        int atHide = clicks.PlayCount;
        await Task.Delay(180);
        await state.DrainActiveRunAsync();

        Assert.False(player.IsActive);
        Assert.Equal(atHide, clicks.PlayCount);
    }

    [Fact]
    public async Task MultipleLeaveReturnCycles_NeverOverlapBeeps()
    {
        var clicks = new RecordingInstantClicks();
        var player = new WaitingCountInPlayer(clicks);
        var state = new PageCountInState(player, clicks);

        state.SimulateMusicVisibleAndStartCountIn();
        for (int i = 0; i < 4; i++)
        {
            await Task.Delay(35);
            state.SimulatePageHide();
            await state.DrainActiveRunAsync();

            if (i < 3)
                state.SimulatePageAppearAndResumeCountIn();
        }

        Assert.True(clicks.MaxConcurrentPlays <= 1,
            $"Overlapping Count-In plays: max concurrent={clicks.MaxConcurrentPlays}");
        Assert.False(player.IsActive);
        Assert.Equal(4, state.HideCount);
        Assert.Equal(3, state.ArmedResumeCount);
    }

    [Fact]
    public async Task StopWhileAway_PreventsCountInResume()
    {
        var clicks = new RecordingInstantClicks();
        var player = new WaitingCountInPlayer(clicks);
        var state = new PageCountInState(player, clicks);

        state.SimulateMusicVisibleAndStartCountIn();
        await Task.Delay(40);
        state.SimulatePageHide();
        await state.DrainActiveRunAsync();
        int afterHide = clicks.PlayCount;

        // User Stop while Music is obscured (or Stop sticky before return).
        state.SimulateUserStopWhileAway();
        state.SimulatePageAppearAndResumeCountIn();
        await Task.Delay(120);

        Assert.Equal(afterHide, clicks.PlayCount);
        Assert.False(player.IsActive);
        Assert.Equal(0, state.ArmedResumeCount);
        Assert.False(MusicListeningVisibility.ShouldScheduleAutoStartOnAppear(
            listeningPausedForHide: false,
            userStoppedListening: true,
            autoStart: false,
            countInEnabled: true,
            isTuner: false,
            holdResult: false));
    }

    [Fact]
    public async Task MusicCountIn_OpenTuner_StopsBeepsImmediately()
    {
        var clicks = new RecordingInstantClicks();
        var player = new WaitingCountInPlayer(clicks);
        var state = new PageCountInState(player, clicks);

        state.SimulateMusicVisibleAndStartCountIn();
        await Task.Delay(50);
        Assert.True(player.IsActive || clicks.PlayCount >= 1);

        state.SimulateEnterTuner();
        int atTuner = clicks.PlayCount;
        await Task.Delay(200);
        await state.DrainActiveRunAsync();

        Assert.False(player.IsActive);
        Assert.Equal(atTuner, clicks.PlayCount);
        Assert.True(state.IsTunerVisible);
        Assert.True(state.ListeningPausedForHide);
    }

    [Fact]
    public async Task NoAdditionalBeep_AfterTunerIsVisible()
    {
        var clicks = new RecordingInstantClicks();
        var player = new WaitingCountInPlayer(clicks);
        var state = new PageCountInState(player, clicks);

        state.SimulateMusicVisibleAndStartCountIn();
        await Task.Delay(40);
        state.SimulateEnterTuner();
        int atTuner = clicks.PlayCount;

        // Stale delayed arm must not sound while Tuner is visible.
        state.SimulateStaleCountInArmAttempt();
        await Task.Delay(150);
        await state.DrainActiveRunAsync();

        Assert.Equal(atTuner, clicks.PlayCount);
        Assert.False(player.IsActive);
    }

    [Fact]
    public async Task TunerToMusic_ResumesCountInWhenAppropriate()
    {
        var clicks = new RecordingInstantClicks();
        var player = new WaitingCountInPlayer(clicks);
        var state = new PageCountInState(player, clicks);

        state.SimulateMusicVisibleAndStartCountIn();
        await Task.Delay(40);
        state.SimulateEnterTuner();
        await state.DrainActiveRunAsync();
        int afterTuner = clicks.PlayCount;

        state.SimulateLeaveTunerAndResumeCountIn(noteCount: 4);
        await Task.Delay(80);

        Assert.True(clicks.PlayCount > afterTuner);
        Assert.Equal(1, state.ArmedResumeCount);
        Assert.False(state.IsTunerVisible);

        state.SimulateEnterTuner();
        await state.DrainActiveRunAsync();
    }

    [Fact]
    public async Task RepeatedMusicTunerNavigation_NeverOverlapsCountIn()
    {
        var clicks = new RecordingInstantClicks();
        var player = new WaitingCountInPlayer(clicks);
        var state = new PageCountInState(player, clicks);

        state.SimulateMusicVisibleAndStartCountIn();
        for (int i = 0; i < 4; i++)
        {
            await Task.Delay(35);
            state.SimulateEnterTuner();
            await state.DrainActiveRunAsync();

            if (i < 3)
                state.SimulateLeaveTunerAndResumeCountIn(noteCount: 4);
        }

        Assert.True(clicks.MaxConcurrentPlays <= 1,
            $"Overlapping Count-In plays: max concurrent={clicks.MaxConcurrentPlays}");
        Assert.False(player.IsActive);
        Assert.Equal(4, state.TunerEnterCount);
        Assert.Equal(3, state.ArmedResumeCount);
    }

    [Fact]
    public async Task StoppedBeforeTuner_ReturnToMusic_DoesNotStartCountIn()
    {
        var clicks = new RecordingInstantClicks();
        var player = new WaitingCountInPlayer(clicks);
        var state = new PageCountInState(player, clicks);

        state.SimulateMusicVisibleAndStartCountIn();
        await Task.Delay(40);
        state.SimulateUserStopWhileAway();
        int afterStop = clicks.PlayCount;

        state.SimulateEnterTuner();
        await state.DrainActiveRunAsync();
        state.SimulateLeaveTunerAndResumeCountIn(noteCount: 4);
        await Task.Delay(120);

        Assert.Equal(afterStop, clicks.PlayCount);
        Assert.False(player.IsActive);
        Assert.Equal(0, state.ArmedResumeCount);
        Assert.False(state.ListeningPausedForHide);
    }

    [Fact]
    public async Task MusicToMyProgress_StopsCountInImmediately()
    {
        var clicks = new RecordingInstantClicks();
        var player = new WaitingCountInPlayer(clicks);
        var state = new PageCountInState(player, clicks);

        state.SimulateMusicVisibleAndStartCountIn();
        await Task.Delay(50);
        Assert.True(player.IsActive || clicks.PlayCount >= 1);

        // Music → My Progress (Session) uses the same OnDisappearing soft-pause path.
        state.SimulatePageHide();
        int atHide = clicks.PlayCount;
        await Task.Delay(200);
        await state.DrainActiveRunAsync();

        Assert.False(player.IsActive);
        Assert.Equal(atHide, clicks.PlayCount);
        Assert.True(state.ListeningPausedForHide);
        Assert.True(state.IsRunning);
    }

    [Fact]
    public async Task MyProgressToMusic_ResumesCountIn_DespiteNavigatedToAutoStart()
    {
        var clicks = new RecordingInstantClicks();
        var player = new WaitingCountInPlayer(clicks);
        var state = new PageCountInState(player, clicks);

        state.SimulateMusicVisibleAndStartCountIn();
        await Task.Delay(40);
        state.SimulatePageHide();
        await state.DrainActiveRunAsync();
        int afterHide = clicks.PlayCount;

        // OnAppearing schedules resume; OnNavigatedTo must not cancel it when soft-paused.
        Assert.False(MusicListeningVisibility.ShouldScheduleAutoStartOnNavigatedTo(
            holdResult: false,
            isTuner: false,
            userStoppedListening: false,
            isRunning: true,
            autoStart: true,
            countInEnabled: true));

        state.SimulatePageAppearAndResumeCountIn();
        // Simulate a late NavigatedTo AutoStart attempt that must be a no-op.
        state.SimulateNavigatedToAutoStartAttempt();
        await Task.Delay(80);

        Assert.True(clicks.PlayCount > afterHide);
        Assert.Equal(1, state.ArmedResumeCount);
        Assert.True(clicks.MaxConcurrentPlays <= 1);
    }

    [Fact]
    public async Task RepeatedMusicMyProgress_NeverOverlapsCountIn()
    {
        var clicks = new RecordingInstantClicks();
        var player = new WaitingCountInPlayer(clicks);
        var state = new PageCountInState(player, clicks);

        state.SimulateMusicVisibleAndStartCountIn();
        for (int i = 0; i < 4; i++)
        {
            await Task.Delay(35);
            state.SimulatePageHide();
            await state.DrainActiveRunAsync();
            if (i < 3)
            {
                state.SimulatePageAppearAndResumeCountIn();
                state.SimulateNavigatedToAutoStartAttempt();
            }
        }

        Assert.True(clicks.MaxConcurrentPlays <= 1,
            $"Overlapping Count-In plays: max concurrent={clicks.MaxConcurrentPlays}");
        Assert.False(player.IsActive);
        Assert.Equal(4, state.HideCount);
        Assert.Equal(3, state.ArmedResumeCount);
    }

    [Fact]
    public async Task StoppedBeforeMyProgress_ReturnDoesNotStartCountIn()
    {
        var clicks = new RecordingInstantClicks();
        var player = new WaitingCountInPlayer(clicks);
        var state = new PageCountInState(player, clicks);

        state.SimulateMusicVisibleAndStartCountIn();
        await Task.Delay(40);
        state.SimulateUserStopWhileAway();
        int afterStop = clicks.PlayCount;

        state.SimulatePageHide();
        await state.DrainActiveRunAsync();
        state.SimulatePageAppearAndResumeCountIn();
        state.SimulateNavigatedToAutoStartAttempt();
        await Task.Delay(120);

        Assert.Equal(afterStop, clicks.PlayCount);
        Assert.False(player.IsActive);
        Assert.Equal(0, state.ArmedResumeCount);
    }

    /// <summary>
    /// Mirrors MusicPage Count-In visibility without constructing the full page.
    /// </summary>
    private sealed class PageCountInState
    {
        private readonly WaitingCountInPlayer _player;
        private readonly RecordingInstantClicks _clicks;
        private int _generation;
        private CancellationTokenSource? _cts;
        private Task? _activeRun;
        private int _startEpoch;

        public PageCountInState(WaitingCountInPlayer player, RecordingInstantClicks clicks)
        {
            _player = player;
            _clicks = clicks;
        }

        public bool IsPageVisible { get; private set; }
        public bool IsTunerVisible { get; private set; }
        public bool IsRunning { get; private set; }
        public bool WaitingCountInActive { get; private set; }
        public bool ListeningPausedForHide { get; private set; }
        public bool UserStoppedListening { get; private set; }
        public int ArmedResumeCount { get; private set; }
        public int HideCount { get; private set; }
        public int TunerEnterCount { get; private set; }

        public void SimulateMusicVisibleAndStartCountIn()
        {
            IsPageVisible = true;
            IsTunerVisible = false;
            UserStoppedListening = false;
            ListeningPausedForHide = false;
            IsRunning = true;
            ArmAndStartCountIn();
        }

        public void SimulatePageHide()
        {
            IsPageVisible = false;
            HideCount++;
            ListeningPausedForHide = MusicListeningVisibility.ShouldPauseListeningForHide(
                IsRunning, WaitingCountInActive, UserStoppedListening);

            // CancelPendingListeningStarts + StopWaitingCountIn
            Interlocked.Increment(ref _startEpoch);
            WaitingCountInArming.Invalidate(ref _generation);
            WaitingCountInActive = false;
            try { _cts?.Cancel(); } catch { }
            _player.Stop();
            _clicks.Stop();

            // Soft-pause keeps IsRunning; hard stop clears it.
            if (!ListeningPausedForHide)
                IsRunning = false;
        }

        public void SimulateEnterTuner()
        {
            bool entering = !IsTunerVisible;
            TunerEnterCount++;
            IsTunerVisible = true;

            if (MusicListeningVisibility.ShouldPauseMusicListeningForTuner(
                    entering, IsRunning, WaitingCountInActive, UserStoppedListening))
                ListeningPausedForHide = true;

            Interlocked.Increment(ref _startEpoch);
            WaitingCountInArming.Invalidate(ref _generation);
            WaitingCountInActive = false;
            try { _cts?.Cancel(); } catch { }
            _player.Stop();
            _clicks.Stop();

            // Yield running so Tuner mic can start (MusicPage SetButtonStates(false)).
            IsRunning = false;
        }

        public void SimulateLeaveTunerAndResumeCountIn(int noteCount)
        {
            bool leaving = IsTunerVisible;
            IsTunerVisible = false;

            if (!MusicListeningVisibility.ShouldResumeMusicListeningAfterLeavingTuner(
                    leaving, ListeningPausedForHide, UserStoppedListening))
                return;

            ListeningPausedForHide = false;
            if (!MusicListeningVisibility.ShouldRestartCountInOnResume(
                    countInEnabled: true,
                    noteCount: noteCount,
                    currentNoteIndex: 0,
                    firstPitchAlreadyDetected: false))
                return;

            IsRunning = true;
            ArmedResumeCount++;
            ArmAndStartCountIn();
        }

        public void SimulateStaleCountInArmAttempt()
        {
            // Mimics a delayed StartWaitingCountInAsync after Tuner became visible.
            int armed = WaitingCountInArming.Arm(ref _generation);
            _activeRun = RunArmedAsync(armed, _startEpoch, pageVisibleAtArm: true, runningAtArm: true);
        }

        /// <summary>
        /// OnNavigatedTo used to ScheduleAutoStart which cancelled resume CTS.
        /// When soft-paused (IsRunning), this must be a no-op — do not arm a second Count-In.
        /// </summary>
        public void SimulateNavigatedToAutoStartAttempt()
        {
            if (!MusicListeningVisibility.ShouldScheduleAutoStartOnNavigatedTo(
                    holdResult: false,
                    isTuner: IsTunerVisible,
                    userStoppedListening: UserStoppedListening,
                    isRunning: IsRunning,
                    autoStart: true,
                    countInEnabled: true))
            {
                return;
            }

            // Only if truly idle — would start a fresh listen/Count-In (not used on soft-pause return).
            IsRunning = true;
            ArmedResumeCount++;
            ArmAndStartCountIn();
        }

        public void SimulateUserStopWhileAway()
        {
            UserStoppedListening = true;
            ListeningPausedForHide = false;
            IsRunning = false;
            WaitingCountInArming.Invalidate(ref _generation);
            WaitingCountInActive = false;
            try { _cts?.Cancel(); } catch { }
            _player.Stop();
            _clicks.Stop();
        }

        public void SimulatePageAppearAndResumeCountIn()
        {
            bool resume = MusicListeningVisibility.ShouldResumeListeningOnAppear(
                ListeningPausedForHide, IsRunning, UserStoppedListening, pageIsVisible: true);
            ListeningPausedForHide = false;
            IsPageVisible = true;

            if (!resume)
                return;

            if (!MusicListeningVisibility.ShouldRestartCountInOnResume(
                    countInEnabled: true,
                    noteCount: 4,
                    currentNoteIndex: 0,
                    firstPitchAlreadyDetected: false))
                return;

            ArmedResumeCount++;
            ArmAndStartCountIn();
        }

        public async Task DrainActiveRunAsync()
        {
            if (_activeRun != null)
                await _activeRun;
        }

        private void ArmAndStartCountIn()
        {
            WaitingCountInArming.Invalidate(ref _generation);
            int armed = WaitingCountInArming.Arm(ref _generation);
            WaitingCountInActive = true;
            int epoch = _startEpoch;
            bool pageVisible = IsPageVisible;
            bool running = IsRunning;
            _activeRun = RunArmedAsync(armed, epoch, pageVisible, running);
        }

        private async Task RunArmedAsync(int armed, int epochAtArm, bool pageVisibleAtArm, bool runningAtArm)
        {
            _ = pageVisibleAtArm;
            _ = runningAtArm;
            if (!WaitingCountInArming.MayBegin(armed, _generation, IsRunning)
                || !IsPageVisible
                || IsTunerVisible)
            {
                WaitingCountInActive = false;
                return;
            }

            if (epochAtArm != Volatile.Read(ref _startEpoch) || !IsPageVisible || IsTunerVisible)
            {
                WaitingCountInActive = false;
                return;
            }

            var cts = new CancellationTokenSource();
            _cts = cts;
            try
            {
                await _player.RunAsync(
                    tempoBpm: 120,
                    beatsPerMeasure: 4,
                    accentedVolume: 0.4f,
                    unaccentedVolume: 0.2f,
                    accentedPitchHz: 1760,
                    unaccentedPitchHz: 880,
                    beatDurationPercent: 15,
                    externalCt: cts.Token,
                    beforeClickAsync: (_, _) =>
                    {
                        if (!IsPageVisible || IsTunerVisible)
                            throw new OperationCanceledException();
                        return Task.CompletedTask;
                    });
            }
            catch (OperationCanceledException)
            {
                // expected on hide/stop/tuner
            }
            finally
            {
                if (armed == Volatile.Read(ref _generation) && !_player.IsActive)
                    WaitingCountInActive = false;
            }
        }
    }

    private sealed class RecordingInstantClicks : ICountInClickService
    {
        private int _concurrent;
        public int PlayCount { get; private set; }
        public int MaxConcurrentPlays { get; private set; }

        public void Warmup(double accentedPitchHz, float accentedVolume, double unaccentedPitchHz, float unaccentedVolume, int durationMs) { }

        public Task PlayClickAsync(
            bool accented,
            int durationMs,
            float volume,
            double frequencyHz,
            CancellationToken ct,
            MetronomeClickScheduleInfo? schedule = null)
        {
            if (ct.IsCancellationRequested)
                return Task.CompletedTask;

            int c = Interlocked.Increment(ref _concurrent);
            MaxConcurrentPlays = Math.Max(MaxConcurrentPlays, c);
            PlayCount++;
            Interlocked.Decrement(ref _concurrent);
            return Task.CompletedTask;
        }

        public void Stop() { }
    }
}
