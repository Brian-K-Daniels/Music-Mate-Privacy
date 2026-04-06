using Microsoft.Maui.Graphics;
using musicmate.Services;

namespace musicmate.Drawables
{
    public class StaffDrawable : IDrawable
    {
        private float correctionFactor = 1.3f;  //  2026.04.04 1131  1.5f too big;
        private float correctionConstantY = 0f;
        private const float flatSizeBoost = 1.5f;  //  2026.04.05 0916  1.25f;
        float glyphH = 10f;
        float glyphW = 10f;  //  2026.04.04 1123 
        private readonly NoteSessionService _session;
        private readonly ThemeService _theme_service;
        // Cached computed height for the staff band (measured outside of Draw)
        private float _computedStaffHeight = 220f;

        public StaffDrawable(NoteSessionService session, ThemeService themeService)
        {
            _session = session;
            _theme_service = themeService;
        }

        // Draw a simple vector approximation of a treble clef using only primitive drawing
        // operations so we avoid platform text rendering and associated Java calls on Android.
        private static void DrawTrebleClef(ICanvas canvas, float x, float y, float w, float h, Color color)
        {
            canvas.SaveState();
            canvas.StrokeColor = color;
            canvas.FillColor = color;
            canvas.StrokeSize = Math.Max(1f, w * 0.06f);

            // Center for spiral/ellipses
            var cx = x + w * 0.45f;
            var cy = y + h * 0.25f;

            // Draw a sequence of decreasing ellipses to approximate the curled body
            for (int i = 0; i < 5; i++)
            {
                var iw = w * (0.6f - i * 0.09f);
                var ih = h * (0.6f - i * 0.11f);
                var ix = cx - iw / 2f + i * (w * 0.03f);
                var iy = cy + i * (h * 0.08f);
                canvas.DrawEllipse(ix, iy, iw, ih);
            }

            // Draw a sweeping curve down the staff by connecting short segments (no text APIs)
            int steps = 14;
            float px = x + w * 0.85f;
            float py = y + h * 0.05f;
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                // parametric curve tuned to look roughly like a clef
                float nx = x + w * (0.85f - 0.95f * t + 0.25f * (float)Math.Sin(t * Math.PI * 2.0));
                float ny = y + h * (0.05f + 0.92f * t + 0.03f * (float)Math.Cos(t * Math.PI));
                canvas.DrawLine(px, py, nx, ny);
                px = nx; py = ny;
            }

            // Small filled circle near the bottom of the clef
            canvas.FillEllipse(x + w * 0.55f, y + h * 0.84f, w * 0.12f, h * 0.12f);

            canvas.RestoreState();
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            // Measure required height first, based on notes, clef and feedback, then draw into that rectangle.
            // Basic primitives
            var noteHeadH = 8f;
            var headW = noteHeadH * 1.5f;
            var staffSpacing = noteHeadH + 2f;

            // Compute required height (cached) using numeric-only method
            var requiredHeight = ComputeRequiredHeight(dirtyRect.Width);

            canvas.SaveState();

            // Colors
            var backgroundColor = _theme_service.PanelBackgroundColor;
            var contrastColor = _theme_service.ContrastingTextColor;

            // Position the staff band vertically so it fits within dirtyRect (centered by default)
            var staffBandHeight = requiredHeight;
            var staffBandTop = Math.Max(0f, (dirtyRect.Height - staffBandHeight) / 2f);

            // Note: the tuner vertical offset is applied to the computed required height
            // (via ComputeRequiredHeight) so the GraphicsView is given extra space and
            // the staff/notes can be positioned lower. Do not apply an additional runtime
            // shift here.

            // Fill background for the full dirty area to avoid gaps
            canvas.FillColor = backgroundColor;
            canvas.FillRectangle(dirtyRect.X, 0f, dirtyRect.Width, dirtyRect.Height);

            canvas.StrokeColor = contrastColor;
            canvas.FontColor = contrastColor;

            // Determine extremes (diatonic steps) to place the core 5-line staff inside the band
            int maxStepsAbove = 0, maxStepsBelow = 0;
            foreach (var note in _session.NotesToDraw)
            {
                var s = note.Name.Trim();
                if (string.IsNullOrEmpty(s))
                    continue;
                var letter = char.ToUpperInvariant(s[0]);
                if (!int.TryParse(s[^1].ToString(), out var octave))
                    continue;
                int stepsFromB4 = StaffStepsFromB4(letter, octave);
                if (stepsFromB4 < 0)
                    maxStepsAbove = Math.Max(maxStepsAbove, -stepsFromB4);
                else
                    maxStepsBelow = Math.Max(maxStepsBelow, stepsFromB4);
            }

