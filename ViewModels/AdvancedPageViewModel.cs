    using System.ComponentModel;
using musicmate.Services;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Graphics;

namespace musicmate.ViewModels
{
    public class AdvancedPageViewModel : INotifyPropertyChanged
    {
        private readonly ThemeService _themeService;
        private readonly NoteSessionService _session;
        
        public AdvancedPageViewModel(ThemeService themeService, NoteSessionService session)
        {
            _themeService = themeService;
            _session = session;

            // Listen for theme changes
            _themeService.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(_themeService.PanelBackgroundColor))
                {
                    OnPropertyChanged(nameof(PanelBackgroundColor));
                    OnPropertyChanged(nameof(ContrastingTextColor));
                }
            };
            // Forward session property changes so the view model updates when other pages modify session settings
            _session.PropertyChanged += (s, e) =>
            {
                // Map session property name to view model properties
                switch (e.PropertyName)
                {
                    case nameof(NoteSessionService.Tolerance):
                        OnPropertyChanged(nameof(Tolerance));
                        break;
                    case nameof(NoteSessionService.RmsThreshold):
                        OnPropertyChanged(nameof(RmsThreshold));
                        break;
                    case nameof(NoteSessionService.CooldownMs):
                        OnPropertyChanged(nameof(CooldownMs));
                        break;
                    case nameof(NoteSessionService.PitchOffsetCents):
                        OnPropertyChanged(nameof(PitchOffsetCents));
                        break;
                    case nameof(NoteSessionService.AudioBufferSize):
                        OnPropertyChanged(nameof(AudioBufferSize));
                        break;
                    case nameof(NoteSessionService.PitchWindowSize):
                        OnPropertyChanged(nameof(PitchWindowSize));
                        break;
                    case nameof(NoteSessionService.MinFrequency):
                        OnPropertyChanged(nameof(MinFrequency));
                        break;
                    case nameof(NoteSessionService.MaxFrequency):
                        OnPropertyChanged(nameof(MaxFrequency));
                        break;
                    case nameof(NoteSessionService.SmoothingWindowSize):
                        OnPropertyChanged(nameof(SmoothingWindowSize));
                        break;
                    case nameof(NoteSessionService.PitchConfidenceThreshold):
                        OnPropertyChanged(nameof(PitchConfidenceThreshold));
                        break;
                    case nameof(NoteSessionService.WrongDebounceMs):
                        OnPropertyChanged(nameof(WrongDebounceMs));
                        break;
                    default:
                        break;
                }
            };
        }

        public Color PanelBackgroundColor => _themeService.PanelBackgroundColor;
        public Color ContrastingTextColor => _themeService.ContrastingTextColor;
        public int AccidentalPercent
        {
            get => _session.AccidentalPercent;
            set { _session.AccidentalPercent = value; OnPropertyChanged(nameof(AccidentalPercent));}
        }
        public int AudioBufferSize
        {
            get => _session.AudioBufferSize;
            set { _session.AudioBufferSize = value; OnPropertyChanged(nameof(AudioBufferSize)); }
        }

        public int PitchWindowSize
        {
            get => _session.PitchWindowSize;
            set { _session.PitchWindowSize = value; OnPropertyChanged(nameof(PitchWindowSize)); }
        }

        public int MinFrequency
        {
            get => _session.MinFrequency;
            set { _session.MinFrequency = value; OnPropertyChanged(nameof(MinFrequency)); }
        }

        public int MaxFrequency
        {
            get => _session.MaxFrequency;
            set { _session.MaxFrequency = value; OnPropertyChanged(nameof(MaxFrequency)); }
        }

        public int SmoothingWindowSize
        {
            get => _session.SmoothingWindowSize;
            set { _session.SmoothingWindowSize = value; OnPropertyChanged(nameof(SmoothingWindowSize)); }
        }

        public double PitchConfidenceThreshold
        {
            get => _session.PitchConfidenceThreshold;
            set { _session.PitchConfidenceThreshold = value; OnPropertyChanged(nameof(PitchConfidenceThreshold)); }
        }

        public int Tolerance
        {
            get => _session.Tolerance;
            set { _session.Tolerance = value; OnPropertyChanged(nameof(Tolerance)); }
        }

        public float RmsThreshold
        {
            get => _session.RmsThreshold;
            set { _session.RmsThreshold = value; OnPropertyChanged(nameof(RmsThreshold)); }
        }

        public int CooldownMs
        {
            get => _session.CooldownMs;
            set { _session.CooldownMs = value; OnPropertyChanged(nameof(CooldownMs)); }
        }

        public int WrongDebounceMs
        {
            get => _session.WrongDebounceMs;
            set { _session.WrongDebounceMs = value; OnPropertyChanged(nameof(WrongDebounceMs)); }
        }

        public double PitchOffsetCents
        {
            get => _session.PitchOffsetCents;
            set { _session.PitchOffsetCents = value; OnPropertyChanged(nameof(PitchOffsetCents)); }
        }

        public double RepeatDelaySeconds
        {
            get => Preferences.Default.Get("RepeatDelaySeconds", 2.0);
            set { Preferences.Default.Set("RepeatDelaySeconds", value); OnPropertyChanged(nameof(RepeatDelaySeconds)); }
        }

        public void ResetToDefaults()
        {
            AccidentalPercent = 0;
            AudioBufferSize = 1024;
            PitchWindowSize = 4096;
            MinFrequency = 60;
            MaxFrequency = 8000;
            SmoothingWindowSize = 3;
            PitchConfidenceThreshold = 0.5;
            Tolerance = 20;
            RmsThreshold = 0.025f;
            CooldownMs = 50;
            PitchOffsetCents = 0;
            WrongDebounceMs = NoteSessionService.DefaultDebounceMs;
            RepeatDelaySeconds = 2.0;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
