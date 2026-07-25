#if ANDROID
using System.Diagnostics;
using System.Text;
using Android.BillingClient.Api;
using Microsoft.Maui.Storage;
using musicmate.Services;
using static Android.BillingClient.Api.BillingFlowParams;

namespace musicmate.Platforms.Android
{
    /// <summary>
    /// Production IStoreService backed by Google Play Billing (used in Release builds).
    /// Targets Xamarin.Android.Google.BillingClient 8.x.
    /// </summary>
    public class GooglePlayStoreService : Java.Lang.Object, IStoreService, IPurchasesUpdatedListener
    {
        private const string LogTag = "MusicMate.Billing";

        private BillingClient? _billingClient;
        private TaskCompletionSource<bool>? _purchaseTcs;

        // ── IStoreService ────────────────────────────────────────────────────

        public async Task InitializeAsync()
        {
            _billingClient = BuildClient();
            await ConnectAsync();
            // Do not write StatusService here. App.InitializePremiumStatus clears any
            // backup-restored local flag, then calls IsPurchasedAsync as the single source of truth.
        }

        public async Task<bool?> IsPurchasedAsync(string productId)
        {
            // VS / adb sideloads must not auto-inherit Play ownership (license testers,
            // prior purchases). Production Play Store installs still restore normally.
            if (!IsInstalledFromGooglePlay())
            {
                Log("IsPurchasedAsync: not installed from Play Store — returning false (no auto-restore).");
                return false;
            }

            string id = NormalizeProductId(productId);
            var result = await QueryCurrentPurchasesAsync(id);
            return result.Entitlement;
        }

        public async Task<bool> PurchaseAsync(string productId)
        {
            await EnsureConnectedAsync();
            string id = NormalizeProductId(productId);

            var products = await QueryProductDetailsAsync(new[] { id });
            if (products.Count == 0)
                return false;

            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity == null)
                return false;

            _purchaseTcs = new TaskCompletionSource<bool>();

            var flowParams = BillingFlowParams.NewBuilder()
                .SetProductDetailsParamsList(new List<ProductDetailsParams>
                {
                    ProductDetailsParams.NewBuilder()
                        .SetProductDetails(products[0])
                        .Build()
                })
                .Build();

            _billingClient!.LaunchBillingFlow(activity, flowParams);

            // Wait for OnPurchasesUpdated to signal completion (timeout 5 min)
            var completed = await Task.WhenAny(_purchaseTcs.Task,
                                               Task.Delay(TimeSpan.FromMinutes(5)));
            if (completed == _purchaseTcs.Task && _purchaseTcs.Task.Result)
            {
                ApplyPremiumEntitlement(true);
                return true;
            }
            return false;
        }

        public async Task<bool> RestorePurchasesAsync()
        {
            var result = await QueryCurrentPurchasesAsync(PremiumProduct.Id);
            if (result.Entitlement is bool owned)
            {
                ApplyPremiumEntitlement(owned);
                return owned;
            }

            Log("RestorePurchasesAsync: query did not succeed — leaving entitlement unchanged.");
            return StatusService.Instance.IsPremiumUser;
        }

        public async Task<bool> CheckPremiumStatusAsync()
        {
            if (!IsInstalledFromGooglePlay())
            {
                // Sideload: do not pull Play ownership into the session. Leave whatever
                // Buy/Restore already set; App cold-start clears premium for a clean slate.
                Log("CheckPremiumStatusAsync: sideload — leaving entitlement unchanged "
                    + $"(IsPremiumUser={StatusService.Instance.IsPremiumUser}).");
                return StatusService.Instance.IsPremiumUser;
            }

            var result = await QueryCurrentPurchasesAsync(PremiumProduct.Id);
            if (result.Entitlement is bool owned)
            {
                ApplyPremiumEntitlement(owned);
                return owned;
            }

            Log("CheckPremiumStatusAsync: query did not succeed — leaving entitlement unchanged "
                + $"(IsPremiumUser={StatusService.Instance.IsPremiumUser}).");
            return StatusService.Instance.IsPremiumUser;
        }

        // ── IPurchasesUpdatedListener ────────────────────────────────────────

