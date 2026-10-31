namespace musicmate.Services;

/// <summary>
/// Host that owns a single BillingClient. The gate never rebuilds or disposes it.
/// </summary>
public interface IBillingConnectionHost
{
    bool IsReady { get; }

    /// <summary>
    /// Invoke BillingClient.StartConnection exactly once per idle→connecting transition.
    /// Must not throw after the Play listener is registered; synchronous throws are treated as start-failed.
    /// </summary>
    void BeginStartConnection(IBillingConnectionObserver observer);
}

/// <summary>Play BillingClient state-listener callbacks forwarded into the gate.</summary>
public interface IBillingConnectionObserver
{
    void OnSetupFinished(bool succeeded, string responseCode, string? debugMessage);
    void OnDisconnected();
}

/// <summary>
/// Ensures only one BillingClient.StartConnection is in flight. Concurrent callers await
/// the same setup task. A missing callback times out the waiter without starting another
/// connection or disposing the client.
/// </summary>
public sealed class BillingClientConnectionGate : IBillingConnectionObserver
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private readonly TimeSpan _timeout;
    private readonly object _lock = new();
    private TaskCompletionSource<BillingConnectionResult>? _setupTcs;
    private bool _startInProgress;
    private bool _disposed;

    public BillingClientConnectionGate(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? DefaultTimeout;
    }

    /// <summary>True while StartConnection has been called and setup has not yet finished or disconnected.</summary>
    public bool IsConnectionInProgress
    {
        get { lock (_lock) return _startInProgress; }
    }

    public Task<BillingConnectionResult> EnsureConnectedAsync(
        IBillingConnectionHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (host.IsReady)
            return Task.FromResult(BillingConnectionResult.Ready("already-ready"));

        Task<BillingConnectionResult> setup = GetOrStartSetupTask(host);
        return WaitForSetupAsync(setup, cancellationToken);
    }

    /// <summary>
    /// Completes in-flight waiters and refuses new StartConnection calls.
    /// Does not dispose the host's BillingClient — the owner must not EndConnection
    /// while <see cref="IsConnectionInProgress"/> is true.
    /// </summary>
    public void MarkDisposed()
    {
        TaskCompletionSource<BillingConnectionResult>? tcs;
        lock (_lock)
        {
            _disposed = true;
            _startInProgress = false;
            tcs = _setupTcs;
        }

        tcs?.TrySetResult(BillingConnectionResult.Failed("disposed", "Billing client was disposed."));
    }

    void IBillingConnectionObserver.OnSetupFinished(bool succeeded, string responseCode, string? debugMessage)
        => OnSetupFinished(succeeded, responseCode, debugMessage);

    void IBillingConnectionObserver.OnDisconnected()
        => OnDisconnected();

    public void OnSetupFinished(bool succeeded, string responseCode, string? debugMessage)
    {
        if (IsAlreadyConnecting(responseCode, debugMessage))
        {
            // Another StartConnection is still in progress. Keep waiters on the original
            // setup task; do not start a second connection and do not complete as failed.
            return;
        }

        BillingConnectionResult result = succeeded
            ? BillingConnectionResult.Ready($"responseCode={responseCode}")
            : BillingConnectionResult.Failed(
                "failed",
                $"responseCode={responseCode}; debugMessage={Sanitize(debugMessage)}");

        CompleteSetup(result);
    }

    public void OnDisconnected()
    {
        CompleteSetup(BillingConnectionResult.Failed("disconnected", "Billing service disconnected."));
    }

    /// <summary>
    /// Play Billing 8 reports DeveloperError with this debug message when StartConnection
    /// is invoked on a client that is already connecting.
    /// </summary>
    public static bool IsAlreadyConnecting(string? responseCode, string? debugMessage)
    {
        if (!string.IsNullOrEmpty(debugMessage)
            && debugMessage.Contains("already in the process of connecting", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrEmpty(debugMessage)
            && debugMessage.Contains("already connecting", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private Task<BillingConnectionResult> GetOrStartSetupTask(IBillingConnectionHost host)
    {
        bool shouldStart = false;
        TaskCompletionSource<BillingConnectionResult> tcs;
        lock (_lock)
        {
            if (_disposed)
                return Task.FromResult(BillingConnectionResult.Failed("disposed", "Billing client was disposed."));

            if (host.IsReady)
                return Task.FromResult(BillingConnectionResult.Ready("already-ready"));

            if (_startInProgress && _setupTcs != null && !_setupTcs.Task.IsCompleted)
                return _setupTcs.Task;

            _setupTcs = new TaskCompletionSource<BillingConnectionResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _startInProgress = true;
            tcs = _setupTcs;
            shouldStart = true;
        }

        if (shouldStart)
        {
            try
            {
                host.BeginStartConnection(this);
            }
            catch (Exception ex)
            {
                CompleteSetup(BillingConnectionResult.Failed("start-failed", ex.GetType().Name));
            }
        }

        return tcs.Task;
    }

    private async Task<BillingConnectionResult> WaitForSetupAsync(
        Task<BillingConnectionResult> setup,
        CancellationToken cancellationToken)
    {
        if (setup.IsCompleted)
            return await setup.ConfigureAwait(false);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);
        try
        {
            return await setup.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return BillingConnectionResult.Canceled();
        }
        catch (OperationCanceledException)
        {
            return BillingConnectionResult.TimedOut();
        }
    }

    private void CompleteSetup(BillingConnectionResult result)
    {
        TaskCompletionSource<BillingConnectionResult>? tcs;
        lock (_lock)
        {
            _startInProgress = false;
            tcs = _setupTcs;
        }

        tcs?.TrySetResult(result);
    }

    private static string Sanitize(string? debugMessage)
    {
        if (string.IsNullOrWhiteSpace(debugMessage))
            return "(none)";

        // Connection diagnostics only — never log tokens or account identifiers.
        string trimmed = debugMessage.Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed[..200];
    }
}
