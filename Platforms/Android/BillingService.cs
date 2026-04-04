#if ANDROID
using Android.App;
using Android.BillingClient.Api;
using static Android.BillingClient.Api.BillingFlowParams;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MusicMate.Platforms.Android
{
    public class BillingService : Java.Lang.Object, IPurchasesUpdatedListener
    {
        private BillingClient billingClient;
        private Activity activity;

        public BillingService(Activity activity)
        {
            this.activity = activity;
#pragma warning disable CS0618 // Type or member is obsolete
            billingClient = BillingClient.NewBuilder(activity)
                .EnablePendingPurchases()
                .SetListener(this)
                .Build();
#pragma warning restore CS0618 // Type or member is obsolete
        }

        public Task<bool> StartConnectionAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            billingClient.StartConnection(new BillingClientStateListenerImpl(
                billingResult => tcs.SetResult(billingResult.ResponseCode == 0),
                () => tcs.SetResult(false)
            ));
            return tcs.Task;
        }

        public Task<IList<ProductDetails>> QueryProductsAsync(IEnumerable<string> productIds)
        {
            var tcs = new TaskCompletionSource<IList<ProductDetails>>();
            var paramsBuilder = QueryProductDetailsParams.NewBuilder();
            var products = new List<QueryProductDetailsParams.Product>();
            foreach (var id in productIds)
            {
                products.Add(QueryProductDetailsParams.Product.NewBuilder()
                    .SetProductId(id)
                    .SetProductType(BillingClient.IProductType.Inapp)
                    .Build());
            }
            paramsBuilder.SetProductList(products);

            billingClient.QueryProductDetailsAsync(paramsBuilder.Build(), new ProductDetailsResponseListenerImpl(
                (billingResult, productDetailsList) =>
                {
                    tcs.SetResult(productDetailsList ?? new List<ProductDetails>());
                }
            ));
            return tcs.Task;
        }

        public void LaunchPurchaseFlow(ProductDetails productDetails)
        {
            var paramsBuilder = BillingFlowParams.NewBuilder()
                .SetProductDetailsParamsList(new List<ProductDetailsParams> {
                    ProductDetailsParams.NewBuilder()
                        .SetProductDetails(productDetails)
                        .Build()
                });
            billingClient.LaunchBillingFlow(activity, paramsBuilder.Build());
        }

        public void OnPurchasesUpdated(BillingResult billingResult, IList<Purchase>? purchases)
        {
            if (billingResult.ResponseCode == 0 && purchases != null)
            {
                foreach (var purchase in purchases)
                {
                    // TODO: Grant entitlement and acknowledge purchase
                }
            }
            else if (billingResult.ResponseCode == 1)
            {
                // Handle user cancellation
            }
            else
            {
                // Handle other errors
            }
        }

        private class BillingClientStateListenerImpl : Java.Lang.Object, IBillingClientStateListener
        {
            private readonly Action<BillingResult> _onSetupFinished;
            private readonly Action _onDisconnected;

            public BillingClientStateListenerImpl(Action<BillingResult> onSetupFinished, Action onDisconnected)
            {
                _onSetupFinished = onSetupFinished;
                _onDisconnected = onDisconnected;
            }

            public void OnBillingSetupFinished(BillingResult billingResult) => _onSetupFinished(billingResult);
            public void OnBillingServiceDisconnected() => _onDisconnected();
        }

        private class ProductDetailsResponseListenerImpl : Java.Lang.Object, IProductDetailsResponseListener
        {
            private readonly Action<BillingResult, IList<ProductDetails>> _onResponse;

            public ProductDetailsResponseListenerImpl(Action<BillingResult, IList<ProductDetails>> onResponse)
            {
                _onResponse = onResponse;
            }

            public void OnProductDetailsResponse(BillingResult billingResult, IList<ProductDetails>? productDetailsList)
                => _onResponse(billingResult, productDetailsList ?? new List<ProductDetails>());
        }
    }
}
#endif
