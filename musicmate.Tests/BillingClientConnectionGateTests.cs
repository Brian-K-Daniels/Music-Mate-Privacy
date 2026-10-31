using musicmate.Services;

namespace musicmate.Tests;

public class BillingClientConnectionGateTests
{
    [Fact]
    public async Task ConcurrentCallers_ShareOneStartConnection_AndAllSeeSuccess()
    {
        var host = new FakeHost();
        var gate = new BillingClientConnectionGate(TimeSpan.FromSeconds(5));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var callers = Enumerable.Range(0, 24).Select(_ => Task.Run(async () =>
        {
            var wait = gate.EnsureConnectedAsync(host);
            started.TrySetResult();
            return await wait;
        })).ToArray();

        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await WaitForBeginAsync(host);
        Assert.Equal(1, host.BeginCount);
        Assert.True(gate.IsConnectionInProgress);

        host.IsReady = true;
        host.Observer!.OnSetupFinished(true, "Ok", null);

        var results = await Task.WhenAll(callers);
        Assert.All(results, r => Assert.True(r.Succeeded));
        Assert.Equal(1, host.BeginCount);
        Assert.False(gate.IsConnectionInProgress);
    }

    [Fact]
    public async Task AlreadyConnectingResponse_DoesNotStartSecondConnection_AndWaitsForRealSetup()
    {
        var host = new FakeHost { CallbackAlreadyConnectingOnBegin = true };
        var gate = new BillingClientConnectionGate(TimeSpan.FromSeconds(5));

        var first = gate.EnsureConnectedAsync(host);
        await WaitForBeginAsync(host);
        Assert.False(first.IsCompleted);
        Assert.True(gate.IsConnectionInProgress);

        var second = gate.EnsureConnectedAsync(host);
        Assert.Equal(1, host.BeginCount);
        Assert.False(second.IsCompleted);

        host.IsReady = true;
        host.Observer!.OnSetupFinished(true, "Ok", null);

        Assert.True((await first).Succeeded);
        Assert.True((await second).Succeeded);
        Assert.Equal(1, host.BeginCount);
    }

    [Fact]
    public async Task MissingCallback_TimesOut_WithoutStartingAnotherConnection()
    {
        var host = new FakeHost();
        var gate = new BillingClientConnectionGate(TimeSpan.FromMilliseconds(80));

        var first = await gate.EnsureConnectedAsync(host);

        Assert.Equal("timeout", first.Status);
        Assert.False(first.Succeeded);
        Assert.Equal(1, host.BeginCount);
        Assert.True(gate.IsConnectionInProgress);

        var second = await gate.EnsureConnectedAsync(host);
        Assert.Equal("timeout", second.Status);
        Assert.Equal(1, host.BeginCount);
        Assert.True(gate.IsConnectionInProgress);
    }

    [Fact]
    public async Task SuccessfulConnection_ShortCircuitsWhenAlreadyReady()
    {
        var host = new FakeHost { IsReady = true };
        var gate = new BillingClientConnectionGate(TimeSpan.FromSeconds(1));

        var result = await gate.EnsureConnectedAsync(host);

        Assert.True(result.Succeeded);
        Assert.Equal("already-ready", result.Detail);
        Assert.Equal(0, host.BeginCount);
    }

    [Fact]
    public async Task SuccessfulConnection_CompletesWhenSetupFinishes()
    {
        var host = new FakeHost { CallbackSuccessOnBegin = true };
        var gate = new BillingClientConnectionGate(TimeSpan.FromSeconds(1));

        var result = await gate.EnsureConnectedAsync(host);

        Assert.True(result.Succeeded);
        Assert.Equal("ready", result.Status);
        Assert.Equal(1, host.BeginCount);
        Assert.False(gate.IsConnectionInProgress);
    }

    [Fact]
    public async Task DisconnectDuringConnect_FailsWaiters_AndAllowsReconnectOnSameHost()
    {
        var host = new FakeHost();
        var gate = new BillingClientConnectionGate(TimeSpan.FromSeconds(5));

        var first = gate.EnsureConnectedAsync(host);
        await WaitForBeginAsync(host);
        host.Observer!.OnDisconnected();

        var disconnected = await first;
        Assert.False(disconnected.Succeeded);
        Assert.Equal("disconnected", disconnected.Status);
        Assert.False(gate.IsConnectionInProgress);

        host.CallbackSuccessOnBegin = true;
        host.IsReady = false;
        var second = await gate.EnsureConnectedAsync(host);

        Assert.True(second.Succeeded);
        Assert.Equal(2, host.BeginCount);
    }

