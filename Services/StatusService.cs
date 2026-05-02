using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Maui.Storage;

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
            // Restore persisted premium state on every cold start
            _isPremiumUser = Preferences.Get(PremiumKey, false);
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
            get => _statusMessage;
            set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    Debug.WriteLine($"[DEBUG] StatusService.StatusMessage changed: {_statusMessage}");
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
