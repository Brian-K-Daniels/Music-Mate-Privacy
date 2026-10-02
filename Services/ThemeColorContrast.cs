using Microsoft.Maui.Graphics;

namespace musicmate.Services;

/// <summary>Simple luminance-based contrast helpers for themed text on colored backgrounds.</summary>
public static class ThemeColorContrast
{
    public const double MinimumLuminanceSeparation = 0.35;

    public static double GetLuminance(Color color)
        => 0.299 * color.Red + 0.587 * color.Green + 0.114 * color.Blue;

    public static Color GetContrastingTextColor(Color background)
        => GetLuminance(background) > 0.5 ? Colors.Black : Colors.White;

    public static bool HasReadableContrast(Color foreground, Color background)
        => Math.Abs(GetLuminance(foreground) - GetLuminance(background)) >= MinimumLuminanceSeparation;

    /// <summary>Returns requested text when readable; otherwise black or white on the background.</summary>
    public static Color ResolveReadableText(Color requestedText, Color background)
        => HasReadableContrast(requestedText, background)
            ? requestedText
            : GetContrastingTextColor(background);
}
