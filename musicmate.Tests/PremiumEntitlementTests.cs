using musicmate.Services;

namespace musicmate.Tests;

public class PremiumEntitlementTests
{
    private static PremiumEntitlement.PurchaseInfo Purchased(params string[] productIds)
        => new(productIds, PremiumEntitlement.PurchaseStatePurchased);

    private static PremiumEntitlement.PurchaseInfo Pending(params string[] productIds)
        => new(productIds, PremiumEntitlement.PurchaseStatePending);

    [Fact]
    public void SuccessfulQuery_WithValidPremiumPurchase_ReturnsTrue()
    {
        var purchases = new[] { Purchased(PremiumProduct.Id) };

        bool? decision = PremiumEntitlement.Resolve(querySucceeded: true, purchases);

        Assert.True(decision);
    }

    [Fact]
    public void SuccessfulQuery_WithNoPremiumPurchase_ReturnsFalse()
    {
        var purchases = new[] { Purchased("some_other_sku") };

        bool? decision = PremiumEntitlement.Resolve(querySucceeded: true, purchases);

        Assert.False(decision);
    }

    [Fact]
    public void SuccessfulQuery_WithEmptyPurchases_ReturnsFalse()
    {
        bool? decision = PremiumEntitlement.Resolve(
            querySucceeded: true,
            purchases: Array.Empty<PremiumEntitlement.PurchaseInfo>());

        Assert.False(decision);
    }

    [Fact]
    public void SuccessfulQuery_WithOnlyPendingPurchase_ReturnsFalse()
    {
        var purchases = new[] { Pending(PremiumProduct.Id) };

        bool? decision = PremiumEntitlement.Resolve(querySucceeded: true, purchases);

        Assert.False(decision);
    }

    [Fact]
    public void FailedOrDisconnectedQuery_ReturnsNull_DoNotRevoke()
    {
        var purchases = Array.Empty<PremiumEntitlement.PurchaseInfo>();

        bool? decision = PremiumEntitlement.Resolve(querySucceeded: false, purchases);

        Assert.Null(decision);
    }

    [Fact]
    public void FailedQuery_DoesNotChangeExistingPremiumTrue()
    {
        bool isPremium = true;
        bool? decision = PremiumEntitlement.Resolve(
            querySucceeded: false,
            purchases: Array.Empty<PremiumEntitlement.PurchaseInfo>());

        bool applied = PremiumEntitlement.TryApply(decision, ref isPremium);

        Assert.False(applied);
        Assert.True(isPremium);
    }

    [Fact]
    public void RestoredLocalPremiumTrue_FollowedBySuccessfulEmptyQuery_BecomesFalse()
    {
        // Simulate Android backup / DEBUG leftover: local flag is true.
        bool isPremium = true;
        PreferencesLikeSet(true);

        bool? decision = PremiumEntitlement.Resolve(
            querySucceeded: true,
            purchases: Array.Empty<PremiumEntitlement.PurchaseInfo>());

        Assert.False(decision);
        Assert.True(PremiumEntitlement.TryApply(decision, ref isPremium));
        Assert.False(isPremium);

        // Successful empty query must persist false, not leave a restored true.
        PreferencesLikeSet(decision!.Value);
        Assert.False(PreferencesLikeGet());
    }

    [Fact]
    public void TryApply_NullDecision_LeavesFlagUnchanged()
    {
        bool isPremium = false;
        Assert.False(PremiumEntitlement.TryApply(null, ref isPremium));
        Assert.False(isPremium);

        isPremium = true;
        Assert.False(PremiumEntitlement.TryApply(null, ref isPremium));
        Assert.True(isPremium);
    }

    [Fact]
    public void PendingAndOtherSku_DoesNotGrantPremium()
    {
        var purchases = new[]
        {
            Pending(PremiumProduct.Id),
            Purchased("unrelated_product"),
        };

        Assert.False(PremiumEntitlement.Resolve(true, purchases));
    }

    // Minimal stand-in for Preferences so the restore→empty-query case is explicit
    // without requiring a MAUI app host.
    private bool _prefIsPremium;

    private void PreferencesLikeSet(bool value) => _prefIsPremium = value;
    private bool PreferencesLikeGet() => _prefIsPremium;
}