            // Also include tuner last note in extents when in tuner mode so very high tuner notes
            // are accounted for in the vertical layout.
            if (_session.Tune == "Tuner" && !string.IsNullOrEmpty(_session.TunerLastNoteName))
            {
                try
                {
                    var s = _session.TunerLastNoteName.Trim();
                    var letter = char.ToUpperInvariant(s[0]);
                    if (int.TryParse(s[^1].ToString(), out var octave))
                    {
                        int stepsFromB4 = StaffStepsFromB4(letter, octave);
                        if (stepsFromB4 < 0)
                            maxStepsAbove = Math.Max(maxStepsAbove, -stepsFromB4);
                        else
                            maxStepsBelow = Math.Max(maxStepsBelow, stepsFromB4);
                    }
                }
                catch
                {
                    // ignore malformed tuner name
                }
            }

            // Include clef center (G4) in extents
            var clefSteps = StaffStepsFromB4('G', 4);
            if (clefSteps < 0) maxStepsAbove = Math.Max(maxStepsAbove, -clefSteps); else maxStepsBelow = Math.Max(maxStepsBelow, clefSteps);

            var stepSize = staffSpacing / 2f;

            // Compute extra space above and below the 5-line staff to accommodate ledger lines and glyph extents
            var extraTop = maxStepsAbove * stepSize + noteHeadH * 1.5f;
            var extraBottom = maxStepsBelow * stepSize + noteHeadH * 2.5f;

            // Position core staff (5 lines) inside the band
            var staffCoreTop = staffBandTop + extraTop;
            var staffCoreBottom = staffCoreTop + 4f * staffSpacing;

            // Draw staff lines
            var staffLineLeftMargin = 8f;
            var staffLineRightMargin = 8f;
            for (int i = 0; i < 5; i++)
            {
                var y = staffCoreTop + i * staffSpacing;
                canvas.DrawLine(staffLineLeftMargin, y, dirtyRect.Width - staffLineRightMargin, y);
            }

            // Draw clef (prefer text glyph, but protect against exceptions). If text rendering fails,
            // fallback to the embedded vector path drawing which avoids Java/text APIs.
            var middleLineY = staffCoreTop + 2f * staffSpacing;
            var clefCenterY = GetYForSpelledNote("G4", middleLineY, staffSpacing);
            var clefX = 16f;
            var clefW = headW * 1.6f * 3f;
            var clefH = headW * 3.2f * 3f;
            var clefY = clefCenterY - (clefH / 2f) - 1.5f * staffSpacing;
            try
            {
                // Try to render the original beautiful glyph
                // Protect against the clef being drawn off the top of the canvas by ensuring a
                // small top margin. This prevents the top of the treble clef glyph from being clipped.
                if (clefY < 6f) clefY = 6f;
                canvas.FontSize = 18.666f * 3f;
                canvas.DrawString("𝄞", clefX, clefY, clefW, clefH, HorizontalAlignment.Left, VerticalAlignment.Center);
            }
            catch
            {
                // If DrawString throws (platform font issue), draw the vector fallback instead.
                if (clefY < 6f) clefY = 6f;
                DrawTrebleClef(canvas, clefX, clefY, clefW, clefH, contrastColor);
            }

            // Key signature and note horizontal layout
            var accidentalCount = GetAccidentalCountForScale(_session.Key, _session.SelectedScale);
            var accScale = 1.5f;
            var accWidth = headW * accScale;
            var accSpacing = headW * 0.33f * accScale;
            var accStartX = clefX + clefW + 6f;

            // Ensure first note is at least ~3 note-head widths to the right of the key signature.
            // This gives room for any staff accidental and avoids overlap.
            var desiredHeadPadding = 3f * headW;                 // 3 head widths
            var fallbackPadding = 32f;                           // keep existing minimum
            var extraLeftPadding = Math.Max(fallbackPadding, desiredHeadPadding);

