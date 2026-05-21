using Microsoft.Maui.Graphics;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Drawables
{
    /// <summary>
    /// V3 endless two-staff drawable.
    /// Renders an upper and a lower treble staff inside a single <see cref="ICanvas"/>.
    /// Reading order follows standard sheet music: upper staff first, then lower staff.
    ///
    /// <para><b>Scroll behaviour:</b> the active staff (the one being played) scrolls so
    /// the current note is pinned at <c>TargetZoneX</c>.  The inactive staff shows its
    /// notes statically (all visible at once).</para>
    ///
    /// <para><b>Transitions / fade:</b>  when new notes are loaded onto a staff while the
    /// player is on the other staff, set the corresponding alpha array to 0 and animate
    /// it toward 1 from the page layer.  The drawable reads <see cref="UpperAlpha"/> and
    /// <see cref="LowerAlpha"/> and applies them uniformly to every note on that staff.</para>
    ///
    /// <para><b>Feedback:</b> no feedback row.  Instead, noteheads are coloured:
    /// blue = current target, red = one or more wrong attempts (still pending),
    /// green = correct, muted = pending future.</para>
    /// </summary>
    public class V3StaffDrawable : IDrawable
    {
        // ── Dependencies ──────────────────────────────────────────────────────────
        private readonly NoteSessionService _session;
        private readonly ThemeService _theme;

        // ── Staff data ────────────────────────────────────────────────────────────

        /// <summary>Notes displayed on the upper staff (played first).</summary>
        public List<GeneratedNote> UpperNotes { get; set; } = new();

        /// <summary>Notes displayed on the lower staff (played after upper).</summary>
        public List<GeneratedNote> LowerNotes { get; set; } = new();

        /// <summary>Measure bar beat-positions for the upper staff.</summary>
        public List<double> UpperBarBeats { get; set; } = new();

        /// <summary>Measure bar beat-positions for the lower staff.</summary>
        public List<double> LowerBarBeats { get; set; } = new();

        /// <summary>
        /// Visual state for every note on the upper staff, parallel to <see cref="UpperNotes"/>.
        /// </summary>
        public V2NoteState[] UpperNoteStates { get; set; } = Array.Empty<V2NoteState>();

        /// <summary>
        /// Visual state for every note on the lower staff, parallel to <see cref="LowerNotes"/>.
        /// </summary>
        public V2NoteState[] LowerNoteStates { get; set; } = Array.Empty<V2NoteState>();

        /// <summary>
        /// When <c>true</c> the player is currently on the upper staff.
        /// When <c>false</c> the player is on the lower staff.
        /// </summary>
        public bool IsUpperActive { get; set; } = true;

        /// <summary>Current-note index within the <b>active</b> staff's note list.</summary>
        public int ActiveNoteIndex { get; set; }

        /// <summary>
        /// Global opacity for every notehead on the upper staff (0 = transparent, 1 = opaque).
        /// Animate from 0→1 after loading new notes onto the upper staff.
        /// </summary>
        public float UpperAlpha { get; set; } = 1f;

        /// <summary>
        /// Global opacity for every notehead on the lower staff (0 = transparent, 1 = opaque).
        /// Animate from 0→1 after loading new notes onto the lower staff.
        /// </summary>
        public float LowerAlpha { get; set; } = 1f;

        /// <summary>
        /// When <c>true</c> a single end barline is drawn at the end of the upper staff
        /// (used when the scale ascending portion fills the upper staff and the
        /// descending portion continues on the lower staff).
        /// </summary>
        public bool UpperHasEndBar { get; set; } = false;

        // ── Layout constants ──────────────────────────────────────────────────────
        private const float StaffLineSpacing = 12.6f;
        private const float NoteHeadRadius   = 6.3f;
        private const float StemLength       = 36f;
        private const float TopMargin        = 18f;
        private const float BottomMargin     = 18f;
        private const float StaffGap         = 32.4f;  // pixels between bottom of upper staff and top of lower staff
        private const float ScrollPxPerBeat  = 48f;
        private const float RightMargin      = 16f;

        private float _leftMargin = 115f;
        private float LeftMargin => _leftMargin;

        // ── Cached layout output ──────────────────────────────────────────────────

        /// <summary>Pixels-per-beat as computed during the most recent <see cref="Draw"/> call.</summary>
        public float LastComputedPxPerBeat { get; private set; }

        // ── Constructor ───────────────────────────────────────────────────────────
        public V3StaffDrawable(NoteSessionService session, ThemeService theme)
        {
            _session = session;
            _theme   = theme;
        }

        // ── IDrawable ─────────────────────────────────────────────────────────────
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var bg  = _theme.PanelBackgroundColor;
            var ink = _theme.ContrastingTextColor;

            canvas.FillColor = bg;
            canvas.FillRectangle(dirtyRect);

            if (UpperNotes.Count == 0 && LowerNotes.Count == 0)
            {
                canvas.FontColor = ink;
                canvas.FontSize  = 14;
                canvas.DrawString("V3 Mode — no notes generated yet",
                    dirtyRect.X + 8, dirtyRect.Y + dirtyRect.Height / 2f,
                    HorizontalAlignment.Left);
                return;
            }

            _leftMargin = ComputeHeaderWidth();

            // ── Upper staff geometry ──────────────────────────────────────────────
            float upperTop = TopMargin + StaffLineSpacing * 2f;
            AdjustForHighNotes(UpperNotes, ref upperTop);
            float upperMid = upperTop + StaffLineSpacing * 2f;
            float upperBot = upperTop + StaffLineSpacing * 4f;

            // ── Lower staff geometry ──────────────────────────────────────────────
            float lowerTop = upperBot + StaffGap + StaffLineSpacing * 2f;
            AdjustForHighNotes(LowerNotes, ref lowerTop, upperBot + StaffGap);
            float lowerMid = lowerTop + StaffLineSpacing * 2f;
            float lowerBot = lowerTop + StaffLineSpacing * 4f;

            // ── Compute px-per-beat ───────────────────────────────────────────────
            // Scale notes to fill the available staff width exactly.  Each staff is
            // laid out from its own first note, so spans must be measured relative to
            // that start beat — not as absolute beat positions — otherwise a lower staff
            // that begins at beat 16 would report a span of 32 and halve pxPerBeat.
            float lineWidth0 = dirtyRect.Width - _leftMargin - RightMargin;
            float pxPerBeat  = ScrollPxPerBeat;
            if (UpperNotes.Count > 0 || LowerNotes.Count > 0)
            {
                double upperStart = UpperNotes.Count > 0 ? (UpperNotes[0].BeatPosition ?? 0.0) : 0.0;
                double upperSpan  = UpperNotes.Count > 0
                    ? (UpperNotes[^1].BeatPosition ?? 0.0) + UpperNotes[^1].BeatDuration - upperStart
                    : 0.0;

                double lowerStart = LowerNotes.Count > 0 ? (LowerNotes[0].BeatPosition ?? 0.0) : 0.0;
                double lowerSpan  = LowerNotes.Count > 0
                    ? (LowerNotes[^1].BeatPosition ?? 0.0) + LowerNotes[^1].BeatDuration - lowerStart
                    : 0.0;

                double maxSpan = Math.Max(upperSpan, lowerSpan);
                if (maxSpan > 0.0)
                {
                    float fitted = (lineWidth0 - NoteHeadRadius * 2f) / (float)maxSpan;
                    pxPerBeat = fitted;
                }
            }
            LastComputedPxPerBeat = pxPerBeat;

            // ── Static layout: no scrolling ──────────────────────────────────────
            float upperScroll = 0f;
            double lowerStartBeat = LowerNotes.Count > 0 ? (LowerNotes[0].BeatPosition ?? 0.0) : 0.0;
            float lowerScroll = -(float)(lowerStartBeat * pxPerBeat);

            // ── Draw both staffs ──────────────────────────────────────────────────
            DrawStaff(canvas, dirtyRect, ink, upperTop, upperMid, upperBot,
                      UpperNotes, UpperBarBeats, UpperNoteStates, upperScroll, pxPerBeat,
                      UpperAlpha, IsUpperActive, IsUpperActive ? ActiveNoteIndex : -1,
                      isFinalStaff: LowerNotes.Count == 0, hasEndSingleBar: UpperHasEndBar && LowerNotes.Count > 0);

            DrawStaff(canvas, dirtyRect, ink, lowerTop, lowerMid, lowerBot,
                      LowerNotes, LowerBarBeats, LowerNoteStates, lowerScroll, pxPerBeat,
                      LowerAlpha, !IsUpperActive, !IsUpperActive ? ActiveNoteIndex : -1,
                      isFinalStaff: true);
        }

        // ── Per-staff rendering ───────────────────────────────────────────────────

        private void DrawStaff(
            ICanvas canvas, RectF dirtyRect, Color ink,
            float staffTop, float staffMid, float staffBot,
            List<GeneratedNote> notes, List<double> barBeats, V2NoteState[] states,
            float scrollOffsetPx, float pxPerBeat,
            float alpha,
            bool isActive, int currentIdx, bool isFinalStaff = false, bool hasEndSingleBar = false)
        {
            float lineWidth = dirtyRect.Width - LeftMargin - RightMargin;
            float clipLeft  = LeftMargin - NoteHeadRadius * 4f;
            float clipRight = LeftMargin + lineWidth + NoteHeadRadius * 4f;

            // Staff lines
            canvas.StrokeColor = ink;
            canvas.StrokeSize  = 1.5f;
            for (int i = 0; i < 5; i++)
            {
                float y = staffTop + i * StaffLineSpacing;
                canvas.DrawLine(dirtyRect.X, y, LeftMargin + lineWidth, y);
            }


            // Clef
            canvas.SaveState();
            canvas.FontColor = ink;
            canvas.FontSize  = 60f;
            float clefH = staffBot - staffTop + StaffLineSpacing * 3.2f;
            canvas.DrawString("𝄞", dirtyRect.X + 2f, staffTop, 54f, clefH,
                HorizontalAlignment.Left, VerticalAlignment.Top);
            canvas.RestoreState();

            // Key sig + time sig (only on active / upper staff to reduce clutter on inactive)
            float keySigEndX = DrawKeySignature(canvas, staffTop, staffMid, ink);
            DrawTimeSignature(canvas, staffTop, staffMid, ink, keySigEndX);

            // Measure bar lines
            canvas.StrokeColor = ink;
            canvas.StrokeSize  = 2f;
            foreach (var barBeat in barBeats)
            {
                float bx = LeftMargin + scrollOffsetPx + (float)(barBeat * pxPerBeat);
                if (bx < LeftMargin || bx > LeftMargin + lineWidth) continue;
                canvas.DrawLine(bx, staffTop - 2f, bx, staffBot + 2f);
            }

            // Single end bar — drawn at the end of the upper staff when a lower staff follows
            // (ascending half ends here; descending half continues below).
            if (hasEndSingleBar && notes.Count > 0)
            {
                var last = notes[notes.Count - 1];
                double lastEndBeat = (last.BeatPosition ?? 0.0) + last.BeatDuration;
                float endX = LeftMargin + scrollOffsetPx + (float)(lastEndBeat * pxPerBeat);
                if (endX >= LeftMargin && endX <= LeftMargin + lineWidth)
                {
                    canvas.StrokeColor = ink;
                    canvas.StrokeSize  = 2f;
                    canvas.DrawLine(endX, staffTop - 2f, endX, staffBot + 2f);
                    canvas.StrokeSize  = 1f;
                }
            }

            // Final double bar — only on the last staff, and only when the end falls within the visible width.
            if (isFinalStaff && notes.Count > 0)
            {
                var last = notes[notes.Count - 1];
                double lastEndBeat = (last.BeatPosition ?? 0.0) + last.BeatDuration;
                float endX = LeftMargin + scrollOffsetPx + (float)(lastEndBeat * pxPerBeat);
                if (endX >= LeftMargin && endX <= LeftMargin + lineWidth)
                {
                    canvas.StrokeSize = 2f;
                    canvas.DrawLine(endX,      staffTop - 2f, endX,      staffBot + 2f);
                    canvas.StrokeSize = 4f;
                    canvas.DrawLine(endX + 4f, staffTop - 2f, endX + 4f, staffBot + 2f);
                    canvas.StrokeSize = 1f;
                }
            }

            // Notes
            // barCancelledAccidentals: pitch-classes whose key-sig accidental was cancelled
            // (overridden) within the current bar. Cleared at every bar line.
            // When a key-sig note appears later in the same bar, its accidental must be
            // shown explicitly (not suppressed) because the cancellation is still in effect.
            var accHistory = new Dictionary<(char, int), Accidental>();
            var barCancelledAccidentals = new HashSet<(char, int)>();
            int barBeatIdx = 0; // index into sorted barBeats for bar-boundary detection
            var sortedBarBeats = barBeats.OrderBy(b => b).ToList();
            double beatCursor = 0;
            for (int i = 0; i < notes.Count; i++)
            {
                var note        = notes[i];
                double beatAnchor = note.BeatPosition ?? beatCursor;

                // Advance past any bar lines that occur at or before this note's beat position.
                // Each crossing resets the within-bar cancelled-accidental tracking.
                while (barBeatIdx < sortedBarBeats.Count && sortedBarBeats[barBeatIdx] <= beatAnchor + 1e-9)
                {
                    barCancelledAccidentals.Clear();
                    barBeatIdx++;
                }

                float slotWidth   = (float)(note.BeatDuration * pxPerBeat);
                float nx          = LeftMargin + scrollOffsetPx + (float)(beatAnchor * pxPerBeat) + slotWidth * 0.5f;
                var state         = (states.Length > i) ? states[i] : V2NoteState.Pending;

                if (!note.IsRest)
                {
                    var key = (note.Letter, note.Octave);
                    // If this note's accidental differs from what the key signature implies for
                    // this pitch-class, record the cancellation so later notes in the same bar
                    // know they must show the key-sig accidental explicitly.
                    if (IsAccidentalInKeySig(note))
                    {
                        // Note matches key sig — no longer cancelled in this bar.
                        barCancelledAccidentals.Remove(key);
                    }
                    else if (IsNoteInKeySig(note))
                    {
                        // This pitch-class is governed by the key sig, but the current note
                        // deviates (e.g. natural cancelling a key-sig sharp/flat, or a different
                        // accidental). Record the cancellation so later notes in the same bar
                        // that return to the key-sig accidental must show it explicitly.
                        barCancelledAccidentals.Add(key);
                    }
                    accHistory[key] = note.Accidental;
                }

                if (nx < clipLeft || nx > clipRight) { beatCursor += note.BeatDuration; continue; }

                // Apply fade alpha to note color opacity
                byte fadeAlpha = (byte)Math.Clamp((int)(alpha * 255), 0, 255);

                if (note.IsRest)
                {
                    DrawRest(canvas, note.Duration, nx, staffTop, staffMid, staffBot, ink, state, fadeAlpha);
                }
                else
                {
                    float ny = NoteY(note, staffTop, staffMid);
                    DrawNote(canvas, note.Duration, nx, ny, staffTop, staffBot, ink, state, fadeAlpha);
                    DrawLedgerLines(canvas, note, nx, staffTop, staffBot, ink, fadeAlpha);
                    DrawAccidental(canvas, note, nx, ny, ink, accHistory, barCancelledAccidentals, fadeAlpha);

                    var nameDisplay = _session.V2NoteNameDisplay;
                    bool showName = nameDisplay == "All notes"
                        || (nameDisplay == "Current only" && state == V2NoteState.Current);
                    if (showName)
                        DrawNoteName(canvas, note, nx, ny, staffTop, staffBot, ink, fadeAlpha);
                }

                beatCursor += note.BeatDuration;
            }
        }

        private static double SumBeats(List<GeneratedNote> notes, int from, int to)
        {
            double s = 0;
            for (int i = from; i < Math.Min(to, notes.Count); i++)
                s += notes[i].BeatDuration;
            return s;
        }

        // ── Layout helpers ────────────────────────────────────────────────────────

        private static void AdjustForHighNotes(List<GeneratedNote> notes, ref float staffTop, float minTop = TopMargin)
        {
            float fixedTop = staffTop;
            float minNoteY = fixedTop;
            float fixedMid = fixedTop + StaffLineSpacing * 2f;
            foreach (var n in notes)
            {
                if (n.IsRest) continue;
                float ny = NoteY(n, fixedTop, fixedMid);
                float topEdge = ny - NoteHeadRadius;
                if (topEdge < minNoteY) minNoteY = topEdge;
            }
            float push = Math.Max(0f, minTop - minNoteY);
            staffTop += push;
        }

        /// <summary>
        /// Total canvas height required to fit both staffs without clipping.
        /// Call this after updating note lists to resize the <c>GraphicsView</c>.
        /// </summary>
        public float ComputeRequiredHeight()
        {
            float upperTop = TopMargin + StaffLineSpacing * 2f;
            AdjustForHighNotes(UpperNotes, ref upperTop);
            float upperBot = upperTop + StaffLineSpacing * 4f;

            float lowerTop = upperBot + StaffGap + StaffLineSpacing * 2f;
            AdjustForHighNotes(LowerNotes, ref lowerTop, upperBot + StaffGap);
            float lowerBot = lowerTop + StaffLineSpacing * 4f;

            // Notes and ledger lines below the lower staff
            float maxY = lowerBot;
            float lowerMid = lowerTop + StaffLineSpacing * 2f;
            foreach (var n in LowerNotes)
            {
                if (n.IsRest) continue;
                float ny = NoteY(n, lowerTop, lowerMid);
                // Ledger lines extend from staffBot+StaffLineSpacing in steps down to ny
                float bottomEdge = ny > lowerBot + 2f
                    ? ny + NoteHeadRadius
                    : ny + NoteHeadRadius;
                if (bottomEdge > maxY) maxY = bottomEdge;
            }

            return maxY + BottomMargin;
        }

        // ── Header metrics ────────────────────────────────────────────────────────

        private float ComputeHeaderWidth()
        {
            const float clefWidth = 54f;
            const float symSlot   = 14f;
            const float keySigGap = 6f;
            const float timeSigW  = 24f + 8f;
            const float minMargin = 8f;

            string key   = _session.Key;
            string scale = _session.SelectedScale;
            int accCount = _session.Tune == "Tuner" ? 0 : GetAccidentalCount(key, scale);
            float keySigEnd = clefWidth + accCount * symSlot + keySigGap;
            return keySigEnd + timeSigW + minMargin;
        }

        // ── Note geometry ─────────────────────────────────────────────────────────

        private static float NoteY(GeneratedNote note, float staffTop, float staffMid)
        {
            int steps = DiatonicStepsFromB4(note.Letter, note.Octave);
            return staffMid + steps * (StaffLineSpacing / 2f);
        }

        private static int DiatonicStepsFromB4(char letter, int octave)
        {
            int noteVal = letter switch
            {
                'C' => 0, 'D' => 1, 'E' => 2, 'F' => 3,
                'G' => 4, 'A' => 5, 'B' => 6, _ => 0
            };
            int b4Val   = 6 + 4 * 7;
            int thisVal = noteVal + octave * 7;
            return b4Val - thisVal;
        }

        // ── Drawing primitives ────────────────────────────────────────────────────

        private static Color ApplyAlpha(Color c, byte alpha)
            => Color.FromRgba(c.Red, c.Green, c.Blue, alpha / 255f);

        private void DrawNote(ICanvas canvas, NoteDuration duration, float x, float y,
                              float staffTop, float staffBot, Color ink,
                              V2NoteState state, byte fadeAlpha)
        {
            canvas.SaveState();
            try
            {
                float r = NoteHeadRadius;

                Color noteColor;
                switch (state)
                {
                    case V2NoteState.Current:
                        var hlFill   = ApplyAlpha(Color.FromArgb("#007BFF"), (byte)(fadeAlpha * 0.27f));
                        var hlStroke = ApplyAlpha(Color.FromArgb("#007BFF"), fadeAlpha);
                        canvas.FillColor   = hlFill;
                        canvas.StrokeColor = hlStroke;
                        canvas.StrokeSize  = 2f;
                        canvas.FillRoundedRectangle(x - r * 2.8f, y - r * 3.5f, r * 5.6f, r * 8f, 5f);
                        canvas.DrawRoundedRectangle(x - r * 2.8f, y - r * 3.5f, r * 5.6f, r * 8f, 5f);
                        noteColor = ApplyAlpha(Color.FromArgb("#007BFF"), fadeAlpha);
                        break;
                    case V2NoteState.Correct:
                        noteColor = ApplyAlpha(Color.FromArgb("#22AA44"), fadeAlpha);
                        break;
                    case V2NoteState.Wrong:
                        noteColor = ApplyAlpha(Color.FromArgb("#CC2222"), fadeAlpha);
                        break;
                    default:
                        noteColor = Color.FromRgba(ink.Red, ink.Green, ink.Blue, (fadeAlpha / 255f) * 0.7f);
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

                if (duration != NoteDuration.Whole)
                {
                    bool stemUp = y > (staffTop + (staffBot - staffTop) * 0.5f);
                    float stemX  = stemUp ? x + r : x - r;
                    float stemY  = stemUp ? y - r * 0.75f : y + r * 0.75f;
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
                            float off = f * (stemUp ? 10f : -10f);
                            if (stemUp)
                            {
                                canvas.DrawLine(stemX, stemEnd + off, stemX + 12f, stemEnd + 10f + off);
                                canvas.DrawLine(stemX + 12f, stemEnd + 10f + off, stemX + 6f, stemEnd + 18f + off);
                            }
                            else
                            {
                                canvas.DrawLine(stemX, stemEnd + off, stemX + 12f, stemEnd - 10f + off);
                                canvas.DrawLine(stemX + 12f, stemEnd - 10f + off, stemX + 6f, stemEnd - 18f + off);
                            }
                        }
                    }
                }
            }
            finally { canvas.RestoreState(); }
        }

        private void DrawRest(ICanvas canvas, NoteDuration duration, float x,
                              float staffTop, float staffMid, float staffBot,
                              Color ink, V2NoteState state, byte fadeAlpha)
        {
            canvas.SaveState();
            try
            {
                float r = NoteHeadRadius;
                if (state == V2NoteState.Current)
                {
                    canvas.FillColor   = ApplyAlpha(Color.FromArgb("#007BFF"), (byte)(fadeAlpha * 0.19f));
                    canvas.StrokeColor = ApplyAlpha(Color.FromArgb("#007BFF"), fadeAlpha);
                    canvas.StrokeSize  = 1.5f;
                    canvas.FillRoundedRectangle(x - r * 2f, staffMid - r * 3f, r * 4f, r * 6f, 4f);
                }

                Color rc = state switch
                {
                    V2NoteState.Correct => ApplyAlpha(Colors.Green,   fadeAlpha),
                    V2NoteState.Wrong   => ApplyAlpha(Colors.DarkRed, fadeAlpha),
                    V2NoteState.Current => ApplyAlpha(Color.FromArgb("#007BFF"), fadeAlpha),
                    _ => ApplyAlpha(ink, fadeAlpha)
                };

                canvas.FillColor   = rc;
                canvas.StrokeColor = rc;
                canvas.StrokeSize  = 1.5f;

                switch (duration)
                {
                    case NoteDuration.Whole:
                        canvas.FillRectangle(x - 8f, staffTop + StaffLineSpacing - 5f, 16f, 5f);
                        break;
                    case NoteDuration.Half:
                        canvas.FillRectangle(x - 8f, staffMid, 16f, 5f);
                        break;
                    case NoteDuration.Quarter:
                        canvas.DrawLine(x,      staffMid - 10f, x + 4f, staffMid - 6f);
                        canvas.DrawLine(x + 4f, staffMid - 6f,  x - 4f, staffMid - 2f);
                        canvas.DrawLine(x - 4f, staffMid - 2f,  x + 4f, staffMid + 2f);
                        canvas.DrawLine(x + 4f, staffMid + 2f,  x,      staffMid + 6f);
                        break;
                    case NoteDuration.Eighth:
                        canvas.FillEllipse(x - 2f, staffMid - 2f, 4f, 4f);
                        canvas.DrawLine(x, staffMid - 2f, x + 6f, staffMid - 10f);
                        break;
                    default:
                        canvas.DrawLine(x,      staffMid + 4f,  x + 4f, staffMid - 4f);
                        canvas.DrawLine(x + 4f, staffMid - 4f,  x - 2f, staffMid - 10f);
                        break;
                }
            }
            finally { canvas.RestoreState(); }
        }

        private void DrawLedgerLines(ICanvas canvas, GeneratedNote note, float x,
                                     float staffTop, float staffBot, Color ink, byte fadeAlpha)
        {
            canvas.SaveState();
            try
            {
                float staffMid  = staffTop + StaffLineSpacing * 2f;
                float ny        = NoteY(note, staffTop, staffMid);
                float ledgerHW  = NoteHeadRadius * 2.2f;

                canvas.StrokeColor = ApplyAlpha(ink, fadeAlpha);
                canvas.StrokeSize  = 1.5f;

                if (ny < staffTop - 2f)
                {
                    float cur = staffTop - StaffLineSpacing;
                    while (cur >= ny - 2f) { canvas.DrawLine(x - ledgerHW, cur, x + ledgerHW, cur); cur -= StaffLineSpacing; }
                }
                if (ny > staffBot + 2f)
                {
                    float cur = staffBot + StaffLineSpacing;
                    while (cur <= ny + 2f) { canvas.DrawLine(x - ledgerHW, cur, x + ledgerHW, cur); cur += StaffLineSpacing; }
                }
            }
            finally { canvas.RestoreState(); }
        }

        private void DrawAccidental(ICanvas canvas, GeneratedNote note, float x, float y,
                                    Color ink, Dictionary<(char, int), Accidental>? history,
                                    HashSet<(char, int)>? barCancelled,
                                    byte fadeAlpha)
        {
            canvas.SaveState();
            try
            {
                var eff = note.Accidental;

                if (eff == Accidental.None && history != null)
                {
                    if (history.TryGetValue((note.Letter, note.Octave), out var prev)
                        && (prev == Accidental.Sharp || prev == Accidental.Flat
                            || prev == Accidental.DoubleSharp || prev == Accidental.DoubleFlat))
                        eff = Accidental.Natural;
                }

                if (history != null) history[(note.Letter, note.Octave)] = eff;
                if (eff == Accidental.None) return;

                // Suppress key-sig accidentals unless this pitch-class had its key-sig
                // accidental cancelled earlier in the same bar — in that case the accidental
                // must be shown explicitly to restore the key-signature pitch.
                if (IsAccidentalInKeySig(note))
                {
                    if (barCancelled == null || !barCancelled.Contains((note.Letter, note.Octave)))
                        return;
                    // Falls through: accidental will be drawn as a reminder.
                }

                string glyph = eff switch
                {
                    Accidental.Sharp       => "♯",
                    Accidental.Flat        => "♭",
                    Accidental.Natural     => "♮",
                    Accidental.DoubleSharp => "𝄪",
                    Accidental.DoubleFlat  => "𝄫",
                    _ => ""
                };
                if (string.IsNullOrEmpty(glyph)) return;

                bool isFlat = eff == Accidental.Flat || eff == Accidental.DoubleFlat;
                const float symH = 60f, symW = 36f;
                float fontSize = isFlat ? 48f : 30f;
                float yAdjust  = isFlat ? 0.66f : 0.38f;
                float boxLeft  = x + NoteHeadRadius - 2f - symW;
                float yTop     = y - symH * yAdjust;

                canvas.FontColor = ApplyAlpha(ink, fadeAlpha);
                canvas.FontSize  = fontSize;
                canvas.DrawString(glyph, boxLeft, yTop, symW, symH,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
            }
            finally { canvas.RestoreState(); }
        }

        private void DrawNoteName(ICanvas canvas, GeneratedNote note, float x, float ny,
                                   float staffTop, float staffBot, Color ink, byte fadeAlpha)
        {
            canvas.SaveState();
            try
            {
                canvas.FontColor = ApplyAlpha(ink, fadeAlpha);
                canvas.FontSize  = 11;
                float labelY = ny > (staffTop + staffBot) / 2f
                    ? ny + NoteHeadRadius + 5f
                    : ny - NoteHeadRadius - 15f;
                canvas.DrawString(note.SpelledName, x - 14f, labelY, 28f, 14f,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
            }
            finally { canvas.RestoreState(); }
        }

        // ── Key / time signature drawing ──────────────────────────────────────────

        private float DrawKeySignature(ICanvas canvas, float staffTop, float staffMid, Color ink)
        {
            const float startX  = 54f;
            const float symSlot = 14f;
            const float symH    = 60f;
            const float symW    = 36f;

            if (_session.Tune == "Tuner") return startX;

            int accCount = GetAccidentalCount(_session.Key, _session.SelectedScale);
            if (accCount == 0) return startX;

            bool useFlats = IsKeyFlat(_session.Key);
            string glyph  = useFlats ? "♭" : "♯";

            int[] flatSteps  = { 0, -3,  1, -2,  2, -1,  3 };
            int[] sharpSteps = { -4, -1, -5, -2,  1, -3,  0 };
            int[] steps = useFlats ? flatSteps : sharpSteps;

            canvas.SaveState();
            canvas.FontColor = ink;
            canvas.FontSize  = useFlats ? 48f : 30f;
            float sigX = startX;
            for (int i = 0; i < Math.Min(accCount, steps.Length); i++)
            {
                float yCenter = staffMid + steps[i] * (StaffLineSpacing / 2f);
                float yAdjust = useFlats ? 0.66f : 0.38f;
                float yTop    = yCenter - symH * yAdjust;
                canvas.DrawString(glyph, sigX, yTop, symW, symH,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
                sigX += symSlot;
            }
            canvas.RestoreState();
            return sigX;
        }

        private void DrawTimeSignature(ICanvas canvas, float staffTop, float staffMid,
                                       Color ink, float keySigEndX)
        {
            canvas.SaveState();
            try
            {
                string timeSig = _session.V2TimeSignature ?? "4/4";
                var parts = timeSig.Split('/');
                if (parts.Length != 2) return;

                float tsX = keySigEndX + 6f;
                float boxW = 24f;

                canvas.FontColor = ink;
                canvas.FontSize  = 22;
                canvas.Font      = Microsoft.Maui.Graphics.Font.DefaultBold;

                float halfH   = StaffLineSpacing * 2f;
                float topY    = staffTop + (halfH - 22f) * 0.5f;
                float bottomY = staffMid + (halfH - 22f) * 0.5f;

                canvas.DrawString(parts[0], tsX, topY,    boxW, 22f, HorizontalAlignment.Center, VerticalAlignment.Top);
                canvas.DrawString(parts[1], tsX, bottomY, boxW, 22f, HorizontalAlignment.Center, VerticalAlignment.Top);
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            }
            finally { canvas.RestoreState(); }
        }

        // ── Key signature helpers (mirrors V2) ────────────────────────────────────

        private static int GetAccidentalCount(string key, string scale)
        {
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
            "A" => "C", "E" => "G", "B" => "D", "F#" => "A", "C#" => "E",
            "G#" => "B", "D#" => "F#", "D" => "F", "G" => "Bb", "C" => "Eb",
            "F" => "Ab", "Bb" => "Db", "Eb" => "Gb", _ => minorKey
        };

        private static bool IsKeyFlat(string key)
            => key is "F" or "Bb" or "Eb" or "Ab" or "Db" or "Gb" or "Cb";

        private bool IsAccidentalInKeySig(GeneratedNote note)
        {
            if (note.Accidental == Accidental.None || note.Accidental == Accidental.Natural) return false;

            string key = _session.Key;
            string scale = _session.SelectedScale;
            int accCount = GetAccidentalCount(key, scale);
            if (accCount == 0) return false;

            bool useFlats = IsKeyFlat(key);
            bool typeMatch = useFlats
                ? note.Accidental == Accidental.Flat
                : note.Accidental == Accidental.Sharp;
            if (!typeMatch) return false;

            char[] flatLetters  = { 'B', 'E', 'A', 'D', 'G', 'C', 'F' };
            char[] sharpLetters = { 'F', 'C', 'G', 'D', 'A', 'E', 'B' };
            char[] letters = useFlats ? flatLetters : sharpLetters;
            for (int i = 0; i < Math.Min(accCount, letters.Length); i++)
                if (letters[i] == note.Letter) return true;
            return false;
        }

        /// <summary>
        /// Returns true if this note's letter is governed by the key signature
        /// (i.e. the key sig applies a sharp or flat to this pitch-class),
        /// regardless of the accidental currently on the note.
        /// Used to detect when a key-sig note is given a different accidental,
        /// cancelling the key sig within the bar.
        /// </summary>
        private bool IsNoteInKeySig(GeneratedNote note)
        {
            string key = _session.Key;
            string scale = _session.SelectedScale;
            int accCount = GetAccidentalCount(key, scale);
            if (accCount == 0) return false;

            bool useFlats = IsKeyFlat(key);
            char[] flatLetters  = { 'B', 'E', 'A', 'D', 'G', 'C', 'F' };
            char[] sharpLetters = { 'F', 'C', 'G', 'D', 'A', 'E', 'B' };
            char[] letters = useFlats ? flatLetters : sharpLetters;
            for (int i = 0; i < Math.Min(accCount, letters.Length); i++)
                if (letters[i] == note.Letter) return true;
            return false;
        }
    }
}
