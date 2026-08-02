using System.Windows.Input;
using Microsoft.Maui.Controls;
using musicmate.LayoutDebug;
using musicmate.Pages;
using musicmate.Services;
using musicmate.Utilities;

namespace musicmate
{
    public partial class AppShell : Shell
    {
        public ICommand? GoPracticeCommand { get; }
        private bool _openingTuner;

        public AppShell()
        {
            try
            {
                InitializeComponent();

                GoPracticeCommand = new Command(async () => await GoToAsync("//HomePage"));
                BindingContext = this;
                Navigating += OnShellNavigating;

                var theme = ServiceHelper.GetService<ThemeService>();
                if (theme != null)
                {
                    theme.ThemeColorsChanged += (_, _) => theme.ApplyToShellIfAvailable();
                    theme.ApplyToShellIfAvailable();
                }
#if DEBUG
                RegisterDebugFlyoutItem();
#endif
                // All page routes are declared via Route="..." on ShellContent in AppShell.xaml,
                // so no additional Routing.RegisterRoute calls are needed here.  The previous
                // calls silently threw ArgumentException (duplicate route) on every cold start.
            }
            catch (Exception ex)
            {
                Utils.Log($"Error initializing AppShell: {ex.Message}");
            }
        }

        /// <summary>
        /// Hamburger → Tuner is a FlyoutItem for reliable taps, but must not stay on a blank
        /// gateway page. Cancel that navigation and open Music in Tuner mode instead.
        /// </summary>
        private void OnShellNavigating(object? sender, ShellNavigatingEventArgs e)
        {
            var target = e.Target?.Location?.OriginalString ?? string.Empty;
            if (target.IndexOf("TunerEntry", StringComparison.OrdinalIgnoreCase) < 0)
                return;
            if (_openingTuner || !e.CanCancel)
                return;

            e.Cancel();
            _openingTuner = true;
            Utils.Log("[AppShell] Tuner flyout → apply Tuner + open MusicPage");

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    LayoutTestTune.SetEnabled(false);
                    var session = ServiceHelper.GetService<NoteSessionService>();
                    if (session != null)
                        PlayModePickerOptions.ApplyOtherSelection(session, PlayModePickerOptions.Tuner);

                    FlyoutIsPresented = false;
                    // Let the cancelled flyout navigation finish before switching routes (Android).
                    await Task.Delay(50);
                    await GoToAsync("//MusicPage");
                    FlyoutIsPresented = false;
                }
                catch (Exception ex)
                {
                    Utils.Log($"[AppShell] Tuner navigation ERROR: {ex}");
                }
                finally
                {
                    _openingTuner = false;
                }
            });
        }

#if DEBUG
        private void RegisterDebugFlyoutItem()
        {
            var debugItem = new FlyoutItem
            {
                Title = "Debug Items",
                Route = "DebugItems",
            };
            debugItem.Items.Add(new ShellContent
            {
                Title = "Debug Items",
                Route = "DebugItemsPage",
                ContentTemplate = new DataTemplate(typeof(DebugItemsPage)),
            });
            Items.Add(debugItem);
        }
#endif
    }
}
