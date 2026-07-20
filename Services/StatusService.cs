using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Maui.Storage;
using musicmate.Diagnostics;

namespace musicmate.Services
{
    public class StatusService : INotifyPropertyChanged
    {
        // Singleton instance
        public static StatusService Instance { get; } = new StatusService();

        private bool _isPremiumUser;

        private StatusService()
        {
#if DEBUG
            // In debug, restore persisted state so testers don't lose premium on restart.
            _isPremiumUser = SessionPreferences.Get(PremiumProduct.PreferenceKey, false);
#else
            // In release, always start as non-premium. App.InitializePremiumStatus clears any
            // backup-restored flag, then grants premium only if the store confirms ownership.
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
                    if (value)
                        SessionPreferences.Set(PremiumProduct.PreferenceKey, true);
                    else
                        SessionPreferences.Remove(PremiumProduct.PreferenceKey);
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
