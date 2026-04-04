using Microsoft.Maui.Storage;
using musicmate.Services;
using musicmate.Utilities;
using Microsoft.Maui.Graphics;
using musicmate.ViewModels;
using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Net;
using System.Linq;

namespace musicmate.Pages
{
    public partial class AboutPage : ContentPage
    {
        private const double DefaultFontSize = 12;
        private readonly ThemeService _themeService;
        private readonly IOrientationService _orientationService;

        // Search state
        private int _aboutMatchCount = 0;
        private int _aboutCurrentIndex = -1;
        private string _aboutSearchQuery = string.Empty;

        // Track whether we've asked the app to pause listening while searching
        private bool _aboutPausedListening = false;

        public AboutPage()
        {
            InitializeComponent();
            _orientationService = ServiceHelper.GetService<IOrientationService>()!;
            _themeService = ServiceHelper.GetService<ThemeService>()!;
            var vm = new AboutPageViewModel(_themeService);
            BindingContext = vm;
            // Set 9mm left margin on the outermost ScrollView
            var mainLayout = this.FindByName<VerticalStackLayout>("AboutMainLayout");
            //if (mainLayout != null)
            //    musicmate.Utilities.MarginUtils.SetLeftMarginMM(mainLayout, 9, 0, 0, 0);  //  2026.04.02 1719   out

            // Auto-size WebView to its content height so the outer ScrollView handles scrolling
            var web = this.FindByName<Microsoft.Maui.Controls.WebView>("AboutWebView");
            if (web != null)
            {
                web.Navigated += async (s, e) =>
                {
                    await Task.Delay(200);
                    await AutoSizeWebViewAsync((Microsoft.Maui.Controls.WebView)s!);
                };
            }

            // Reload WebView when font size or theme changes
            vm.PropertyChanged += async (_, __) => await LoadAboutHtmlAsync();
            _themeService.PropertyChanged += async (_, __) => await LoadAboutHtmlAsync();

            // Ensure Find Next button initial state
            var nextBtn = this.FindByName<Button>("AboutFindNextButton");
            if (nextBtn != null)
                nextBtn.IsEnabled = false;
            var posLbl = this.FindByName<Label>("AboutFindPositionLabel");
            if (posLbl != null)
                posLbl.Text = string.Empty;
        }

        protected override void OnAppearing()
        {
            _orientationService?.AllowAutorotate();
            base.OnAppearing();

            _ = LoadAboutHtmlAsync();
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
                        var enc = Encoding.GetEncoding(1252);
                        using var reader = new StreamReader(stream, enc);
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
                                var enc = Encoding.GetEncoding(1252);
                                using var reader = new StreamReader(stream, enc);
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

                // Normalize HTML entities in text (safe for typical HTML content)
                try
                {
                    html = WebUtility.HtmlDecode(html);
                }
                catch
                {
                    // best-effort: if decode fails, continue with the original html
                }

                var vm = BindingContext as AboutPageViewModel;
                var bg = _themeService?.PanelBackgroundColor ?? Colors.White;
                var fg = vm?.ContrastingTextColor ?? Colors.Black;
                var fontSize = vm?.SelectedFontSize ?? DefaultFontSize;
                var fontFamily = "-apple-system, BlinkMacSystemFont, \"Segoe UI Symbol\", \"Segoe UI Emoji\", \"Segoe UI\", Roboto, \"Helvetica Neue\", Arial, \"Times New Roman\", serif";
                var css = $"html, body {{ background: {ColorToHex(bg)} !important; color: {ColorToHex(fg)} !important; font-size: {fontSize}px !important; margin:0; padding:8px; font-family: {fontFamily}; overflow: hidden !important; }} ";
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
                css += ".about-search-highlight { background: rgba(255,255,0,0.6) !important; color: inherit !important; padding: 0 0.05em !important; border-radius: 2px !important; } ";
                var styled = InjectCssIntoHtml(html, css);
                web.Source = new HtmlWebViewSource { Html = styled };
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
            int headIndex = lower.IndexOf("<head");
            if (headIndex >= 0)
            {
                int headClose = html.IndexOf('>', headIndex);
                if (headClose >= 0)
                {
                    var meta = "";
                    if (!lower.Contains("charset"))
                    {
                        meta = "<meta charset=\"utf-8\">";
                    }
                    return html.Insert(headClose + 1, meta + $"<style>{css}</style>");
                }
            }
            int htmlIndex = lower.IndexOf("<html");
            if (htmlIndex >= 0)
            {
                int htmlClose = html.IndexOf('>', htmlIndex);
                if (htmlClose >= 0)
                {
                    return html.Insert(htmlClose + 1, $"<head><meta charset=\"utf-8\"><style>{css}</style></head>");
                }
            }
            return $"<html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><style>{css}</style></head><body>{html}</body></html>";
        }

