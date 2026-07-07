using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Maui.Storage;
using musicmate.Diagnostics;

namespace musicmate.Services
{
    public class StatusService : INotifyPropertyChanged
    {
        private const string PremiumKey = "IsPremium";

        // Singleton instance
        public static StatusService Instance { get; } = new StatusService();

        private bool _isPremiumUser;

        private StatusService()
        {
#if DEBUG
            // In debug, restore persisted state so testers don't lose premium on restart.
            _isPremiumUser = Preferences.Get(PremiumKey, false);
#else
            // In release, always start as non-premium. The store sync in App.xaml.cs
            // will set the real value from Google Play after startup.
            _isPremiumUser = false;
#endif
        }

        public bool IsPremiumUser
        {
            get => _isPremiumUser;
            set
            {
                if (_isPremiumUser != value)
                {
                    _isPremiumUser = value;
                    Preferences.Set(PremiumKey, value);   // persist immediately
                    OnPropertyChanged(nameof(IsPremiumUser));
                }
            }
        }

        private string _statusMessage = "";
        public string StatusMessage
        {
            get => "  " + _statusMessage;  //  2026.06.11 1204  space to give a little separation from the stop/start button
            set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    DebugLog.WriteLine($"[DEBUG] StatusService.StatusMessage changed: {_statusMessage}");
                    OnPropertyChanged(nameof(StatusMessage));
                }
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
