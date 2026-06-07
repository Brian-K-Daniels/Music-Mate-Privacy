using System.Collections.Concurrent;
using Microsoft.Maui.Graphics;
using musicmate.Models;
using SkiaSharp;

namespace musicmate.Drawables
{
    /// <summary>
    /// Fallback: rasterize rests off-screen and draw via ICanvas.DrawImage (respects ScalingCanvas).
    /// </summary>
    internal static class SmuFLRestRaster
    {
        private static readonly ConcurrentDictionary<string, byte[]> PngCache = new();

        private static readonly Dictionary<NoteDuration, string> Glyphs = new()
        {
            [NoteDuration.Whole]     = "\uE4E3",
            [NoteDuration.Half]      = "\uE4E4",
            [NoteDuration.Quarter]   = "\uE4E5",
            [NoteDuration.Eighth]    = "\uE4E6",
            [NoteDuration.Sixteenth] = "\uE4E7",
        };

        internal static bool TryDraw(
            ICanvas canvas,
            NoteDuration duration,
            float left,
            float top,
            float width,
            float height,
            float fontSize,
            Color color)
        {
            if (!SmuFLFont.IsLoaded || !Glyphs.TryGetValue(duration, out var glyph))
                return false;

            int sizeKey = Math.Max(8, (int)Math.Round(fontSize * 4));
            int colorKey = ColorCacheKey(color);
            string cacheKey = $"{(int)duration}:{sizeKey}:{colorKey}";

            var png = PngCache.GetOrAdd(cacheKey, _ => RasterizeToPng(glyph, fontSize, color));
            if (png == null || png.Length == 0)
                return false;

            try
            {
                using var ms = new MemoryStream(png);
                var image = Microsoft.Maui.Graphics.Platform.PlatformImage.FromStream(ms);
                canvas.DrawImage(image, left, top, width, height);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SmuFLRestRaster] DrawImage failed: {ex.Message}");
                return false;
            }
        }

        private static byte[]? RasterizeToPng(string glyph, float fontSize, Color color)
        {
            try
            {
                using var paint = new SKPaint
                {
                    Typeface     = SmuFLFont.SkiaTypeface,
                    TextSize     = fontSize,
                    Color        = ToSkColor(color),
                    IsAntialias  = true,
                    SubpixelText = true
                };

                var bounds = new SKRect();
                paint.MeasureText(glyph, ref bounds);

                int w = Math.Max(2, (int)Math.Ceiling(bounds.Width) + 4);
                int h = Math.Max(2, (int)Math.Ceiling(bounds.Height) + 4);

                using var bitmap = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
                using var skCanvas = new SKCanvas(bitmap);
                skCanvas.Clear(SKColors.Transparent);
                skCanvas.DrawText(glyph, 2f - bounds.Left, 2f - bounds.Top, paint);

                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                return data?.ToArray();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SmuFLRestRaster] raster failed: {ex.Message}");
                return null;
            }
        }

        private static int ColorCacheKey(Color color) =>
            HashCode.Combine(
                (byte)(color.Red * 255),
                (byte)(color.Green * 255),
                (byte)(color.Blue * 255),
                (byte)(color.Alpha * 255));

        private static SKColor ToSkColor(Color color) => new(
            (byte)(color.Red * 255),
            (byte)(color.Green * 255),
            (byte)(color.Blue * 255),
            (byte)(color.Alpha * 255));
    }
}