        private async Task AutoSizeWebViewAsync(Microsoft.Maui.Controls.WebView webView)
        {
            try
            {
                var heightStr = await webView.EvaluateJavaScriptAsync("document.body.scrollHeight");
                if (double.TryParse(heightStr, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var height) && height > 0)
                {
                    webView.HeightRequest = height + 20;
                }
            }
            catch
            {
                // ignore measurement failures
            }
        }

        private async void OnAboutSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                var query = e.NewTextValue ?? string.Empty;
                var web = this.FindByName<Microsoft.Maui.Controls.WebView>("AboutWebView");
                if (web == null) return;

                // Pause audio capture once when user starts searching. No automatic restart required.
                if (!string.IsNullOrEmpty(query) && !_aboutPausedListening)
                {
                    try
                    {
                        var audio = ServiceHelper.GetService<IAudioCaptureService>();
                        audio?.StopCapture();
                        _aboutPausedListening = true;
                    }
                    catch
                    {
                        // best-effort
                    }
                }

                _aboutSearchQuery = query;
                _aboutCurrentIndex = -1;
                _aboutMatchCount = 0;

                // Serialize query safely for JS string literal
                var jsQuery = JsonSerializer.Serialize(query);

                // JS highlights all matches and returns match count. It also scrolls the first match into view inside the WebView (center).
                var js = $@"(function(){{
                        var q = {jsQuery};
                        try {{
                            // Remove existing highlights first
                            var olds = document.querySelectorAll('.about-search-highlight');
                            if (olds && olds.length) {{
                                for (var i=0;i<olds.length;i++) {{
                                    var el = olds[i];
                                    el.outerHTML = el.textContent;
                                }}
                            }}
                        }} catch(e) {{ }}

                        if (!q) {{ window.scrollTo(0,0); return 0; }}

