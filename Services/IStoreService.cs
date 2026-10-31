using System.Threading.Tasks;

namespace musicmate.Services
{
    public interface IStoreService
    {
        Task InitializeAsync();

        /// <summary>
        /// Queries current Play ownership of <paramref name="productId"/>.
        /// Returns <c>true</c>/<c>false</c> when the query succeeded;
        /// <c>null</c> when billing is unavailable/disconnected/errored (do not treat as not owned).
        /// </summary>
        Task<bool?> IsPurchasedAsync(string productId);

        /// <summary>
        /// Starts the store purchase flow. Returns a structured result so the UI can
        /// keep the prompt open and show a reason when Billing does not launch.
        /// </summary>
        Task<StorePurchaseResult> PurchaseAsync(string productId);
        Task<bool> RestorePurchasesAsync();

        /// <summary>
        /// Queries the store and updates <see cref="StatusService.Instance"/> only when
        /// the current-purchase query succeeds. A failed query leaves entitlement unchanged.
        /// Call this on page appearing to keep premium UI in sync.
        /// </summary>
        Task<bool> CheckPremiumStatusAsync();
    }
}
