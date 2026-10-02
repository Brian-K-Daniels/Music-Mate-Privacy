using musicmate.Services;

namespace musicmate.Tests;

[Collection("StatusService")]
public class StatusServiceMainThreadTests : IDisposable
{
    private readonly StatusService _status = StatusService.Instance;

    public StatusServiceMainThreadTests()
    {
        _status.ResetTemporaryMessageStateForTests();
        _status.StatusMessage = string.Empty;
    }

    public void Dispose()
    {
        _status.ResetTemporaryMessageStateForTests();
        StatusService.TemporaryMessageDelayOverrideForTests = null;
    }

    [Fact]
    public async Task StatusMessage_SetFromBackgroundThread_DoesNotThrow()
    {
        Exception? caught = null;
        await Task.Run(() =>
        {
            try
            {
                _status.StatusMessage = "E Natural Minor (Assortment by Level)";
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });

        Assert.Null(caught);
        Assert.Equal("E Natural Minor (Assortment by Level)", _status.RawStatusMessage);
    }

    [Fact]
    public async Task ShowTemporaryMessage_RestoreFromBackgroundDelay_DoesNotThrow()
    {
        var delayGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        StatusService.TemporaryMessageDelayOverrideForTests = (_, _) => delayGate.Task;

        _status.StatusMessage = "Prior";
        _status.ShowTemporaryMessage(StatusService.CountInStatusMessage, StatusService.CountInStatusDuration);
        delayGate.SetResult();

        for (int i = 0; i < 20 && _status.RawStatusMessage != "Prior"; i++)
            await Task.Delay(25);

        Assert.Equal("Prior", _status.RawStatusMessage);
    }
}
