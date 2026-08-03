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
        private bool _isNavigatingToMusic;

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
        /// Clears the Music navigation re-entry guard (e.g. when What To Play becomes active again).
        /// </summary>
        public void ResetMusicNavigationGuard()
            => _isNavigatingToMusic = false;

        /// <summary>
        /// Shared Music navigation used after a final What To Play activity choice.
        /// Re-entry safe for concurrent calls only while navigation is in flight.
        /// </summary>
        public async Task OpenMusicPageAsync()
        {
            if (_isNavigatingToMusic)
                return;

            _isNavigatingToMusic = true;
            try
            {
                FlyoutIsPresented = false;
                await GoToAsync("//MusicPage");
                FlyoutIsPresented = false;
            }
            catch (Exception ex)
            {
                Utils.Log($"[AppShell] OpenMusicPageAsync ERROR: {ex}");
            }
            finally
            {
                // Clear after navigation finishes so hamburger → Tuner (and later Music
                // picks) are not permanently blocked. Leaving this set stranded users on
                // the blank TunerMenuPage when SelectTunerAndOpenMusicAsync early-returned.
                _isNavigatingToMusic = false;
            }
        }

        /// <summary>
        /// Shared Tuner entry for hamburger flyout and What To Play → Other → Tuner.
        /// Sets Other/Tuner session state via <see cref="PlayModePickerOptions.ApplyOtherSelection"/>,
        /// then opens Music (MusicPage skips By Level / note generation while Tune == Tuner).
        /// </summary>
        public async Task SelectTunerAndOpenMusicAsync()
        {
            if (_isNavigatingToMusic)
                return;

            _isNavigatingToMusic = true;
            try
            {
                LayoutTestTune.SetEnabled(false);
                var session = ServiceHelper.GetService<NoteSessionService>();
                if (session != null)
                    PlayModePickerOptions.ApplyOtherSelection(session, PlayModePickerOptions.Tuner);

                FlyoutIsPresented = false;
                // Let a cancelled flyout navigation finish before switching routes (Android).
                await Task.Delay(50);
                await GoToAsync("//MusicPage");
                FlyoutIsPresented = false;
            }
            catch (Exception ex)
            {
                Utils.Log($"[AppShell] SelectTunerAndOpenMusicAsync ERROR: {ex}");
            }
            finally
            {
                _isNavigatingToMusic = false;
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

            // Always divert Tuner flyout taps. If a Music navigation is already in flight,
            // still cancel the blank gateway route; the in-flight call (or fallback page)
            // will finish opening Music.
            if (e.CanCancel)
                e.Cancel();

            if (_isNavigatingToMusic)
            {
                Utils.Log("[AppShell] Tuner flyout ignored — Music navigation already in progress");
                return;
            }

            Utils.Log("[AppShell] Tuner flyout → SelectTunerAndOpenMusicAsync");

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await SelectTunerAndOpenMusicAsync();
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
