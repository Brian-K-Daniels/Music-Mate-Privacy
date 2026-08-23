using Microsoft.Maui.Graphics;
using musicmate.Models;

namespace musicmate.Drawables
{
    /// <summary>
    /// Draws standard SMuFL rest glyphs from the bundled Bravura font.
    /// </summary>
    internal static class SmuFLRestDrawer
    {
        private static string Glyph(NoteDuration duration) => duration switch
        {
            NoteDuration.Whole => "\uE4E3",
            NoteDuration.Half => "\uE4E4",
            NoteDuration.Quarter => "\uE4E5",
            NoteDuration.Eighth => "\uE4E6",
            NoteDuration.Sixteenth => "\uE4E7",
            _ => "\uE4E7"
        };

        internal static void Draw(
            ICanvas canvas,
            NoteDuration duration,
            float centerX,
            float staffTop,
            float staffMid,
            float sls,
            Color glyphColor,
            float restScale = 0.72f)
        {
            GetLayout(duration, centerX, staffTop, staffMid, sls, restScale,
                out float left, out float top, out float width, out float height, out float fontSize);

            canvas.SaveState();
            try
            {
                string glyph = Glyph(duration);

                // 1) Platform text (worked on Android before raster bypass).
                if (PlatformRestText.DrawCentered(canvas, glyph, fontSize, glyphColor,
                        left, top, width, height))
                    return;

                // 2) Scaled DrawImage fallback (never blit directly to Android canvas).
                if (SmuFLRestRaster.TryDraw(canvas, duration, left, top, width, height, fontSize, glyphColor))
                    return;

                DrawVectorFallback(canvas, duration, centerX, staffTop, staffMid, sls, glyphColor);
            }
            finally
            {
                canvas.RestoreState();
            }
        }

        private static void DrawVectorFallback(
            ICanvas canvas,
            NoteDuration duration,
            float centerX,
            float staffTop,
            float staffMid,
            float sls,
            Color color)
        {
            canvas.StrokeColor = color;
            canvas.FillColor = color;
            canvas.StrokeSize = 1.5f;
            float r = sls * 0.5f;

            switch (duration)
            {
                case NoteDuration.Whole:
                    canvas.FillRectangle(centerX - r * 1.3f, staffTop + sls - r * 0.5f, r * 2.6f, r * 0.5f);
                    break;
                case NoteDuration.Half:
                    canvas.FillRectangle(centerX - r * 1.3f, staffMid, r * 2.6f, r * 0.5f);
                    break;
                case NoteDuration.Quarter:
                    canvas.DrawLine(centerX, staffMid - sls * 0.7f, centerX + r * 0.5f, staffMid - sls * 0.4f);
                    canvas.DrawLine(centerX + r * 0.5f, staffMid - sls * 0.4f, centerX - r * 0.5f, staffMid);
                    canvas.DrawLine(centerX - r * 0.5f, staffMid, centerX + r * 0.5f, staffMid + sls * 0.4f);
                    canvas.DrawLine(centerX + r * 0.5f, staffMid + sls * 0.4f, centerX, staffMid + sls * 0.7f);
                    break;
                case NoteDuration.Eighth:
                    canvas.FillEllipse(centerX - 2f, staffMid - 2f, 4f, 4f);
                    canvas.DrawLine(centerX, staffMid, centerX + sls * 0.4f, staffMid - sls * 0.7f);
                    break;
                default:
                    canvas.DrawLine(centerX, staffMid + sls * 0.3f, centerX + r * 0.5f, staffMid - sls * 0.3f);
                    canvas.DrawLine(centerX + r * 0.5f, staffMid - sls * 0.3f, centerX - r * 0.25f, staffMid - sls * 0.7f);
                    break;
            }
        }

        /// <summary>
        /// Horizontal ink width of a rest glyph at the given staff space and scale.
        /// Must stay in sync with <see cref="GetLayout"/> so packing reserves what drawing paints.
        /// </summary>
        internal static float GetInkWidth(float sls, float restScale)
            => sls * 4.4f * restScale;

        private static void GetLayout(
            NoteDuration duration,
            float centerX,
            float staffTop,
            float staffMid,
            float sls,
            float restScale,
            out float left,
            out float top,
            out float width,
            out float height,
            out float fontSize)
        {
            width = GetInkWidth(sls, restScale: 1f);
            left = centerX - width * 0.5f;

            switch (duration)
            {
                case NoteDuration.Whole:
                    height = sls * 2.4f;
                    top = staffTop + 2.6f * sls;
                    fontSize = sls * 3.4f;
                    break;

                case NoteDuration.Half:
                    height = sls * 2.4f;
                    top = staffMid - height * 0.55f;
                    fontSize = sls * 3.4f;
                    break;

                case NoteDuration.Quarter:
                    height = sls * 5f;
                    top = staffMid - height * 0.5f;
                    fontSize = sls * 4.4f;
                    break;

                case NoteDuration.Eighth:
                    height = sls * 4.6f;
                    top = staffMid - height * 0.52f;
                    fontSize = sls * 4.1f;
                    break;

                default:
                    height = sls * 4.6f;
                    top = staffMid - height * 0.5f;
                    fontSize = sls * 4.1f;
                    break;
            }

            float centerY = top + height * 0.5f;
            width *= restScale;
            height *= restScale;
            fontSize *= restScale;
            left = centerX - width * 0.5f;
            top = centerY - height * 0.5f;
        }
    }
}