        public void OnPurchasesUpdated(BillingResult billingResult, IList<Purchase>? purchases)
        {
            var code = billingResult.ResponseCode;
            if (code == BillingResponseCode.Ok && purchases != null)
            {
                foreach (var purchase in purchases)
                {
                    if (purchase.PurchaseState == PurchaseState.Purchased &&
                        purchase.Products.Contains(PremiumProduct.Id))
                    {
                        // Persist entitlement before acknowledging so it survives a crash
                        ApplyPremiumEntitlement(true);
                        AcknowledgePurchase(purchase);
                        _purchaseTcs?.TrySetResult(true);
                        Log($"OnPurchasesUpdated: Premium purchased (responseCode={code}).");
                        return;
                    }
                }
            }
            else
            {
                Log($"OnPurchasesUpdated: no Premium grant (responseCode={code}, "
                    + $"purchases={(purchases == null ? "null" : purchases.Count.ToString())}).");
            }
            _purchaseTcs?.TrySetResult(false);
        }

        // ── private helpers ──────────────────────────────────────────────────

        /// <summary>
        /// True when the APK was installed from the Play Store. Visual Studio / adb
        /// deployments report a different installer and are treated as sideloads.
        /// </summary>
        internal static bool IsInstalledFromGooglePlay()
        {
            try
            {
                var context = global::Android.App.Application.Context;
                var pm = context.PackageManager;
                if (pm == null)
                    return false;

                const string playStore = "com.android.vending";
                if (OperatingSystem.IsAndroidVersionAtLeast(30))
                {
                    var info = pm.GetInstallSourceInfo(context.PackageName!);
                    return string.Equals(info?.InstallingPackageName, playStore, StringComparison.Ordinal)
                        || string.Equals(info?.InitiatingPackageName, playStore, StringComparison.Ordinal);
                }

#pragma warning disable CS0618
                var installer = pm.GetInstallerPackageName(context.PackageName!);
#pragma warning restore CS0618
                return string.Equals(installer, playStore, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizeProductId(string productId)
            => string.Equals(productId, PremiumProduct.Id, StringComparison.Ordinal)
                ? PremiumProduct.Id
                // Legacy callers passed "premium" — map to the Play Console id.
                : string.Equals(productId, "premium", StringComparison.OrdinalIgnoreCase)
                    ? PremiumProduct.Id
                    : productId;

        private BillingClient BuildClient()
        {
            var context = global::Android.App.Application.Context;
            var pendingParams = PendingPurchasesParams.NewBuilder()
                .EnableOneTimeProducts()
                .Build();

            return BillingClient.NewBuilder(context)
                .EnablePendingPurchases(pendingParams)
                .SetListener(this)
                .Build();
        }

        private Task ConnectAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            _billingClient!.StartConnection(new BillingStateListener(
                result =>
                {
                    Log($"Billing setup finished: responseCode={result.ResponseCode}");
                    tcs.TrySetResult(result.ResponseCode == BillingResponseCode.Ok);
                },
                () =>
                {
                    Log("Billing service disconnected.");
                    tcs.TrySetResult(false);
                }));
            return tcs.Task;
        }

        private async Task EnsureConnectedAsync()
        {
            if (_billingClient == null)
                _billingClient = BuildClient();

            if (!_billingClient.IsReady)
                await ConnectAsync();

            // One reconnect attempt if still not ready.
            if (!_billingClient.IsReady)
            {
                Log("Billing client not ready after connect — rebuilding and retrying once.");
                _billingClient = BuildClient();
                await ConnectAsync();
            }
        }

        private async Task<IList<ProductDetails>> QueryProductDetailsAsync(IEnumerable<string> productIds)
        {
            var products = productIds
                .Select(id => QueryProductDetailsParams.Product.NewBuilder()
                    .SetProductId(id)
                    .SetProductType(BillingClient.ProductType.Inapp)
                    .Build())
                .ToList();

            var queryParams = QueryProductDetailsParams.NewBuilder()
                .SetProductList(products)
                .Build();

            // Billing 8.x: Task-based API returns QueryProductDetailsResult.
            var result = await _billingClient!.QueryProductDetailsAsync(queryParams);
            return result.ProductDetailsList ?? (IList<ProductDetails>)new List<ProductDetails>();
        }

        /// <summary>
        /// Current in-app purchases via <c>queryPurchasesAsync</c> (not purchase history).
        /// </summary>
        private async Task<PurchaseQueryResult> QueryCurrentPurchasesAsync(string productId)
        {
            await EnsureConnectedAsync();

            if (_billingClient == null || !_billingClient.IsReady)
            {
                Log("QueryCurrentPurchasesAsync: billing not ready — querySucceeded=false, entitlement unchanged.");
                return PurchaseQueryResult.Failed(BillingResponseCode.ServiceDisconnected);
            }

            var queryParams = QueryPurchasesParams.NewBuilder()
                .SetProductType(BillingClient.ProductType.Inapp)
                .Build();

            var purchasesResult = await _billingClient.QueryPurchasesAsync(queryParams);
            var code = purchasesResult.Result?.ResponseCode ?? BillingResponseCode.Error;
            bool succeeded = code == BillingResponseCode.Ok;
            var purchases = purchasesResult.Purchases;

            var infos = new List<PremiumEntitlement.PurchaseInfo>();
            if (purchases != null)
            {
                foreach (var p in purchases)
                {
                    infos.Add(new PremiumEntitlement.PurchaseInfo(
                        p.Products?.ToList() ?? new List<string>(),
                        (int)p.PurchaseState));
                }
            }

            bool? entitlement = PremiumEntitlement.Resolve(succeeded, infos, productId);
            LogPurchaseQuery(code, succeeded, infos, entitlement);
            return new PurchaseQueryResult(succeeded, code, entitlement);
        }

        private static void ApplyPremiumEntitlement(bool owned)
        {
            // Update StatusService first (setter may Remove the pref when false), then
            // write the authoritative value so a successful empty query persists false.
            MainThread.BeginInvokeOnMainThread(() =>
                StatusService.Instance.IsPremiumUser = owned);
            Preferences.Set(PremiumProduct.PreferenceKey, owned);
            Log($"ApplyPremiumEntitlement: IsPremium={owned}");
        }

        private static void LogPurchaseQuery(
            BillingResponseCode responseCode,
            bool querySucceeded,
            IReadOnlyList<PremiumEntitlement.PurchaseInfo> purchases,
            bool? entitlement)
        {
            var sb = new StringBuilder();
            sb.Append("QueryCurrentPurchasesAsync: responseCode=").Append(responseCode);
            sb.Append(", querySucceeded=").Append(querySucceeded);
            sb.Append(", purchaseCount=").Append(purchases.Count);
            sb.Append(", products=[");
            for (int i = 0; i < purchases.Count; i++)
            {
                if (i > 0) sb.Append("; ");
                var p = purchases[i];
                sb.Append("state=").Append(p.PurchaseState);
                sb.Append(" ids=").Append(string.Join(',', p.ProductIds ?? Array.Empty<string>()));
            }
            sb.Append("], entitlementDecision=");
            sb.Append(entitlement is null ? "unchanged (query failed)" : entitlement.Value.ToString());
            Log(sb.ToString());
        }

        private static void Log(string message)
        {
            string line = $"[{LogTag}] {message}";
            Debug.WriteLine(line);
            try { global::Android.Util.Log.Info(LogTag, message); } catch { /* ignore */ }
        }

        private void AcknowledgePurchase(Purchase purchase)
        {
            if (purchase.IsAcknowledged)
                return;

            var ackParams = AcknowledgePurchaseParams.NewBuilder()
                .SetPurchaseToken(purchase.PurchaseToken)
                .Build();

            _billingClient?.AcknowledgePurchase(ackParams, new AckListener());
        }

        private readonly record struct PurchaseQueryResult(
            bool QuerySucceeded,
            BillingResponseCode ResponseCode,
            bool? Entitlement)
        {
            public static PurchaseQueryResult Failed(BillingResponseCode responseCode)
                => new(false, responseCode, null);
        }

        // ── inner listener shims ─────────────────────────────────────────────

        private class BillingStateListener : Java.Lang.Object, IBillingClientStateListener
        {
            private readonly Action<BillingResult> _onFinished;
            private readonly Action _onDisconnected;
            public BillingStateListener(Action<BillingResult> onFinished, Action onDisconnected)
            { _onFinished = onFinished; _onDisconnected = onDisconnected; }
            public void OnBillingSetupFinished(BillingResult r) => _onFinished(r);
            public void OnBillingServiceDisconnected() => _onDisconnected();
        }

        private class AckListener : Java.Lang.Object, IAcknowledgePurchaseResponseListener
        {
            public void OnAcknowledgePurchaseResponse(BillingResult r) { /* fire-and-forget */ }
        }
    }
}
#endif