            var leftMargin = accStartX + Math.Abs(accidentalCount) * accSpacing + extraLeftPadding;
            var rightMargin = headW + 16f;
            try
            {
                DrawKeySignature(canvas, staffCoreTop, staffSpacing, _session.Key, accStartX, accWidth, accSpacing, headW, accidentalCount, backgroundColor, contrastColor);
            }
            catch
            {
                // Swallow platform font/rendering exceptions so notes are still drawn
            }

            // Horizontal scaling of source positions into available width
            float minSrcX = float.MaxValue, maxSrcX = float.MinValue;
            foreach (var n in _session.NotesToDraw)
            {
                if (n.X < minSrcX) minSrcX = n.X;
                if (n.X > maxSrcX) maxSrcX = n.X;
            }
            if (minSrcX == float.MaxValue) { minSrcX = 0f; maxSrcX = 1f; }

            var targetRange = Math.Max(1f, dirtyRect.Width - leftMargin - rightMargin);
            var srcRange = Math.Max(1f, maxSrcX - minSrcX);
            var scale = targetRange / srcRange;

            // If in Tuner mode, draw the last detected tuner note centered in the view.
            if (_session.Tune == "Tuner")
            {
                var tunerName = _session.TunerLastNoteName;
                if (!string.IsNullOrEmpty(tunerName))
                {
                    var centerX = dirtyRect.Width / 2f;
                    // Make the tuner note head half the previous size for a cleaner tuner display
                    var tunerHeadH = noteHeadH * 1.2f; // previously 2.4x, now half that
                    var tunerHeadW = tunerHeadH * 1.5f;
                    DrawCenteredNote(canvas, tunerName, centerX, staffCoreTop, staffSpacing, tunerHeadH, tunerHeadW, contrastColor, contrastColor);

                    // (Intentionally omitted) Do not draw cents on the graphics panel in tuner mode.
                }
            }
            else
            {
                // Draw notes for normal session modes
                foreach (var n in _session.NotesToDraw)
                {
                    var scaledX = leftMargin + (n.X - minSrcX) * scale;
                    DrawNoteWithLedger(canvas, n, staffCoreTop, staffCoreBottom, staffSpacing, noteHeadH, headW, scaledX, accidentalCount, backgroundColor, contrastColor, true);
                }
            }

            // Draw feedback boxes below the lowest notes
            DrawSmallFeedback(canvas, staffCoreTop, staffCoreBottom, staffSpacing, noteHeadH, headW, leftMargin, minSrcX, scale, dirtyRect.Height);

            canvas.RestoreState();
        }

