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
        private const float StaffLineSpacing  = 10f;  // pixels between adjacent staff lines
        private const float NoteHeadRadius    = 5f;
        private const float StemLength        = 30f;
        private const float LeftMargin        = 48f;  // room for clef symbol
        private const float RightMargin       = 16f;
        private const float TopMargin         = 24f;
        private const float BottomMargin      = 24f;
        private const float PixelsPerBeat     = 50f;  // base px/beat for static (fit-all) mode

        // Scrolling mode: fixed px per beat so notes extend beyond the view width.
        // 60 px/beat gives a quarter note a comfortable 60 px slot on a phone.
        private const float ScrollPxPerBeat   = 60f;

        // The canvas X at which the current note is always pinned in scroll mode.
        // Sits at LeftMargin + one extra beat-slot so there is a little look-ahead to the left.
        private const float TargetZoneX       = LeftMargin + ScrollPxPerBeat;

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

            // ── Staff geometry ────────────────────────────────────────────────────
            float staffTop = TopMargin + StaffLineSpacing * 2f;  // line 1 (E5)
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

            // ── Draw staff lines (full width, always visible) ─────────────────────
            canvas.StrokeColor = ink;
            canvas.StrokeSize  = 1f;
            for (int i = 0; i < 5; i++)
            {
                float y = staffTop + i * StaffLineSpacing;
                canvas.DrawLine(LeftMargin, y, LeftMargin + lineWidth, y);
            }

            // ── Target zone band (scroll mode only) ──────────────────────────────
            if (ScrollMode && CurrentNoteIndex < Notes.Count)
            {
                var curNote    = Notes[CurrentNoteIndex];
                float slotW    = (float)(curNote.BeatDuration * pxPerBeat);
                float zoneLeft = TargetZoneX;
                float zoneW    = slotW;

                // Soft highlight behind the target note slot
                canvas.FillColor = Color.FromArgb("#18007BFF");
                canvas.FillRectangle(zoneLeft, staffTop - 4f, zoneW, staffBot - staffTop + 8f);

                // Thin left edge line marking the exact start of the target slot
                canvas.StrokeColor = Color.FromArgb("#60007BFF");
                canvas.StrokeSize  = 1.5f;
                canvas.DrawLine(zoneLeft, staffTop - 6f, zoneLeft, staffBot + 6f);
            }

            // ── Draw clef (always in the fixed left margin) ───────────────────────
            canvas.FontColor = ink;
            canvas.FontSize  = 28;
            canvas.DrawString("𝄞", dirtyRect.X + 4, staffTop - 4f, 36f,
                staffBot - staffTop + 16f, HorizontalAlignment.Left, VerticalAlignment.Top);

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
            canvas.StrokeColor = ink;
            canvas.StrokeSize  = 1.5f;
            foreach (var barBeat in MeasureBarBeats)
            {
                float bx = LeftMargin + scrollOffsetPx + (float)(barBeat * pxPerBeat);
                if (bx < LeftMargin || bx > LeftMargin + lineWidth) continue;
                canvas.DrawLine(bx, staffTop, bx, staffBot);
            }

            // Final bar line (double) — only if visible
            float endX = LeftMargin + scrollOffsetPx + (float)(totalBeatsForBars * pxPerBeat);
            if (endX >= LeftMargin && endX <= LeftMargin + lineWidth)
            {
                canvas.StrokeSize = 1.5f;
                canvas.DrawLine(endX,       staffTop, endX,       staffBot);
                canvas.StrokeSize = 3f;
                canvas.DrawLine(endX + 3f,  staffTop, endX + 3f,  staffBot);
                canvas.StrokeSize = 1f;
            }

            // ── Clip drawing to the staff area so notes don't overrun ─────────────
            // (MAUI ICanvas does not expose SaveState/RestoreState in all renderers,
            //  so we skip notes that are outside the visible X range instead.)
            float clipLeft  = LeftMargin - NoteHeadRadius * 4f;
            float clipRight = LeftMargin + lineWidth + NoteHeadRadius * 4f;

            // ── Draw notes ────────────────────────────────────────────────────────
            double beatCursor = 0;
            for (int i = 0; i < Notes.Count; i++)
            {
                var note      = Notes[i];
                double beatAnchor = note.BeatPosition ?? beatCursor;
                float slotWidth   = (float)(note.BeatDuration * pxPerBeat);
                // Centre the notehead in the middle of its slot.
                float nx = LeftMargin + scrollOffsetPx + (float)(beatAnchor * pxPerBeat) + slotWidth * 0.5f;
                var state = (NoteStates.Length > i) ? NoteStates[i] : V2NoteState.Pending;

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
                    DrawAccidental(canvas, note, nx, ny, ink);
                    DrawOctaveName(canvas, note, nx, ny, staffTop, staffBot, ink);
                }

                beatCursor += note.BeatDuration;
            }
        }

        // ── Note geometry helpers ─────────────────────────────────────────────────

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
            float r = NoteHeadRadius;

            // State-based highlight box and notehead color
            Color noteColor;
            switch (state)
            {
                case V2NoteState.Current:
                    canvas.FillColor   = Color.FromArgb("#30007BFF");
                    canvas.StrokeColor = Color.FromArgb("#007BFF");
                    canvas.StrokeSize  = 1.5f;
                    canvas.FillRoundedRectangle(x - r * 2.5f, y - r * 3f, r * 5f, r * 7f, 4f);
                    canvas.DrawRoundedRectangle(x - r * 2.5f, y - r * 3f, r * 5f, r * 7f, 4f);
                    noteColor = Color.FromArgb("#007BFF");
                    break;
                case V2NoteState.Correct:
                    noteColor = Colors.Green;
                    break;
                case V2NoteState.Wrong:
                    noteColor = Colors.DarkRed;
                    break;
                default: // Pending
                    noteColor = ink;
                    break;
            }

            canvas.StrokeColor = noteColor;
            canvas.StrokeSize  = 1.5f;

            bool filled = duration != NoteDuration.Whole && duration != NoteDuration.Half;

            if (filled)
            {
                canvas.FillColor = noteColor;
                canvas.FillEllipse(x - r, y - r * 0.7f, r * 2f, r * 1.4f);
            }
            else
            {
                canvas.StrokeColor = noteColor;
                canvas.DrawEllipse(x - r, y - r * 0.7f, r * 2f, r * 1.4f);
            }

            // Stem (all except whole note)
            if (duration != NoteDuration.Whole)
            {
                bool stemUp = y > (staffTop + (staffBot - staffTop) * 0.5f);
                float stemX = stemUp ? x + r : x - r;
                float stemY = stemUp ? y - r * 0.7f : y + r * 0.7f;
                float stemEnd = stemUp ? stemY - StemLength : stemY + StemLength;
                canvas.StrokeColor = noteColor;
                canvas.StrokeSize  = 1.5f;
                canvas.DrawLine(stemX, stemY, stemX, stemEnd);

                if (duration == NoteDuration.Eighth)
                {
                    canvas.StrokeSize = 1.5f;
                    if (stemUp)
                    {
                        canvas.DrawLine(stemX, stemEnd, stemX + 10f, stemEnd + 8f);
                        canvas.DrawLine(stemX + 10f, stemEnd + 8f, stemX + 5f, stemEnd + 14f);
                    }
                    else
                    {
                        canvas.DrawLine(stemX, stemEnd, stemX + 10f, stemEnd - 8f);
                        canvas.DrawLine(stemX + 10f, stemEnd - 8f, stemX + 5f, stemEnd - 14f);
                    }
                }

                if (duration == NoteDuration.Sixteenth)
                {
                    for (int f = 0; f < 2; f++)
                    {
                        float offset = f * (stemUp ? 8f : -8f);
                        if (stemUp)
                        {
                            canvas.DrawLine(stemX, stemEnd + offset, stemX + 10f, stemEnd + 8f + offset);
                            canvas.DrawLine(stemX + 10f, stemEnd + 8f + offset, stemX + 5f, stemEnd + 14f + offset);
                        }
                        else
                        {
                            canvas.DrawLine(stemX, stemEnd + offset, stemX + 10f, stemEnd - 8f + offset);
                            canvas.DrawLine(stemX + 10f, stemEnd - 8f + offset, stemX + 5f, stemEnd - 14f + offset);
                        }
                    }
                }
            }
        }

        private void DrawRest(ICanvas canvas, NoteDuration duration, float x,
                              float staffTop, float staffMid, float staffBot, Color ink, V2NoteState state)
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

        private void DrawLedgerLines(ICanvas canvas, GeneratedNote note, float x,
                                     float staffTop, float staffBot, Color ink)
        {
            float y = NoteY(note, staffTop, staffTop + (staffBot - staffTop) / 2f /* staffMid */);
            // Actually recompute staffMid:
            float staffMid = staffTop + StaffLineSpacing * 2f;
            float ny = NoteY(note, staffTop, staffMid);

            canvas.StrokeColor = ink;
            canvas.StrokeSize  = 1f;
            float ledgerHalfW  = NoteHeadRadius * 2f;

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

        private void DrawAccidental(ICanvas canvas, GeneratedNote note, float x, float y, Color ink)
        {
            if (note.Accidental == Accidental.None) return;
            string glyph = note.Accidental switch
            {
                Accidental.Sharp      => "♯",
                Accidental.Flat       => "♭",
                Accidental.DoubleSharp => "𝄪",
                Accidental.DoubleFlat  => "𝄫",
                _ => ""
            };
            if (string.IsNullOrEmpty(glyph)) return;
            canvas.FontColor = ink;
            canvas.FontSize  = 12;
            canvas.DrawString(glyph, x - 18f, y - 8f, HorizontalAlignment.Left);
        }

        private void DrawOctaveName(ICanvas canvas, GeneratedNote note, float x, float ny,
                                    float staffTop, float staffBot, Color ink)
        {
            // Draw note name below/above the note head as a small label for readability
            canvas.FontColor = ink;
            canvas.FontSize  = 8;
            float labelY = ny > (staffTop + staffBot) / 2f
                ? ny + NoteHeadRadius + 4f   // note is low: label below
                : ny - NoteHeadRadius - 12f; // note is high: label above
            canvas.DrawString(note.SpelledName, x - 12f, labelY, 24f, 12f, HorizontalAlignment.Center, VerticalAlignment.Top);
        }
    }
}
