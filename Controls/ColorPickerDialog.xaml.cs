using Maui.ColorPicker;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Timers;
using musicmate.Utilities;

namespace musicmate.Controls
{
    public partial class ColorPickerDialog : ContentView, INotifyPropertyChanged
    {
        public event EventHandler<Color>? ColorPicked;
        public new event PropertyChangedEventHandler? PropertyChanged;
        private Color _previewColor = Colors.White;

        // For arrow button repeat
        private System.Timers.Timer? _arrowTimer;
        private Action? _moveAction;

        public Color PreviewColor
        {
            get => _previewColor;
            set
            {
                if (_previewColor != value)
                {
                    _previewColor = value;
                    OnPropertyChanged();
                }
            }
        }

        public ColorPickerDialog()
        {
            InitializeComponent();
            ColorPicker.PickedColorChanged += OnPickedColorChanged;
            WhitenessSlider.ValueChanged += OnWhitenessSliderChanged;
            UpdatePreviewColor();
        }

        /// <summary>
        /// Show the dialog, restoring the last pointer and whiteness positions if available.
        /// </summary>
        public void Show()
        {
            // Restore pointer and slider positions from preferences (default to center/white)
            ColorPicker.PointerRingPositionXUnits = Preferences.Default.Get("ColorPicker_X", 0.5);
            ColorPicker.PointerRingPositionYUnits = Preferences.Default.Get("ColorPicker_Y", 0.5);
            WhitenessSlider.Value = Preferences.Default.Get("ColorPicker_Whiteness", 0.8);

            UpdatePreviewColor();
            IsVisible = true;
        }

        /// <summary>
        /// Show the dialog, restoring the last saved pointer and whiteness positions.
        /// The color argument is ignored — the saved positions already encode the color.
        /// </summary>
        public void Show(Color staffPanelColor)
        {
            // Restore saved pointer and slider positions — do NOT set PickedColor because
            // the library resets X/Y when PickedColor is assigned, moving the dot to the bottom.
            ColorPicker.PointerRingPositionXUnits = Preferences.Default.Get("ColorPicker_X", 0.5);
            ColorPicker.PointerRingPositionYUnits = Preferences.Default.Get("ColorPicker_Y", 0.5);
            WhitenessSlider.Value = Preferences.Default.Get("ColorPicker_Whiteness", 0.8);

            UpdatePreviewColor();
            IsVisible = true;
        }

        private void OnPickedColorChanged(object? sender, Maui.ColorPicker.PickedColorChangedEventArgs e)
        {
            UpdatePreviewColor();
        }

        private void OnWhitenessSliderChanged(object? sender, ValueChangedEventArgs e)
        {
            UpdatePreviewColor();
        }

        private void UpdatePreviewColor()
        {
            var baseColor = ColorPicker.PickedColor;
            baseColor ??= Colors.AliceBlue;  //  2026.04.02 0931  
            //if (baseColor == null)
            //{
            //    PreviewColor = Colors.Transparent;
            //    return;
            //}
            var whiteness = Math.Clamp(WhitenessSlider.Value, 0.0, 1.0);
            double a = whiteness;
            double b = 1 - whiteness;
            var mixed = new Color(
                (float)(baseColor.Red * b + a),
                (float)(baseColor.Green * b + a),
                (float)(baseColor.Blue * b + a),
                baseColor.Alpha
            );
            PreviewColor = mixed;
            ColorPreviewed?.Invoke(this, mixed);
        }

        public event EventHandler<Color>? ColorPreviewed;

        private void OnOkClicked(object sender, EventArgs e)
        {
            // Persist pointer and slider positions
            Preferences.Default.Set("ColorPicker_X", ColorPicker.PointerRingPositionXUnits);
            Preferences.Default.Set("ColorPicker_Y", ColorPicker.PointerRingPositionYUnits);
            Preferences.Default.Set("ColorPicker_Whiteness", WhitenessSlider.Value);

            ColorPicked?.Invoke(this, PreviewColor);
            this.IsVisible = false;
        }

        /// <summary>
        /// Programmatically confirm the current color selection (equivalent to tapping OK).
        /// Useful when callers want to apply the dialog color without showing the UI.
        /// </summary>
        public void Confirm()
        {
            // Ensure preview is up to date
            UpdatePreviewColor();
            // Persist pointer and slider positions
            Preferences.Default.Set("ColorPicker_X", ColorPicker.PointerRingPositionXUnits);
            Preferences.Default.Set("ColorPicker_Y", ColorPicker.PointerRingPositionYUnits);
            Preferences.Default.Set("ColorPicker_Whiteness", WhitenessSlider.Value);

            ColorPicked?.Invoke(this, PreviewColor);
            this.IsVisible = false;
        }

        protected new void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // Arrow button handlers
        private void OnLeftArrowPressed(object sender, EventArgs e) => StartArrowMove(() => MoveSelectedPoint(-1, 0));
        private void OnRightArrowPressed(object sender, EventArgs e) => StartArrowMove(() => MoveSelectedPoint(1, 0));
        private void OnUpArrowPressed(object sender, EventArgs e) => StartArrowMove(() => MoveSelectedPoint(0, -1));
        private void OnDownArrowPressed(object sender, EventArgs e) => StartArrowMove(() => MoveSelectedPoint(0, 1));
        private void OnArrowReleased(object sender, EventArgs e) => StopArrowMove();

        private void StartArrowMove(Action moveAction)
        {
            _moveAction = moveAction;
            _moveAction?.Invoke(); // Move once immediately
            _arrowTimer = new System.Timers.Timer(60); // 60ms repeat
            _arrowTimer.Elapsed += (s, e) => MainThread.BeginInvokeOnMainThread(() => _moveAction?.Invoke());
            _arrowTimer.Start();
        }
        private void StopArrowMove()
        {
            _arrowTimer?.Stop();
            _arrowTimer?.Dispose();
            _arrowTimer = null;
        }

        /// <summary>
        /// Reset pointer and whiteness slider to factory defaults (center position, 80% white).
        /// </summary>
        public void ResetToDefaults()
        {
            ColorPicker.PointerRingPositionXUnits = 0.5;
            ColorPicker.PointerRingPositionYUnits = 0.5;
            WhitenessSlider.Value = 0.8;
            UpdatePreviewColor();
        }

        // You must implement this method in your ColorPicker control or expose X/Y properties
        private void MoveSelectedPoint(int dx, int dy)
        {
            const double m = 0.005;
            double dxd = m * dx;
            double dyd = m * dy;
            ColorPicker.PointerRingPositionXUnits += dxd;
            ColorPicker.PointerRingPositionYUnits += dyd;
            double x = ColorPicker.PointerRingPositionXUnits;
            double y = ColorPicker.PointerRingPositionYUnits;
            // Utils.Log($"Moving by {dxd}, {dyd} to {x:000.0} {y:000.0}");
        }
    }
}