        /// <summary>
        /// Compute and cache an estimated height required to render the staff, ledger lines,
        /// clef and feedback for the current session. This uses only numeric calculations
        /// (no platform text measurement) so it is safe to call from non-UI threads and
        /// does not invoke platform font subsystems.
        /// </summary>
        public float ComputeRequiredHeight(float width)
        {
            // Basic glyph geometry (kept in sync with Draw)
            var noteHeadH = 8f;
            var headW = noteHeadH * 1.5f;
            var staffSpacing = noteHeadH + 2f;

            // Core staff area (4 spaces = 4 * spacing between lines)
            var staffCoreHeight = 4f * staffSpacing;

            // Determine extreme note positions in diatonic steps relative to B4 (middle line reference)
            int maxStepsAbove = 0; // number of steps above middle line
            int maxStepsBelow = 0; // number of steps below middle line
            foreach (var note in _session.NotesToDraw)
            {
                var s = note.Name.Trim();
                if (string.IsNullOrEmpty(s))
                    continue;
                var letter = char.ToUpperInvariant(s[0]);
                if (!int.TryParse(s[^1].ToString(), out var octave))
                    continue;
                int stepsFromB4 = StaffStepsFromB4(letter, octave);
                if (stepsFromB4 < 0)
                    maxStepsAbove = Math.Max(maxStepsAbove, -stepsFromB4);
                else
                    maxStepsBelow = Math.Max(maxStepsBelow, stepsFromB4);
            }

            // Also include tuner last note in extents when in tuner mode so very high tuner notes
            // are accounted for when computing required height.
            if (_session.Tune == "Tuner" && !string.IsNullOrEmpty(_session.TunerLastNoteName))
            {
                try
                {
                    var s = _session.TunerLastNoteName.Trim();
                    var letter = char.ToUpperInvariant(s[0]);
                    if (int.TryParse(s[^1].ToString(), out var octave))
                    {
                        int stepsFromB4 = StaffStepsFromB4(letter, octave);
                        if (stepsFromB4 < 0)
                            maxStepsAbove = Math.Max(maxStepsAbove, -stepsFromB4);
                        else
                            maxStepsBelow = Math.Max(maxStepsBelow, stepsFromB4);
                    }
                }
                catch
                {
                    // ignore malformed tuner name
                }
            }

            // Ensure clef glyph is also accounted for (G4 center)
            var clefLetter = 'G';
            var clefOctave = 4;
            var clefSteps = StaffStepsFromB4(clefLetter, clefOctave);
            if (clefSteps < 0)
                maxStepsAbove = Math.Max(maxStepsAbove, -clefSteps);
            else
                maxStepsBelow = Math.Max(maxStepsBelow, clefSteps);

            var stepSize = staffSpacing / 2f; // same as GetYForSpelledNote

            // Extra vertical space to allow ledger lines and note heads beyond the 5-line staff
            var extraTop = maxStepsAbove * stepSize + noteHeadH * 1.5f;
            var extraBottom = maxStepsBelow * stepSize + noteHeadH * 2.5f; // room for feedback below

            // If in tuner mode, bias the extra space towards the top so high notes have room
            // above the core staff. This shifts the staff lower in the graphics area.
            if (_session.Tune == "Tuner")
            {
                try
                {
                    var mmToDp = 160.0 / 25.4;
                    var shift = (float)(30.0 * mmToDp);
                    extraTop += shift; // add extra space above the staff
                }
                catch
                {
                    // ignore
                }
            }

            // Feedback boxes underneath lowest notes
            var feedbackBoxHeight = noteHeadH * 2.4f + 4f;

            // Padding to keep content from touching edges
            var padding = 8f;

            var computed = staffCoreHeight + extraTop + extraBottom + feedbackBoxHeight + padding;

            // Keep a sensible minimum (matches earlier hard-coded value)
            _computedStaffHeight = Math.Max(220f, computed);
            return _computedStaffHeight;
        }

        private void DrawKeySignature(
            ICanvas canvas,
            float top,
            float spacing,
            string key,
            float startX,
            float symbolWidth,
            float symbolSpacing,
            float headW,
            int countOverride,
            Color fillColor,
            Color strokeColor)
        {
            var count = countOverride;
            var abs = Math.Abs(count);
            if (abs == 0) return;

            var middleLineY = top + spacing * 2f;

            var sharpNotes = new[] { "F#5", "C#5", "G#5", "D#5", "A#4", "E#5", "B#4" };
            var flatNotes  = new[] { "Bb4", "Eb5", "Ab4", "Db5", "Gb4", "Cb5", "Fb4" };
            var notes = count > 0 ? sharpNotes : flatNotes;
            bool isFlat = count < 0;

            // Size: adjust flats upward in glyph size to visually match sharps
            var glyphSize = symbolWidth * correctionFactor * (isFlat ? flatSizeBoost : 1.0f);

            canvas.FontColor   = strokeColor;
            canvas.FillColor   = fillColor;
            canvas.StrokeColor = strokeColor;
            canvas.FontSize    = glyphSize;

            // baseline compensation  for flats 
            var flatDY = -spacing * 0.25f;

            for (int i = 0; i < abs && i < notes.Length; i++)
            {
                var note = notes[i];

                // Center on the correct staff line/space, then apply baseline compensation.
                float yCenter = GetYForSpelledNote(note, middleLineY, spacing) - 0.5f * spacing;

                // Move flats down by 0.5 spaces (they were too high previously).
                if (isFlat)
                    yCenter += flatDY; 

                float x = startX + i * (symbolSpacing + 2f);

                canvas.DrawString(
                    isFlat ? "♭" : "♯",
                    x,
                    yCenter - glyphSize / 2f,
                    glyphSize,
                    glyphSize,
                    HorizontalAlignment.Center,
                    VerticalAlignment.Center);
            }
        }

        // Map letter to diatonic pitch index within an octave (C=0 .. B=6)
        private static int LetterIndex(char letter)
        {
            switch (char.ToUpperInvariant(letter))
            {
                case 'C': return 0;
                case 'D': return 1;
                case 'E': return 2;
                case 'F': return 3;
                case 'G': return 4;
                case 'A': return 5;
                case 'B': return 6;
                default: return 0;
            }
        }

