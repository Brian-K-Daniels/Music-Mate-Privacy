namespace musicmate.Services;

/// <summary>Shared Google Play / store product identifiers for premium entitlement.</summary>
public static class PremiumProduct
{
    /// <summary>In-app product id configured in Google Play Console.</summary>
    public const string Id = "music_mate_premium";

    /// <summary>Local preferences key for cached entitlement (never trust alone in Release).</summary>
    public const string PreferenceKey = "IsPremium";
}
