using System.Threading.Tasks;

namespace musicmate.Services
{
    public interface IStoreService
    {
        Task InitializeAsync();
        Task<bool> IsPurchasedAsync(string productId);
        Task<bool> PurchaseAsync(string productId);
        Task<bool> RestorePurchasesAsync();
        /// <summary>
        /// Queries the store and updates <see cref="StatusService.Instance"/>.
        /// In Release, a "not owned" result clears any stale local premium flag.
        /// Call this on page appearing to keep premium UI in sync.
        /// </summary>
        Task<bool> CheckPremiumStatusAsync();
    }
}
