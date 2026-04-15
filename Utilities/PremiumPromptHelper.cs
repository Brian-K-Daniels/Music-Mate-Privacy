using CommunityToolkit.Maui.Extensions;
using musicmate.Views;

namespace musicmate.Utilities;

/// <summary>
/// Shows the "Premium users only" popup from any Page.
/// Returns true if the user successfully purchased premium.
/// </summary>
public static class PremiumPromptHelper
{
    /// <summary>Shows the premium upsell popup and returns the purchase outcome.</summary>
    /// <param name="page">The page from which to present the popup.</param>
    /// <param name="onDecline">
    /// Optional action invoked by the popup before it closes when the user
    /// taps "No thanks, continue free". Use it to revert whatever triggered this prompt.
    /// </param>
    /// <returns><c>true</c> if the user successfully purchased premium; otherwise <c>false</c>.</returns>
    public static async Task<bool> ShowAsync(Page page, Action? onDecline = null)
    {
        var popup = new PremiumPopup(onDecline);
        await page.ShowPopupAsync(popup);
        return popup.PurchaseResult;
    }
}
