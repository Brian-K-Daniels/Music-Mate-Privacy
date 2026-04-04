using Foundation;
using Microsoft.Maui.ApplicationModel;
using musicmate.Services;
using UIKit;

namespace musicmate.Platform.ios
{
    public partial class OrientationService
    {
        partial void SetLandscapePlatform()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                UIDevice.CurrentDevice.SetValueForKey(new NSNumber((int)UIInterfaceOrientation.LandscapeLeft), new NSString("orientation"));
                UIViewController.AttemptRotationToDeviceOrientation();
            });
        }

        partial void AllowAutorotatePlatform()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                UIDevice.CurrentDevice.SetValueForKey(new NSNumber((int)UIInterfaceOrientation.Unknown), new NSString("orientation"));
                UIViewController.AttemptRotationToDeviceOrientation();
            });
        }
    }
}
