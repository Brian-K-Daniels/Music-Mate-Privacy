using Android.Views;
using musicmate.Services;
using Microsoft.Maui.Controls.Compatibility.Platform.Android;

namespace musicmate.Platforms.Android
{
    public class SafeAreaService : ISafeAreaService
    {
        public (float Left, float Top, float Right, float Bottom) GetSafeAreaInsets()
        {
            try
            {
                var activity = Platform.CurrentActivity;
                if (activity?.Window == null)
                {
                    // Fallback: assume landscape camera cutout on the right
                    return (0f, 0f, 80f, 0f);
                }

                var rootView = activity.Window.DecorView.RootView;
                if (rootView == null)
                {
                    return (0f, 0f, 80f, 0f);
                }

                var insets = rootView.RootWindowInsets;
                if (insets == null)
                {
                    return (0f, 0f, 80f, 0f);
                }

                var displayCutout = insets.DisplayCutout;
                if (displayCutout == null)
                {
                    // No cutout detected
                    return (0f, 0f, 0f, 0f);
                }

                // Convert from pixels to device-independent units
                var density = rootView.Context?.Resources?.DisplayMetrics?.Density ?? 1f;

                float left = displayCutout.SafeInsetLeft / density;
                float top = displayCutout.SafeInsetTop / density;
                float right = displayCutout.SafeInsetRight / density;
                float bottom = displayCutout.SafeInsetBottom / density;

                return (left, top, right, bottom);
            }
            catch
            {
                // Fallback: assume landscape camera cutout on the right
                return (0f, 0f, 80f, 0f);
            }
        }
    }
}
