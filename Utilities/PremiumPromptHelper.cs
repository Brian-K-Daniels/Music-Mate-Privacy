using CommunityToolkit.Maui.Extensions;
using musicmate.Services;
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
    /// <returns><c>true</c> if the user successfully purchased premium (or already owned it); otherwise <c>false</c>.</returns>
    public static async Task<bool> ShowAsync(Page page, Action? onDecline = null)
    {
        // Native picker dialogs sit on the modal stack and make the Premium popup
        // flash closed before LaunchBillingFlow. Dismiss them first.
        await DismissBlockingPickerAsync(page);

        // USB Release / cold start may not have finished restoring yet — re-check Play first.
        try
        {
            var store = ServiceHelper.GetService<IStoreService>();
            if (store != null && await store.CheckPremiumStatusAsync())
                return true;
        }
        catch
        {
            // Fall through to the purchase popup.
        }

        if (StatusService.Instance.IsPremiumUser)
            return true;

        var popup = new PremiumPopup(onDecline);
        await page.ShowPopupAsync(popup);
        return popup.PurchaseResult;
    }

    private static async Task DismissBlockingPickerAsync(Page page)
    {
        try
        {
            page.Unfocus();
        }
        catch
        {
            // best effort
        }

        try
        {
            if (page.Navigation.ModalStack.Count > 0)
                await page.Navigation.PopModalAsync(animated: false);
        }
        catch
        {
            // The picker may already have closed itself.
        }

        await Task.Delay(80);
    }
}
