using Microsoft.Maui.Graphics;
using musicmate.Models;

namespace musicmate.Services;

/// <summary>
/// Distinctive Note Mastery status colors that stay readable on light or dark panel backgrounds.
/// </summary>
public static class NoteMasteryStatusColors
{
    // Light backgrounds: deeper hues. Dark backgrounds: brighter hues. Hue identity stays fixed.
    private static readonly Color MasteredOnLight = Color.FromArgb("#1B7A3A");
    private static readonly Color MasteredOnDark = Color.FromArgb("#7DDB8E");
    private static readonly Color ImprovingOnLight = Color.FromArgb("#0D47A1");
    private static readonly Color ImprovingOnDark = Color.FromArgb("#74B9FF");
    private static readonly Color NeedsPracticeOnLight = Color.FromArgb("#C43E00");
    private static readonly Color NeedsPracticeOnDark = Color.FromArgb("#FFB74D");
    private static readonly Color NotAttemptedOnLight = Color.FromArgb("#5F6368");
    private static readonly Color NotAttemptedOnDark = Color.FromArgb("#B0B3B8");

    public static Color ForState(NoteMasteryState state, Color background)
    {
        bool darkBackground = ThemeColorContrast.GetLuminance(background) < 0.45;
        Color candidate = state switch
        {
            NoteMasteryState.Mastered => darkBackground ? MasteredOnDark : MasteredOnLight,
            NoteMasteryState.Improving => darkBackground ? ImprovingOnDark : ImprovingOnLight,
            NoteMasteryState.NeedsPractice => darkBackground ? NeedsPracticeOnDark : NeedsPracticeOnLight,
            _ => darkBackground ? NotAttemptedOnDark : NotAttemptedOnLight,
        };

        return ThemeColorContrast.HasReadableContrast(candidate, background)
            ? candidate
            : ThemeColorContrast.GetContrastingTextColor(background);
    }
}
