using Microsoft.Maui.Graphics;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Drawables
{
    /// <summary>Visual state of a single note slot in the v2 staff display.</summary>
    public enum V2NoteState
    {
        /// <summary>Not yet reached — neutral notehead colour.</summary>
        Pending,
        /// <summary>The note the player must play next — highlighted.</summary>
        Current,
        /// <summary>Played correctly — green.</summary>
        Correct,
        /// <summary>At least one wrong attempt has been made (may still be the current target).</summary>
        Wrong
    }

    /// <summary>
    /// Music Mate v2 — simple measure staff drawable.
    /// Renders one or more measures of <see cref="GeneratedNote"/> objects onto a
    /// <see cref="ICanvas"/>.  This is an intentionally simple first implementation:
    /// no complex engraving, just filled/open noteheads, stems, staff lines,
    /// measure bar lines, and a highlighted current-note box.
    /// </summary>
    public class V2MeasureDrawable : IDrawable
    {
        // ── Session data ──────────────────────────────────────────────────────────
        private readonly NoteSessionService _session;
        private readonly ThemeService _theme;

        /// <summary>Flat list of notes to display (pitch notes + rests).</summary>
        public List<GeneratedNote> Notes { get; set; } = new();

        /// <summary>Beat positions (0-based) at which measure bar lines fall.</summary>
        public List<double> MeasureBarBeats { get; set; } = new();

        /// <summary>Index (into <see cref="Notes"/>) of the note currently being practiced.</summary>
        public int CurrentNoteIndex { get; set; }

        /// <summary>
        /// Per-note visual state, parallel to <see cref="Notes"/>.
        /// Updated by <c>MainPage</c> after each pitch evaluation.
        /// </summary>
        public V2NoteState[] NoteStates { get; set; } = Array.Empty<V2NoteState>();

        // ── Layout constants ──────────────────────────────────────────────────────
        private const float StaffLineSpacing  = 14f;  // pixels between adjacent staff lines
        private const float NoteHeadRadius    = 7f;
        private const float StemLength        = 40f;
        // LeftMargin is computed dynamically each Draw() to fit clef + key sig + time sig.
        // The field is updated at the top of Draw(); TargetZoneX uses it via a property.
        private float _leftMargin = 115f;
        private float LeftMargin => _leftMargin;
        private const float RightMargin       = 16f;
        private const float TopMargin         = 32f;
        private const float BottomMargin      = 32f;
        private const float PixelsPerBeat     = 60f;  // base px/beat for static (fit-all) mode

        // Scrolling mode: fixed px per beat so notes extend beyond the view width.
        // 72 px/beat gives a quarter note a comfortable slot on a phone.
        private const float ScrollPxPerBeat   = 48f;   // 2/3 of original 72 px per beat

        // The canvas X at which the current note is always pinned in scroll mode.
        // Sits at LeftMargin + one extra beat-slot so there is a little look-ahead to the left.
        private float TargetZoneX => _leftMargin + ScrollPxPerBeat;

        // ── Scroll mode toggle ────────────────────────────────────────────────────

        /// <summary>
        /// When <c>true</c> (default), the staff scrolls left so the current note is
        /// always pinned at the target zone.  Completed notes slide off to the left;
        /// upcoming notes are visible to the right.
        /// <para>
        /// When <c>false</c>, the old static layout is used: all notes are scaled to
        /// fit within the view width.  Set to <c>false</c> for the fallback / compare view.
        /// </para>
        /// </summary>
        public bool ScrollMode { get; set; } = true;

        // ── Layout state (updated each Draw call) ─────────────────────────────────

        /// <summary>
        /// Pixels per quarter-note beat as computed during the most recent
        /// <see cref="Draw"/> call. Zero until first draw.  Exposed so future
        /// features (metronome cursor, scroll sync, rhythm scoring) can convert
        /// beat positions to canvas X without duplicating the layout maths.
        /// </summary>
        public float LastComputedPxPerBeat { get; private set; }

        /// <summary>
        /// Returns the horizontal pixel width that the given note occupies in the
        /// current layout.  Reflects the note's <see cref="GeneratedNote.BeatDuration"/>.
        /// Returns 0 if <see cref="LastComputedPxPerBeat"/> has not been set yet.
        /// </summary>
        public float NoteSlotWidth(GeneratedNote note)
            => (float)(note.BeatDuration * LastComputedPxPerBeat);

        // ── Constructor ───────────────────────────────────────────────────────────
        public V2MeasureDrawable(NoteSessionService session, ThemeService theme)
        {
            _session = session;
            _theme   = theme;
        }

        // ── IDrawable ─────────────────────────────────────────────────────────────
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var bg  = _theme.PanelBackgroundColor;
            var ink = _theme.ContrastingTextColor;

            // Background
            canvas.FillColor = bg;
            canvas.FillRectangle(dirtyRect);

            if (Notes.Count == 0)
            {
                canvas.FontColor = ink;
                canvas.FontSize  = 14;
                canvas.DrawString("V2 Mode — no notes generated yet",
                    dirtyRect.X + 8, dirtyRect.Y + dirtyRect.Height / 2f,
                    HorizontalAlignment.Left);
                return;
            }

            // ── Measure header width (clef + key sig + time sig) → sets _leftMargin ────
            // Compute without drawing so that note layout uses the correct margin.
            _leftMargin = ComputeHeaderWidth();

            // ── Staff geometry ────────────────────────────────────────────────────
            // Compute the topmost note Y to push the staff down far enough that all
            // ledger lines and pitch labels fit above staffTop without being clipped.
            float staffTop;
            {
                float fixedTop = TopMargin + StaffLineSpacing * 2f;
                float fixedMid = fixedTop + StaffLineSpacing * 2f;
                float minNoteY = fixedTop;
                foreach (var n in Notes)
                {
                    if (n.IsRest) continue;
                    float ny = NoteY(n, fixedTop, fixedMid);
                    float topEdge = ny - NoteHeadRadius - 18f;  // 18px for pitch label
                    if (topEdge < minNoteY) minNoteY = topEdge;
                }
                // Push staffTop down so the highest note's top edge has TopMargin clearance.
                float extraPush = Math.Max(0f, TopMargin - minNoteY);
                staffTop = fixedTop + extraPush;
            }
            float staffMid = staffTop + StaffLineSpacing * 2f;   // line 3 (B4)
            float staffBot = staffTop + StaffLineSpacing * 4f;   // line 5 (F4)

            // ── Determine layout scale and scroll offset ──────────────────────────
            float lineWidth = dirtyRect.Width - LeftMargin - RightMargin;
            float pxPerBeat;
            float scrollOffsetPx; // added to every note X to slide the content left

            if (ScrollMode)
            {
                // Fixed scale — notes may extend beyond the visible area.
                pxPerBeat = ScrollPxPerBeat;

                // Find the beat-start of the current note to pin it at TargetZoneX.
                double currentBeatStart = 0;
                if (CurrentNoteIndex < Notes.Count)
                {
                    var cur = Notes[CurrentNoteIndex];
                    if (cur.BeatPosition.HasValue)
                        currentBeatStart = cur.BeatPosition.Value;
                    else
                    {
                        // Fallback: sum durations up to the current index.
                        for (int k = 0; k < CurrentNoteIndex && k < Notes.Count; k++)
                            currentBeatStart += Notes[k].BeatDuration;
                    }
                    // Pin at the left edge of the note's slot (not the centre).
                    scrollOffsetPx = TargetZoneX - LeftMargin - (float)(currentBeatStart * pxPerBeat);
                }
                else
                {
                    // Session complete: all notes to the left.
                    double totalB = 0;
                    foreach (var n in Notes) totalB += n.BeatDuration;
                    scrollOffsetPx = TargetZoneX - LeftMargin - (float)(totalB * pxPerBeat);
                }
            }
            else
            {
                // Static mode: scale everything to fit the view width.
                double totalBeats;
                var lastNote = Notes.Count > 0 ? Notes[^1] : null;
                if (lastNote?.BeatPosition.HasValue == true)
                    totalBeats = lastNote.BeatPosition!.Value + lastNote.BeatDuration;
                else
                {
                    totalBeats = 0;
                    foreach (var n in Notes) totalBeats += n.BeatDuration;
                }
                float availPx = lineWidth - 8f;
                pxPerBeat = totalBeats > 0 ? (float)(availPx / totalBeats) : PixelsPerBeat;
                pxPerBeat = Math.Max(30f, pxPerBeat);
                scrollOffsetPx = 0f;
            }

            LastComputedPxPerBeat = pxPerBeat;

            // ── Draw staff lines (full width, from left edge) ─────────────────────
            canvas.StrokeColor = ink;
            canvas.StrokeSize  = 1.5f;
            for (int i = 0; i < 5; i++)
            {
                float y = staffTop + i * StaffLineSpacing;
                canvas.DrawLine(dirtyRect.X, y, LeftMargin + lineWidth, y);
            }

            // ── Target zone band (scroll mode only) ──────────────────────────────
            if (ScrollMode && CurrentNoteIndex < Notes.Count)
            {
                var curNote    = Notes[CurrentNoteIndex];
                float slotW    = (float)(curNote.BeatDuration * pxPerBeat);
                float zoneLeft = TargetZoneX;
                float zoneW    = slotW;

                // Solid highlight band behind the target note slot
                canvas.FillColor = Color.FromArgb("#33007BFF");
                canvas.FillRectangle(zoneLeft, staffTop - 8f, zoneW, staffBot - staffTop + 16f);

                // Bold left-edge marker on the exact slot start
                canvas.StrokeColor = Color.FromArgb("#007BFF");
                canvas.StrokeSize  = 3f;
                canvas.DrawLine(zoneLeft, staffTop - 10f, zoneLeft, staffBot + 10f);
                canvas.StrokeSize = 1f;
            }

            // ── Draw clef (always in the fixed left margin) ───────────────────────
            // Font 60, box positioned so the G-clef circle lands on the G4 staff line
            // (staffTop + 3*StaffLineSpacing).  The box starts above the staff to allow
            // the upper curl to render, and extends below for the bottom curl.
            canvas.SaveState();  //  2026.05.16 0840  
            canvas.FontColor = ink;
            canvas.FontSize  = 60f;
            float clefY = staffTop ;  //  2026.05.16 1035  - StaffLineSpacing * 1.5f;
            float clefH = staffBot - staffTop + StaffLineSpacing * 3.2f;
            canvas.DrawString("𝄞", dirtyRect.X + 2f, clefY, 54f, clefH,
                HorizontalAlignment.Left, VerticalAlignment.Top);
            canvas.RestoreState();

            // ── Draw key signature and time signature ─────────────────────────────
            float keySigEndX = DrawKeySignature(canvas, staffTop, staffMid, ink);
            DrawTimeSignature(canvas, staffTop, staffMid, ink, keySigEndX);

            // ── Compute shared totalBeats for bar lines ───────────────────────────
            double totalBeatsForBars;
            {
                var ln = Notes.Count > 0 ? Notes[^1] : null;
                if (ln?.BeatPosition.HasValue == true)
                    totalBeatsForBars = ln.BeatPosition!.Value + ln.BeatDuration;
                else
                {
                    totalBeatsForBars = 0;
                    foreach (var n in Notes) totalBeatsForBars += n.BeatDuration;
                }
            }

            // ── Draw measure bar lines (only when inside the visible area) ────────
            // Pre-compute X ranges of notes that are current or just played so we can
            // skip any bar line that would cut through such a note.
            var protectedRanges = new System.Collections.Generic.List<(float left, float right)>();
            {
                double bc = 0;
                for (int i = 0; i < Notes.Count; i++)
                {
                    var st = (NoteStates.Length > i) ? NoteStates[i] : V2NoteState.Pending;
                    if (st == V2NoteState.Current || st == V2NoteState.Correct || st == V2NoteState.Wrong)
                    {
                        double ba  = Notes[i].BeatPosition ?? bc;
                        float  sw  = (float)(Notes[i].BeatDuration * pxPerBeat);
                        float  nx2 = LeftMargin + scrollOffsetPx + (float)(ba * pxPerBeat) + sw * 0.5f;
                        // Guard only the notehead itself — previously NoteHeadRadius*6 was far too wide
                        // and blocked nearly every bar line that was near a played note.
                        protectedRanges.Add((nx2 - NoteHeadRadius * 1.5f, nx2 + NoteHeadRadius * 1.5f));
                    }
                    bc += Notes[i].BeatDuration;
                }
            }

            canvas.StrokeColor = ink;
            canvas.StrokeSize  = 2f;
            foreach (var barBeat in MeasureBarBeats)
            {
                float bx = LeftMargin + scrollOffsetPx + (float)(barBeat * pxPerBeat);
                if (bx < LeftMargin || bx > LeftMargin + lineWidth) continue;
                // Skip if this bar line falls inside any protected note zone.
                bool blocked = false;
                foreach (var (left, right) in protectedRanges)
                    if (bx >= left && bx <= right) { blocked = true; break; }
                if (blocked) continue;
                canvas.DrawLine(bx, staffTop - 2f, bx, staffBot + 2f);
            }

            // Final bar line (double) — only if visible
            float endX = LeftMargin + scrollOffsetPx + (float)(totalBeatsForBars * pxPerBeat);
            if (endX >= LeftMargin && endX <= LeftMargin + lineWidth)
            {
                canvas.StrokeSize = 2f;
                canvas.DrawLine(endX,       staffTop - 2f, endX,       staffBot + 2f);
                canvas.StrokeSize = 4f;
                canvas.DrawLine(endX + 4f,  staffTop - 2f, endX + 4f,  staffBot + 2f);
                canvas.StrokeSize = 1f;
            }

            // ── Clip drawing to the staff area so notes don't overrun ─────────────
            // (MAUI ICanvas does not expose SaveState/RestoreState in all renderers,
            //  so we skip notes that are outside the visible X range instead.)
            float clipLeft  = LeftMargin - NoteHeadRadius * 4f;
            float clipRight = LeftMargin + lineWidth + NoteHeadRadius * 4f;

            // ── Draw notes ────────────────────────────────────────────────────────
            // Pre-build an accidental history for courtesy-natural tracking.
            // Key = (letter, octave), Value = last Accidental seen for that pitch.
            // We scan ALL notes (including off-screen) so the history is correct
            // even when the visible window starts mid-sequence.
            var accidentalHistory = new Dictionary<(char, int), Accidental>();
            double beatCursor = 0;
            for (int i = 0; i < Notes.Count; i++)
            {
                var note      = Notes[i];
                double beatAnchor = note.BeatPosition ?? beatCursor;
                float slotWidth   = (float)(note.BeatDuration * pxPerBeat);
                // Centre the notehead in the middle of its slot.
                float nx = LeftMargin + scrollOffsetPx + (float)(beatAnchor * pxPerBeat) + slotWidth * 0.5f;
                var state = (NoteStates.Length > i) ? NoteStates[i] : V2NoteState.Pending;

                // Always update accidental history, even for clipped notes, so the
                // courtesy-natural context is accurate when notes scroll into view.
                if (!note.IsRest)
                    accidentalHistory[(note.Letter, note.Octave)] = note.Accidental;

                // Skip notes outside the visible strip to avoid clutter on left/right edges.
                if (nx < clipLeft || nx > clipRight)
                {
                    beatCursor += note.BeatDuration;
                    continue;
                }

                if (note.IsRest)
                {
                    DrawRest(canvas, note.Duration, nx, staffTop, staffMid, staffBot, ink, state);
                }
                else
                {
                    float ny = NoteY(note, staffTop, staffMid);
                    DrawNote(canvas, note.Duration, nx, ny, staffTop, staffBot, ink, state);
                    DrawLedgerLines(canvas, note, nx, staffTop, staffBot, ink);
                    DrawAccidental(canvas, note, nx, ny, ink, accidentalHistory);

                    // Note name display — controlled by V2NoteNameDisplay setting.
                    var nameDisplay = _session.V2NoteNameDisplay;
                    bool showName = nameDisplay == "All notes"
                        || (nameDisplay == "Current only" && state == V2NoteState.Current);
                    if (showName)
                        DrawOctaveName(canvas, note, nx, ny, staffTop, staffBot, ink);
                }

                beatCursor += note.BeatDuration;
            }

            // ── Feedback row ──────────────────────────────────────────────────────
            // Draw a small colored rectangle below each pitch note showing whether
            // it was played correctly (green), incorrectly (red), or is still pending.
            DrawFeedbackRow(canvas, staffBot, pxPerBeat, scrollOffsetPx, clipLeft, clipRight);
        }

        // ── Note geometry helpers ─────────────────────────────────────────────────

        /// <summary>
        /// Returns the X position immediately after the time signature — used as LeftMargin
        /// so notes never overlap the clef / key-sig / time-sig header.
        /// </summary>
        private float ComputeHeaderWidth()
        {
            const float clefWidth  = 54f;
            const float symSlot    = 14f;
            const float keySigGap  = 6f;
            const float timeSigW   = 24f + 8f;  // box width + right gap
            const float minMargin  = 8f;

            string key   = _session.Key;
            string scale = _session.SelectedScale;
            int accCount = _session.Tune == "Tuner" ? 0 : GetAccidentalCount(key, scale);

            float keySigEnd = clefWidth + accCount * symSlot + keySigGap;
            return keySigEnd + timeSigW + minMargin;
        }

        /// <summary>
        /// Draws sharps or flats in the standard treble-clef key-signature positions.
        /// Returns the X coordinate immediately after the last drawn symbol so the
        /// caller can place the time signature without overlap.
        /// Shown for all modes except Tuner.
        /// </summary>
        private float DrawKeySignature(ICanvas canvas, float staffTop, float staffMid, Color ink)
        {
            canvas.SaveState();   //  2026.05.16 0846  
            try                   //  2026.05.16 0847  
            {
                const float startX   = 54f;  // begins right after the treble clef
                const float symSlot  = 14f;  // horizontal slot per accidental symbol
                const float symH     = 60f;  //  2026.05.15 1814  30f;  // bounding-box height for each symbol
                const float symW     = 36f;  //  2026.05.15 1814  18f;  // bounding-box width for each symbol

                // Key signature is suppressed only in Tuner mode.
                if (_session.Tune == "Tuner") return startX;

                string key   = _session.Key;
                string scale = _session.SelectedScale;

                int accCount = GetAccidentalCount(key, scale);
                if (accCount == 0) return startX;

                bool useFlats = IsKeyFlat(key);
                string glyph  = useFlats ? "♭" : "♯";

                // Standard treble-clef diatonic-step offsets from staffMid (B4).
                // Positive = lower on staff.  Each step = StaffLineSpacing / 2.
                // Flats  order: Bb  Eb  Ab  Db  Gb  Cb  Fb
                //                B4  E5  A4  D5  G4  C5  F4
                int[] flatSteps  = {  0, -3,  1, -2,  2, -1,  3 };
                // Sharps order: F#  C#  G#  D#  A#  E#  B#
                //                F5  C5  G5  D5  A4  E5  B4
                int[] sharpSteps = { -4, -1, -5, -2,  1, -3,  0 };
                int[] steps = useFlats ? flatSteps : sharpSteps;
                canvas.FontColor = ink;
                canvas.FontSize  = useFlats ? 48f : 30f;  //  2026.05.15 1837   26f;  // larger glyphs, closer to real sheet music
                  //  2026.05.16 0837  all will be either sharps of flats
                float sigX = startX;
                for (int i = 0; i < Math.Min(accCount, steps.Length); i++)
                {
                    // Compute the vertical centre of this symbol on the staff,
                    // then offset up by half the bounding box so DrawString centres it.
                    float yCenter = staffMid + steps[i] * (StaffLineSpacing / 2f);
                    float yAdjust = useFlats ? 0.66f : 0.38f;
                    float yTop    = yCenter  - symH * yAdjust;  //  2026.05.15 1818   0.55f;  // optical centre of ♭/♯ glyph
                    canvas.DrawString(glyph, sigX, yTop, symW, symH,
                        HorizontalAlignment.Center, VerticalAlignment.Top);
                    sigX += symSlot;
                }           

                return sigX;  // X immediately after the last symbol
            }
            finally
            {
                canvas.RestoreState();
            }
        }

        /// <summary>
        /// Returns the number of accidentals (sharps or flats) for the key signature.
        /// </summary>
        private static int GetAccidentalCount(string key, string scale)
        {
            // Use circle-of-fifths distance for major keys;
            // relative major for minor/modal keys.
            string majorKey = scale switch
            {
                "Natural Minor" or "Aeolian" or "Harmonic Minor"
                    or "Melodic Minor" or "Jazz Melodic Minor" => RelativeMajorOf(key),
                _ => key
            };

            return majorKey switch
            {
                "C"  => 0,
                "G"  => 1, "D"  => 2, "A"  => 3, "E"  => 4, "B"  => 5, "F#" => 6, "C#" => 7,
                "F"  => 1, "Bb" => 2, "Eb" => 3, "Ab" => 4, "Db" => 5, "Gb" => 6, "Cb" => 7,
                _ => 0
            };
        }

        private static string RelativeMajorOf(string minorKey) => minorKey switch
        {
            "A" => "C", "E" => "G", "B" => "D", "F#" => "A", "C#" => "E", "G#" => "B", "D#" => "F#",
            "D" => "F", "G" => "Bb", "C" => "Eb", "F" => "Ab", "Bb" => "Db", "Eb" => "Gb",
            _ => minorKey
        };

        private static bool IsKeyFlat(string key)
            => key is "F" or "Bb" or "Eb" or "Ab" or "Db" or "Gb" or "Cb";

        /// <summary>
        /// Draws the time signature numerals (e.g. 4 over 4) on the staff.
        /// <paramref name="keySigEndX"/> is the X returned by <see cref="DrawKeySignature"/> so
        /// the numerals are placed immediately after the key signature without overlap.
        /// </summary>
        private void DrawTimeSignature(ICanvas canvas, float staffTop, float staffMid,
                                       Color ink, float keySigEndX)
        {
            canvas.SaveState();   //  2026.05.16 0846  
            try                   //  2026.05.16 0847  
            {
                string timeSig = _session.V2TimeSignature ?? "4/4";
                var parts = timeSig.Split('/');
                if (parts.Length != 2) return;

                // Horizontal centre of the two numerals — leave a small gap after the key sig.
                float tsX  = keySigEndX + 6f;
                float boxW = 24f;

                canvas.FontColor = ink;
                canvas.FontSize  = 22;
                canvas.Font      = Microsoft.Maui.Graphics.Font.DefaultBold;

                float halfH   = StaffLineSpacing * 2f;
                float topY    = staffTop  + (halfH - 22f) * 0.5f;
                float bottomY = staffMid  + (halfH - 22f) * 0.5f;

                canvas.DrawString(parts[0], tsX, topY,    boxW, 22f, HorizontalAlignment.Center, VerticalAlignment.Top);
                canvas.DrawString(parts[1], tsX, bottomY, boxW, 22f, HorizontalAlignment.Center, VerticalAlignment.Top);
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            }
            finally
            {
                canvas.RestoreState();
            }
        }

        /// <summary>
        /// Draws a row of small colored rectangles below the staff — one per pitch note —
        /// mirroring the feedback boxes in the v1 staff drawable.
        /// Green = correct, red = wrong, blue-outline = current target, transparent = pending.
        /// </summary>
        private void DrawFeedbackRow(ICanvas canvas, float staffBot, float pxPerBeat,
                                     float scrollOffsetPx, float clipLeft, float clipRight)
        {
            canvas.SaveState();   //  2026.05.16 0846  
            try                   //  2026.05.16 0847  
            {
                const float boxH   = 10f;
                const float rowGap = 8f;   // gap between staffBot and the top of the feedback row
                float rowY = staffBot + rowGap;

                int sessionIdx = 0;
                for (int i = 0; i < Notes.Count; i++)
                {
                    var note = Notes[i];
                    if (note.IsRest) continue;

                    double beatAnchor = note.BeatPosition ?? 0;
                    float slotWidth   = (float)(note.BeatDuration * pxPerBeat);
                    float nx = LeftMargin + scrollOffsetPx + (float)(beatAnchor * pxPerBeat) + slotWidth * 0.5f;

                    // Skip notes outside the visible strip
                    if (nx < clipLeft || nx > clipRight)
                    {
                        sessionIdx++;
                        continue;
                    }

                    float boxW = Math.Min(slotWidth * 0.88f, 48f);
                    float boxX = nx - boxW / 2f;

                    var state = (NoteStates.Length > i) ? NoteStates[i] : V2NoteState.Pending;

                    switch (state)
                    {
                        case V2NoteState.Correct:
                            canvas.FillColor   = Color.FromArgb("#BB22AA44");
                            canvas.StrokeColor = Color.FromArgb("#22AA44");
                            canvas.StrokeSize  = 1f;
                            canvas.FillRoundedRectangle(boxX, rowY, boxW, boxH, 3f);
                            canvas.DrawRoundedRectangle(boxX, rowY, boxW, boxH, 3f);
                            break;
                        case V2NoteState.Wrong:
                            canvas.FillColor   = Color.FromArgb("#BBCC2222");
                            canvas.StrokeColor = Color.FromArgb("#CC2222");
                            canvas.StrokeSize  = 1f;
                            canvas.FillRoundedRectangle(boxX, rowY, boxW, boxH, 3f);
                            canvas.DrawRoundedRectangle(boxX, rowY, boxW, boxH, 3f);
                            break;
                        case V2NoteState.Current:
                            // Outline only — shows the slot without covering correct/wrong state
                            canvas.FillColor   = Color.FromArgb("#44007BFF");
                            canvas.StrokeColor = Color.FromArgb("#007BFF");
                            canvas.StrokeSize  = 1.5f;
                            canvas.FillRoundedRectangle(boxX, rowY, boxW, boxH, 3f);
                            canvas.DrawRoundedRectangle(boxX, rowY, boxW, boxH, 3f);
                            break;
                        default: // Pending — faint outline only
                            canvas.StrokeColor = Color.FromArgb("#44888888");
                            canvas.StrokeSize  = 1f;
                            canvas.DrawRoundedRectangle(boxX, rowY, boxW, boxH, 3f);
                            break;
                    }

                    sessionIdx++;
                }
            }
            finally
            {
                canvas.RestoreState();
            }
        }

        /// <summary>Vertical center Y for the notehead given its pitch.</summary>
        private static float NoteY(GeneratedNote note, float staffTop, float staffMid)
        {
            // Map to half-steps from B4; B4 sits on staffMid (line 3).
            // Staff steps: each line/space is one diatonic step.
            // We use a chromatic approach: map letter+octave to diatonic position relative to B4.
            int steps = DiatonicStepsFromB4(note.Letter, note.Octave);
            // Positive steps = below B4 (lower pitch → further down on staff)
            return staffMid + steps * (StaffLineSpacing / 2f);
        }

        /// <summary>
        /// Returns how many diatonic half-steps (line/space units) a note is from B4.
        /// Positive = below B4 (visually lower on the staff).
        /// </summary>
        private static int DiatonicStepsFromB4(char letter, int octave)
        {
            // Diatonic pitch values within an octave (C=0..B=6)
            int noteVal = letter switch
            {
                'C' => 0, 'D' => 1, 'E' => 2, 'F' => 3,
                'G' => 4, 'A' => 5, 'B' => 6, _ => 0
            };
            // B4 = letter B, octave 4
            int b4Val = 6 + 4 * 7; // = 34
            int thisVal = noteVal + octave * 7;
            return b4Val - thisVal; // positive = lower pitch
        }

        private void DrawNote(ICanvas canvas, NoteDuration duration, float x, float y,
                              float staffTop, float staffBot, Color ink, V2NoteState state)
        {
            canvas.SaveState();   //  2026.05.16 0846
            try
            { 
                float r = NoteHeadRadius;

                // State-based highlight box and notehead color
                Color noteColor;
                switch (state)
                {
                    case V2NoteState.Current:
                        // Solid rounded rect behind the note head
                        canvas.FillColor   = Color.FromArgb("#44007BFF");
                        canvas.StrokeColor = Color.FromArgb("#007BFF");
                        canvas.StrokeSize  = 2f;
                        canvas.FillRoundedRectangle(x - r * 2.8f, y - r * 3.5f, r * 5.6f, r * 8f, 5f);
                        canvas.DrawRoundedRectangle(x - r * 2.8f, y - r * 3.5f, r * 5.6f, r * 8f, 5f);
                        noteColor = Color.FromArgb("#007BFF");
                        break;
                    case V2NoteState.Correct:
                        noteColor = Color.FromArgb("#22AA44");  // slightly softer green
                        break;
                    case V2NoteState.Wrong:
                        noteColor = Color.FromArgb("#CC2222");
                        break;
                    default: // Pending — slightly muted so current/done states stand out
                        noteColor = Color.FromRgba(ink.Red, ink.Green, ink.Blue, 0.55f);
                        break;
                }

                canvas.StrokeColor = noteColor;
                canvas.StrokeSize  = 2f;

                bool filled = duration != NoteDuration.Whole && duration != NoteDuration.Half;

                if (filled)
                {
                    canvas.FillColor = noteColor;
                    canvas.FillEllipse(x - r, y - r * 0.75f, r * 2f, r * 1.5f);
                }
                else
                {
                    canvas.StrokeColor = noteColor;
                    canvas.StrokeSize  = 2f;
                    canvas.DrawEllipse(x - r, y - r * 0.75f, r * 2f, r * 1.5f);
                }

                // Stem (all except whole note)
                if (duration != NoteDuration.Whole)
                {
                    bool stemUp = y > (staffTop + (staffBot - staffTop) * 0.5f);
                    float stemX = stemUp ? x + r : x - r;
                    float stemY = stemUp ? y - r * 0.75f : y + r * 0.75f;
                    float stemEnd = stemUp ? stemY - StemLength : stemY + StemLength;
                    canvas.StrokeColor = noteColor;
                    canvas.StrokeSize  = 2f;
                    canvas.DrawLine(stemX, stemY, stemX, stemEnd);

                    if (duration == NoteDuration.Eighth)
                    {
                        canvas.StrokeSize = 2f;
                        if (stemUp)
                        {
                            canvas.DrawLine(stemX, stemEnd, stemX + 12f, stemEnd + 10f);
                            canvas.DrawLine(stemX + 12f, stemEnd + 10f, stemX + 6f, stemEnd + 18f);
                        }
                        else
                        {
                            canvas.DrawLine(stemX, stemEnd, stemX + 12f, stemEnd - 10f);
                            canvas.DrawLine(stemX + 12f, stemEnd - 10f, stemX + 6f, stemEnd - 18f);
                        }
                    }

                    if (duration == NoteDuration.Sixteenth)
                    {
                        for (int f = 0; f < 2; f++)
                        {
                            float offset = f * (stemUp ? 10f : -10f);
                            if (stemUp)
                            {
                                canvas.DrawLine(stemX, stemEnd + offset, stemX + 12f, stemEnd + 10f + offset);
                                canvas.DrawLine(stemX + 12f, stemEnd + 10f + offset, stemX + 6f, stemEnd + 18f + offset);
                            }
                            else
                            {
                                canvas.DrawLine(stemX, stemEnd + offset, stemX + 12f, stemEnd - 10f + offset);
                                canvas.DrawLine(stemX + 12f, stemEnd - 10f + offset, stemX + 6f, stemEnd - 18f + offset);
                            }
                        }
                    }
                }
            }
            finally
            {
                canvas.RestoreState();
            }
        }

        private void DrawRest(ICanvas canvas, NoteDuration duration, float x,
                              float staffTop, float staffMid, float staffBot, Color ink, V2NoteState state)
        {
            canvas.SaveState();
            try
            {
            float r = NoteHeadRadius;

            if (state == V2NoteState.Current)
            {
                canvas.FillColor   = Color.FromArgb("#30007BFF");
                canvas.StrokeColor = Color.FromArgb("#007BFF");
                canvas.StrokeSize  = 1.5f;
                canvas.FillRoundedRectangle(x - r * 2f, staffMid - r * 3f, r * 4f, r * 6f, 4f);
            }

            Color restColor = state switch
            {
                V2NoteState.Correct => Colors.Green,
                V2NoteState.Wrong   => Colors.DarkRed,
                V2NoteState.Current => Color.FromArgb("#007BFF"),
                _ => ink
            };

            canvas.FillColor   = restColor;
            canvas.StrokeColor = restColor;
            canvas.StrokeSize  = 1.5f;

            switch (duration)
            {
                case NoteDuration.Whole:
                    // Whole rest: filled rectangle hanging from second line
                    canvas.FillRectangle(x - 8f, staffTop + StaffLineSpacing - 5f, 16f, 5f);
                    break;
                case NoteDuration.Half:
                    // Half rest: filled rectangle sitting on middle line
                    canvas.FillRectangle(x - 8f, staffMid, 16f, 5f);
                    break;
                case NoteDuration.Quarter:
                    // Quarter rest: squiggle approximated with lines
                    canvas.DrawLine(x,       staffMid - 10f, x + 4f,  staffMid - 6f);
                    canvas.DrawLine(x + 4f,  staffMid - 6f,  x - 4f,  staffMid - 2f);
                    canvas.DrawLine(x - 4f,  staffMid - 2f,  x + 4f,  staffMid + 2f);
                    canvas.DrawLine(x + 4f,  staffMid + 2f,  x,       staffMid + 6f);
                    break;
                case NoteDuration.Eighth:
                    // Eighth rest: small dot + hook
                    canvas.FillEllipse(x - 2f, staffMid - 2f, 4f, 4f);
                    canvas.DrawLine(x, staffMid - 2f, x + 6f, staffMid - 10f);
                    break;
                default:
                    // Sixteenth rest: small squiggle
                    canvas.DrawLine(x, staffMid + 4f, x + 4f, staffMid - 4f);
                    canvas.DrawLine(x + 4f, staffMid - 4f, x - 2f, staffMid - 10f);
                    break;
            }
            }
            finally
            {
                canvas.RestoreState();
            }
        }

        /// <summary>
        /// Computes the canvas height
        /// without clipping ledger lines or pitch labels above/below the staff.
        /// Call this after updating <see cref="Notes"/> to resize the GraphicsView.
        /// </summary>
        public float ComputeRequiredHeight()
        {
            float fixedTop = TopMargin + StaffLineSpacing * 2f;
            float fixedMid = fixedTop + StaffLineSpacing * 2f;

            // Determine extra push needed (same logic as Draw).
            float minNoteYFixed = fixedTop;
            float maxNoteY = fixedTop + StaffLineSpacing * 4f;  // staffBot when no notes below
            foreach (var note in Notes)
            {
                if (note.IsRest) continue;
                float ny = NoteY(note, fixedTop, fixedMid);
                float topEdge = ny - NoteHeadRadius - 18f;
                float botEdge = ny + NoteHeadRadius;
                if (topEdge < minNoteYFixed) minNoteYFixed = topEdge;
                if (botEdge > maxNoteY)       maxNoteY       = botEdge;
            }

            float extraPush = Math.Max(0f, TopMargin - minNoteYFixed);
            float staffTop  = fixedTop + extraPush;
            float staffBot  = staffTop + StaffLineSpacing * 4f;

            // Bottom: notes below the staff + feedback row (10px) + gap (8px) + bottom margin.
            float adjustedMaxY = maxNoteY + extraPush;
            float bottomEdge   = Math.Max(staffBot, adjustedMaxY);
            float total        = bottomEdge + 8f + 10f + BottomMargin;
            return Math.Max(120f, total);
        }

        private void DrawLedgerLines(ICanvas canvas, GeneratedNote note, float x,
                                     float staffTop, float staffBot, Color ink)
        {
            canvas.SaveState();
            try
            {
            float y = NoteY(note, staffTop, staffTop + (staffBot - staffTop) / 2f /* staffMid */);
            // Actually recompute staffMid:
            float staffMid = staffTop + StaffLineSpacing * 2f;
            float ny = NoteY(note, staffTop, staffMid);

            canvas.StrokeColor = ink;
            canvas.StrokeSize  = 1.5f;
            float ledgerHalfW  = NoteHeadRadius * 2.2f;

            // Above the staff (above E5, which is staffTop)
            if (ny < staffTop - 2f)
            {
                float cursor = staffTop - StaffLineSpacing;
                while (cursor >= ny - 2f)
                {
                    canvas.DrawLine(x - ledgerHalfW, cursor, x + ledgerHalfW, cursor);
                    cursor -= StaffLineSpacing;
                }
            }
            // Below the staff (below F4, which is staffBot)
            if (ny > staffBot + 2f)
            {
                float cursor = staffBot + StaffLineSpacing;
                while (cursor <= ny + 2f)
                {
                    canvas.DrawLine(x - ledgerHalfW, cursor, x + ledgerHalfW, cursor);
                    cursor += StaffLineSpacing;
                }
            }
            }
            finally
            {
                canvas.RestoreState();
            }
        }

        private void DrawAccidental(ICanvas canvas, GeneratedNote note, float x, float y, Color ink,
            Dictionary<(char, int), Accidental>? history = null)
        {
            canvas.SaveState();
            try
            {
                var effectiveAcc = note.Accidental;

                // Courtesy natural: the generator assigned Accidental.None to this note but an
                // earlier note on the same (letter, octave) was sharp or flat.  Show ♮ so the
                // player is not confused by the implicit cancellation.
                if (effectiveAcc == Accidental.None && history != null)
                {
                    if (history.TryGetValue((note.Letter, note.Octave), out var prev)
                        && (prev == Accidental.Sharp || prev == Accidental.Flat
                            || prev == Accidental.DoubleSharp || prev == Accidental.DoubleFlat))
                    {
                        effectiveAcc = Accidental.Natural;
                    }
                }

                // Update history AFTER the courtesy check so the current note's own accidental
                // is recorded for subsequent notes.
                if (history != null)
                    history[(note.Letter, note.Octave)] = effectiveAcc;

                if (effectiveAcc == Accidental.None) return;

                // Suppress if already implied by the key signature.
                if (IsAccidentalInKeySig(note)) return;

                string glyph = effectiveAcc switch
                {
                    Accidental.Sharp       => "♯",
                    Accidental.Flat        => "♭",
                    Accidental.Natural     => "♮",
                    Accidental.DoubleSharp => "𝄪",
                    Accidental.DoubleFlat  => "𝄫",
                    _ => ""
                };
                if (string.IsNullOrEmpty(glyph)) return;

                // Use the same metrics as DrawKeySignature so body accidentals
                // are identical in size and optical vertical alignment.
                bool isFlat = effectiveAcc == Accidental.Flat || effectiveAcc == Accidental.DoubleFlat;
                const float symH     = 60f;
                const float symW     = 36f;
                const float rightGap = 2f;   // gap between accidental right edge and notehead left
                float fontSize = isFlat ? 48f : 30f;
                float yAdjust  = isFlat ? 0.66f : 0.38f;

                float boxLeft = x + NoteHeadRadius - rightGap - symW;  //  2026.05.16 1106   - NoteHeadRadius - rightGap - symW;
                float yTop    = y - symH * yAdjust;

                canvas.FontColor = ink;
                canvas.FontSize  = fontSize;
                canvas.DrawString(glyph, boxLeft, yTop, symW, symH,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
            }
            finally
            {
                canvas.RestoreState();
            }
        }

        /// <summary>
        /// Returns true when the note's accidental
        /// current key signature, so it should not be re-drawn in the note body.
        /// </summary>
        private bool IsAccidentalInKeySig(GeneratedNote note)
        {
            if (note.Accidental == Accidental.None) return false;
            // Natural signs are never implied by the key signature — they contradict it.
            if (note.Accidental == Accidental.Natural) return false;

            string key   = _session.Key;
            string scale = _session.SelectedScale;
            int accCount = GetAccidentalCount(key, scale);
            if (accCount == 0) return false;

            bool useFlats = IsKeyFlat(key);

            // Flats:  Bb Eb Ab Db Gb Cb Fb
            char[] flatLetters  = { 'B', 'E', 'A', 'D', 'G', 'C', 'F' };
            // Sharps: F# C# G# D# A# E# B#
            char[] sharpLetters = { 'F', 'C', 'G', 'D', 'A', 'E', 'B' };

            bool typeMatch = useFlats
                ? note.Accidental == Accidental.Flat
                : note.Accidental == Accidental.Sharp;

            if (!typeMatch) return false;

            char[] keySigLetters = useFlats ? flatLetters : sharpLetters;
            for (int i = 0; i < Math.Min(accCount, keySigLetters.Length); i++)
                if (keySigLetters[i] == note.Letter) return true;

            return false;
        }

        private void DrawOctaveName(ICanvas canvas, GeneratedNote note, float x, float ny,
                                    float staffTop, float staffBot, Color ink)
        {
            canvas.SaveState();
            try
            {
            // Draw note name below/above the note head as a label for readability
            canvas.FontColor = ink;
            canvas.FontSize  = 11;
            float labelY = ny > (staffTop + staffBot) / 2f
                ? ny + NoteHeadRadius + 5f    // note is low: label below
                : ny - NoteHeadRadius - 15f;  // note is high: label above
            canvas.DrawString(note.SpelledName, x - 14f, labelY, 28f, 14f, HorizontalAlignment.Center, VerticalAlignment.Top);
            }
            finally
            {
                canvas.RestoreState();
            }
        }
    }
}
