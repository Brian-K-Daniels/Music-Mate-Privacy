using Android.Content.PM;
using Microsoft.Maui.ApplicationModel;
using musicmate.Services;

namespace musicmate.Services
{
    public partial class OrientationService
    {
        partial void SetLandscapePlatform()
        {
            var activity = Platform.CurrentActivity;
            if (activity != null)
            {
                activity.RequestedOrientation = ScreenOrientation.SensorLandscape;
            }
        }

        partial void AllowAutorotatePlatform()
        {
            var activity = Platform.CurrentActivity;
            if (activity != null)
            {
                activity.RequestedOrientation = ScreenOrientation.Unspecified;
            }
        }
    }
}