        /// <summary>
        /// Compute visual staff steps from B4 (treble clef middle line).
        /// Positive = below B4 (lower pitch, higher Y), negative = above B4 (higher pitch, lower Y).
        /// </summary>
        private static int StaffStepsFromB4(char letter, int octave)
        {
            // B4 diatonic index = 4*7 + 6 = 34
            return 34 - (octave * 7 + LetterIndex(letter));
        }

        // Convert a spelled note like "C4" or "Bb4" to a Y coordinate.
        // middleLineY is the Y coordinate of the reference middle line (B4),
        // spacing is distance between staff lines.
        private static float GetYForSpelledNote(string noteName, float middleLineY, float spacing, bool adjustForAccidental = true, bool isLine = false)
        {
            if (string.IsNullOrWhiteSpace(noteName))
                return middleLineY;

            var s = noteName.Trim();
            var letter = char.ToUpperInvariant(s[0]);

            // parse octave (support multi-digit octaves)
            int octave = 4;
            for (int i = s.Length - 1; i >= 0; i--)
            {
                if (char.IsDigit(s[i]))
                {
                    int j = i;
                    while (j >= 0 && char.IsDigit(s[j])) j--;
                    var octStr = s.Substring(j + 1, i - j);
                    if (int.TryParse(octStr, out var oct)) octave = oct;
                    break;
                }
            }

            // Steps from B4 (diatonic steps; 1 step = half staff-space because lines+spaces)
            int stepsFromB4 = StaffStepsFromB4(letter, octave);
            var stepSize = spacing / 2f;

            // Y increases downward; positive steps (below B4) move Y down.
            var y = middleLineY + stepsFromB4 * stepSize;

            return y;
        }

        // Draw a regular note (not tuner-centered) with ledger lines, accidental and stem.
        private void DrawNoteWithLedger(ICanvas canvas, NoteInfo note, float staffTop, float staffBottom, float spacing,
                         float headH, float headW, float centerX, int accidentalCount, Color fillColor, Color strokeColor, bool drawAccidental)
        {
            canvas.SaveState();
            canvas.FillColor = fillColor;
            canvas.StrokeColor = strokeColor;

            var middleLineY = staffTop + spacing * 2;
            var noteY = GetYForSpelledNote(note.Name, middleLineY, spacing);
            var xLeft = centerX - headW / 2f;

            // Draw base head (background layer) so ledger/stem can be drawn on top
            canvas.FillEllipse(xLeft, noteY - headH / 2f, headW, headH);

            // Stem
            var baseThickness = Math.Max(1f, headW * 0.06f);
            canvas.StrokeSize = baseThickness;
            var stemLength = spacing * 3f;
            if (noteY < middleLineY)
            {
                // stem down
                canvas.DrawLine(xLeft, noteY, xLeft, noteY + stemLength);
            }
            else
            {
                // stem up
                canvas.DrawLine(xLeft + headW, noteY, xLeft + headW, noteY - stemLength);
            }

            // Ledger lines where necessary
            DrawLedgerLines(canvas, centerX, noteY, staffTop, staffTop + 4 * spacing, spacing, headW, fillColor, strokeColor);

            // Outline/fill the head with strokeColor for visibility (final pass)
            canvas.FillColor = strokeColor;
            canvas.FillEllipse(xLeft, noteY - headH / 2f, headW, headH);

            // --- Accidental display with proper music-theory rules ---
            if (drawAccidental)
            {
                var raw = note.Name.Trim();
                char letter = char.ToUpperInvariant(raw[0]);

                // What the key signature already implies for this letter (null = nothing)
                var sigAcc = GetSignatureAccidentalForLetter(letter, accidentalCount);

                bool wantsSharp   = raw.Contains('#');
                bool wantsFlat    = raw.Contains('b');
                bool wantsNatural = !wantsSharp && !wantsFlat;

                // Determine the glyph to draw and whether it is a flat (for size/offset math).
                // null means no accidental should be shown.
                string? accidentalGlyph = null;
                bool isAccFlat = false;

                if (_session.Tune == "Random")
                {
                    // Music-theory rules for Random mode:
                    //  • note wants ♯ and key sig does NOT already sharpen that letter → show ♯
                    //  • note wants ♭ and key sig does NOT already flatten that letter → show ♭
                    //  • note is natural but key sig alters that letter                → show ♮ (cancel)
                    //  • otherwise (accidental matches key sig, or natural with no key sig effect) → nothing
                    if (wantsSharp && sigAcc != "#")
                    {
                        accidentalGlyph = "♯";
                        isAccFlat = false;
                    }
                    else if (wantsFlat && sigAcc != "b")
                    {
                        accidentalGlyph = "♭";
                        isAccFlat = true;
                    }
                    else if (wantsNatural && sigAcc != null)
                    {
                        // The key signature alters this letter, but the note is natural → show ♮
                        accidentalGlyph = "♮";
                        isAccFlat = false;
                    }
                    // else: redundant (key sig covers it) or no alteration needed — draw nothing
                }
                else
                {
                    // Non-Random modes: show accidental only when NOT already covered by key sig
                    if (wantsSharp && sigAcc != "#")
                    {
                        accidentalGlyph = "♯";
                        isAccFlat = false;
                    }
                    else if (wantsFlat && sigAcc != "b")
                    {
                        accidentalGlyph = "♭";
                        isAccFlat = true;
                    }
                }

                if (accidentalGlyph != null)
                {
                    // Match key-signature sizing: symbolWidth = headW * 1.5, same correctionFactor.
                    var accSize = headW * 1.5f * correctionFactor * (isAccFlat ? flatSizeBoost : 1.0f);
                    canvas.FontSize = accSize;
                    canvas.FontColor = strokeColor;

                    // Vertical alignment: baseline compensation + extra offset for flats
                    var verticalAdjust = spacing / 2f;
                    var flatDY = -spacing * 0.25f;

                    var drawY = noteY - verticalAdjust - accSize / 2f;
                    if (isAccFlat)
                        drawY += flatDY;

                    var accX = xLeft - headW * 1.1f;
                    canvas.DrawString(accidentalGlyph,
                        accX,
                        drawY,
                        accSize,
                        accSize,
                        HorizontalAlignment.Center,
                        VerticalAlignment.Center);
                }
            }

            canvas.RestoreState();
        }

