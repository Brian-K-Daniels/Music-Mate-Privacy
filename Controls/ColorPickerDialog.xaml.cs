using System.Collections.Generic;
using System.Linq;
using Maui.ColorPicker;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Timers;
using Microsoft.Maui.Storage;
using musicmate.Services;
using musicmate.Utilities;

namespace musicmate.Controls
{
    public sealed class ColorTargetOption
    {
        public ColorTargetOption(AppColorTarget target, string displayName)
        {
            Target = target;
            DisplayName = displayName;
        }

        public AppColorTarget Target { get; }
        public string DisplayName { get; }
    }

    public partial class ColorPickerDialog : ContentView, INotifyPropertyChanged
    {
        public event EventHandler<Color>? ColorPicked;
        public event EventHandler<AppColorPickedEventArgs>? AppColorPicked;
        public new event PropertyChangedEventHandler? PropertyChanged;
        private Color _previewColor = Colors.White;
        private AppColorTarget _selectedTarget = AppColorTarget.PanelBackground;
        private bool _isRestoringPicker;
        private bool _isSuccessFlashActive;

        // For arrow button repeat
        private System.Timers.Timer? _arrowTimer;
        private Action? _moveAction;

        public AppColorTarget SelectedTarget
        {
            get => _selectedTarget;
            set
            {
                if (_selectedTarget != value)
                {
                    _selectedTarget = value;
                    OnPropertyChanged();
                }
            }
        }

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

            var options = ThemeService.AllColorTargets
                .Select(t => new ColorTargetOption(t, ThemeService.GetDisplayName(t)))
                .ToList();
            ColorTargetPicker.ItemsSource = options;
            ColorTargetPicker.ItemDisplayBinding = new Binding(nameof(ColorTargetOption.DisplayName));
            ColorTargetPicker.SelectedIndexChanged += (_, _) =>
            {
                if (_isRestoringPicker)
                    return;

                if (ColorTargetPicker.SelectedItem is ColorTargetOption option)
                    LoadPickerForTarget(option.Target);
            };
            ColorTargetPicker.SelectedIndex = options.FindIndex(o => o.Target == AppColorTarget.PanelBackground);

            EnsurePickerReadableColors();
            UpdatePreviewColor();
        }

        /// <summary>
        /// Show the dialog, restoring the last pointer and whiteness positions if available.
        /// </summary>
        public void Show()
        {
            Show(AppColorTarget.PanelBackground);
        }

        /// <summary>
        /// Show the dialog for the given color target.
        /// </summary>
        public void Show(AppColorTarget target)
        {
            _isRestoringPicker = true;
            try
            {
                SelectedTarget = target;
                if (ColorTargetPicker.ItemsSource is IList<ColorTargetOption> options)
                {
                    var index = options.ToList().FindIndex(o => o.Target == target);
                    if (index >= 0)
                        ColorTargetPicker.SelectedIndex = index;
                }
            }
            finally
            {
                _isRestoringPicker = false;
            }

            LoadPickerForTarget(target);
            EnsurePickerReadableColors();
            IsVisible = true;
        }

        private void EnsurePickerReadableColors()
        {
            // Dialog panel is always white; override app/global picker theme so text stays readable.
            ColorTargetPicker.TextColor = Colors.Black;
            ColorTargetPicker.TitleColor = Color.FromArgb("#444444");
            ColorTargetPicker.BackgroundColor = Colors.White;
        }

        /// <summary>
        /// Show the dialog for panel background (legacy overload).
        /// </summary>
        public void Show(Color staffPanelColor)
        {
            Show(AppColorTarget.PanelBackground);
        }

