using musicmate.Services;

namespace musicmate.Tests;

public class CountInSessionGateTests
{
    [Fact]
    public void Go_CountInEnabled_SoundsBeforeListening()
    {
        var gate = new CountInSessionGate();

        Assert.True(gate.TryBegin(countInEnabled: true, tuner: false, noteCount: 8));
        Assert.True(gate.CountInRunning);
        Assert.False(gate.ClicksSounded);
        Assert.False(gate.ListeningStarted);
        Assert.False(gate.FirstNoteIsTarget);
        Assert.False(gate.MayBeginListening());

        gate.OnClickSounded();
        Assert.True(gate.ClicksSounded);
        Assert.True(gate.MayBeginListening());
        Assert.False(gate.ListeningStarted);

        gate.MarkListeningStarted();
        Assert.True(gate.ListeningStarted);
        Assert.True(gate.FirstNoteIsTarget);
        Assert.Equal(0, gate.NoteIndex);
        Assert.False(gate.FirstNoteHeard);
    }

    [Fact]
    public void AutomaticListening_CountInSoundsBeforeListeningBegins()
    {
        var gate = new CountInSessionGate();

        Assert.True(gate.TryBegin(countInEnabled: true, tuner: false, noteCount: 4));
        Assert.False(gate.ListeningStarted);

        gate.OnClickSounded();
        Assert.True(gate.ClicksSounded);
        Assert.False(gate.FirstNoteIsTarget);

        gate.MarkListeningStarted();
        Assert.True(gate.ListeningStarted);
        Assert.True(gate.FirstNoteIsTarget);
    }

    [Fact]
    public void Repeat_EachNewSessionGetsItsOwnCountIn()
    {
        var gate = new CountInSessionGate();

        Assert.True(gate.TryBegin(countInEnabled: true, tuner: false, noteCount: 8));
        gate.OnClickSounded();
        gate.MarkListeningStarted();
        Assert.Equal(0, gate.NoteIndex);
        int firstGeneration = gate.Generation;

        gate.Stop();
        Assert.False(gate.CountInRunning);
        Assert.Equal(0, gate.ActiveSequences);

        Assert.True(gate.TryBegin(countInEnabled: true, tuner: false, noteCount: 8));
        Assert.NotEqual(firstGeneration, gate.Generation);
        Assert.False(gate.ClicksSounded);
        Assert.False(gate.ListeningStarted);
        Assert.Equal(0, gate.NoteIndex);
        Assert.Equal(1, gate.ActiveSequences);

        gate.OnClickSounded();
        Assert.True(gate.MayBeginListening());
        gate.MarkListeningStarted();
        Assert.True(gate.FirstNoteIsTarget);
        Assert.False(gate.FirstNoteHeard);
    }

    [Fact]
    public void StopDuringCountIn_ClearsAllCountInActivity()
    {
        var gate = new CountInSessionGate();
        Assert.True(gate.TryBegin(countInEnabled: true, tuner: false, noteCount: 4));
        gate.OnClickSounded();

        gate.Stop();

        Assert.False(gate.CountInRunning);
        Assert.False(gate.ClicksSounded);
        Assert.False(gate.ListeningStarted);
        Assert.False(gate.MayBeginListening());
        Assert.False(gate.FirstNoteIsTarget);
        Assert.Equal(0, gate.ActiveSequences);
        Assert.Equal(0, gate.ClickCount);
    }

    [Fact]
    public void GoAfterStop_StartsAFreshCountIn()
    {
        var gate = new CountInSessionGate();
        Assert.True(gate.TryBegin(countInEnabled: true, tuner: false, noteCount: 4));
        gate.OnClickSounded();
        int stoppedFrom = gate.Generation;

        gate.Stop();
        Assert.True(gate.TryBegin(countInEnabled: true, tuner: false, noteCount: 4));

        Assert.NotEqual(stoppedFrom, gate.Generation);
        Assert.True(gate.CountInRunning);
        Assert.False(gate.ClicksSounded);
        Assert.False(gate.ListeningStarted);
        Assert.Equal(1, gate.ActiveSequences);

        gate.OnClickSounded();
        Assert.Equal(1, gate.ClickCount);
        Assert.True(gate.MayBeginListening());
    }

    [Fact]
    public void CountInTone_IsNotTheFirstPlayedNote()
    {
        var gate = new CountInSessionGate();
        Assert.True(gate.TryBegin(countInEnabled: true, tuner: false, noteCount: 4));
        gate.OnClickSounded();
        gate.MarkListeningStarted();

        Assert.False(gate.TryAcceptHeardPitch(correct: true, nearCountInTone: true));
        Assert.False(gate.FirstNoteHeard);
        Assert.Equal(0, gate.NoteIndex);
        Assert.True(gate.CountInRunning);

        Assert.False(gate.TryAcceptHeardPitch(correct: true, nearCountInTone: true));
        Assert.True(gate.TryAcceptHeardPitch(correct: true, nearCountInTone: false));
        Assert.True(gate.FirstNoteHeard);
        Assert.False(gate.CountInRunning);
    }

    [Fact]
    public void PitchBeforeListening_IsNotTheFirstNote()
    {
        var gate = new CountInSessionGate();
        Assert.True(gate.TryBegin(countInEnabled: true, tuner: false, noteCount: 4));
        gate.OnClickSounded();

        Assert.False(gate.ListeningStarted);
        Assert.False(gate.TryAcceptHeardPitch(correct: true, nearCountInTone: false));
        Assert.False(gate.FirstNoteHeard);
        Assert.False(gate.FirstNoteIsTarget);
    }

    [Fact]
    public void OnlyOneCountIn_CanRunAtATime()
    {
        var gate = new CountInSessionGate();
        Assert.True(gate.TryBegin(countInEnabled: true, tuner: false, noteCount: 4));
        Assert.False(gate.TryBegin(countInEnabled: true, tuner: false, noteCount: 4));

        Assert.Equal(1, gate.ActiveSequences);
        Assert.Equal(1, gate.Generation);
        Assert.True(gate.CountInRunning);
    }

    [Fact]
    public void ConductorCueMeters_StayFourFourAndSixEight()
    {
        Assert.Equal(4, WaitingCountInLogic.GetBeatsPerMeasure("4/4"));
        Assert.Equal(2, WaitingCountInLogic.GetBeatsPerMeasure("6/8"));
    }
}
