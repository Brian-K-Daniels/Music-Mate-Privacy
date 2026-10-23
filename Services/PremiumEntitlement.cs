namespace musicmate.Services;

/// <summary>
/// Pure Premium entitlement decision from a Google Play current-purchase query.
/// Does not use purchase history — only the current in-app purchase list.
/// </summary>
public static class PremiumEntitlement
{
    /// <summary>Google Play Billing <c>PurchaseState.Purchased</c>.</summary>
    public const int PurchaseStatePurchased = 1;

    /// <summary>Google Play Billing <c>PurchaseState.Pending</c>.</summary>
    public const int PurchaseStatePending = 2;

    /// <summary>
    /// One row from <c>queryPurchasesAsync</c>: product ids, purchase state, and acknowledgement.
    /// </summary>
    /// <param name="IsAcknowledged">Play's acknowledged flag. Omitted rows are treated as not acknowledged.</param>
    /// <param name="PurchaseToken">Token required to acknowledge. Empty when the query did not supply one.</param>
    public readonly record struct PurchaseInfo(
        IReadOnlyList<string> ProductIds,
        int PurchaseState,
        bool IsAcknowledged = false,
        string? PurchaseToken = null);

    /// <summary>
    /// Resolves Premium from a completed purchase query.
    /// </summary>
    /// <param name="querySucceeded">
    /// True only when Billing returned a successful response (e.g. <c>OK</c>).
    /// False for disconnected, timeout, unavailable, or other errors.
    /// </param>
    /// <param name="purchases">Current in-app purchases from the successful query (ignored if not succeeded).</param>
    /// <param name="premiumProductId">Play Console product id (default <see cref="PremiumProduct.Id"/>).</param>
    /// <returns>
    /// <c>true</c> / <c>false</c> when the query succeeded (apply this entitlement);
    /// <c>null</c> when the query failed (do not change the existing entitlement).
    /// </returns>
    public static bool? Resolve(
        bool querySucceeded,
        IEnumerable<PurchaseInfo>? purchases,
        string? premiumProductId = null)
    {
        if (!querySucceeded)
            return null;

        string id = string.IsNullOrEmpty(premiumProductId) ? PremiumProduct.Id : premiumProductId;
        if (purchases == null)
            return false;

        foreach (var purchase in purchases)
        {
            if (OwnsProduct(purchase, id))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Purchase tokens for <paramref name="premiumProductId"/> that are Purchased and not yet
    /// acknowledged. Pending purchases, other products, already-acknowledged purchases, and
    /// rows without a token are omitted. A failed query returns none so a later successful
    /// query can retry. Acknowledgement does not change entitlement.
    /// </summary>
    public static IReadOnlyList<string> UnacknowledgedPremiumTokens(
        bool querySucceeded,
        IEnumerable<PurchaseInfo>? purchases,
        string? premiumProductId = null)
    {
        if (!querySucceeded || purchases == null)
            return Array.Empty<string>();

        string id = string.IsNullOrEmpty(premiumProductId) ? PremiumProduct.Id : premiumProductId;
        var tokens = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var purchase in purchases)
        {
            if (purchase.IsAcknowledged || !OwnsProduct(purchase, id))
                continue;
            if (string.IsNullOrEmpty(purchase.PurchaseToken))
                continue;
            if (seen.Add(purchase.PurchaseToken))
                tokens.Add(purchase.PurchaseToken);
        }

        return tokens;
    }

    private static bool OwnsProduct(PurchaseInfo purchase, string productId)
    {
        if (purchase.PurchaseState != PurchaseStatePurchased || purchase.ProductIds == null)
            return false;

        for (int i = 0; i < purchase.ProductIds.Count; i++)
        {
            if (string.Equals(purchase.ProductIds[i], productId, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Applies a resolved decision to an in-memory entitlement flag.
    /// When <paramref name="decision"/> is null, leaves <paramref name="isPremium"/> unchanged.
    /// </summary>
    /// <returns>True if the entitlement was updated; false if left unchanged.</returns>
    public static bool TryApply(bool? decision, ref bool isPremium)
    {
        if (decision is null)
            return false;

        isPremium = decision.Value;
        return true;
    }
}
