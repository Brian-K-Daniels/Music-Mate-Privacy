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
        private const string PremiumKey      = "IsPremium";
        private const string PremiumProductId = "premium";

        private BillingClient? _billingClient;
        private TaskCompletionSource<bool>? _purchaseTcs;

        // ── IStoreService ────────────────────────────────────────────────────

        public async Task InitializeAsync()
        {
            _billingClient = BuildClient();
            await ConnectAsync();

            // Sync premium flag from the Play store on every cold start
            await SyncPurchasesAsync();
        }

        public async Task<bool> IsPurchasedAsync(string productId)
        {
            // First check the local cached flag for speed
            if (Preferences.Get(PremiumKey, false))
                return true;

            // Fall back to a live Play store query
            await EnsureConnectedAsync();
            return await QueryPurchasedAsync(productId);
        }

        public async Task<bool> PurchaseAsync(string productId)
        {
            await EnsureConnectedAsync();

            var products = await QueryProductDetailsAsync(new[] { productId });
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
            bool owned = await QueryPurchasedAsync(PremiumProductId);
            MainThread.BeginInvokeOnMainThread(() =>
                StatusService.Instance.IsPremiumUser = owned);
            return owned;
        }

        // ── IPurchasesUpdatedListener ────────────────────────────────────────

        public void OnPurchasesUpdated(BillingResult billingResult, IList<Purchase>? purchases)
        {
            if (billingResult.ResponseCode == 0 && purchases != null)
            {
                foreach (var purchase in purchases)
                {
                    if (purchase.PurchaseState == 1 /* Purchased */)
                    {
                        AcknowledgePurchase(purchase);
                        _purchaseTcs?.TrySetResult(true);
                        return;
                    }
                }
            }
            _purchaseTcs?.TrySetResult(false);
        }

        // ── private helpers ──────────────────────────────────────────────────

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
                ()     => tcs.TrySetResult(false)));
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

        private async Task SyncPurchasesAsync()
        {
            bool owned = await QueryPurchasedAsync(PremiumProductId);
            // Update via StatusService so PropertyChanged fires and all bound ViewModels update.
            MainThread.BeginInvokeOnMainThread(() =>
                StatusService.Instance.IsPremiumUser = owned);
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