        private void LoadPickerForTarget(AppColorTarget target)
        {
            SelectedTarget = target;
            _isRestoringPicker = true;
            try
            {
                var existing = GetExistingColorForTarget(target);
                if (!TryRestorePickerStateForTarget(target, existing))
                {
                    var (baseColor, whiteness) = ColorPickerColorMapper.Decompose(existing);
                    WhitenessSlider.Value = whiteness;
                    ColorPicker.PickedColor = baseColor;
                }
            }
            finally
            {
                _isRestoringPicker = false;
                UpdatePreviewColor();
                Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(50), UpdatePreviewColor);
            }
        }

        private Color GetExistingColorForTarget(AppColorTarget target)
        {
            var theme = ServiceHelper.GetService<ThemeService>();
            return theme?.GetColor(target) ?? Colors.White;
        }

        private static string PositionKey(AppColorTarget target, string suffix)
            => $"musicmate.colorPicker.pos.{target}.{suffix}";

        private bool TryRestorePickerStateForTarget(AppColorTarget target, Color expectedColor)
        {
            var xKey = PositionKey(target, "X");
            if (!Preferences.Default.ContainsKey(xKey))
                return false;

            var x = Preferences.Default.Get(xKey, 0.5);
            var y = Preferences.Default.Get(PositionKey(target, "Y"), 0.5);
            var w = Preferences.Default.Get(PositionKey(target, "Whiteness"), 0.8);

            ColorPicker.PointerRingPositionXUnits = x;
            ColorPicker.PointerRingPositionYUnits = y;
            WhitenessSlider.Value = w;

            var baseColor = ColorPicker.PickedColor ?? Colors.AliceBlue;
            var restored = ColorPickerColorMapper.MixWithWhiteness(baseColor, w);
            return ColorPickerColorMapper.ColorDistance(restored, expectedColor) < 0.02;
        }

        private void SavePickerStateForTarget(AppColorTarget target)
        {
            Preferences.Default.Set(PositionKey(target, "X"), ColorPicker.PointerRingPositionXUnits);
            Preferences.Default.Set(PositionKey(target, "Y"), ColorPicker.PointerRingPositionYUnits);
            Preferences.Default.Set(PositionKey(target, "Whiteness"), WhitenessSlider.Value);
        }

        private void RestorePickerPositions()
        {
            LoadPickerForTarget(SelectedTarget);
        }

        private void OnPickedColorChanged(object? sender, Maui.ColorPicker.PickedColorChangedEventArgs e)
        {
            if (_isRestoringPicker)
                return;

            UpdatePreviewColor();
        }

        private void OnWhitenessSliderChanged(object? sender, ValueChangedEventArgs e)
        {
            if (_isRestoringPicker)
                return;

            UpdatePreviewColor();
        }

        private void UpdatePreviewColor()
        {
            var baseColor = ColorPicker.PickedColor;
            baseColor ??= Colors.AliceBlue;
            PreviewColor = ColorPickerColorMapper.MixWithWhiteness(baseColor, WhitenessSlider.Value);
            ColorPreviewed?.Invoke(this, PreviewColor);
        }

        public event EventHandler<Color>? ColorPreviewed;
        public event EventHandler? DialogClosed;

        private void OnOkClicked(object sender, EventArgs e)
        {
            ApplyCurrentSelection();
        }

        private void OnCloseClicked(object sender, EventArgs e)
        {
            Close();
        }

        /// <summary>Hides the dialog without applying the current preview color.</summary>
        public void Close()
        {
            if (!IsVisible)
                return;

            IsVisible = false;
            DialogClosed?.Invoke(this, EventArgs.Empty);
        }

        private void ApplyCurrentSelection()
        {
            UpdatePreviewColor();
            Preferences.Default.Set("ColorPicker_X", ColorPicker.PointerRingPositionXUnits);
            Preferences.Default.Set("ColorPicker_Y", ColorPicker.PointerRingPositionYUnits);
            Preferences.Default.Set("ColorPicker_Whiteness", WhitenessSlider.Value);
            SavePickerStateForTarget(SelectedTarget);

            ColorPicked?.Invoke(this, PreviewColor);
            AppColorPicked?.Invoke(this, new AppColorPickedEventArgs(SelectedTarget, PreviewColor));
            _ = FlashApplySuccessAsync();
        }

        private async Task FlashApplySuccessAsync()
        {
            if (!IsVisible || _isSuccessFlashActive)
                return;

            _isSuccessFlashActive = true;
            try
            {
                const uint pulseMs = 100;
                var panelColor = DialogPanel.BackgroundColor;
                var okColor = OkButton.BackgroundColor;

                DialogPanel.BackgroundColor = Color.FromArgb("#C8E6C9");
                OkButton.BackgroundColor = Color.FromArgb("#2E7D32");

                await PreviewBox.ScaleToAsync(1.18, pulseMs, Easing.CubicOut);
                await Task.Delay(60);
                DialogPanel.BackgroundColor = panelColor;
                OkButton.BackgroundColor = okColor;
                await PreviewBox.ScaleToAsync(1.0, pulseMs, Easing.CubicIn);
            }
            finally
            {
                _isSuccessFlashActive = false;
            }
        }

        /// <summary>
        /// Programmatically confirm the current color selection (equivalent to tapping OK).
        /// Useful when callers want to apply the dialog color without showing the UI.
        /// </summary>
        public void Confirm()
        {
            ApplyCurrentSelection();
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