        // Draw a centered tuner-style note (used in Tuner mode)
        private static void DrawCenteredNote(ICanvas canvas, string noteName, float centerX, float staffTop, float spacing, float headH, float headW,
                        Color fillColor, Color strokeColor)
        {
            canvas.SaveState();
            canvas.FillColor = fillColor;
            canvas.StrokeColor = strokeColor;

            var middleLineY = staffTop + spacing * 2;
            var noteY = GetYForSpelledNote(noteName, middleLineY, spacing);
            var xLeft = centerX - headW / 2f;

            // Draw the head at the requested size
            canvas.FillEllipse(xLeft, noteY - headH / 2f, headW, headH);

            // Thicker stem for tuner display
            var baseThickness = Math.Max(1f, headW * 0.06f);
            canvas.StrokeSize = baseThickness * 3f;

            var stemLength = spacing * 3f;
            if (noteY < middleLineY)
            {
                canvas.DrawLine(xLeft, noteY, xLeft, noteY + stemLength);
            }
            else
            {
                canvas.DrawLine(xLeft + headW, noteY, xLeft + headW, noteY - stemLength);
            }

            DrawLedgerLines(canvas, centerX, noteY, staffTop, staffTop + 4 * spacing, spacing, headW, fillColor, strokeColor);

            // Outline/fill for visibility
            canvas.FillColor = strokeColor;
            canvas.FillEllipse(xLeft, noteY - headH / 2f, headW, headH);

            canvas.RestoreState();
        }

        private static void DrawLedgerLines(ICanvas canvas, float centerX, float noteY, float staffTop, float staffBottom, float spacing, float headW,
                        Color fillColor, Color strokeColor)
        {
            var topLineY = staffTop;
            var bottomLineY = staffBottom;
            var step = spacing / 2f;

            if (noteY < topLineY)
            {
                var delta = topLineY - noteY;
                var steps = (int)Math.Round(delta / step);
                for (int s = 2; s <= steps; s += 2)
                {
                    var y = topLineY - s * step;
                    canvas.DrawLine(centerX - headW * 0.7f, y, centerX + headW * 0.7f, y);
                }
            }
            else if (noteY > bottomLineY)
            {
                var delta = noteY - bottomLineY;
                var steps = (int)Math.Round(delta / step);
                for (int s = 2; s <= steps; s += 2)
                {
                    var y = bottomLineY + s * step;
                    canvas.DrawLine(centerX - headW * 0.7f, y, centerX + headW * 0.7f, y);
                }
            }
        }

