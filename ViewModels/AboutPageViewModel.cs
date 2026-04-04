using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Maui.Graphics;
using musicmate.Services;
using Microsoft.Maui.Storage;

namespace musicmate.ViewModels
{
    public class AboutPageViewModel : INotifyPropertyChanged
    {
        private const string PremiumKey = "IsPremium";
        private Color _backgroundColor = Colors.White;
        private double _selectedFontSize;
        ThemeService? _themeService;
        private const string AboutFontSizeKey = "About_FontSize";
        private readonly IStoreService? _storeService;

        public AboutPageViewModel(ThemeService theme)
        {
            FontSizeOptions = new ObservableCollection<double> { 6, 8, 10, 12, 14 };
            // Default selection from preferences or fallback to 12
            var defaultSize = FontSizeOptions[3];
            var saved = Preferences.Get(AboutFontSizeKey, defaultSize);
            SelectedFontSize = FontSizeOptions.Contains(saved) ? saved : defaultSize;

            _themeService = theme;
            _backgroundColor = _themeService.PanelBackgroundColor;

            // Resolve store service (may be local stub)
            _storeService = ServiceHelper.GetService<IStoreService>();

            BuyPremiumCommand = new Command(async () =>
            {
                try { await BuyPremiumAsync(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"BuyPremium error: {ex}"); }
            });
            RestorePurchasesCommand = new Command(async () =>
            {
                try { await RestorePurchasesAsync(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"RestorePurchases error: {ex}"); }
            });

            // Keep view model in sync with ThemeService so About page matches Settings page live
            _themeService.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ThemeService.PanelBackgroundColor))
                {
                    PanelBackgroundColor = _themeService.PanelBackgroundColor;
                }
            };

            // Sync IsPremium with StatusService
            IsPremium = Services.StatusService.Instance.IsPremiumUser;
            Services.StatusService.Instance.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Services.StatusService.IsPremiumUser))
                {
                    IsPremium = Services.StatusService.Instance.IsPremiumUser;
                }
            };
        }

        public Color PanelBackgroundColor
        {
            get => _backgroundColor;
            set
            {
                if (_backgroundColor != value)
                {
                    _backgroundColor = value;
                    OnPropertyChanged(nameof(PanelBackgroundColor));
                    OnPropertyChanged(nameof(ContrastingTextColor));
                }
            }
        }

        public Color ContrastingTextColor
        {
            get
            {
                double luminance = 0.299 * PanelBackgroundColor.Red + 0.587 * PanelBackgroundColor.Green + 0.114 * PanelBackgroundColor.Blue;
                return luminance > 0.5 ? Colors.Black : Colors.White;
            }
        }

        public ObservableCollection<double> FontSizeOptions { get; }

        public double SelectedFontSize
        {
            get => _selectedFontSize;
            set
            {
                if (_selectedFontSize != value)
                {
                    _selectedFontSize = value;
                    Preferences.Set(AboutFontSizeKey, value);
                    OnPropertyChanged(nameof(SelectedFontSize));
                }
            }
        }

        private bool _isPremium;
        public bool IsPremium
        {
            get => _isPremium;
            set
            {
                if (_isPremium != value)
                {
                    _isPremium = value;
                    Preferences.Set(PremiumKey, value);
                    StatusService.Instance.IsPremiumUser = value;
                    OnPropertyChanged(nameof(IsPremium));
                }
            }
        }

        public ICommand BuyPremiumCommand { get; }
        public ICommand RestorePurchasesCommand { get; }

        private async Task BuyPremiumAsync()
        {
            System.Diagnostics.Debug.WriteLine($"BuyPremiumAsync called. StoreService={_storeService?.GetType().Name}");
            if (_storeService != null)
            {
                var ok = await _storeService.PurchaseAsync("premium");
                System.Diagnostics.Debug.WriteLine($"PurchaseAsync returned: {ok}");
                if (ok)
                {
                    MainThread.BeginInvokeOnMainThread(() => IsPremium = true);
                }
            }
            else
            {
                MainThread.BeginInvokeOnMainThread(() => IsPremium = true);
            }
        }

        private async Task RestorePurchasesAsync()
        {
            System.Diagnostics.Debug.WriteLine($"RestorePurchasesAsync called. StoreService={_storeService?.GetType().Name}");
            if (_storeService != null)
            {
                var ok = await _storeService.RestorePurchasesAsync();
                System.Diagnostics.Debug.WriteLine($"RestorePurchasesAsync returned: {ok}");
                MainThread.BeginInvokeOnMainThread(() => IsPremium = ok);
            }
            else
            {
                // No store service available — nothing to restore
                MainThread.BeginInvokeOnMainThread(() => IsPremium = false);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
