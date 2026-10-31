namespace musicmate.Services
{
    /// <summary>
    /// Fallback safe area service for platforms without cutouts.
    /// Returns zero insets on all sides.
    /// </summary>
    public class FallbackSafeAreaService : ISafeAreaService
    {
        public (float Left, float Top, float Right, float Bottom) GetSafeAreaInsets()
        {
            return (0f, 0f, 0f, 0f);
        }
    }
}
