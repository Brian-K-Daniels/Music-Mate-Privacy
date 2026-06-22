using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls.Shapes;
using musicmate.Services;
using musicmate.Utilities;

namespace musicmate.Views;

/// <summary>Code-only premium upsell popup (no XAML).</summary>
public class PremiumPopup : Popup
{
    /// <summary>True if the user successfully completed a purchase.</summary>
    public bool PurchaseResult { get; private set; }

    private readonly IStoreService? _storeService;
    private readonly Action? _onDecline;

    /// <param name="onDecline">
    /// Optional action invoked when the user taps "No thanks, continue free".
    /// Use it to revert whatever property triggered this prompt.
    /// </param>
    public PremiumPopup(Action? onDecline = null)
    {
        _storeService = ServiceHelper.GetService<IStoreService>();
        _onDecline = onDecline;
        CanBeDismissedByTappingOutsideOfPopup = false;

        var info = DeviceDisplay.Current.MainDisplayInfo;
        double density = info.Density > 0 ? info.Density : 1;
        double screenW = info.Width / density;
        double popupW = Math.Min(screenW * 0.82, 480);
        const double popupH = 280;   // raised to give button rows room for descenders

        var titleLabel = new Label
        {
            Text = "⭐ Premium Feature",
            FontAttributes = FontAttributes.Bold,
            FontSize = 14,
            TextColor = Colors.White,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        var subtitleLabel = new Label
        {
            Text = "Premium users only",
            FontSize = 12,
            TextColor = Color.FromArgb("#CCCCCC"),
            HorizontalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center
        };

        var buyButton = new Button
        {
            Text = "Buy Premium",
            BackgroundColor = Color.FromArgb("#8B4513"),
            TextColor = Colors.White,
            CornerRadius = 6,
            FontAttributes = FontAttributes.Bold,
            FontSize = 13,
            Padding = new Thickness(10, 10),  // vertical padding keeps descenders clear
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        buyButton.Clicked += OnBuyPremiumClicked;

        var declineButton = new Button
        {
            Text = "No thanks, continue free",
            BackgroundColor = Color.FromArgb("#01000000"),
            TextColor = Color.FromArgb("#AAAAAA"),
            BorderColor = Color.FromArgb("#888888"),
            BorderWidth = 1,
            CornerRadius = 6,
            FontSize = 12,
            Padding = new Thickness(10, 10),  // vertical padding keeps descenders clear
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        declineButton.Clicked += OnDeclineClicked;

        var grid = new Grid
        {
            RowSpacing = 0,
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Star    }, // row 0 – top gap
                new RowDefinition { Height = GridLength.Auto    }, // row 1 – title
                new RowDefinition { Height = GridLength.Star    }, // row 2 – gap
                new RowDefinition { Height = GridLength.Auto    }, // row 3 – subtitle
                new RowDefinition { Height = GridLength.Star    }, // row 4 – gap
                new RowDefinition { Height = new GridLength(52) }, // row 5 – Buy Premium
                new RowDefinition { Height = GridLength.Star    }, // row 6 – gap
                new RowDefinition { Height = new GridLength(52) }, // row 7 – Decline
#if DEBUG
                new RowDefinition { Height = GridLength.Star    }, // row 8 – gap (debug only)
                new RowDefinition { Height = new GridLength(44) }, // row 9 – Restore (debug only)
#endif
                new RowDefinition { Height = GridLength.Star    }, // bottom gap
            }
        };

        grid.Add(titleLabel, column: 0, row: 1);
        grid.Add(subtitleLabel, column: 0, row: 3);
        grid.Add(buyButton, column: 0, row: 5);
        grid.Add(declineButton, column: 0, row: 7);

#if DEBUG
        // DEBUG ONLY – "Restore Purchases" resets the local premium flag so you
        // can test the flow repeatedly without uninstalling.
        var restoreButton = new Button
        {
            Text = "🔄 Restore Purchases (debug)",
            BackgroundColor = Color.FromArgb("#01000000"),
            TextColor = Color.FromArgb("#888888"),
            BorderColor = Color.FromArgb("#555555"),
            BorderWidth = 1,
            CornerRadius = 6,
            FontSize = 11,
            Padding = new Thickness(10, 6),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        restoreButton.Clicked += OnRestoreClicked;
        grid.Add(restoreButton, column: 0, row: 9);

        double debugPopupH = popupH + 60;   // extra height for the extra button
#endif

        Content = new Border
        {
            BackgroundColor = Color.FromArgb("#1E1E1E"),
            Stroke = Color.FromArgb("#8B4513"),
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            Padding = new Thickness(20, 14),
            WidthRequest = popupW,
#if DEBUG
            HeightRequest = debugPopupH,
#else
            HeightRequest   = popupH,
#endif
            Content = grid
        };
    }

    private async void OnBuyPremiumClicked(object? sender, EventArgs e)
    {
        if (_storeService != null)
        {
            var purchased = await _storeService.PurchaseAsync("music_mate_premium");
            if (purchased)
            {
                StatusService.Instance.IsPremiumUser = true;
                PurchaseResult = true;
            }
        }
        await SafeCloseAsync();
    }

    private async void OnDeclineClicked(object? sender, EventArgs e)
    {
        // Revert the triggering property before the popup closes
        _onDecline?.Invoke();
        await SafeCloseAsync();
    }

#if DEBUG
    private async void OnRestoreClicked(object? sender, EventArgs e)
    {
        if (_storeService != null)
        {
            bool restored = await _storeService.RestorePurchasesAsync();
            if (restored)
            {
                StatusService.Instance.IsPremiumUser = true;
                PurchaseResult = true;
            }
            else
            {
                StatusService.Instance.IsPremiumUser = false;
            }
        }
        await SafeCloseAsync();
    }
#endif

    /// <summary>
    /// Closes the popup safely. If a modal page is blocking closure (e.g. the Instrument
    /// picker dialog is still on the stack) it is popped first, then the popup closes.
    /// Also handles the CommunityToolkit.Maui "Ambiguous routes" Shell bug that occurs
    /// when duplicate PopupPage route registrations are detected on close.
    /// </summary>
    private async Task SafeCloseAsync()
    {
        try
        {
            await CloseAsync();
        }
        catch (Exception ex) when (ex.Message.Contains("blocked by the Modal Page") ||
                                   ex.Message.Contains("PopupBlockedException") ||
                                   ex.Message.Contains("Ambiguous routes"))
        {
            try
            {
                var nav = Application.Current?.Windows[0].Page?.Navigation;
                if (nav?.ModalStack.Count > 0)
                    await nav.PopModalAsync(animated: false);
            }
            catch
            {
                // best effort — popup will close when the modal is dismissed naturally
            }
        }
        catch
        {
            // Windows or other platform threw an unrecognised exception on close — ignore.
        }
    }
}
