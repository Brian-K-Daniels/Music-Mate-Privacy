using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using musicmate.Services;
using musicmate.ViewModels;
using musicmate.Utilities;
using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Text;
using System.IO;
using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace musicmate.Pages
{
    public partial class AboutPage : ContentPage
    {
        private CancellationTokenSource? _aboutSearchDebounceCts;
        private const double DefaultFontSize = 12;
        private readonly ThemeService _themeService;
        private readonly IOrientationService _orientationService;

        // Search state
        private int _aboutMatchCount = 0;
        private int _aboutCurrentIndex = -1;
        private string _aboutSearchQuery = string.Empty;
        private bool _aboutHtmlReady;

        // Track whether we've asked the app to pause listening while searching
        private bool _aboutPausedListening = false;
        private double _aboutLastWebViewWidth = -1;
        private CancellationTokenSource? _aboutWebViewSizeCts;


        public AboutPage()
        {
            InitializeComponent();
            _orientationService = ServiceHelper.GetService<IOrientationService>()!;
            _themeService = ServiceHelper.GetService<ThemeService>()!;
            var vm = new AboutPageViewModel(_themeService);
            BindingContext = vm;
            SyncAboutSearchRowHeight();

            // Reload WebView only when appearance settings change — not on IsPremium, etc.
            // Premium checks were wiping search highlights by reloading the whole document.
            vm.PropertyChanged += OnAboutViewModelPropertyChanged;
            _themeService.PropertyChanged += OnThemeServicePropertyChanged;
            AboutWebView.Navigated += OnAboutWebViewNavigated;
            AboutWebView.SizeChanged += OnAboutWebViewSizeChanged;

            // Ensure Find Next button initial state
            var nextBtn = this.FindByName<Button>("AboutFindNextButton");
            if (nextBtn != null)
                nextBtn.IsEnabled = false;
            var posLbl = this.FindByName<Label>("AboutFindPositionLabel");
            if (posLbl != null)
                posLbl.Text = string.Empty;
        }

        private void OnAboutViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is null
                or nameof(AboutPageViewModel.SelectedFontSize)
                or nameof(AboutPageViewModel.PanelBackgroundColor)
                or nameof(AboutPageViewModel.ContrastingTextColor))
            {
                if (e.PropertyName is null or nameof(AboutPageViewModel.SelectedFontSize))
                    SyncAboutSearchRowHeight();
                _ = LoadAboutHtmlAsync();
            }
        }

        private void OnThemeServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is null
                or nameof(ThemeService.PanelBackgroundColor)
                or nameof(ThemeService.ContrastingTextColor))
            {
                _ = LoadAboutHtmlAsync();
            }
        }

        private void OnAboutWebViewNavigated(object? sender, WebNavigatedEventArgs e)
        {
            _aboutHtmlReady = e.Result == WebNavigationResult.Success;

            if (_aboutHtmlReady)
                _ = SizeAboutWebViewToContentAsync();

            // Re-apply an active query after the document is (re)loaded.
            if (_aboutHtmlReady && !string.IsNullOrEmpty(AboutSearchEntry?.Text))
            {
                var q = AboutSearchEntry.Text;
                _ = SearchAboutPageAsync(q);
            }
        }

        private void OnAboutWebViewSizeChanged(object? sender, EventArgs e)
        {
            if (!_aboutHtmlReady || AboutWebView == null)
                return;

            // Width changes (rotation / window) reflow the HTML; remeasure height.
            if (Math.Abs(AboutWebView.Width - _aboutLastWebViewWidth) < 0.5)
                return;

            _ = SizeAboutWebViewToContentAsync();
        }

        /// <summary>
        /// Sizes the WebView to the full HTML document height so the outer ScrollView
        /// scrolls search, Premium, and the guide together (no nested WebView scrollbar).
        /// </summary>
        private async Task SizeAboutWebViewToContentAsync()
        {
            _aboutWebViewSizeCts?.Cancel();
            _aboutWebViewSizeCts?.Dispose();
            var cts = new CancellationTokenSource();
            _aboutWebViewSizeCts = cts;

            try
            {
                // Let layout + WebView paint settle before reading scrollHeight.
                await Task.Delay(50, cts.Token);

                var web = AboutWebView;
                if (web == null || !_aboutHtmlReady)
                    return;

#if ANDROID
                ApplyAboutWebViewOuterScrollSupport(web);
#endif

                const string js = @"(function(){
        try {
            var body = document.body;
            var html = document.documentElement;
            if (!body || !html) return '0';
            var h = Math.max(
                body.scrollHeight || 0,
                body.offsetHeight || 0,
                html.clientHeight || 0,
                html.scrollHeight || 0,
                html.offsetHeight || 0
            );
            return String(Math.ceil(h));
        } catch (e) {
            return '0';
        }
    })();";

                string? result = null;
                try
                {
                    result = await web.EvaluateJavaScriptAsync(js);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AboutScroll] Height measure failed: {ex.Message}");
                }

                cts.Token.ThrowIfCancellationRequested();

                if (!TryParseJavaScriptInteger(result, out var contentHeight) || contentHeight <= 0)
                    return;

                // CSS px ≈ MAUI dp with device-width viewport (see InjectCssIntoHtml).
                double height = Math.Max(120, contentHeight + 8);
                if (Math.Abs(web.HeightRequest - height) < 1)
                {
                    _aboutLastWebViewWidth = web.Width;
                    return;
                }

                web.HeightRequest = height;
                _aboutLastWebViewWidth = web.Width;

                // Second pass: fonts/layout sometimes settle after the first paint.
                await Task.Delay(150, cts.Token);
                string? result2 = null;
                try
                {
                    result2 = await web.EvaluateJavaScriptAsync(js);
                }
                catch
                {
                    return;
                }

                cts.Token.ThrowIfCancellationRequested();
                if (TryParseJavaScriptInteger(result2, out var contentHeight2)
                    && contentHeight2 > 0
                    && Math.Abs(contentHeight2 - contentHeight) >= 2)
                {
                    web.HeightRequest = Math.Max(120, contentHeight2 + 8);
                }
            }
            catch (OperationCanceledException)
            {
                // A newer measure replaced this one.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AboutScroll] SizeAboutWebViewToContentAsync: {ex.Message}");
            }
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            _orientationService?.ForceLandscape();

            // Font size lives on Settings → Display; reload when returning here.
            if (BindingContext is AboutPageViewModel vm)
                vm.ReloadFontSizeFromPreferences();

            SyncAboutSearchRowHeight();
            _ = LoadAboutHtmlAsync();
            _ = CheckPremiumStatusAsync();
        }

        /// <summary>
        /// Search Entry/Next must be taller than the font: Shell TitleView used to clip
        /// large sizes; the in-page row still needs an explicit height for platform Entry chrome.
        /// </summary>
        private void SyncAboutSearchRowHeight()
        {
            double font = DefaultFontSize;
            if (BindingContext is AboutPageViewModel vm)
                font = vm.SelectedFontSize;

            // Line box (~1.35×) + Entry internal padding + border stroke.
            double entryH = Math.Max(36, Math.Ceiling(font * 1.45) + 16);

            if (AboutSearchEntry != null)
            {
                AboutSearchEntry.HeightRequest = entryH;
                AboutSearchEntry.MinimumHeightRequest = entryH;
            }

            var border = this.FindByName<Border>("AboutSearchBorder");
            if (border != null)
            {
                border.HeightRequest = entryH;
                border.MinimumHeightRequest = entryH;
            }

            if (AboutFindNextButton != null)
            {
                AboutFindNextButton.HeightRequest = entryH;
                AboutFindNextButton.MinimumHeightRequest = entryH;
            }

            var row = this.FindByName<Grid>("AboutSearchRow");
            if (row != null)
            {
                double rowH = entryH + row.Padding.Top + row.Padding.Bottom;
                row.MinimumHeightRequest = rowH;
            }
        }
        protected override void OnDisappearing()
        {
            _aboutSearchDebounceCts?.Cancel();
            _aboutSearchDebounceCts?.Dispose();
            _aboutSearchDebounceCts = null;
            _aboutWebViewSizeCts?.Cancel();
            _aboutWebViewSizeCts?.Dispose();
            _aboutWebViewSizeCts = null;
            // Do not AllowAutorotate here — destination landscape pages ForceLandscape in
            // OnAppearing; unlocking mid-navigation causes a portrait flash.

            if (!string.IsNullOrEmpty(AboutSearchEntry.Text))  //  2026.07.27 1536  
            {
                AboutSearchEntry.Text = string.Empty;
            }
            base.OnDisappearing();
        }

        private static async Task CheckPremiumStatusAsync()
        {
            try
            {
                var store = ServiceHelper.GetService<IStoreService>();
                if (store != null)
                    await store.CheckPremiumStatusAsync();
            }
            catch { }
        }

        private async Task LoadAboutHtmlAsync()
        {
            try
            {
                string? html = null;
                var candidates = new[] { "about.html", "Resources/about.html", "Resources/Raw/about.html" };
                foreach (var name in candidates)
                {
                    try
                    {
                        using var stream = await FileSystem.OpenAppPackageFileAsync(name);
                        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                        html = await reader.ReadToEndAsync();
                        if (!string.IsNullOrWhiteSpace(html)) break;
                    }
                    catch
                    {
                        // ignore and try next candidate
                    }
                }

#if ANDROID
                if (string.IsNullOrWhiteSpace(html))
                {
                    try
                    {
                        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
                        if (activity != null && activity.Resources != null)
                        {
                            var pkg = activity.PackageName;
                            var resId = activity.Resources.GetIdentifier("about", "raw", pkg);
                            if (resId != 0)
                            {
                                using var stream = activity.Resources.OpenRawResource(resId);
                                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                                html = await reader.ReadToEndAsync();
                            }
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                }
#endif

                var web = this.FindByName<Microsoft.Maui.Controls.WebView>("AboutWebView");
                if (web == null)
                    return;

                if (string.IsNullOrWhiteSpace(html))
                {
                    web.Source = new HtmlWebViewSource { Html = "<html><body><h2>About page not available</h2><p>Could not load about.html from app package.</p></body></html>" };
                    return;
                }

                // Fix common double-encoded ampersand problems and strange "$amp:amp:" sequences
                // Do a few targeted replacements first (low risk), then HtmlDecode once to normalize entities in text nodes.
                html = html.Replace("$amp:amp:", "&", StringComparison.OrdinalIgnoreCase)
                           .Replace("amp:amp:", "&", StringComparison.OrdinalIgnoreCase)
                           .Replace("&amp;amp;", "&", StringComparison.OrdinalIgnoreCase);

                // Clean up Word-specific conditional comments and XML blobs that non-IE browsers
                // (including Android WebView) cannot parse.
                html = CleanWordHtml(html);

                var vm = BindingContext as AboutPageViewModel;
                var bg = _themeService?.PanelBackgroundColor ?? Colors.White;
                var fg = vm?.ContrastingTextColor ?? Colors.Black;
                var fontSize = vm?.SelectedFontSize ?? DefaultFontSize;
                var fontFamily = "-apple-system, BlinkMacSystemFont, \"Segoe UI Symbol\", \"Segoe UI Emoji\", \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, \"Times New Roman\", serif";
                // height:auto + overflow:visible so content grows; MAUI ScrollView scrolls.
                var css =    $"html, body {{ " +
                             $"background: {ColorToHex(bg)} !important; " +
                             $"color: {ColorToHex(fg)} !important; " +
                             $"font-size: {fontSize}px !important; " +
                             $"-webkit-text-size-adjust: 100% !important; " +
                             $"text-size-adjust: 100% !important; " +
                             $"margin: 0; " +
                             $"padding: 8px; " +
                             $"font-family: {fontFamily}; " +
                             $"box-sizing: border-box; " +
                             $"height: auto !important; " +
                             $"min-height: 0 !important; " +
                             $"overflow-x: hidden !important; " +
                             $"overflow-y: visible !important; " +
                             $"}} ";
                // Force ALL elements to inherit fg color so inline style="color:#000000" from
                // Word-generated HTML cannot make text invisible against a dark background.
                css += $"*, *::before, *::after {{ color: {ColorToHex(fg)} !important; }} ";
                css += "p, span, li, td, th, div, a, b, i, u, em, strong { font-size: inherit !important; } ";
                css += "span[style*='Symbol'] { font-size: 1.2em !important; line-height: 1; } ";
                css += "span[style*='Wingdings'] { font-size: 1.2em !important; line-height: 1; } ";
                css += $"img {{ max-width: 100%; height: auto; }} a {{ color: {ColorToHex(fg)} !important; }} ";
                css += ".pages, .note, .limitations { background: transparent !important; color: inherit !important; font-family: inherit !important; } ";
                css += $".pages, .limitations {{ border-color: {ColorToHex(fg)}33 !important; }} ";
                css += "h1, h2, h3, h4, h5, h6 { color: inherit !important; font-family: inherit !important; margin-left: 0 !important; text-indent: 0 !important; } ";
                css += "h1 { font-size: 1.8em !important; font-weight: bold !important; font-style: normal !important; margin-top: 1.2em !important; margin-bottom: 0.4em !important; border-bottom: 1px solid currentColor !important; padding-bottom: 0.2em !important; } ";
                css += "h2 { font-size: 1.4em !important; font-weight: bold !important; font-style: normal !important; margin-top: 1.0em !important; margin-bottom: 0.3em !important; } ";
                css += "h3 { font-size: 1.2em !important; font-weight: bold !important; font-style: normal !important; margin-top: 0.8em !important; margin-bottom: 0.2em !important; } ";
                css += "h4 { font-size: 1.05em !important; font-weight: bold !important; font-style: normal !important; margin-top: 0.6em !important; margin-bottom: 0.2em !important; } ";
                css += "h5 { font-size: 1.0em !important; font-weight: bold !important; font-style: italic !important; margin-top: 0.4em !important; margin-bottom: 0.1em !important; } ";
                css += "h6 { font-size: 0.95em !important; font-weight: normal !important; font-style: italic !important; margin-top: 0.3em !important; margin-bottom: 0.1em !important; } ";
                css += ".about-search-highlight { background: rgba(255,230,0,0.75) !important; color: inherit !important; padding: 0 0.05em !important; border-radius: 2px !important; } ";
                css += ".about-search-highlight.about-search-current { background: rgba(255,140,0,0.9) !important; outline: 2px solid #ff6600 !important; } ";
                var styled = InjectCssIntoHtml(html, css);
                _aboutHtmlReady = false;
                _aboutMatchCount = 0;
                _aboutCurrentIndex = -1;
                _aboutLastWebViewWidth = -1;
                UpdateAboutSearchControls();
                web.HeightRequest = 400;
                web.Source = new HtmlWebViewSource { Html = styled };
                if (AboutScrollView != null)
                    _ = AboutScrollView.ScrollToAsync(0, 0, false);
#if ANDROID
                // Android accessibility font scale otherwise inflates WebView text far beyond the
                // Settings "Font used in About page" size (email/contact text looked huge).
                ApplyAboutWebViewTextZoom(web);
#endif
            }
            catch (Exception ex)
            {
                var web = this.FindByName<Microsoft.Maui.Controls.WebView>("AboutWebView");
                if (web != null)
                {
                    web.Source = new HtmlWebViewSource { Html = $"<html><body><h2>About page error</h2><pre>{System.Net.WebUtility.HtmlEncode(ex.Message)}</pre></body></html>" };
                }
            }
        }

#if ANDROID
        private static void ApplyAboutWebViewTextZoom(Microsoft.Maui.Controls.WebView web)
        {
            void Apply()
            {
                try
                {
                    if (web.Handler?.PlatformView is Android.Webkit.WebView native)
                    {
                        native.Settings.JavaScriptEnabled = true;
                        native.Settings.DomStorageEnabled = true;
                        native.Settings.TextZoom = 100;
                        ApplyAboutWebViewOuterScrollSupport(web);
                    }
                }
                catch
                {
                    // Best effort — CSS text-size-adjust still helps.
                }
            }

            if (web.Handler?.PlatformView is Android.Webkit.WebView)
                Apply();
            else
                web.HandlerChanged += (_, _) => Apply();
        }

        /// <summary>
        /// When the WebView is fully expanded inside a ScrollView, stop it from stealing
        /// vertical pans so the page chrome can scroll away with the guide.
        /// </summary>
        private static void ApplyAboutWebViewOuterScrollSupport(Microsoft.Maui.Controls.WebView web)
        {
            try
            {
                if (web.Handler?.PlatformView is not Android.Webkit.WebView native)
                    return;

                native.OverScrollMode = Android.Views.OverScrollMode.Never;
                native.VerticalScrollBarEnabled = false;
                native.HorizontalScrollBarEnabled = false;
                native.SetOnTouchListener(new AboutWebViewPassTouchesToParentListener());
            }
            catch
            {
                // Best effort.
            }
        }

        private sealed class AboutWebViewPassTouchesToParentListener
            : Java.Lang.Object, Android.Views.View.IOnTouchListener
        {
            public bool OnTouch(Android.Views.View? v, Android.Views.MotionEvent? e)
            {
                // Let the parent ScrollView handle vertical scrolling.
                v?.Parent?.RequestDisallowInterceptTouchEvent(false);
                return false;
            }
        }
#endif

        private static string ColorToHex(Color c)
        {
            int r = (int)Math.Round(c.Red * 255);
            int g = (int)Math.Round(c.Green * 255);
            int b = (int)Math.Round(c.Blue * 255);
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        private static string InjectCssIntoHtml(string html, string css)
        {
            if (string.IsNullOrEmpty(html)) return html;

            var lower = html.ToLowerInvariant();

            // Insert just before </head>. This is safe because </head> has no attributes
            // and can never contain a stray > like <head> or the elements inside head can.
            int headCloseIdx = lower.IndexOf("</head>");
            if (headCloseIdx >= 0)
            {
                var meta = lower.Contains("charset") ? "" : "<meta charset=\"utf-8\">";
                // Always inject viewport — without it Android WebView uses a ~980px wide virtual
                // viewport and scales it down, making JS offsetTop values ~3x MAUI dp values,
                // which breaks scroll-to-highlight position calculations.
                var viewport = lower.Contains("viewport") ? "" : "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">";
                return html.Insert(headCloseIdx, meta + viewport + $"<style>{css}</style>");
            }

            // Fallback: no </head>; wrap the whole thing
            return $"<html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><style>{css}</style></head><body>{html}</body></html>";
        }

        private async void OnAboutSearchEntryCompleted( object? sender, EventArgs e)
        {
            if (sender is not Entry entry)
                return;

            _aboutSearchDebounceCts?.Cancel();

            var query = entry.Text?.Trim() ?? string.Empty;

            try
            {
                var isNewQuery = !string.Equals(
                    query,
                    _aboutSearchQuery,
                    StringComparison.Ordinal);

                if (isNewQuery)
                {
                    await SearchAboutPageAsync(query);
                }
                else if (_aboutMatchCount > 0)
                {
                    await MoveToNextAboutMatchAsync();
                }
            }
            catch
            {
                await ShowAboutSearchErrorAsync();
            }
            finally
            {
                // Close the soft keyboard / search IME after Search finishes its work.
                HideAboutSearchKeyboard(entry);
            }
        }

        private async void OnAboutFindNextClicked( object? sender, EventArgs e)
        {
            try
            {
                await MoveToNextAboutMatchAsync();
            }
            catch
            {
                await ShowAboutSearchErrorAsync();
            }
            finally
            {
                HideAboutSearchKeyboard(AboutSearchEntry);
            }
        }

        /// <summary>
        /// Dismisses the About search Entry focus and soft keyboard (Android IME).
        /// </summary>
        private static void HideAboutSearchKeyboard(Entry? entry)
        {
            try
            {
                entry?.Unfocus();
            }
            catch
            {
                // Best effort.
            }

#if ANDROID
            try
            {
                var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
                if (activity == null)
                    return;

                var imm = activity.GetSystemService(Android.Content.Context.InputMethodService)
                    as Android.Views.InputMethods.InputMethodManager;
                if (imm == null)
                    return;

                var token = activity.CurrentFocus?.WindowToken
                    ?? entry?.Handler?.PlatformView switch
                    {
                        Android.Views.View v => v.WindowToken,
                        _ => null
                    };

                if (token != null)
                {
                    imm.HideSoftInputFromWindow(
                        token,
                        Android.Views.InputMethods.HideSoftInputFlags.None);
                }
            }
            catch
            {
                // Best effort only.
            }
#endif
        }


        /// <summary>
        /// Removes Word-specific conditional comments and XML blobs that non-IE browsers
        /// (including Android WebView) cannot parse. Unrecognised (left arrow bracket)![if ...]> tags are left
        /// "open" by the parser, swallowing the opening of the next real element and causing
        /// its attributes to appear as visible text in the rendered page.
        /// </summary>
        private static string CleanWordHtml(string html)
        {
            // 1. Non-standard Word conditionals: <![if ...]>...</[endif]>
            html = System.Text.RegularExpressions.Regex.Replace(
                html,
                @"<!\[if[^\]]*\]>.*?<!\[endif\]>",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.Singleline |
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // 2. Standard IE conditional comments: <!--[if ...]>...</[endif]-->
            html = System.Text.RegularExpressions.Regex.Replace(
                html,
                @"<!--\[if[^\]]*\]>.*?<!\[endif\]-->",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.Singleline |
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // 3. Any orphaned tags that survived the above (defensive)
            html = System.Text.RegularExpressions.Regex.Replace(
                html, @"<!\[if[^\]]*\]>", string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            html = System.Text.RegularExpressions.Regex.Replace(
                html, @"<!\[endif\]>", string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // 4. Word XML blobs in <head> (<xml>...</xml>)
            html = System.Text.RegularExpressions.Regex.Replace(
                html,
                @"<xml>.*?</xml>",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.Singleline |
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return html;
        }
        private async void OnAboutSearchEntryFocused(object? sender, FocusEventArgs e)
        {
            if (sender is not Entry entry)
                return;

            // Allow the native Entry to finish receiving focus.
            await Task.Delay(100);

            entry.CursorPosition = 0;
            entry.SelectionLength = entry.Text?.Length ?? 0;
        }
        private async void OnAboutSearchEntryTextChanged(object? sender, TextChangedEventArgs e)
        {
            _aboutSearchDebounceCts?.Cancel();
            _aboutSearchDebounceCts?.Dispose();

            var cts = new CancellationTokenSource();
            _aboutSearchDebounceCts = cts;

            try
            {
                // Avoid rebuilding the highlights after every individual keystroke.
                await Task.Delay(250, cts.Token);

                await SearchAboutPageAsync(
                    e.NewTextValue,
                    cts.Token);
            }
            catch (OperationCanceledException)
            {
                // A later TextChanged event replaced this search.
            }
            catch
            {
                await ShowAboutSearchErrorAsync();
            }
        }
        private async Task SearchAboutPageAsync( string? searchText,  CancellationToken cancellationToken = default)
        {
            var query = searchText?.Trim() ?? string.Empty;

            cancellationToken.ThrowIfCancellationRequested();

            var web = AboutWebView;
            if (web == null)
                return;

            /*
             * Pause audio capture once when the user begins searching.
             * Clearing the search does not restart audio capture.
             */
            if (!string.IsNullOrEmpty(query) &&
                !_aboutPausedListening)
            {
                try
                {
                    var audio =
                        ServiceHelper.GetService<IAudioCaptureService>();

                    audio?.StopCapture();
                    _aboutPausedListening = true;
                }
                catch
                {
                    // Best effort only.
                }
            }

            var jsQuery = ToJavaScriptStringLiteral(query);

            // Android WebView often rejects TreeWalker filter *objects*; use null + manual skips.
            // Always return a string so MAUI Android reliably delivers the result.
            var js = $@"(function(){{
        var q = {jsQuery};

        function unwrapHighlights() {{
            var oldHighlights = document.querySelectorAll('.about-search-highlight');
            for (var i = oldHighlights.length - 1; i >= 0; i--) {{
                var element = oldHighlights[i];
                var parent = element.parentNode;
                if (!parent) continue;
                while (element.firstChild)
                    parent.insertBefore(element.firstChild, element);
                parent.removeChild(element);
                parent.normalize();
            }}
        }}

        unwrapHighlights();

        if (!q) {{
            return '0';
        }}

        if (!document.body)
            return '0';

        function escapeRegExp(value) {{
            return value.replace(/[.*+?^${{}}()|[\]\\]/g, '\\$&');
        }}

        var expression = new RegExp(escapeRegExp(q), 'gi');
        var count = 0;

        try {{
            var walker = document.createTreeWalker(
                document.body,
                NodeFilter.SHOW_TEXT,
                null
            );

            var nodes = [];
            var node;
            while ((node = walker.nextNode())) {{
                var parent = node.parentNode;
                var tag = parent ? parent.nodeName.toUpperCase() : '';
                if (tag === 'SCRIPT' || tag === 'STYLE' || tag === 'NOSCRIPT')
                    continue;
                if (parent && parent.classList && parent.classList.contains('about-search-highlight'))
                    continue;
                nodes.push(node);
            }}

            for (var nodeIndex = 0; nodeIndex < nodes.length; nodeIndex++) {{
                var textNode = nodes[nodeIndex];
                var text = textNode.nodeValue || '';
                expression.lastIndex = 0;
                if (!expression.test(text))
                    continue;

                expression.lastIndex = 0;
                var fragment = document.createDocumentFragment();
                var previousIndex = 0;
                var match;

                while ((match = expression.exec(text)) !== null) {{
                    if (match.index > previousIndex) {{
                        fragment.appendChild(document.createTextNode(
                            text.slice(previousIndex, match.index)));
                    }}

                    var span = document.createElement('span');
                    span.className = 'about-search-highlight';
                    span.setAttribute('data-about-index', String(count));
                    span.textContent = match[0];
                    fragment.appendChild(span);
                    count++;
                    previousIndex = match.index + match[0].length;
                    if (match[0].length === 0)
                        expression.lastIndex++;
                }}

                if (previousIndex < text.length) {{
                    fragment.appendChild(document.createTextNode(
                        text.slice(previousIndex)));
                }}

                if (textNode.parentNode)
                    textNode.parentNode.replaceChild(fragment, textNode);
            }}

            if (count > 0) {{
                var first = document.querySelector(
                    '.about-search-highlight[data-about-index=""0""]');
                if (first)
                    first.classList.add('about-search-current');
            }}
        }}
        catch (error) {{
            return '0';
        }}

        return String(count);
    }})();";

            cancellationToken.ThrowIfCancellationRequested();

            string? result = null;
            try
            {
                result = await web.EvaluateJavaScriptAsync(js);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AboutSearch] EvaluateJavaScript failed: {ex.Message}");
                result = null;
            }

            cancellationToken.ThrowIfCancellationRequested();

            _aboutSearchQuery = query;

            if (TryParseJavaScriptInteger(result, out var count))
            {
                _aboutMatchCount = count;
                _aboutCurrentIndex = count > 0 ? 0 : -1;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AboutSearch] Unparsed JS result for '{query}': '{result}'");
                _aboutMatchCount = 0;
                _aboutCurrentIndex = -1;
            }

            UpdateAboutSearchControls();

            if (_aboutMatchCount > 0)
                await ScrollToAboutMatchAsync(0);
            else if (string.IsNullOrEmpty(query))
                await ScrollAboutPageToAsync(0);
        }

        private static string ToJavaScriptStringLiteral(string value)
        {
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\u2028': sb.Append("\\u2028"); break;
                    case '\u2029': sb.Append("\\u2029"); break;
                    default:
                        if (ch < ' ')
                            sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else
                            sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
        private async Task ScrollToAboutMatchAsync(int matchIndex)
        {
            if (_aboutMatchCount <= 0)
                return;

            if (matchIndex < 0 || matchIndex >= _aboutMatchCount)
                return;

            var web = AboutWebView;
            if (web == null)
                return;

            _aboutCurrentIndex = matchIndex;

            // Highlight the match and return its Y in the document (outer ScrollView scrolls).
            var js = $@"(function(){{
        var matches = document.querySelectorAll('.about-search-highlight');
        for (var i = 0; i < matches.length; i++)
            matches[i].classList.remove('about-search-current');

        var current = document.querySelector(
            '.about-search-highlight[data-about-index=""{matchIndex}""]');
        if (!current)
            return '-1';

        current.classList.add('about-search-current');

        try {{
            var top = current.getBoundingClientRect().top
                + (window.pageYOffset || document.documentElement.scrollTop || 0);
            return String(Math.round(top));
        }}
        catch (error) {{
            return '-1';
        }}
    }})();";

            string? result = null;
            try
            {
                result = await web.EvaluateJavaScriptAsync(js);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AboutSearch] ScrollToAboutMatch failed: {ex.Message}");
            }

            if (TryParseJavaScriptInteger(result, out var matchTop) && matchTop >= 0)
                await ScrollAboutPageToMatchAsync(matchTop);

            UpdateAboutSearchControls();
        }

        private async Task MoveToNextAboutMatchAsync()
        {
            var web = AboutWebView;
            if (web == null)
                return;

            // If the document was reloaded, rebuild highlights before advancing.
            string? domCountResult = null;
            try
            {
                domCountResult = await web.EvaluateJavaScriptAsync(
                    "(function(){ try { return String(document.querySelectorAll('.about-search-highlight').length); } catch(e) { return '0'; } })();");
            }
            catch
            {
                domCountResult = "0";
            }

            if (!TryParseJavaScriptInteger(domCountResult, out var domCount) || domCount <= 0)
            {
                var text = AboutSearchEntry?.Text?.Trim();
                if (string.IsNullOrEmpty(text))
                    return;

                await SearchAboutPageAsync(text);
                return;
            }

            _aboutMatchCount = domCount;

            _aboutCurrentIndex++;
            if (_aboutCurrentIndex < 0 || _aboutCurrentIndex >= _aboutMatchCount)
                _aboutCurrentIndex = 0;

            await ScrollToAboutMatchAsync(_aboutCurrentIndex);
        }

        /// <summary>
        /// Scrolls the page ScrollView so a match (Y within the WebView document) is in view.
        /// </summary>
        private async Task ScrollAboutPageToMatchAsync(double matchTopInDocument)
        {
            var scroll = AboutScrollView;
            var web = AboutWebView;
            if (scroll == null || web == null)
                return;

            // Wait a layout pass so Bounds.Top reflects search/premium chrome height.
            await Task.Yield();

            double webOffset = web.Bounds.Top;
            if (webOffset <= 0 && AboutMainLayout != null)
            {
                // Fallback before first layout of Bounds: sum chrome above the WebView.
                webOffset = AboutSearchRow.Height + AboutPremiumRow.Height + web.Margin.Top;
            }

            double viewport = scroll.Height > 0 ? scroll.Height : 300;
            double targetY = Math.Max(0, webOffset + matchTopInDocument - (viewport / 3.0));
            await ScrollAboutPageToAsync(targetY);
        }

        private async Task ScrollAboutPageToAsync(double y)
        {
            var scroll = AboutScrollView;
            if (scroll == null)
                return;

            try
            {
                await scroll.ScrollToAsync(0, Math.Max(0, y), false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AboutScroll] ScrollToAsync failed: {ex.Message}");
            }
        }
        private void UpdateAboutSearchControls()
        {
            AboutFindNextButton.IsEnabled =
                _aboutMatchCount > 0;

            AboutFindPositionLabel.Text =
                _aboutMatchCount > 0
                    ? $"{_aboutCurrentIndex + 1}/{_aboutMatchCount}"
                    : string.Empty;
        }
        private static bool TryParseJavaScriptInteger( string? result, out int value)
        {
            value = 0;

            if (string.IsNullOrWhiteSpace(result))
                return false;

            var cleaned = result
                .Trim()
                .Trim('"')
                .Trim('\'');

            if (cleaned.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                cleaned.Equals("undefined", StringComparison.OrdinalIgnoreCase) ||
                cleaned.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return int.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
        private static async Task ShowAboutSearchErrorAsync()
        {
            await MainThread.InvokeOnMainThreadAsync(
                async () =>
                {
                    await Toast
                        .Make(
                            "Search unavailable.",
                            ToastDuration.Short)
                        .Show();
                });
        }
    }
}