                        function escapeRegExp(s) {{ return s.replace(/[.*+?^{{}}()|[\\]\\\/]/g, '\\\\$&'); }}
                        try {{
                            var body = document.body;
                            var html = body.innerHTML;
                            var re = new RegExp(escapeRegExp(q), 'gi');
                            var count = 0;
                            var newHtml = html.replace(re, function(m) {{
                                var r = '<span class=""about-search-highlight"" data-about-index=""' + count + '"">' + m + '</span>';
                                count++;
                                return r;
                            }});
                            if (count > 0) {{
                                body.innerHTML = newHtml;
                                var first = document.querySelector('.about-search-highlight[data-about-index=""0""]');
                                if (first) {{
                                    try {{ first.scrollIntoView({{behavior:'smooth', block:'center'}}); }} catch(e) {{ }}
                                }}
                            }}
                            return count;
                        }} catch(e) {{
                            return 0;
                        }}
                    }})();";

                var result = await web.EvaluateJavaScriptAsync(js);
                if (int.TryParse(result?.Trim('"'), out var cnt))
                {
                    _aboutMatchCount = cnt;
                    if (cnt > 0)
                    {
                        _aboutCurrentIndex = 0;
                        // Ensure the highlighted match is centered vertically in the outer ScrollView
                        var sv = this.FindByName<ScrollView>("AboutScrollView");
                        if (sv != null)
                        {
                            await CenterHighlightedInScrollViewAsync(_aboutCurrentIndex);
                        }
                    }
                }
                else
                {
                    _aboutMatchCount = 0;
                    _aboutCurrentIndex = -1;
                }

                // Update UI controls: Next button and position label
                var nextBtn = this.FindByName<Button>("AboutFindNextButton");
                var posLbl = this.FindByName<Label>("AboutFindPositionLabel");
                if (nextBtn != null)
                    nextBtn.IsEnabled = _aboutMatchCount > 1;
                if (posLbl != null)
                    posLbl.Text = _aboutMatchCount > 0 ? $"{(_aboutCurrentIndex + 1)}/{_aboutMatchCount}" : string.Empty;
            }
            catch
            {
                // best-effort; ignore errors
            }
        }

        private void OnAboutSearchButtonPressed(object sender, EventArgs e)
        {
            // Dismiss keyboard when user presses search
            AboutSearchBar.Unfocus();
        }

        private async void OnAboutFindNextClicked(object sender, EventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_aboutSearchQuery) || _aboutMatchCount <= 0)
                    return;

                _aboutCurrentIndex++;
                if (_aboutCurrentIndex >= _aboutMatchCount)
                    _aboutCurrentIndex = 0;

                var web = this.FindByName<Microsoft.Maui.Controls.WebView>("AboutWebView");
                if (web == null) return;

                // JS: scroll the specific highlighted span into view inside the WebView (center)
                var js = $@"(function(idx) {{
                        try {{
                            var el = document.querySelector('.about-search-highlight[data-about-index=""' + idx + '""]');
                            if (el) {{
                                try {{ el.scrollIntoView({{behavior:'smooth', block:'center'}}); }} catch(e) {{ }}
                                return true;
                            }}
                        }} catch(e) {{ }}
                        return false;
                    }})({_aboutCurrentIndex});";

                var res = await web.EvaluateJavaScriptAsync(js);

                // Center the highlighted element in the outer ScrollView vertically
                var sv = this.FindByName<ScrollView>("AboutScrollView");
                if (sv != null)
                {
                    await CenterHighlightedInScrollViewAsync(_aboutCurrentIndex);
                }

                // Update position label
                var posLbl = this.FindByName<Label>("AboutFindPositionLabel");
                if (posLbl != null)
                    posLbl.Text = _aboutMatchCount > 0 ? $"{(_aboutCurrentIndex + 1)}/{_aboutMatchCount}" : string.Empty;
            }
            catch
            {
                // ignore
            }
        }

        private async Task CenterHighlightedInScrollViewAsync(int index)
        {
            try
            {
                var web = this.FindByName<Microsoft.Maui.Controls.WebView>("AboutWebView");
                var sv = this.FindByName<ScrollView>("AboutScrollView");
                if (web == null || sv == null) return;

                // Ensure we have measured sizes and positions; retry a few times if needed
                int retries = 5;
                while (retries-- > 0 && (sv.Height <= 0 || AboutWebView.Y == 0))
                {
                    await Task.Delay(50);
                }

                // Return absolute top (document-coordinate) and element height as "top|height"
                var infoJs = $@"(function(idx){{ var el = document.querySelector('.about-search-highlight[data-about-index=""' + idx + '""]'); if(!el) return ''; var r = el.getBoundingClientRect(); return (window.pageYOffset + r.top) + '|' + r.height; }})({index});";
                var infoRes = await web.EvaluateJavaScriptAsync(infoJs);
                var trimmed = infoRes?.Trim('"');
                if (string.IsNullOrEmpty(trimmed))
                {
                    // fallback: center the WebView
                    await sv.ScrollToAsync(AboutWebView, ScrollToPosition.Center, true);
                    return;
                }

                var parts = trimmed.Split('|');
                if (parts.Length >= 2
                    && double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var top)
                    && double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var elHeight))
                {
                    var svHeight = sv.Height;
                    if (svHeight <= 0)
                        svHeight = Application.Current?.Windows?.FirstOrDefault()?.Page?.Height ?? 600;

                    // WebView top offset within the ScrollView content
                    var webTop = AboutWebView.Y;

                    // target scroll Y so the element's center sits in the vertical middle of the ScrollView.
                    // top is element position inside the web document; add webTop to map into the outer ScrollView content coordinates.
                    var target = webTop + top + (elHeight / 2.0) - (svHeight / 2.0);
                    if (target < 0) target = 0;

                    await sv.ScrollToAsync(0, target, true);
                }
                else
                {
                    await sv.ScrollToAsync(AboutWebView, ScrollToPosition.Center, true);
                }
            }
            catch
            {
                // ignore errors - best-effort
            }
        }

        private async void OnNavigateHomeClicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//MainPage");
        }
    }
}