        private void DrawSmallFeedback(ICanvas canvas, float top, float bottom, float spacing, float headH, float headW, float leftMargin, float minSrcX, float scale, float canvasHeight)
        {
            // Find lowest note position
            float lowestNoteY = bottom; // Default to bottom staff line (E4 in treble clef)
            var middleLineY = top + spacing * 2;

            foreach (var note in _session.NotesToDraw)
            {
                var noteY = GetYForSpelledNote(note.Name, middleLineY, spacing);
                if (noteY > lowestNoteY)
                {
                    lowestNoteY = noteY;
                }
            }

            // Position feedback 2 note head heights below the lowest point
            var boxHeight = headH * 2.4f;
            var rowY = lowestNoteY + headH * 2f; // 2 note head heights below
            var gap = 1f; // Reduced from 1.5f

            // Check if all notes are correct (session complete)
            bool allCorrect = _session.FeedbackViewModels.Count == _session.NotesToDraw.Count &&
                _session.FeedbackViewModels.All(fb => fb.IsCorrect);

            bool stillContiguous = true;
            for (int i = 0; i < _session.NotesToDraw.Count && i < _session.FeedbackViewModels.Count; i++)
            {
                var note = _session.NotesToDraw[i];
                var fb = _session.FeedbackViewModels[i];

                var noteLeft = leftMargin + (note.X - minSrcX) * scale;
                var noteCenterX = noteLeft + headW / 2f;

                float spacingX;
                if (i < _session.NotesToDraw.Count - 1)
                {
                    var nextLeft = leftMargin + (_session.NotesToDraw[i + 1].X - minSrcX) * scale;
                    spacingX = Math.Max(headW * 1.5f, nextLeft - noteLeft);
                }
                else if (i > 0)
                {
                    var prevLeft = leftMargin + (_session.NotesToDraw[i - 1].X - minSrcX) * scale;
                    spacingX = Math.Max(headW * 1.5f, noteLeft - prevLeft);
                }
                else
                {
                    spacingX = headW * 2f;
                }

                var boxWidth = Math.Max(0, spacingX * 0.96f - gap * 2f);
                var boxX = noteCenterX - boxWidth / 2f;
                var boxY = rowY;

                // If all correct, mark all green; otherwise, only first contiguous group
                var fill = allCorrect
                    ? Colors.Lime.WithAlpha(0.95f)
                    : (stillContiguous && fb.IsCorrect)
                        ? Colors.Lime.WithAlpha(0.95f)
                        : Colors.DarkRed.WithAlpha(0.80f);

                if (!fb.IsCorrect && !allCorrect)
                {
                    stillContiguous = false;
                }

                canvas.FillColor = fill;
                canvas.StrokeColor = Colors.Black.WithAlpha(0.1f);
                canvas.FillRoundedRectangle(boxX, boxY, boxWidth, boxHeight, 3f);
            }
        }

