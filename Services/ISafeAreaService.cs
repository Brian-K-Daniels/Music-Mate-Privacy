namespace musicmate.Services
{
    /// <summary>
    /// Provides platform-specific safe area insets to avoid drawing under
    /// camera cutouts, notches, or other system UI elements.
    /// </summary>
    public interface ISafeAreaService
    {
        /// <summary>
        /// Gets the safe area insets for the current orientation.
        /// Returns (left, top, right, bottom) insets in device-independent pixels.
        /// </summary>
        (float Left, float Top, float Right, float Bottom) GetSafeAreaInsets();
    }
}
