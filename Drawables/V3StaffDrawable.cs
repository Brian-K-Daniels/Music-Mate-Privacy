using Microsoft.Maui.Graphics;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Drawables
{
    /// <summary>
    /// V3 two-staff drawable.
    /// Renders an upper and a lower treble staff inside a single <see cref="ICanvas"/>.
    /// Reading order follows standard sheet music: upper staff first, then lower staff.
    ///
    /// <para><b>Transitions / fade:</b> when new notes are loaded onto a staff while the
    /// player is on the other staff, set the corresponding alpha array to 0 and animate
    /// it toward 1 from the page layer.  The drawable reads <see cref="UpperAlpha"/> and
    /// <see cref="LowerAlpha"/> and applies them uniformly to every note on that staff.</para>
    ///
    /// <para><b>Feedback:</b> noteheads are coloured:
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

        // ── Fixed horizontal constants ─────────────────────────────────────────
        private const float ScrollPxPerBeat = 48f;
        private const float RightMargin     = 16f;

        private float _leftMargin = 115f;
        private float LeftMargin => _leftMargin;

        /// <summary>
        /// Available canvas height set by the page before calling <see cref="ComputeRequiredHeight"/>.
        /// The drawable scales staff geometry to fill this height, reserving space for the OS home bar.
        /// </summary>
        public float AvailableHeight { get; set; } = 300f;

        // ── Cached layout ─────────────────────────────────────────────────────────

        /// <summary>Pixels-per-beat as computed during the most recent <see cref="Draw"/> call.</summary>
        public float LastComputedPxPerBeat { get; private set; }

        private struct V3Layout
        {
            public float Sls;          // pixels per staff space (line spacing)
            public float HS;           // half-space = Sls/2
            public float NoteHeadR;    // notehead radius
            public float StemLen;      // stem length
            public float UpperTop, UpperMid, UpperBot;
            public float LowerTop,  LowerMid,  LowerBot;
            public float TotalHeight;
        }
        private V3Layout _layout;

        // ── Constructor ───────────────────────────────────────────────────────────
        public V3StaffDrawable(NoteSessionService session, ThemeService theme)
        {
            _session = session;
            _theme   = theme;
        }

        // ── Ordered layout pipeline ──────────────────────────────────────────────
        /// <summary>
        /// Steps 1–8: determine note ranges, derive staff-line spacing so the complete
        /// note range (both staffs + gap) fills <paramref name="availH"/> exactly, then
        /// compute all staff Y positions.  Must be called before any drawing.
        /// </summary>
        // Approximate height of the iOS/Android system home-indicator bar at the bottom of the screen.
        private const float BottomBarReserve = 34f;

        private void ComputeLayout(float availH)
        {
            if (availH <= 0f) availH = 300f;

            // Reserve space for the OS home-indicator bar so notes are never hidden behind it.
            float usableH = availH - BottomBarReserve;

            // Diatonic-step range for each staff.
            int minS1 = 0, maxS1 = 0, minS2 = 0, maxS2 = 0;
            bool hasU = false, hasL = false;
            foreach (var n in UpperNotes)
            {
                if (n.IsRest) continue;
                int s = DiatonicStepsFromB4(n.Letter, n.Octave);
                if (!hasU) { minS1 = maxS1 = s; hasU = true; }
                else { if (s < minS1) minS1 = s; if (s > maxS1) maxS1 = s; }
            }
            foreach (var n in LowerNotes)
            {
                if (n.IsRest) continue;
                int s = DiatonicStepsFromB4(n.Letter, n.Octave);
                if (!hasL) { minS2 = maxS2 = s; hasL = true; }
                else { if (s < minS2) minS2 = s; if (s > maxS2) maxS2 = s; }
            }

            // Half-spaces of clearance beyond the 5-line staff boundaries.
            // eA = above-top clearance, eB = below-bottom clearance.
            // A fixed breathing-room constant pads both the top of the upper staff
            // and the bottom of the lower staff so noteheads are never clipped.
            const int breathing = 3;
            int eA1 = Math.Max(0, -4 - minS1) + breathing;
            int eB1 = Math.Max(0,  maxS1 - 4) + breathing;
            int eA2 = Math.Max(0, -4 - minS2) + breathing;
            int eB2 = Math.Max(0,  maxS2 - 4) + breathing;

            // Total half-spaces consumed by the two staffs (each staff = 8 hs for 5 lines / 4 spaces).
            float totalHalfSpaces = (float)(8 + eA1 + eB1 + 8 + eA2 + eB2);

            // Derive sls so the two staffs fill usableH, then clamp to a comfortable range.
            float sls = usableH / (totalHalfSpaces / 2f);
            sls = Math.Clamp(sls, 6f, 14f);
            float hs = sls / 2f;

            float noteHeadR = sls / 2f;
            float stemLen   = sls * 2.83f;

            // Staff Y positions — no extra gap between the staffs beyond the natural breathing room.
            float upperTop = eA1 * hs;
            float upperMid = upperTop + 2f * sls;
            float upperBot = upperTop + 4f * sls;

            float lowerTop = upperBot + eB1 * hs + eA2 * hs;
            float lowerMid = lowerTop + 2f * sls;
            float lowerBot = lowerTop + 4f * sls;
            float contentH = lowerBot + eB2 * hs;

            // Distribute unused space equally above the top and below the bottom (and the
            // middle gap between the staffs already receives eB1+eA2 half-spaces of padding).
            float slack  = Math.Max(0f, usableH - contentH);
            float vOffset = slack / 2f;

            _layout = new V3Layout
            {
                Sls         = sls,
                HS          = hs,
                NoteHeadR   = noteHeadR,
                StemLen     = stemLen,
                UpperTop    = upperTop + vOffset,
                UpperMid    = upperMid + vOffset,
                UpperBot    = upperBot + vOffset,
                LowerTop    = lowerTop + vOffset,
                LowerMid    = lowerMid + vOffset,
                LowerBot    = lowerBot + vOffset,
                TotalHeight = contentH + vOffset
            };
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

            // Step 8 & 9: compute layout, then draw
            ComputeLayout(dirtyRect.Height);
            _leftMargin = ComputeHeaderWidth();

            float upperTop = _layout.UpperTop;
            float upperMid = _layout.UpperMid;
            float upperBot = _layout.UpperBot;
            float lowerTop = _layout.LowerTop;
            float lowerMid = _layout.LowerMid;
            float lowerBot = _layout.LowerBot;

            // ── Compute px-per-beat ───────────────────────────────────────────────
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
                    float fitted = (lineWidth0 - _layout.NoteHeadR * 2f) / (float)maxSpan;
                    pxPerBeat = fitted;
                }
            }
            LastComputedPxPerBeat = pxPerBeat;

            // ── Static layout: no scrolling ──────────────────────────────────────
            double upperStartBeat = UpperNotes.Count > 0 ? (UpperNotes[0].BeatPosition ?? 0.0) : 0.0;
            float upperScroll = -(float)(upperStartBeat * pxPerBeat);
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
            float clipLeft  = LeftMargin - _layout.NoteHeadR * 4f;
            float clipRight = LeftMargin + lineWidth + _layout.NoteHeadR * 4f;

            // Staff lines
            canvas.StrokeColor = ink;
            canvas.StrokeSize  = 1.5f;
            for (int i = 0; i < 5; i++)
            {
                float y = staffTop + i * _layout.Sls;
                canvas.DrawLine(dirtyRect.X, y, LeftMargin + lineWidth, y);
            }

            // Clef — scale font size so the glyph fills the staff height
            canvas.SaveState();
            canvas.FontColor = ink;
            canvas.FontSize  = _layout.Sls * 5f;   // 60 at sls=12
            float clefW = _layout.Sls * 4.5f;       // 54 at sls=12
            float clefH = staffBot - staffTop + _layout.Sls * 3.2f;
            canvas.DrawString("𝄞", dirtyRect.X + 2f, staffTop, clefW, clefH,
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

            // Pre-pass: identify pairs of consecutive eighth notes that share a beat
            // (beat-on + beat-and), so they can be beamed together.
            // beamPairs[i] = (partner index, stemUp)
            var beamPairs = new Dictionary<int, (int partner, bool stemUp)>();
            {
                double cur = 0;
                for (int i = 0; i < notes.Count - 1; i++)
                {
                    var n0 = notes[i];
                    var n1 = notes[i + 1];
                    double pos0 = n0.BeatPosition ?? cur;
                    double pos1 = n1.BeatPosition ?? (pos0 + n0.BeatDuration);

                    bool canBeam = !n0.IsRest && !n1.IsRest
                        && n0.Duration == NoteDuration.Eighth
                        && n1.Duration == NoteDuration.Eighth
                        && Math.Abs(pos1 - pos0 - 0.5) < 1e-9   // second note is exactly an eighth after the first
                        && Math.Abs(pos0 % 1.0) < 1e-9;          // first note falls on a whole beat

                    if (canBeam)
                    {
                        float staffMidLocal = staffTop + _layout.Sls * 2f;
                        float ny0 = NoteY(n0, staffTop, staffMidLocal);
                        float ny1 = NoteY(n1, staffTop, staffMidLocal);
                        // Use the note farthest from staff midline to decide direction.
                        // Standard: stem up when note is below midline.
                        float mid = staffTop + _layout.Sls * 2f;
                        bool stemUp = (Math.Abs(ny0 - mid) >= Math.Abs(ny1 - mid))
                            ? ny0 > mid
                            : ny1 > mid;
                        beamPairs[i]     = (i + 1, stemUp);
                        beamPairs[i + 1] = (i,     stemUp);
                    }

                    cur = pos0 + n0.BeatDuration;
                }
            }
            // Stores stem-tip positions for beamed notes: index → (x, y, color)
            var beamStemTips = new Dictionary<int, (float x, float y, Color color)>();

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
                    if (!IsAccidentalInKeySig(note) && IsNoteInKeySig(note))
                    {
                        // This pitch-class is governed by the key sig, but the current note
                        // deviates. Record the cancellation so later notes in the same bar
                        // that return to the key-sig accidental must show it explicitly.
                        barCancelledAccidentals.Add(key);
                    }
                }

                if (nx < clipLeft || nx > clipRight)
                {
                    // Still update accidental history for off-screen notes so on-screen notes
                    // that follow get the correct natural/reminder logic.
                    if (!note.IsRest)
                        accHistory[(note.Letter, note.Octave)] = note.Accidental;
                    beatCursor += note.BeatDuration;
                    continue;
                }

                // Apply fade alpha to note color opacity
                byte fadeAlpha = (byte)Math.Clamp((int)(alpha * 255), 0, 255);

                if (note.IsRest)
                {
                    DrawRest(canvas, note.Duration, nx, staffTop, staffMid, staffBot, ink, state, fadeAlpha);
                }
                else
                {
                    float ny = NoteY(note, staffTop, staffMid);

                    bool isBeamed = beamPairs.ContainsKey(i);
                    bool? forceStemUp = isBeamed ? beamPairs[i].stemUp : (bool?)null;
                    DrawNote(canvas, note.Duration, nx, ny, staffTop, staffBot, ink, state, fadeAlpha,
                             forceStemUp, isBeamed,
                             out float stemTipX, out float stemTipY);
                    if (isBeamed)
                        beamStemTips[i] = (stemTipX, stemTipY,
                            GetNoteColor(state, ink, fadeAlpha));

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

            // Draw beams connecting paired eighth-note stem tips
            var drawnBeams = new HashSet<int>();
            foreach (var kv in beamPairs)
            {
                int idx = kv.Key;
                int partner = kv.Value.partner;
                if (drawnBeams.Contains(idx) || !beamStemTips.TryGetValue(idx, out var t0)
                    || !beamStemTips.TryGetValue(partner, out var t1))
                    continue;
                drawnBeams.Add(idx);
                drawnBeams.Add(partner);

                canvas.SaveState();
                canvas.StrokeColor = t0.color;
                canvas.StrokeSize  = 4f;
                canvas.DrawLine(t0.x, t0.y, t1.x, t1.y);
                canvas.RestoreState();
            }
        }

        private static double SumBeats(List<GeneratedNote> notes, int from, int to)
        {
            double s = 0;
            for (int i = from; i < Math.Min(to, notes.Count); i++)
                s += notes[i].BeatDuration;
            return s;
        }

        // ── Layout public API ─────────────────────────────────────────────────────

        /// <summary>
        /// Total canvas height to allocate.  Equal to <see cref="AvailableHeight"/> by
        /// construction — the layout scales to exactly fill the available space.
        /// </summary>
        public float ComputeRequiredHeight()
        {
            ComputeLayout(AvailableHeight);
            return AvailableHeight;   // always fill the available space; no wasted bottom gap
        }

        // ── Header metrics ────────────────────────────────────────────────────────

        private float ComputeHeaderWidth()
        {
            float clefWidth = _layout.Sls * 4.5f;     // 54 at sls=12
            float symSlot   = _layout.Sls * 1.17f;    // 14 at sls=12
            const float keySigGap = 6f;
            const float timeSigW  = 24f + 8f;
            const float minMargin = 8f;

            string key   = _session.Key;
            string scale = _session.SelectedScale;
            bool suppressKeySig = _session.Tune == "Tuner"
                || _session.Tune == "Practice Tune" || scale == "Chromatic";
            int accCount = suppressKeySig ? 0 : GetAccidentalCount(key, scale);
            float keySigEnd = clefWidth + accCount * symSlot + keySigGap;
            return keySigEnd + timeSigW + minMargin;
        }

        // ── Note geometry ─────────────────────────────────────────────────────────

        private float NoteY(GeneratedNote note, float staffTop, float staffMid)
        {
            int steps = DiatonicStepsFromB4(note.Letter, note.Octave);
            return staffMid + steps * _layout.HS;
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

        private static Color GetNoteColor(V2NoteState state, Color ink, byte fadeAlpha) => state switch
        {
            V2NoteState.Current => ApplyAlpha(Color.FromArgb("#007BFF"), fadeAlpha),
            V2NoteState.Correct => ApplyAlpha(Color.FromArgb("#22AA44"), fadeAlpha),
            V2NoteState.Wrong   => ApplyAlpha(Color.FromArgb("#CC2222"), fadeAlpha),
            _ => ApplyAlpha(Colors.Black, (byte)(fadeAlpha * 0.85f))
        };

        private void DrawNote(ICanvas canvas, NoteDuration duration, float x, float y,
                              float staffTop, float staffBot, Color ink,
                              V2NoteState state, byte fadeAlpha,
                              bool? forceStemUp, bool isBeamed,
                              out float stemTipX, out float stemTipY)
        {
            stemTipX = x;
            stemTipY = y;
            canvas.SaveState();
            try
            {
                float r = _layout.NoteHeadR;

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
                        noteColor = ApplyAlpha(Colors.Black, (byte)(fadeAlpha * 0.85f));
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
                    bool stemUp = forceStemUp ?? (y > (staffTop + (staffBot - staffTop) * 0.5f));
                    float stemX  = stemUp ? x + r : x - r;
                    float stemY  = stemUp ? y - r * 0.75f : y + r * 0.75f;
                    float stemEnd = stemUp ? stemY - _layout.StemLen : stemY + _layout.StemLen;
                    canvas.StrokeColor = noteColor;
                    canvas.StrokeSize  = 2f;
                    canvas.DrawLine(stemX, stemY, stemX, stemEnd);

                    stemTipX = stemX;
                    stemTipY = stemEnd;

                    // Individual flag — suppressed for beamed notes (beam drawn after all notes)
                    if (!isBeamed)
                    {
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
                float r = _layout.NoteHeadR;
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
                        canvas.FillRectangle(x - r * 1.3f, staffTop + _layout.Sls - r * 0.8f, r * 2.6f, r * 0.8f);
                        break;
                    case NoteDuration.Half:
                        canvas.FillRectangle(x - r * 1.3f, staffMid, r * 2.6f, r * 0.8f);
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
                float staffMid  = staffTop + _layout.Sls * 2f;
                float ny        = NoteY(note, staffTop, staffMid);
                float ledgerHW  = _layout.NoteHeadR * 2.2f;

                canvas.StrokeColor = ApplyAlpha(ink, fadeAlpha);
                canvas.StrokeSize  = 1.5f;

                if (ny < staffTop - 2f)
                {
                    float cur = staffTop - _layout.Sls;
                    while (cur >= ny - 2f) { canvas.DrawLine(x - ledgerHW, cur, x + ledgerHW, cur); cur -= _layout.Sls; }
                }
                if (ny > staffBot + 2f)
                {
                    float cur = staffBot + _layout.Sls;
                    while (cur <= ny + 2f) { canvas.DrawLine(x - ledgerHW, cur, x + ledgerHW, cur); cur += _layout.Sls; }
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
                bool isKeySigAcc = IsAccidentalInKeySig(note);
                if (isKeySigAcc)
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
                float boxLeft  = x + _layout.NoteHeadR - 2f - symW;
                float yTop     = y - symH * yAdjust;

                canvas.FontColor = ApplyAlpha(ink, fadeAlpha);
                canvas.FontSize  = fontSize;
                canvas.DrawString(glyph, boxLeft, yTop, symW, symH,
                    HorizontalAlignment.Center, VerticalAlignment.Top);

                // Reminder drawn — clear the cancellation so this key-sig note does not
                // continue to show an accidental for every subsequent appearance in the bar.
                if (isKeySigAcc)
                    barCancelled?.Remove((note.Letter, note.Octave));
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
                    ? ny + _layout.NoteHeadR + 5f
                    : ny - _layout.NoteHeadR - 15f;
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

            if (_session.Tune == "Tuner"
                || _session.Tune == "Practice Tune" || _session.SelectedScale == "Chromatic")
                return startX;

            int accCount = GetAccidentalCount(_session.Key, _session.SelectedScale);
            if (accCount == 0) return startX;

            bool useFlats = IsKeyFlat(_session.Key);
            string glyph  = useFlats ? "♭" : "♯";

            int[] flatSteps  = { 0, -3,  1, -2,  2, -1,  3 };
            int[] sharpSteps = { -4, -1, -5, -2,  1, -3,  0 };
            int[] steps = useFlats ? flatSteps : sharpSteps;

            float scaledSlot = symSlot * _layout.Sls / 12f;
            canvas.SaveState();
            canvas.FontColor = ink;
            canvas.FontSize  = useFlats ? _layout.Sls * 4f : _layout.Sls * 2.5f;
            float sigX = startX;
            for (int i = 0; i < Math.Min(accCount, steps.Length); i++)
            {
                float yCenter = staffMid + steps[i] * _layout.HS;
                float yAdjust = useFlats ? 0.78f : 0.38f;
                float yTop    = yCenter - symH * yAdjust;
                canvas.DrawString(glyph, sigX, yTop, symW, symH,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
                sigX += scaledSlot;
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

                float tsFontSize = Math.Max(10f, _layout.Sls * 1.83f);  // 22 at sls=12
                canvas.FontColor = ink;
                canvas.FontSize  = tsFontSize;
                canvas.Font      = Microsoft.Maui.Graphics.Font.DefaultBold;

                float halfH   = _layout.Sls * 2f;
                float topY    = staffTop + (halfH - tsFontSize) * 0.5f;
                float bottomY = staffMid + (halfH - tsFontSize) * 0.5f;

                canvas.DrawString(parts[0], tsX, topY,    boxW, tsFontSize, HorizontalAlignment.Center, VerticalAlignment.Top);
                canvas.DrawString(parts[1], tsX, bottomY, boxW, tsFontSize, HorizontalAlignment.Center, VerticalAlignment.Top);
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
