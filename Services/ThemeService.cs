using System.ComponentModel;
using Microsoft.Maui.Graphics;

namespace musicmate.Services
{
    public class ThemeService : INotifyPropertyChanged
    {
        public Color CurrentNoteHighlightColor => Colors.Yellow.WithAlpha(0.95f);
        public Color CorrectNoteColor => Colors.LightGreen.WithAlpha(0.95f);
        public Color WrongNoteColor => Colors.LightPink.WithAlpha(0.95f);
        private Color _panelBackgroundColor = Colors.White;
        public Color PanelBackgroundColor
        {
            get => _panelBackgroundColor;
            set
            {
                if (_panelBackgroundColor != value)
                {
                    _panelBackgroundColor = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PanelBackgroundColor)));
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

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