    [Fact]
    public async Task Cancellation_DoesNotStartAnotherConnection_PendingSetupCanStillSucceed()
    {
        var host = new FakeHost();
        var gate = new BillingClientConnectionGate(TimeSpan.FromSeconds(5));
        using var cts = new CancellationTokenSource();

        var canceledWait = gate.EnsureConnectedAsync(host, cts.Token);
        await WaitForBeginAsync(host);
        cts.Cancel();

        var canceled = await canceledWait;
        Assert.Equal("canceled", canceled.Status);
        Assert.Equal(1, host.BeginCount);
        Assert.True(gate.IsConnectionInProgress);

        var pending = gate.EnsureConnectedAsync(host);
        host.IsReady = true;
        host.Observer!.OnSetupFinished(true, "Ok", null);

        Assert.True((await pending).Succeeded);
        Assert.Equal(1, host.BeginCount);
    }

    [Fact]
    public async Task DisposeWhileConnecting_CompletesWaiters_AndDoesNotStartAgain()
    {
        var host = new FakeHost();
        var gate = new BillingClientConnectionGate(TimeSpan.FromSeconds(5));

        var wait = gate.EnsureConnectedAsync(host);
        await WaitForBeginAsync(host);
        Assert.True(gate.IsConnectionInProgress);

        gate.MarkDisposed();
        var disposed = await wait;
        Assert.Equal("disposed", disposed.Status);
        Assert.False(gate.IsConnectionInProgress);

        var again = await gate.EnsureConnectedAsync(host);
        Assert.Equal("disposed", again.Status);
        Assert.Equal(1, host.BeginCount);
    }

    [Fact]
    public async Task SetupFailure_AllowsALaterRetry()
    {
        var host = new FakeHost();
        var gate = new BillingClientConnectionGate(TimeSpan.FromSeconds(5));

        var first = gate.EnsureConnectedAsync(host);
        await WaitForBeginAsync(host);
        host.Observer!.OnSetupFinished(false, "ServiceUnavailable", "Billing service unavailable.");

        Assert.False((await first).Succeeded);
        Assert.False(gate.IsConnectionInProgress);

        host.CallbackSuccessOnBegin = true;
        Assert.True((await gate.EnsureConnectedAsync(host)).Succeeded);
        Assert.Equal(2, host.BeginCount);
    }

    [Fact]
    public async Task TimeoutThenLateSuccess_DoesNotRebuild_JoinersSeeReady()
    {
        var host = new FakeHost();
        var gate = new BillingClientConnectionGate(TimeSpan.FromMilliseconds(60));

        Assert.Equal("timeout", (await gate.EnsureConnectedAsync(host)).Status);
        Assert.Equal(1, host.BeginCount);

        host.IsReady = true;
        host.Observer!.OnSetupFinished(true, "Ok", null);

        var after = await gate.EnsureConnectedAsync(host);
        Assert.True(after.Succeeded);
        Assert.Equal(1, host.BeginCount);
    }

    [Fact]
    public async Task BeginStartConnectionThrow_FailsWithoutHanging()
    {
        var host = new FakeHost { ThrowOnBegin = new InvalidOperationException("jni") };
        var gate = new BillingClientConnectionGate(TimeSpan.FromSeconds(2));

        var result = await gate.EnsureConnectedAsync(host);

        Assert.Equal("start-failed", result.Status);
        Assert.Contains("InvalidOperationException", result.Detail);
        Assert.False(gate.IsConnectionInProgress);
        Assert.Equal(1, host.BeginCount);
    }

    [Fact]
    public void AlreadyConnecting_DetectsPlayBillingDeveloperErrorMessage()
    {
        Assert.True(BillingClientConnectionGate.IsAlreadyConnecting(
            "DeveloperError",
            "Client is already in the process of connecting to billing service."));
        Assert.True(BillingClientConnectionGate.IsAlreadyConnecting(
            "DeveloperError",
            "already connecting"));
        Assert.False(BillingClientConnectionGate.IsAlreadyConnecting("Ok", null));
        Assert.False(BillingClientConnectionGate.IsAlreadyConnecting("ServiceUnavailable", "Billing unavailable"));
    }

    private static async Task WaitForBeginAsync(FakeHost host)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (host.BeginCount == 0 || host.Observer == null)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("BeginStartConnection was not invoked.");
            await Task.Yield();
        }
    }

    private sealed class FakeHost : IBillingConnectionHost
    {
        public bool IsReady { get; set; }
        public int BeginCount;
        public IBillingConnectionObserver? Observer;
        public bool CallbackAlreadyConnectingOnBegin;
        public bool CallbackSuccessOnBegin;
        public Exception? ThrowOnBegin;

        public void BeginStartConnection(IBillingConnectionObserver observer)
        {
            Interlocked.Increment(ref BeginCount);
            Observer = observer;
            if (ThrowOnBegin != null)
                throw ThrowOnBegin;
            if (CallbackAlreadyConnectingOnBegin)
            {
                observer.OnSetupFinished(
                    false,
                    "DeveloperError",
                    "Client is already in the process of connecting to billing service.");
            }

            if (CallbackSuccessOnBegin)
            {
                IsReady = true;
                observer.OnSetupFinished(true, "Ok", null);
            }
        }
    }
}
