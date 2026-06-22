using Microsoft.Maui.Graphics;

namespace musicmate.Drawables
{
    /// <summary>
    /// Draws SMuFL eighth-note flags (Bravura <c>flag8thUp</c> / <c>flag8thDown</c>) at stem tips.
    /// SMuFL flag glyphs register their origin at the stem attachment point (y = 0).
    /// </summary>
    internal static class SmuFLFlagDrawer
    {
        private const string Flag8thUp = "\uE240";
        private const string Flag8thDown = "\uE241";

        /// <summary>Two eighth-style flags spaced along the stem (isolated sixteenth notes).</summary>
        internal static void DrawSixteenth(
            ICanvas canvas,
            bool stemUp,
            float stemX,
            float stemTipY,
            float sls,
            float glyphScale,
            Color color)
        {
            Draw(canvas, stemUp, stemX, stemTipY, sls, glyphScale, color);
            float spacing = sls * 0.52f * glyphScale;
            float secondTipY = stemUp ? stemTipY + spacing : stemTipY - spacing;
            Draw(canvas, stemUp, stemX, secondTipY, sls, glyphScale, color);
        }

        internal static void Draw(
            ICanvas canvas,
            bool stemUp,
            float stemX,
            float stemTipY,
            float sls,
            float glyphScale,
            Color color)
        {
            if (!SmuFLFont.IsLoaded)
            {
                DrawVectorFallback(canvas, stemUp, stemX, stemTipY, sls, color);
                return;
            }

            string glyph = stemUp ? Flag8thUp : Flag8thDown;
            float fontSize = sls * 2.0f * glyphScale;

            canvas.SaveState();
            try
            {
                // 1) Origin-anchored raster — matches SMuFL stem-tip registration on all platforms.
                if (SmuFLRestRaster.TryDrawGlyphAtOrigin(canvas, glyph, stemX, stemTipY, fontSize, color))
                    return;

                // 2) Android platform text in a tight metrics box (origin at top-left of bounds).
                if (SmuFLGlyphMetrics.TryMeasure(glyph, fontSize, out var m))
                {
                    float left = stemX + m.Left;
                    float top = stemTipY + m.Top;
                    if (PlatformRestText.DrawAligned(canvas, glyph, fontSize, color,
                            left, top, m.Width, m.Height,
                            HorizontalAlignment.Left, VerticalAlignment.Top))
                        return;
                }

                DrawVectorFallback(canvas, stemUp, stemX, stemTipY, sls, color);
            }
            finally
            {
                canvas.RestoreState();
            }
        }

        private static void DrawVectorFallback(
            ICanvas canvas,
            bool stemUp,
            float stemX,
            float stemTipY,
            float sls,
            Color color)
        {
            float flagW = sls * 1.55f;
            float flagH = sls * 1.15f;
            canvas.StrokeColor = color;
            canvas.StrokeSize = Math.Max(1.5f, sls * 0.14f);

            if (stemUp)
            {
                canvas.DrawLine(stemX, stemTipY, stemX + flagW, stemTipY + flagH * 0.5f);
                canvas.DrawLine(stemX + flagW, stemTipY + flagH * 0.5f, stemX + flagW * 0.5f, stemTipY + flagH);
            }
            else
            {
                canvas.DrawLine(stemX, stemTipY, stemX + flagW, stemTipY - flagH * 0.5f);
                canvas.DrawLine(stemX + flagW, stemTipY - flagH * 0.5f, stemX + flagW * 0.5f, stemTipY - flagH);
            }
        }
    }
}
