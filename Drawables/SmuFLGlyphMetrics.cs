using SkiaSharp;

namespace musicmate.Drawables
{
    /// <summary>Measures Bravura SMuFL glyph bounds at a given <paramref name="fontSize"/>.</summary>
    internal static class SmuFLGlyphMetrics
    {
        internal readonly struct Layout
        {
            public float Left { get; init; }
            public float Top { get; init; }
            public float Width { get; init; }
            public float Height { get; init; }
        }

        internal static bool TryMeasure(string glyph, float fontSize, out Layout layout)
        {
            layout = default;
            if (!SmuFLFont.IsLoaded || string.IsNullOrEmpty(glyph))
                return false;

            using var paint = new SKPaint
            {
                Typeface     = SmuFLFont.SkiaTypeface,
                TextSize     = fontSize,
                IsAntialias  = true,
                SubpixelText = true
            };

            var bounds = new SKRect();
            paint.MeasureText(glyph, ref bounds);
            if (bounds.Width < 0.5f || bounds.Height < 0.5f)
                return false;

            layout = new Layout
            {
                Left   = bounds.Left,
                Top    = bounds.Top,
                Width  = bounds.Width,
                Height = bounds.Height
            };
            return true;
        }
    }
}
