namespace musicmate.Services;

/// <summary>Outcome of a serialized Google Play BillingClient connection attempt.</summary>
public readonly record struct BillingConnectionResult(
    bool Succeeded,
    string Status,
    string? Detail = null)
{
    public static BillingConnectionResult Ready(string detail = "ready")
        => new(true, "ready", detail);

    public static BillingConnectionResult Failed(string status, string? detail = null)
        => new(false, status, detail);

    public static BillingConnectionResult TimedOut()
        => new(false, "timeout", "OnBillingSetupFinished did not arrive.");

    public static BillingConnectionResult Canceled()
        => new(false, "canceled", "Connection wait was canceled.");
}
