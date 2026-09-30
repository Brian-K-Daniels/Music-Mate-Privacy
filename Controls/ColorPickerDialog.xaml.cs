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

        private double _arrangedWidth;
        private double _arrangedHeight;

        /// <summary>
        /// Fit the landscape dialog to the page. The applies-to group stays on the left
        /// and the color band on the right. Fonts step down only when a smaller phone,
        /// or a visible system bar, would otherwise clip a control.
        /// </summary>
        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            if (width < 1 || height < 1)
                return;
            if (Math.Abs(width - _arrangedWidth) < 0.5 && Math.Abs(height - _arrangedHeight) < 0.5)
                return;

            _arrangedWidth = width;
            _arrangedHeight = height;
            ArrangeForLandscape(width, height);
        }

        private void ArrangeForLandscape(double width, double height)
        {
            var insets = ReadEdgeInsets();
            // Extra edge keeps the card off a gesture bar or 3-button nav when the
            // page already sits inside the system windows (those insets are then 0).
            double marginL = Math.Max(10, insets.Left + 8);
            double marginT = Math.Max(8, insets.Top + 6);
            double marginR = Math.Max(10, insets.Right + 8);
            double marginB = Math.Max(12, insets.Bottom + 8);
            DialogHost.Padding = new Thickness(marginL, marginT, marginR, marginB);

            double availableW = Math.Max(280, width - marginL - marginR);
            double availableH = Math.Max(180, height - marginT - marginB);
            bool shortScreen = availableH < 300;
            double padH = availableW < 560 ? 10 : 14;
            double padV = shortScreen ? 8 : 10;
            double innerW = availableW - padH * 2;
            double titleGap = shortScreen ? 4 : 8;
            double buttonGap = shortScreen ? 6 : 10;
            double stackSpacing = shortScreen ? 8 : 12;
            double buttonH = availableH < 250 ? 44 : 48;

            var fit = ChooseTypeSize(innerW, availableH, padV, titleGap, buttonGap, stackSpacing, buttonH);
            TitleLabel.FontSize = fit.Title;
            AppliesToLabel.FontSize = fit.Body;
            ColorTargetPicker.FontSize = fit.Body;
            ColorLabel.FontSize = fit.Body;
            WhiteLabel.FontSize = fit.Body;
            UpArrowButton.FontSize = fit.Arrow;
            LeftArrowButton.FontSize = fit.Arrow;
            RightArrowButton.FontSize = fit.Arrow;
            DownArrowButton.FontSize = fit.Arrow;
            OkButton.FontSize = fit.Button;
            CloseButton.FontSize = fit.Button;

            TitleLabel.Margin = new Thickness(0, 0, 0, titleGap);
            ButtonRow.Margin = new Thickness(0, buttonGap, 0, 0);
            AppliesColumn.Spacing = stackSpacing;
            SetButtonSize(UpArrowButton, 48, buttonH);
            SetButtonSize(LeftArrowButton, 48, buttonH);
            SetButtonSize(RightArrowButton, 48, buttonH);
            SetButtonSize(DownArrowButton, 48, buttonH);
            SetButtonSize(OkButton, 88, buttonH);
            SetButtonSize(CloseButton, 108, buttonH);

            double titleLine = Math.Ceiling(fit.Title * 1.35);
            double chromeV = padV * 2 + titleLine + titleGap + buttonGap + buttonH;
            // Cap the band so the left column is not a tall empty well beside it.
            // 6dp of slack keeps a slightly taller native picker from pushing the buttons off.
            double bandRoom = availableH - chromeV - 6;
            double bandH = Math.Clamp(bandRoom, 72, 176);

            double columnGap = innerW < 560 ? 12 : 16;
            double preview = Math.Clamp(Math.Round(bandH * 0.42), 48, 72);
            const double bandGap = 12;
            double left = Math.Max(188, EstimateTextWidth("Second panel background", fit.Body) + 56);
            double bandW = innerW - left - columnGap - preview - bandGap;
            const double maxBandW = 520;
            if (bandW > maxBandW)
            {
                left += bandW - maxBandW;
                bandW = maxBandW;
            }

            if (bandW < 140)
            {
                double need = 140 - bandW;
                double shrink = Math.Min(need, Math.Max(0, left - 188));
                left -= shrink;
                bandW += shrink;
            }

            ControlColumns.ColumnDefinitions[0].Width = new GridLength(left);
            ControlColumns.ColumnSpacing = columnGap;
            ColorBandRow.Spacing = bandGap;
            ColorPicker.WidthRequest = Math.Max(120, bandW);
            ColorPicker.HeightRequest = bandH;
            PreviewBox.WidthRequest = preview;
            PreviewBox.HeightRequest = preview;

            DialogPanel.Padding = new Thickness(padH, padV);
            DialogPanel.WidthRequest = availableW;
            DialogPanel.MaximumWidthRequest = availableW;
        }

        private readonly record struct TypeSize(double Title, double Body, double Button, double Arrow);

        private static TypeSize ChooseTypeSize(
            double innerW,
            double availableH,
            double padV,
            double titleGap,
            double buttonGap,
            double stackSpacing,
            double buttonH)
        {
            // Largest first. A step is used only when the title, the longest picker
            // name, the button row, and a usable color band all fit together.
            TypeSize[] steps =
            [
                new(22, 18, 18, 20),
                new(20, 17, 17, 20),
                new(18, 16, 16, 18),
                new(16, 14, 15, 16),
            ];

            foreach (var step in steps)
            {
                if (StepFits(step, innerW, availableH, padV, titleGap, buttonGap, stackSpacing, buttonH))
                    return step;
            }

            return steps[^1];
        }

        private static bool StepFits(
            TypeSize step,
            double innerW,
            double availableH,
            double padV,
            double titleGap,
            double buttonGap,
            double stackSpacing,
            double buttonH)
        {
            double titleW = EstimateTextWidth("Pick a color and set whiteness with slider", step.Title);
            double left = EstimateTextWidth("Second panel background", step.Body) + 56;
            double columnGap = innerW < 560 ? 12 : 16;
            double titleLine = Math.Ceiling(step.Title * 1.35);
            double leftBlock = LeftColumnHeight(step.Body, stackSpacing);
            double chromeV = padV * 2 + titleLine + titleGap + buttonGap + buttonH;
            double middle = Math.Max(96, leftBlock);
            double preview = 72;
            double bandW = innerW - left - columnGap - preview - 12;
            double buttonRow = 48 * 4 + 8 * 3 + 24 + 88 + 24 + 108;
            return titleW <= innerW - 8
                && buttonRow <= innerW - 4
                && left <= innerW * 0.58
                && bandW >= 150
                && chromeV + middle + 6 <= availableH;
        }

        private static double LeftColumnHeight(double body, double stackSpacing)
            => Math.Ceiling(body * 1.4) + stackSpacing + 52 + stackSpacing + 44;

        private static double EstimateTextWidth(string text, double fontSize)
            => text.Length * fontSize * 0.58;

        private static void SetButtonSize(Button button, double width, double height)
        {
            button.WidthRequest = width;
            button.HeightRequest = height;
            button.MinimumWidthRequest = width;
            button.MinimumHeightRequest = height;
        }

        private static (float Left, float Top, float Right, float Bottom) ReadEdgeInsets()
        {
            try
            {
                return ServiceHelper.GetService<ISafeAreaService>()?.GetSafeAreaInsets()
                    ?? (0f, 0f, 0f, 0f);
            }
            catch
            {
                return (0f, 0f, 0f, 0f);
            }
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
