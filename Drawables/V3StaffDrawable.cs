using System.Diagnostics;
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
        private const float RightMargin     = 36f;

        // V3 uniform spacing constants
        private const float ItemSpacing      = 46f;   // fixed horizontal spacing per item (reduced from 52)
        private const float AccidentalWidth  = 40f;   // extra width reserved when a note has an accidental
        private const float BarLeftPadding      = 16f;   // minimum space from bar line to notehead/stem
        private const float BarStemClearance    = 8f;    // extra gap from bar to stem-ward edge of notehead
        private const float MinStemBarGap       = 6f;    // minimum px between stem column and a bar line
        private const float MeasureStartExtraPad = 6f;   // added when a note starts exactly on a bar beat
        private const float StaffStartExtraPad   = 4f;   // first note in staff after time signature
        private const float BarRightPadding  = 20f;   // minimum space from last note to final bar line

        /// <summary>Padding before the layout right limit (dp).</summary>
        private const float V3LayoutRightPad = 2f;

        /// <summary>
        /// Subtract this from <see cref="ISafeAreaService"/> Right inset before computing SafeRight (dp).
        /// Android SafeInsetRight often includes empty margin before the cutout (~11mm / ~42dp).
        /// Increase (e.g. 40–50) to stretch the staff toward the view edge; 0 = use full inset.
        /// </summary>
        private const float V3RelaxCutoutInsetRightDp = 42f;

        private float _leftMargin = 115f;
        private float LeftMargin => _leftMargin;
        private StaffHeaderMetrics _headerMetrics;

        private const float AccidentalRightGap = 0.2f;
        private const float DoubleBarExtraWidth  = 4f;
        private const float BodyAccidentalRaise  = 0.32f; // staff-space fraction — lifts body glyphs

        // Notehead ellipse is drawn with height = NoteHeadR * NoteHeadHeightFactor.
        private const float NoteHeadHeightFactor     = 1.5f;
        private const float BeginnerNoteHeadSpaceRatio = 0.9f; // target: 90% of staff-space height
        private const float CompactNoteHeadRRatio    = 0.32f;
        private const float CompactStemLenRatio      = 2.15f;
        private const float CompactAccidentalRefPx   = 26f;
        private const float CompactRestScale         = 0.72f;

        /// <summary>Child levels 1–30 use enlarged notation scaled from staff-space height.</summary>
        private bool UseBeginnerNotationScale =>
            _session.ChildLevel > 0 && _session.ChildLevel <= 30;

        private float AccidentalRightGapPx => AccidentalRightGap * _layout.GlyphScale;

        private float AccidentalSymbolWidth()
            => CompactAccidentalRefPx * (_layout.Sls / 12f) * _layout.GlyphScale;

        private static bool IsFlatBodyAccidental(Accidental acc)
            => acc == Accidental.Flat || acc == Accidental.DoubleFlat;

        /// <summary>Draw box width for a body accidental tucked beside the notehead.</summary>
        private float BodyAccidentalDrawWidth(bool isFlat)
            => isFlat
                ? AccidentalSymbolWidth() * 0.78f
                : AccidentalSymbolWidth() * 0.82f;

        private float NoteHeadLeft(float centerX) => centerX - _layout.NoteHeadR;

        private float AccidentalBoxRight(float noteCenterX)
            => NoteHeadLeft(noteCenterX) - AccidentalRightGapPx;

        /// <summary>Left edge of accidental draw box; right edge is <see cref="AccidentalRightGap"/> before notehead.</summary>
        private float AccidentalBoxLeft(float noteCenterX, bool isFlat)
            => AccidentalBoxRight(noteCenterX) - BodyAccidentalDrawWidth(isFlat);

        /// <summary>Leftmost ink edge of a note/rest group (accidental or notehead).</summary>
        private float NoteGroupLeft(float centerX, bool isRest, bool hasAcc, bool isFlatAcc)
            => hasAcc ? AccidentalBoxLeft(centerX, isFlatAcc) : centerX - NoteHalfWidth(isRest);

        private float NoteGroupLeftFromLayout(NoteLayout layout)
            => NoteGroupLeft(layout.X, layout.IsRest, layout.HasAccidental, layout.AccidentalIsFlat);

        private static float BarLineRightEdge(BarLayout bar)
            => bar.X + (bar.IsDouble ? DoubleBarExtraWidth : 0f);

        private static int GetMeasureIndexForBeat(double relBeat, IReadOnlyList<double> sortedBarBeatsRel)
        {
            for (int m = 0; m < sortedBarBeatsRel.Count; m++)
            {
                if (relBeat < sortedBarBeatsRel[m] - 1e-6)
                    return m;
            }

            return sortedBarBeatsRel.Count;
        }

        /// <summary>Distance from note center to the left edge of its drawable group.</summary>
        private float NoteCenterLeftReach(bool isRest, bool hasAcc, bool isFlatAcc)
            => hasAcc
                ? _layout.NoteHeadR + AccidentalRightGapPx + BodyAccidentalDrawWidth(isFlatAcc)
                : NoteHalfWidth(isRest);

        /// <summary>Distance from note center to its right drawable edge.</summary>
        private float NoteCenterTrailingReach(bool isRest)
            => isRest ? _layout.NoteHeadR * 1.05f : _layout.NoteHeadR + Math.Max(5f, 3f * _layout.GlyphScale);

        private void SyncAccidentalX(NoteLayout[] noteLayouts, int index)
        {
            if (noteLayouts[index].HasAccidental)
            {
                noteLayouts[index].AccidentalX = AccidentalBoxLeft(
                    noteLayouts[index].X, noteLayouts[index].AccidentalIsFlat);
            }
        }

        /// <summary>Minimum clear ink-to-ink gap between consecutive items (px).</summary>
        private const float MinInkGap = 8f;

        /// <summary>Active ink gap for the current plan/draw pass (may shrink when content overflows).</summary>
        private float _planInkGap = MinInkGap;

        /// <summary>Left anchor used while <see cref="PlanHorizontalLayout"/> runs.</summary>
        private float _planStaffLeftMargin;

        /// <summary>When false, first-note anchor is clef-only (lower staff in two-staff layout).</summary>
        private bool _planUseFullHeader;

        private float MinNoteGap(bool isRest) => _planInkGap;

        private float NoteHalfWidth(bool isRest)
            => isRest ? _layout.NoteHeadR * 0.95f : _layout.NoteHeadR;

        /// <summary>Right edge after note/rest center (stem-ward for notes).</summary>
        private float NoteTrailingRight(bool isRest, float centerX)
            => centerX + NoteCenterTrailingReach(isRest);

        private float NoteTrailingRight(GeneratedNote note, float centerX)
            => NoteTrailingRight(note.IsRest, centerX);

        /// <summary>Minimum center X after <paramref name="prevRight"/> for the next item.</summary>
        private float MinCenterAfterPrevRight(float prevRight, bool isRest, bool hasAcc, bool isFlatAcc, float? inkGap = null)
            => prevRight + (inkGap ?? _planInkGap) + NoteCenterLeftReach(isRest, hasAcc, isFlatAcc);

        private float KeySigGlyphWidth() => AccidentalSymbolWidth();

        /// <summary>
        /// Horizontal advance between key-sig symbols (tight cluster).
        /// Draw box width stays <see cref="KeySigGlyphWidth"/>; slot matches V2 (~14px at sls=12).
        /// </summary>
        private float KeySigSymbolSlot()
        {
            float symW = KeySigGlyphWidth();
            return Math.Max(_layout.Sls * 1.17f, symW * 0.38f);
        }

        /// <summary>Total horizontal span of <paramref name="accCount"/> key-sig symbols (tight slots + last glyph width).</summary>
        private float KeySigDrawnWidth(int accCount)
        {
            if (accCount <= 0) return 0f;
            return (accCount - 1) * KeySigSymbolSlot() + KeySigGlyphWidth();
        }

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
            public float AccidentalX;    // left edge of accidental draw box
            public bool HasAccidental;   // whether this note needs accidental space
            public bool AccidentalIsFlat; // flat vs sharp/natural box metrics
            public bool IsRest;
        }

        /// <summary>
        /// Pre-computed bar line positions for a staff.
        /// </summary>
        private struct BarLayout
        {
            public float X;              // horizontal position of bar line
            public bool IsDouble;        // true for final double bar
        }

        /// <summary>One measure's beat span and the note indices that fall inside it.</summary>
        private struct MeasureSegment
        {
            public double StartBeat;
            public double EndBeat;
            public List<int> NoteIndices;
        }

        private struct V3Layout
        {
            public float Sls;          // pixels per staff space (line spacing)
            public float HS;           // half-space = Sls/2
            public float NoteHeadR;    // notehead radius
            public float StemLen;      // stem length
            /// <summary>1.0 at compact size; &gt;1 when level ≤30 enlarges notation.</summary>
            public float GlyphScale;
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
            sls = Math.Clamp(sls, 6f, 12f);
            float hs = sls / 2f;

            float compactNoteHeadR = sls * CompactNoteHeadRRatio;
            float noteHeadR = UseBeginnerNotationScale
                ? sls * BeginnerNoteHeadSpaceRatio / NoteHeadHeightFactor
                : compactNoteHeadR;
            float glyphScale = noteHeadR / compactNoteHeadR;
            float stemLen    = sls * CompactStemLenRatio * glyphScale;

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
                GlyphScale  = glyphScale,
                UpperTop    = upperTop + vOffset,
                UpperMid    = upperMid + vOffset,
                UpperBot    = upperBot + vOffset,
                LowerTop    = lowerTop + vOffset,
                LowerMid    = lowerMid + vOffset,
                LowerBot    = lowerBot + vOffset,
                TotalHeight = contentH + vOffset
            };
        }

        private List<MeasureSegment> BuildMeasureSegments(
            List<GeneratedNote> notes,
            List<double> sortedBarBeats,
            double beatOrigin,
            double totalBeats)
        {
            var segments = new List<MeasureSegment>();
            double segStart = 0;

            foreach (var barBeat in sortedBarBeats)
            {
                segments.Add(new MeasureSegment
                {
                    StartBeat = segStart,
                    EndBeat = barBeat,
                    NoteIndices = new List<int>()
                });
                segStart = barBeat;
            }

            segments.Add(new MeasureSegment
            {
                StartBeat = segStart,
                EndBeat = Math.Max(totalBeats, segStart + 1e-3),
                NoteIndices = new List<int>()
            });

            for (int i = 0; i < notes.Count; i++)
            {
                double rel = (notes[i].BeatPosition ?? 0.0) - beatOrigin;
                for (int m = 0; m < segments.Count; m++)
                {
                    bool isLast = m == segments.Count - 1;
                    if (rel >= segments[m].StartBeat - 1e-6
                        && (isLast || rel < segments[m].EndBeat - 1e-6))
                    {
                        segments[m].NoteIndices.Add(i);
                        break;
                    }
                }
            }

            return segments;
        }

        private static List<int> SortIndicesByBeat(
            List<GeneratedNote> notes,
            IReadOnlyList<int> indices,
            double beatOrigin)
        {
            return indices
                .OrderBy(i => (notes[i].BeatPosition ?? 0.0) - beatOrigin)
                .ThenBy(i => i)
                .ToList();
        }

        private float ComputeMeasureMinWidth(
            List<GeneratedNote> notes,
            MeasureSegment segment,
            double beatOrigin)
        {
            if (segment.NoteIndices.Count == 0)
                return BarLeftPadding + 16f;

            var sorted = SortIndicesByBeat(notes, segment.NoteIndices, beatOrigin);
            float width = BarLeftPadding;

            for (int k = 0; k < sorted.Count; k++)
            {
                int i = sorted[k];
                var note = notes[i];
                bool hasAcc = !note.IsRest && WillReserveAccidentalSpace(note);
                bool isFlatAcc = hasAcc && IsFlatBodyAccidental(note.Accidental);

                if (k == 0)
                {
                    double relBeat = (note.BeatPosition ?? 0.0) - beatOrigin - segment.StartBeat;
                    float startPad = BarStemClearance + (relBeat < 1e-6 ? MeasureStartExtraPad : 0f);
                    width += NoteCenterLeftReach(note.IsRest, hasAcc, isFlatAcc) + startPad;
                }
                else
                {
                    width += MinNoteGap(note.IsRest) + NoteCenterLeftReach(note.IsRest, hasAcc, isFlatAcc);
                }
            }

            int lastIdx = sorted[^1];
            width += NoteCenterTrailingReach(notes[lastIdx].IsRest) + BarLeftPadding * 0.5f;
            return width;
        }

        private static float[] AllocateMeasureWidths(
            List<MeasureSegment> segments,
            float[] minWidths,
            float availableWidth)
        {
            int n = segments.Count;
            if (n == 0)
                return Array.Empty<float>();

            var widths = new float[n];
            float equal = availableWidth / n;

            for (int i = 0; i < n; i++)
                widths[i] = equal;

            float sumMin = 0f;
            for (int i = 0; i < n; i++)
                sumMin += minWidths[i];

            if (sumMin > availableWidth)
            {
                // Keep each measure at its packed minimum; whole-staff horizontal scale handles overflow.
                for (int i = 0; i < n; i++)
                    widths[i] = minWidths[i];
            }
            else
            {
                float deficit = 0f;
                float surplus = 0f;
                for (int i = 0; i < n; i++)
                {
                    if (minWidths[i] > equal)
                        deficit += minWidths[i] - equal;
                    else
                        surplus += equal - minWidths[i];
                }

                if (deficit > 0f && surplus > 0f)
                {
                    float take = Math.Min(deficit, surplus);
                    float takeRatio = take / deficit;
                    float giveRatio = take / surplus;
                    for (int i = 0; i < n; i++)
                    {
                        if (minWidths[i] > equal)
                            widths[i] = equal + (minWidths[i] - equal) * takeRatio;
                        else
                            widths[i] = equal - (equal - minWidths[i]) * giveRatio;
                    }
                }
            }

            return widths;
        }

        /// <summary>Left-to-last-right span when notes are packed at <see cref="MinInkGap"/>.</summary>
        private float ComputeMinimumPackedSpan(IReadOnlyList<GeneratedNote> notes, IReadOnlyList<int> sorted)
        {
            if (sorted.Count == 0)
                return 0f;

            float prevRight = float.NegativeInfinity;
            float lastRight = 0f;

            for (int k = 0; k < sorted.Count; k++)
            {
                int i = sorted[k];
                var note = notes[i];
                bool hasAcc = !note.IsRest && WillReserveAccidentalSpace(note);
                bool isFlat = hasAcc && IsFlatBodyAccidental(note.Accidental);

                float center;
                if (k == 0)
                {
                    center = NoteCenterLeftReach(note.IsRest, hasAcc, isFlat);
                }
                else
                {
                    center = MinCenterAfterPrevRight(prevRight, note.IsRest, hasAcc, isFlat);
                }

                lastRight = NoteTrailingRight(note, center);
                prevRight = lastRight;
            }

            return lastRight;
        }

        private void CompressMeasureNoteSpan(
            List<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            IReadOnlyList<int> sortedIndices,
            float targetRight)
        {
            if (sortedIndices.Count < 2)
                return;

            int firstIdx = sortedIndices[0];
            var firstLayout = noteLayouts[firstIdx];
            float firstGroupLeft = NoteGroupLeft(
                firstLayout.X, firstLayout.IsRest, firstLayout.HasAccidental, firstLayout.AccidentalIsFlat);
            float firstX = firstLayout.X;
            float lastRight = NoteTrailingRight(notes[sortedIndices[^1]], noteLayouts[sortedIndices[^1]].X);
            float span = lastRight - firstGroupLeft;
            if (span <= 1f || lastRight <= targetRight)
                return;

            float minSpan = ComputeMinimumPackedSpan(notes, sortedIndices);
            float available = targetRight - firstGroupLeft;
            float fitScale = available < minSpan
                ? (span > 1f ? available / span : 1f)
                : ComputeMeasureFitScale(available, span, minSpan);

            UniformScaleMeasureIndices(notes, noteLayouts, sortedIndices, firstX, fitScale);
        }

        /// <summary>
        /// Forward pass on a sorted index list — each group's left edge must clear the
        /// previous group's trailing right by at least <paramref name="inkGap"/>.
        /// </summary>
        private void EnforceGroupOrderSpacingOnIndices(
            IReadOnlyList<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            IReadOnlyList<int> sorted,
            float inkGap)
        {
            float prevRight = float.NegativeInfinity;
            for (int k = 0; k < sorted.Count; k++)
            {
                int i = sorted[k];
                var note = notes[i];
                bool isRest = noteLayouts[i].IsRest;
                bool hasAcc = noteLayouts[i].HasAccidental;
                bool isFlat = noteLayouts[i].AccidentalIsFlat;

                if (prevRight > float.NegativeInfinity)
                {
                    float minLeft = prevRight + inkGap;
                    float curLeft = NoteGroupLeft(noteLayouts[i].X, isRest, hasAcc, isFlat);
                    if (curLeft < minLeft)
                    {
                        noteLayouts[i].X += minLeft - curLeft;
                        SyncAccidentalX(noteLayouts, i);
                    }
                }

                prevRight = NoteTrailingRight(note, noteLayouts[i].X);
            }
        }

        /// <summary>Beat-order spacing using group left/trailing edges (never moves items backward).</summary>
        private void EnforceGroupOrderSpacing(
            IReadOnlyList<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            double beatOrigin,
            float inkGap = MinInkGap)
        {
            if (notes.Count == 0 || noteLayouts.Length == 0)
                return;

            var order = Enumerable.Range(0, notes.Count)
                .OrderBy(i => (notes[i].BeatPosition ?? 0.0) - beatOrigin)
                .ThenBy(i => i)
                .ToList();

            EnforceGroupOrderSpacingOnIndices(notes, noteLayouts, order, inkGap);
        }

        private void EnforceMeasureNoteGaps(
            IReadOnlyList<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            IReadOnlyList<int> sorted)
            => EnforceGroupOrderSpacingOnIndices(notes, noteLayouts, sorted, _planInkGap);

        private void ResolveMeasureNoteSpacing(
            List<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            IReadOnlyList<int> sorted,
            float innerLeft,
            float innerRight,
            float barLineX)
        {
            float maxTrailing = barLineX - BarLeftPadding;
            float compressTarget = Math.Min(innerRight - BarLeftPadding * 0.5f, maxTrailing);

            EnforceMeasureNoteGaps(notes, noteLayouts, sorted);
            CompressMeasureNoteSpan(notes, noteLayouts, sorted, compressTarget);
            EnforceMeasureNoteGaps(notes, noteLayouts, sorted);
            ClampMeasureTrailingBeforeBar(notes, noteLayouts, sorted, maxTrailing);
        }

        /// <summary>Ensures no note/rest in a measure extends past the following bar line.</summary>
        private void ClampMeasureTrailingBeforeBar(
            IReadOnlyList<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            IReadOnlyList<int> sorted,
            float maxTrailingRight)
        {
            if (sorted.Count == 0)
                return;

            int lastIdx = sorted[^1];
            float trailing = NoteTrailingRight(notes[lastIdx], noteLayouts[lastIdx].X);
            if (trailing <= maxTrailingRight)
                return;

            int firstIdx = sorted[0];
            var firstLayout = noteLayouts[firstIdx];
            float firstGroupLeft = NoteGroupLeft(
                firstLayout.X, firstLayout.IsRest, firstLayout.HasAccidental, firstLayout.AccidentalIsFlat);
            float firstX = firstLayout.X;
            float span = trailing - firstGroupLeft;
            float available = maxTrailingRight - firstGroupLeft;
            if (span <= 1f || available <= 0f)
                return;

            float minSpan = ComputeMinimumPackedSpan(notes, sorted);
            float fitScale = available < minSpan
                ? (span > 1f ? available / span : 1f)
                : ComputeMeasureFitScale(available, span, minSpan);

            UniformScaleMeasureIndices(notes, noteLayouts, sorted, firstX, fitScale);
            EnforceMeasureNoteGaps(notes, noteLayouts, sorted);
        }

        private void UniformScaleMeasureIndices(
            IReadOnlyList<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            IReadOnlyList<int> sorted,
            float anchorCenterX,
            float fitScale)
        {
            for (int k = 0; k < sorted.Count; k++)
            {
                int i = sorted[k];
                float rel = noteLayouts[i].X - anchorCenterX;
                noteLayouts[i].X = anchorCenterX + rel * fitScale;
                SyncAccidentalX(noteLayouts, i);
            }
        }

        /// <summary>
        /// Uniform measure scale that fits trailing ink inside <paramref name="available"/> width.
        /// Avoids <see cref="Math.Clamp(Single,Single,Single)"/> when min packed span ≈ current span (fp).
        /// </summary>
        private static float ComputeMeasureFitScale(float available, float span, float minSpan)
        {
            if (span <= 1e-3f)
                return 1f;

            float raw = available / span;
            float minScale = minSpan / span;
            if (minScale > 1f + 1e-5f)
                return Math.Min(1f, raw);

            float floor = Math.Min(1f, minScale);
            return Math.Clamp(raw, floor, 1f);
        }

        /// <summary>Forward pass in beat order — enforces <see cref="MinInkGap"/> between items.</summary>
        private void EnforceGlobalBeatOrderSpacing(
            IReadOnlyList<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            double beatOrigin,
            float inkGap = MinInkGap)
            => EnforceGroupOrderSpacing(notes, noteLayouts, beatOrigin, inkGap);

        /// <summary>Packs notes at min ink gap when proportional beat spacing cannot fit.</summary>
        private void LayoutNotesSequentialInMeasure(
            List<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            IReadOnlyList<int> sorted,
            float innerLeft,
            float barLineX,
            bool isFirstMeasureOnStaff)
        {
            float prevRight = float.NegativeInfinity;
            for (int k = 0; k < sorted.Count; k++)
            {
                int i = sorted[k];
                var note = notes[i];
                bool hasAcc = !note.IsRest && WillReserveAccidentalSpace(note);
                bool isFlatAcc = hasAcc && IsFlatBodyAccidental(note.Accidental);

                float center;
                if (prevRight <= float.NegativeInfinity)
                {
                    center = innerLeft + NoteCenterLeftReach(note.IsRest, hasAcc, isFlatAcc);
                    if (isFirstMeasureOnStaff && k == 0)
                    {
                        float headerEdge = _planUseFullHeader
                            ? _headerMetrics.TimeSigRightRel
                            : _planStaffLeftMargin;
                        center = Math.Max(center, headerEdge + NoteCenterLeftReach(note.IsRest, hasAcc, isFlatAcc));
                    }
                }
                else
                {
                    center = MinCenterAfterPrevRight(prevRight, note.IsRest, hasAcc, isFlatAcc);
                }

                noteLayouts[i] = new NoteLayout
                {
                    X = center,
                    AccidentalX = center,
                    HasAccidental = hasAcc,
                    AccidentalIsFlat = isFlatAcc,
                    IsRest = note.IsRest
                };
                SyncAccidentalX(noteLayouts, i);
                prevRight = NoteTrailingRight(note, center);
            }

            float maxTrailing = barLineX - BarLeftPadding;
            ClampMeasureTrailingBeforeBar(notes, noteLayouts, sorted, maxTrailing);
        }

        private void LayoutNotesInMeasure(
            List<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            MeasureSegment segment,
            double beatOrigin,
            float measureLeft,
            float measureWidth,
            bool isFirstMeasureOnStaff)
        {
            if (segment.NoteIndices.Count == 0)
                return;

            var sorted = SortIndicesByBeat(notes, segment.NoteIndices, beatOrigin);
            float innerLeft = measureLeft + BarLeftPadding;
            float innerRight = measureLeft + measureWidth - BarLeftPadding * 0.5f;
            float barLineX = measureLeft + measureWidth;

            float minPackedSpan = ComputeMinimumPackedSpan(notes, sorted);
            if (minPackedSpan > measureWidth - BarLeftPadding * 0.5f)
            {
                LayoutNotesSequentialInMeasure(notes, noteLayouts, sorted, innerLeft, barLineX,
                    isFirstMeasureOnStaff);
                return;
            }

            double measureBeats = segment.EndBeat - segment.StartBeat;
            if (measureBeats < 1e-9)
                measureBeats = 1;

            var firstNote = notes[sorted[0]];
            var lastNote = notes[sorted[^1]];
            bool firstHasAcc = !firstNote.IsRest && WillReserveAccidentalSpace(firstNote);
            bool firstFlat = firstHasAcc && IsFlatBodyAccidental(firstNote.Accidental);
            float startCenter = innerLeft + NoteCenterLeftReach(firstNote.IsRest, firstHasAcc, firstFlat);
            float endCenter = innerRight - NoteCenterTrailingReach(lastNote.IsRest);
            float spread = Math.Max(8f, endCenter - startCenter);

            float prevRight = float.NegativeInfinity;
            for (int k = 0; k < sorted.Count; k++)
            {
                int i = sorted[k];
                var note = notes[i];
                double relBeat = (note.BeatPosition ?? 0.0) - beatOrigin - segment.StartBeat;
                // Tiny per-index offset prevents identical beat positions mapping to the same X.
                float frac = (float)Math.Clamp((relBeat + k * 1e-4) / measureBeats, 0.0, 1.0);

                bool hasAcc = !note.IsRest && WillReserveAccidentalSpace(note);
                bool isFlatAcc = hasAcc && IsFlatBodyAccidental(note.Accidental);

                float idealX = startCenter + frac * spread;

                if (relBeat < 1e-6)
                {
                    float onBarMin = measureLeft + BarLeftPadding + MeasureStartExtraPad + BarStemClearance
                                     + NoteCenterLeftReach(note.IsRest, hasAcc, isFlatAcc);
                    idealX = Math.Max(idealX, onBarMin);

                    if (isFirstMeasureOnStaff && k == 0)
                    {
                        float headerEdge = _planUseFullHeader
                            ? _headerMetrics.TimeSigRightRel
                            : _planStaffLeftMargin;
                        float headerMin = headerEdge + NoteCenterLeftReach(note.IsRest, hasAcc, isFlatAcc);
                        idealX = Math.Max(idealX, headerMin);
                    }
                }

                if (prevRight > float.NegativeInfinity)
                {
                    float minCenter = MinCenterAfterPrevRight(prevRight, note.IsRest, hasAcc, isFlatAcc);
                    idealX = Math.Max(idealX, minCenter);
                }

                noteLayouts[i] = new NoteLayout
                {
                    X = idealX,
                    AccidentalX = idealX,
                    HasAccidental = hasAcc,
                    AccidentalIsFlat = isFlatAcc,
                    IsRest = note.IsRest
                };
                SyncAccidentalX(noteLayouts, i);
                prevRight = NoteTrailingRight(note, noteLayouts[i].X);
            }

            ResolveMeasureNoteSpacing(notes, noteLayouts, sorted, innerLeft, innerRight,
                measureLeft + measureWidth);
        }

        /// <summary>
        /// Computes horizontal layout for a staff: X position for each note and bar line.
        /// Allocates width per measure (dense measures get more), then places notes proportionally
        /// within each measure's span.
        /// </summary>
        private (NoteLayout[] noteLayouts, BarLayout[] barLayouts, float totalWidth)
            PlanHorizontalLayout(
                List<GeneratedNote> notes,
                List<double> barBeats,
                float availableWidth,
                float staffLeftMargin,
                bool useFullHeaderAnchor,
                bool isFinalStaff,
                bool hasEndSingleBar)
        {
            _planStaffLeftMargin = staffLeftMargin;
            _planUseFullHeader = useFullHeaderAnchor;
            availableWidth = Math.Max(availableWidth, 64f);

            if (notes.Count == 0)
                return (Array.Empty<NoteLayout>(), Array.Empty<BarLayout>(), staffLeftMargin);

            double beatOrigin = GetStaffBeatOrigin(notes, barBeats);

            double totalBeats = 0.0;
            for (int i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                double relBeat = (note.BeatPosition ?? 0.0) - beatOrigin;
                totalBeats = Math.Max(totalBeats, relBeat + note.BeatDuration);
            }

            var sortedBarBeats = barBeats.Select(b => b - beatOrigin).OrderBy(b => b).ToList();
            var segments = BuildMeasureSegments(notes, sortedBarBeats, beatOrigin, totalBeats);

            _planInkGap = MinInkGap;
            var minWidths = new float[segments.Count];
            for (int m = 0; m < segments.Count; m++)
                minWidths[m] = ComputeMeasureMinWidth(notes, segments[m], beatOrigin);

            float sumMin = 0f;
            for (int m = 0; m < minWidths.Length; m++)
                sumMin += minWidths[m];
            if (sumMin > availableWidth && sumMin > 0f)
            {
                _planInkGap = MinInkGap * (availableWidth / sumMin);
                for (int m = 0; m < segments.Count; m++)
                    minWidths[m] = ComputeMeasureMinWidth(notes, segments[m], beatOrigin);
            }

            float[] measureWidths = AllocateMeasureWidths(segments, minWidths, availableWidth);

            var noteLayouts = new NoteLayout[notes.Count];
            var barList = new List<BarLayout>();
            float x = staffLeftMargin;

            for (int m = 0; m < segments.Count; m++)
            {
                LayoutNotesInMeasure(notes, noteLayouts, segments[m], beatOrigin, x, measureWidths[m], m == 0);
                x += measureWidths[m];

                if (m < sortedBarBeats.Count)
                    barList.Add(new BarLayout { X = x, IsDouble = false });
            }

            if (notes.Count > 0)
            {
                float lastNoteRight = float.NegativeInfinity;
                for (int i = 0; i < notes.Count; i++)
                {
                    float right = NoteTrailingRight(notes[i], noteLayouts[i].X);
                    if (right > lastNoteRight)
                        lastNoteRight = right;
                }

                float endBarX = Math.Max(x, lastNoteRight + BarRightPadding);
                barList.Add(new BarLayout { X = endBarX, IsDouble = isFinalStaff });
            }

            float totalWidth = barList.Count > 0
                ? barList.Max(b => b.X) + RightMargin
                : staffLeftMargin + availableWidth;

            LastComputedPxPerBeat = totalBeats > 0 ? availableWidth / (float)totalBeats : 42f;

            EnforceGlobalBeatOrderSpacing(notes, noteLayouts, beatOrigin, _planInkGap);
            ValidateLayout(notes, barBeats, noteLayouts, beatOrigin);

            return (noteLayouts, barList.ToArray(), totalWidth);
        }

        /// <summary>
        /// Validates musical measure structure and note spacing.
        /// Logs warnings for incorrect measure durations or overlapping notes.
        /// </summary>
        private void ValidateLayout(List<GeneratedNote> notes, List<double> barBeats, NoteLayout[] noteLayouts, double beatOrigin)
        {
            if (notes.Count == 0) return;

            try
            {
                var sortedBars = barBeats.Select(b => b - beatOrigin).OrderBy(b => b).ToList();
                var timeSig = _session.V2TimeSignature ?? "4/4";
                var tsParts = timeSig.Split('/');
                double expectedMeasureBeats = tsParts.Length == 2 && int.TryParse(tsParts[0], out int tsNum) ? tsNum : 4;

                for (int i = 0; i < sortedBars.Count; i++)
                {
                    double measureStart = i == 0 ? 0.0 : sortedBars[i - 1];
                    double measureEnd = sortedBars[i];
                    double measureBeats = measureEnd - measureStart;
                    double expectedBeats = expectedMeasureBeats;

                    // Allow tolerance for pickup measures and final incomplete measures
                    bool isFirstMeasure = i == 0 && measureStart == 0.0;
                    bool isLastMeasure = i == sortedBars.Count - 1;

                    if (!isFirstMeasure && !isLastMeasure && Math.Abs(measureBeats - expectedBeats) > 0.01)
                    {
                        V3Log($"[V3 Validation] Measure {i}: {measureBeats:F2} beats (expected {expectedBeats}), " +
                              $"start={measureStart:F2}, end={measureEnd:F2}");
                    }
                }

                double totalBeats = 0.0;
                for (int i = 0; i < notes.Count; i++)
                {
                    double rel = (notes[i].BeatPosition ?? 0.0) - beatOrigin;
                    totalBeats = Math.Max(totalBeats, rel + notes[i].BeatDuration);
                }

                var segments = BuildMeasureSegments(notes, sortedBars, beatOrigin, totalBeats);

                for (int m = 0; m < segments.Count; m++)
                {
                    bool isFirstMeasure = m == 0 && segments[m].StartBeat == 0.0;
                    bool isLastMeasure = m == segments.Count - 1;

                    double sumDuration = 0.0;
                    foreach (int idx in segments[m].NoteIndices)
                        sumDuration += notes[idx].BeatDuration;

                    if (!isFirstMeasure && !isLastMeasure
                        && Math.Abs(sumDuration - expectedMeasureBeats) > 0.01)
                    {
                        V3Log($"[V3 Validation] Measure {m}: note durations sum to {sumDuration:F2} " +
                              $"(expected {expectedMeasureBeats:F0}), beats {segments[m].StartBeat:F2}-{segments[m].EndBeat:F2}");
                    }
                }

                // Validate note spacing in beat order (not array index order)
                const float MinAllowedSpacing = MinInkGap - 0.5f; // compare against nominal gap
                var order = Enumerable.Range(0, notes.Count)
                    .OrderBy(i => (notes[i].BeatPosition ?? 0.0) - beatOrigin)
                    .ThenBy(i => i)
                    .ToList();
                for (int k = 1; k < order.Count; k++)
                {
                    int prev = order[k - 1];
                    int cur = order[k];
                    float prevRight = NoteTrailingRight(notes[prev], noteLayouts[prev].X);
                    float curLeft = NoteGroupLeft(
                        noteLayouts[cur].X,
                        noteLayouts[cur].IsRest,
                        noteLayouts[cur].HasAccidental,
                        noteLayouts[cur].AccidentalIsFlat);
                    float gap = curLeft - prevRight;
                    if (gap < MinAllowedSpacing)
                    {
                        V3Log($"[V3 Validation] Notes {prev} and {cur} too close: {gap:F1}px apart");
                    }
                }
            }
            catch (Exception ex)
            {
                V3Log($"[V3 Validation] Error: {ex.Message}");
            }
        }

        // ── IDrawable ─────────────────────────────────────────────────────────────
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var bg  = _theme.PanelBackgroundColor;
            var ink = _theme.ContrastingTextColor;

            canvas.FillColor = bg;
            canvas.FillRectangle(dirtyRect);

            try
            {
                DrawV3Content(canvas, dirtyRect, ink);
            }
            catch (Exception ex)
            {
                LogDrawFailure(ex);
                DrawV3ErrorMessage(canvas, dirtyRect, ink, ex);
            }
        }

        private void DrawV3Content(ICanvas canvas, RectF dirtyRect, Color ink)
        {
            UpperNotes ??= new();
            LowerNotes ??= new();
            UpperBarBeats ??= new();
            LowerBarBeats ??= new();

            if (dirtyRect.Width < 32f || dirtyRect.Height < 32f)
                return;

            if (UpperNotes.Count == 0 && LowerNotes.Count == 0)
            {
                canvas.FontColor = ink;
                canvas.FontSize  = 14;
                canvas.DrawString("V3 Mode — no notes generated yet",
                    dirtyRect.X + 8, dirtyRect.Y + dirtyRect.Height / 2f,
                    HorizontalAlignment.Left);
                return;
            }

            // Step 0: Safe horizontal bounds — see V3RelaxCutoutInsetRightDp to tune cutout margin.
            //   viewRight     = dirtyRect.X + dirtyRect.Width
            //   safeRight     = viewRight - effectiveRightInset  (hard clip for staff + notes)
            //   layoutLimit   = safeRight - V3LayoutRightPad     (notes/bars stretch to here)
            var insets = _safeArea?.GetSafeAreaInsets() ?? (0f, 0f, 0f, 0f);
            float viewRight = dirtyRect.X + dirtyRect.Width;
            float effectiveRightInset = Math.Max(0f, insets.Right - V3RelaxCutoutInsetRightDp);
            float safeLeft = dirtyRect.X + insets.Left;
            float safeRight = viewRight - effectiveRightInset;
            float layoutRightLimit = safeRight - V3LayoutRightPad;
            float safeWidth = layoutRightLimit - safeLeft;

            V3Log($"[V3] Canvas={dirtyRect.Width:F0}x{dirtyRect.Height:F0}, " +
                  $"Insets=L{insets.Left:F0},R{insets.Right:F0}, relaxR={V3RelaxCutoutInsetRightDp:F0}, " +
                  $"ViewRight={viewRight:F0}, SafeRight={safeRight:F0}, LayoutLimit={layoutRightLimit:F0}, " +
                  $"SafeWidth={safeWidth:F0}");

            // Step 1: Compute vertical layout
            ComputeLayout(dirtyRect.Height);
            _headerMetrics = ComputeHeaderMetrics(safeLeft);
            _leftMargin = _headerMetrics.LeftMargin;
            bool lowerClefOnlyStart = LowerNotes.Count > 0 && UpperNotes.Count > 0;
            float upperStaffMargin = _headerMetrics.LeftMargin;
            float lowerStaffMargin = lowerClefOnlyStart
                ? _headerMetrics.ClefOnlyLeftMargin
                : _headerMetrics.LeftMargin;

            float upperTop = _layout.UpperTop;
            float upperMid = _layout.UpperMid;
            float upperBot = _layout.UpperBot;
            float lowerTop = _layout.LowerTop;
            float lowerMid = _layout.LowerMid;
            float lowerBot = _layout.LowerBot;

            // Step 2: Compute horizontal layout for both staffs
            float upperUsableWidth = safeWidth - upperStaffMargin - RightMargin;
            float lowerUsableWidth = safeWidth - lowerStaffMargin - RightMargin;

            var (upperNoteLayouts, upperBarLayouts, upperTotalWidth) = PlanHorizontalLayout(
                UpperNotes, UpperBarBeats, upperUsableWidth,
                upperStaffMargin, useFullHeaderAnchor: true,
                isFinalStaff: LowerNotes.Count == 0,
                hasEndSingleBar: UpperHasEndBar && LowerNotes.Count > 0);
            float upperPlanInkGap = _planInkGap;

            var (lowerNoteLayouts, lowerBarLayouts, lowerTotalWidth) = PlanHorizontalLayout(
                LowerNotes, LowerBarBeats, lowerUsableWidth,
                lowerStaffMargin, useFullHeaderAnchor: !lowerClefOnlyStart,
                isFinalStaff: true,
                hasEndSingleBar: false);
            float lowerPlanInkGap = _planInkGap;

            float[] upperPreScaleX = CopyLayoutX(upperNoteLayouts);
            float[] lowerPreScaleX = CopyLayoutX(lowerNoteLayouts);

            // Step 3: Calculate horizontal compression if needed
            float upperContentWidth = upperTotalWidth - upperStaffMargin;
            float lowerContentWidth = lowerTotalWidth - lowerStaffMargin;
            float upperOverflow = upperContentWidth > upperUsableWidth
                ? upperUsableWidth / upperContentWidth : 1f;
            float lowerOverflow = lowerContentWidth > lowerUsableWidth
                ? lowerUsableWidth / lowerContentWidth : 1f;
            float horizontalScale = Math.Min(upperOverflow, lowerOverflow);
            float maxContentWidth = Math.Max(upperContentWidth, lowerContentWidth);

            float safeContentSpan = safeRight - safeLeft - upperStaffMargin - 4f;
            if (horizontalScale < 0.999f)
            {
                if (safeContentSpan > 0f)
                    horizontalScale = Math.Min(horizontalScale, safeContentSpan / maxContentWidth);
                horizontalScale = Math.Clamp(horizontalScale, 0.50f, 1f);
                V3Log($"[V3] Compression needed: contentWidth={maxContentWidth:F0}, " +
                      $"upperUsable={upperUsableWidth:F0}, lowerUsable={lowerUsableWidth:F0}, scale={horizontalScale:F3}");

                ApplyHorizontalScale(upperNoteLayouts, upperBarLayouts, horizontalScale, safeLeft, upperStaffMargin);
                ApplyHorizontalScale(lowerNoteLayouts, lowerBarLayouts, horizontalScale, safeLeft, lowerStaffMargin);
            }
            else
            {
                MapStaffLayoutToScreen(upperNoteLayouts, upperBarLayouts, safeLeft, upperStaffMargin);
                MapStaffLayoutToScreen(lowerNoteLayouts, lowerBarLayouts, safeLeft, lowerStaffMargin);
            }

            double upperBeatOrigin = GetStaffBeatOrigin(UpperNotes, UpperBarBeats);
            double lowerBeatOrigin = GetStaffBeatOrigin(LowerNotes, LowerBarBeats);
            bool compressed = horizontalScale < 0.999f;

            _planInkGap = Math.Max(4f, upperPlanInkGap * horizontalScale);
            EnforceMonotonicNoteSpacingInMeasures(UpperNotes, upperNoteLayouts, upperBarLayouts, UpperBarBeats, upperBeatOrigin);
            EnforceGlobalBeatOrderSpacing(UpperNotes, upperNoteLayouts, upperBeatOrigin, _planInkGap);
            _planInkGap = Math.Max(4f, lowerPlanInkGap * horizontalScale);
            EnforceMonotonicNoteSpacingInMeasures(LowerNotes, lowerNoteLayouts, lowerBarLayouts, LowerBarBeats, lowerBeatOrigin);
            EnforceGlobalBeatOrderSpacing(LowerNotes, lowerNoteLayouts, lowerBeatOrigin, _planInkGap);

            if (!compressed)
            {
                _planInkGap = upperPlanInkGap;
                EnforceGlobalBeatOrderSpacing(UpperNotes, upperNoteLayouts, upperBeatOrigin);
                _planInkGap = lowerPlanInkGap;
                EnforceGlobalBeatOrderSpacing(LowerNotes, lowerNoteLayouts, lowerBeatOrigin);
            }

            NudgeNotesClearOfBarlines(UpperNotes, upperNoteLayouts, upperBarLayouts, upperTop, upperMid, upperBot);
            NudgeNotesClearOfBarlines(LowerNotes, lowerNoteLayouts, lowerBarLayouts, lowerTop, lowerMid, lowerBot);
            ReconcileFinalBarLayout(upperNoteLayouts, upperBarLayouts);
            ReconcileFinalBarLayout(lowerNoteLayouts, lowerBarLayouts);

            ClampLayoutToSafeRight(upperNoteLayouts, upperBarLayouts, layoutRightLimit, safeLeft, upperStaffMargin);
            ClampLayoutToSafeRight(lowerNoteLayouts, lowerBarLayouts, layoutRightLimit, safeLeft, lowerStaffMargin);

            if (!compressed)
            {
                ExpandLayoutToFillSafeRight(upperNoteLayouts, upperBarLayouts, layoutRightLimit, safeLeft, upperStaffMargin);
                ExpandLayoutToFillSafeRight(lowerNoteLayouts, lowerBarLayouts, layoutRightLimit, safeLeft, lowerStaffMargin);
                _planInkGap = upperPlanInkGap;
                EnforceGlobalBeatOrderSpacing(UpperNotes, upperNoteLayouts, upperBeatOrigin);
                _planInkGap = lowerPlanInkGap;
                EnforceGlobalBeatOrderSpacing(LowerNotes, lowerNoteLayouts, lowerBeatOrigin);
            }

            ReconcileFinalBarLayout(upperNoteLayouts, upperBarLayouts);
            ReconcileFinalBarLayout(lowerNoteLayouts, lowerBarLayouts);
            PadLayoutGutterToLimit(upperNoteLayouts, upperBarLayouts, layoutRightLimit);
            PadLayoutGutterToLimit(lowerNoteLayouts, lowerBarLayouts, layoutRightLimit);
            ReconcileFinalBarLayout(upperNoteLayouts, upperBarLayouts);
            ReconcileFinalBarLayout(lowerNoteLayouts, lowerBarLayouts);
            AlignGrandStaffEndBars(upperBarLayouts, upperNoteLayouts, lowerBarLayouts, lowerNoteLayouts, layoutRightLimit);
            ClampLayoutToSafeRight(upperNoteLayouts, upperBarLayouts, layoutRightLimit, safeLeft, upperStaffMargin);
            ClampLayoutToSafeRight(lowerNoteLayouts, lowerBarLayouts, layoutRightLimit, safeLeft, lowerStaffMargin);
            SanitizeLayoutPositions(upperNoteLayouts, upperBarLayouts);
            SanitizeLayoutPositions(lowerNoteLayouts, lowerBarLayouts);

            LogV3StaffLayoutDiagnostics("Upper", UpperNotes, upperNoteLayouts, upperPreScaleX,
                upperTop, upperMid, upperBot);
            LogV3StaffLayoutDiagnostics("Lower", LowerNotes, lowerNoteLayouts, lowerPreScaleX,
                lowerTop, lowerMid, lowerBot);

            float upperContentRight = GetLayoutMaxRight(upperNoteLayouts, upperBarLayouts);
            float lowerContentRight = GetLayoutMaxRight(lowerNoteLayouts, lowerBarLayouts);
            float contentRight = Math.Max(upperContentRight, lowerContentRight);

            float upperEndBar = upperBarLayouts.Length > 0 ? upperBarLayouts[^1].X : 0f;
            float lowerEndBar = lowerBarLayouts.Length > 0 ? lowerBarLayouts[^1].X : 0f;
            V3Log($"[V3] ContentRight={contentRight:F0} (endBar U={upperEndBar:F0} L={lowerEndBar:F0}), " +
                  $"LayoutLimit={layoutRightLimit:F0}, gutter={(layoutRightLimit - contentRight):F1}, " +
                  $"pastSafeRight={(contentRight > safeRight ? contentRight - safeRight : 0f):F1}");

            // Step 4: Draw both staffs using pre-computed layouts
            DrawStaff(canvas, dirtyRect, ink, upperTop, upperMid, upperBot,
                      UpperNotes, UpperNoteStates, upperNoteLayouts, upperBarLayouts,
                      UpperBarBeats, upperBeatOrigin,
                      UpperAlpha, IsUpperActive, IsUpperActive ? ActiveNoteIndex : -1,
                      safeLeft, safeRight, layoutRightLimit, upperStaffMargin,
                      drawKeyAndTimeSig: true);

            DrawStaff(canvas, dirtyRect, ink, lowerTop, lowerMid, lowerBot,
                      LowerNotes, LowerNoteStates, lowerNoteLayouts, lowerBarLayouts,
                      LowerBarBeats, lowerBeatOrigin,
                      LowerAlpha, !IsUpperActive, !IsUpperActive ? ActiveNoteIndex : -1,
                      safeLeft, safeRight, layoutRightLimit, lowerStaffMargin,
                      drawKeyAndTimeSig: UpperNotes.Count == 0);
        }

        private static void SanitizeLayoutPositions(NoteLayout[] noteLayouts, BarLayout[] barLayouts)
        {
            for (int i = 0; i < noteLayouts.Length; i++)
            {
                if (!float.IsFinite(noteLayouts[i].X))
                    noteLayouts[i].X = 0f;
                if (!float.IsFinite(noteLayouts[i].AccidentalX))
                    noteLayouts[i].AccidentalX = noteLayouts[i].X;
            }

            for (int i = 0; i < barLayouts.Length; i++)
            {
                if (!float.IsFinite(barLayouts[i].X))
                    barLayouts[i].X = 0f;
            }
        }

        private static void LogDrawFailure(Exception ex)
        {
            var detail = ex;
            while (detail.InnerException != null)
                detail = detail.InnerException;
#if DEBUG
            Debug.WriteLine($"[MusicMate] [V3 Draw] {ex.GetType().Name}: {detail.Message}");
            Debug.WriteLine($"[MusicMate] [V3 Draw] {ex}");
            Utilities.Utils.Log($"[V3 Draw] {ex.GetType().Name}: {detail.Message}");
#endif
        }

        private static void DrawV3ErrorMessage(ICanvas canvas, RectF dirtyRect, Color ink, Exception ex)
        {
            canvas.FontColor = ink;
            canvas.FontSize  = 12;
            var detail = ex;
            while (detail.InnerException != null)
                detail = detail.InnerException;
            canvas.DrawString(
                $"Notation draw error: {detail.GetType().Name}",
                dirtyRect.X + 8, dirtyRect.Y + dirtyRect.Height * 0.45f,
                dirtyRect.Width - 16, 40f,
                HorizontalAlignment.Left, VerticalAlignment.Top);
        }

        /// <summary>Maps planned layout coords to screen space from <paramref name="staffLeftMargin"/>.</summary>
        private void MapStaffLayoutToScreen(
            NoteLayout[] noteLayouts, BarLayout[] barLayouts,
            float safeLeft, float staffLeftMargin, float scale = 1f)
        {
            for (int i = 0; i < noteLayouts.Length; i++)
            {
                float relativeX = noteLayouts[i].X - staffLeftMargin;
                noteLayouts[i].X = safeLeft + staffLeftMargin + (relativeX * scale);
                if (noteLayouts[i].HasAccidental)
                {
                    float relAcc = noteLayouts[i].AccidentalX - staffLeftMargin;
                    noteLayouts[i].AccidentalX = safeLeft + staffLeftMargin + (relAcc * scale);
                }
            }

            for (int i = 0; i < barLayouts.Length; i++)
            {
                float relativeX = barLayouts[i].X - staffLeftMargin;
                barLayouts[i].X = safeLeft + staffLeftMargin + (relativeX * scale);
            }
        }

        private void ApplyHorizontalScale(NoteLayout[] noteLayouts, BarLayout[] barLayouts,
                                          float scale, float safeLeft, float staffLeftMargin)
            => MapStaffLayoutToScreen(noteLayouts, barLayouts, safeLeft, staffLeftMargin, scale);

        /// <summary>
        /// Lines up only the final bar between staves; internal bars stay on each staff's own layout.
        /// </summary>
        private void AlignGrandStaffEndBars(
            BarLayout[] upperBarLayouts,
            NoteLayout[] upperNoteLayouts,
            BarLayout[] lowerBarLayouts,
            NoteLayout[] lowerNoteLayouts,
            float layoutRightLimit)
        {
            if (upperBarLayouts.Length == 0 || lowerBarLayouts.Length == 0)
                return;

            bool isDouble = upperBarLayouts[^1].IsDouble || lowerBarLayouts[^1].IsDouble;
            float maxBarX = layoutRightLimit - (isDouble ? DoubleBarExtraWidth : 0f);
            float endX = Math.Max(upperBarLayouts[^1].X, lowerBarLayouts[^1].X);
            endX = Math.Max(endX, RequiredEndBarX(upperNoteLayouts));
            endX = Math.Max(endX, RequiredEndBarX(lowerNoteLayouts));
            endX = Math.Min(endX, maxBarX);
            upperBarLayouts[^1].X = endX;
            lowerBarLayouts[^1].X = endX;
        }

        private float RequiredEndBarX(NoteLayout[] noteLayouts)
        {
            float lastNoteRight = float.NegativeInfinity;
            for (int i = 0; i < noteLayouts.Length; i++)
            {
                float right = NoteTrailingRight(noteLayouts[i].IsRest, noteLayouts[i].X);
                if (right > lastNoteRight)
                    lastNoteRight = right;
            }

            return lastNoteRight <= float.NegativeInfinity
                ? float.NegativeInfinity
                : lastNoteRight + BarRightPadding;
        }

        /// <summary>Moves note centers so ink and stems stay clear of bar lines on both sides.</summary>
        private void NudgeNotesClearOfBarlines(
            IReadOnlyList<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            BarLayout[] barLayouts,
            float staffTop, float staffMid, float staffBot)
        {
            if (notes.Count == 0 || barLayouts.Length == 0)
                return;

            for (int i = 0; i < notes.Count; i++)
            {
                if (notes[i].IsRest)
                    continue;

                float centerX = noteLayouts[i].X;
                float stemX = ComputeStemX(notes[i], centerX, staffTop, staffMid, staffBot);

                for (int b = 0; b < barLayouts.Length; b++)
                {
                    float barX = barLayouts[b].X;
                    float gap = stemX - barX;
                    if (Math.Abs(gap) < MinStemBarGap)
                    {
                        float shift = gap < 0f
                            ? barX - MinStemBarGap - stemX
                            : barX + MinStemBarGap - stemX;
                        if (shift > 0f)
                        {
                            noteLayouts[i].X += shift;
                            SyncAccidentalX(noteLayouts, i);
                            centerX = noteLayouts[i].X;
                            stemX = ComputeStemX(notes[i], centerX, staffTop, staffMid, staffBot);
                        }
                    }
                }
            }

            for (int b = 0; b < barLayouts.Length; b++)
            {
                float barX = barLayouts[b].X;
                for (int i = 0; i < notes.Count; i++)
                {
                    var layout = noteLayouts[i];
                    float centerX = layout.X;
                    float groupLeft = NoteGroupLeftFromLayout(layout);

                    if (centerX > barX + 0.5f)
                    {
                        float minLeft = barX + BarLeftPadding;
                        if (groupLeft < minLeft)
                        {
                            noteLayouts[i].X += minLeft - groupLeft;
                            SyncAccidentalX(noteLayouts, i);
                        }
                    }
                    // Notes before a bar: never nudge backward (left) — that encroaches on predecessors.
                }
            }
        }

        /// <summary>Ensures the final bar clears the last note without moving internal bar lines.</summary>
        private void ReconcileFinalBarLayout(NoteLayout[] noteLayouts, BarLayout[] barLayouts)
        {
            if (noteLayouts.Length == 0 || barLayouts.Length == 0)
                return;

            float lastNoteRight = float.NegativeInfinity;
            for (int i = 0; i < noteLayouts.Length; i++)
            {
                float right = NoteTrailingRight(noteLayouts[i].IsRest, noteLayouts[i].X);
                if (right > lastNoteRight)
                    lastNoteRight = right;
            }

            if (lastNoteRight <= float.NegativeInfinity)
                return;

            int finalBarIndex = barLayouts.Length - 1;
            float endBarX = Math.Max(barLayouts[finalBarIndex].X, lastNoteRight + BarRightPadding);
            if (finalBarIndex > 0)
                endBarX = Math.Max(endBarX, barLayouts[finalBarIndex - 1].X + 5f);
            barLayouts[finalBarIndex].X = endBarX;
        }

        /// <summary>
        /// Fits notes and bars inside [header floor, layout right limit]. Uses uniform scale when the
        /// span is too wide; otherwise a single shift. Avoids pulling past the left floor.
        /// </summary>
        private void ClampLayoutToSafeRight(
            NoteLayout[] noteLayouts,
            BarLayout[] barLayouts,
            float layoutRightLimit,
            float safeLeft,
            float staffLeftMargin)
        {
            float limit = layoutRightLimit;
            float floorCenter = safeLeft + staffLeftMargin;

            float minX = GetLayoutMinX(noteLayouts, barLayouts);
            float maxRight = GetLayoutMaxRight(noteLayouts, barLayouts);
            float avail = limit - floorCenter;
            float span = maxRight - minX;

            if (span <= 0f || avail <= 0f)
                return;

            if (span > avail)
            {
                float s = avail / span;
                for (int i = 0; i < noteLayouts.Length; i++)
                {
                    noteLayouts[i].X = floorCenter + (noteLayouts[i].X - minX) * s;
                    noteLayouts[i].AccidentalX = floorCenter + (noteLayouts[i].AccidentalX - minX) * s;
                }

                for (int i = 0; i < barLayouts.Length; i++)
                    barLayouts[i].X = floorCenter + (barLayouts[i].X - minX) * s;
            }
            else
            {
                float shift = 0f;
                if (maxRight > limit)
                    shift -= maxRight - limit;
                if (minX + shift < floorCenter)
                    shift += floorCenter - (minX + shift);

                if (Math.Abs(shift) < 0.01f)
                    return;

                for (int i = 0; i < noteLayouts.Length; i++)
                {
                    noteLayouts[i].X += shift;
                    noteLayouts[i].AccidentalX += shift;
                }

                for (int i = 0; i < barLayouts.Length; i++)
                    barLayouts[i].X += shift;
            }
        }

        /// <summary>
        /// Uniformly stretches content so the end bar reaches the safe right edge
        /// (uses space before the camera cutout without changing left header anchor).
        /// </summary>
        private void ExpandLayoutToFillSafeRight(
            NoteLayout[] noteLayouts,
            BarLayout[] barLayouts,
            float layoutRightLimit,
            float safeLeft,
            float staffLeftMargin)
        {
            const float fillThreshold = 4f;
            float limit = layoutRightLimit;
            float anchor = safeLeft + staffLeftMargin;

            float maxRight = GetLayoutMaxRight(noteLayouts, barLayouts);
            if (maxRight >= limit - fillThreshold)
                return;

            float contentSpan = maxRight - anchor;
            if (contentSpan <= 1f)
                return;

            float targetSpan = limit - anchor;
            float stretch = targetSpan / contentSpan;
            if (stretch <= 1.001f)
                return;

            V3Log($"[V3] Expand: span {contentSpan:F0} → {targetSpan:F0}, ×{stretch:F3}");

            for (int i = 0; i < noteLayouts.Length; i++)
            {
                noteLayouts[i].X = anchor + (noteLayouts[i].X - anchor) * stretch;
                noteLayouts[i].AccidentalX = anchor + (noteLayouts[i].AccidentalX - anchor) * stretch;
            }

            for (int i = 0; i < barLayouts.Length; i++)
                barLayouts[i].X = anchor + (barLayouts[i].X - anchor) * stretch;
        }

        /// <summary>Slides content right when stretch was not needed but a gutter remains.</summary>
        private void PadLayoutGutterToLimit(
            NoteLayout[] noteLayouts,
            BarLayout[] barLayouts,
            float layoutRightLimit)
        {
            float maxRight = GetLayoutMaxRight(noteLayouts, barLayouts);
            if (maxRight >= layoutRightLimit - 1f)
                return;

            float delta = layoutRightLimit - maxRight;
            for (int i = 0; i < noteLayouts.Length; i++)
            {
                noteLayouts[i].X += delta;
                noteLayouts[i].AccidentalX += delta;
            }

            for (int i = 0; i < barLayouts.Length; i++)
                barLayouts[i].X += delta;
        }

        private float GetLayoutMinX(NoteLayout[] noteLayouts, BarLayout[] barLayouts)
        {
            float minX = float.PositiveInfinity;
            for (int i = 0; i < noteLayouts.Length; i++)
            {
                float left = NoteGroupLeftFromLayout(noteLayouts[i]);
                if (left < minX)
                    minX = left;
            }

            for (int i = 0; i < barLayouts.Length; i++)
            {
                if (barLayouts[i].X < minX)
                    minX = barLayouts[i].X;
            }

            return minX;
        }

        private float GetLayoutMaxRight(NoteLayout[] noteLayouts, BarLayout[] barLayouts)
        {
            float maxRight = float.NegativeInfinity;
            for (int i = 0; i < noteLayouts.Length; i++)
            {
                float right = NoteTrailingRight(noteLayouts[i].IsRest, noteLayouts[i].X);
                if (right > maxRight)
                    maxRight = right;
            }

            for (int i = 0; i < barLayouts.Length; i++)
            {
                float barRight = BarLineRightEdge(barLayouts[i]);
                if (barRight > maxRight)
                    maxRight = barRight;
            }

            return maxRight;
        }

        /// <summary>Minimum global beat on a staff so layout uses staff-local beat 0 at the left.</summary>
        private static double GetStaffBeatOrigin(
            IReadOnlyList<GeneratedNote> notes,
            IReadOnlyList<double> barBeats)
        {
            double origin = double.PositiveInfinity;
            for (int i = 0; i < notes.Count; i++)
            {
                double bp = notes[i].BeatPosition ?? 0.0;
                if (bp < origin)
                    origin = bp;
            }

            for (int i = 0; i < barBeats.Count; i++)
            {
                if (barBeats[i] < origin)
                    origin = barBeats[i];
            }

            return double.IsPositiveInfinity(origin) ? 0.0 : origin;
        }

        /// <summary>Forward pass within each measure only — avoids stealing space across bar lines.</summary>
        private void EnforceMonotonicNoteSpacingInMeasures(
            IReadOnlyList<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            BarLayout[] barLayouts,
            IReadOnlyList<double> barBeats,
            double beatOrigin)
        {
            if (notes.Count == 0 || noteLayouts.Length == 0)
                return;

            var sortedBarBeats = barBeats.Select(b => b - beatOrigin).OrderBy(b => b).ToList();
            double totalBeats = 0;
            for (int i = 0; i < notes.Count; i++)
            {
                double rel = (notes[i].BeatPosition ?? 0.0) - beatOrigin;
                totalBeats = Math.Max(totalBeats, rel + notes[i].BeatDuration);
            }

            var segments = BuildMeasureSegments(notes.ToList(), sortedBarBeats, beatOrigin, totalBeats);
            int internalBarCount = sortedBarBeats.Count;
            float prevMeasureTrailing = float.NegativeInfinity;

            for (int m = 0; m < segments.Count; m++)
            {
                if (segments[m].NoteIndices.Count < 2)
                {
                    if (segments[m].NoteIndices.Count == 1)
                    {
                        int onlyIdx = segments[m].NoteIndices[0];
                        prevMeasureTrailing = NoteTrailingRight(notes[onlyIdx], noteLayouts[onlyIdx].X);
                    }
                    continue;
                }

                float measureRight = m < internalBarCount
                    ? barLayouts[m].X
                    : barLayouts[^1].X;

                var sorted = SortIndicesByBeat(notes.ToList(), segments[m].NoteIndices, beatOrigin);
                var firstLayout = noteLayouts[sorted[0]];
                float barInnerLeft = m > 0 ? barLayouts[m - 1].X + BarLeftPadding : float.NegativeInfinity;
                float innerLeft = m > 0
                    ? Math.Max(barInnerLeft,
                        prevMeasureTrailing > float.NegativeInfinity
                            ? prevMeasureTrailing + _planInkGap
                            : barInnerLeft)
                    : NoteGroupLeft(
                        firstLayout.X,
                        firstLayout.IsRest,
                        firstLayout.HasAccidental,
                        firstLayout.AccidentalIsFlat);

                ResolveMeasureNoteSpacing(
                    notes.ToList(), noteLayouts, sorted, innerLeft, measureRight - BarLeftPadding * 0.5f,
                    measureRight);

                int lastIdx = sorted[^1];
                prevMeasureTrailing = NoteTrailingRight(notes[lastIdx], noteLayouts[lastIdx].X);
            }
        }

        private static float[] CopyLayoutX(NoteLayout[] layouts)
        {
            var copy = new float[layouts.Length];
            for (int i = 0; i < layouts.Length; i++)
                copy[i] = layouts[i].X;
            return copy;
        }

        private float ComputeStemX(GeneratedNote note, float noteCenterX, float staffTop, float staffMid, float staffBot)
        {
            if (note.IsRest)
                return noteCenterX;
            float ny = NoteY(note, staffTop, staffMid);
            bool stemUp = ny >= staffMid;
            return stemUp ? noteCenterX + _layout.NoteHeadR : noteCenterX - _layout.NoteHeadR;
        }

        private void LogV3StaffLayoutDiagnostics(
            string staffLabel,
            IReadOnlyList<GeneratedNote> notes,
            NoteLayout[] layouts,
            float[] preScaleX,
            float staffTop,
            float staffMid,
            float staffBot)
        {
            const float minMelodyDelta = 6f;
            const int maxEvents = 12;
            int count = Math.Min(Math.Min(notes.Count, layouts.Length), maxEvents);

            float prevDrawnX = float.NaN;
            float prevStemX = float.NaN;

            for (int i = 0; i < count; i++)
            {
                var n = notes[i];
                float naturalX = i < preScaleX.Length ? preScaleX[i] : layouts[i].X;
                float drawnX = layouts[i].X;
                float stemX = ComputeStemX(n, drawnX, staffTop, staffMid, staffBot);
                float delta = float.IsNaN(prevDrawnX) ? 0f : drawnX - prevDrawnX;

                string label = n.IsRest ? "REST" : n.SpelledName;
                V3Log(
                    $"[V3Layout] {staffLabel} i={i} {label} {n.Duration} " +
                    $"naturalX={naturalX:F1} drawnX={drawnX:F1} stemX={stemX:F1} " +
                    $"prevDrawnX={(float.IsNaN(prevDrawnX) ? 0f : prevDrawnX):F1} delta={delta:F1}");

                if (!n.IsRest && !float.IsNaN(prevDrawnX))
                {
                    if (delta < minMelodyDelta)
                    {
                        V3Log(
                            $"[V3Layout WARN] {staffLabel} i={i}: drawn X delta {delta:F1} < {minMelodyDelta} " +
                            $"(prev={prevDrawnX:F1} cur={drawnX:F1})");
                    }

                    if (Math.Abs(stemX - prevStemX) < 0.5f)
                    {
                        V3Log(
                            $"[V3Layout WARN] {staffLabel} i={i}: stem X {stemX:F1} same as previous {prevStemX:F1}");
                    }

                    if (Math.Abs(drawnX - stemX) < 1f)
                    {
                        V3Log(
                            $"[V3Layout WARN] {staffLabel} i={i}: notehead X {drawnX:F1} equals stem X {stemX:F1}");
                    }
                }

                if (!n.IsRest)
                {
                    prevDrawnX = drawnX;
                    prevStemX = stemX;
                }
            }
        }

        /// <summary>
        /// Apply horizontal offset when no compression is needed.
        /// </summary>
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
            IReadOnlyList<double> barBeats, double beatOrigin,
            float alpha,
            bool isActive, int currentIdx,
            float safeLeft, float safeRight, float layoutRightLimit,
            float staffLeftMargin,
            bool drawKeyAndTimeSig = true)
        {
            if (notes.Count == 0 || noteLayouts.Length == 0)
                return;
            if (notes.Count != noteLayouts.Length)
            {
                V3Log($"[V3] Skipping staff draw: {notes.Count} notes vs {noteLayouts.Length} layouts");
                return;
            }

            barBeats ??= Array.Empty<double>();
            float headerRightAbs = safeLeft + staffLeftMargin - _layout.NoteHeadR;

            const float safeEdgePad = 4f;
            float contentEndX = safeLeft + staffLeftMargin;
            for (int i = 0; i < noteLayouts.Length; i++)
            {
                float noteRight = NoteTrailingRight(noteLayouts[i].IsRest, noteLayouts[i].X);
                if (noteRight > contentEndX)
                    contentEndX = noteRight;
            }

            for (int i = 0; i < barLayouts.Length; i++)
            {
                float barRight = BarLineRightEdge(barLayouts[i]);
                if (barRight > contentEndX)
                    contentEndX = barRight;
            }

            float staffLineEndX = Math.Max(contentEndX + 8f, layoutRightLimit - safeEdgePad);
            staffLineEndX = Math.Min(staffLineEndX, safeRight - safeEdgePad);
            staffLineEndX = Math.Max(staffLineEndX, safeLeft + staffLeftMargin);

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
            float clefH = staffBot - staffTop + _layout.Sls * 3.2f;
            canvas.DrawString("𝄞", _headerMetrics.ClefX, staffTop, _headerMetrics.ClefWidth, clefH,
                HorizontalAlignment.Left, VerticalAlignment.Top);
            canvas.RestoreState();

            // Key + time on upper staff only in two-staff layout (grand-staff convention).
            if (drawKeyAndTimeSig)
            {
                const float keySigGap = 4f;
                float keySigEndX = DrawKeySignature(canvas, staffTop, staffMid, ink);
                DrawTimeSignature(canvas, staffTop, staffMid, ink, keySigEndX + keySigGap);
            }

            // Bar lines (using pre-computed positions)
            canvas.StrokeColor = ink;
            foreach (var bar in barLayouts)
            {
                if (!float.IsFinite(bar.X))
                    continue;

                if (bar.IsDouble)
                {
                    // Double bar (final)
                    canvas.StrokeSize = 2f;
                    canvas.DrawLine(bar.X, staffTop - 2f, bar.X, staffBot + 2f);
                    canvas.StrokeSize = 4f;
                    canvas.DrawLine(bar.X + DoubleBarExtraWidth, staffTop - 2f, bar.X + DoubleBarExtraWidth, staffBot + 2f);
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
            var beamGroups = ComputeBeamGroups(notes, noteLayouts, staffTop, staffMid, barBeats, beatOrigin);

            // Draw notes and collect stem tips for beaming
            var beamStemTips = new Dictionary<int, (float x, float y, Color color, NoteDuration dur)>();
            var accHistory = new Dictionary<(char, int), Accidental>();
            var barCancelledAccidentals = new HashSet<(char, int)>();

            byte fadeAlpha = (byte)Math.Clamp((int)(alpha * 255), 0, 255);
            double prevBeat = -1.0;

            for (int i = 0; i < notes.Count; i++)
            {
                if (i >= noteLayouts.Length)
                    break;

                var note = notes[i];
                var layout = noteLayouts[i];
                if (!float.IsFinite(layout.X))
                    continue;

                var state = (states.Length > i) ? states[i] : V2NoteState.Pending;
                double beat = (note.BeatPosition ?? 0.0) - beatOrigin;

                ResetAccidentalStateIfCrossedBar(beat, prevBeat, barBeats, beatOrigin,
                    accHistory, barCancelledAccidentals);

                // Key-sig pitch-class cancelled only when this note explicitly contradicts the sig
                // (e.g. B♮ in F major). None does not cancel — implied key-sig pitch needs no glyph.
                if (!note.IsRest && note.Accidental != Accidental.None
                    && !IsAccidentalInKeySig(note.Accidental, note.Letter) && IsNoteInKeySig(note))
                {
                    barCancelledAccidentals.Add((note.Letter, note.Octave));
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
                    DrawAccidental(canvas, note, layout, ny, ink, accHistory, barCancelledAccidentals, fadeAlpha, headerRightAbs);

                    var nameDisplay = _session.V2NoteNameDisplay;
                    bool showName = nameDisplay == "All notes"
                        || (nameDisplay == "Current only" && state == V2NoteState.Current);
                    if (showName)
                        DrawNoteName(canvas, note, layout.X, ny, staffTop, staffBot, ink, fadeAlpha);
                }

                prevBeat = beat;
            }

            // Draw beams using pre-computed stem positions
            DrawBeams(canvas, beamGroups, beamStemTips, barLayouts);
        }

        /// <summary>Accidentals and key-sig cancellations apply within the current measure only.</summary>
        private static void ResetAccidentalStateIfCrossedBar(
            double beat,
            double prevBeat,
            IReadOnlyList<double> barBeats,
            double beatOrigin,
            Dictionary<(char, int), Accidental> accHistory,
            HashSet<(char, int)> barCancelledAccidentals)
        {
            foreach (var barBeat in barBeats.OrderBy(b => b))
            {
                double relBar = barBeat - beatOrigin;
                if (prevBeat < relBar - 1e-6 && beat >= relBar - 1e-6)
                {
                    accHistory.Clear();
                    barCancelledAccidentals.Clear();
                    return;
                }
            }
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
            float staffMid,
            IReadOnlyList<double> barBeats,
            double beatOrigin)
        {
            var beamGroup = new Dictionary<int, (int groupId, bool stemUp)>();
            var sortedBarBeatsRel = barBeats.Select(b => b - beatOrigin).OrderBy(b => b).ToList();
            int groupId = 0;
            int i = 0;

            while (i < notes.Count)
            {
                var n = notes[i];
                double pos = (n.BeatPosition ?? 0.0) - beatOrigin;

                // Only beam eighth or sixteenth non-rest notes
                bool isBeamable = !n.IsRest
                    && (n.Duration == NoteDuration.Eighth || n.Duration == NoteDuration.Sixteenth);

                if (!isBeamable)
                {
                    i++;
                    continue;
                }

                int currentMeasure = GetMeasureIndexForBeat(pos, sortedBarBeatsRel);
                double measureEnd = currentMeasure < sortedBarBeatsRel.Count
                    ? sortedBarBeatsRel[currentMeasure]
                    : double.PositiveInfinity;

                // Eighths beam within half-beat groups; sixteenths beam within a full beat.
                double groupStart = n.Duration == NoteDuration.Sixteenth
                    ? Math.Floor(pos + 1e-9)
                    : Math.Floor(pos * 2.0) / 2.0;
                double groupEnd = n.Duration == NoteDuration.Sixteenth
                    ? groupStart + 1.0
                    : groupStart + 0.5;
                groupEnd = Math.Min(groupEnd, measureEnd - 1e-6);
                if (groupEnd <= groupStart + 1e-6)
                {
                    i++;
                    continue;
                }

                // Collect consecutive beamable notes within the same beat/half-beat group and measure
                var groupIndices = new List<int>();
                int j = i;

                while (j < notes.Count)
                {
                    var nj = notes[j];
                    double pj = (nj.BeatPosition ?? groupEnd) - beatOrigin;

                    // Stop if we hit a rest or unbeamable duration
                    if (nj.IsRest || (nj.Duration != NoteDuration.Eighth && nj.Duration != NoteDuration.Sixteenth))
                        break;

                    // Stop if we cross into a different measure or reach the bar beat
                    if (GetMeasureIndexForBeat(pj, sortedBarBeatsRel) != currentMeasure
                        || pj >= measureEnd - 1e-6)
                        break;

                    // Sixteenths only group with other sixteenths in the same beat
                    if (n.Duration == NoteDuration.Sixteenth && nj.Duration != NoteDuration.Sixteenth)
                        break;
                    if (n.Duration == NoteDuration.Eighth && nj.Duration == NoteDuration.Sixteenth)
                        break;

                    // Stop if this note starts outside our beam group window
                    if (pj < groupStart - 1e-6)
                        break;
                    if (pj >= groupEnd + 1e-6)
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
            Dictionary<int, (float x, float y, Color color, NoteDuration dur)> beamStemTips,
            BarLayout[] barLayouts)
        {
            float beamThick = 4f * _layout.GlyphScale;
            float beamGap   = 3f * _layout.GlyphScale;

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

                float x0 = tips[0].x;
                float y0 = tips[0].y;
                float x1 = tips[^1].x;
                float y1 = tips[^1].y;

                // Cap slope to 1.5 staff spaces for natural melodic contour
                float maxTilt = _layout.Sls * 1.5f;
                if (Math.Abs(y1 - y0) > maxTilt)
                    y1 = y0 + Math.Sign(y1 - y0) * maxTilt;

                float BeamY(float x) => x0 == x1 ? y0 : y0 + (y1 - y0) * ((x - x0) / (x1 - x0));

                var beamColor = tips.FirstOrDefault(t => t.color != default).color;
                if (beamColor == default) beamColor = ApplyAlpha(Colors.Black, 220);

                var segments = SplitBeamAtBarLines(x0, x1, barLayouts);
                foreach (var (segLeft, segRight) in segments)
                {
                    if (segRight - segLeft < 1f)
                        continue;

                    canvas.SaveState();
                    canvas.StrokeColor = beamColor;

                    // Primary beam (eighth notes)
                    canvas.StrokeSize = beamThick;
                    canvas.DrawLine(segLeft, BeamY(segLeft), segRight, BeamY(segRight));

                    // Secondary beam (sixteenth notes)
                    float secondaryOffset = grpStemUp ? (beamThick + beamGap) : -(beamThick + beamGap);
                    for (int ti = 0; ti < tips.Count; ti++)
                    {
                        if (tips[ti].dur != NoteDuration.Sixteenth) continue;

                        int segStart = ti;
                        while (ti + 1 < tips.Count && tips[ti + 1].dur == NoteDuration.Sixteenth)
                            ti++;
                        int segEnd = ti;

                        float sx0 = Math.Max(tips[segStart].x, segLeft);
                        float sx1 = Math.Min(tips[segEnd].x, segRight);
                        if (sx1 - sx0 < 1f)
                            continue;

                        if (segStart == segEnd)
                        {
                            float halfSlot = (x1 - x0) / Math.Max(tips.Count - 1, 1) * 0.5f;
                            if (segStart == 0)
                                sx1 = Math.Min(sx0 + halfSlot, segRight);
                            else
                                sx0 = Math.Max(sx1 - halfSlot, segLeft);
                        }

                        canvas.StrokeSize = beamThick;
                        canvas.DrawLine(
                            sx0, BeamY(sx0) + secondaryOffset,
                            sx1, BeamY(sx1) + secondaryOffset);
                    }

                    canvas.RestoreState();
                }
            }
        }

        /// <summary>Splits a beam span into measure-safe segments that stop before each bar line.</summary>
        private static List<(float left, float right)> SplitBeamAtBarLines(
            float beamLeft,
            float beamRight,
            BarLayout[] barLayouts)
        {
            var segments = new List<(float left, float right)>();
            if (beamRight - beamLeft < 1f)
                return segments;

            var interiorBars = new List<float>();
            for (int b = 0; b < barLayouts.Length; b++)
            {
                float barX = barLayouts[b].X;
                if (barX > beamLeft + 0.5f && barX < beamRight - 0.5f)
                    interiorBars.Add(barX);
            }

            interiorBars.Sort();

            float segLeft = beamLeft;
            GetBeamClipBounds(beamLeft, beamRight, barLayouts, out float outerLeft, out float outerRight);
            segLeft = Math.Max(segLeft, outerLeft);

            foreach (float barX in interiorBars)
            {
                float segRight = barX - BarLeftPadding;
                if (segRight - segLeft >= 1f)
                    segments.Add((segLeft, segRight));
                segLeft = Math.Max(segLeft, barX + BarLeftPadding);
            }

            float finalRight = Math.Min(beamRight, outerRight);
            if (finalRight - segLeft >= 1f)
                segments.Add((segLeft, finalRight));

            if (segments.Count == 0 && outerRight - outerLeft >= 1f)
                segments.Add((outerLeft, outerRight));

            return segments;
        }

        /// <summary>Limits beam endpoints so ink stays clear of bar lines on both sides.</summary>
        private static void GetBeamClipBounds(
            float beamLeft,
            float beamRight,
            BarLayout[] barLayouts,
            out float clipLeft,
            out float clipRight)
        {
            clipLeft = beamLeft;
            clipRight = beamRight;

            for (int b = 0; b < barLayouts.Length; b++)
            {
                float barX = barLayouts[b].X;
                if (barX <= beamLeft + 0.5f)
                    clipLeft = Math.Max(clipLeft, barX + BarLeftPadding);
                else if (barX >= beamRight - 0.5f)
                    clipRight = Math.Min(clipRight, barX - BarLeftPadding);
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

        /// <summary>Shared clef / key / time X positions and note left margin for one draw pass.</summary>
        private readonly struct StaffHeaderMetrics
        {
            public float ClefX { get; init; }
            public float ClefWidth { get; init; }
            public float KeySigStartX { get; init; }
            public float KeySigEndX { get; init; }
            public float TimeSigX { get; init; }
            /// <summary>Right edge of the time signature, relative to <c>safeLeft</c>.</summary>
            public float TimeSigRightRel { get; init; }
            /// <summary>Offset from <c>safeLeft</c> to the first note beat-0 anchor (center X).</summary>
            public float LeftMargin { get; init; }
            /// <summary>Clef-only anchor for lower staff (no key/time repeat).</summary>
            public float ClefOnlyLeftMargin { get; init; }
        }

        private StaffHeaderMetrics ComputeHeaderMetrics(float safeLeft)
        {
            const float clefPad = 2f;
            const float keySigGap = 4f;
            const float timeSigW = 24f;

            float clefWidth = _layout.Sls * 4.5f;
            float clefOnlyRightRel = clefPad + clefWidth;
            float clefOnlyLeftMargin = clefOnlyRightRel + _layout.NoteHeadR + _layout.NoteHeadR;

            string key   = _session.Key;
            string scale = _session.SelectedScale;
            bool suppressKeySig = _session.Tune == "Tuner"
                || _session.Tune == "Practice Tune" || scale == "Chromatic";
            int accCount = suppressKeySig ? 0 : GetAccidentalCount(key, scale);

            float clefX = safeLeft + clefPad;
            float keySigStartX = safeLeft + clefWidth;
            float keySigEndX = keySigStartX + KeySigDrawnWidth(accCount);
            float timeSigX = keySigEndX + keySigGap;
            float timeSigRightRel = timeSigX - safeLeft + timeSigW;
            // Gap from time sig to first item (note/rest/accidental) ≈ one note-head width.
            float leftMargin = timeSigRightRel + _layout.NoteHeadR + _layout.NoteHeadR;

            return new StaffHeaderMetrics
            {
                ClefX               = clefX,
                ClefWidth           = clefWidth,
                KeySigStartX        = keySigStartX,
                KeySigEndX          = keySigEndX,
                TimeSigX            = timeSigX,
                TimeSigRightRel     = timeSigRightRel,
                LeftMargin          = leftMargin,
                ClefOnlyLeftMargin  = clefOnlyLeftMargin
            };
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

                float stroke = 2f * _layout.GlyphScale;
                canvas.StrokeColor = noteColor;
                canvas.StrokeSize  = stroke;

                bool filled = duration != NoteDuration.Whole && duration != NoteDuration.Half;
                if (filled)
                {
                    canvas.FillColor = noteColor;
                    canvas.FillEllipse(x - r, y - r * 0.75f, r * 2f, r * 1.5f);
                }
                else
                {
                    canvas.StrokeColor = noteColor;
                    canvas.StrokeSize  = stroke;
                    canvas.DrawEllipse(x - r, y - r * 0.75f, r * 2f, r * 1.5f);
                }

                if (duration != NoteDuration.Whole)
                {
                    float g = _layout.GlyphScale;
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
                    canvas.StrokeSize  = stroke;
                    canvas.DrawLine(stemX, stemY, stemX, stemEnd);

                    stemTipX = stemX;
                    stemTipY = stemEnd;

                    // Individual flag — suppressed for beamed notes (beam drawn after all notes)
                    if (!isBeamed)
                    {
                        if (duration == NoteDuration.Eighth)
                        {
                            canvas.StrokeSize = stroke;
                            if (stemUp)
                            {
                                canvas.DrawLine(stemX, stemEnd, stemX + 12f * g, stemEnd + 10f * g);
                                canvas.DrawLine(stemX + 12f * g, stemEnd + 10f * g, stemX + 6f * g, stemEnd + 18f * g);
                            }
                            else
                            {
                                canvas.DrawLine(stemX, stemEnd, stemX + 12f * g, stemEnd - 10f * g);
                                canvas.DrawLine(stemX + 12f * g, stemEnd - 10f * g, stemX + 6f * g, stemEnd - 18f * g);
                            }
                        }

                        if (duration == NoteDuration.Sixteenth)
                        {
                            for (int f = 0; f < 2; f++)
                            {
                                float off = f * (stemUp ? 10f * g : -10f * g);
                                if (stemUp)
                                {
                                    canvas.DrawLine(stemX, stemEnd + off, stemX + 12f * g, stemEnd + 10f * g + off);
                                    canvas.DrawLine(stemX + 12f * g, stemEnd + 10f * g + off, stemX + 6f * g, stemEnd + 18f * g + off);
                                }
                                else
                                {
                                    canvas.DrawLine(stemX, stemEnd + off, stemX + 12f * g, stemEnd - 10f * g + off);
                                    canvas.DrawLine(stemX + 12f * g, stemEnd - 10f * g + off, stemX + 6f * g, stemEnd - 18f * g + off);
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

                float restScale = Math.Min(1f, CompactRestScale * _layout.GlyphScale);
                SmuFLRestDrawer.Draw(canvas, duration, x, staffTop, staffMid, _layout.Sls, rc, restScale);
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
                canvas.StrokeSize  = 1.5f * _layout.GlyphScale;

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

        private void DrawAccidental(ICanvas canvas, GeneratedNote note, NoteLayout layout, float y,
                                    Color ink, Dictionary<(char, int), Accidental>? history,
                                    HashSet<(char, int)>? barCancelled,
                                    byte fadeAlpha, float headerRightAbs)
        {
            if (!TryResolveBodyAccidental(note, history, barCancelled, out var eff, out bool draw))
                return;

            if (history != null && draw && eff != Accidental.None)
                history[(note.Letter, note.Octave)] = eff;

            if (!draw || eff == Accidental.None)
                return;

            canvas.SaveState();
            try
            {
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

                bool isFlat = IsFlatBodyAccidental(eff);
                bool isNatural = eff == Accidental.Natural;
                float scaleRef = _layout.Sls / 12f;
                float boxLeft = layout.HasAccidental
                    ? layout.AccidentalX
                    : AccidentalBoxLeft(layout.X, isFlat);
                float boxRight = AccidentalBoxRight(layout.X);

                // Never paint body accidentals over this staff's header (clef, or clef+key+time).
                if (boxLeft < headerRightAbs)
                    return;

                canvas.FontColor = ApplyAlpha(ink, fadeAlpha);

                float symW = BodyAccidentalDrawWidth(isFlat);
                float bodyFont = _layout.Sls * 2.2f * _layout.GlyphScale;
                float raise = _layout.Sls * BodyAccidentalRaise * _layout.GlyphScale;

                if (isFlat)
                {
                    float symH = 60f * scaleRef * _layout.GlyphScale;
                    float yTop = y - symH * 0.50f - raise;
                    canvas.FontSize = _layout.Sls * 4f * _layout.GlyphScale;
                    canvas.DrawString(glyph, boxLeft, yTop, boxRight - boxLeft, symH,
                        HorizontalAlignment.Right, VerticalAlignment.Top);
                }
                else
                {
                    float symH = symW * (isNatural ? 1.15f : 1.05f);
                    float yTop = y - symH * 0.42f - raise;
                    canvas.FontSize = bodyFont;
                    canvas.DrawString(glyph, boxLeft, yTop, boxRight - boxLeft, symH,
                        HorizontalAlignment.Right, VerticalAlignment.Top);
                }

                // Key-sig reminder drawn — clear cancellation for this letter+octave in the bar.
                if (IsAccidentalInKeySig(eff, note.Letter))
                    barCancelled?.Remove((note.Letter, note.Octave));
            }
            finally { canvas.RestoreState(); }
        }

        /// <summary>
        /// Resolves the single body accidental to draw (StaffDrawable deviation rules).
        /// Returns false when the note should not alter bar accidental state.
        /// </summary>
        private bool TryResolveBodyAccidental(
            GeneratedNote note,
            Dictionary<(char, int), Accidental>? history,
            HashSet<(char, int)>? barCancelled,
            out Accidental eff,
            out bool draw)
        {
            eff = note.Accidental;
            draw = false;
            var pitchKey = (note.Letter, note.Octave);
            string? sigAcc = GetSignatureAccidentalForLetter(note.Letter);

            // Courtesy after a chromatic on this letter+octave earlier in the measure.
            if (eff == Accidental.None && history != null
                && history.TryGetValue(pitchKey, out var prev)
                && (prev == Accidental.Sharp || prev == Accidental.Flat
                    || prev == Accidental.DoubleSharp || prev == Accidental.DoubleFlat))
            {
                // Restore key-sig pitch with ♯/♭; otherwise cancel with ♮ (e.g. Ab → A).
                if (sigAcc == "#")      eff = Accidental.Sharp;
                else if (sigAcc == "b") eff = Accidental.Flat;
                else                    eff = Accidental.Natural;
            }

            if (eff == Accidental.None)
                return true;

            // ♮ only when cancelling a key-signature alteration on this letter.
            if (eff == Accidental.Natural)
            {
                draw = sigAcc != null;
                return true;
            }

            // ♯/♭ already implied by the key signature — omit unless restored after cancellation.
            if (IsAccidentalInKeySig(eff, note.Letter))
            {
                draw = barCancelled != null && barCancelled.Contains(pitchKey);
                return true;
            }

            draw = true;
            return true;
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
            float scaleRef = _layout.Sls / 12f;
            float symH     = 60f * scaleRef * _layout.GlyphScale;
            float symW     = KeySigGlyphWidth();
            float symSlot  = KeySigSymbolSlot();

            if (_session.Tune == "Tuner"
                || _session.Tune == "Practice Tune" || _session.SelectedScale == "Chromatic")
                return _headerMetrics.KeySigStartX;

            int accCount = GetAccidentalCount(_session.Key, _session.SelectedScale);
            if (accCount == 0)
                return _headerMetrics.KeySigStartX;

            V3Log($"[V3] KeySig key={_session.Key} scale={_session.SelectedScale} count={accCount}");

            bool useFlats = IsKeyFlat(_session.Key);
            string glyph  = useFlats ? "♭" : "♯";
            float fontSize  = (useFlats ? _layout.Sls * 4f : _layout.Sls * 2.5f) * _layout.GlyphScale;

            int[] flatSteps  = { 0, -3,  1, -2,  2, -1,  3 };
            int[] sharpSteps = { -4, -1, -5, -2,  1, -3,  0 };
            int[] steps = useFlats ? flatSteps : sharpSteps;

            canvas.SaveState();
            canvas.FontColor = ink;
            canvas.FontSize  = fontSize;
            float sigX = _headerMetrics.KeySigStartX;
            for (int i = 0; i < Math.Min(accCount, steps.Length); i++)
            {
                float yCenter = staffMid + steps[i] * _layout.HS;
                if (useFlats)
                {
                    float yTop = yCenter - symH * 0.75f + _layout.HS;
                    canvas.DrawString(glyph, sigX, yTop, symW, symH,
                        HorizontalAlignment.Center, VerticalAlignment.Top);
                }
                else
                {
                    float yAdjust = 0.38f;
                    float yTop    = yCenter - symH * yAdjust;
                    canvas.DrawString(glyph, sigX, yTop, symW, symH,
                        HorizontalAlignment.Center, VerticalAlignment.Top);
                }

                sigX += symSlot;
            }
            canvas.RestoreState();
            return _headerMetrics.KeySigStartX + KeySigDrawnWidth(accCount);
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

                float tsX = keySigEndX;
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

        private bool WillReserveAccidentalSpace(GeneratedNote note)
        {
            if (note.Accidental == Accidental.None) return false;
            if (note.Accidental == Accidental.Natural)
                return GetSignatureAccidentalForLetter(note.Letter) != null;
            return !IsAccidentalInKeySig(note.Accidental, note.Letter);
        }

        private string? GetSignatureAccidentalForLetter(char letter)
        {
            int accCount = GetAccidentalCount(_session.Key, _session.SelectedScale);
            if (accCount == 0) return null;

            bool useFlats = IsKeyFlat(_session.Key);
            char[] flatLetters  = { 'B', 'E', 'A', 'D', 'G', 'C', 'F' };
            char[] sharpLetters = { 'F', 'C', 'G', 'D', 'A', 'E', 'B' };
            char[] letters = useFlats ? flatLetters : sharpLetters;
            for (int i = 0; i < Math.Min(accCount, letters.Length); i++)
            {
                if (letters[i] == letter)
                    return useFlats ? "b" : "#";
            }
            return null;
        }

        private bool IsAccidentalInKeySig(GeneratedNote note)
            => IsAccidentalInKeySig(note.Accidental, note.Letter);

        private bool IsAccidentalInKeySig(Accidental accidental, char letter)
        {
            if (accidental == Accidental.None || accidental == Accidental.Natural) return false;

            int accCount = GetAccidentalCount(_session.Key, _session.SelectedScale);
            if (accCount == 0) return false;

            bool useFlats = IsKeyFlat(_session.Key);
            bool typeMatch = useFlats
                ? accidental == Accidental.Flat
                : accidental == Accidental.Sharp;
            if (!typeMatch) return false;

            char[] flatLetters  = { 'B', 'E', 'A', 'D', 'G', 'C', 'F' };
            char[] sharpLetters = { 'F', 'C', 'G', 'D', 'A', 'E', 'B' };
            char[] letters = useFlats ? flatLetters : sharpLetters;
            for (int i = 0; i < Math.Min(accCount, letters.Length); i++)
                if (letters[i] == letter) return true;
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

#if DEBUG
        private static void V3Log(string message) => Utilities.Utils.Log(message);
#else
        private static void V3Log(string message) { }
#endif
    }
}
