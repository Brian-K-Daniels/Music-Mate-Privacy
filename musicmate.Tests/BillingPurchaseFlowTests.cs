using musicmate.Services;
using musicmate.ViewModels;

namespace musicmate.Tests;

public class BillingPurchaseFlowTests
{
    [Fact]
    public void EvaluateLaunch_RestoresAlreadyOwned_WithoutLaunching()
    {
        var result = BillingPurchasePreconditions.EvaluateLaunch(
            BillingConnectionResult.Ready(),
            existingEntitlement: true,
            productCount: 1,
            hasActivity: true);

        Assert.NotNull(result);
        Assert.True(result.Value.Succeeded);
        Assert.False(result.Value.ShouldShowError);
    }

    [Fact]
    public void EvaluateLaunch_AllowsPurchaseSheet_WhenConnectedWithProductAndActivity()
    {
        var result = BillingPurchasePreconditions.EvaluateLaunch(
            BillingConnectionResult.Ready(),
            existingEntitlement: false,
            productCount: 1,
            hasActivity: true);

        Assert.Null(result);
    }

    [Fact]
    public void EvaluateLaunch_FailsClosed_WhenConnectionTimesOut()
    {
        var result = BillingPurchasePreconditions.EvaluateLaunch(
            BillingConnectionResult.TimedOut(),
            existingEntitlement: false,
            productCount: 1,
            hasActivity: true);

        Assert.NotNull(result);
        Assert.True(result.Value.ShouldShowError);
        Assert.Equal(BillingPurchasePreconditions.NotReadyMessage, result.Value.UserMessage);
    }

    [Fact]
    public void RestoreEntitlement_LeavesPremiumUnchanged_WhenQueryFails()
    {
        Assert.True(BillingPurchasePreconditions.RestoreEntitlement(queryEntitlement: null, currentPremium: true));
        Assert.False(BillingPurchasePreconditions.RestoreEntitlement(queryEntitlement: null, currentPremium: false));
    }

    [Fact]
    public void RestoreEntitlement_AppliesSuccessfulQuery()
    {
        Assert.True(BillingPurchasePreconditions.RestoreEntitlement(queryEntitlement: true, currentPremium: false));
        Assert.False(BillingPurchasePreconditions.RestoreEntitlement(queryEntitlement: false, currentPremium: true));
    }

    [Fact]
    public async Task LaunchDispatch_RunsOnMainThread_WhenCalledOffThread()
    {
        bool ranInsideInvoke = false;
        int code = await BillingUiThread.InvokeAsync(
            () => 200,
            isMainThread: () => false,
            invokeOnMainThread: async f =>
            {
                ranInsideInvoke = true;
                await f();
            });

        Assert.Equal(200, code);
        Assert.True(ranInsideInvoke);
    }

    [Fact]
    public async Task LaunchDispatch_RunsInline_WhenAlreadyOnMainThread()
    {
        bool invoked = false;
        int code = await BillingUiThread.InvokeAsync(
            () => 200,
            isMainThread: () => true,
            invokeOnMainThread: async f =>
            {
                invoked = true;
                await f();
            });

        Assert.Equal(200, code);
        Assert.False(invoked);
    }

    [Fact]
    public async Task AboutPurchaseAlert_RunsOnUiThread()
    {
        bool invokedOnMain = false;
        string? seenTitle = null;
        string? seenMessage = null;

        await AboutPageViewModel.ShowPurchaseAlertAsync(
            "Premium",
            BillingPurchasePreconditions.NotReadyMessage,
            displayAlert: (title, message) =>
            {
                seenTitle = title;
                seenMessage = message;
                return Task.CompletedTask;
            },
            isMainThread: () => false,
            invokeOnMainThread: async f =>
            {
                invokedOnMain = true;
                await f();
            });

        Assert.True(invokedOnMain);
        Assert.Equal("Premium", seenTitle);
        Assert.Equal(BillingPurchasePreconditions.NotReadyMessage, seenMessage);
    }

    [Fact]
    public void GooglePlayStoreService_UsesSingleFlightConnectAndMainThreadLaunch()
    {
        string text = File.ReadAllText(FindRepoFile(Path.Combine("Platforms", "Android", "GooglePlayStoreService.cs")));

        Assert.Contains("BillingClientConnectionGate", text);
        Assert.Contains("GetOrCreateClient()", text);
        Assert.Contains("_billingClient ??= BuildClient();", text);
        Assert.Contains("BillingUiThread.InvokeAsync", text);
        Assert.Contains("LaunchBillingFlow(activity, flowParams)", text);
        Assert.Contains("waiting for the in-flight connection (not rebuilding)", text);
        Assert.Contains("connection still in progress — not calling EndConnection", text);
        Assert.DoesNotContain("rebuilding and retrying once", text);
        Assert.DoesNotContain("PurchaseToken}", text);
        Assert.DoesNotContain("purchase.PurchaseToken)", text.Split("AcknowledgePurchase(")[0]);
    }

    [Fact]
    public void ConnectTimeout_IsBoundedAtFifteenSeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), BillingClientConnectionGate.DefaultTimeout);
    }

    [Fact]
    public void GooglePlayStoreService_PreservesEntitlementRestorePath()
    {
        string text = File.ReadAllText(FindRepoFile(Path.Combine("Platforms", "Android", "GooglePlayStoreService.cs")));

        Assert.Contains("already owned — granting Premium without billing flow", text);
        Assert.Contains("ItemAlreadyOwned — restoring Premium from current purchases", text);
        Assert.Contains("QueryCurrentPurchasesAsync", text);
        Assert.Contains("ApplyPremiumEntitlement(owned)", text);
        Assert.Contains("leaving entitlement unchanged", text);
        Assert.Contains("PremiumEntitlement.Resolve", text);
    }

    [Fact]
    public void AboutGetPremium_ShowsErrorsOnUiThread()
    {
        string vm = File.ReadAllText(FindRepoFile(Path.Combine("ViewModels", "AboutPageViewModel.cs")));

        Assert.Contains("BillingUiThread.InvokeAsync", vm);
        Assert.Contains("ShowPurchaseAlertAsync", vm);
        Assert.Contains("result.ShouldShowError", vm);
        Assert.Contains("DisplayAlertAsync", vm);
    }

    private static string FindRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate {relativePath} from {AppContext.BaseDirectory}");
    }
}
