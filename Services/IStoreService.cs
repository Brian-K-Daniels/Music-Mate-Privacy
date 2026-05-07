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
        /// Non-destructive check: queries the store (or local cache) and updates
        /// <see cref="musicmate.Services.StatusService.Instance"/> without clearing any flag.
        /// Call this on page appearing to keep premium UI in sync.
        /// </summary>
        Task<bool> CheckPremiumStatusAsync();
    }
}
