using Android.Views;
using AndroidX.Core.View;
using musicmate.Services;

namespace musicmate.Platforms.Android
{
    public class SafeAreaService : ISafeAreaService
    {
        public (float Left, float Top, float Right, float Bottom) GetSafeAreaInsets()
        {
            // DisplayCutout / SafeInset* require API 28+; min SDK is 21.
            if (!OperatingSystem.IsAndroidVersionAtLeast(28))
                return ZeroInsets;

            try
            {
                var activity = Platform.CurrentActivity;
                if (activity?.Window == null)
                    return ZeroInsets;

                var rootView = activity.Window.DecorView.RootView;
                if (rootView == null)
                    return ZeroInsets;

                var insets = rootView.RootWindowInsets;
                if (insets == null)
                {
                    // Insets can be null on the first draw before the window is laid out.
                    // Request a fresh pass; return zero rather than guessing a cutout margin.
                    ViewCompat.RequestApplyInsets(rootView);
                    return ZeroInsets;
                }

                var displayCutout = insets.DisplayCutout;
                if (displayCutout == null)
                    return ZeroInsets;

                var density = rootView.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
                if (density <= 0f)
                    density = 1f;

                float left = displayCutout.SafeInsetLeft / density;
                float top = displayCutout.SafeInsetTop / density;
                float right = displayCutout.SafeInsetRight / density;
                float bottom = displayCutout.SafeInsetBottom / density;

                return (left, top, right, bottom);
            }
            catch
            {
                return ZeroInsets;
            }
        }

        private static (float Left, float Top, float Right, float Bottom) ZeroInsets =>
            (0f, 0f, 0f, 0f);
    }
}
