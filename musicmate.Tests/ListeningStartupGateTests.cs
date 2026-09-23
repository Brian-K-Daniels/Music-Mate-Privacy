using musicmate.Services;

namespace musicmate.Tests;

public class ListeningStartupGateTests
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    public void PlayingChrome_RequiresBufferWhileStartupIsActive(
        bool startupActive, bool bufferConfirmed, bool present)
    {
        Assert.Equal(
            present,
            ListeningStartupGate.ShouldPresentPlayingChrome(startupActive, bufferConfirmed));
    }

    [Fact]
    public void Abort_OnlyTheInProgressGeneration()
    {
        Assert.True(ListeningStartupGate.ShouldAbortStartup(
            startupActive: true, chromeShown: false, expectedGeneration: 3, currentGeneration: 3));
        Assert.False(ListeningStartupGate.ShouldAbortStartup(
            startupActive: true, chromeShown: false, expectedGeneration: 3, currentGeneration: 4));
        Assert.False(ListeningStartupGate.ShouldAbortStartup(
            startupActive: true, chromeShown: true, expectedGeneration: 3, currentGeneration: 3));
        Assert.False(ListeningStartupGate.ShouldAbortStartup(
            startupActive: false, chromeShown: false, expectedGeneration: 3, currentGeneration: 3));
    }
}
