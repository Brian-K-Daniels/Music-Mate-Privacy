namespace musicmate.Services;

/// <summary>Outcome of a Google Play / store purchase attempt.</summary>
public readonly record struct StorePurchaseResult(
    bool Succeeded,
    bool Canceled = false,
    string? UserMessage = null)
{
    public static StorePurchaseResult Success() => new(true);

    public static StorePurchaseResult AlreadyOwned() => new(true);

    public static StorePurchaseResult UserCanceled() => new(false, true);

    public static StorePurchaseResult Failed(string userMessage)
        => new(false, false, userMessage);

    /// <summary>True when the UI should keep the purchase sheet open and show <see cref="UserMessage"/>.</summary>
    public bool ShouldShowError
        => !Succeeded && !Canceled && !string.IsNullOrWhiteSpace(UserMessage);
}
