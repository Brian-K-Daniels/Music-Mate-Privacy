using System.Reflection;
using Microsoft.Maui.Graphics;

namespace musicmate.Drawables
{
    /// <summary>
    /// Draws SMuFL rests via PlatformCanvas TextPaint (Android GraphicsView path).
    /// </summary>
    internal static class PlatformRestText
    {
        private const BindingFlags MemberFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static bool DrawCentered(
            ICanvas canvas,
            string glyph,
            float fontSize,
            Color color,
            float left,
            float top,
            float width,
            float height)
            => DrawAligned(canvas, glyph, fontSize, color, left, top, width, height,
                HorizontalAlignment.Center, VerticalAlignment.Center);

        internal static bool DrawAligned(
            ICanvas canvas,
            string glyph,
            float fontSize,
            Color color,
            float left,
            float top,
            float width,
            float height,
            HorizontalAlignment horizontalAlignment,
            VerticalAlignment verticalAlignment)
        {
            var inner = FindInnerCanvas(canvas);
            if (inner == null || !TryConfigureFont(state: GetCurrentState(inner), fontSize, color))
                return false;

            canvas.DrawString(glyph, left, top, width, height, horizontalAlignment, verticalAlignment);

            // Bravura must not leak into accidentals, time sig, or labels on the same canvas.
            ResetPlatformFont(inner);
            return true;
        }

        private static bool TryConfigureFont(object? state, float fontSize, Color color)
        {
            if (state == null)
                return false;

            if (state.GetType().Name != "PlatformCanvasState")
                return false;

#if ANDROID
            if (!SmuFLFont.IsLoaded)
                return false;

            var textPaint = new Android.Text.TextPaint(Android.Graphics.PaintFlags.AntiAlias);
            textPaint.SetTypeface(SmuFLFont.AndroidTypeface);
            textPaint.TextSize = fontSize;

            var stateType = state.GetType();
            stateType.GetProperty("FontPaint", MemberFlags)?.SetValue(state, textPaint);
            stateType.GetProperty("FontSize", MemberFlags)?.SetValue(state, fontSize);
            stateType.GetProperty("FontColor", MemberFlags)?.SetValue(state, color);
            stateType.GetField("_typefaceInvalid", MemberFlags)?.SetValue(state, false);
            return true;
#else
            return false;
#endif
        }

        private static object? FindInnerCanvas(ICanvas canvas)
        {
            var current = canvas;
            for (int depth = 0; depth < 8; depth++)
            {
                if (current.GetType().Name == "PlatformCanvas")
                    return current;

                if (current is ScalingCanvas scaling)
                {
                    current = scaling.Wrapped as ICanvas ?? scaling.ParentCanvas;
                    continue;
                }
                break;
            }
            return null;
        }

        private static object? GetCurrentState(object canvas)
        {
            for (var type = canvas.GetType(); type != null; type = type.BaseType)
            {
                var prop = type.GetProperty("CurrentState", MemberFlags);
                if (prop != null)
                    return prop.GetValue(canvas);
            }
            return null;
        }

        private static void ResetPlatformFont(object platformCanvas)
        {
            var state = GetCurrentState(platformCanvas);
            if (state == null)
                return;

            var stateType = state.GetType();
            stateType.GetField("_font", MemberFlags)?.SetValue(state, null);
            stateType.GetField("_typefaceInvalid", MemberFlags)?.SetValue(state, true);
        }
    }
}
