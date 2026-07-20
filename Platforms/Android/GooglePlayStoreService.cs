#if ANDROID
using Android.BillingClient.Api;
using Microsoft.Maui.Storage;
using musicmate.Services;
using static Android.BillingClient.Api.BillingFlowParams;

namespace musicmate.Platforms.Android
{
    /// <summary>
    /// Production IStoreService backed by Google Play Billing (used in Release builds).
    /// </summary>
    public class GooglePlayStoreService : Java.Lang.Object, IStoreService, IPurchasesUpdatedListener
    {
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

        public async Task<bool> IsPurchasedAsync(string productId)
        {
            // VS / adb sideloads must not auto-inherit Play ownership (license testers,
            // prior purchases). Production Play Store installs still restore normally.
            if (!IsInstalledFromGooglePlay())
                return false;

            await EnsureConnectedAsync();
            string id = NormalizeProductId(productId);
            return await QueryPurchasedAsync(id);
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
                MainThread.BeginInvokeOnMainThread(() =>
                    StatusService.Instance.IsPremiumUser = true);
                return true;
            }
            return false;
        }

        public async Task<bool> RestorePurchasesAsync()
        {
            await EnsureConnectedAsync();
            bool owned = await QueryPurchasedAsync(PremiumProduct.Id);
            MainThread.BeginInvokeOnMainThread(() =>
                StatusService.Instance.IsPremiumUser = owned);
            if (!owned)
                Preferences.Remove(PremiumProduct.PreferenceKey);
            return owned;
        }

        public async Task<bool> CheckPremiumStatusAsync()
        {
            if (!IsInstalledFromGooglePlay())
            {
                // Sideload: do not pull Play ownership into the session. Leave whatever
                // Buy/Restore already set; App cold-start clears premium for a clean slate.
                return StatusService.Instance.IsPremiumUser;
            }

            await EnsureConnectedAsync();
            bool owned = await QueryPurchasedAsync(PremiumProduct.Id);
            if (owned)
            {
                Preferences.Set(PremiumProduct.PreferenceKey, true);
                MainThread.BeginInvokeOnMainThread(() =>
                    StatusService.Instance.IsPremiumUser = true);
            }
            else
            {
                // Play says not owned — clear any backup-/DEBUG-restored local flag.
                Preferences.Remove(PremiumProduct.PreferenceKey);
                MainThread.BeginInvokeOnMainThread(() =>
                    StatusService.Instance.IsPremiumUser = false);
            }
            return owned;
        }

        // ── IPurchasesUpdatedListener ────────────────────────────────────────

        public void OnPurchasesUpdated(BillingResult billingResult, IList<Purchase>? purchases)
        {
            if (billingResult.ResponseCode == 0 && purchases != null)
            {
                foreach (var purchase in purchases)
                {
                    if (purchase.PurchaseState == 1 /* Purchased */ &&
                        purchase.Products.Contains(PremiumProduct.Id))
                    {
                        // Persist entitlement before acknowledging so it survives a crash
                        Preferences.Set(PremiumProduct.PreferenceKey, true);
                        MainThread.BeginInvokeOnMainThread(() =>
                            StatusService.Instance.IsPremiumUser = true);
                        AcknowledgePurchase(purchase);
                        _purchaseTcs?.TrySetResult(true);
                        return;
                    }
                }
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
#pragma warning disable CS0618
            return BillingClient.NewBuilder(context)
                .EnablePendingPurchases()
                .SetListener(this)
                .Build();
#pragma warning restore CS0618
        }

        private Task ConnectAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            _billingClient!.StartConnection(new BillingStateListener(
                result => tcs.TrySetResult(result.ResponseCode == 0),
                () => tcs.TrySetResult(false)));
            return tcs.Task;
        }

        private async Task EnsureConnectedAsync()
        {
            if (_billingClient == null)
                _billingClient = BuildClient();

            if (!_billingClient.IsReady)
                await ConnectAsync();
        }

        private Task<IList<ProductDetails>> QueryProductDetailsAsync(IEnumerable<string> productIds)
        {
            var tcs = new TaskCompletionSource<IList<ProductDetails>>();
            var products = productIds
                .Select(id => QueryProductDetailsParams.Product.NewBuilder()
                    .SetProductId(id)
                    .SetProductType(BillingClient.IProductType.Inapp)
                    .Build())
                .ToList();

            var queryParams = QueryProductDetailsParams.NewBuilder()
                .SetProductList(products)
                .Build();

            _billingClient!.QueryProductDetailsAsync(queryParams,
                new ProductDetailsListener((result, list) =>
                    tcs.TrySetResult(list ?? new List<ProductDetails>())));

            return tcs.Task;
        }

        private async Task<bool> QueryPurchasedAsync(string productId)
        {
            var tcs = new TaskCompletionSource<bool>();
            var queryParams = QueryPurchasesParams.NewBuilder()
                .SetProductType(BillingClient.IProductType.Inapp)
                .Build();

            _billingClient!.QueryPurchasesAsync(queryParams,
                new PurchasesListener((result, purchases) =>
                {
                    bool found = purchases?.Any(p =>
                        p.Products.Contains(productId) &&
                        p.PurchaseState == 1 /* Purchased */) ?? false;
                    tcs.TrySetResult(found);
                }));

            return await tcs.Task;
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

        private class ProductDetailsListener : Java.Lang.Object, IProductDetailsResponseListener
        {
            private readonly Action<BillingResult, IList<ProductDetails>?> _cb;
            public ProductDetailsListener(Action<BillingResult, IList<ProductDetails>?> cb) => _cb = cb;
            public void OnProductDetailsResponse(BillingResult r, IList<ProductDetails>? list) => _cb(r, list);
        }

        private class PurchasesListener : Java.Lang.Object, IPurchasesResponseListener
        {
            private readonly Action<BillingResult, IList<Purchase>?> _cb;
            public PurchasesListener(Action<BillingResult, IList<Purchase>?> cb) => _cb = cb;
            public void OnQueryPurchasesResponse(BillingResult r, IList<Purchase> list) => _cb(r, list);
        }

        private class AckListener : Java.Lang.Object, IAcknowledgePurchaseResponseListener
        {
            public void OnAcknowledgePurchaseResponse(BillingResult r) { /* fire-and-forget */ }
        }
    }
}
#endif
