using Microsoft.Maui.Graphics;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Drawables
{
    public class StaffDrawable : IDrawable
    {
        private float                           correctionFactor = 1.3f;  //  2026.04.04 1131  1.5f too big;
        private const float                     flatSizeBoost = 1.5f;  //  2026.04.05 0916  1.25f;
        private readonly NoteSessionService     _session;
        private readonly ThemeService           _theme_service;
        // Cached computed height for the staff band (measured outside of Draw)
        private float                           _computedStaffHeight = 220f;


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
            // Basic primitives
            var noteHeadH = 6f;//was 8f
            var headW = noteHeadH * 1.5f;
            var staffSpacing = noteHeadH + 2f;

            canvas.SaveState();
            canvas.SaveState();

            // Colors
            var backgroundColor = _theme_service.PanelBackgroundColor;
            var contrastColor = _theme_service.ContrastingTextColor;

            // Fill background
            canvas.FillColor = backgroundColor;
            canvas.FillRectangle(dirtyRect.X, 0f, dirtyRect.Width, dirtyRect.Height);

            canvas.StrokeColor = contrastColor;
            canvas.FontColor = contrastColor;

            // Calculate content layout from the top with exact 8px margin
            const float topMargin = 8f;

            // Determine extremes (same as ComputeRequiredHeight)
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

            // Include tuner last note
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
                catch { }
            }

            // In Tuner mode, also seed the range from the session note range so the
            // staff is always the same size as it is in scale modes.
            if (_session.Tune == "Tuner")
            {
                foreach (var noteName in new[] { _session.LowestNote, _session.HighestNote })
                {
                    if (string.IsNullOrEmpty(noteName)) continue;
                    var sR = noteName.Trim();
                    var letterR = char.ToUpperInvariant(sR[0]);
                    if (!int.TryParse(sR[^1].ToString(), out var octaveR)) continue;
                    int stepsR = StaffStepsFromB4(letterR, octaveR);
                    if (stepsR < 0) maxStepsAbove = Math.Max(maxStepsAbove, -stepsR);
                    else            maxStepsBelow = Math.Max(maxStepsBelow, stepsR);
                }
            }

            // Include clef center (G4)
            var clefSteps = StaffStepsFromB4('G', 4);
            if (clefSteps < 0) maxStepsAbove = Math.Max(maxStepsAbove, -clefSteps);
            else maxStepsBelow = Math.Max(maxStepsBelow, clefSteps);

            var stepSize = staffSpacing / 2f;
            var extraTop = maxStepsAbove * stepSize + noteHeadH * 1.5f;
            var extraBottom = maxStepsBelow * stepSize + noteHeadH * 2.5f;

            // Calculate staff positions exactly as Draw method does
            var staffCoreTop = topMargin + extraTop;
            var staffCoreBottom = staffCoreTop + 4f * staffSpacing;

            // Draw staff lines
            var staffLineLeftMargin = 8f;
            // The tuner text column is outside the TunerBorder canvas (separate Grid column),
            // so no right-margin cutoff is needed — fill to the right edge with a small margin.
            var staffLineRightMargin = 8f;
            for (int i = 0; i < 5; i++)
            {
                var y = staffCoreTop + i * staffSpacing;
                canvas.DrawLine(staffLineLeftMargin, y, dirtyRect.Width - staffLineRightMargin, y);
            }

            // Draw clef
            var middleLineY = staffCoreTop + 2f * staffSpacing;
            var clefCenterY = GetYForSpelledNote("G4", middleLineY, staffSpacing);
            var clefX = 16f;
            var clefW = headW * 1.6f * 3f;
            var clefH = headW * 3.2f * 3f;
            var clefY = clefCenterY - (clefH / 2f) - 1.5f * staffSpacing;
            try
            {
                if (clefY < 6f) clefY = 6f;
                canvas.FontSize = 18.666f * 3f;
                canvas.DrawString("𝄞", clefX, clefY, clefW, clefH, HorizontalAlignment.Left, VerticalAlignment.Center);
            }
            catch
            {
                if (clefY < 6f) clefY = 6f;
                DrawTrebleClef(canvas, clefX, clefY, clefW, clefH, contrastColor);
            }

            // Key signature and note horizontal layout.
            // The KEY SIGNATURE reflects the tonal centre and scale TYPE, not individual note accidentals.
            // Accidentals that deviate from the key signature (e.g. raised 6th/7th in melodic minor,
            // raised 7th in harmonic minor) are drawn per-note in DrawNoteWithLedger — they are NOT
            // part of the key signature.
            // Chromatic scale, Tuner, and Practice Tune show no key signature.
            var accidentalCount = (_session.Tune == "Tuner"
                                   || _session.Tune == "Practice Tune"
                                   || _session.SelectedScale == "Chromatic")
                ? 0
                : GetAccidentalCountForScale(_session.Key, _session.SelectedScale);
            var accScale = 1.5f;
            var accWidth = headW * accScale;
            // Slightly wider spacing so key-signature symbols have breathing room in dense keys (e.g. 7 flats).
            var accSpacing = headW * 0.48f * accScale;
            var accStartX = clefX + clefW * 0.65f + 4f;

            var desiredHeadPadding = 3f * headW;
            var fallbackPadding = 32f;
            var extraLeftPadding = _session.SelectedScale == "Chromatic"
                ? headW
                : _session.Tune == "Practice Tune"
                    ? headW * 1.2f          // tight gap after time signature
                    : Math.Max(fallbackPadding, desiredHeadPadding);

            // Width consumed by the key signature symbols (each slot + the final glyph's own body)
            var keySigGlyphSize = accWidth * correctionFactor * (accidentalCount < 0 ? flatSizeBoost : 1.0f);
            var keySigWidth = Math.Abs(accidentalCount) > 0
                ? (Math.Abs(accidentalCount) - 1) * (accSpacing + 2f) + keySigGlyphSize
                : 0f;

            // For Practice Tune, reserve space for: gap (headW) + time sig box (1.6 * staffSpacing)
            var timeSigWidth = (_session.Tune == "Practice Tune") ? headW + staffSpacing * 1.6f + headW : 0f;

            var leftMargin = accStartX + keySigWidth + timeSigWidth + extraLeftPadding;
            var rightMargin = headW + 16f;
            try
            {
                DrawKeySignature(canvas, staffCoreTop, staffSpacing, _session.Key, accStartX, accWidth, accSpacing, headW, accidentalCount, backgroundColor, contrastColor);
            }
            catch { }

            // Horizontal scaling
            float minSrcX = float.MaxValue, maxSrcX = float.MinValue;
            foreach (var n in _session.NotesToDraw)
            {
                if (n.X < minSrcX) minSrcX = n.X;
                if (n.X > maxSrcX) maxSrcX = n.X;
            }
            // Include rest slots in range so they scale together with the notes
            foreach (var rx in _session.RestXPositions)
            {
                if (rx < minSrcX) minSrcX = rx;
                if (rx > maxSrcX) maxSrcX = rx;
            }
            if (minSrcX == float.MaxValue) { minSrcX = 0f; maxSrcX = 1f; }

            var targetRange = Math.Max(1f, dirtyRect.Width - leftMargin - rightMargin);
            var srcRange = Math.Max(1f, maxSrcX - minSrcX);
            var scale = targetRange / srcRange;

            // Draw notes
            if (_session.Tune == "Tuner")
            {
                var tunerName = _session.TunerLastNoteName;
                if (!string.IsNullOrEmpty(tunerName))
                {
                    // Layout: [staff left] [clef] [note] [trailing staff lines …] [canvas edge] | gap | text column
                    // Place the note at ~20 % of the canvas width so the staff lines continue
                    // clearly to the right of the note head.  Enforce a hard minimum so the note
                    // (and any ♯/♭ to its left) never overlaps the clef.
                    var clefRightEdge = clefX + clefW;
                    var minAccClearance = headW * 1.5f * correctionFactor * flatSizeBoost + headW * 1.2f;
                    var noteZoneLeft = clefRightEdge + minAccClearance;
                    var centerX = Math.Max(noteZoneLeft, dirtyRect.Width * 0.2f);  //  2026.05.16 1713   0.42f);
                    var tunerHeadH = noteHeadH * 1.2f;
                    var tunerHeadW = tunerHeadH * 1.5f;
                    DrawCenteredNote(canvas, tunerName, centerX, staffCoreTop, staffSpacing, tunerHeadH, tunerHeadW, contrastColor, contrastColor);
                }
            }
            else
            {
                // ── Time signature + bar lines (Practice Tune mode only) ────────────────────
                if (_session.Tune == "Practice Tune" && _session.CurrentTune != null)
                {
                    var ts = _session.CurrentTune.TimeSignature;
                    // Place time sig immediately after the key signature, with a clear gap
                    var timeSigX = accStartX + keySigWidth + headW;
                    DrawTimeSignature(canvas, timeSigX, staffCoreTop, staffSpacing, ts, contrastColor);

                    // Bar lines: thin vertical lines spanning the full staff height
                    canvas.SaveState();
                    canvas.StrokeColor = contrastColor;
                    canvas.StrokeSize  = Math.Max(1f, headW * 0.08f);
                    foreach (var barX in _session.MeasureBarXPositions)
                    {
                        var scaledBarX = leftMargin + (barX - minSrcX) * scale;
                        canvas.DrawLine(scaledBarX, staffCoreTop, scaledBarX, staffCoreTop + 4f * staffSpacing);
                    }
                    // Final bar line (double) after the rightmost slot (note or rest)
                    if (_session.NotesToDraw.Count > 0 || _session.RestXPositions.Count > 0)
                    {
                        var lastX = leftMargin + (maxSrcX - minSrcX) * scale + headW * 1.5f;
                        var thin  = Math.Max(1f, headW * 0.08f);
                        var thick = thin * 3f;
                        canvas.StrokeSize  = thin;
                        canvas.DrawLine(lastX, staffCoreTop, lastX, staffCoreTop + 4f * staffSpacing);
                        canvas.StrokeSize  = thick;
                        canvas.DrawLine(lastX + thin * 2f, staffCoreTop, lastX + thin * 2f, staffCoreTop + 4f * staffSpacing);
                    }
                    canvas.RestoreState();

                    // Rest symbols (quarter, half, whole) — dispatch by duration
                    for (int ri = 0; ri < _session.RestXPositions.Count; ri++)
                    {
                        var restSrcX = _session.RestXPositions[ri];
                        var rx = leftMargin + (restSrcX - minSrcX) * scale;
                        var dur = ri < _session.RestDurations.Count ? _session.RestDurations[ri] : NoteDuration.Quarter;
                        switch (dur)
                        {
                            case NoteDuration.Whole:
                                DrawWholeRest(canvas, rx, staffCoreTop, staffSpacing, headW, contrastColor);
                                break;
                            case NoteDuration.Half:
                                DrawHalfRest(canvas, rx, staffCoreTop, staffSpacing, headW, contrastColor);
                                break;
                            default:
                                DrawQuarterRest(canvas, rx, staffCoreTop, staffSpacing, headW, contrastColor);
                                break;
                        }
                    }
                }

                var activeAccidentals = new Dictionary<(char, int), string>();
                foreach (var n in _session.NotesToDraw)
                {
                    var scaledX = leftMargin + (n.X - minSrcX) * scale;
                    DrawNoteWithLedger(canvas, n, staffCoreTop, staffCoreBottom, staffSpacing, noteHeadH, headW, scaledX, accidentalCount, backgroundColor, contrastColor, true, activeAccidentals);
                }
            }

            // Draw feedback boxes - no shifting, just positioned naturally
            DrawSmallFeedback(canvas, staffCoreTop, staffCoreBottom, staffSpacing, noteHeadH, headW, leftMargin, minSrcX, scale, dirtyRect.Height);

            canvas.RestoreState();
            canvas.RestoreState();
        }

        /// <summary>
        /// Compute and cache an estimated height required to render the staff, ledger lines,
        /// clef and feedback for the current session. This uses only numeric calculations
        /// (no platform text measurement) so it is safe to call from non-UI threads and
        /// does not invoke platform font subsystems.
        /// </summary>
        public float ComputeRequiredHeight(float canvasWidth)
        {
            // Use the EXACT same constants as the Draw method for consistency
            const float topMargin = 8f;  // Match Draw method exactly
            var noteHeadH = 8f;           // Match Draw method exactly
            var staffSpacing = noteHeadH + 2f;  // Match Draw method calculation

            // Calculate staff dimensions exactly as Draw method does
            int maxStepsAbove = 0, maxStepsBelow = 0;

            foreach (var note in _session.NotesToDraw)
            {
                var s = note.Name.Trim();
                var letter = char.ToUpperInvariant(s[0]);
                if (!int.TryParse(s[^1].ToString(), out var octave))
                    continue;
                int stepsFromB4 = StaffStepsFromB4(letter, octave);
                if (stepsFromB4 < 0)
                    maxStepsAbove = Math.Max(maxStepsAbove, -stepsFromB4);
                else
                    maxStepsBelow = Math.Max(maxStepsBelow, stepsFromB4);
            }

            // Include tuner last note
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
                catch { }
            }

            // In Tuner mode, also seed the range from the session note range so the
            // computed height matches scale modes.
            if (_session.Tune == "Tuner")
            {
                foreach (var noteName in new[] { _session.LowestNote, _session.HighestNote })
                {
                    if (string.IsNullOrEmpty(noteName)) continue;
                    var sR = noteName.Trim();
                    var letterR = char.ToUpperInvariant(sR[0]);
                    if (!int.TryParse(sR[^1].ToString(), out var octaveR)) continue;
                    int stepsR = StaffStepsFromB4(letterR, octaveR);
                    if (stepsR < 0) maxStepsAbove = Math.Max(maxStepsAbove, -stepsR);
                    else            maxStepsBelow = Math.Max(maxStepsBelow, stepsR);
                }
            }

            // Include clef center (G4)
            var clefSteps = StaffStepsFromB4('G', 4);
            if (clefSteps < 0) maxStepsAbove = Math.Max(maxStepsAbove, -clefSteps);
            else maxStepsBelow = Math.Max(maxStepsBelow, clefSteps);

            var stepSize = staffSpacing / 2f;
            var extraTop = maxStepsAbove * stepSize + noteHeadH * 1.5f;
            var extraBottom = maxStepsBelow * stepSize + noteHeadH * 2.5f;

            // Position everything from the top with exact margins - no shifting needed
            var staffCoreTop = topMargin + extraTop;
            var staffCoreBottom = staffCoreTop + 4f * staffSpacing;

            // Calculate feedback position using EXACT same logic as DrawSmallFeedback
            var lowestNoteY = staffCoreBottom; // Default to bottom staff line
            var middleLineY = staffCoreTop + 2f * staffSpacing;

            foreach (var note in _session.NotesToDraw)
            {
                var noteY = GetYForSpelledNote(note.Name, middleLineY, staffSpacing);
                if (noteY > lowestNoteY)
                    lowestNoteY = noteY;
            }

            // Exact same feedback calculation as DrawSmallFeedback
            var boxHeight = noteHeadH * 2.4f;
            var feedbackRowY = lowestNoteY + noteHeadH * 2f;
            var feedbackBottom = feedbackRowY + boxHeight;

            // Use more negative bottom margin to achieve the tight 8px spacing
            // Layout has multiple spacing elements, so we need to compensate more aggressively
            const float bottomMargin = 8f;//-16f; // More negative to pull graphics view smaller
            var totalHeight = feedbackBottom + bottomMargin;

            // DEBUG: Log all the calculations
            System.Diagnostics.Debug.WriteLine($"[ComputeRequiredHeight] staffCoreTop={staffCoreTop:F1}, staffCoreBottom={staffCoreBottom:F1}");
            System.Diagnostics.Debug.WriteLine($"[ComputeRequiredHeight] lowestNoteY={lowestNoteY:F1}, feedbackRowY={feedbackRowY:F1}, feedbackBottom={feedbackBottom:F1}");
           // System.Diagnostics.Debug.WriteLine($"[ComputeRequiredHeight] totalHeight={totalHeight:F1}, bottomMargin={bottomMargin:F1}");

            // Cache and return the computed height with aggressive negative margin
            _computedStaffHeight = totalHeight;
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

            for (int i = 0; i < abs && i < notes.Length; i++)
            {
                var note = notes[i];
                float pitchY = GetYForSpelledNote(note, middleLineY, spacing);

                // ♯  — crossing bars sit at ≈ 50 % of the glyph bounding-box; factor = 0.50
                //        centres the bars on pitchY.
                // ♭  — oval body sits at ≈ 60 % of the glyph bounding-box height;
                //        factor = 0.62 shifts the box up so the oval lands on pitchY.
                float drawBoxTopY = isFlat
                    ? pitchY - glyphSize * 0.5f  // flat   2026.05.30 0957  0.62f
                    : pitchY - glyphSize * 0.7f;// sharp .4 int the space below, .45 same, .55 bit higher
                                                // .7 perfect

                float x = startX + i * (symbolSpacing + 2f);

                canvas.DrawString(
                    isFlat ? "♭" : "♯",
                    x,
                    drawBoxTopY,
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
             float headH, float headW, float centerX, int accidentalCount, Color fillColor, Color strokeColor, bool drawAccidental,
             Dictionary<(char, int), string>? activeAccidentals = null)
        {
            canvas.SaveState();
            canvas.FillColor = fillColor;
            canvas.StrokeColor = strokeColor;

            var dur = note.Duration;
            bool isOpen  = dur == NoteDuration.Half || dur == NoteDuration.Whole;
            bool hasStem = dur != NoteDuration.Whole;
            bool hasFlag = dur == NoteDuration.Eighth;

            var middleLineY = staffTop + spacing * 2;
            var noteY = GetYForSpelledNote(note.Name, middleLineY, spacing);
            var xLeft = centerX - headW / 2f;
            var baseThickness = Math.Max(1f, headW * 0.06f);

            // Draw head background layer (filled with fillColor so the hole punches through for open noteheads)
            canvas.FillColor = fillColor;
            canvas.FillEllipse(xLeft, noteY - headH / 2f, headW, headH);

            // Ledger lines (before outline so note sits on top)
            DrawLedgerLines(canvas, centerX, noteY, staffTop, staffTop + 4 * spacing, spacing, headW, fillColor, strokeColor);

            // Stem
            canvas.StrokeColor = strokeColor;
            canvas.StrokeSize = baseThickness;
            bool stemUp = noteY >= middleLineY;
            float stemLength = spacing * 3f;
            float stemX, stemTipY;
            if (hasStem)
            {
                if (stemUp)
                {
                    stemX    = xLeft + headW;
                    stemTipY = noteY - stemLength;
                    canvas.DrawLine(stemX, noteY, stemX, stemTipY);
                }
                else
                {
                    stemX    = xLeft;
                    stemTipY = noteY + stemLength;
                    canvas.DrawLine(stemX, noteY, stemX, stemTipY);
                }

                // Eighth flag
                if (hasFlag)
                {
                    var flagW = headW * 0.9f;
                    var flagH = spacing * 1.2f;
                    if (stemUp)
                    {
                        // flag curves down-right from the tip
                        canvas.DrawLine(stemX, stemTipY, stemX + flagW, stemTipY + flagH * 0.5f);
                        canvas.DrawLine(stemX + flagW, stemTipY + flagH * 0.5f, stemX + flagW * 0.5f, stemTipY + flagH);
                    }
                    else
                    {
                        // flag curves up-right from the tip
                        canvas.DrawLine(stemX, stemTipY, stemX + flagW, stemTipY - flagH * 0.5f);
                        canvas.DrawLine(stemX + flagW, stemTipY - flagH * 0.5f, stemX + flagW * 0.5f, stemTipY - flagH);
                    }
                }
            }

            // Draw notehead outline
            canvas.StrokeColor = strokeColor;
            canvas.StrokeSize = baseThickness * (isOpen ? 2.5f : 1f);
            canvas.DrawEllipse(xLeft, noteY - headH / 2f, headW, headH);

            if (!isOpen)
            {
                // Filled notehead: paint over with solid stroke color
                canvas.FillColor = strokeColor;
                canvas.FillEllipse(xLeft, noteY - headH / 2f, headW, headH);
            }

            // --- Accidental display with proper music-theory rules ---
            if (drawAccidental)
            {
                var raw = note.Name.Trim();
                char letter = char.ToUpperInvariant(raw[0]);

                // Parse octave from note name
                int noteOctave = 4;
                for (int oi = raw.Length - 1; oi >= 0; oi--)
                {
                    if (char.IsDigit(raw[oi]))
                    {
                        int oj = oi;
                        while (oj > 0 && char.IsDigit(raw[oj - 1])) oj--;
                        if (int.TryParse(raw.Substring(oj, oi - oj + 1), out var oct)) noteOctave = oct;
                        break;
                    }
                }
                var noteKey = (letter, noteOctave);

                // sigAcc: the accidental this letter carries in the key signature ("#", "b", or null).
                // A note accidental glyph is shown only when the note DEVIATES from the key signature.
                // Examples for C minor (3 flats: Bb, Eb, Ab):
                //   Eb4 → sigAcc="b", wantsFlat=true  → no glyph (already implied by key sig)
                //   E4  → sigAcc="b", wantsNatural=true → glyph="♮" (raised: Melodic/Harmonic minor context)
                //   B4  → sigAcc="b", wantsNatural=true → glyph="♮" (raised leading tone in Harmonic minor)
                //   A4  → sigAcc="b", wantsNatural=true → glyph="♮" (raised 6th in Melodic minor ascending)
                var sigAcc = GetSignatureAccidentalForLetter(letter, accidentalCount);

                bool wantsDoubleSharp = raw.Contains("##");
                bool wantsDoubleFlat = raw.Contains("bb");
                bool wantsSharp = !wantsDoubleSharp && raw.Contains('#');
                bool wantsFlat = !wantsDoubleFlat && raw.Contains('b');
                bool wantsNatural = !wantsSharp && !wantsFlat && !wantsDoubleSharp && !wantsDoubleFlat;

                string? accidentalGlyph = null;
                bool isAccFlat = false;

                if (wantsDoubleSharp && sigAcc != "#")
                {
                    accidentalGlyph = "𝄪";
                }
                else if (wantsDoubleFlat && sigAcc != "b")
                {
                    accidentalGlyph = "𝄫";
                    isAccFlat = true;
                }
                else if (wantsSharp && sigAcc != "#")
                {
                    accidentalGlyph = "♯";
                }
                else if (wantsFlat && sigAcc != "b")
                {
                    accidentalGlyph = "♭";
                    isAccFlat = true;
                }
                else if (wantsNatural && sigAcc != null)
                {
                    accidentalGlyph = "♮";
                }
                // Courtesy accidental: this (letter, octave) was locally altered earlier in the sequence.
                // Show whichever accidental the key signature requires, or ♮ if none — purely visual.
                else if (activeAccidentals != null && activeAccidentals.ContainsKey(noteKey))
                {
                    accidentalGlyph = sigAcc == "#" ? "♯" : sigAcc == "b" ? "♭" : "♮";
                    isAccFlat = accidentalGlyph == "♭";
                }

                // Update the within-sequence tracker keyed by (letter, octave)
                if (activeAccidentals != null && accidentalGlyph != null)
                    activeAccidentals[noteKey] = accidentalGlyph;

                if (accidentalGlyph != null)
                {
                    var accSize = headW * 1.5f * correctionFactor * (isAccFlat ? flatSizeBoost : 1.0f);
                    canvas.FontSize = accSize;
                    canvas.FontColor = strokeColor;

                    // ♯ : crossing bars at ≈ 50 % of glyph box → factor 0.50.
                    // ♮ : visual centre sits lower in box (≈ 55 %) → factor 0.55.
                    // ♭ : oval body at ≈ 62 % of glyph box → factor 0.62.
                    bool isNatural = accidentalGlyph == "♮";
                    var drawY = isAccFlat
                        ? noteY - accSize * 0.62f      // flat
                        : isNatural
                            ? noteY - accSize * 0.70f  // natural was .55  //  2026.05.30 1004  
                            : noteY - accSize * 0.50f; // sharp

                    const float mmToDp = 160f / 25.4f;
                    var accX = isAccFlat
                        ? xLeft - headW * 1.1f - 1.5f * mmToDp
                        : xLeft - headW * 1.1f - 0.7f * mmToDp;

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

        // Draw a centered tuner-style note
        private void DrawCenteredNote(ICanvas canvas, string noteName, float centerX, float staffTop, float spacing, float headH, float headW,
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
                canvas.DrawLine(xLeft, noteY, xLeft, noteY + stemLength);
            else
                canvas.DrawLine(xLeft + headW, noteY, xLeft + headW, noteY - stemLength);

            DrawLedgerLines(canvas, centerX, noteY, staffTop, staffTop + 4 * spacing, spacing, headW, fillColor, strokeColor);

            // Outline/fill for visibility
            canvas.FillColor = strokeColor;
            canvas.FillEllipse(xLeft, noteY - headH / 2f, headW, headH);

            // Accidental: Tuner uses C key signature (no accidentals), so every ♯/♭ in the
            // note name must be shown. Letter names are uppercase; 'b' is always the flat sign.
            var raw = noteName.Trim();
            bool wantsSharp = raw.Contains('#');
            bool wantsFlat = raw.Contains('b');

            string? accidentalGlyph = wantsSharp ? "♯" : wantsFlat ? "♭" : null;
            bool isAccFlat = wantsFlat && !wantsSharp;

            if (accidentalGlyph != null)
            {
                var accSize = headW * 1.5f * correctionFactor * (isAccFlat ? flatSizeBoost : 1.0f);
                canvas.FontSize = accSize;
                canvas.FontColor = strokeColor;

                // ♯ / ♮ : centre glyph box on the note's pitch position.
                // ♭     : oval body is at ≈ 70 % of glyph height — shift box up so oval lands on pitch.
                var drawY = isAccFlat
                    ? noteY - accSize * 0.70f
                    : noteY - accSize * 0.50f;

                const float mmToDp = 160f / 25.4f;
                var accX = isAccFlat
                    ? xLeft - headW * 1.1f - 1.5f * mmToDp   // ♭ needs slightly more clearance
                    : xLeft - headW * 1.1f - 0.7f * mmToDp;  // ♯ / ♮

                canvas.DrawString(accidentalGlyph,
                    accX, drawY, accSize, accSize,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
            }

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
            var feedbackBottom = rowY + boxHeight;

            // DEBUG: Log feedback positioning
            System.Diagnostics.Debug.WriteLine($"[DrawSmallFeedback] top={top:F1}, bottom={bottom:F1}, canvasHeight={canvasHeight:F1}");
            System.Diagnostics.Debug.WriteLine($"[DrawSmallFeedback] lowestNoteY={lowestNoteY:F1}, rowY={rowY:F1}, feedbackBottom={feedbackBottom:F1}");
            System.Diagnostics.Debug.WriteLine($"[DrawSmallFeedback] bottomMargin={(canvasHeight - feedbackBottom):F1}");

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

                // Limit feedback rectangle width so it doesn't become excessively wide on large displays
                const float MaxFeedbackBoxWidth = 60f; // pixels
                var desiredBoxWidth = Math.Max(0, spacingX * 0.96f - gap * 2f);
                var boxWidth = Math.Min(MaxFeedbackBoxWidth, desiredBoxWidth);
                var boxX = noteCenterX - boxWidth / 2f; // keep centered on note head
                var boxY = rowY;

                // In DrawSmallFeedback, use PlaybackHighlightIndex to draw a transient visual highlight.
                // Replace the fill selection block with this variant (inside the loop where `fb` is available):

                // If playback is highlighting this index, force highlight visually (visual-only; session state unchanged).
                var fill = Colors.DarkRed.WithAlpha(0.80f);
                if (_session.PlaybackHighlightIndex.HasValue && _session.PlaybackHighlightIndex.Value == i)
                {
                    fill = Colors.Lime.WithAlpha(0.95f);
                }
                else
                {
                    // Existing logic: If all correct, mark all green; otherwise, only first contiguous group
                    fill = allCorrect
                        ? Colors.Lime.WithAlpha(0.95f)
                        : (stillContiguous && fb.IsCorrect)
                            ? Colors.Lime.WithAlpha(0.95f)
                            : Colors.DarkRed.WithAlpha(0.80f);
                }

                if (!fb.IsCorrect && !allCorrect)
                {
                    stillContiguous = false;
                }

                canvas.FillColor = fill;
                canvas.StrokeColor = Colors.Black.WithAlpha(0.1f);
                canvas.FillRoundedRectangle(boxX, boxY, boxWidth, boxHeight, 3f);

                // Show cents deviation inside the box once the note has been played correctly
                if (fb.IsCorrect || fb.CentsDeviation != 0)
                {
                    canvas.FontSize = Math.Max(8f, boxHeight * 0.42f);  //  2026.04.09 1103  6f->8f
                    canvas.FontColor = Colors.Black;
                    canvas.DrawString(
                        fb.CentsText,
                        boxX, boxY, boxWidth, boxHeight,
                        HorizontalAlignment.Center, VerticalAlignment.Center);
                }
            }
        }

        /// <summary>
        /// Compute and cache an estimated height required to render the staff, ledger lines,
        /// clef and feedback for the current session. This uses only numeric calculations
        /// (no platform text measurement) so it is safe to call from non-UI threads and
        /// does not invoke platform font subsystems.
        /// </summary>
       

        /// <summary>
        /// Returns the number of sharps (positive) or flats (negative) for the key signature
        /// to display on the staff.
        /// <para>
        /// KEY SIGNATURE vs. SCALE ACCIDENTALS — important distinction:
        /// The key signature shows the tonal centre's standard sharps/flats. Scale-specific
        /// chromatic alterations (e.g. raised ♭6 and ♭7 ascending in Melodic Minor, raised ♭7
        /// in Harmonic Minor) are NOT shown in the key signature; they appear as per-note
        /// accidentals drawn by DrawNoteWithLedger.
        /// </para>
        /// <para>
        /// Harmonic Minor and Melodic Minor share the same key signature as Natural Minor
        /// for their tonic.  C Harmonic Minor and C Melodic Minor both show 3 flats (Bb, Eb, Ab),
        /// exactly like C Natural Minor.  The raised 7th (B♮) in C Harmonic Minor and the
        /// raised 6th (A♮) and 7th (B♮) in C Melodic Minor ascending appear as ♮ accidentals
        /// on the affected note heads.
        /// </para>
        /// <para>
        /// Modal scales (Dorian, Phrygian, Lydian, Mixolydian, Locrian) are modes of the major
        /// scale.  Their key signature reflects the parent major key, e.g. C Dorian = Bb major
        /// (2 flats), C Phrygian = Ab major (4 flats), C Lydian = G major (1 sharp).
        /// </para>
        /// </summary>
        private static int GetAccidentalCountForScale(string key, string selectedScale)
        {
            // Scales that use the NATURAL-MINOR key signature for their tonic.
            // Harmonic and Melodic minor keep the natural-minor signature; their extra
            // chromatic notes are shown as per-note accidentals, not in the key signature.
            var isMinorLike = selectedScale is
                "Natural Minor" or "Harmonic Minor" or "Melodic Minor" or
                "Aeolian" or "Jazz Melodic Minor" or
                "Hungarian Minor" or "Neapolitan Minor" or
                "Minor Pentatonic" or "Minor Blues";

            if (isMinorLike)
            {
                // Natural-minor key signatures (same count used for Harmonic/Melodic minor).
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
                    "C" => -3,  // C minor: Bb, Eb, Ab
                    "F" => -4,
                    "Bb" => -5,
                    "Eb" => -6,
                    "Ab" => -7,
                    _ => 0
                };
            }

            // Modal scales: each mode is a rotation of the major scale, so its key signature
            // equals the parent major key (the major key whose tonic is the relevant degree).
            // Offsets below give the count of the parent major key, derived from circle-of-fifths.
            // Example: C Dorian = 2nd mode of Bb major → Bb major has 2 flats → return -2.
            var modalOffset = selectedScale switch
            {
                // Ionian = major (degree 1, offset 0)
                "Ionian" => 0,
                // Dorian = degree 2 of major scale, parent is a major 2nd below → -2 fifths
                "Dorian" => -2,
                // Phrygian = degree 3, parent is a major 3rd below → -4 fifths
                "Phrygian" => -4,
                // Lydian = degree 4, parent is a perfect 4th below → +1 fifth
                "Lydian" => 1,
                // Mixolydian = degree 5, parent is a perfect 5th below → -1 fifth
                "Mixolydian" => -1,
                // Locrian = degree 7, parent is a major 7th below → -5 fifths
                "Locrian" => -5,
                _ => int.MinValue  // sentinel: not a simple modal scale
            };

            if (modalOffset != int.MinValue)
            {
                // The parent major key's accidental count = this key's major count + modalOffset.
                int majorCount = key switch
                {
                    "C" => 0,  "G" => 1,  "D" => 2,  "A" => 3,  "E" => 4,  "B" => 5,
                    "F#" => 6, "C#" => 7,
                    "F" => -1, "Bb" => -2, "Eb" => -3, "Ab" => -4, "Db" => -5,
                    "Gb" => -6, "Cb" => -7,
                    _ => 0
                };
                // Clamp to valid range [-7, 7]
                return Math.Max(-7, Math.Min(7, majorCount + modalOffset));
            }

            // Major and all remaining scales use the standard major key signature.
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

        // Recomputes lowest note Y and feedback extents for the provided staff core placement.
        private void RecomputeFeedbackPositions(float staffCoreTop, float staffCoreBottom, float spacing, float noteHeadH,
                                                out float lowestNoteY, out float feedbackRowY, out float feedbackBottom)
        {
            lowestNoteY = staffCoreBottom;
            var middleLineY = staffCoreTop + 2f * spacing;

            foreach (var note in _session.NotesToDraw)
            {
                var noteY = GetYForSpelledNote(note.Name, middleLineY, spacing);
                if (noteY > lowestNoteY)
                    lowestNoteY = noteY;
            }

            var boxHeight = noteHeadH * 2.4f;
            feedbackRowY = lowestNoteY + noteHeadH * 2f;
            feedbackBottom = feedbackRowY + boxHeight;
        }

        /// <summary>
        /// Draws a time signature (e.g. 4/4) as two stacked numerals spanning the staff.
        /// Each numeral is centered on the middle two staff spaces.
        /// </summary>
        /// <summary>Half rest: a filled rectangle sitting ON top of the middle (3rd) staff line.</summary>
        private static void DrawHalfRest(ICanvas canvas, float cx, float staffTop, float spacing, float headW, Color color)
        {
            canvas.SaveState();
            canvas.FillColor = color;
            // Middle staff line (line 3) is at staffTop + 2*spacing
            var lineY = staffTop + 2f * spacing;
            var w = headW * 1.2f;
            var h = spacing * 0.45f;
            // Half rest sits on top of line 3
            canvas.FillRectangle(cx - w / 2f, lineY - h, w, h);
            canvas.RestoreState();
        }

        /// <summary>Whole rest: a filled rectangle hanging BELOW the 4th staff line.</summary>
        private static void DrawWholeRest(ICanvas canvas, float cx, float staffTop, float spacing, float headW, Color color)
        {
            canvas.SaveState();
            canvas.FillColor = color;
            // 4th staff line is at staffTop + 3*spacing
            var lineY = staffTop + 3f * spacing;
            var w = headW * 1.2f;
            var h = spacing * 0.45f;
            // Whole rest hangs below line 4 (down from line 4)
            canvas.FillRectangle(cx - w / 2f, lineY, w, h);
            canvas.RestoreState();
        }

        /// <summary>
        /// Uses a zigzag approximation: three short diagonal strokes forming the classic quarter-rest shape.
        /// </summary>
        private static void DrawQuarterRest(ICanvas canvas, float cx, float staffTop, float spacing, float headW, Color color)
        {
            canvas.SaveState();
            canvas.StrokeColor = color;
            canvas.StrokeLineCap = LineCap.Round;
            var sw = Math.Max(1.5f, headW * 0.12f);
            canvas.StrokeSize = sw;

            // The rest spans from line 2 to line 4 (two staff spaces in the middle).
            // staffTop + spacing = line 2;  staffTop + 3*spacing = line 4
            var top    = staffTop + spacing;
            var bottom = staffTop + 3f * spacing;
            var h      = bottom - top;         // = 2 * spacing
            var w      = h * 0.45f;            // proportional width

            // Classic quarter-rest zigzag: 4 points
            //   A (top-right)  → B (middle-left)  → C (just below middle, right)  → D (bottom-left)
            float ax = cx + w * 0.5f, ay = top;
            float bx = cx - w * 0.5f, by = top + h * 0.38f;
            float cx2 = cx + w * 0.35f, cy2 = top + h * 0.55f;
            float dx = cx - w * 0.5f, dy = bottom;

            canvas.DrawLine(ax, ay, bx, by);
            canvas.DrawLine(bx, by, cx2, cy2);
            canvas.DrawLine(cx2, cy2, dx, dy);

            canvas.RestoreState();
        }

        private static void DrawTimeSignature(ICanvas canvas, float x, float staffTop, float spacing,
            TimeSignature ts, Color color)
        {
            canvas.SaveState();
            canvas.FontColor = color;
            canvas.Font = new Microsoft.Maui.Graphics.Font("Arial", FontWeights.Bold);

            // Derive the bottom number: how many of that duration fit in a whole note.
            int bottomNumber = (int)Math.Round(4.0 / ts.BeatUnit.ToBeatValue());

            // Each numeral fills exactly half the staff height (2 * spacing).
            // Combined they span the full distance from top staff line to bottom staff line.
            var numeralH = spacing * 2f;
            // Font size slightly smaller than box height so DrawString fills the box.
            canvas.FontSize = numeralH * 0.88f;

            var boxW = spacing * 1.6f;
            var leftX = x - boxW * 0.5f;

            // Top number: sits in the upper two staff spaces (lines 1 – 3)
            canvas.DrawString(ts.Beats.ToString(),
                leftX, staffTop, boxW, numeralH,
                HorizontalAlignment.Center, VerticalAlignment.Center);

            // Bottom number: sits in the lower two staff spaces (lines 3 – 5)
            canvas.DrawString(bottomNumber.ToString(),
                leftX, staffTop + numeralH, boxW, numeralH,
                HorizontalAlignment.Center, VerticalAlignment.Center);

            canvas.RestoreState();
        }
    }
}
