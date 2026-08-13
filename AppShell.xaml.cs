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
        public const string SightTrainingFlyoutTitle = "Interval Sight Training";
        public const string SightTrainingFlyoutRoute = "IntervalSightTraining";

        public ICommand? GoPracticeCommand { get; }
        private bool _isNavigatingToMusic;
        private bool _flyoutPresentedHooked;
        private bool _shellNavInProgress;

        public AppShell()
        {
            try
            {
                InitializeComponent();

                GoPracticeCommand = new Command(async () => await GoToAsync("//HomePage"));
                BindingContext = this;
                Navigating += OnShellNavigating;
                Navigated += OnShellNavigated;
                Loaded += (_, _) =>
                {
                    HookFlyoutPresentedWatch();
                    EnsureFlyoutItemsVisible();
                };

                var theme = ServiceHelper.GetService<ThemeService>();
                if (theme != null)
                {
                    theme.ThemeColorsChanged += (_, _) => theme.ApplyToShellIfAvailable();
                    theme.ApplyToShellIfAvailable();
                }
#if DEBUG
                RegisterDebugFlyoutItem();
#endif
                EnsureFlyoutItemsVisible();
                // All page routes are declared via Route="..." on ShellContent in AppShell.xaml,
                // so no additional Routing.RegisterRoute calls are needed here.  The previous
                // calls silently threw ArgumentException (duplicate route) on every cold start.
            }
            catch (Exception ex)
            {
                Utils.Log($"Error initializing AppShell: {ex.Message}");
            }
        }

        private void HookFlyoutPresentedWatch()
        {
            if (_flyoutPresentedHooked)
                return;
            _flyoutPresentedHooked = true;
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(FlyoutIsPresented) || !FlyoutIsPresented)
                    return;

                // Opening the flyout must never be blocked by a stuck navigation overlay.
                if (_shellNavInProgress || NavigationBusyService.Instance.IsBusy)
                    CompleteShellNavigationBusy();

                EnsureFlyoutItemsVisible();
            };
        }

        /// <summary>
        /// WinUI/Shell can leave a FlyoutItem invisible or title-cleared after visiting its
        /// page (especially with AsSingleItem / null FlyoutIcon templates historically).
        /// Re-assert visibility and titles so menu rows never disappear after use.
        /// </summary>
        private void OnShellNavigated(object? sender, ShellNavigatedEventArgs e)
        {
            try
            {
                CompleteShellNavigationBusy();
            }
            finally
            {
                EnsureFlyoutItemsVisible();
            }
        }

        /// <summary>Public entry for pages that need to re-assert flyout rows after appear.</summary>
        public void EnsureFlyoutItemsVisiblePublic()
            => EnsureFlyoutItemsVisible();

        private void EnsureFlyoutItemsVisible()
        {
            try
            {
                if (FlyoutBehavior != FlyoutBehavior.Flyout)
                    FlyoutBehavior = FlyoutBehavior.Flyout;

                foreach (var item in Items)
                {
                    if (item is not FlyoutItem flyoutItem)
                        continue;

                    EnsureFlyoutItemListed(flyoutItem);
                }

                EnsureSightTrainingFlyoutPersistent();
            }
            catch (Exception ex)
            {
                Utils.Log($"[AppShell] EnsureFlyoutItemsVisible: {ex.Message}");
            }
        }

        /// <summary>
        /// Guarantees exactly one Interval Sight Training flyout row: restore title/visibility
        /// if Shell cleared them, or re-insert the existing named instance if it was dropped
        /// from <see cref="Shell.Items"/> (never allocate a second FlyoutItem).
        /// </summary>
        private void EnsureSightTrainingFlyoutPersistent()
        {
            var sight = ResolveSightTrainingFlyoutItem();
            if (sight == null)
            {
                Utils.Log("[AppShell] Sight Training FlyoutItem missing from shell graph");
                return;
            }

            if (!Items.Contains(sight))
            {
                int insertAt = FindSightTrainingInsertIndex();
                if (insertAt >= 0 && insertAt <= Items.Count)
                    Items.Insert(insertAt, sight);
                else
                    Items.Add(sight);
                Utils.Log("[AppShell] Re-inserted Interval Sight Training FlyoutItem (same instance)");
            }

            sight.Title = SightTrainingFlyoutTitle;
            if (sight.CurrentItem != null && string.IsNullOrWhiteSpace(sight.CurrentItem.Title))
                sight.CurrentItem.Title = SightTrainingFlyoutTitle;

            EnsureFlyoutItemListed(sight);
        }

        private FlyoutItem? ResolveSightTrainingFlyoutItem()
        {
            if (SightTrainingFlyoutItem != null)
                return SightTrainingFlyoutItem;

            foreach (var item in Items)
            {
                if (item is not FlyoutItem flyout)
                    continue;
                if (string.Equals(flyout.Route, SightTrainingFlyoutRoute, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(flyout.Title, SightTrainingFlyoutTitle, StringComparison.OrdinalIgnoreCase))
                {
                    return flyout;
                }

                foreach (var content in flyout.Items)
                {
                    if (content?.Route != null
                        && content.Route.IndexOf("IntervalSightTraining", StringComparison.OrdinalIgnoreCase) >= 0)
                        return flyout;
                }
            }

            return null;
        }

        private int FindSightTrainingInsertIndex()
        {
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i] is FlyoutItem fi
                    && string.Equals(fi.Route, "EarTraining", StringComparison.OrdinalIgnoreCase))
                {
                    return i + 1;
                }
            }

            return Items.Count;
        }

        private static void EnsureFlyoutItemListed(FlyoutItem flyoutItem)
        {
            if (string.IsNullOrWhiteSpace(flyoutItem.Title)
                && flyoutItem.CurrentItem?.Title is { Length: > 0 } contentTitle)
            {
                flyoutItem.Title = contentTitle;
            }

            // Only flip visibility when needed — WinUI can blank titles when IsVisible is
            // toggled true→true via SetFlyoutItemIsVisible repeatedly.
            if (!Shell.GetFlyoutItemIsVisible(flyoutItem))
                Shell.SetFlyoutItemIsVisible(flyoutItem, true);
            if (!flyoutItem.IsVisible)
                flyoutItem.IsVisible = true;
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
                // GoToAsync may no-op when already on Music (no Navigated) — always clear busy.
                CompleteShellNavigationBusy();
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
                // Same-route Music navigation often skips Navigated — clear busy here.
                CompleteShellNavigationBusy();
                _isNavigatingToMusic = false;
            }
        }

        /// <summary>
        /// Hamburger → Tuner is a FlyoutItem for reliable taps, but must not stay on a blank
        /// gateway page. Cancel that navigation and open Music in Tuner mode instead.
        /// Also starts the shared navigation busy overlay and blocks stacked flyout taps.
        /// </summary>
        private void OnShellNavigating(object? sender, ShellNavigatingEventArgs e)
        {
            var target = e.Target?.Location?.OriginalString ?? string.Empty;

            if (target.IndexOf("TunerEntry", StringComparison.OrdinalIgnoreCase) >= 0)
            {
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

                // If a prior busy state got stuck, clear it so Tuner divert can run.
                if (_shellNavInProgress)
                    CompleteShellNavigationBusy();

                Utils.Log("[AppShell] Tuner flyout → SelectTunerAndOpenMusicAsync");

                // Show busy immediately for the known redirect path (Music page can be slow).
                BeginShellNavigationBusy(closeFlyout: true);

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await SelectTunerAndOpenMusicAsync();
                });
                return;
            }

            // Ignore stacked flyout taps while a navigation is already running.
            // Exception: Tuner divert starts busy early, then GoToAsync("//MusicPage") must proceed.
            if (_shellNavInProgress)
            {
                bool isMusicFollowThrough =
                    _isNavigatingToMusic
                    && target.IndexOf("MusicPage", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isMusicFollowThrough)
                    return;

                if (e.CanCancel)
                {
                    e.Cancel();
                    Utils.Log("[AppShell] Navigation ignored — already in progress");
                }

                return;
            }

            BeginShellNavigationBusy(closeFlyout: true);
        }

        private void BeginShellNavigationBusy(bool closeFlyout)
        {
            if (closeFlyout)
                FlyoutIsPresented = false;

            if (_shellNavInProgress)
                return;

            _shellNavInProgress = true;
            NavigationBusyService.Instance.Begin();
        }

        /// <summary>
        /// Clears navigation busy UI and re-entry guards. Safe to call more than once.
        /// </summary>
        private void CompleteShellNavigationBusy()
        {
            _shellNavInProgress = false;
            NavigationBusyService.Instance.Reset();
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
