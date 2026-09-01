using musicmate.Services;

namespace musicmate.Tests;

[Collection("StatusService")]
public class StatusServiceTemporaryMessageTests : IDisposable
{
    private readonly StatusService _status = StatusService.Instance;

    public StatusServiceTemporaryMessageTests()
    {
        _status.ResetTemporaryMessageStateForTests();
        _status.StatusMessage = "Prior message";
    }

    public void Dispose()
    {
        _status.ResetTemporaryMessageStateForTests();
        StatusService.TemporaryMessageDelayOverrideForTests = null;
    }

    [Fact]
    public void ShowTemporaryMessage_DisplaysCountInMessageWhenCountInBegins()
    {
        _status.ShowTemporaryMessage(
            StatusService.CountInStatusMessage,
            StatusService.CountInStatusDuration);

        Assert.Equal(StatusService.CountInStatusMessage, _status.RawStatusMessage);
        Assert.True(_status.IsTemporaryMessageActive);
    }

    [Fact]
    public async Task ShowTemporaryMessage_RestoresPriorMessageAfterApproximatelyTwoSeconds()
    {
        var delayGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TimeSpan? capturedDuration = null;
        StatusService.TemporaryMessageDelayOverrideForTests = (duration, _) =>
        {
            capturedDuration = duration;
            return delayGate.Task;
        };

        _status.ShowTemporaryMessage(StatusService.CountInStatusMessage, StatusService.CountInStatusDuration);

        Assert.Equal(TimeSpan.FromSeconds(2), capturedDuration);
        Assert.Equal(StatusService.CountInStatusMessage, _status.RawStatusMessage);

        delayGate.SetResult();
        await Task.Delay(50);
        Assert.Equal("Prior message", _status.RawStatusMessage);
        Assert.False(_status.IsTemporaryMessageActive);
    }

    [Fact]
    public async Task ShowTemporaryMessage_PreservesExactPriorStatusMessage()
    {
        const string prior = "Assortment by Level — Major";
        _status.StatusMessage = prior;

        var delayGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        StatusService.TemporaryMessageDelayOverrideForTests = (_, _) => delayGate.Task;

        _status.ShowTemporaryMessage(StatusService.CountInStatusMessage, StatusService.CountInStatusDuration);
        delayGate.SetResult();
        await Task.Delay(50);

        Assert.Equal(prior, _status.RawStatusMessage);
    }

    [Fact]
    public async Task LegitimateStatusChangeDuringTemporaryDisplay_IsNotLost()
    {
        const string liveFeedback = "Expected: C4, Heard: C4, 0¢, Notes: 5";
        var delayGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        StatusService.TemporaryMessageDelayOverrideForTests = (_, _) => delayGate.Task;

        _status.ShowTemporaryMessage(StatusService.CountInStatusMessage, StatusService.CountInStatusDuration);
        _status.StatusMessage = liveFeedback;

        delayGate.SetResult();
        await Task.Delay(50);

        Assert.Equal(liveFeedback, _status.RawStatusMessage);
        Assert.False(_status.IsTemporaryMessageActive);
    }

    [Fact]
    public async Task RepeatedTemporaryMessages_DoNotRestoreStaleMessageFromEarlierTimer()
    {
        var delayCompletions = new Queue<TaskCompletionSource>();
        StatusService.TemporaryMessageDelayOverrideForTests = (_, _) =>
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            delayCompletions.Enqueue(completion);
            return completion.Task;
        };

        _status.StatusMessage = "Original";
        _status.ShowTemporaryMessage("temp1", TimeSpan.FromSeconds(2));
        _status.ShowTemporaryMessage("temp2", TimeSpan.FromSeconds(2));

        delayCompletions.Dequeue().SetResult();
        await Task.Delay(50);
        Assert.Equal("temp2", _status.RawStatusMessage);

        delayCompletions.Dequeue().SetResult();
        await Task.Delay(50);
        Assert.Equal("Original", _status.RawStatusMessage);
    }
}
