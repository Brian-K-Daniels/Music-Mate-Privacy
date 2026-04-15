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

        var info       = DeviceDisplay.Current.MainDisplayInfo;
        double density = info.Density > 0 ? info.Density : 1;
        double screenW = info.Width / density;
        double popupW  = Math.Min(screenW * 0.82, 480);
        const double popupH = 280;   // raised to give button rows room for descenders

        var titleLabel = new Label
        {
            Text              = "⭐ Premium Feature",
            FontAttributes    = FontAttributes.Bold,
            FontSize          = 14,
            TextColor         = Colors.White,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions   = LayoutOptions.Center
        };

        var subtitleLabel = new Label
        {
            Text                    = "Premium users only",
            FontSize                = 12,
            TextColor               = Color.FromArgb("#CCCCCC"),
            HorizontalOptions       = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalOptions         = LayoutOptions.Center
        };

        var buyButton = new Button
        {
            Text              = "Buy Premium",
            BackgroundColor   = Color.FromArgb("#8B4513"),
            TextColor         = Colors.White,
            CornerRadius      = 6,
            FontAttributes    = FontAttributes.Bold,
            FontSize          = 13,
            Padding           = new Thickness(10, 10),  // vertical padding keeps descenders clear
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions   = LayoutOptions.Fill
        };
        buyButton.Clicked += OnBuyPremiumClicked;

        var declineButton = new Button
        {
            Text              = "No thanks, continue free",
            BackgroundColor   = Colors.Transparent,
            TextColor         = Color.FromArgb("#AAAAAA"),
            BorderColor       = Color.FromArgb("#888888"),
            BorderWidth       = 1,
            CornerRadius      = 6,
            FontSize          = 12,
            Padding           = new Thickness(10, 10),  // vertical padding keeps descenders clear
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions   = LayoutOptions.Fill
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
                new RowDefinition { Height = new GridLength(52) }, // row 5 – Buy Premium (raised from 40)
                new RowDefinition { Height = GridLength.Star    }, // row 6 – gap
                new RowDefinition { Height = new GridLength(52) }, // row 7 – Decline (raised from 38)
                new RowDefinition { Height = GridLength.Star    }, // row 8 – bottom gap
            }
        };

        grid.Add(titleLabel,    column: 0, row: 1);
        grid.Add(subtitleLabel, column: 0, row: 3);
        grid.Add(buyButton,     column: 0, row: 5);
        grid.Add(declineButton, column: 0, row: 7);

        Content = new Border
        {
            BackgroundColor = Color.FromArgb("#1E1E1E"),
            Stroke          = Color.FromArgb("#8B4513"),
            StrokeThickness = 2,
            StrokeShape     = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            Padding         = new Thickness(20, 14),
            WidthRequest    = popupW,
            HeightRequest   = popupH,
            Content         = grid
        };
    }

    private async void OnBuyPremiumClicked(object? sender, EventArgs e)
    {
        if (_storeService != null)
        {
            var purchased = await _storeService.PurchaseAsync("premium");
            if (purchased)
            {
                StatusService.Instance.IsPremiumUser = true;
                PurchaseResult = true;
            }
        }
        await CloseAsync();
    }

    private async void OnDeclineClicked(object? sender, EventArgs e)
    {
        // Revert the triggering property before the popup closes
        _onDecline?.Invoke();
        await CloseAsync();
    }
}
