using musicmate.Utilities;

namespace musicmate.Services
{
    /// <summary>
    /// Single app-wide navigation busy overlay. AppShell begins/ends around Shell
    /// Navigating/Navigated so pages do not each own spinner logic.
    /// Show is deferred so operations that finish in under about 1 second never flash.
    /// </summary>
    public sealed class NavigationBusyService
    {
        public static NavigationBusyService Instance { get; } = new();

        /// <summary>Delay before showing; cancelled if the operation finishes sooner.</summary>
        public const int ShowDelayMs = DelayedBusySession.DefaultShowDelayMs;
        /// <summary>Hard cap so a missed Navigated event cannot leave the overlay forever.</summary>
        private const int MaxBusyMs = 20000;

        private readonly DelayedBusySession _session = new();
        private CancellationTokenSource? _showCts;

        private Grid? _overlay;
        private ActivityIndicator? _indicator;
        private ContentPage? _wrappedPage;
        private View? _originalContent;
        private bool _didWrap;

        private NavigationBusyService() { }

        /// <summary>True while a Begin has not yet been fully ended/reset.</summary>
        public bool IsBusy => _session.IsBusy;

        /// <summary>
        /// Runs <paramref name="operation"/> under the shared delayed spinner.
        /// Nested with Shell navigation: the overlay stays until the outermost End.
        /// Always Ends, including when <paramref name="operation"/> throws.
        /// </summary>
        public async Task RunAsync(Func<Task> operation)
        {
            ArgumentNullException.ThrowIfNull(operation);
            Begin();
            try
            {
                await operation().ConfigureAwait(true);
            }
            finally
            {
                End();
            }
        }

        /// <summary>Call when a Shell navigation or other long UI update starts.</summary>
        public void Begin()
        {
            int gen = _session.Begin(out bool ownsShowTimer);
            if (!ownsShowTimer)
                return;

            CancelPendingShow();
            var cts = new CancellationTokenSource();
            _showCts = cts;
            var token = cts.Token;

            try
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        await Task.Delay(ShowDelayMs, token).ConfigureAwait(true);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    if (!_session.IsCurrent(gen))
                        return;

                    try
                    {
                        AttachAndShow();
                    }
                    catch (Exception ex)
                    {
                        Utils.Log($"[NavigationBusy] Show ERROR: {ex.Message}");
                        return;
                    }

                    if (!_session.IsCurrent(gen))
                    {
                        try { DetachAndHide(); }
                        catch { /* ignore */ }
                    }
                });
            }
            catch (Exception ex)
            {
                Utils.Log($"[NavigationBusy] Show dispatch ERROR: {ex.Message}");
            }

            ArmMaxBusyWatchdog(gen);
        }

        /// <summary>Call when Shell navigation or a wrapped operation completes or fails.</summary>
        public void End()
        {
            if (!_session.End(out _))
                return;

            CancelPendingShow();
            HideOnMainThread();
        }

        /// <summary>Force-clear if navigation is abandoned without a matching End.</summary>
        public void Reset()
        {
            _session.Reset();
            CancelPendingShow();
            HideOnMainThread();
        }

        /// <summary>
        /// Programmatic Shell navigation that always clears the overlay if GoToAsync throws
        /// (Shell may not raise Navigated on failure).
        /// </summary>
        public static async Task GoToAsync(string route)
        {
            try
            {
                if (Shell.Current == null)
                    return;
                await Shell.Current.GoToAsync(route);
            }
            catch
            {
                Instance.Reset();
                throw;
            }
        }

        private void ArmMaxBusyWatchdog(int gen)
        {
            try
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        await Task.Delay(MaxBusyMs).ConfigureAwait(true);
                    }
                    catch
                    {
                        return;
                    }

                    if (!_session.IsCurrent(gen))
                        return;

                    Utils.Log("[NavigationBusy] Max-busy watchdog — forcing Reset");
                    Reset();
                });
            }
            catch (Exception ex)
            {
                Utils.Log($"[NavigationBusy] Watchdog dispatch ERROR: {ex.Message}");
            }
        }

        private void CancelPendingShow()
        {
            _showCts?.Cancel();
            _showCts?.Dispose();
            _showCts = null;
        }

        private void HideOnMainThread()
        {
            try
            {
                if (MainThread.IsMainThread)
                {
                    SafeDetach();
                    return;
                }

                MainThread.BeginInvokeOnMainThread(SafeDetach);
            }
            catch (Exception ex)
            {
                Utils.Log($"[NavigationBusy] Hide dispatch ERROR: {ex.Message}");
                SafeDetach();
            }
        }

        private void SafeDetach()
        {
            try { DetachAndHide(); }
            catch (Exception ex)
            {
                Utils.Log($"[NavigationBusy] Hide ERROR: {ex.Message}");
            }
        }

        private void EnsureOverlay()
        {
            if (_overlay != null)
                return;

            _indicator = new ActivityIndicator
            {
                IsRunning = false,
                WidthRequest = 48,
                HeightRequest = 48,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Color = ResolveIndicatorColor(),
            };

            _overlay = new Grid
            {
                IsVisible = false,
                BackgroundColor = Color.FromArgb("#66000000"),
                ZIndex = 10000,
                InputTransparent = false,
                CascadeInputTransparent = false,
                Children = { _indicator },
            };

            // Absorb taps so rapid menu presses cannot stack while busy.
            _overlay.GestureRecognizers.Add(new TapGestureRecognizer());
        }

        private static Color ResolveIndicatorColor()
        {
            try
            {
                if (Application.Current?.Resources.TryGetValue("Primary", out var primary) == true
                    && primary is Color c)
                    return c;
            }
            catch { /* fall through */ }

            return Colors.White;
        }

        private void AttachAndShow()
        {
            EnsureOverlay();
            if (_overlay == null || _indicator == null)
                return;

            // Already showing on a live host.
            if (_overlay.Parent != null && _overlay.IsVisible)
            {
                _indicator.IsRunning = true;
                return;
            }

            DetachAndHide();

            if (Shell.Current?.CurrentPage is not ContentPage page || page.Content == null)
                return;

            if (page.Content is Grid grid)
            {
                if (!grid.Children.Contains(_overlay))
                    grid.Children.Add(_overlay);
                _didWrap = false;
                _wrappedPage = null;
                _originalContent = null;
            }
            else
            {
                var existing = page.Content;
                var wrap = new Grid();
                page.Content = null;
                wrap.Children.Add(existing);
                wrap.Children.Add(_overlay);
                page.Content = wrap;
                _didWrap = true;
                _wrappedPage = page;
                _originalContent = existing;
            }

            _indicator.Color = ResolveIndicatorColor();
            _indicator.IsRunning = true;
            _overlay.IsVisible = true;
        }

        private void DetachAndHide()
        {
            if (_indicator != null)
                _indicator.IsRunning = false;

            if (_overlay != null)
            {
                _overlay.IsVisible = false;
                if (_overlay.Parent is Layout parentLayout)
                    parentLayout.Children.Remove(_overlay);
            }

            if (_didWrap
                && _wrappedPage != null
                && _originalContent != null
                && _wrappedPage.Content is Grid wrap
                && wrap.Children.Contains(_originalContent))
            {
                wrap.Children.Remove(_originalContent);
                if (_overlay != null && wrap.Children.Contains(_overlay))
                    wrap.Children.Remove(_overlay);
                _wrappedPage.Content = _originalContent;
            }

            _didWrap = false;
            _wrappedPage = null;
            _originalContent = null;
        }
    }
}
