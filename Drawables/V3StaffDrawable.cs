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
        private readonly ISafeAreaService? _safeArea;

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
        private const float ScrollPxPerBeat = 42f;   // reduced from 48 for better fit
        private const float RightMargin     = 48f;   // increased to prevent last notes from being cut off

        // V3 uniform spacing constants
        private const float ItemSpacing      = 46f;   // fixed horizontal spacing per item (reduced from 52)
        private const float AccidentalWidth  = 40f;   // extra width reserved when a note has an accidental
        private const float BarLeftPadding   = 16f;   // minimum space from bar line to following note
        private const float BarRightPadding  = 20f;   // minimum space from last note to final bar line

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

        /// <summary>
        /// Pre-computed horizontal position for each note/rest on a staff.
        /// Calculated once during layout planning, then used for drawing notes, beams, and bars.
        /// </summary>
        private struct NoteLayout
        {
            public float X;              // horizontal center of notehead
            public float AccidentalX;    // X position for accidental (left of notehead)
            public bool HasAccidental;   // whether this note needs accidental space
        }

        /// <summary>
        /// Pre-computed bar line positions for a staff.
        /// </summary>
        private struct BarLayout
        {
            public float X;              // horizontal position of bar line
            public bool IsDouble;        // true for final double bar
        }

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
        public V3StaffDrawable(NoteSessionService session, ThemeService theme, ISafeAreaService? safeArea = null)
        {
            _session = session;
            _theme   = theme;
            _safeArea = safeArea;
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

        /// <summary>
        /// Computes horizontal layout for a staff: X position for each note and bar line.
        /// Returns (noteLayouts, barLayouts, totalWidth).
        /// 
        /// Phase 1: Calculate px/beat to fit content in available width
        /// Phase 2: Position each note based on beat position with minimum spacing
        /// Phase 3: Position bar lines at measure boundaries and final bar
        /// </summary>
        private (NoteLayout[] noteLayouts, BarLayout[] barLayouts, float totalWidth) 
            PlanHorizontalLayout(
                List<GeneratedNote> notes,
                List<double> barBeats,
                float availableWidth,
                bool isFinalStaff,
                bool hasEndSingleBar)
        {
            if (notes.Count == 0)
                return (Array.Empty<NoteLayout>(), Array.Empty<BarLayout>(), LeftMargin);

            // Calculate total beat duration of all notes
            double totalBeats = 0.0;
            for (int i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                double beatPos = note.BeatPosition ?? totalBeats;
                totalBeats = beatPos + note.BeatDuration;
            }

            // Ensure reasonable spacing: aim for 40-50 px per beat
            float pxPerBeat = totalBeats > 0 
                ? Math.Clamp(availableWidth / (float)totalBeats, 38f, 54f)
                : 42f;

            // Phase 1: Position notes with collision detection
            var noteLayouts = new NoteLayout[notes.Count];
            float prevNoteRight = LeftMargin;  // Track rightmost edge of previous note

            // Build bar line positions first so we can avoid them when placing notes
            var barPositions = new List<float>();
            foreach (var beatPos in barBeats.OrderBy(b => b))
            {
                float bx = LeftMargin + (float)beatPos * pxPerBeat;
                barPositions.Add(bx);
            }

            for (int i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                double beatPos = note.BeatPosition ?? 0.0;

                // Base X position from beat position
                float idealX = LeftMargin + (float)beatPos * pxPerBeat;

                // Check if accidental is needed
                bool hasAcc = !note.IsRest && note.Accidental != Accidental.None;
                float accWidth = hasAcc ? AccidentalWidth * 0.6f : 0f;

                // Minimum spacing between notes (including accidental space)
                const float MinNoteGap = 8f;
                float noteLeftEdge = idealX - accWidth - _layout.NoteHeadR;

                // If this note would overlap the previous note, push it right
                if (noteLeftEdge < prevNoteRight + MinNoteGap)
                {
                    idealX = prevNoteRight + MinNoteGap + accWidth + _layout.NoteHeadR;
                }

                // Check if note would be too close to any bar line
                foreach (var barX in barPositions)
                {
                    float noteRight = idealX + _layout.NoteHeadR + 4f; // add small buffer for stem

                    // If note would overlap bar line (within BarLeftPadding), push note right
                    if (Math.Abs(idealX - barX) < BarLeftPadding || 
                        (noteLeftEdge < barX && noteRight > barX - 2f))
                    {
                        idealX = barX + BarLeftPadding + accWidth + _layout.NoteHeadR;
                        break;
                    }
                }

                noteLayouts[i] = new NoteLayout
                {
                    X = idealX,
                    AccidentalX = idealX - AccidentalWidth * 0.8f,
                    HasAccidental = hasAcc
                };

                // Update rightmost edge for next note (include stem width)
                prevNoteRight = idealX + _layout.NoteHeadR + 4f;
            }

            // Phase 2: Position bar lines at measure boundaries
            var barList = new List<BarLayout>();

            // Draw bar lines at measure boundaries
            foreach (var beatPos in barBeats.OrderBy(b => b))
            {
                float bx = LeftMargin + (float)beatPos * pxPerBeat;
                barList.Add(new BarLayout { X = bx, IsDouble = false });
            }

            // Phase 3: Final bar (always add - single or double)
            if (notes.Count > 0)
            {
                // Position final bar after the last note with proper spacing
                var lastNote = notes[^1];
                double lastBeatEnd = (lastNote.BeatPosition ?? 0.0) + lastNote.BeatDuration;

                // Ensure final bar doesn't overlap last note
                float lastNoteLayout = noteLayouts[^1].X;
                float lastNoteRight = lastNoteLayout + _layout.NoteHeadR + 4f; // include stem
                float endBarXFromBeat = LeftMargin + (float)lastBeatEnd * pxPerBeat;

                // Use the greater of: beat-based position or last-note-right + padding
                float endBarX = Math.Max(endBarXFromBeat, lastNoteRight + BarRightPadding);

                // Always add a final bar:
                // - Double bar if this is the final staff (end of entire sequence)
                // - Single bar otherwise (end of upper staff, or end of section)
                bool isDouble = isFinalStaff;
                barList.Add(new BarLayout { X = endBarX, IsDouble = isDouble });
            }

            // Calculate total width (furthest bar line + right margin for scrolling)
            float totalWidth = barList.Count > 0 
                ? barList.Max(b => b.X) + RightMargin 
                : LeftMargin + availableWidth;

            LastComputedPxPerBeat = pxPerBeat;

            // Validation: Check measure beat totals and note spacing
            ValidateLayout(notes, barBeats, noteLayouts);

            return (noteLayouts, barList.ToArray(), totalWidth);
        }

        /// <summary>
        /// Validates musical measure structure and note spacing.
        /// Logs warnings for incorrect measure durations or overlapping notes.
        /// </summary>
        private void ValidateLayout(List<GeneratedNote> notes, List<double> barBeats, NoteLayout[] noteLayouts)
        {
            if (notes.Count == 0) return;

            try
            {
                // Validate measure beat totals
                var sortedBars = barBeats.OrderBy(b => b).ToList();
                for (int i = 0; i < sortedBars.Count; i++)
                {
                    double measureStart = i == 0 ? 0.0 : sortedBars[i - 1];
                    double measureEnd = sortedBars[i];
                    double measureBeats = measureEnd - measureStart;

                    // Expected beats from time signature (default 4/4)
                    var timeSig = _session.V2TimeSignature ?? "4/4";
                    var parts = timeSig.Split('/');
                    double expectedBeats = parts.Length == 2 && int.TryParse(parts[0], out int num) ? num : 4;

                    // Allow tolerance for pickup measures and final incomplete measures
                    bool isFirstMeasure = i == 0 && measureStart == 0.0;
                    bool isLastMeasure = i == sortedBars.Count - 1;

                    if (!isFirstMeasure && !isLastMeasure && Math.Abs(measureBeats - expectedBeats) > 0.01)
                    {
                        Utilities.Utils.Log($"[V3 Validation] Measure {i}: {measureBeats:F2} beats (expected {expectedBeats}), " +
                                           $"start={measureStart:F2}, end={measureEnd:F2}");
                    }
                }

                // Validate note spacing
                const float MinAllowedSpacing = 4f;
                for (int i = 1; i < noteLayouts.Length; i++)
                {
                    float spacing = noteLayouts[i].X - noteLayouts[i - 1].X;
                    if (spacing < MinAllowedSpacing)
                    {
                        Utilities.Utils.Log($"[V3 Validation] Notes {i - 1} and {i} too close: {spacing:F1}px apart");
                    }
                }
            }
            catch (Exception ex)
            {
                Utilities.Utils.Log($"[V3 Validation] Error: {ex.Message}");
            }
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

            // Step 0: Calculate safe drawing area accounting for camera cutouts
            var insets = _safeArea?.GetSafeAreaInsets() ?? (0f, 0f, 0f, 0f);
            float safeLeft = dirtyRect.X + insets.Left;
            float safeRight = dirtyRect.X + dirtyRect.Width - insets.Right;
            float safeWidth = safeRight - safeLeft;

            Utilities.Utils.Log($"[V3] Canvas={dirtyRect.Width:F0}x{dirtyRect.Height:F0}, " +
                               $"Insets=L{insets.Left:F0},R{insets.Right:F0}, SafeWidth={safeWidth:F0}");

            // Step 1: Compute vertical layout
            ComputeLayout(dirtyRect.Height);
            _leftMargin = ComputeHeaderWidth();

            float upperTop = _layout.UpperTop;
            float upperMid = _layout.UpperMid;
            float upperBot = _layout.UpperBot;
            float lowerTop = _layout.LowerTop;
            float lowerMid = _layout.LowerMid;
            float lowerBot = _layout.LowerBot;

            // Step 2: Compute horizontal layout for both staffs
            // usableLineWidth = width available for notes/bars after accounting for header and right margin
            float usableLineWidth = safeWidth - _leftMargin - RightMargin;

            var (upperNoteLayouts, upperBarLayouts, upperTotalWidth) = PlanHorizontalLayout(
                UpperNotes, UpperBarBeats, usableLineWidth,
                isFinalStaff: LowerNotes.Count == 0,
                hasEndSingleBar: UpperHasEndBar && LowerNotes.Count > 0);

            var (lowerNoteLayouts, lowerBarLayouts, lowerTotalWidth) = PlanHorizontalLayout(
                LowerNotes, LowerBarBeats, usableLineWidth,
                isFinalStaff: true,
                hasEndSingleBar: false);

            // Step 3: Calculate horizontal compression if needed
            // Compare content width (without margins) against usable line width
            float upperContentWidth = upperTotalWidth - LeftMargin;
            float lowerContentWidth = lowerTotalWidth - LeftMargin;
            float maxContentWidth = Math.Max(upperContentWidth, lowerContentWidth);
            float horizontalScale = 1f;

            if (maxContentWidth > usableLineWidth)
            {
                // Clamp minimum scale to prevent collapsing notes on top of each other
                horizontalScale = Math.Max(0.5f, usableLineWidth / maxContentWidth);
                Utilities.Utils.Log($"[V3] Compression needed: contentWidth={maxContentWidth:F0}, " +
                                   $"usable={usableLineWidth:F0}, scale={horizontalScale:F3}");

                // Apply compression to all note and bar positions
                ApplyHorizontalScale(upperNoteLayouts, upperBarLayouts, horizontalScale, safeLeft);
                ApplyHorizontalScale(lowerNoteLayouts, lowerBarLayouts, horizontalScale, safeLeft);
            }
            else
            {
                // Just offset by safe left margin
                ApplyHorizontalOffset(upperNoteLayouts, upperBarLayouts, safeLeft);
                ApplyHorizontalOffset(lowerNoteLayouts, lowerBarLayouts, safeLeft);
            }

            // Verify rightmost position is within safe bounds
            float upperRightmost = upperBarLayouts.Length > 0 ? upperBarLayouts.Max(b => b.X) : 0f;
            float lowerRightmost = lowerBarLayouts.Length > 0 ? lowerBarLayouts.Max(b => b.X) : 0f;
            float rightmost = Math.Max(upperRightmost, lowerRightmost);

            Utilities.Utils.Log($"[V3] Rightmost={rightmost:F0}, SafeRight={safeRight:F0}, " +
                               $"Margin={(safeRight - rightmost):F0}");

            // Step 4: Draw both staffs using pre-computed layouts
            DrawStaff(canvas, dirtyRect, ink, upperTop, upperMid, upperBot,
                      UpperNotes, UpperNoteStates, upperNoteLayouts, upperBarLayouts,
                      UpperAlpha, IsUpperActive, IsUpperActive ? ActiveNoteIndex : -1,
                      safeLeft, safeRight);

            DrawStaff(canvas, dirtyRect, ink, lowerTop, lowerMid, lowerBot,
                      LowerNotes, LowerNoteStates, lowerNoteLayouts, lowerBarLayouts,
                      LowerAlpha, !IsUpperActive, !IsUpperActive ? ActiveNoteIndex : -1,
                      safeLeft, safeRight);
        }

        /// <summary>
        /// Apply horizontal scaling to compress layout when music exceeds safe width.
        /// Scale is applied to positions relative to LeftMargin, then offset by safeLeft + _leftMargin.
        /// </summary>
        private void ApplyHorizontalScale(NoteLayout[] noteLayouts, BarLayout[] barLayouts, 
                                          float scale, float safeLeft)
        {
            const float MinNoteSpacing = 6f;  // minimum px between sequential note centers after compression
            float prevX = safeLeft + _leftMargin;

            for (int i = 0; i < noteLayouts.Length; i++)
            {
                // Scale position relative to LeftMargin, then add safeLeft + _leftMargin offset
                float relativeX = noteLayouts[i].X - LeftMargin;
                float scaledX = safeLeft + _leftMargin + (relativeX * scale);

                // Enforce minimum spacing to prevent overlapping note heads
                if (i > 0 && scaledX < prevX + MinNoteSpacing)
                {
                    scaledX = prevX + MinNoteSpacing;
                }

                noteLayouts[i].X = scaledX;
                noteLayouts[i].AccidentalX = scaledX - AccidentalWidth * 0.8f;
                prevX = scaledX;
            }

            for (int i = 0; i < barLayouts.Length; i++)
            {
                float relativeX = barLayouts[i].X - LeftMargin;
                barLayouts[i].X = safeLeft + _leftMargin + (relativeX * scale);
            }
        }

        /// <summary>
        /// Apply horizontal offset when no compression is needed.
        /// </summary>
        private void ApplyHorizontalOffset(NoteLayout[] noteLayouts, BarLayout[] barLayouts, float offset)
        {
            for (int i = 0; i < noteLayouts.Length; i++)
            {
                noteLayouts[i].X += offset;
                noteLayouts[i].AccidentalX += offset;
            }

            for (int i = 0; i < barLayouts.Length; i++)
            {
                barLayouts[i].X += offset;
            }
        }

        // ── Per-staff rendering ───────────────────────────────────────────────────

        /// <summary>
        /// Draws a single staff using pre-computed horizontal layout.
        /// All X positions are read from noteLayouts and barLayouts arrays.
        /// </summary>
        private void DrawStaff(
            ICanvas canvas, RectF dirtyRect, Color ink,
            float staffTop, float staffMid, float staffBot,
            List<GeneratedNote> notes, V2NoteState[] states,
            NoteLayout[] noteLayouts, BarLayout[] barLayouts,
            float alpha,
            bool isActive, int currentIdx,
            float safeLeft, float safeRight)
        {
            if (notes.Count == 0) return;

            // Calculate staff line end position: extend to furthest bar line
            float staffLineEndX = barLayouts.Length > 0 
                ? barLayouts.Max(b => b.X) + 8f  // extend slightly past final bar
                : safeRight - RightMargin;

            // Staff lines - extend from safe left to the end of all content
            canvas.StrokeColor = ink;
            canvas.StrokeSize  = 1.5f;
            for (int i = 0; i < 5; i++)
            {
                float y = staffTop + i * _layout.Sls;
                canvas.DrawLine(safeLeft, y, staffLineEndX, y);
            }

            // Clef
            canvas.SaveState();
            canvas.FontColor = ink;
            canvas.FontSize  = _layout.Sls * 5f;
            float clefW = _layout.Sls * 4.5f;
            float clefH = staffBot - staffTop + _layout.Sls * 3.2f;
            canvas.DrawString("𝄞", safeLeft + 2f, staffTop, clefW, clefH,
                HorizontalAlignment.Left, VerticalAlignment.Top);
            canvas.RestoreState();

            // Key signature + time signature
            float keySigEndX = DrawKeySignature(canvas, staffTop, staffMid, ink, safeLeft);
            DrawTimeSignature(canvas, staffTop, staffMid, ink, keySigEndX);

            // Bar lines (using pre-computed positions)
            canvas.StrokeColor = ink;
            foreach (var bar in barLayouts)
            {
                if (bar.IsDouble)
                {
                    // Double bar (final)
                    canvas.StrokeSize = 2f;
                    canvas.DrawLine(bar.X, staffTop - 2f, bar.X, staffBot + 2f);
                    canvas.StrokeSize = 4f;
                    canvas.DrawLine(bar.X + 4f, staffTop - 2f, bar.X + 4f, staffBot + 2f);
                }
                else
                {
                    // Single bar
                    canvas.StrokeSize = 2f;
                    canvas.DrawLine(bar.X, staffTop - 2f, bar.X, staffBot + 2f);
                }
            }
            canvas.StrokeSize = 1f;

            // Beam pre-pass: identify beam groups using pre-computed note X positions
            var beamGroups = ComputeBeamGroups(notes, noteLayouts, staffTop, staffMid);

            // Draw notes and collect stem tips for beaming
            var beamStemTips = new Dictionary<int, (float x, float y, Color color, NoteDuration dur)>();
            var accHistory = new Dictionary<(char, int), Accidental>();
            var barCancelledAccidentals = new HashSet<(char, int)>();

            byte fadeAlpha = (byte)Math.Clamp((int)(alpha * 255), 0, 255);

            for (int i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                var layout = noteLayouts[i];
                var state = (states.Length > i) ? states[i] : V2NoteState.Pending;

                // Track accidental cancellations
                if (!note.IsRest)
                {
                    var key = (note.Letter, note.Octave);
                    if (!IsAccidentalInKeySig(note) && IsNoteInKeySig(note))
                        barCancelledAccidentals.Add(key);
                }

                if (note.IsRest)
                {
                    DrawRest(canvas, note.Duration, layout.X, staffTop, staffMid, staffBot, ink, state, fadeAlpha);
                }
                else
                {
                    float ny = NoteY(note, staffTop, staffMid);

                    bool isBeamed = beamGroups.ContainsKey(i);
                    bool? forceStemUp = isBeamed ? beamGroups[i].stemUp : (bool?)null;

                    DrawNote(canvas, note.Duration, layout.X, ny, staffTop, staffBot, ink, state, fadeAlpha,
                             forceStemUp, isBeamed,
                             out float stemTipX, out float stemTipY);

                    if (isBeamed)
                        beamStemTips[i] = (stemTipX, stemTipY, GetNoteColor(state, ink, fadeAlpha), note.Duration);

                    DrawLedgerLines(canvas, note, layout.X, staffTop, staffBot, ink, fadeAlpha);
                    DrawAccidental(canvas, note, layout.X, ny, ink, accHistory, barCancelledAccidentals, fadeAlpha);

                    var nameDisplay = _session.V2NoteNameDisplay;
                    bool showName = nameDisplay == "All notes"
                        || (nameDisplay == "Current only" && state == V2NoteState.Current);
                    if (showName)
                        DrawNoteName(canvas, note, layout.X, ny, staffTop, staffBot, ink, fadeAlpha);
                }
            }

            // Draw beams using pre-computed stem positions
            DrawBeams(canvas, beamGroups, beamStemTips);
        }

        /// <summary>
        /// Identifies beam groups from notes and their pre-computed X positions.
        /// Returns a dictionary mapping note index to (groupId, stemUp).
        /// 
        /// In 4/4 time, beam groups are formed within half-beat boundaries:
        /// - Beat 0.0-0.5, 0.5-1.0, 1.0-1.5, 1.5-2.0, etc.
        /// - Don't cross beat boundaries
        /// - Don't cross measure boundaries
        /// - Only beam consecutive eighth/sixteenth notes (no rests between)
        /// </summary>
        private Dictionary<int, (int groupId, bool stemUp)> ComputeBeamGroups(
            List<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            float staffTop,
            float staffMid)
        {
            var beamGroup = new Dictionary<int, (int groupId, bool stemUp)>();
            int groupId = 0;
            int i = 0;

            while (i < notes.Count)
            {
                var n = notes[i];
                double pos = n.BeatPosition ?? 0.0;

                // Only beam eighth or sixteenth non-rest notes
                bool isBeamable = !n.IsRest
                    && (n.Duration == NoteDuration.Eighth || n.Duration == NoteDuration.Sixteenth);

                if (!isBeamable)
                {
                    i++;
                    continue;
                }

                // Find the half-beat group this note belongs to
                // In 4/4: 0.0-0.5, 0.5-1.0, 1.0-1.5, 1.5-2.0, 2.0-2.5, 2.5-3.0, 3.0-3.5, 3.5-4.0
                double halfBeatStart = Math.Floor(pos * 2.0) / 2.0;
                double halfBeatEnd = halfBeatStart + 0.5;

                int currentMeasure = n.MeasureIndex ?? 0;

                // Collect consecutive beamable notes within the same half-beat group and measure
                var groupIndices = new List<int>();
                int j = i;

                while (j < notes.Count)
                {
                    var nj = notes[j];
                    double pj = nj.BeatPosition ?? halfBeatEnd;

                    // Stop if we hit a rest or unbeamable duration
                    if (nj.IsRest || (nj.Duration != NoteDuration.Eighth && nj.Duration != NoteDuration.Sixteenth))
                        break;

                    // Stop if we cross into a different measure
                    if ((nj.MeasureIndex ?? currentMeasure) != currentMeasure)
                        break;

                    // Stop if this note starts outside our half-beat group
                    if (pj >= halfBeatEnd + 1e-6)
                        break;

                    groupIndices.Add(j);
                    j++;
                }

                // Form beam group if 2+ notes
                if (groupIndices.Count >= 2)
                {
                    // Stem direction: note farthest from midline decides
                    bool stemUp = false;
                    float maxDist = -1f;
                    foreach (var gi in groupIndices)
                    {
                        float ny = NoteY(notes[gi], staffTop, staffMid);
                        float dist = Math.Abs(ny - staffMid);
                        if (dist > maxDist) { maxDist = dist; stemUp = ny > staffMid; }
                    }

                    foreach (var gi in groupIndices)
                        beamGroup[gi] = (groupId, stemUp);
                    groupId++;
                }

                i = j > i ? j : i + 1;  // Advance past the group or move to next note
            }

            return beamGroup;
        }

        /// <summary>
        /// Draws beam bars for all beam groups using pre-computed stem tip positions.
        /// </summary>
        private void DrawBeams(
            ICanvas canvas,
            Dictionary<int, (int groupId, bool stemUp)> beamGroups,
            Dictionary<int, (float x, float y, Color color, NoteDuration dur)> beamStemTips)
        {
            const float BeamThick = 4f;
            const float BeamGap = 3f;

            // Build per-group stem tip lists
            var groupTips = new Dictionary<int, List<(int noteIdx, float x, float y, Color color, NoteDuration dur)>>();
            foreach (var kv in beamGroups)
            {
                int ni = kv.Key;
                int gid = kv.Value.groupId;
                if (!beamStemTips.TryGetValue(ni, out var tip)) continue;
                if (!groupTips.TryGetValue(gid, out var list))
                    groupTips[gid] = list = new();
                list.Add((ni, tip.x, tip.y, tip.color, tip.dur));
            }

            foreach (var gkv in groupTips)
            {
                var tips = gkv.Value;
                if (tips.Count < 2) continue;

                tips.Sort((a, b) => a.x.CompareTo(b.x));

                bool grpStemUp = beamGroups[tips[0].noteIdx].stemUp;

                // Beam slope from first to last stem tip
                float x0 = tips[0].x, y0 = tips[0].y;
                float x1 = tips[^1].x, y1 = tips[^1].y;

                // Cap slope to 1.5 staff spaces for natural melodic contour
                float maxTilt = _layout.Sls * 1.5f;
                if (Math.Abs(y1 - y0) > maxTilt)
                    y1 = y0 + Math.Sign(y1 - y0) * maxTilt;

                float BeamY(float x) => x0 == x1 ? y0 : y0 + (y1 - y0) * ((x - x0) / (x1 - x0));

                var beamColor = tips.FirstOrDefault(t => t.color != default).color;
                if (beamColor == default) beamColor = ApplyAlpha(Colors.Black, 220);

                canvas.SaveState();
                canvas.StrokeColor = beamColor;

                // Primary beam (eighth notes)
                canvas.StrokeSize = BeamThick;
                canvas.DrawLine(x0, y0, x1, y1);

                // Secondary beam (sixteenth notes)
                float secondaryOffset = grpStemUp ? (BeamThick + BeamGap) : -(BeamThick + BeamGap);
                for (int ti = 0; ti < tips.Count; ti++)
                {
                    if (tips[ti].dur != NoteDuration.Sixteenth) continue;

                    int segStart = ti;
                    while (ti + 1 < tips.Count && tips[ti + 1].dur == NoteDuration.Sixteenth)
                        ti++;
                    int segEnd = ti;

                    float sx0 = tips[segStart].x;
                    float sx1 = tips[segEnd].x;

                    if (segStart == segEnd)
                    {
                        float halfSlot = (x1 - x0) / Math.Max(tips.Count - 1, 1) * 0.5f;
                        if (segStart == 0)
                            sx1 = sx0 + halfSlot;
                        else
                            sx0 = sx1 - halfSlot;
                    }

                    canvas.StrokeSize = BeamThick;
                    canvas.DrawLine(
                        sx0, BeamY(sx0) + secondaryOffset,
                        sx1, BeamY(sx1) + secondaryOffset);
                }

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
                    // Stem direction: notes ABOVE middle line stem DOWN; notes BELOW middle stem UP
                    float staffMiddle = staffTop + (staffBot - staffTop) * 0.5f;
                    bool stemUp = forceStemUp ?? (y >= staffMiddle);  // note at/below middle → stem up
                    float stemX  = stemUp ? x + r : x - r;
                    float stemY  = stemUp ? y - r * 0.75f : y + r * 0.75f;
                    // Shorten stems for beamed notes so they terminate near the beam
                    // baseline and do not project past the beam when beam bars are drawn.
                    float actualStemLen = isBeamed ? _layout.StemLen * 0.6f : _layout.StemLen;
                    float stemEnd = stemUp ? stemY - actualStemLen : stemY + actualStemLen;
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

        private void DrawAccidental(ICanvas canvas, GeneratedNote note, float noteX, float y,
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
                float yAdjust  = isFlat ? 0.5f : 0.38f;  // optical center of glyph

                // Position accidental to the left of the notehead
                // noteX is the center of the notehead, so place accidental left of it
                float accidentalX = noteX - _layout.NoteHeadR - 4f - symW * 0.5f;
                float yTop = y - symH * yAdjust;

                canvas.FontColor = ApplyAlpha(ink, fadeAlpha);
                canvas.FontSize  = fontSize;
                canvas.DrawString(glyph, accidentalX, yTop, symW, symH,
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

        private float DrawKeySignature(ICanvas canvas, float staffTop, float staffMid, Color ink, float safeLeft)
        {
            float startX  = safeLeft + 54f;
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

            // Flat positions: Bb Eb Ab Db Gb Cb Fb
            // Bb is on middle line (B4), Eb top space (E5), Ab 2nd space (A4), etc.
            int[] flatSteps  = { 0, -3,  1, -2,  2, -1,  3 };  // Bb Eb Ab Db Gb Cb Fb
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
                float yAdjust = useFlats ? 0.5f : 0.38f;  // optical center of glyph within bounding box
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
