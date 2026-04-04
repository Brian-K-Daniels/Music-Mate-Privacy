using System.ComponentModel;
using System.Diagnostics;

namespace musicmate.Services
{
    public class StatusService : INotifyPropertyChanged
    {
        // Singleton instance
        public static StatusService Instance { get; } = new StatusService();

        private bool _isPremiumUser;
        public bool IsPremiumUser
        {
            get => _isPremiumUser;
            set
            {
                if (_isPremiumUser != value)
                {
                    _isPremiumUser = value;
                    OnPropertyChanged(nameof(IsPremiumUser)); // Use OnPropertyChanged for consistency
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
