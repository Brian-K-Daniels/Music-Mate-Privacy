namespace musicmate.Services;

/// <summary>
/// Pure purchase-launch and restore decisions shared by GooglePlayStoreService and tests.
/// Does not talk to Play Billing.
/// </summary>
public static class BillingPurchasePreconditions
{
    public const string NotReadyMessage =
        "Google Play Billing is not ready. Check your connection and try again.";

    public const string ProductMissingMessage =
        "Google Play could not load Premium (music_mate_premium). " +
        "The product may not be available for this account yet.";

    public const string NoActivityMessage =
        "Google Play Billing could not open. Return to the app and tap Get Premium again.";

    public const string LaunchFailedMessage =
        "Google Play Billing could not open the purchase sheet. Try again.";

    public const string LaunchTimeoutMessage =
        "Google Play Billing timed out. Try again.";

    /// <summary>
    /// Returns an early result when the billing flow must not be launched.
    /// Null means LaunchBillingFlow should run on the Android main thread.
    /// Existing Premium ownership is restored without launching.
    /// </summary>
    public static StorePurchaseResult? EvaluateLaunch(
        BillingConnectionResult connection,
        bool? existingEntitlement,
        int productCount,
        bool hasActivity)
    {
        if (!connection.Succeeded)
            return StorePurchaseResult.Failed(NotReadyMessage);

        if (existingEntitlement is true)
            return StorePurchaseResult.AlreadyOwned();

        if (productCount <= 0)
            return StorePurchaseResult.Failed(ProductMissingMessage);

        if (!hasActivity)
            return StorePurchaseResult.Failed(NoActivityMessage);

        return null;
    }

    /// <summary>
    /// Restore/CheckPremium: a failed connection or query leaves the current flag unchanged.
    /// </summary>
    public static bool RestoreEntitlement(bool? queryEntitlement, bool currentPremium)
    {
        bool premium = currentPremium;
        PremiumEntitlement.TryApply(queryEntitlement, ref premium);
        return premium;
    }
}
