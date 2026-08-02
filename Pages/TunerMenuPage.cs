using musicmate.LayoutDebug;
using musicmate.Services;
using musicmate.Utilities;

namespace musicmate.Pages
{
    /// <summary>
    /// Placeholder for the Tuner flyout item. Preferred path cancels this route in
    /// <c>AppShell</c> and opens Music. This page is a fallback if cancel does not run.
    /// </summary>
    public class TunerMenuPage : ContentPage
    {
        private bool _redirectStarted;

        public TunerMenuPage()
        {
            BackgroundColor = Colors.Transparent;
            Content = new BoxView { Color = Colors.Transparent };
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            if (_redirectStarted)
                return;

            _redirectStarted = true;
            Utils.Log("[TunerMenuPage] Fallback redirect to Music Tuner");

            // Delay so we are not navigating synchronously inside OnAppearing (blank screen on Android).
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), async () =>
            {
                try
                {
                    LayoutTestTune.SetEnabled(false);
                    var session = ServiceHelper.GetService<NoteSessionService>();
                    if (session != null)
                        PlayModePickerOptions.ApplyOtherSelection(session, PlayModePickerOptions.Tuner);

                    if (Shell.Current != null)
                    {
                        Shell.Current.FlyoutIsPresented = false;
                        await Shell.Current.GoToAsync("//MusicPage");
                    }
                }
                catch (Exception ex)
                {
                    Utils.Log($"[TunerMenuPage] ERROR: {ex}");
                    _redirectStarted = false;
                }
            });
        }

        protected override void OnDisappearing()
        {
            _redirectStarted = false;
            base.OnDisappearing();
        }
    }
}
