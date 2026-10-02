using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;
using musicmate.Diagnostics;

namespace musicmate.Services
{
    public class StatusService : INotifyPropertyChanged
    {
        public static StatusService Instance { get; } = new StatusService();

        public const string CountInStatusMessage = "Count-in… play the first note when ready.";
        public static readonly TimeSpan CountInStatusDuration = TimeSpan.FromSeconds(2);

        internal static Func<TimeSpan, CancellationToken, Task>? TemporaryMessageDelayOverrideForTests;

        private bool _isPremiumUser;
        private int _temporaryMessageGeneration;
        private string? _temporaryMessageBaseline;
        private string? _activeTemporaryMessage;
        private CancellationTokenSource? _temporaryMessageCts;

        private StatusService()
        {
#if DEBUG || LOCAL_RELEASE
            _isPremiumUser = SessionPreferences.Get(PremiumProduct.PreferenceKey, false);
#else
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
                    RaisePropertyChanged(nameof(IsPremiumUser));
                }
            }
        }

        public bool IsTemporaryMessageActive => _activeTemporaryMessage != null;

        internal string RawStatusMessage => _statusMessage;

        private string _statusMessage = "";
        public string StatusMessage
        {
            get => "  " + _statusMessage;
            set
            {
                if (_activeTemporaryMessage != null && value != _activeTemporaryMessage)
                    CancelActiveTemporaryMessage();

                SetStatusMessageCore(value);
            }
        }

        public void ShowTemporaryMessage(string message, TimeSpan duration)
        {
            int myGeneration = Interlocked.Increment(ref _temporaryMessageGeneration);
            CancelTemporaryMessageTimer();

            string preserve = _activeTemporaryMessage != null && _statusMessage == _activeTemporaryMessage
                ? _temporaryMessageBaseline ?? ""
                : _statusMessage;

            _temporaryMessageBaseline = preserve;
            _activeTemporaryMessage = message;
            SetStatusMessageCore(message);

            _temporaryMessageCts = new CancellationTokenSource();
            _ = RunTemporaryMessageRestoreAsync(myGeneration, message, duration, _temporaryMessageCts.Token);
        }

        internal void ResetTemporaryMessageStateForTests()
        {
            Interlocked.Increment(ref _temporaryMessageGeneration);
            CancelTemporaryMessageTimer();
            _activeTemporaryMessage = null;
            _temporaryMessageBaseline = null;
        }

        private async Task RunTemporaryMessageRestoreAsync(
            int generation,
            string temporaryMessage,
            TimeSpan duration,
            CancellationToken ct)
        {
            try
            {
                var delay = TemporaryMessageDelayOverrideForTests ?? Task.Delay;
                await delay(duration, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (generation != Volatile.Read(ref _temporaryMessageGeneration))
                return;

            if (_activeTemporaryMessage == temporaryMessage && _statusMessage == temporaryMessage)
                SetStatusMessageCore(_temporaryMessageBaseline ?? "");

            if (generation == Volatile.Read(ref _temporaryMessageGeneration))
            {
                _activeTemporaryMessage = null;
                _temporaryMessageBaseline = null;
            }
        }

        private void CancelActiveTemporaryMessage()
        {
            Interlocked.Increment(ref _temporaryMessageGeneration);
            CancelTemporaryMessageTimer();
            _activeTemporaryMessage = null;
            _temporaryMessageBaseline = null;
        }

        private void CancelTemporaryMessageTimer()
        {
            try { _temporaryMessageCts?.Cancel(); } catch { }
            try { _temporaryMessageCts?.Dispose(); } catch { }
            _temporaryMessageCts = null;
        }

        private void SetStatusMessageCore(string value)
        {
            if (_statusMessage == value)
                return;

            _statusMessage = value;
            DebugLog.WriteLine($"[DEBUG] StatusService.StatusMessage changed: {_statusMessage}");
            RaisePropertyChanged(nameof(StatusMessage));
        }

        private void RaisePropertyChanged(string propertyName)
        {
            if (TryDispatchPropertyChanged(propertyName))
                return;

            OnPropertyChanged(propertyName);
        }

        private bool TryDispatchPropertyChanged(string propertyName)
        {
            try
            {
                if (MainThread.IsMainThread)
                {
                    OnPropertyChanged(propertyName);
                    return true;
                }

                MainThread.BeginInvokeOnMainThread(() => OnPropertyChanged(propertyName));
                return true;
            }
            catch (Exception ex) when (IsNoUiDispatcherAvailable(ex))
            {
                return false;
            }
        }

        private static bool IsNoUiDispatcherAvailable(Exception ex)
            => ex is COMException or InvalidOperationException or NotSupportedException;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
