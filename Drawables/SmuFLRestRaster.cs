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
            if (!Glyphs.TryGetValue(duration, out var glyph))
                return false;
            return TryDrawGlyph(canvas, glyph, left, top, width, height, fontSize, color);
        }

        internal static bool TryDrawGlyph(
            ICanvas canvas,
            string glyph,
            float left,
            float top,
            float width,
            float height,
            float fontSize,
            Color color)
        {
            if (!SmuFLFont.IsLoaded || string.IsNullOrEmpty(glyph))
                return false;

            int sizeKey = Math.Max(8, (int)Math.Round(fontSize * 4));
            int colorKey = ColorCacheKey(color);
            string cacheKey = $"{glyph}:{sizeKey}:{colorKey}";

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

        /// <summary>Draws a SMuFL glyph with its font origin (stem tip) at <paramref name="stemX"/>, <paramref name="stemTipY"/>.</summary>
        internal static bool TryDrawGlyphAtOrigin(
            ICanvas canvas,
            string glyph,
            float stemX,
            float stemTipY,
            float fontSize,
            Color color)
        {
            if (!SmuFLFont.IsLoaded || string.IsNullOrEmpty(glyph))
                return false;

            int sizeKey = Math.Max(8, (int)Math.Round(fontSize * 4));
            int colorKey = ColorCacheKey(color);
            string cacheKey = $"origin:{glyph}:{sizeKey}:{colorKey}";

            if (!OriginPngCache.TryGetValue(cacheKey, out var entry))
            {
                entry = RasterizeAtOrigin(glyph, fontSize, color);
                if (entry == null)
                    return false;
                OriginPngCache.TryAdd(cacheKey, entry);
            }

            try
            {
                using var ms = new MemoryStream(entry.Png);
                var image = Microsoft.Maui.Graphics.Platform.PlatformImage.FromStream(ms);
                canvas.DrawImage(image,
                    stemX - entry.OriginX,
                    stemTipY - entry.OriginY,
                    entry.Width,
                    entry.Height);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SmuFLRestRaster] origin DrawImage failed: {ex.Message}");
                return false;
            }
        }

        private static readonly ConcurrentDictionary<string, OriginRaster?> OriginPngCache = new();

        private sealed record OriginRaster(byte[] Png, float OriginX, float OriginY, float Width, float Height);

        private static OriginRaster? RasterizeAtOrigin(string glyph, float fontSize, Color color)
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

                const float pad = 2f;
                float originX = pad - bounds.Left;
                float originY = pad - bounds.Top;
                int w = Math.Max(2, (int)Math.Ceiling(bounds.Width) + (int)(pad * 2));
                int h = Math.Max(2, (int)Math.Ceiling(bounds.Height) + (int)(pad * 2));

                using var bitmap = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
                using var skCanvas = new SKCanvas(bitmap);
                skCanvas.Clear(SKColors.Transparent);
                skCanvas.DrawText(glyph, originX, originY, paint);

                using var image = SKImage.FromBitmap(bitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                var png = data?.ToArray();
                if (png == null || png.Length == 0)
                    return null;

                return new OriginRaster(png, originX, originY, w, h);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SmuFLRestRaster] origin raster failed: {ex.Message}");
                return null;
            }
        }

        private static byte[]? RasterizeToPng(string glyph, float fontSize, Color color)
        {
            var entry = RasterizeAtOrigin(glyph, fontSize, color);
            return entry?.Png;
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
