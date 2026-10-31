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
        /// <summary>logcat tag for Release billing diagnostics (adb logcat -s MusicMateBilling).</summary>
        private const string LogTag = "MusicMateBilling";

        private BillingClient? _billingClient;
        private TaskCompletionSource<StorePurchaseResult>? _purchaseTcs;
        private readonly BillingClientConnectionGate _connectionGate = new();
        private readonly BillingConnectionHost _host;
        private readonly object _clientLock = new();
        private BillingStateListener? _activeListener;
        private volatile bool _disposed;

        public GooglePlayStoreService()
        {
            _host = new BillingConnectionHost(this);
        }

        // ── IStoreService ────────────────────────────────────────────────────

        public async Task InitializeAsync()
        {
            LogInfo($"InitializeAsync: package={PackageName()}, installerPlay={IsInstalledFromGooglePlay()}");
            GetOrCreateClient();
            var connect = await EnsureConnectedResultAsync().ConfigureAwait(false);
            LogInfo($"InitializeAsync: connect status={connect.Status}, isReady={_billingClient?.IsReady}");
            // Do not write StatusService here. App.InitializePremiumStatus clears any
            // backup-restored local flag, then calls IsPurchasedAsync as the single source of truth.
        }

        public async Task<bool?> IsPurchasedAsync(string productId)
        {
            // Always ask Play Billing for the signed-in account's ownership.
            // Installer package (Play Store vs USB/sideload) must not hide a real purchase —
            // Release USB debugging still needs Premium when the account already owns it.
            if (!IsInstalledFromGooglePlay())
                LogInfo("IsPurchasedAsync: not installed from Play Store (USB/sideload) — still querying Billing.");

            string id = NormalizeProductId(productId);
            var result = await QueryCurrentPurchasesAsync(id);
            return result.Entitlement;
        }

        public async Task<StorePurchaseResult> PurchaseAsync(string productId)
        {
            LogInfo($"PurchaseAsync start: requestedId={productId}, package={PackageName()}, "
                + $"installerPlay={IsInstalledFromGooglePlay()}");
            var connect = await EnsureConnectedResultAsync().ConfigureAwait(false);
            string id = NormalizeProductId(productId);
            LogInfo($"PurchaseAsync: product ID being queried={id}");

            if (!connect.Succeeded || _billingClient == null || !_billingClient.IsReady)
            {
                LogFailure("PurchaseAsync: abort — billing client is not ready "
                    + $"(status={connect.Status}, isReady={_billingClient?.IsReady}).");
                return StorePurchaseResult.Failed(BillingPurchasePreconditions.NotReadyMessage);
            }

            // Already owned (common when reinstalling or USB-deploying a Release build).
            var existing = await QueryCurrentPurchasesAsync(id);
            if (existing.Entitlement is true)
            {
                ApplyPremiumEntitlement(true);
                LogInfo("PurchaseAsync: already owned — granting Premium without billing flow.");
                return StorePurchaseResult.AlreadyOwned();
            }

            var products = await QueryProductDetailsAsync(new[] { id });
            LogInfo($"PurchaseAsync: number of products returned={products.Count}");
            if (products.Count == 0)
            {
                LogFailure("PurchaseAsync: abort — no product details "
                    + $"(package={PackageName()} must match Play Console app that owns '{id}').");
                return StorePurchaseResult.Failed(BillingPurchasePreconditions.ProductMissingMessage);
            }

            _purchaseTcs = new TaskCompletionSource<StorePurchaseResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            BillingResult? launchResult;
            bool hasActivity;
            try
            {
                (launchResult, hasActivity) = await BillingUiThread.InvokeAsync(() =>
                {
                    var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
                    if (activity == null)
                        return ((BillingResult?)null, false);

                    var flowParams = BillingFlowParams.NewBuilder()
                        .SetProductDetailsParamsList(new List<ProductDetailsParams>
                        {
                            ProductDetailsParams.NewBuilder()
                                .SetProductDetails(products[0])
                                .Build()
                        })
                        .Build();

                    return (_billingClient!.LaunchBillingFlow(activity, flowParams), true);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogFailure($"PurchaseAsync: LaunchBillingFlow dispatch failed ({ex.GetType().Name}).");
                return StorePurchaseResult.Failed(BillingPurchasePreconditions.LaunchFailedMessage);
            }

            if (!hasActivity || launchResult == null)
            {
                LogFailure("PurchaseAsync: abort — CurrentActivity is null.");
                return StorePurchaseResult.Failed(BillingPurchasePreconditions.NoActivityMessage);
            }

            LogInfo($"purchase launch response code={launchResult.ResponseCode}, "
                + $"BillingResult debugMessage={launchResult.DebugMessage ?? "(null)"}");
            if (launchResult.ResponseCode != BillingResponseCode.Ok)
            {
                string launchError = string.IsNullOrWhiteSpace(launchResult.DebugMessage)
                    ? launchResult.ResponseCode.ToString()
                    : launchResult.DebugMessage;
                LogFailure($"PurchaseAsync: LaunchBillingFlow failed ({launchError}).");
                return StorePurchaseResult.Failed(BillingPurchasePreconditions.LaunchFailedMessage);
            }

            // Wait for OnPurchasesUpdated to signal completion (timeout 5 min)
            var completed = await Task.WhenAny(_purchaseTcs.Task,
                                               Task.Delay(TimeSpan.FromMinutes(5)));
            if (completed != _purchaseTcs.Task)
            {
                LogFailure("PurchaseAsync: timed out waiting for the purchase callback.");
                return StorePurchaseResult.Failed(BillingPurchasePreconditions.LaunchTimeoutMessage);
            }

            var result = _purchaseTcs.Task.Result;
            if (result.Succeeded)
            {
                ApplyPremiumEntitlement(true);
                LogInfo("PurchaseAsync: purchase callback succeeded — Premium granted.");
                return result;
            }

            if (!result.Canceled)
            {
                LogFailure($"PurchaseAsync: failed ({result.UserMessage ?? "no message"}).");
            }
            return result;
        }

        public async Task<bool> RestorePurchasesAsync()
        {
            var result = await QueryCurrentPurchasesAsync(PremiumProduct.Id);
            if (result.Entitlement is bool owned)
            {
                ApplyPremiumEntitlement(owned);
                return owned;
            }

            LogFailure("RestorePurchasesAsync: query did not succeed — leaving entitlement unchanged.");
            return StatusService.Instance.IsPremiumUser;
        }

        public async Task<bool> CheckPremiumStatusAsync()
        {
            if (!IsInstalledFromGooglePlay())
                LogInfo("CheckPremiumStatusAsync: not installed from Play Store (USB/sideload) — still querying Billing.");

            var result = await QueryCurrentPurchasesAsync(PremiumProduct.Id);
            if (result.Entitlement is bool owned)
            {
                ApplyPremiumEntitlement(owned);
                return owned;
            }

            LogFailure("CheckPremiumStatusAsync: query did not succeed — leaving entitlement unchanged "
                + $"(IsPremiumUser={StatusService.Instance.IsPremiumUser}).");
            return StatusService.Instance.IsPremiumUser;
        }

        // ── IPurchasesUpdatedListener ────────────────────────────────────────

        public void OnPurchasesUpdated(BillingResult billingResult, IList<Purchase>? purchases)
        {
            var code = billingResult.ResponseCode;
            LogInfo($"purchase callback result: responseCode={code}, "
                + $"BillingResult debugMessage={billingResult.DebugMessage ?? "(null)"}, "
                + $"purchaseCount={(purchases == null ? "null" : purchases.Count.ToString())}");

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
                        _purchaseTcs?.TrySetResult(StorePurchaseResult.Success());
                        LogInfo($"OnPurchasesUpdated: Premium purchased (responseCode={code}).");
                        return;
                    }
                }
            }
            else if (code == BillingResponseCode.ItemAlreadyOwned)
            {
                LogInfo("OnPurchasesUpdated: ItemAlreadyOwned — restoring Premium from current purchases.");
                _ = CompleteAlreadyOwnedPurchaseAsync();
                return;
            }
            else if (code == BillingResponseCode.UserCancelled)
            {
                LogInfo($"OnPurchasesUpdated: canceled (responseCode={code}).");
                _purchaseTcs?.TrySetResult(StorePurchaseResult.UserCanceled());
                return;
            }
            else
            {
                LogFailure($"OnPurchasesUpdated: no Premium grant (responseCode={code}, "
                    + $"purchases={(purchases == null ? "null" : purchases.Count.ToString())}).");
            }
            _purchaseTcs?.TrySetResult(StorePurchaseResult.Failed(
                "Google Play Billing could not complete the purchase. Try again."));
        }

        private async Task CompleteAlreadyOwnedPurchaseAsync()
        {
            try
            {
                var result = await QueryCurrentPurchasesAsync(PremiumProduct.Id);
                bool owned = result.Entitlement ?? true;
                ApplyPremiumEntitlement(owned);
                _purchaseTcs?.TrySetResult(owned
                    ? StorePurchaseResult.AlreadyOwned()
                    : StorePurchaseResult.Failed(
                        "Google Play said this item is already owned, but Premium could not be restored."));
            }
            catch (Exception ex)
            {
                LogFailure($"CompleteAlreadyOwnedPurchaseAsync failed: {ex.GetType().Name} — granting Premium.");
                ApplyPremiumEntitlement(true);
                _purchaseTcs?.TrySetResult(StorePurchaseResult.AlreadyOwned());
            }
        }

        // ── private helpers ──────────────────────────────────────────────────

        private static string PackageName()
        {
            try { return global::Android.App.Application.Context.PackageName ?? "(null)"; }
            catch { return "(unavailable)"; }
        }

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

        private Task<BillingConnectionResult> EnsureConnectedResultAsync()
        {
            if (_disposed)
                return Task.FromResult(BillingConnectionResult.Failed("disposed", "Billing client was disposed."));

            GetOrCreateClient();
            return WaitForConnectionAsync();
        }

        private BillingClient GetOrCreateClient()
        {
            lock (_clientLock)
            {
                _billingClient ??= BuildClient();
                return _billingClient;
            }
        }

        private async Task<BillingConnectionResult> WaitForConnectionAsync()
        {
            var result = await _connectionGate.EnsureConnectedAsync(_host).ConfigureAwait(false);
            if (result.Succeeded)
            {
                LogInfo($"EnsureConnectedAsync: status={result.Status}, detail={result.Detail ?? "(none)"}, "
                    + $"isReady={_billingClient?.IsReady}");
            }
            else
            {
                LogFailure($"EnsureConnectedAsync: status={result.Status}, detail={result.Detail ?? "(none)"}, "
                    + $"isReady={_billingClient?.IsReady}");
            }

            return result;
        }

        private async Task EnsureConnectedAsync()
        {
            await EnsureConnectedResultAsync().ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _disposed = true;
                bool connectionInProgress = _connectionGate.IsConnectionInProgress;
                _connectionGate.MarkDisposed();
                if (connectionInProgress)
                {
                    LogFailure("Dispose: BillingClient connection still in progress — not calling EndConnection.");
                }
                else
                {
                    try { _billingClient?.EndConnection(); }
                    catch { /* ignore */ }
                }
            }

            base.Dispose(disposing);
        }

        private Task<(BillingResult Billing, IList<ProductDetails> Products)> QueryProductDetailsWithBillingAsync(
            QueryProductDetailsParams queryParams)
        {
            var tcs = new TaskCompletionSource<(BillingResult, IList<ProductDetails>)>();
            _billingClient!.QueryProductDetails(queryParams, new ProductDetailsListener(
                (billingResult, detailsResult) =>
                {
                    var list = detailsResult?.ProductDetailsList
                        ?? (IList<ProductDetails>)new List<ProductDetails>();
                    tcs.TrySetResult((billingResult, list));
                }));
            return tcs.Task;
        }

        private async Task<IList<ProductDetails>> QueryProductDetailsAsync(IEnumerable<string> productIds)
        {
            var idList = productIds.ToList();
            LogInfo($"product ID being queried=[{string.Join(',', idList)}], package={PackageName()}");

            var products = idList
                .Select(id => QueryProductDetailsParams.Product.NewBuilder()
                    .SetProductId(id)
                    .SetProductType(BillingClient.ProductType.Inapp)
                    .Build())
                .ToList();

            var queryParams = QueryProductDetailsParams.NewBuilder()
                .SetProductList(products)
                .Build();

            // Listener API exposes BillingResult; the Task-only overload returns only product lists.
            var (billingResult, list) = await QueryProductDetailsWithBillingAsync(queryParams);

            LogInfo($"product query response code={billingResult?.ResponseCode}, "
                + $"BillingResult debugMessage={billingResult?.DebugMessage ?? "(null)"}, "
                + $"number of products returned={list.Count}");
            for (int i = 0; i < list.Count; i++)
                Log($"  product[{i}] id={list[i].ProductId}, title={list[i].Title}");

            return list;
        }

        /// <summary>
        /// Current in-app purchases via <c>queryPurchasesAsync</c> (not purchase history).
        /// </summary>
        private async Task<PurchaseQueryResult> QueryCurrentPurchasesAsync(string productId)
        {
            await EnsureConnectedAsync();

            if (_billingClient == null || !_billingClient.IsReady)
            {
                LogFailure("QueryCurrentPurchasesAsync: billing not ready — querySucceeded=false, entitlement unchanged.");
                return PurchaseQueryResult.Failed(BillingResponseCode.ServiceDisconnected);
            }

            var queryParams = QueryPurchasesParams.NewBuilder()
                .SetProductType(BillingClient.ProductType.Inapp)
                .Build();

            var purchasesResult = await _billingClient.QueryPurchasesAsync(queryParams);
            var code = purchasesResult.Result?.ResponseCode ?? BillingResponseCode.Error;
            bool succeeded = code == BillingResponseCode.Ok;
            var purchases = purchasesResult.Purchases;

            LogInfo($"QueryCurrentPurchasesAsync BillingResult: responseCode={code}, "
                + $"debugMessage={purchasesResult.Result?.DebugMessage ?? "(null)"}");

            var infos = new List<PremiumEntitlement.PurchaseInfo>();
            if (purchases != null)
            {
                foreach (var p in purchases)
                {
                    infos.Add(new PremiumEntitlement.PurchaseInfo(
                        p.Products?.ToList() ?? new List<string>(),
                        (int)p.PurchaseState,
                        p.IsAcknowledged,
                        p.PurchaseToken));
                }
            }

            bool? entitlement = PremiumEntitlement.Resolve(succeeded, infos, productId);
            if (succeeded)
                AcknowledgeUnacknowledgedPremium(purchases, infos, productId);
            LogPurchaseQuery(code, succeeded, infos, entitlement);
            return new PurchaseQueryResult(succeeded, code, entitlement);
        }

        /// <summary>
        /// Acknowledges Purchased, not-yet-acknowledged <paramref name="productId"/> rows from this
        /// query. Failures are logged and do not change entitlement; the next successful query retries.
        /// </summary>
        private void AcknowledgeUnacknowledgedPremium(
            IList<Purchase>? purchases,
            IReadOnlyList<PremiumEntitlement.PurchaseInfo> infos,
            string productId)
        {
            IReadOnlyList<string> tokens;
            try
            {
                tokens = PremiumEntitlement.UnacknowledgedPremiumTokens(true, infos, productId);
            }
            catch (Exception ex)
            {
                LogFailure($"acknowledgement selection failed: {ex.Message}");
                return;
            }

            if (tokens.Count == 0 || purchases == null)
                return;

            for (int i = 0; i < purchases.Count; i++)
            {
                try
                {
                    string? token = purchases[i].PurchaseToken;
                    if (string.IsNullOrEmpty(token))
                        continue;

                    bool selected = false;
                    for (int t = 0; t < tokens.Count; t++)
                    {
                        if (string.Equals(tokens[t], token, StringComparison.Ordinal))
                        {
                            selected = true;
                            break;
                        }
                    }

                    if (selected)
                        AcknowledgePurchase(purchases[i]);
                }
                catch (Exception ex)
                {
                    LogFailure($"acknowledgement failed: {ex.Message}");
                }
            }
        }

        private static void ApplyPremiumEntitlement(bool owned)
        {
            // Update StatusService first (setter may Remove the pref when false), then
            // write the authoritative value so a successful empty query persists false.
            void Apply()
            {
                StatusService.Instance.IsPremiumUser = owned;
                Preferences.Set(PremiumProduct.PreferenceKey, owned);
                LogInfo($"ApplyPremiumEntitlement: IsPremium={owned}");
            }

            if (MainThread.IsMainThread)
                Apply();
            else
                MainThread.BeginInvokeOnMainThread(Apply);
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
            LogInfo(sb.ToString());
        }

        [Conditional("DEBUG")]
        private static void Log(string message) => Emit(global::Android.Util.LogPriority.Info, message);

        /// <summary>Connection, launch, and query diagnostics visible in Release logcat (no tokens).</summary>
        private static void LogInfo(string message) => Emit(global::Android.Util.LogPriority.Info, message);

        /// <summary>Billing failure that a published build should still record.</summary>
        private static void LogFailure(string message) => Emit(global::Android.Util.LogPriority.Warn, message);

        private static void Emit(global::Android.Util.LogPriority priority, string message)
        {
            Debug.WriteLine($"[{LogTag}] {message}");
            try { global::Android.Util.Log.WriteLine(priority, LogTag, message); } catch { /* ignore */ }
        }

        private void AcknowledgePurchase(Purchase purchase)
        {
            try
            {
                if (purchase.IsAcknowledged)
                {
                    Log("acknowledgement result: skipped (already acknowledged)");
                    return;
                }

                if (string.IsNullOrEmpty(purchase.PurchaseToken))
                {
                    LogFailure("acknowledgement skipped: empty purchase token");
                    return;
                }

                var ackParams = AcknowledgePurchaseParams.NewBuilder()
                    .SetPurchaseToken(purchase.PurchaseToken)
                    .Build();

                _billingClient?.AcknowledgePurchase(ackParams, new AckListener());
            }
            catch (Exception ex)
            {
                // Leave the purchase unacknowledged so the next successful query can retry.
                LogFailure($"acknowledgement failed: {ex.Message}");
            }
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

        private sealed class BillingConnectionHost : IBillingConnectionHost
        {
            private readonly GooglePlayStoreService _svc;

            public BillingConnectionHost(GooglePlayStoreService svc) => _svc = svc;

            public bool IsReady => _svc._billingClient?.IsReady == true;

            public void BeginStartConnection(IBillingConnectionObserver observer)
            {
                var client = _svc._billingClient
                    ?? throw new InvalidOperationException("BillingClient is null.");

                LogInfo("BillingClient StartConnection: beginning (single in-flight attempt).");
                _svc._activeListener = new BillingStateListener(
                    result =>
                    {
                        string connectMessage =
                            $"BillingClient connection result: responseCode={result.ResponseCode}, "
                            + $"BillingResult debugMessage={result.DebugMessage ?? "(null)"}, "
                            + $"isReady={_svc._billingClient?.IsReady}";

                        bool alreadyConnecting = BillingClientConnectionGate.IsAlreadyConnecting(
                            result.ResponseCode.ToString(),
                            result.DebugMessage);

                        if (result.ResponseCode == BillingResponseCode.Ok)
                            LogInfo(connectMessage);
                        else if (alreadyConnecting)
                            LogFailure(connectMessage + " — waiting for the in-flight connection (not rebuilding).");
                        else
                            LogFailure(connectMessage);

                        observer.OnSetupFinished(
                            result.ResponseCode == BillingResponseCode.Ok,
                            result.ResponseCode.ToString(),
                            result.DebugMessage);
                    },
                    () =>
                    {
                        LogFailure("Billing service disconnected.");
                        observer.OnDisconnected();
                    });

                client.StartConnection(_svc._activeListener);
            }
        }

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
            private readonly Action<BillingResult, QueryProductDetailsResult?> _onResponse;
            public ProductDetailsListener(Action<BillingResult, QueryProductDetailsResult?> onResponse)
                => _onResponse = onResponse;
            public void OnProductDetailsResponse(BillingResult billingResult, QueryProductDetailsResult? detailsResult)
                => _onResponse(billingResult, detailsResult);
        }

        private class AckListener : Java.Lang.Object, IAcknowledgePurchaseResponseListener
        {
            public void OnAcknowledgePurchaseResponse(BillingResult r)
            {
                string message = $"acknowledgement result: responseCode={r.ResponseCode}, "
                    + $"BillingResult debugMessage={r.DebugMessage ?? "(null)"}";
                if (r.ResponseCode == BillingResponseCode.Ok)
                    Log(message);
                else
                    LogFailure(message);
            }
        }
    }
}
#endif