        /// <summary>
        /// Compute and cache an estimated height required to render the staff, ledger lines,
        /// clef and feedback for the current session. This uses only numeric calculations
        /// (no platform text measurement) so it is safe to call from non-UI threads and
        /// does not invoke platform font subsystems.
        /// </summary>
        public float ComputeRequiredHeightOld(float width)
        {
            // Basic glyph geometry (kept in sync with Draw)
            var noteHeadH = 8f;
            var headW = noteHeadH * 1.5f;
            var staffSpacing = noteHeadH + 2f;

            // Core staff area (4 spaces = 4 * spacing between lines)
            var staffCoreHeight = 4f * staffSpacing;

            // Determine extreme note positions in diatonic steps relative to B4 (middle line reference)
            int maxStepsAbove = 0; // number of steps above middle line
            int maxStepsBelow = 0; // number of steps below middle line
            foreach (var note in _session.NotesToDraw)
            {
                var s = note.Name.Trim();
                if (string.IsNullOrEmpty(s))
                    continue;
                var letter = char.ToUpperInvariant(s[0]);
                if (!int.TryParse(s[^1].ToString(), out var octave))
                    continue;
                int stepsFromB4 = StaffStepsFromB4(letter, octave);
                if (stepsFromB4 < 0)
                    maxStepsAbove = Math.Max(maxStepsAbove, -stepsFromB4);
                else
                    maxStepsBelow = Math.Max(maxStepsBelow, stepsFromB4);
            }

            // Ensure clef glyph is also accounted for (G4 center)
            var clefLetter = 'G';
            var clefOctave = 4;
            var clefSteps = (clefOctave - 4) * 7 + (LetterIndex(clefLetter) - LetterIndex('B'));
            if (clefSteps < 0)
                maxStepsAbove = Math.Max(maxStepsAbove, -clefSteps);
            else
                maxStepsBelow = Math.Max(maxStepsBelow, clefSteps);

            var stepSize = staffSpacing / 2f; // same as GetYForSpelledNote

            // Extra vertical space to allow ledger lines and note heads beyond the 5-line staff
            var extraTop = maxStepsAbove * stepSize + noteHeadH * 1.5f;
            var extraBottom = maxStepsBelow * stepSize + noteHeadH * 2.5f; // room for feedback below

            // Bias the extra space towards the top so high notes have room above the core staff.
            // This shifts the staff lower in the graphics area.
            try
            {
                var mmToDp = 160.0 / 25.4;
                var shift = (float)(30.0 * mmToDp);
                extraTop += shift; // add extra space above the staff
            }
            catch
            {
                // ignore
            }

            // Feedback boxes underneath lowest notes
            var feedbackBoxHeight = noteHeadH * 2.4f + 4f;

            // Padding to keep content from touching edges
            var padding = 8f;

            var computed = staffCoreHeight + extraTop + extraBottom + feedbackBoxHeight + padding;

            // Keep a sensible minimum (matches earlier hard-coded value)
            _computedStaffHeight = Math.Max(220f, computed);
            return _computedStaffHeight;
        }

        private static int GetAccidentalCountForScale(string key, string selectedScale)
        {
            var isMinorLike = selectedScale is "Natural Minor" or "Harmonic Minor" or "Melodic Minor" or "Aeolian" or "Jazz Melodic Minor"
                              || selectedScale.Contains("Minor", StringComparison.OrdinalIgnoreCase)
                              || selectedScale.Contains("Melodic", StringComparison.OrdinalIgnoreCase);
            if (!isMinorLike)
            {
                // Major keys (Ionian/major-like)
                return key switch
                {
                    "C" => 0,
                    "G" => 1,
                    "D" => 2,
                    "A" => 3,
                    "E" => 4,
                    "B" => 5,
                    "F#" => 6,
                    "C#" => 7,
                    "F" => -1,
                    "Bb" => -2,
                    "Eb" => -3,
                    "Ab" => -4,
                    "Db" => -5,
                    "Gb" => -6,
                    "Cb" => -7,
                    _ => 0
                };
            }

            // Natural minor (relative minor) key signatures
            return key switch
            {
                "A" => 0,
                "E" => 1,
                "B" => 2,
                "F#" => 3,
                "C#" => 4,
                "G#" => 5,
                "D#" => 6,
                "A#" => 7,
                "D" => -1,
                "G" => -2,
                "C" => -3,
                "F" => -4,
                "Bb" => -5,
                "Eb" => -6,
                "Ab" => -7,
                _ => 0
            };
        }
        private  void DrawKeySignatureOld(ICanvas canvas, float top, float spacing, string key, float startX, float symbolWidth, float symbolSpacing,
            float headW, int countOverride, Color fillColor, Color strokeColor)
        {
            // Historical alias kept for compatibility — delegate to the maintained implementation.
            DrawKeySignature(canvas, top, spacing, key, startX, symbolWidth, symbolSpacing, headW, countOverride, fillColor, strokeColor);
        }

        private static string? GetSignatureAccidentalForLetter(char letter, int signatureCount)
        {
            if (signatureCount == 0)
            {
                return null;
            }

            var sharpsOrder = new[] { 'F', 'C', 'G', 'D', 'A', 'E', 'B' };
            var flatsOrder = new[] { 'B', 'E', 'A', 'D', 'G', 'C', 'F' };

            if (signatureCount > 0)
            {
                return sharpsOrder.Take(signatureCount).Contains(letter) ? "#" : null;
            }

            var abs = Math.Abs(signatureCount);
            return flatsOrder.Take(abs).Contains(letter) ? "b" : null;
        }
    }
}
