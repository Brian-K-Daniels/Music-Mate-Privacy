using Microsoft.Maui.Graphics;

namespace musicmate.Services;

/// <summary>
/// Expands the drawn clef into a phone-sized tap target without covering the time signature.
/// </summary>
public static class ClefHitTargetLayout
{
    public const float MinimumSize = 48f;

    public static RectF Expand(RectF glyph, RectF? timeSignatureBounds)
    {
        const float padY = 10f;
        const float padLeft = 8f;
        const float padRight = 4f;

        float x = glyph.X - padLeft;
        float y = glyph.Y - padY;
        float w = glyph.Width + padLeft + padRight;
        float h = glyph.Height + padY * 2f;

        if (w < MinimumSize)
        {
            x -= MinimumSize - w;
            w = MinimumSize;
        }

        if (h < MinimumSize)
        {
            float extra = MinimumSize - h;
            y -= extra * 0.25f;
            h = MinimumSize;
        }

        if (x < 0)
        {
            w += x;
            x = 0;
        }

        if (y < 0)
        {
            h += y;
            y = 0;
        }

        if (timeSignatureBounds is RectF timeSignature)
        {
            float limit = timeSignature.X - 4f;
            if (x + w > limit)
                w = Math.Max(glyph.Width, limit - x);
        }

        if (w < 8f)
            w = 8f;
        if (h < 8f)
            h = 8f;

        return new RectF(x, y, w, h);
    }
}
