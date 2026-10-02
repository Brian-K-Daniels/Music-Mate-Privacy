using musicmate.Services;

namespace musicmate.Tests;

public class TunerListeningSessionTests
{
    [Fact]
    public void AutoStartOn_Appear_StartsListening()
    {
        var audio = new FakeMic();
        var session = new TunerListeningSession();

        Assert.True(session.ShouldAutoStartOnAppear(
            listeningStartsWhenPageAppears: true,
            isCapturing: false,
            referenceTonePlaying: false,
            pitchPlaying: false));

        Appear(session, audio, autoStart: true);

        Assert.Equal(1, session.StartAttempts);
        Assert.Equal(1, audio.StartCount);
        Assert.Equal(1, session.ActiveSessions);
        Assert.True(session.IsListening);
        Assert.True(audio.IsCapturing);
        Assert.Equal(TunerListeningSession.ListeningMessage, session.StatusMessage);
    }

    [Fact]
    public void AutoStartOff_Appear_DoesNotStart()
    {
        var audio = new FakeMic();
        var session = new TunerListeningSession();

        Assert.False(session.ShouldAutoStartOnAppear(
            listeningStartsWhenPageAppears: false,
            isCapturing: false,
            referenceTonePlaying: false,
            pitchPlaying: false));

        if (session.ShouldAutoStartOnAppear(false, audio.IsCapturing, false, false))
            GoOrBegin(session, audio);

        Assert.Equal(0, session.StartAttempts);
        Assert.False(audio.IsCapturing);
    }

    [Fact]
    public void FirstStartFails_ShowsFailure_GoRetries_SecondSuccessListens()
    {
        var audio = new FakeMic { Succeed = false };
        var session = new TunerListeningSession();

        Appear(session, audio, autoStart: true);

        Assert.Equal(TunerListeningSession.MicrophoneFailedMessage, session.StatusMessage);
        Assert.False(session.IsListening);
        Assert.Equal(0, session.ActiveSessions);
        Assert.False(audio.IsCapturing);

        audio.Succeed = true;
        GoOrBegin(session, audio);

        Assert.Equal(2, session.StartAttempts);
        Assert.Equal(2, audio.StartCount);
        Assert.True(session.IsListening);
        Assert.True(audio.IsCapturing);
        Assert.Equal(1, session.ActiveSessions);
        Assert.Equal(TunerListeningSession.ListeningMessage, session.StatusMessage);
    }

    [Fact]
    public void RepeatedGoWhileStarting_DoesNotOpenASecondSession()
    {
        var session = new TunerListeningSession();
        Assert.True(session.TryBeginStart(isCapturing: false));
        int generation = session.Generation;

        Assert.False(session.TryBeginStart(isCapturing: false));
        Assert.False(session.ShouldStopOnGo(isCapturing: false));
        Assert.Equal(1, session.StartAttempts);

        session.CompleteStart(generation, captureIsRunning: true);
        Assert.Equal(1, session.ActiveSessions);
        Assert.False(session.TryBeginStart(isCapturing: true));
        Assert.Equal(1, session.StartAttempts);
    }

    [Fact]
    public void RepeatedGoAfterFailure_EachTapIsANewAttempt_UntilOneSucceeds()
    {
        var audio = new FakeMic { Succeed = false };
        var session = new TunerListeningSession();

        GoOrBegin(session, audio);
        GoOrBegin(session, audio);
        Assert.Equal(2, audio.StartCount);
        Assert.Equal(0, session.ActiveSessions);

        audio.Succeed = true;
        GoOrBegin(session, audio);
        Assert.Equal(3, audio.StartCount);
        Assert.Equal(1, session.ActiveSessions);
        Assert.True(audio.IsCapturing);
        Assert.True(session.ShouldStopOnGo(audio.IsCapturing));

        GoOrBegin(session, audio);
        Assert.Equal(3, audio.StartCount);
        Assert.Equal(0, session.ActiveSessions);
        Assert.False(audio.IsCapturing);
    }

    [Fact]
    public void Leave_StopsTheListener_AndIgnoresALateSuccess()
    {
        var audio = new FakeMic();
        var session = new TunerListeningSession();
        Assert.True(session.TryBeginStart(isCapturing: false));
        int generation = session.Generation;
        audio.Start();

        session.Leave();
        audio.Stop();
        session.CompleteStart(generation, captureIsRunning: true);

        Assert.False(session.IsListening);
        Assert.Equal(0, session.ActiveSessions);
        Assert.False(audio.IsCapturing);
        Assert.Equal(0, audio.Live);
    }

    private static void Appear(TunerListeningSession session, FakeMic audio, bool autoStart)
    {
        if (!session.ShouldAutoStartOnAppear(autoStart, audio.IsCapturing, false, false))
            return;
        GoOrBegin(session, audio);
    }

    private static void GoOrBegin(TunerListeningSession session, FakeMic audio)
    {
        if (session.ShouldStopOnGo(audio.IsCapturing))
        {
            audio.Stop();
            session.MarkStopped();
            return;
        }

        if (!session.TryBeginStart(audio.IsCapturing))
            return;

        int generation = session.Generation;
        audio.Stop();
        bool started = audio.TryStart();
        session.CompleteStart(generation, started && audio.IsCapturing);
    }

    private sealed class FakeMic
    {
        public bool Succeed { get; set; } = true;
        public int StartCount { get; private set; }
        public int Live { get; private set; }
        public bool IsCapturing => Live > 0;

        public bool TryStart()
        {
            StartCount++;
            if (!Succeed)
            {
                Live = 0;
                return false;
            }

            Live = 1;
            return true;
        }

        public void Start() => TryStart();

        public void Stop() => Live = 0;
    }
}
