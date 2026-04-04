using System.Threading.Tasks;
using Microsoft.Maui.Storage;

namespace musicmate.Services
{
    // A simple local "store" used for initial development/testing.
    // Later replace with a real Google Play implementation.
    public class LocalStoreService : IStoreService
    {
        private const string PremiumKey = "IsPremium";
        public Task InitializeAsync()
        {
            // nothing to initialize for local stub
            return Task.CompletedTask;
        }

        public Task<bool> IsPurchasedAsync(string productId)
        {
            var val = Preferences.Get(PremiumKey, false);
            return Task.FromResult(val);
        }

        public Task<bool> PurchaseAsync(string productId)
        {
            // Simulate a successful one-time purchase and persist locally
            Preferences.Set(PremiumKey, true);
            return Task.FromResult(true);
        }

        public Task<bool> RestorePurchasesAsync()
        {
            // Local stub has no real store to verify against, so clear the local flag
            Preferences.Set(PremiumKey, false);
            return Task.FromResult(false);
        }
    }
}
