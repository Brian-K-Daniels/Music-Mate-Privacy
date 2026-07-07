using Microsoft.Maui.Graphics;

namespace musicmate.Controls;

/// <summary>Maps between theme colors and color-picker base color + whiteness slider.</summary>
public static class ColorPickerColorMapper
{
    public static Color MixWithWhiteness(Color baseColor, double whiteness)
    {
        var w = Math.Clamp(whiteness, 0.0, 1.0);
        var blend = 1.0 - w;
        return new Color(
            (float)(baseColor.Red * blend + w),
            (float)(baseColor.Green * blend + w),
            (float)(baseColor.Blue * blend + w),
            baseColor.Alpha);
    }

    public static double ColorDistance(Color a, Color b)
    {
        double dr = a.Red - b.Red;
        double dg = a.Green - b.Green;
        double db = a.Blue - b.Blue;
        return dr * dr + dg * dg + db * db;
    }

    /// <summary>Inverse of MixWithWhiteness: finds base + whiteness that reproduce target.</summary>
    public static (Color BaseColor, double Whiteness) Decompose(Color target)
    {
        double tr = target.Red;
        double tg = target.Green;
        double tb = target.Blue;

        if (tr > 0.985 && tg > 0.985 && tb > 0.985)
            return (Color.FromRgb(0.5f, 0.5f, 0.5f), 1.0);

        (Color BaseColor, double Whiteness)? best = null;
        double bestSaturation = -1;

        for (double w = 1.0; w >= 0.0; w -= 0.01)
        {
            if (w > 0.995)
            {
                if (tr > 0.9 && tg > 0.9 && tb > 0.9)
                    return (Color.FromRgb(0.5f, 0.5f, 0.5f), 1.0);
                continue;
            }

            double br = (tr - w) / (1.0 - w);
            double bg = (tg - w) / (1.0 - w);
            double bb = (tb - w) / (1.0 - w);

            if (br < -0.02 || br > 1.02 || bg < -0.02 || bg > 1.02 || bb < -0.02 || bb > 1.02)
                continue;

            var baseColor = new Color(
                (float)Math.Clamp(br, 0, 1),
                (float)Math.Clamp(bg, 0, 1),
                (float)Math.Clamp(bb, 0, 1),
                target.Alpha);

            double saturation = Math.Max(baseColor.Red, Math.Max(baseColor.Green, baseColor.Blue))
                - Math.Min(baseColor.Red, Math.Min(baseColor.Green, baseColor.Blue));
            if (saturation > bestSaturation)
            {
                bestSaturation = saturation;
                best = (baseColor, w);
            }
        }

        return best ?? (target, 0.0);
    }
}
