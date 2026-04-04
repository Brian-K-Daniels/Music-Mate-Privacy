using System.Threading.Tasks;

namespace musicmate.Services
{
    public interface IStoreService
    {
        Task InitializeAsync();
        Task<bool> IsPurchasedAsync(string productId);
        Task<bool> PurchaseAsync(string productId);
        Task<bool> RestorePurchasesAsync();
    }
}
