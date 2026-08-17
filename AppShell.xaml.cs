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
        private bool _ensuringFlyoutItems;
        private int _busyGeneration;
        private const int BusyWatchdogSeconds = 20;

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
                // Second pass only checks membership — do not rewrite Titles (MAUI #15827).
                Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), EnsureFlyoutItemsVisible);
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
                // End (not Reset) so nested page OnAppearing busy scopes can keep the overlay.
                FinishShellNavigationBusy();
            }
            finally
            {
                EnsureFlyoutItemsVisible();
                // One delayed membership check after Shell remaps the native flyout.
                Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), EnsureFlyoutItemsVisible);
            }
        }

        /// <summary>Public entry for pages that need to re-assert flyout rows after appear.</summary>
        public void EnsureFlyoutItemsVisiblePublic()
            => EnsureFlyoutItemsVisible();

        private void EnsureFlyoutItemsVisible()
        {
            if (_ensuringFlyoutItems)
                return;

            _ensuringFlyoutItems = true;
            try
            {
                if (FlyoutBehavior != FlyoutBehavior.Flyout)
                    FlyoutBehavior = FlyoutBehavior.Flyout;

                // Only restore flags that are actually off — never rewrite Title (MAUI #15827
                // blanks WinUI flyout rows when Title is assigned at runtime).
                foreach (var item in Items)
                {
                    if (item is FlyoutItem flyoutItem)
                        EnsureFlyoutItemVisibleFlags(flyoutItem);
                }

                EnsureSightTrainingStillInShellItems();
            }
            catch (Exception ex)
            {
                Utils.Log($"[AppShell] EnsureFlyoutItemsVisible: {ex.Message}");
            }
            finally
            {
                _ensuringFlyoutItems = false;
            }
        }

        /// <summary>
        /// Root cause of intermittent vanishing was our own repair path: repeatedly assigning
        /// <see cref="BaseShellItem.Title"/> and pulsing <c>FlyoutItemIsVisible</c> after every
        /// navigation. On WinUI that blanks/removes the row (MAUI #15827). Dynamic recreate
        /// also fails when routes stay registered.
        /// <para>
        /// Fix: treat Sight Training like every other XAML FlyoutItem. Only re-insert the
        /// original instance if Shell dropped it from <see cref="Shell.Items"/> — do not
        /// mutate Title, do not pulse visibility, do not recreate ShellContent.
        /// </para>
        /// </summary>
        private void EnsureSightTrainingStillInShellItems()
        {
            var sight = SightTrainingFlyoutItem;
            if (sight == null)
            {
                Utils.Log("[AppShell] SightTrainingFlyoutItem field is null");
                return;
            }

            // Remove accidental duplicates (same route), keep the XAML-named instance.
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                if (Items[i] is not FlyoutItem other || ReferenceEquals(other, sight))
                    continue;
                if (!IsSightTrainingFlyout(other))
                    continue;
                Items.RemoveAt(i);
                Utils.Log("[AppShell] Removed duplicate Interval Sight Training FlyoutItem");
            }

            if (Items.Contains(sight))
            {
                EnsureFlyoutItemVisibleFlags(sight);
                return;
            }

            int insertAt = FindSightTrainingInsertIndex();
            if (insertAt < 0 || insertAt > Items.Count)
                insertAt = Items.Count;

            try
            {
                Items.Insert(insertAt, sight);
                EnsureFlyoutItemVisibleFlags(sight);
                Utils.Log("[AppShell] Re-inserted Interval Sight Training FlyoutItem (was dropped from Items)");
            }
            catch (Exception ex)
            {
                Utils.Log($"[AppShell] Sight Training re-insert failed: {ex.Message}");
            }
        }

        private static bool IsSightTrainingFlyout(FlyoutItem flyout)
        {
            if (string.Equals(flyout.Route, SightTrainingFlyoutRoute, StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (var content in flyout.Items)
            {
                if (content?.Route != null
                    && content.Route.IndexOf("IntervalSightTraining", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
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

            return Math.Min(Items.Count, 5);
        }

        private static void EnsureFlyoutItemVisibleFlags(FlyoutItem flyoutItem)
        {
            if (!flyoutItem.FlyoutItemIsVisible)
                flyoutItem.FlyoutItemIsVisible = true;
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
                CompleteShellNavigationBusy();
            }
            finally
            {
                // Same-route Music navigation can skip Navigated — clear only if still in-flight.
                // Do not Reset after a successful Navigated: Music OnAppearing may still be busy.
                if (_shellNavInProgress)
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
                CompleteShellNavigationBusy();
            }
            finally
            {
                if (_shellNavInProgress)
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
            var current = CurrentState?.Location?.OriginalString ?? string.Empty;

            if (target.IndexOf("TunerEntry", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Always divert Tuner flyout taps. If a Music navigation is already in flight,
                // still cancel the blank gateway route; the in-flight call (or fallback page)
                // will finish opening Music.
                if (e.CanCancel)
                    e.Cancel();

                if (_isNavigatingToMusic || _shellNavInProgress)
                {
                    Utils.Log("[AppShell] Tuner flyout ignored — navigation already in progress");
                    return;
                }

                Utils.Log("[AppShell] Tuner flyout → SelectTunerAndOpenMusicAsync");

                // Show busy immediately for the known redirect path (Music page can be slow).
                BeginShellNavigationBusy(closeFlyout: true);

                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        await SelectTunerAndOpenMusicAsync();
                    }
                    catch (Exception ex)
                    {
                        Utils.Log($"[AppShell] Tuner divert ERROR: {ex}");
                        CompleteShellNavigationBusy();
                    }
                });
                return;
            }

            // Same flyout destination as the page already showing: close the menu and
            // do not start a navigation. Shell often skips Navigated for this case, which
            // used to leave the spinner on forever (Music → hamburger → Music).
            if (!_isNavigatingToMusic && ShellNavigationTarget.IsSameDestination(current, target))
            {
                if (e.CanCancel)
                    e.Cancel();
                FlyoutIsPresented = false;
                if (_shellNavInProgress || NavigationBusyService.Instance.IsBusy)
                    CompleteShellNavigationBusy();
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
            int gen = ++_busyGeneration;
            NavigationBusyService.Instance.Begin();
            Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(BusyWatchdogSeconds), () =>
            {
                if (gen != _busyGeneration || !_shellNavInProgress)
                    return;
                Utils.Log("[AppShell] Navigation busy watchdog — clearing stuck spinner");
                CompleteShellNavigationBusy(releaseMusicGuard: true);
            });
        }

        /// <summary>
        /// Matching End for a completed Shell navigation. Nested page busy scopes stay active.
        /// </summary>
        private void FinishShellNavigationBusy()
        {
            _busyGeneration++;
            _shellNavInProgress = false;
            NavigationBusyService.Instance.End();
        }

        /// <summary>
        /// Force-clear if navigation is abandoned, cancelled, or stuck. Safe to call more than once.
        /// </summary>
        private void CompleteShellNavigationBusy(bool releaseMusicGuard = false)
        {
            _busyGeneration++;
            _shellNavInProgress = false;
            if (releaseMusicGuard)
                _isNavigatingToMusic = false;
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
