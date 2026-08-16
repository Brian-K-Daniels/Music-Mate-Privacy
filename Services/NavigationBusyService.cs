using musicmate.Utilities;

namespace musicmate.Services
{
    /// <summary>
    /// Single app-wide navigation busy overlay. AppShell begins/ends around Shell
    /// Navigating/Navigated so pages do not each own spinner logic.
    /// Show is deferred briefly so instantaneous transitions never flash a spinner.
    /// </summary>
    public sealed class NavigationBusyService
    {
        public static NavigationBusyService Instance { get; } = new();

        /// <summary>Delay before showing; cancelled if navigation finishes sooner.</summary>
        private const int ShowDelayMs = 120;
        /// <summary>Hard cap so a missed Navigated event cannot leave the overlay forever.</summary>
        private const int MaxBusyMs = 20000;

        private readonly object _gate = new();
        private int _depth;
        private int _generation;
        private CancellationTokenSource? _showCts;

        private Grid? _overlay;
        private ActivityIndicator? _indicator;
        private ContentPage? _wrappedPage;
        private View? _originalContent;
        private bool _didWrap;

        private NavigationBusyService() { }

        /// <summary>True while a Begin has not yet been fully ended/reset.</summary>
        public bool IsBusy
        {
            get { lock (_gate) return _depth > 0; }
        }

        /// <summary>Call when a Shell navigation starts (once per in-flight navigation).</summary>
        public void Begin()
        {
            CancellationToken token;
            int gen;

            lock (_gate)
            {
                _depth++;
                if (_depth > 1)
                    return;

                _showCts?.Cancel();
                _showCts?.Dispose();
                _showCts = new CancellationTokenSource();
                token = _showCts.Token;
                gen = ++_generation;
            }

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

                    // Attach only if this Begin is still the active navigation.
                    // Re-check after attach so a concurrent End/Reset cannot leave a stuck overlay.
                    if (!IsCurrent(gen))
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

                    if (!IsCurrent(gen))
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

        /// <summary>Call when Shell navigation completes or fails.</summary>
        public void End()
        {
            lock (_gate)
            {
                if (_depth > 0)
                    _depth--;

                if (_depth > 0)
                    return;

                InvalidatePendingShow_NoLock();
            }

            HideOnMainThread();
        }

        /// <summary>Force-clear if navigation is abandoned without a matching End.</summary>
        public void Reset()
        {
            lock (_gate)
            {
                _depth = 0;
                InvalidatePendingShow_NoLock();
            }

            HideOnMainThread();
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

                    if (!IsCurrent(gen))
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

        private void InvalidatePendingShow_NoLock()
        {
            _generation++;
            _showCts?.Cancel();
            _showCts?.Dispose();
            _showCts = null;
        }

        private bool IsCurrent(int gen)
        {
            lock (_gate)
                return gen == _generation && _depth > 0;
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
