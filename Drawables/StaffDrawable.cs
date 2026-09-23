using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Skia;
//  2026.07.09 1946  using Microsoft.UI.Xaml.Controls;
using Microsoft.Maui.Controls;
using musicmate.Models;
using musicmate.Services;
using System.Diagnostics;
using musicmate.Diagnostics;

namespace musicmate.Drawables
{
    /// <summary>
    /// Two-staff drawable.
    /// Renders an upper and a lower treble staff inside a single <see cref="ICanvas"/>.
    /// Reading order follows standard sheet music: upper staff first, then lower staff.
    /// These are two <b>independent</b> lines of music (not a grand staff). Key and time
    /// signatures may be drawn on both staffs or only the upper staff
    /// (<see cref="NoteSessionService.ShowSignaturesOnBothStaffs"/>). Internal bar
    /// lines need not align vertically between staves (only the final bar may be aligned).
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
    public class StaffDrawable : IDrawable
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
        public StaffNoteState[] UpperNoteStates { get; set; } = Array.Empty<StaffNoteState>();

        /// <summary>
        /// Visual state for every note on the lower staff, parallel to <see cref="LowerNotes"/>.
        /// </summary>
        public StaffNoteState[] LowerNoteStates { get; set; } = Array.Empty<StaffNoteState>();

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

        /// <summary>
        /// When true, vertical layout and drawing use the upper staff only.
        /// Used by Interval Sight Training. Default false so Music is unchanged.
        /// Independent of Tuner mode (which also uses a single staff for other reasons).
        /// </summary>
        public bool SingleStaffLayout { get; set; }

        /// <summary>
        /// When true with <see cref="SingleStaffLayout"/>, omit clef / key / tempo chrome
        /// so a short interval (e.g. Ear Training reveal) can sit in a narrow panel.
        /// Default false — Sight Training and Music keep their headers.
        /// </summary>
        public bool OmitStaffHeader { get; set; }

        /// <summary>
        /// Optional key for Sight Training engraving/key signature without mutating
        /// <see cref="NoteSessionService.Key"/> (Music session). Null = use session key.
        /// </summary>
        public string? NotationKeyOverride { get; set; }

        /// <summary>
        /// Optional scale for Sight Training key-signature rules. Null = use session scale.
        /// </summary>
        public string? NotationScaleOverride { get; set; }

        private string ActiveNotationKey
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(NotationKeyOverride))
                    return NotationKeyOverride!;
                // Practice / saved tunes keep their authored key even if the Music key picker changes.
                if (_session.Tune == "Practice Tune" && _session.CurrentTune != null)
                    return _session.GetNotationKeyAndScale().Key;
                return _session.Key;
            }
        }

        /// <summary>
        /// Music-page Tuner reference staff only — not Ear/Singing interval reveal panels
        /// (<see cref="OmitStaffHeader"/>).
        /// </summary>
        private bool IsTunerReferenceStaff
            => _session.Tune == "Tuner" && !OmitStaffHeader;

        /// <summary>Result of assigning whole measures to upper/lower staves by width.</summary>
        public sealed class StaffMeasureSplitResult
        {
            public List<GeneratedNote> UpperNotes { get; } = new();
            public List<GeneratedNote> LowerNotes { get; } = new();
            /// <summary>
            /// Generated notes belonging to measures that did not fit on either staff
            /// at engraved minimum width. Never silently discarded — callers must account
            /// for these (next page / report). Invariant:
            /// UpperMeasureCount + LowerMeasureCount + UnplacedMeasureCount == TotalMeasureCount.
            /// </summary>
            public List<GeneratedNote> UnplacedNotes { get; } = new();
            public int UpperMeasureCount { get; set; }
            public int LowerMeasureCount { get; set; }
            public int TotalMeasureCount { get; set; }
            /// <summary>Measures that did not fit on either staff at readable engraved width.</summary>
            public int UnplacedMeasureCount { get; set; }
        }

        /// <summary>
        /// How consecutive measures are assigned to the two Music staves.
        /// </summary>
        public enum StaffMeasureSplitMode
        {
            /// <summary>Maximize placed measures, then balance upper/lower counts.</summary>
            Balanced = 0,
            /// <summary>
            /// Fill the upper staff first, then the lower — used for saved / practice tunes
            /// so empty upper space is not left while the lower overflows.
            /// </summary>
            FillUpperFirst = 1,
        }

        // ── Fixed horizontal constants ─────────────────────────────────────────
          //  2026.07.09 1912  private const float ScrollPxPerBeat = 42f;   // reduced from 48 for better fit
        private const float RightMargin = 36f;
        /// <summary>Tighter trailing gutter for single-staff Sight Training (more usable width).</summary>
        private const float SingleStaffRightMargin = 8f;

        private float EffectiveRightMargin
            => SingleStaffLayout ? SingleStaffRightMargin : RightMargin;

        // Uniform spacing constants
        private const float ItemSpacing = 46f;   // fixed horizontal spacing per item (reduced from 52)
        private const float AccidentalWidth = 40f;   // extra width reserved when a note has an accidental
        private const float BarLeftPadding = 16f;   // minimum space from bar line to notehead/stem
        private const float BarStemClearance = 8f;    // extra gap from bar to stem-ward edge of notehead
        private const float MinStemBarGap = 6f;    // minimum px between stem column and a bar line
        private const float MeasureStartExtraPad = 6f;   // added when a note starts exactly on a bar beat
        private const float StaffStartExtraPad = 4f;   // first note in staff after time signature
        private const float BarRightPadding = 20f;   // minimum space from last note to final bar line

        /// <summary>Padding before the layout right limit (dp).</summary>
        private const float LayoutRightPad = 2f;

        /// <summary>
        /// Subtract this from <see cref="ISafeAreaService"/> Right inset before computing SafeRight (dp).
        /// Android SafeInsetRight often includes empty margin before the cutout (~11mm / ~42dp).
        /// Increase (e.g. 40–50) to stretch the staff toward the view edge; 0 = use full inset.
        /// </summary>
        private const float RelaxCutoutInsetRightDp = 42f;

        private float _leftMargin = 115f;
        private float LeftMargin => _leftMargin;
        private StaffHeaderMetrics _headerMetrics;

        /// <summary>
        /// False during staff-local musical layout (<see cref="PlanHorizontalLayout"/>).
        /// Set true once geometry is mapped to screen; structural mutators must not run after that.
        /// </summary>
        private bool _horizontalScreenStage;

        private const float AccidentalRightGap = 0.5f;
        /// <summary>Minimum clear ink between a body accidental and the notehead left edge (px).</summary>
        private const float MinimumAccidentalNoteGap = 4f;
        /// <summary>Extra clear ink before a note that draws a body accidental (e.g. A → B♭).</summary>
        private const float AccidentalLeadingInkPad = 4f;
        private const float FlatAccidentalExtraInkPad = 2f;
        private const float DoubleBarExtraWidth = 4f;

        // Notehead ellipse is drawn with height = NoteHeadR * NoteHeadHeightFactor.
        private const float NoteHeadHeightFactor = 1.5f;
        /// <summary>Stroke used for the five staff lines (and Ear Training notehead height).</summary>
        private const float StaffLineStrokeSize = 1.5f;
        private const float BeginnerNoteHeadSpaceRatio = 0.9f; // target: 90% of staff-space height
        private const float CompactNoteHeadRRatio = 0.32f;
        /// <summary>Child levels 31+: modest notehead enlargement without beginner-scale collision risk.</summary>
        private const float MidLevelNoteHeadRRatio = 0.41f;
        /// <summary>Horizontal gap between key signature and time signature (px).</summary>
        private const float KeySigTimeSigGap = 1f;
        private const float CompactStemLenRatio = 2.15f;
        private const float CompactAccidentalRefPx = 26f;
        private const float CompactRestScale = 0.72f;

        // ── Interval Ear Training miniature staff (all sizes in staff-spaces) ──
        private const float OmitHeaderNoteHeadHeightSpaces = 0.90f;
        private const float OmitHeaderNoteHeadWidthSpaces = 1.30f;
        private const float OmitHeaderStemLenSpaces = 3.25f;
        private const float OmitHeaderStemThicknessSpaces = 0.12f;
        /// <summary>SMuFL: one staff-space is 0.25 em, so fontSize = 4 × staff-space.</summary>
        private const float OmitHeaderSmuFLEmPerStaffSpace = 4f;
        private const float OmitHeaderAccidentalHeightSpaces = 2.70f;
        private const float OmitHeaderAccidentalGapSpaces = 0.50f;
        /// <summary>Reserved accidental column (wide enough for a flat).</summary>
        private const float OmitHeaderAccidentalColumnSpaces = 1.05f;

        /// <summary>Child levels 1–30 use enlarged notation scaled from staff-space height.</summary>
        private bool UseBeginnerNotationScale =>
            _session.ChildLevel > 0 && _session.ChildLevel <= 30;

        /// <summary>Visual scale for key-signature accidentals only (not body accidentals).</summary>
        private float KeySigAccidentalScale()
        {
            if (_session.ChildLevel <= 0)
                return _layout.GlyphScale;
            if (_session.ChildLevel <= 30)
                return _layout.GlyphScale;
            float compactR = _layout.Sls * CompactNoteHeadRRatio;
            float beginnerR = _layout.Sls * BeginnerNoteHeadSpaceRatio / NoteHeadHeightFactor;
            return beginnerR / compactR;
        }

        private const float KeySigFlatSizeBoost = 0.95f;
        private const float BodyFlatSizeBoost = 1.34f;
        private const float BodyNaturalSizeBoost = 1.22f;

        /// <summary>
        /// Body-accidental scale factor for child levels 31+ (compact noteheads).
        /// Key-signature size is unaffected.
        /// </summary>
        private const float BodyAccidentalScale = 0.65f;

        private float OmitHeaderStaffSpace => _layout.Sls;
        private float OmitHeaderNoteHeadHalfWidth => OmitHeaderStaffSpace * OmitHeaderNoteHeadWidthSpaces * 0.5f;
        private float OmitHeaderNoteHeadHalfHeight => OmitHeaderStaffSpace * OmitHeaderNoteHeadHeightSpaces * 0.5f;
        private float OmitHeaderStemStroke => Math.Max(1f, OmitHeaderStaffSpace * OmitHeaderStemThicknessSpaces);
        private float OmitHeaderSmuFLFontSize => OmitHeaderStaffSpace * OmitHeaderSmuFLEmPerStaffSpace;
        private float OmitHeaderAccidentalGap => OmitHeaderStaffSpace * OmitHeaderAccidentalGapSpaces;

        /// <summary>Draw/layout scale for accidentals attached to notes (after the key signature).</summary>
        private float BodyAccidentalGlyphScale()
        {
            float keyScale = KeySigAccidentalScale();
            // Compact noteheads (adult levels, or the Ear Training side panel) use a matching
            // compact body accidental so two reserved accidental slots fit the narrow panel.
            if (OmitStaffHeader)
                return 1f;
            if (_session.ChildLevel > 30)
                return keyScale * BodyAccidentalScale;
            return keyScale;
        }

        /// <summary>
        /// Child levels use beat-proportional measure layout: bar widths from accumulated
        /// rhythmic span, items at beat-slot centers, and <see cref="BarLeftPadding"/> at bar lines.
        /// </summary>
        private bool UseBeginnerHorizontalLayout =>
            _session.ChildLevel > 0;

        /// <summary>Minimum ink gap for beat-proportional child layout (levels 1–30).</summary>
        private const float BeginnerInkGap = 12f;

        /// <summary>
        /// Minimum ink gap between consecutive notes inside a beam group.
        /// Must stay near <see cref="MinInkGap"/> — 2px packing made beamed eighths/sixteenths
        /// look like colliding clusters even when measure durations were exact.
        /// </summary>
        private const float BeamedInternalInkGap = 8f;

        /// <summary>
        /// Minimum uniform horizontal scale before fixed-size ink (noteheads, accidentals,
        /// bar pads) outruns scaled beat lanes. Screen mapping and measure packing must not
        /// go below this value — repack whole measures instead.
        /// <para>
        /// Derivation: after scale <c>s</c>, bar–note clearance is
        /// <c>s·C − (1−s)·F</c> where <c>C</c> is plan clearance at scale 1 and <c>F</c> is
        /// fixed trailing/leading reach (<see cref="NoteCenterTrailingReach"/>,
        /// accidental column ≈ <see cref="BodyAccidentalSymbolWidth"/> +
        /// <see cref="BodyAccidentalRightGapPx"/> + <see cref="AccidentalLeadingInkPad"/>).
        /// Requiring clearance ≥ <see cref="BarLeftPadding"/> + <see cref="BarStemClearance"/>
        /// yields <c>s ≥ (barPad + F) / (C + F)</c>; worst dense-accidental measures land near 0.78–0.82.
        /// <see cref="HorizontalCompressFloor"/> is set to this same conservative value (0.85).
        /// </para>
        /// </summary>
        internal const float MinimumSafeHorizontalScale = 0.85f;

        /// <summary>
        /// Floor for uniform horizontal compress/scale (alias of
        /// <see cref="MinimumSafeHorizontalScale"/>).
        /// </summary>
        private const float HorizontalCompressFloor = MinimumSafeHorizontalScale;

        /// <summary>
        /// Largest sum of staff-local measure minimum widths that may share one staff row
        /// at <paramref name="usableScreenWidth"/> without needing scale below
        /// <see cref="MinimumSafeHorizontalScale"/>.
        /// </summary>
        internal static float MaxSafePackableLocalWidth(float usableScreenWidth)
            => usableScreenWidth / MinimumSafeHorizontalScale;

        /// <summary>
        /// Scale needed to map <paramref name="requiredLocalWidth"/> into
        /// <paramref name="usableScreenWidth"/> (content span, excluding header margin).
        /// </summary>
        internal static float ComputeRequiredHorizontalScale(
            float requiredLocalWidth,
            float usableScreenWidth)
        {
            if (requiredLocalWidth <= 0f)
                return 1f;
            return usableScreenWidth / requiredLocalWidth;
        }

        /// <summary>
        /// True when packing this local content width on a staff would force sub-safe scale.
        /// </summary>
        internal static bool RequiresMeasureRepackForScale(
            float requiredLocalWidth,
            float usableScreenWidth)
            => ComputeRequiredHorizontalScale(requiredLocalWidth, usableScreenWidth)
               < MinimumSafeHorizontalScale - 1e-4f;

        /// <summary>Minimum ink gap before a note that carries a body accidental (keeps ♭/♯ with its head).</summary>
        private const float AccidentalPairInkGap = 14f;

        /// <summary>Clear ink between body-accidental box and notehead left edge (px).</summary>
        private float BodyAccidentalRightGapPx =>
            OmitStaffHeader
                ? OmitHeaderAccidentalGap
                : Math.Max(3.5f * BodyAccidentalGlyphScale(), AccidentalRightGap * _layout.Sls * 0.1f);

        private float KeySigSymbolWidth()
            => CompactAccidentalRefPx * (_layout.Sls / 12f) * KeySigAccidentalScale() * ArpeggioKeySigSizeBoost();

        private float BodyAccidentalSymbolWidth(bool isFlat = false, bool isNatural = false)
        {
            if (OmitStaffHeader)
            {
                string smufl = isFlat ? "\uE260" : isNatural ? "\uE261" : "\uE262";
                if (SmuFLGlyphMetrics.TryMeasure(smufl, OmitHeaderSmuFLFontSize, out var m))
                    return Math.Max(m.Width, OmitHeaderStaffSpace * (isFlat ? 0.90f : 0.75f));
                return OmitHeaderStaffSpace * (isFlat ? OmitHeaderAccidentalColumnSpaces : 0.85f);
            }

            float fontSize = BodyAccidentalFontSize(isFlat, isNatural);
            string bodySmufl = isFlat ? "\uE260" : isNatural ? "\uE261" : "\uE262";
            if (SmuFLGlyphMetrics.TryMeasure(bodySmufl, fontSize, out var measured))
            {
                float fallback = CompactAccidentalRefPx * (_layout.Sls / 12f) * BodyAccidentalGlyphScale()
                    * (isFlat ? BodyFlatSizeBoost : 1f);
                return Math.Max(measured.Width, fallback);
            }

            return CompactAccidentalRefPx * (_layout.Sls / 12f) * BodyAccidentalGlyphScale()
                * (isFlat ? BodyFlatSizeBoost : 1f);
        }

        private static bool IsFlatBodyAccidental(Accidental acc)
            => acc == Accidental.Flat || acc == Accidental.DoubleFlat;

        private static bool IsNaturalBodyAccidental(Accidental acc)
            => acc == Accidental.Natural;

        /// <summary>Layout/draw width for a body accidental beside the notehead.</summary>
        private float BodyAccidentalDrawWidth(bool isFlat, bool isNatural = false)
            => isFlat
                ? BodyAccidentalSymbolWidth(isFlat: true)
                : BodyAccidentalSymbolWidth(isNatural: isNatural) * (isNatural ? 0.92f : 1.0f);

        /// <summary>Left edge of the drawn notehead ellipse (matches <see cref="DrawNote"/>).</summary>
        private float NoteHeadVisualHalfWidth()
            => OmitStaffHeader ? OmitHeaderNoteHeadHalfWidth : NoteHeadDrawR(filled: true);

        private float NoteHeadLeft(float centerX) => centerX - NoteHeadVisualHalfWidth();

        /// <summary>Gap before notehead; naturals use a tighter gap than sharps/flats.</summary>
        private float BodyAccidentalRightGapPxFor(bool isNatural, bool isFlat = false)
        {
            if (OmitStaffHeader)
                return OmitHeaderAccidentalGap;
            float gap = isNatural
                ? Math.Max(2f * BodyAccidentalGlyphScale(), AccidentalRightGap * _layout.Sls * 0.06f)
                : BodyAccidentalRightGapPx;
            gap = Math.Max(gap, MinimumAccidentalNoteGap);
            if (isFlat)
                gap += FlatAccidentalExtraInkPad;
            return gap;
        }

        private float AccidentalBoxRight(float noteCenterX, bool isFlat, bool isNatural = false)
            => NoteHeadLeft(noteCenterX) - BodyAccidentalRightGapPxFor(isNatural, isFlat);

        /// <summary>Left edge of accidental draw box; right edge clears the notehead by <see cref="BodyAccidentalRightGapPxFor"/>.</summary>
        private float AccidentalBoxLeft(float noteCenterX, bool isFlat, bool isNatural = false)
            => AccidentalBoxRight(noteCenterX, isFlat, isNatural) - BodyAccidentalDrawWidth(isFlat, isNatural);

        private void ResolveBodyAccidentalHorizontalBounds(
            float noteCenterX,
            bool isFlat,
            bool isNatural,
            out float boxLeft,
            out float boxRight)
        {
            boxRight = AccidentalBoxRight(noteCenterX, isFlat, isNatural);
            boxLeft = boxRight - BodyAccidentalDrawWidth(isFlat, isNatural);
        }

        /// <summary>Leftmost ink edge of a note/rest group (accidental or notehead).</summary>
        private float NoteGroupLeft(float centerX, bool isRest, bool hasAcc, bool isFlatAcc, bool isNaturalAcc = false, NoteDuration duration = NoteDuration.Quarter)
            => hasAcc ? AccidentalBoxLeft(centerX, isFlatAcc, isNaturalAcc) : centerX - NoteHalfWidth(isRest, duration);

        private float NoteGroupLeftFromLayout(NoteLayout layout)
            => NoteGroupLeft(layout.X, layout.IsRest, layout.HasAccidental, layout.AccidentalIsFlat, layout.AccidentalIsNatural, layout.Duration);

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
        private float NoteCenterLeftReach(bool isRest, bool hasAcc, bool isFlatAcc, bool isNaturalAcc = false, NoteDuration duration = NoteDuration.Quarter)
            => hasAcc
                ? NoteHeadVisualHalfWidth() + BodyAccidentalRightGapPxFor(isNaturalAcc, isFlatAcc) + BodyAccidentalDrawWidth(isFlatAcc, isNaturalAcc)
                  + AccidentalLeadingInkPad
                : NoteHalfWidth(isRest, duration);

        /// <summary>Distance from note center to its right drawable edge.</summary>
        private float NoteCenterTrailingReach(bool isRest, NoteDuration duration = NoteDuration.Quarter)
            => isRest
                ? RestInkHalfWidth(duration)
                : _layout.NoteHeadR + Math.Max(5f, 3f * _layout.GlyphScale);

        /// <summary>Half-width of a ledger line, matching <see cref="DrawLedgerLines"/>.</summary>
        private float NoteLedgerHalfWidth()
            => OmitStaffHeader
                ? OmitHeaderNoteHeadHalfWidth * 1.35f
                : _layout.NoteHeadR * 2.2f;

        /// <summary>
        /// Rightmost ink for a laid-out note, including ledger lines. Used for end-bar and
        /// clamp extent so high/low notes do not paint past the last bar. Inter-note spacing
        /// still uses <see cref="NoteCenterTrailingReach"/> (stem/head only).
        /// </summary>
        private float NoteInkRightForLayout(NoteLayout layout)
            => layout.IsRest
                ? NoteTrailingRight(true, layout.X, layout.Duration)
                : layout.X + Math.Max(NoteCenterTrailingReach(false, layout.Duration), NoteLedgerHalfWidth());

        private void SyncAccidentalX(NoteLayout[] noteLayouts, int index)
        {
            if (noteLayouts[index].HasAccidental)
            {
                ResolveBodyAccidentalHorizontalBounds(
                    noteLayouts[index].X,
                    noteLayouts[index].AccidentalIsFlat,
                    noteLayouts[index].AccidentalIsNatural,
                    out float boxLeft,
                    out _);
                noteLayouts[index].AccidentalX = boxLeft;
            }
        }

        /// <summary>Shifts every item in a measure forward so its left ink clears <paramref name="minGroupLeft"/>.</summary>
        private void ShiftMeasureIndicesToMinGroupLeft(
            NoteLayout[] noteLayouts,
            IReadOnlyList<int> sorted,
            float minGroupLeft)
        {
            if (sorted.Count == 0 || minGroupLeft <= float.NegativeInfinity + 1f)
                return;

            float firstGroupLeft = NoteGroupLeftFromLayout(noteLayouts[sorted[0]]);
            if (firstGroupLeft >= minGroupLeft - 0.5f)
                return;

            float shift = minGroupLeft - firstGroupLeft;
            foreach (int idx in sorted)
            {
                noteLayouts[idx].X += shift;
                SyncAccidentalX(noteLayouts, idx);
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

        /// <summary>
        /// Half-width of rest ink matching <see cref="SmuFLRestDrawer"/> (not notehead radius).
        /// Under-reserving this was the main cause of rests overlapping neighbors.
        /// </summary>
        private float RestInkHalfWidth(NoteDuration duration)
        {
            _ = duration; // width is duration-agnostic in SmuFLRestDrawer today
            float restScale = Math.Min(1f, CompactRestScale * Math.Max(0.5f, _layout.GlyphScale));
            return SmuFLRestDrawer.GetInkWidth(_layout.Sls, restScale) * 0.5f;
        }

        private float NoteHalfWidth(bool isRest, NoteDuration duration = NoteDuration.Quarter)
            => isRest ? RestInkHalfWidth(duration) : _layout.NoteHeadR + StemStrokeHalfWidth;

        /// <summary>Right edge after note/rest center (stem-ward for notes).</summary>
        private float NoteTrailingRight(bool isRest, float centerX, NoteDuration duration = NoteDuration.Quarter)
            => centerX + NoteCenterTrailingReach(isRest, duration);

        private float NoteTrailingRight(GeneratedNote note, float centerX)
            => NoteTrailingRight(note.IsRest, centerX, note.Duration);

        /// <summary>Minimum center X after <paramref name="prevRight"/> for the next item.</summary>
        private float MinCenterAfterPrevRight(float prevRight, bool isRest, bool hasAcc, bool isFlatAcc, bool isNaturalAcc = false, float? inkGap = null, NoteDuration duration = NoteDuration.Quarter)
            => prevRight + (inkGap ?? _planInkGap) + NoteCenterLeftReach(isRest, hasAcc, isFlatAcc, isNaturalAcc, duration);

        private float KeySigGlyphWidth() => KeySigSymbolWidth();

        private float ArpeggioKeySigSizeBoost()
            => _session.Tune == "Arpeggio" ? 1.05f : 1f;

        /// <summary>
        /// Horizontal advance between key-sig symbols (tight cluster).
        /// Draw box width stays <see cref="KeySigGlyphWidth"/>; slot matches V2 (~14px at sls=12).
        /// </summary>
        private float KeySigSymbolSlot()
        {
            float symW = KeySigGlyphWidth();
            return Math.Max(_layout.Sls * 0.68f, symW * 0.27f);
        }

        /// <summary>Total horizontal span of <paramref name="accCount"/> key-sig symbols (tight slots + last glyph width).</summary>
        private float KeySigDrawnWidth(int accCount)
        {
            if (accCount <= 0) return 0f;
            return (accCount - 1) * KeySigSymbolSlot() + KeySigGlyphWidth();
        }

        /// <summary>
        /// Available canvas height set by the page before calling <see cref="ComputeRequiredHeight"/>.
        /// The drawable scales staff geometry to fill this height, reserving space for the OS Practice bar.
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
            public bool AccidentalIsNatural;
            public bool IsRest;
            public NoteDuration Duration;
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

        private struct StaffLayout
        {
            public float Sls;          // pixels per staff space (line spacing)
            public float HS;           // half-space = Sls/2
            public float NoteHeadR;    // notehead radius
            public float StemLen;      // stem length
            /// <summary>1.0 at compact size; &gt;1 when level ≤30 enlarges notation.</summary>
            public float GlyphScale;
            public float UpperTop, UpperMid, UpperBot;
            public float LowerTop, LowerMid, LowerBot;
            public float TotalHeight;
        }
        private StaffLayout _layout;

        /// <summary>
        /// Cached horizontal/vertical layout so feedback redraws during listening
        /// only repaint note states without re-planning or validating the staff.
        /// </summary>
        private sealed class StaffDrawLayoutCache
        {
            public LayoutCacheKey Key;
            public StaffLayout VerticalLayout;
            public StaffHeaderMetrics HeaderMetrics;
            public float LeftMargin;
            public float UpperStaffMargin, LowerStaffMargin;
            public float UpperTop, UpperMid, UpperBot, LowerTop, LowerMid, LowerBot;
            public NoteLayout[] UpperNoteLayouts = Array.Empty<NoteLayout>();
            public BarLayout[] UpperBarLayouts = Array.Empty<BarLayout>();
            public NoteLayout[] LowerNoteLayouts = Array.Empty<NoteLayout>();
            public BarLayout[] LowerBarLayouts = Array.Empty<BarLayout>();
            public double UpperBeatOrigin, LowerBeatOrigin;
            public float SafeLeft, SafeRight, LayoutRightLimit;
            public byte[]? StaticChromePng;
            public float StaticChromeWidth, StaticChromeHeight;
        }

        private readonly struct LayoutCacheKey : IEquatable<LayoutCacheKey>
        {
            public float Width { get; init; }
            public float Height { get; init; }
            public float SafeLeftInset { get; init; }
            public float SafeRightInset { get; init; }
            public int UpperNotesHash { get; init; }
            public int LowerNotesHash { get; init; }
            public int UpperBarHash { get; init; }
            public int LowerBarHash { get; init; }
            public bool UpperHasEndBar { get; init; }
            public bool BeginnerLayout { get; init; }
            public int ChildLevel { get; init; }
            public string SessionKey { get; init; }
            public string SessionScale { get; init; }
            public string SessionTune { get; init; }
            public string TimeSig { get; init; }
            public int MusicBpm { get; init; }
            public bool ShowSignaturesOnBothStaffs { get; init; }

            public bool Equals(LayoutCacheKey other) =>
                Width == other.Width && Height == other.Height
                && SafeLeftInset == other.SafeLeftInset && SafeRightInset == other.SafeRightInset
                && UpperNotesHash == other.UpperNotesHash && LowerNotesHash == other.LowerNotesHash
                && UpperBarHash == other.UpperBarHash && LowerBarHash == other.LowerBarHash
                && UpperHasEndBar == other.UpperHasEndBar && BeginnerLayout == other.BeginnerLayout
                && ChildLevel == other.ChildLevel
                && SessionKey == other.SessionKey && SessionScale == other.SessionScale
                && SessionTune == other.SessionTune
                && TimeSig == other.TimeSig && MusicBpm == other.MusicBpm
                && ShowSignaturesOnBothStaffs == other.ShowSignaturesOnBothStaffs;

            public override bool Equals(object? obj) => obj is LayoutCacheKey other && Equals(other);
            public override int GetHashCode()
            {
                var hc = new HashCode();
                hc.Add(Width); hc.Add(Height);
                hc.Add(SafeLeftInset); hc.Add(SafeRightInset);
                hc.Add(UpperNotesHash); hc.Add(LowerNotesHash);
                hc.Add(UpperBarHash); hc.Add(LowerBarHash);
                hc.Add(UpperHasEndBar); hc.Add(BeginnerLayout); hc.Add(ChildLevel);
                hc.Add(SessionKey); hc.Add(SessionScale); hc.Add(SessionTune); hc.Add(TimeSig); hc.Add(MusicBpm);
                hc.Add(ShowSignaturesOnBothStaffs);
                return hc.ToHashCode();
            }
        }

        private StaffDrawLayoutCache? _layoutCache;

        /// <summary>Drop cached note positions (e.g. after replacing the tune).</summary>
        public void InvalidateLayoutCache() => _layoutCache = null;

        private static int HashNotes(IReadOnlyList<GeneratedNote> notes)
        {
            unchecked
            {
                int h = 17;
                for (int i = 0; i < notes.Count; i++)
                {
                    var n = notes[i];
                    h = h * 31 + n.MidiNumber;
                    h = h * 31 + (n.BeatPosition?.GetHashCode() ?? 0);
                    h = h * 31 + n.BeatDuration.GetHashCode();
                    h = h * 31 + (n.IsRest ? 1 : 0);
                    h = h * 31 + (int)n.Duration;
                }
                return h;
            }
        }

        private static int HashDoubles(IReadOnlyList<double> values)
        {
            unchecked
            {
                int h = 17;
                for (int i = 0; i < values.Count; i++)
                    h = h * 31 + values[i].GetHashCode();
                return h;
            }
        }

        private LayoutCacheKey BuildLayoutCacheKey(float width, float height, float safeLeftInset, float safeRightInset)
            => new LayoutCacheKey
            {
                Width = width,
                Height = height,
                SafeLeftInset = safeLeftInset,
                SafeRightInset = safeRightInset,
                UpperNotesHash = HashNotes(UpperNotes),
                LowerNotesHash = HashNotes(LowerNotes),
                UpperBarHash = HashDoubles(UpperBarBeats),
                LowerBarHash = HashDoubles(LowerBarBeats),
                UpperHasEndBar = UpperHasEndBar,
                BeginnerLayout = UseBeginnerHorizontalLayout,
                ChildLevel = _session.ChildLevel,
                SessionKey = ActiveNotationKey ?? string.Empty,
                SessionScale = ActiveKeySignatureScale() ?? string.Empty,
                SessionTune = _session.Tune ?? string.Empty,
                TimeSig = _session.GetDisplayTimeSignature(),
                MusicBpm = _session.MusicBpm,
                ShowSignaturesOnBothStaffs = _session.ShowSignaturesOnBothStaffs,
            };

        private bool TryDrawFromLayoutCache(
            ICanvas canvas, RectF dirtyRect, Color ink, LayoutCacheKey key)
        {
            if (_layoutCache == null || !_layoutCache.Key.Equals(key))
                return false;

            var c = _layoutCache;
            _layout = c.VerticalLayout;
            _headerMetrics = c.HeaderMetrics;
            _leftMargin = c.LeftMargin;

            BlitStaticChromeOrDrawFallback(canvas, dirtyRect, ink, c,
                c.UpperTop, c.UpperMid, c.UpperBot, c.LowerTop, c.LowerMid, c.LowerBot,
                c.UpperNoteLayouts, c.UpperBarLayouts, c.LowerNoteLayouts, c.LowerBarLayouts,
                c.SafeLeft, c.SafeRight, c.LayoutRightLimit,
                c.UpperStaffMargin, c.LowerStaffMargin);
            DrawBothStaffsDynamic(canvas, dirtyRect, ink,
                c.UpperTop, c.UpperMid, c.UpperBot, c.LowerTop, c.LowerMid, c.LowerBot,
                c.UpperNoteLayouts, c.UpperBarLayouts, c.LowerNoteLayouts, c.LowerBarLayouts,
                c.UpperBeatOrigin, c.LowerBeatOrigin,
                c.SafeLeft, c.SafeRight, c.LayoutRightLimit,
                c.UpperStaffMargin, c.LowerStaffMargin);
            return true;
        }

        private static bool TryBlitStaticChrome(ICanvas canvas, RectF dirtyRect, StaffDrawLayoutCache cache)
        {
            var png = cache.StaticChromePng;
            if (png == null || png.Length == 0)
                return false;

            try
            {
                using var ms = new MemoryStream(png);
                var image = Microsoft.Maui.Graphics.Platform.PlatformImage.FromStream(ms);
                canvas.DrawImage(image, dirtyRect.X, dirtyRect.Y, cache.StaticChromeWidth, cache.StaticChromeHeight);
                return true;
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[Staff] StaticChrome blit failed: {ex.Message}");
                return false;
            }
        }

        private void RasterizeStaticChrome(
            RectF dirtyRect, Color ink,
            float upperTop, float upperMid, float upperBot,
            float lowerTop, float lowerMid, float lowerBot,
            NoteLayout[] upperNoteLayouts, BarLayout[] upperBarLayouts,
            NoteLayout[] lowerNoteLayouts, BarLayout[] lowerBarLayouts,
            float safeLeft, float safeRight, float layoutRightLimit,
            float upperStaffMargin, float lowerStaffMargin)
        {
            if (_layoutCache == null)
                return;

            int w = Math.Max(1, (int)Math.Ceiling(dirtyRect.Width));
            int h = Math.Max(1, (int)Math.Ceiling(dirtyRect.Height));

            try
            {
                using var export = new SkiaBitmapExportContext(w, h, 1f);
                var canvas = export.Canvas;
                canvas.Translate(-dirtyRect.X, -dirtyRect.Y);
                DrawBothStaffsLinesAndBars(canvas, dirtyRect, ink,
                    upperTop, upperMid, upperBot, lowerTop, lowerMid, lowerBot,
                    upperNoteLayouts, upperBarLayouts, lowerNoteLayouts, lowerBarLayouts,
                    safeLeft, safeRight, layoutRightLimit, upperStaffMargin, lowerStaffMargin);

                using var ms = new MemoryStream();
                export.WriteToStream(ms);
                var png = ms.ToArray();
                if (png.Length == 0)
                    return;

                _layoutCache.StaticChromePng = png;
                _layoutCache.StaticChromeWidth = w;
                _layoutCache.StaticChromeHeight = h;
            }
            catch (Exception ex)
            {
                DebugLog.WriteLine($"[Staff] StaticChrome raster failed: {ex.Message}");
            }
        }

        private void StoreLayoutCache(
            LayoutCacheKey key,
            RectF dirtyRect,
            Color ink,
            float upperTop, float upperMid, float upperBot,
            float lowerTop, float lowerMid, float lowerBot,
            NoteLayout[] upperNoteLayouts, BarLayout[] upperBarLayouts,
            NoteLayout[] lowerNoteLayouts, BarLayout[] lowerBarLayouts,
            double upperBeatOrigin, double lowerBeatOrigin,
            float safeLeft, float safeRight, float layoutRightLimit,
            float upperStaffMargin, float lowerStaffMargin)
        {
            _layoutCache = new StaffDrawLayoutCache
            {
                Key = key,
                VerticalLayout = _layout,
                HeaderMetrics = _headerMetrics,
                LeftMargin = _leftMargin,
                UpperStaffMargin = upperStaffMargin,
                LowerStaffMargin = lowerStaffMargin,
                UpperTop = upperTop,
                UpperMid = upperMid,
                UpperBot = upperBot,
                LowerTop = lowerTop,
                LowerMid = lowerMid,
                LowerBot = lowerBot,
                UpperNoteLayouts = (NoteLayout[])upperNoteLayouts.Clone(),
                UpperBarLayouts = (BarLayout[])upperBarLayouts.Clone(),
                LowerNoteLayouts = (NoteLayout[])lowerNoteLayouts.Clone(),
                LowerBarLayouts = (BarLayout[])lowerBarLayouts.Clone(),
                UpperBeatOrigin = upperBeatOrigin,
                LowerBeatOrigin = lowerBeatOrigin,
                SafeLeft = safeLeft,
                SafeRight = safeRight,
                LayoutRightLimit = layoutRightLimit,
            };

            RasterizeStaticChrome(dirtyRect, ink,
                upperTop, upperMid, upperBot, lowerTop, lowerMid, lowerBot,
                upperNoteLayouts, upperBarLayouts, lowerNoteLayouts, lowerBarLayouts,
                safeLeft, safeRight, layoutRightLimit, upperStaffMargin, lowerStaffMargin);
        }

        /// <summary>
        /// Blits cached staff lines/bar lines when available; always draws clef/key/time on the live canvas
        /// because <see cref="SkiaBitmapExportContext"/> does not rasterize platform text reliably.
        /// </summary>
        private void BlitStaticChromeOrDrawFallback(
            ICanvas canvas, RectF dirtyRect, Color ink, StaffDrawLayoutCache cache,
            float upperTop, float upperMid, float upperBot,
            float lowerTop, float lowerMid, float lowerBot,
            NoteLayout[] upperNoteLayouts, BarLayout[] upperBarLayouts,
            NoteLayout[] lowerNoteLayouts, BarLayout[] lowerBarLayouts,
            float safeLeft, float safeRight, float layoutRightLimit,
            float upperStaffMargin, float lowerStaffMargin)
        {
            if (TryBlitStaticChrome(canvas, dirtyRect, cache))
            {
                DrawBothStaffsHeaderChrome(canvas, ink,
                    upperTop, upperMid, upperBot, lowerTop, lowerMid, lowerBot);
                return;
            }

            DrawBothStaffsStaticChrome(canvas, dirtyRect, ink,
                upperTop, upperMid, upperBot, lowerTop, lowerMid, lowerBot,
                upperNoteLayouts, upperBarLayouts, lowerNoteLayouts, lowerBarLayouts,
                safeLeft, safeRight, layoutRightLimit, upperStaffMargin, lowerStaffMargin);
        }

        private void DrawBothStaffsStaticChrome(
            ICanvas canvas, RectF dirtyRect, Color ink,
            float upperTop, float upperMid, float upperBot,
            float lowerTop, float lowerMid, float lowerBot,
            NoteLayout[] upperNoteLayouts, BarLayout[] upperBarLayouts,
            NoteLayout[] lowerNoteLayouts, BarLayout[] lowerBarLayouts,
            float safeLeft, float safeRight, float layoutRightLimit,
            float upperStaffMargin, float lowerStaffMargin)
        {
            DrawBothStaffsLinesAndBars(canvas, dirtyRect, ink,
                upperTop, upperMid, upperBot, lowerTop, lowerMid, lowerBot,
                upperNoteLayouts, upperBarLayouts, lowerNoteLayouts, lowerBarLayouts,
                safeLeft, safeRight, layoutRightLimit, upperStaffMargin, lowerStaffMargin);
            DrawBothStaffsHeaderChrome(canvas, ink,
                upperTop, upperMid, upperBot, lowerTop, lowerMid, lowerBot);
        }

        private void DrawBothStaffsLinesAndBars(
            ICanvas canvas, RectF dirtyRect, Color ink,
            float upperTop, float upperMid, float upperBot,
            float lowerTop, float lowerMid, float lowerBot,
            NoteLayout[] upperNoteLayouts, BarLayout[] upperBarLayouts,
            NoteLayout[] lowerNoteLayouts, BarLayout[] lowerBarLayouts,
            float safeLeft, float safeRight, float layoutRightLimit,
            float upperStaffMargin, float lowerStaffMargin)
        {
            // Empty staves must not be engraved — otherwise a short Practice Tune that
            // lands on only one staff still shows a second clef/time/tempo system.
            if (PracticeTuneStaffSplit.ShouldEngraveStaff(UpperNotes.Count))
            {
                DrawStaffLinesAndBars(canvas, ink, upperTop, upperMid, upperBot,
                    upperNoteLayouts, upperBarLayouts,
                    safeLeft, safeRight, layoutRightLimit, upperStaffMargin);
            }

            if (_session.Tune != "Tuner" && !SingleStaffLayout
                && PracticeTuneStaffSplit.ShouldEngraveStaff(LowerNotes.Count))
            {
                DrawStaffLinesAndBars(canvas, ink, lowerTop, lowerMid, lowerBot,
                    lowerNoteLayouts, lowerBarLayouts,
                    safeLeft, safeRight, layoutRightLimit, lowerStaffMargin);
            }
        }

        private void DrawBothStaffsHeaderChrome(
            ICanvas canvas, Color ink,
            float upperTop, float upperMid, float upperBot,
            float lowerTop, float lowerMid, float lowerBot)
        {
            // Ear Training compact reveal: staff lines + notes only.
            if (OmitStaffHeader)
                return;

            // BPM marking stays on the upper staff only; key/time may repeat on lower.
            // Interval Sight Training (SingleStaffLayout): clef + key only — no tempo or time sig.
            // Time-signature hit target is owned by the lower staff when it draws a meter;
            // otherwise the single engraved staff owns it. Upper staff never keeps its own target.
            bool lowerWillOwnTimeSignatureHit = _session.Tune != "Tuner" && !SingleStaffLayout
                && PracticeTuneStaffSplit.ShouldEngraveStaff(LowerNotes.Count)
                && (UpperNotes.Count == 0 || _session.ShowSignaturesOnBothStaffs);
            if (PracticeTuneStaffSplit.ShouldEngraveStaff(UpperNotes.Count))
            {
                DrawStaffHeaderChrome(canvas, ink, upperTop, upperMid, upperBot,
                    drawKeyAndTimeSig: true,
                    drawBpmMarking: !SingleStaffLayout,
                    drawTimeSignature: !SingleStaffLayout,
                    captureTimeSignatureHitTarget: !lowerWillOwnTimeSignatureHit);
            }
            if (_session.Tune != "Tuner" && !SingleStaffLayout
                && PracticeTuneStaffSplit.ShouldEngraveStaff(LowerNotes.Count))
            {
                bool drawLowerSignatures = UpperNotes.Count == 0
                    || _session.ShowSignaturesOnBothStaffs;
                DrawStaffHeaderChrome(canvas, ink, lowerTop, lowerMid, lowerBot,
                    drawKeyAndTimeSig: drawLowerSignatures,
                    drawBpmMarking: UpperNotes.Count == 0,
                    drawTimeSignature: drawLowerSignatures,
                    captureTimeSignatureHitTarget: true);
            }
        }

        private void DrawBothStaffsDynamic(
            ICanvas canvas, RectF dirtyRect, Color ink,
            float upperTop, float upperMid, float upperBot,
            float lowerTop, float lowerMid, float lowerBot,
            NoteLayout[] upperNoteLayouts, BarLayout[] upperBarLayouts,
            NoteLayout[] lowerNoteLayouts, BarLayout[] lowerBarLayouts,
            double upperBeatOrigin, double lowerBeatOrigin,
            float safeLeft, float safeRight, float layoutRightLimit,
            float upperStaffMargin, float lowerStaffMargin)
        {
            DrawStaffDynamic(canvas, dirtyRect, ink, upperTop, upperMid, upperBot,
                UpperNotes, UpperNoteStates, upperNoteLayouts, upperBarLayouts,
                UpperBarBeats, upperBeatOrigin,
                UpperAlpha, IsUpperActive, IsUpperActive ? ActiveNoteIndex : -1,
                safeLeft, safeRight, layoutRightLimit, upperStaffMargin, "upper");

            if (_session.Tune != "Tuner" && !SingleStaffLayout)
            {
                DrawStaffDynamic(canvas, dirtyRect, ink, lowerTop, lowerMid, lowerBot,
                    LowerNotes, LowerNoteStates, lowerNoteLayouts, lowerBarLayouts,
                    LowerBarBeats, lowerBeatOrigin,
                    LowerAlpha, !IsUpperActive, !IsUpperActive ? ActiveNoteIndex : -1,
                    safeLeft, safeRight, layoutRightLimit, lowerStaffMargin, "lower");
            }
        }

        // ── Constructor ───────────────────────────────────────────────────────────
        public StaffDrawable(NoteSessionService session, ThemeService theme, ISafeAreaService? safeArea = null)
        {
            _session = session;
            _theme = theme;
            _safeArea = safeArea;
        }

        public StaffMeasureSplitResult SplitMeasuresAcrossStaves(
    List<GeneratedNote> allNotes,
    IReadOnlyList<double> barBeats,
    float canvasWidth,
    float canvasHeight)
            => SplitMeasuresAcrossStaves(
                allNotes, barBeats, canvasWidth, canvasHeight, StaffMeasureSplitMode.Balanced);

        /// <summary>
        /// Measure-based two-staff assignment: compute each measure's minimum engraved width,
        /// pack whole measures onto the upper staff until the next will not fit at readable
        /// spacing, then pack as many remaining measures as fit on the lower staff.
        /// Never splits a measure. Never compresses below readable ink to force more measures.
        /// Width estimates use a provisional two-staff notation scale (matching draw) so the
        /// lower staff is not over-filled from an all-on-upper size underestimate.
        /// </summary>
        public StaffMeasureSplitResult SplitMeasuresAcrossStaves(
    List<GeneratedNote> allNotes,
    IReadOnlyList<double> barBeats,
    float canvasWidth,
    float canvasHeight,
    StaffMeasureSplitMode splitMode)
        {
            ArgumentNullException.ThrowIfNull(allNotes);
            ArgumentNullException.ThrowIfNull(barBeats);

            var result = new StaffMeasureSplitResult();

            if (allNotes.Count == 0)
                return result;

            var insets =
                _safeArea?.GetSafeAreaInsets()
                ?? (0f, 0f, 0f, 0f);

            float effectiveRightInset =
                Math.Max(0f, insets.Right - RelaxCutoutInsetRightDp);

            float safeLeft = insets.Left;

            float layoutRightLimit =
                canvasWidth - effectiveRightInset - LayoutRightPad;

            float safeWidth =
                Math.Max(64f, layoutRightLimit - safeLeft);

            var notes = allNotes;

            double beatOrigin =
                GetStaffBeatOrigin(notes, barBeats);

            var barBeatsList = barBeats is List<double> list
                ? new List<double>(list)
                : barBeats.ToList();

            barBeatsList =
                ResolveStaffBarBeats(notes, barBeatsList, beatOrigin);

            double totalBeats = 0.0;

            for (int i = 0; i < notes.Count; i++)
            {
                double relativeBeat =
                    (notes[i].BeatPosition ?? 0.0) - beatOrigin;

                totalBeats = Math.Max(
                    totalBeats,
                    relativeBeat + notes[i].BeatDuration);
            }

            var sortedBarBeats = barBeatsList
                .Select(beat => beat - beatOrigin)
                .OrderBy(beat => beat)
                .ToList();

            // Segment structure does not depend on notation scale — build first so we can
            // size NoteHeadR/Sls from a provisional two-staff split (draw uses two staves).
            // All-on-upper provisional sizing made heads too small, packing too many measures,
            // and left lower-staff ink past the usable right edge after draw-scale grew.
            var segments = BuildMeasureSegments(
                notes,
                sortedBarBeats,
                beatOrigin,
                totalBeats);

            result.TotalMeasureCount = segments.Count;

            if (segments.Count == 0)
            {
                result.UpperNotes.AddRange(notes);
                return result;
            }

            float layoutHeight = Math.Max(canvasHeight, 120f);
            int provisionalUpperMeasures = Math.Max(1, (segments.Count + 1) / 2);
            ApplyPackNotationLayout(
                layoutHeight, notes, segments, provisionalUpperMeasures);

            _headerMetrics = ComputeHeaderMetrics(safeLeft);
            // Width estimates must use the same ink gaps as live engraving.
            _planInkGap = UseBeginnerHorizontalLayout
                ? (_session.ChildLevel <= 30 ? BeginnerInkGap : MinInkGap)
                : MinInkGap;

            // Packer usable width must match the draw path for each staff.
            // When signatures appear on both staves, lower uses full LeftMargin (not clef-only).
            float upperHeaderMargin = _headerMetrics.LeftMargin;
            float lowerHeaderMargin = ResolveLowerStaffHeaderMargin(
                _headerMetrics.LeftMargin,
                _headerMetrics.ClefOnlyLeftMargin,
                _session.ShowSignaturesOnBothStaffs);
            float upperUsableWidth = Math.Max(
                64f,
                safeWidth - upperHeaderMargin - RightMargin);
            float lowerUsableWidth = Math.Max(
                64f,
                safeWidth - lowerHeaderMargin - RightMargin);

            var minimumWidths = BuildPackMeasureMinimumWidths(notes, segments, beatOrigin);

            int upperMeasureCount;
            int lowerMeasureCount;
            if (splitMode == StaffMeasureSplitMode.FillUpperFirst)
            {
                (upperMeasureCount, lowerMeasureCount) = ChooseGreedyUpperFirstSplit(
                    minimumWidths, upperUsableWidth, lowerUsableWidth);
            }
            else
            {
                // Enumerate consecutive cut points: maximize placed music, then balance.
                // Do not force balance when it would display fewer measures.
                (upperMeasureCount, lowerMeasureCount) = ChooseBalancedMeasureSplit(
                    minimumWidths,
                    upperUsableWidth,
                    lowerUsableWidth);
            }

            // Refine with the chosen cut's actual pitch ranges so min-widths match draw.
            ApplyPackNotationLayout(layoutHeight, notes, segments, upperMeasureCount);
            _headerMetrics = ComputeHeaderMetrics(safeLeft);
            upperHeaderMargin = _headerMetrics.LeftMargin;
            lowerHeaderMargin = ResolveLowerStaffHeaderMargin(
                _headerMetrics.LeftMargin,
                _headerMetrics.ClefOnlyLeftMargin,
                _session.ShowSignaturesOnBothStaffs);
            upperUsableWidth = Math.Max(64f, safeWidth - upperHeaderMargin - RightMargin);
            lowerUsableWidth = Math.Max(64f, safeWidth - lowerHeaderMargin - RightMargin);
            minimumWidths = BuildPackMeasureMinimumWidths(notes, segments, beatOrigin);

            if (splitMode == StaffMeasureSplitMode.FillUpperFirst)
            {
                (upperMeasureCount, lowerMeasureCount) = ChooseGreedyUpperFirstSplit(
                    minimumWidths, upperUsableWidth, lowerUsableWidth);
            }
            else
            {
                int refinedUpper = PackConsecutiveMeasureWidths(
                    minimumWidths, upperUsableWidth, startMeasureIndex: 0);
                if (refinedUpper <= 0)
                    refinedUpper = 1;
                refinedUpper = Math.Min(refinedUpper, upperMeasureCount);

                int refinedLower = 0;
                int remainingAfterUpper = segments.Count - refinedUpper;
                if (remainingAfterUpper > 0)
                {
                    refinedLower = PackConsecutiveMeasureWidths(
                        minimumWidths, lowerUsableWidth, startMeasureIndex: refinedUpper);
                    if (refinedLower <= 0)
                        refinedLower = 1;
                    refinedLower = Math.Min(refinedLower, remainingAfterUpper);
                }

                // Prefer the refined (draw-accurate) pack when it places fewer measures — that
                // means the first pass under-estimated ink and would have overflowed.
                if (refinedUpper + refinedLower < upperMeasureCount + lowerMeasureCount
                    || refinedUpper < upperMeasureCount)
                {
                    upperMeasureCount = refinedUpper;
                    lowerMeasureCount = refinedLower;
                }
                else
                {
                    // Same placed count: keep balanced cut but clamp lower to what still fits.
                    lowerMeasureCount = Math.Min(lowerMeasureCount, refinedLower);
                    int placedCap = refinedUpper + refinedLower;
                    if (upperMeasureCount + lowerMeasureCount > placedCap)
                    {
                        upperMeasureCount = refinedUpper;
                        lowerMeasureCount = refinedLower;
                    }
                }
            }

            int placedEnd = Math.Min(segments.Count, upperMeasureCount + lowerMeasureCount);
            lowerMeasureCount = Math.Max(0, placedEnd - upperMeasureCount);

            var upperIndices = new HashSet<int>();
            var lowerIndices = new HashSet<int>();
            var unplacedIndices = new HashSet<int>();

            for (int measureIndex = 0; measureIndex < upperMeasureCount; measureIndex++)
            {
                foreach (int noteIndex in segments[measureIndex].NoteIndices)
                    upperIndices.Add(noteIndex);
            }

            for (int measureIndex = upperMeasureCount; measureIndex < placedEnd; measureIndex++)
            {
                foreach (int noteIndex in segments[measureIndex].NoteIndices)
                    lowerIndices.Add(noteIndex);
            }

            for (int measureIndex = placedEnd; measureIndex < segments.Count; measureIndex++)
            {
                foreach (int noteIndex in segments[measureIndex].NoteIndices)
                    unplacedIndices.Add(noteIndex);
            }

            for (int noteIndex = 0; noteIndex < notes.Count; noteIndex++)
            {
                if (upperIndices.Contains(noteIndex))
                    result.UpperNotes.Add(notes[noteIndex]);
                else if (lowerIndices.Contains(noteIndex))
                    result.LowerNotes.Add(notes[noteIndex]);
                else if (unplacedIndices.Contains(noteIndex))
                    result.UnplacedNotes.Add(notes[noteIndex]);
            }

            result.UpperMeasureCount = upperMeasureCount;
            result.LowerMeasureCount = lowerMeasureCount;
            result.UnplacedMeasureCount = Math.Max(0, segments.Count - placedEnd);

            return result;
        }

        /// <summary>
        /// Sizes <see cref="_layout"/> from a provisional upper/lower measure cut so pack
        /// min-widths use the same staff-line / notehead scale as two-staff draw.
        /// </summary>
        private void ApplyPackNotationLayout(
            float layoutHeight,
            List<GeneratedNote> notes,
            IReadOnlyList<MeasureSegment> segments,
            int upperMeasureCount)
        {
            int upperEnd = Math.Clamp(upperMeasureCount, 0, segments.Count);
            var upper = new List<GeneratedNote>();
            var lower = new List<GeneratedNote>();
            for (int m = 0; m < segments.Count; m++)
            {
                var target = m < upperEnd ? upper : lower;
                foreach (int noteIndex in segments[m].NoteIndices)
                    target.Add(notes[noteIndex]);
            }

            if (upper.Count == 0 && lower.Count > 0)
            {
                // Degenerate: treat everything as upper for vertical metrics.
                ComputeLayout(layoutHeight, lower, Array.Empty<GeneratedNote>());
                return;
            }

            ComputeLayout(layoutHeight, upper, lower);
        }

        /// <summary>
        /// Per-measure minimum widths using the current <see cref="_layout"/> / <see cref="_planInkGap"/>,
        /// matching <see cref="PlanHorizontalLayout"/> beginner floors.
        /// </summary>
        private float[] BuildPackMeasureMinimumWidths(
            List<GeneratedNote> notes,
            IReadOnlyList<MeasureSegment> segments,
            double beatOrigin)
        {
            var minimumWidths = new float[segments.Count];
            for (int measureIndex = 0; measureIndex < segments.Count; measureIndex++)
            {
                double segBeats = segments[measureIndex].EndBeat - segments[measureIndex].StartBeat;
                if (segBeats < 1e-9)
                    segBeats = 1;
                float beatLaneMin = (float)segBeats * (_layout.NoteHeadR * 2.2f + _planInkGap * 0.45f)
                                    + BarLeftPadding * 2f;
                float engraved = ComputeMeasureMinWidth(
                    notes,
                    segments[measureIndex],
                    beatOrigin);
                minimumWidths[measureIndex] = Math.Max(beatLaneMin, engraved);
            }

            return minimumWidths;
        }

        /// <summary>
        /// Chooses a consecutive upper/lower cut among pack-valid prefixes.
        /// Scoring: (1) maximize placed measures, (2) minimize |U−L|,
        /// (3) maximize min(U,L), (4) prefer U nearest placed/2.
        /// Never prefers a more balanced cut that places fewer measures.
        /// </summary>
        internal static (int UpperCount, int LowerCount) ChooseBalancedMeasureSplit(
            IReadOnlyList<float> minimumWidths,
            float upperUsableWidth,
            float lowerUsableWidth)
        {
            int n = minimumWidths.Count;
            if (n <= 0)
                return (0, 0);

            int bestU = 1;
            int bestL = 0;
            int bestPlaced = -1;
            int bestBal = int.MaxValue;
            int bestMinSide = -1;
            double bestDistFromHalf = double.MaxValue;

            int upperPackMax = PackConsecutiveMeasureWidths(
                minimumWidths, upperUsableWidth, startMeasureIndex: 0);
            if (upperPackMax <= 0)
                upperPackMax = 1;

            for (int u = 1; u <= n; u++)
            {
                if (u > upperPackMax)
                    continue;

                int rem = n - u;
                int l = rem == 0
                    ? 0
                    : PackConsecutiveMeasureWidths(
                        minimumWidths, lowerUsableWidth, startMeasureIndex: u);
                if (rem > 0 && l == 0)
                    l = 1;
                l = Math.Min(l, rem);

                int placed = u + l;
                int bal = Math.Abs(u - l);
                int minSide = Math.Min(u, l);
                double distFromHalf = Math.Abs(u - placed / 2.0);

                bool better =
                    placed > bestPlaced
                    || (placed == bestPlaced && bal < bestBal)
                    || (placed == bestPlaced && bal == bestBal && minSide > bestMinSide)
                    || (placed == bestPlaced && bal == bestBal && minSide == bestMinSide
                        && distFromHalf < bestDistFromHalf - 1e-9);

                if (!better)
                    continue;

                bestPlaced = placed;
                bestBal = bal;
                bestMinSide = minSide;
                bestDistFromHalf = distFromHalf;
                bestU = u;
                bestL = l;
            }

            return (bestU, bestL);
        }

        /// <summary>
        /// Pack as many whole measures as fit on the upper staff, then pack the remainder
        /// onto the lower staff. Prefers filling upper space before wrapping (saved tunes).
        /// </summary>
        internal static (int UpperCount, int LowerCount) ChooseGreedyUpperFirstSplit(
            IReadOnlyList<float> minimumWidths,
            float upperUsableWidth,
            float lowerUsableWidth)
        {
            int n = minimumWidths.Count;
            if (n <= 0)
                return (0, 0);

            int u = PackConsecutiveMeasureWidths(minimumWidths, upperUsableWidth, 0);
            if (u <= 0)
                u = 1;
            u = Math.Min(u, n);

            int rem = n - u;
            if (rem <= 0)
                return (u, 0);

            int l = PackConsecutiveMeasureWidths(minimumWidths, lowerUsableWidth, u);
            if (l <= 0)
                l = 1;
            l = Math.Min(l, rem);
            return (u, l);
        }

        /// <summary>
        /// Packs consecutive whole measures onto one staff row.  Returns the number of
        /// measures placed (may be zero when <paramref name="startMeasureIndex"/> is past the end).
        /// </summary>
        private int PackMeasuresOntoStaff(
    IReadOnlyList<MeasureSegment> segments,
    IReadOnlyList<float> minimumWidths,
    float usableWidth,
    int startMeasureIndex)
        {
            if (segments.Count == 0)
                return 0;

            if (minimumWidths.Count != segments.Count)
            {
                throw new ArgumentException(
                    "The measure-width count must equal the segment count.",
                    nameof(minimumWidths));
            }

            return PackConsecutiveMeasureWidths(minimumWidths, usableWidth, startMeasureIndex);
        }

        /// <summary>
        /// Greedy consecutive pack of measure minimum widths onto one staff row.
        /// The first candidate measure is always taken (even if wider than the staff).
        /// Uses <see cref="MaxSafePackableLocalWidth"/> so no packed prefix forces
        /// horizontal scale below <see cref="MinimumSafeHorizontalScale"/>.
        /// </summary>
        internal static int PackConsecutiveMeasureWidths(
            IReadOnlyList<float> minimumWidths,
            float usableWidth,
            int startMeasureIndex)
        {
            if (minimumWidths.Count == 0)
                return 0;

            if (startMeasureIndex < 0 || startMeasureIndex >= minimumWidths.Count)
                return 0;

            float maxLocalSpan = MaxSafePackableLocalWidth(usableWidth);
            float usedWidth = 0f;
            int placed = 0;

            for (int measureIndex = startMeasureIndex;
                 measureIndex < minimumWidths.Count;
                 measureIndex++)
            {
                float measureWidth = minimumWidths[measureIndex];
                float remainingWidth = maxLocalSpan - usedWidth;

                bool mustWrap =
                    placed > 0 &&
                    measureWidth > remainingWidth + 0.5f;

#if DEBUG
                LogMeasureLayout(
                    measureIndex + 1,
                    measureWidth,
                    remainingWidth,
                    mustWrap);
#endif

                if (mustWrap)
                    break;

                usedWidth += measureWidth;
                placed++;
            }

            return placed;
        }

#if DEBUG
        private static void LogMeasureLayout(int measureNumber, float width, float remainingStaffWidth, bool wrapped)
            => Utilities.DebugTestLog.Write(
                $"[MeasureLayout] Measure={measureNumber} Width={width:F0} RemainingStaffWidth={remainingStaffWidth:F0} Wrapped={wrapped}");
#endif

        // ── Ordered layout pipeline ──────────────────────────────────────────────
        // Approximate height of the iOS/Android system Practice-indicator bar at the bottom of the screen.
        private const float BottomBarReserve = 34f;
        /// <summary>Music two-staff minimum staff-line spacing (px).</summary>
        private const float MusicMinStaffLineSpacing = 6f;
        /// <summary>
        /// Music two-staff maximum staff-line spacing (px). Was 12, which left extra
        /// canvas height as empty vertical slack. 18 enlarges staff spacing, noteheads,
        /// clefs, accidentals, rests, stems, beams and labels together. Pack and draw
        /// both hit this cap on typical phone heights, so MinInkGap packing stays aligned.
        /// Sight Training uses a separate single-staff clamp.
        /// </summary>
        internal const float MusicMaxStaffLineSpacing = 18f;
        private const float SingleStaffMinStaffLineSpacing = 8f;
        private const float SingleStaffMaxStaffLineSpacing = 22f;

        /// <summary>Minimum gap between a note-name label and the top/bottom safe drawing edge.</summary>
        internal const float NoteNameMinEdgeClearancePx = 10f;
        internal const float NoteNameLabelWidth = 28f;
        internal const float NoteNameLabelHeight = 14f;
        private const float NoteNameAboveGap = 15f;
        private const float NoteNameBelowGap = 5f;
        private const float NoteNameBesideGap = 4f;

        internal enum NoteNameLabelSide
        {
            Above,
            Below,
            BesideRight,
            BesideLeft,
        }

        internal readonly record struct NoteNameLabelLayout(
            float X,
            float Y,
            float Width,
            float Height,
            NoteNameLabelSide Side);

        private void ComputeLayout(float availableHeight)
        {
            ComputeLayout(availableHeight, UpperNotes, LowerNotes);
        }
        private void ComputeLayout( float availableHeight,
                                    IReadOnlyList<GeneratedNote> upperNotes,
                                    IReadOnlyList<GeneratedNote> lowerNotes)
        {
            if (availableHeight <= 0f)
                availableHeight = 300f;

            // Reserve space for the OS Practice-indicator bar so notes are
            // never hidden behind it.
            float usableH = Math.Max(1f, availableHeight - BottomBarReserve);

            // Diatonic-step range for each staff.
            int minS1 = 0;
            int maxS1 = 0;
            int minS2 = 0;
            int maxS2 = 0;

            bool hasU = false;
            bool hasL = false;

            foreach (var note in upperNotes)
            {
                if (note.IsRest)
                    continue;

                var (letter, octave) = ResolveStaffLetterOctave(note);
                int steps = DiatonicStepsFromB4(letter, octave);

                if (!hasU)
                {
                    minS1 = steps;
                    maxS1 = steps;
                    hasU = true;
                }
                else
                {
                    minS1 = Math.Min(minS1, steps);
                    maxS1 = Math.Max(maxS1, steps);
                }
            }

            foreach (var note in lowerNotes)
            {
                if (note.IsRest)
                    continue;

                var (letter, octave) = ResolveStaffLetterOctave(note);
                int steps = DiatonicStepsFromB4(letter, octave);

                if (!hasL)
                {
                    minS2 = steps;
                    maxS2 = steps;
                    hasL = true;
                }
                else
                {
                    minS2 = Math.Min(minS2, steps);
                    maxS2 = Math.Max(maxS2, steps);
                }
            }

            // Half-spaces of clearance beyond the five staff lines.
            const int breathing = 3;

            int eA1 = Math.Max(0, -4 - minS1) + breathing;
            int eB1 = Math.Max(0, maxS1 - 4) + breathing;
            int eA2 = Math.Max(0, -4 - minS2) + breathing;
            int eB2 = Math.Max(0, maxS2 - 4) + breathing;

            // Ear Training side staff: keep the five lines at a fixed Y. Ledger
            // room still exists in the centered slack; it must not resize/shift
            // the staff when the interval range changes.
            if (OmitStaffHeader)
            {
                eA1 = breathing;
                eB1 = breathing;
                eA2 = 0;
                eB2 = 0;
            }

            // Reserve room for the BPM marking above the upper staff (Music only).
            if (_session.Tune != "Tuner" && !SingleStaffLayout)
                eA1 = Math.Max(eA1, 9);

            bool tunerSingleStaff = SingleStaffLayout || _session.Tune == "Tuner";

            float totalHalfSpaces = tunerSingleStaff
                ? 8f + eA1 + eB1
                : 8f + eA1 + eB1 + 8f + eA2 + eB2;

            float sls = usableH / (totalHalfSpaces / 2f);
            // Sight Training: allow substantially larger staff spacing for readability.
            // Ear Training headerless panel: slightly tighter so two notes fit beside buttons.
            float minSls = OmitStaffHeader ? 6.5f : SingleStaffMinStaffLineSpacing;
            float maxSls = OmitStaffHeader ? 10.5f : SingleStaffMaxStaffLineSpacing;
            sls = SingleStaffLayout
                ? Math.Clamp(sls, minSls, maxSls)
                : Math.Clamp(sls, MusicMinStaffLineSpacing, MusicMaxStaffLineSpacing);

            float hs = sls / 2f;

            float compactNoteHeadR = sls * CompactNoteHeadRRatio;

            float noteHeadR;
            float glyphScale;
            if (OmitStaffHeader)
            {
                glyphScale = Math.Max(1f, sls * OmitHeaderStemThicknessSpaces) * 0.5f;
                noteHeadR = sls * OmitHeaderNoteHeadWidthSpaces * 0.5f;
            }
            else
            {
                noteHeadR = SingleStaffLayout
                    ? sls * BeginnerNoteHeadSpaceRatio / NoteHeadHeightFactor
                    : UseBeginnerNotationScale
                        ? sls * BeginnerNoteHeadSpaceRatio / NoteHeadHeightFactor
                        : _session.ChildLevel > 30
                            ? sls * MidLevelNoteHeadRRatio
                            : compactNoteHeadR;
                glyphScale = noteHeadR / Math.Max(0.01f, compactNoteHeadR);
            }

            float stemLen = OmitStaffHeader
                ? OmitHeaderStemLenSpaces * sls
                : (UseBeginnerNotationScale || SingleStaffLayout)
                    ? 3.5f * sls
                    : sls * CompactStemLenRatio * glyphScale;

            float upperTop = eA1 * hs;
            float upperMid = upperTop + 2f * sls;
            float upperBot = upperTop + 4f * sls;

            float lowerTop = upperBot + eB1 * hs + eA2 * hs;
            float lowerMid = lowerTop + 2f * sls;
            float lowerBot = lowerTop + 4f * sls;

            float contentH = (SingleStaffLayout || _session.Tune == "Tuner")
                ? upperBot + eB1 * hs
                : lowerBot + eB2 * hs;

            float slack = Math.Max(0f, usableH - contentH);
            float verticalOffset = slack / 2f;

            _layout = new StaffLayout
            {
                Sls = sls,
                HS = hs,
                NoteHeadR = noteHeadR,
                StemLen = stemLen,
                GlyphScale = glyphScale,

                UpperTop = upperTop + verticalOffset,
                UpperMid = upperMid + verticalOffset,
                UpperBot = upperBot + verticalOffset,

                LowerTop = lowerTop + verticalOffset,
                LowerMid = lowerMid + verticalOffset,
                LowerBot = lowerBot + verticalOffset,

                TotalHeight = contentH + verticalOffset
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

        private static bool IsBeamableNote(GeneratedNote note)
            => !note.IsRest
               && (note.Duration == NoteDuration.Eighth || note.Duration == NoteDuration.Sixteenth);

        /// <summary>True when <paramref name="beatPos"/> lies in the half-open beat window [start, end).</summary>
        private static bool IsInBeamBeatWindow(double beatPos, double groupStart, double groupEnd)
            => beatPos >= groupStart - 1e-6 && beatPos < groupEnd - 1e-6;

        /// <summary>Beat-only beam membership for layout (same window rules as <see cref="ComputeBeamGroups"/>).</summary>
        private static Dictionary<int, int> ComputeLayoutBeamGroupIds(
            IReadOnlyList<GeneratedNote> notes,
            IReadOnlyList<int> beatOrder,
            double beatOrigin,
            double measureEndBeat)
        {
            var groupIds = new Dictionary<int, int>();
            int groupId = 0;
            int oi = 0;

            while (oi < beatOrder.Count)
            {
                int i = beatOrder[oi];
                var n = notes[i];
                double pos = (n.BeatPosition ?? 0.0) - beatOrigin;

                bool isBeamable = IsBeamableNote(n);
                if (!isBeamable)
                {
                    oi++;
                    continue;
                }

                double groupStart = Math.Floor(pos + 1e-9);
                double groupEnd = groupStart + 1.0;
                groupEnd = Math.Min(groupEnd, measureEndBeat - 1e-6);
                if (groupEnd <= groupStart + 1e-6)
                {
                    oi++;
                    continue;
                }

                var groupIndices = new List<int> { i };
                int j = oi + 1;
                while (j < beatOrder.Count)
                {
                    int noteIdx = beatOrder[j];
                    var nj = notes[noteIdx];
                    double pj = (nj.BeatPosition ?? groupEnd) - beatOrigin;

                    if (!IsBeamableNote(nj))
                        break;
                    if (pj >= measureEndBeat - 1e-6)
                        break;
                    if (!IsInBeamBeatWindow(pj, groupStart, groupEnd))
                        break;

                    groupIndices.Add(noteIdx);
                    j++;
                }

                if (groupIndices.Count >= 2)
                {
                    foreach (int idx in groupIndices)
                        groupIds[idx] = groupId;
                    groupId++;
                }

                oi = j > oi + 1 ? j : oi + 1;
            }

            return groupIds;
        }

        private static bool ShareLayoutBeamGroup(
            IReadOnlyDictionary<int, int> beamGroups, int prevIdx, int curIdx)
            => beamGroups.TryGetValue(prevIdx, out int g1)
               && beamGroups.TryGetValue(curIdx, out int g2)
               && g1 == g2;

        private float InkGapBetween(
            IReadOnlyDictionary<int, int> beamGroups,
            int prevIdx,
            int curIdx,
            float externalGap,
            bool needsAccidentalClearance = false,
            bool eitherIsRest = false)
        {
            // Rests are never part of a beam group optically — never use the 2px beamed gap beside them.
            float gap = (!eitherIsRest && ShareLayoutBeamGroup(beamGroups, prevIdx, curIdx))
                ? BeamedInternalInkGap
                : externalGap;
            if (needsAccidentalClearance || eitherIsRest)
                gap = Math.Max(gap, eitherIsRest ? Math.Max(externalGap, MinInkGap) : AccidentalPairInkGap);
            if (needsAccidentalClearance)
                gap = Math.Max(gap, AccidentalPairInkGap);
            return gap;
        }

        private float ComputeMeasureMinWidth(
            List<GeneratedNote> notes,
            MeasureSegment segment,
            double beatOrigin)
        {
            if (segment.NoteIndices.Count == 0)
                return BarLeftPadding + 16f;

            var sorted = SortIndicesByBeat(notes, segment.NoteIndices, beatOrigin);
            // Match sequential engraving: packed span already includes each event's trailing
            // reach before the next gap (see ComputeMinimumPackedSpan). Do not rebuild a
            // leftReach-only chain that omits intermediate right-side ink.
            double relBeat0 = (notes[sorted[0]].BeatPosition ?? 0.0) - beatOrigin - segment.StartBeat;
            float startPad = BarStemClearance + (relBeat0 < 1e-6 ? MeasureStartExtraPad : 0f);
            float packedSpan = ComputeMinimumPackedSpan(notes, sorted, beatOrigin, segment.EndBeat);
            return BarLeftPadding + startPad + packedSpan + BarLeftPadding * 0.5f;
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

        /// <summary>Left-to-last-right span when notes are packed at minimum ink gaps.</summary>
        private float ComputeMinimumPackedSpan(
            IReadOnlyList<GeneratedNote> notes,
            IReadOnlyList<int> sorted,
            double beatOrigin,
            double measureEndBeat)
        {
            if (sorted.Count == 0)
                return 0f;

            var beamGroups = ComputeLayoutBeamGroupIds(notes, sorted, beatOrigin, measureEndBeat);
            float prevRight = float.NegativeInfinity;
            float lastRight = 0f;
            var accHistory = new Dictionary<(char, int), Accidental>();
            var barCancelled = new HashSet<(char, int)>();

            for (int k = 0; k < sorted.Count; k++)
            {
                int i = sorted[k];
                var note = notes[i];
                var acc = ResolveLayoutAccidental(note, accHistory, barCancelled);

                float center;
                if (k == 0)
                {
                    center = NoteCenterLeftReach(note.IsRest, acc.HasAccidental, acc.IsFlat, acc.IsNatural, note.Duration);
                }
                else
                {
                    var prev = notes[sorted[k - 1]];
                    float gap = InkGapBetween(
                        beamGroups, sorted[k - 1], i, _planInkGap,
                        needsAccidentalClearance: acc.HasAccidental,
                        eitherIsRest: note.IsRest || prev.IsRest);
                    center = MinCenterAfterPrevRight(
                        prevRight, note.IsRest, acc.HasAccidental, acc.IsFlat, acc.IsNatural, gap, note.Duration);
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
            float targetRight,
            double beatOrigin,
            double measureEndBeat)
        {
            if (sortedIndices.Count < 2)
                return;

            int firstIdx = sortedIndices[0];
            var firstLayout = noteLayouts[firstIdx];
            float firstGroupLeft = NoteGroupLeftFromLayout(firstLayout);
            float firstX = firstLayout.X;
            float lastRight = NoteTrailingRight(notes[sortedIndices[^1]], noteLayouts[sortedIndices[^1]].X);
            float span = lastRight - firstGroupLeft;
            if (span <= 1f || lastRight <= targetRight)
                return;

            float minSpan = ComputeMinimumPackedSpan(notes, sortedIndices, beatOrigin, measureEndBeat);
            float available = targetRight - firstGroupLeft;
            float fitScale = ComputeMeasureFitScale(available, span, minSpan);

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
            float inkGap,
            double beatOrigin = 0.0,
            IReadOnlyList<double>? sortedBarBeatsRel = null)
        {
            float prevRight = float.NegativeInfinity;
            int prevMeasure = -1;
            for (int k = 0; k < sorted.Count; k++)
            {
                int i = sorted[k];
                var note = notes[i];
                bool isRest = noteLayouts[i].IsRest;
                bool hasAcc = noteLayouts[i].HasAccidental;
                bool isFlat = noteLayouts[i].AccidentalIsFlat;
                bool isNatural = noteLayouts[i].AccidentalIsNatural;

                if (sortedBarBeatsRel != null)
                {
                    double relBeat = (note.BeatPosition ?? 0.0) - beatOrigin;
                    int measure = GetMeasureIndexForBeat(relBeat, sortedBarBeatsRel);
                    if (prevMeasure >= 0 && measure != prevMeasure)
                        prevRight = float.NegativeInfinity;
                    prevMeasure = measure;
                }

                if (prevRight > float.NegativeInfinity)
                {
                    float gap = inkGap;
                    if (hasAcc)
                        gap = Math.Max(gap, AccidentalPairInkGap);
                    float minLeft = prevRight + gap;
                    float curLeft = NoteGroupLeft(noteLayouts[i].X, isRest, hasAcc, isFlat, isNatural, note.Duration);
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
            float inkGap = MinInkGap,
            IReadOnlyList<double>? barBeats = null)
        {
            if (notes.Count == 0 || noteLayouts.Length == 0)
                return;

            var order = Enumerable.Range(0, notes.Count)
                .OrderBy(i => (notes[i].BeatPosition ?? 0.0) - beatOrigin)
                .ThenBy(i => i)
                .ToList();

            var sortedBarBeatsRel = barBeats?.Select(b => b - beatOrigin).OrderBy(b => b).ToList();
            EnforceGroupOrderSpacingOnIndices(notes, noteLayouts, order, inkGap, beatOrigin, sortedBarBeatsRel);
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
            float barLineX,
            double beatOrigin,
            double measureEndBeat)
        {
            float maxTrailing = barLineX - BarLeftPadding;
            float compressTarget = Math.Min(innerRight - BarLeftPadding * 0.5f, maxTrailing);

            EnforceMeasureNoteGaps(notes, noteLayouts, sorted);
            RedistributeMeasureLeftoverSpace(notes, noteLayouts, sorted, innerLeft, maxTrailing, beatOrigin);
            EnforceMeasureNoteGaps(notes, noteLayouts, sorted);

            float trailing = NoteTrailingRight(notes[sorted[^1]], noteLayouts[sorted[^1]].X);
            if (trailing > compressTarget + 0.5f)
                CompressMeasureNoteSpan(notes, noteLayouts, sorted, compressTarget, beatOrigin, measureEndBeat);

            EnforceMeasureNoteGaps(notes, noteLayouts, sorted);
            ClampMeasureTrailingBeforeBar(notes, noteLayouts, sorted, maxTrailing, float.NegativeInfinity,
                beatOrigin, measureEndBeat);
        }

        /// <summary>
        /// After minimum ink gaps are satisfied, spreads any leftover interior width across
        /// inter-note gaps in proportion to the beat distance between consecutive onsets so
        /// sparse measures do not leave a single dead zone before the bar line.
        /// </summary>
        private void RedistributeMeasureLeftoverSpace(
            IReadOnlyList<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            IReadOnlyList<int> sorted,
            float innerLeft,
            float maxTrailingRight,
            double beatOrigin)
        {
            if (sorted.Count < 2)
                return;

            float firstGroupLeft = NoteGroupLeftFromLayout(noteLayouts[sorted[0]]);
            if (innerLeft > float.NegativeInfinity && firstGroupLeft < innerLeft)
            {
                float dx = innerLeft - firstGroupLeft;
                for (int k = 0; k < sorted.Count; k++)
                {
                    int i = sorted[k];
                    noteLayouts[i].X += dx;
                    SyncAccidentalX(noteLayouts, i);
                }
                firstGroupLeft = innerLeft;
            }

            float lastTrailing = NoteTrailingRight(notes[sorted[^1]], noteLayouts[sorted[^1]].X);
            float packed = lastTrailing - firstGroupLeft;
            float available = maxTrailingRight - firstGroupLeft;
            float leftover = available - packed;
            if (leftover < 1f || packed < 1f)
                return;

            var weights = new double[sorted.Count - 1];
            double weightSum = 0.0;
            for (int k = 0; k < sorted.Count - 1; k++)
            {
                double b0 = (notes[sorted[k]].BeatPosition ?? 0.0) - beatOrigin;
                double b1 = (notes[sorted[k + 1]].BeatPosition ?? 0.0) - beatOrigin;
                // Prefer onset gap; fall back to the leading event's duration so tied-onsets still get share.
                double w = Math.Max(b1 - b0, notes[sorted[k]].BeatDuration);
                w = Math.Max(w, 1e-3);
                weights[k] = w;
                weightSum += w;
            }

            if (weightSum < 1e-9)
                return;

            float cumulative = 0f;
            for (int k = 1; k < sorted.Count; k++)
            {
                cumulative += leftover * (float)(weights[k - 1] / weightSum);
                int i = sorted[k];
                noteLayouts[i].X += cumulative;
                SyncAccidentalX(noteLayouts, i);
            }
        }

        /// <summary>Ensures no note/rest in a measure extends past the following bar line.</summary>
        private void ClampMeasureTrailingBeforeBar(
            IReadOnlyList<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            IReadOnlyList<int> sorted,
            float maxTrailingRight,
            float minGroupLeft = float.NegativeInfinity,
            double beatOrigin = 0,
            double measureEndBeat = double.PositiveInfinity)
        {
            if (sorted.Count == 0)
                return;

            ShiftMeasureIndicesToMinGroupLeft(noteLayouts, sorted, minGroupLeft);

            int lastIdx = sorted[^1];
            float trailing = NoteTrailingRight(notes[lastIdx], noteLayouts[lastIdx].X);
            if (trailing <= maxTrailingRight)
                return;

            int firstIdx = sorted[0];
            float firstGroupLeft = NoteGroupLeftFromLayout(noteLayouts[firstIdx]);
            float firstX = noteLayouts[firstIdx].X;
            float span = trailing - firstGroupLeft;
            float available = maxTrailingRight - firstGroupLeft;
            if (span <= 1f)
                return;

            float minSpan = ComputeMinimumPackedSpan(notes, sorted, beatOrigin, measureEndBeat);
            float fitScale = ComputeMeasureFitScale(available, span, minSpan);

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
        /// Header margin used for the lower staff when packing and drawing.
        /// Matches draw: full key/time when <paramref name="showSignaturesOnBothStaffs"/>;
        /// otherwise clef-only under a populated upper staff.
        /// </summary>
        internal static float ResolveLowerStaffHeaderMargin(
            float leftMargin,
            float clefOnlyLeftMargin,
            bool showSignaturesOnBothStaffs)
            => showSignaturesOnBothStaffs ? leftMargin : clefOnlyLeftMargin;

        /// <summary>
        /// Uniform measure scale that fits trailing ink inside <paramref name="available"/> width.
        /// Avoids <see cref="Math.Clamp(Single,Single,Single)"/> when min packed span ≈ current span (fp).
        /// Never crushes below the packed minimum ink span — prefer overflow over overlapping glyphs.
        /// </summary>
        private static float ComputeMeasureFitScale(float available, float span, float minSpan)
        {
            if (span <= 1e-3f)
                return 1f;

            float raw = available / span;
            if (available <= 0f)
                return Math.Min(1f, raw);

            // Content cannot fit at packed min gaps: do not crush (allow overflow).
            if (minSpan > available + 0.5f)
                return 1f;

            float minScale = minSpan / span;
            if (minScale > 1f + 1e-5f)
                return Math.Min(1f, raw);

            float floor = Math.Max(HorizontalCompressFloor, Math.Min(1f, minScale));
            return Math.Clamp(raw, floor, 1f);
        }

        /// <summary>Forward pass in beat order — enforces <see cref="MinInkGap"/> between items. Staff-local only.</summary>
        private void EnforceGlobalBeatOrderSpacing(
            IReadOnlyList<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            double beatOrigin,
            float inkGap = MinInkGap,
            IReadOnlyList<double>? barBeats = null)
        {
            if (_horizontalScreenStage)
            {
                StaffLayoutDiag.Count("EnforceGlobalBeatOrderSpacing_BLOCKED_AFTER_SCREEN_MAP");
                return;
            }
            EnforceGroupOrderSpacing(notes, noteLayouts, beatOrigin, inkGap, barBeats);
        }

        /// <summary>
        /// Beat-order spacing that keeps forward-only pushes inside each measure.
        /// Passing <paramref name="barBeats"/> resets the trailing edge at every bar so
        /// accidental/ink gaps cannot shove notes past a fixed bar line (the diminished /
        /// dense-accidental regression). Without bar beats this matches the old
        /// bar-blind behaviour used only where no bars exist (e.g. tuner).
        /// </summary>
        private void EnforceStrictBeatOrderSpacing(
            IReadOnlyList<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            double beatOrigin,
            float inkGap = MinInkGap,
            IReadOnlyList<double>? barBeats = null)
        {
            if (_horizontalScreenStage)
            {
                StaffLayoutDiag.Count("EnforceStrictBeatOrderSpacing_BLOCKED_AFTER_SCREEN_MAP");
                return;
            }
            StaffLayoutDiag.Count(nameof(EnforceStrictBeatOrderSpacing));
            if (notes.Count == 0 || noteLayouts.Length == 0)
                return;

            var order = Enumerable.Range(0, notes.Count)
                .OrderBy(i => (notes[i].BeatPosition ?? 0.0) - beatOrigin)
                .ThenBy(i => i)
                .ToList();
            var sortedBarBeatsRel = barBeats?.Select(b => b - beatOrigin).OrderBy(b => b).ToList();
            EnforceGroupOrderSpacingOnIndices(notes, noteLayouts, order, inkGap, beatOrigin, sortedBarBeatsRel);
        }

        /// <summary>
        /// Places each internal bar line from the completed measure's ink (last trailing edge
        /// + clearance), then ensures the next measure's first object clears that bar.
        /// Bar X follows content; content is not allowed to cross a frozen bar slot.
        /// </summary>
        private void PlaceBarLinesFromMeasureContent(
            IReadOnlyList<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            IList<BarLayout> barLayouts,
            IReadOnlyList<double> barBeats,
            double beatOrigin)
        {
            // Staff-local only — must not run after MapStaffLayoutToScreen.
            if (_horizontalScreenStage)
            {
                StaffLayoutDiag.Count("PlaceBarLinesFromMeasureContent_BLOCKED_AFTER_SCREEN_MAP");
                return;
            }
            StaffLayoutDiag.Count(nameof(PlaceBarLinesFromMeasureContent));
            if (notes.Count == 0 || noteLayouts.Length == 0 || barLayouts.Count == 0)
                return;

            var noteList = notes as List<GeneratedNote> ?? notes.ToList();
            var sortedBarBeats = barBeats.Select(b => b - beatOrigin).OrderBy(b => b).ToList();
            double totalBeats = 0;
            for (int i = 0; i < notes.Count; i++)
            {
                double rel = (notes[i].BeatPosition ?? 0.0) - beatOrigin;
                totalBeats = Math.Max(totalBeats, rel + notes[i].BeatDuration);
            }

            var segments = BuildMeasureSegments(noteList, sortedBarBeats, beatOrigin, totalBeats);
            int internalBarCount = Math.Min(sortedBarBeats.Count, barLayouts.Count);

            for (int b = 0; b < internalBarCount; b++)
            {
                if (b >= segments.Count || segments[b].NoteIndices.Count == 0)
                    continue;

                var sorted = SortIndicesByBeat(noteList, segments[b].NoteIndices, beatOrigin);
                int lastIdx = sorted[^1];
                float trailing = NoteInkRightForLayout(noteLayouts[lastIdx]);
                float requiredBarX = trailing + BarLeftPadding + BarStemClearance;
                if (barLayouts[b].X < requiredBarX)
                {
                    var bar = barLayouts[b];
                    bar.X = requiredBarX;
                    barLayouts[b] = bar;
                }

                if (b + 1 >= segments.Count || segments[b + 1].NoteIndices.Count == 0)
                    continue;

                float minLeading = barLayouts[b].X + BarLeftPadding + BarStemClearance;
                var nextSorted = SortIndicesByBeat(noteList, segments[b + 1].NoteIndices, beatOrigin);
                ShiftMeasureIndicesToMinGroupLeft(noteLayouts, nextSorted, minLeading);
                EnforceGroupOrderSpacingOnIndices(notes, noteLayouts, nextSorted, MinInkGap);

                float nextBarX = b + 1 < barLayouts.Count
                    ? barLayouts[b + 1].X
                    : barLayouts[^1].X;
                float nextTrailing = NoteInkRightForLayout(noteLayouts[nextSorted[^1]]);
                if (nextTrailing > nextBarX - BarLeftPadding - BarStemClearance + 0.5f)
                {
                    ResolveMeasureNoteSpacing(
                        noteList, noteLayouts, nextSorted,
                        minLeading,
                        nextBarX - BarLeftPadding * 0.5f,
                        nextBarX,
                        beatOrigin,
                        segments[b + 1].EndBeat);

                    // If the measure still cannot fit, move the following bar with the content
                    // rather than leaving noteheads under the bar line.
                    nextTrailing = NoteInkRightForLayout(noteLayouts[nextSorted[^1]]);
                    float minNextBar = nextTrailing + BarLeftPadding + BarStemClearance;
                    if (b + 1 < barLayouts.Count && barLayouts[b + 1].X < minNextBar)
                    {
                        var bar = barLayouts[b + 1];
                        bar.X = minNextBar;
                        barLayouts[b + 1] = bar;
                    }
                }
            }

            // Final bar: mirror ReconcileFinalBarLayout for IList.
            float lastNoteRight = float.NegativeInfinity;
            for (int i = 0; i < noteLayouts.Length; i++)
            {
                float right = NoteInkRightForLayout(noteLayouts[i]);
                if (right > lastNoteRight)
                    lastNoteRight = right;
            }

            if (lastNoteRight > float.NegativeInfinity)
            {
                int finalBarIndex = barLayouts.Count - 1;
                float endBarX = Math.Max(barLayouts[finalBarIndex].X, lastNoteRight + BarRightPadding);
                if (finalBarIndex > 0)
                    endBarX = Math.Max(endBarX, barLayouts[finalBarIndex - 1].X + 5f);
                var endBar = barLayouts[finalBarIndex];
                endBar.X = endBarX;
                barLayouts[finalBarIndex] = endBar;
            }
        }

        /// <summary>Packs notes at min ink gap when proportional beat spacing cannot fit.</summary>
        private void LayoutNotesSequentialInMeasure(
            List<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            IReadOnlyList<int> sorted,
            float innerLeft,
            float barLineX,
            bool isFirstMeasureOnStaff,
            double beatOrigin,
            double measureEndBeat)
        {
            float prevRight = float.NegativeInfinity;
            var accHistory = new Dictionary<(char, int), Accidental>();
            var barCancelled = new HashSet<(char, int)>();
            for (int k = 0; k < sorted.Count; k++)
            {
                int i = sorted[k];
                var note = notes[i];
                var acc = ResolveLayoutAccidental(note, accHistory, barCancelled);

                float center;
                if (prevRight <= float.NegativeInfinity)
                {
                    center = innerLeft + NoteCenterLeftReach(note.IsRest, acc.HasAccidental, acc.IsFlat, acc.IsNatural, note.Duration);
                }
                else
                {
                    center = MinCenterAfterPrevRight(prevRight, note.IsRest, acc.HasAccidental, acc.IsFlat, acc.IsNatural, duration: note.Duration);
                }

                if (isFirstMeasureOnStaff)
                {
                    float headerEdge = _planUseFullHeader
                        ? _headerMetrics.FullHeaderRightRel
                        : _planStaffLeftMargin;
                    float headerMin = headerEdge + NoteCenterLeftReach(note.IsRest, acc.HasAccidental, acc.IsFlat, acc.IsNatural, note.Duration);
                    center = Math.Max(center, headerMin);
                }

                noteLayouts[i] = new NoteLayout
                {
                    X = center,
                    AccidentalX = center,
                    HasAccidental = acc.HasAccidental,
                    AccidentalIsFlat = acc.IsFlat,
                    AccidentalIsNatural = acc.IsNatural,
                    IsRest = note.IsRest,
                    Duration = note.Duration
                };
                SyncAccidentalX(noteLayouts, i);
                prevRight = NoteTrailingRight(note, center);
            }

            float maxTrailing = barLineX - BarLeftPadding;
            ClampMeasureTrailingBeforeBar(notes, noteLayouts, sorted, maxTrailing,
                float.NegativeInfinity, beatOrigin, measureEndBeat);
        }

        /// <summary>
        /// Beat-proportional placement when the measure slot is wide enough for sequential min gaps.
        /// If beat placement + spacing push would require crushing below <see cref="MinInkGap"/>,
        /// falls back to the same sequential packing used by the adult path — never fitScale-crush.
        /// </summary>
        private void LayoutBeginnerNotesInMeasure(
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
            float barLineX = measureLeft + measureWidth;

            // Same gate as LayoutNotesInMeasure: if sequential ink packing needs more than this
            // slot, place sequentially (may overflow) rather than crushing gaps.
            float minPackedSpan = ComputeMinimumPackedSpan(notes, sorted, beatOrigin, segment.EndBeat);
            if (minPackedSpan > measureWidth - BarLeftPadding * 0.5f)
            {
                LayoutNotesSequentialInMeasure(notes, noteLayouts, sorted, innerLeft, barLineX,
                    isFirstMeasureOnStaff, beatOrigin, segment.EndBeat);
                return;
            }

            double measureBeats = segment.EndBeat - segment.StartBeat;
            if (measureBeats < 1e-9)
                measureBeats = 1;

            float laneLeft = measureLeft + BarLeftPadding;
            float laneRight = measureLeft + measureWidth - BarLeftPadding;
            float laneSpan = Math.Max(8f, laneRight - laneLeft);

            var centers = new float[sorted.Count];
            var indices = new int[sorted.Count];
            var hasAccList = new bool[sorted.Count];
            var isFlatList = new bool[sorted.Count];
            var isNaturalList = new bool[sorted.Count];
            var isRestList = new bool[sorted.Count];
            var accHistory = new Dictionary<(char, int), Accidental>();
            var barCancelled = new HashSet<(char, int)>();

            for (int k = 0; k < sorted.Count; k++)
            {
                int i = sorted[k];
                var note = notes[i];
                indices[k] = i;
                double relBeat = (note.BeatPosition ?? 0.0) - beatOrigin - segment.StartBeat;
                double anchorBeat = relBeat + note.BeatDuration * 0.5;
                float frac = (float)Math.Clamp(anchorBeat / measureBeats, 0.0, 1.0);

                var acc = ResolveLayoutAccidental(note, accHistory, barCancelled);
                hasAccList[k] = acc.HasAccidental;
                isFlatList[k] = acc.IsFlat;
                isNaturalList[k] = acc.IsNatural;
                isRestList[k] = note.IsRest;

                centers[k] = laneLeft + frac * laneSpan;
            }

            if (isFirstMeasureOnStaff)
            {
                float headerEdge = _planUseFullHeader
                    ? _headerMetrics.FullHeaderRightRel
                    : _planStaffLeftMargin;
                for (int k = 0; k < sorted.Count; k++)
                {
                    float headerMin = headerEdge + NoteCenterLeftReach(isRestList[k], hasAccList[k], isFlatList[k], isNaturalList[k])
                                      + StaffStartExtraPad;
                    if (centers[k] < headerMin)
                        centers[k] = headerMin;
                }
            }

            // Push centers so accidentals/rests clear prior ink — do not fitScale afterward.
            EnforceBeginnerCenterSpacing(
                notes, indices, centers, isRestList, hasAccList, isFlatList, isNaturalList,
                beatOrigin, segment.EndBeat);

            float maxTrailing = barLineX - BarLeftPadding - BarStemClearance;
            if (!isFirstMeasureOnStaff && sorted.Count > 0)
            {
                float barMin = measureLeft + BarLeftPadding + MeasureStartExtraPad + BarStemClearance
                    + NoteCenterLeftReach(isRestList[0], hasAccList[0], isFlatList[0], isNaturalList[0]);
                if (centers[0] < barMin)
                    centers[0] = barMin;
                EnforceBeginnerCenterSpacing(
                    notes, indices, centers, isRestList, hasAccList, isFlatList, isNaturalList,
                    beatOrigin, segment.EndBeat);
            }

            if (sorted.Count > 0)
            {
                float trailing = NoteTrailingRight(notes[indices[^1]], centers[^1]);
                if (trailing > maxTrailing + 0.5f)
                {
                    // Spacing push overflowed the measure slot — sequential preserves MinInkGap.
                    LayoutNotesSequentialInMeasure(notes, noteLayouts, sorted, innerLeft, barLineX,
                        isFirstMeasureOnStaff, beatOrigin, segment.EndBeat);
                    return;
                }
            }

            for (int k = 0; k < sorted.Count; k++)
            {
                int i = indices[k];
                noteLayouts[i] = new NoteLayout
                {
                    X = centers[k],
                    AccidentalX = centers[k],
                    HasAccidental = hasAccList[k],
                    AccidentalIsFlat = isFlatList[k],
                    AccidentalIsNatural = isNaturalList[k],
                    IsRest = isRestList[k],
                    Duration = notes[i].Duration
                };
                SyncAccidentalX(noteLayouts, i);
            }
        }

        /// <summary>
        /// Forward-only push so each note group's left ink (including accidentals) clears the
        /// previous note's trailing ink. Critical for beamed pairs like G / Gb.
        /// </summary>
        private void EnforceBeginnerCenterSpacing(
            IReadOnlyList<GeneratedNote> notes,
            int[] indices,
            float[] centers,
            bool[] isRest,
            bool[] hasAcc,
            bool[] isFlat,
            bool[] isNatural,
            double beatOrigin,
            double measureEndBeat)
        {
            if (centers.Length == 0)
                return;

            var beamGroups = ComputeLayoutBeamGroupIds(notes, indices, beatOrigin, measureEndBeat);
            float prevRight = float.NegativeInfinity;
            for (int k = 0; k < centers.Length; k++)
            {
                float inkGap = k > 0
                    ? InkGapBetween(
                        beamGroups, indices[k - 1], indices[k], _planInkGap,
                        needsAccidentalClearance: hasAcc[k],
                        eitherIsRest: isRest[k] || isRest[k - 1])
                    : _planInkGap;
                // Accidental must read as belonging to this note, not the previous one.
                if (hasAcc[k])
                    inkGap = Math.Max(inkGap, AccidentalPairInkGap);

                float curLeft = NoteGroupLeft(centers[k], isRest[k], hasAcc[k], isFlat[k], isNatural[k], notes[indices[k]].Duration);
                if (prevRight > float.NegativeInfinity)
                {
                    float minLeft = prevRight + inkGap;
                    if (curLeft < minLeft)
                        centers[k] += minLeft - curLeft;
                }

                prevRight = NoteTrailingRight(notes[indices[k]], centers[k]);
            }
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

            float minPackedSpan = ComputeMinimumPackedSpan(notes, sorted, beatOrigin, segment.EndBeat);
            if (minPackedSpan > measureWidth - BarLeftPadding * 0.5f)
            {
                LayoutNotesSequentialInMeasure(notes, noteLayouts, sorted, innerLeft, barLineX,
                    isFirstMeasureOnStaff, beatOrigin, segment.EndBeat);
                return;
            }

            double measureBeats = segment.EndBeat - segment.StartBeat;
            if (measureBeats < 1e-9)
                measureBeats = 1;

            var firstNote = notes[sorted[0]];
            var lastNote = notes[sorted[^1]];
            var accHistory = new Dictionary<(char, int), Accidental>();
            var barCancelled = new HashSet<(char, int)>();
            var firstAcc = ResolveLayoutAccidental(firstNote, accHistory, barCancelled);
            float startCenter = innerLeft + NoteCenterLeftReach(firstNote.IsRest, firstAcc.HasAccidental, firstAcc.IsFlat, firstAcc.IsNatural, firstNote.Duration);
            float endCenter = innerRight - NoteCenterTrailingReach(lastNote.IsRest, lastNote.Duration);
            float spread = Math.Max(8f, endCenter - startCenter);

            float prevRight = float.NegativeInfinity;
            for (int k = 0; k < sorted.Count; k++)
            {
                int i = sorted[k];
                var note = notes[i];
                double relBeat = (note.BeatPosition ?? 0.0) - beatOrigin - segment.StartBeat;
                // Place at the duration midpoint so longer notes claim visual width
                // (onset-only mapping left large trailing voids after halves/wholes).
                double anchorBeat = relBeat + note.BeatDuration * 0.5;
                float frac = (float)Math.Clamp((anchorBeat + k * 1e-4) / measureBeats, 0.0, 1.0);

                var acc = k == 0
                    ? firstAcc
                    : ResolveLayoutAccidental(note, accHistory, barCancelled);

                float idealX = startCenter + frac * spread;

                if (relBeat < 1e-6)
                {
                    float onBarMin = measureLeft + BarLeftPadding + MeasureStartExtraPad + BarStemClearance
                                     + NoteCenterLeftReach(note.IsRest, acc.HasAccidental, acc.IsFlat, acc.IsNatural, note.Duration);
                    idealX = Math.Max(idealX, onBarMin);
                }

                if (isFirstMeasureOnStaff)
                {
                    float headerEdge = _planUseFullHeader
                        ? _headerMetrics.FullHeaderRightRel
                        : _planStaffLeftMargin;
                    float headerMin = headerEdge + NoteCenterLeftReach(note.IsRest, acc.HasAccidental, acc.IsFlat, acc.IsNatural, note.Duration)
                                      + StaffStartExtraPad;
                    idealX = Math.Max(idealX, headerMin);
                }

                if (prevRight > float.NegativeInfinity)
                {
                    float gap = acc.HasAccidental || note.IsRest
                        ? Math.Max(_planInkGap, note.IsRest ? MinInkGap : AccidentalPairInkGap)
                        : _planInkGap;
                    if (acc.HasAccidental)
                        gap = Math.Max(gap, AccidentalPairInkGap);
                    float minCenter = MinCenterAfterPrevRight(
                        prevRight, note.IsRest, acc.HasAccidental, acc.IsFlat, acc.IsNatural, gap, note.Duration);
                    idealX = Math.Max(idealX, minCenter);
                }

                noteLayouts[i] = new NoteLayout
                {
                    X = idealX,
                    AccidentalX = idealX,
                    HasAccidental = acc.HasAccidental,
                    AccidentalIsFlat = acc.IsFlat,
                    AccidentalIsNatural = acc.IsNatural,
                    IsRest = note.IsRest,
                    Duration = note.Duration
                };
                SyncAccidentalX(noteLayouts, i);
                prevRight = NoteTrailingRight(note, noteLayouts[i].X);
            }

            ResolveMeasureNoteSpacing(notes, noteLayouts, sorted, innerLeft, innerRight,
                measureLeft + measureWidth, beatOrigin, segment.EndBeat);
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
            StaffLayoutDiag.Count(nameof(PlanHorizontalLayout));
            _horizontalScreenStage = false; // staff-local musical geometry stage
            _planStaffLeftMargin = staffLeftMargin;
            _planUseFullHeader = useFullHeaderAnchor;
            availableWidth = Math.Max(availableWidth, 64f);

            if (notes.Count == 0)
                return (Array.Empty<NoteLayout>(), Array.Empty<BarLayout>(), staffLeftMargin);

            double beatOrigin = GetStaffBeatOrigin(notes, barBeats);
            barBeats = ResolveStaffBarBeats(notes, barBeats, beatOrigin);

            double totalBeats = 0.0;
            for (int i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                double relBeat = (note.BeatPosition ?? 0.0) - beatOrigin;
                totalBeats = Math.Max(totalBeats, relBeat + note.BeatDuration);
            }

            var sortedBarBeats = barBeats.Select(b => b - beatOrigin).OrderBy(b => b).ToList();
            var segments = BuildMeasureSegments(notes, sortedBarBeats, beatOrigin, totalBeats);

            bool beginner = UseBeginnerHorizontalLayout;
            float[] measureWidths;
            if (beginner)
            {
                _planInkGap = _session.ChildLevel <= 30 ? BeginnerInkGap : MinInkGap;
                var minWidths = new float[segments.Count];
                for (int m = 0; m < segments.Count; m++)
                {
                    double segBeats = segments[m].EndBeat - segments[m].StartBeat;
                    if (segBeats < 1e-9)
                        segBeats = 1;
                    // Beat-lane floor only — never force equalShare into minWidths
                    // (that made sum(min) always ≥ available and invited crush-to-fit).
                    float beatLaneMin = (float)segBeats * (_layout.NoteHeadR * 2.2f + _planInkGap * 0.45f)
                                        + BarLeftPadding * 2f;
                    minWidths[m] = Math.Max(beatLaneMin, ComputeMeasureMinWidth(notes, segments[m], beatOrigin));
                }

                measureWidths = AllocateMeasureWidths(segments, minWidths, availableWidth);
            }
            else
            {
                _planInkGap = MinInkGap;
                var minWidths = new float[segments.Count];
                for (int m = 0; m < segments.Count; m++)
                    minWidths[m] = ComputeMeasureMinWidth(notes, segments[m], beatOrigin);

                float sumMin = 0f;
                for (int m = 0; m < minWidths.Length; m++)
                    sumMin += minWidths[m];
                if (sumMin > availableWidth && sumMin > 0f)
                {
                    // Shrink gaps only down to HorizontalCompressFloor — never to near-zero,
                    // which packed beams into collisions before whole-staff scale ran.
                    _planInkGap = Math.Max(
                        MinInkGap * HorizontalCompressFloor,
                        MinInkGap * (availableWidth / sumMin));
                    for (int m = 0; m < segments.Count; m++)
                        minWidths[m] = ComputeMeasureMinWidth(notes, segments[m], beatOrigin);
                }

                measureWidths = AllocateMeasureWidths(segments, minWidths, availableWidth);
            }

            var noteLayouts = new NoteLayout[notes.Count];
            var barList = new List<BarLayout>();
            float x = staffLeftMargin;

            // Tuner reference staff: layout the note only — never emit measure/end bar lines
            // (normal music / scales / arpeggios / tunes keep the bar path below).
            bool tunerNoBars = _session.Tune == "Tuner";

            for (int m = 0; m < segments.Count; m++)
            {
                if (beginner)
                    LayoutBeginnerNotesInMeasure(notes, noteLayouts, segments[m], beatOrigin, x, measureWidths[m], m == 0);
                else
                    LayoutNotesInMeasure(notes, noteLayouts, segments[m], beatOrigin, x, measureWidths[m], m == 0);
                x += measureWidths[m];

                if (!tunerNoBars && m < sortedBarBeats.Count)
                    barList.Add(new BarLayout { X = x, IsDouble = false });
            }

            if (!tunerNoBars && notes.Count > 0)
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

            if (tunerNoBars && notes.Count == 1 && noteLayouts.Length == 1)
            {
                // Center the selected written note in the open staff area after the clef.
                float left = staffLeftMargin + BarLeftPadding;
                float right = staffLeftMargin + availableWidth - BarRightPadding;
                if (right > left)
                    noteLayouts[0].X = (left + right) * 0.5f;
                SyncAccidentalX(noteLayouts, 0);
            }

            LastComputedPxPerBeat = totalBeats > 0 ? availableWidth / (float)totalBeats : 42f;

            if (!beginner)
            {
                EnforceGlobalBeatOrderSpacing(notes, noteLayouts, beatOrigin, _planInkGap, barBeats);
                EnforceStrictBeatOrderSpacing(notes, noteLayouts, beatOrigin, MinInkGap, barBeats);
                PlaceBarLinesFromMeasureContent(notes, noteLayouts, barList, barBeats, beatOrigin);
            }
            else
            {
                // Per-measure sequential fallback may slightly overflow a bar; restore MinInkGap
                // inside each measure, then re-anchor bars to measure ink.
                EnforceStrictBeatOrderSpacing(notes, noteLayouts, beatOrigin, MinInkGap, barBeats);
                PlaceBarLinesFromMeasureContent(notes, noteLayouts, barList, barBeats, beatOrigin);
            }

            float totalWidth = barList.Count > 0
                ? barList.Max(b => b.X) + EffectiveRightMargin
                : staffLeftMargin + availableWidth;

            if (tunerNoBars && notes.Count == 1 && noteLayouts.Length == 1)
            {
                // Spacing passes can nudge the single note left again — re-center.
                float left = staffLeftMargin + BarLeftPadding;
                float right = staffLeftMargin + availableWidth - BarRightPadding;
                if (right > left)
                    noteLayouts[0].X = (left + right) * 0.5f;
                SyncAccidentalX(noteLayouts, 0);
            }

            ValidateLayout(notes, barBeats, noteLayouts, beatOrigin, beginner);

            return (noteLayouts, barList.ToArray(), totalWidth);
        }

        /// <summary>
        /// Validates musical measure structure and note spacing.
        /// Logs warnings for incorrect measure durations or overlapping notes.
        /// </summary>
        private void ValidateLayout(List<GeneratedNote> notes, List<double> barBeats, NoteLayout[] noteLayouts, double beatOrigin, bool beginnerLayout = false)
        {
            if (notes.Count == 0) return;

            try
            {
                var sortedBars = barBeats.Select(b => b - beatOrigin).OrderBy(b => b).ToList();
                double expectedMeasureBeats = _session.GetDisplayMeasureBeats();

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
                        StaffLog($"[Staff Validation] Measure {i}: {measureBeats:F2} beats (expected {expectedBeats}), " +
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
                        StaffLog($"[Staff Validation] Measure {m}: note durations sum to {sumDuration:F2} " +
                              $"(expected {expectedMeasureBeats:F0}), beats {segments[m].StartBeat:F2}-{segments[m].EndBeat:F2}");
                    }
                }

                // Validate note spacing in beat order (not array index order)
                float minAllowedSpacing = (beginnerLayout ? BeginnerInkGap : MinInkGap) - 0.5f;
                var order = Enumerable.Range(0, notes.Count)
                    .OrderBy(i => (notes[i].BeatPosition ?? 0.0) - beatOrigin)
                    .ThenBy(i => i)
                    .ToList();
                for (int k = 1; k < order.Count; k++)
                {
                    int prev = order[k - 1];
                    int cur = order[k];
                    float prevRight = NoteTrailingRight(notes[prev], noteLayouts[prev].X);
                    float curLeft = NoteGroupLeftFromLayout(noteLayouts[cur]);
                    float gap = curLeft - prevRight;
                    if (gap < minAllowedSpacing)
                    {
                        StaffLog($"[Staff Validation] Notes {prev} and {cur} too close: {gap:F1}px apart");
                    }
                }
            }
            catch (Exception ex)
            {
                StaffLog($"[Staff Validation] Error: {ex.Message}");
            }
        }

        // ── IDrawable ─────────────────────────────────────────────────────────────
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var bg = _theme.PanelBackgroundColor;
            var ink = _theme.ContrastingTextColor;

            canvas.FillColor = bg;
            canvas.FillRectangle(dirtyRect);

            try
            {
                DrawStaffContent(canvas, dirtyRect, ink);
            }
            catch (Exception ex)
            {
                LogDrawFailure(ex);
                DrawStaffErrorMessage(canvas, dirtyRect, ink, ex);
            }
        }

        private void DrawStaffContent(ICanvas canvas, RectF dirtyRect, Color ink)
        {
            UpperNotes ??= new();
            LowerNotes ??= new();
            UpperBarBeats ??= new();
            LowerBarBeats ??= new();

            // Tuner must show at most one note on a single staff — never leftover Music notes.
            if (IsTunerReferenceStaff)
                EnforceTunerSingleNoteDisplay();

            if (dirtyRect.Width < 32f || dirtyRect.Height < 32f)
                return;

            if (UpperNotes.Count == 0 && LowerNotes.Count == 0)
            {
                // Sight Training / Tuner: show an empty staff instead of a blank panel.
                if (SingleStaffLayout || _session.Tune == "Tuner")
                {
                    DrawTunerEmptyStaff(canvas, dirtyRect, ink);
                    return;
                }

                canvas.FontColor = ink;
                canvas.FontSize = 14;
                canvas.DrawString("No notes generated yet",
                    dirtyRect.X + 8, dirtyRect.Y + dirtyRect.Height / 2f,
                    HorizontalAlignment.Left);
                return;
            }

            // Step 0: Safe horizontal bounds — see RelaxCutoutInsetRightDp to tune cutout margin.
            //   viewRight     = dirtyRect.X + dirtyRect.Width
            //   safeRight     = viewRight - effectiveRightInset  (hard clip for staff + notes)
            //   layoutLimit   = safeRight - LayoutRightPad     (notes/bars stretch to here)
            ResolveDrawHorizontalBounds(dirtyRect, out float safeLeft, out float safeRight, out float layoutRightLimit);
            float safeWidth = layoutRightLimit - safeLeft;

            var insets = _safeArea?.GetSafeAreaInsets() ?? (0f, 0f, 0f, 0f);
            var layoutCacheKey = BuildLayoutCacheKey(dirtyRect.Width, dirtyRect.Height, insets.Left, insets.Right);
            // Pair-pan depends on Current note states; skip cache for single-staff Sight Training.
            if (!SingleStaffLayout && TryDrawFromLayoutCache(canvas, dirtyRect, ink, layoutCacheKey))
                return;

            float viewRight = dirtyRect.X + dirtyRect.Width;
            StaffLog($"[Staff] Canvas={dirtyRect.Width:F0}x{dirtyRect.Height:F0}, " +
                  $"Insets=L{insets.Left:F0},R{insets.Right:F0}, omitHeader={OmitStaffHeader}, " +
                  $"ViewRight={viewRight:F0}, SafeRight={safeRight:F0}, LayoutLimit={layoutRightLimit:F0}, " +
                  $"SafeWidth={safeWidth:F0}");

            // Step 1: Compute vertical layout
            ComputeLayout(dirtyRect.Height);
            _headerMetrics = ComputeHeaderMetrics(safeLeft);
            _leftMargin = _headerMetrics.LeftMargin;
            // Lower staff: full key/time header when the setting is ON (or when it is the
            // only staff with notes); otherwise clef-only start like the legacy layout.
            bool lowerUsesFullHeader = LowerNotes.Count > 0
                && (UpperNotes.Count == 0 || _session.ShowSignaturesOnBothStaffs);
            bool lowerClefOnlyStart = LowerNotes.Count > 0 && !lowerUsesFullHeader;
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
            float rightGutter = EffectiveRightMargin;
            float upperUsableWidth = safeWidth - upperStaffMargin - rightGutter;
            float lowerUsableWidth = safeWidth - lowerStaffMargin - rightGutter;

            var (upperNoteLayouts, upperBarLayouts, upperTotalWidth) = PlanHorizontalLayout(
                UpperNotes, UpperBarBeats, upperUsableWidth,
                upperStaffMargin, useFullHeaderAnchor: true,
                isFinalStaff: LowerNotes.Count == 0,
                hasEndSingleBar: UpperHasEndBar && LowerNotes.Count > 0);
            float upperPlanInkGap = _planInkGap;

            // Lower staff: match upper px/beat when both staves are comparably full.
            // A short final line (e.g. 4+1 scale walk) gets an independent readable width
            // instead of a tiny matched strip or a full-staff stretch.
            double upperTotalBeats = ComputeStaffTotalBeats(UpperNotes, UpperBarBeats);
            double lowerTotalBeats = ComputeStaffTotalBeats(LowerNotes, LowerBarBeats);
            bool lowerIsShort = IsShortLowerStaff(upperTotalBeats, lowerTotalBeats);
            float lowerPlanUsableWidth = ComputeMatchedStaffUsableWidth(
                peerContentWidth: Math.Max(1f, upperTotalWidth - upperStaffMargin),
                peerTotalBeats: upperTotalBeats,
                selfTotalBeats: lowerTotalBeats,
                selfUsableWidth: lowerUsableWidth);
            bool lowerFillsWidth = !lowerIsShort
                && lowerPlanUsableWidth >= lowerUsableWidth - 0.5f;

            var (lowerNoteLayouts, lowerBarLayouts, lowerTotalWidth) = PlanHorizontalLayout(
                LowerNotes, LowerBarBeats, lowerPlanUsableWidth,
                lowerStaffMargin, useFullHeaderAnchor: !lowerClefOnlyStart,
                isFinalStaff: true,
                hasEndSingleBar: false);
            float lowerPlanInkGap = _planInkGap;

            // Keep drawable bar-beat lists aligned with ResolveStaffBarBeats (used inside PlanHorizontalLayout).
            double upperBeatOriginResolved = GetStaffBeatOrigin(UpperNotes, UpperBarBeats);
            UpperBarBeats = ResolveStaffBarBeats(UpperNotes, UpperBarBeats, upperBeatOriginResolved);
            double lowerBeatOriginResolved = GetStaffBeatOrigin(LowerNotes, LowerBarBeats);
            LowerBarBeats = ResolveStaffBarBeats(LowerNotes, LowerBarBeats, lowerBeatOriginResolved);

            float[] upperPreScaleX = CopyLayoutX(upperNoteLayouts);
            float[] lowerPreScaleX = CopyLayoutX(lowerNoteLayouts);

            if (UseBeginnerHorizontalLayout)
            {
                FinishBeginnerHorizontalLayout(
                    UpperNotes, upperNoteLayouts, upperBarLayouts, upperTotalWidth, upperUsableWidth,
                    LowerNotes, lowerNoteLayouts, lowerBarLayouts, lowerTotalWidth, lowerUsableWidth,
                    safeLeft, upperStaffMargin, lowerStaffMargin, layoutRightLimit,
                    upperTop, upperMid, upperBot, lowerTop, lowerMid, lowerBot,
                    expandLowerToFill: lowerFillsWidth);

                SanitizeLayoutPositions(upperNoteLayouts, upperBarLayouts);
                SanitizeLayoutPositions(lowerNoteLayouts, lowerBarLayouts);

                if (IsTunerReferenceStaff)
                {
                    upperBarLayouts = Array.Empty<BarLayout>();
                    CenterTunerReferenceNote(
                        upperNoteLayouts, safeLeft, upperStaffMargin, layoutRightLimit);
                }

                if (OmitStaffHeader)
                {
                    upperBarLayouts = Array.Empty<BarLayout>();
                    CenterOmitHeaderIntervalReveal(
                        UpperNotes, upperNoteLayouts,
                        ref upperTop, ref upperMid, ref upperBot,
                        dirtyRect.Height, safeLeft, layoutRightLimit);
                }
                else if (SingleStaffLayout)
                {
                    upperBarLayouts = Array.Empty<BarLayout>();
                }

                // Sight Training only — Ear Training headerless notes are already centered/clamped.
                if (!OmitStaffHeader)
                {
                    EnsureActivePairVisible(
                        UpperNotes, UpperNoteStates, upperNoteLayouts, upperBarLayouts,
                        safeLeft, upperStaffMargin, layoutRightLimit);
                }

                if (SingleStaffLayout && !OmitStaffHeader)
                    upperBarLayouts = FinalizeSingleStaffEndBar(upperNoteLayouts);

                LogStaffLayoutDiagnostics("Upper", UpperNotes, upperNoteLayouts, upperPreScaleX,
                    upperTop, upperMid, upperBot);
                LogStaffLayoutDiagnostics("Lower", LowerNotes, lowerNoteLayouts, lowerPreScaleX,
                    lowerTop, lowerMid, lowerBot);

                LogBeginnerLayoutBounds(upperNoteLayouts, upperBarLayouts, lowerNoteLayouts, lowerBarLayouts,
                    layoutRightLimit, safeRight);

                double upperBeatOriginBeginner = GetStaffBeatOrigin(UpperNotes, UpperBarBeats);
                double lowerBeatOriginBeginner = GetStaffBeatOrigin(LowerNotes, LowerBarBeats);
                if (!SingleStaffLayout)
                {
                    StoreLayoutCache(layoutCacheKey, dirtyRect, ink,
                        upperTop, upperMid, upperBot, lowerTop, lowerMid, lowerBot,
                        upperNoteLayouts, upperBarLayouts, lowerNoteLayouts, lowerBarLayouts,
                        upperBeatOriginBeginner, lowerBeatOriginBeginner,
                        safeLeft, safeRight, layoutRightLimit, upperStaffMargin, lowerStaffMargin);
                }
                if (_layoutCache != null)
                {
                    BlitStaticChromeOrDrawFallback(canvas, dirtyRect, ink, _layoutCache,
                        upperTop, upperMid, upperBot, lowerTop, lowerMid, lowerBot,
                        upperNoteLayouts, upperBarLayouts, lowerNoteLayouts, lowerBarLayouts,
                        safeLeft, safeRight, layoutRightLimit, upperStaffMargin, lowerStaffMargin);
                }
                else
                {
                    DrawBothStaffsStaticChrome(canvas, dirtyRect, ink,
                        upperTop, upperMid, upperBot, lowerTop, lowerMid, lowerBot,
                        upperNoteLayouts, upperBarLayouts, lowerNoteLayouts, lowerBarLayouts,
                        safeLeft, safeRight, layoutRightLimit, upperStaffMargin, lowerStaffMargin);
                }
                DrawBothStaffsDynamic(canvas, dirtyRect, ink,
                    upperTop, upperMid, upperBot, lowerTop, lowerMid, lowerBot,
                    upperNoteLayouts, upperBarLayouts, lowerNoteLayouts, lowerBarLayouts,
                    upperBeatOriginBeginner, lowerBeatOriginBeginner,
                    safeLeft, safeRight, layoutRightLimit, upperStaffMargin, lowerStaffMargin);
                return;
            }

            // Step 3 — Screen stage: map finished staff-local geometry with at most one
            // uniform scale per staff. Do NOT run Strict / Refinish / PlaceBarLines /
            // Reconcile / Align after mapping (those mutate musical geometry in screen space).
            float upperContentWidth = Math.Max(1f, upperTotalWidth - upperStaffMargin);
            float lowerContentWidth = Math.Max(1f, lowerTotalWidth - lowerStaffMargin);
            float upperScale = upperContentWidth > upperUsableWidth
                ? upperUsableWidth / upperContentWidth
                : 1f;
            float lowerScale = lowerContentWidth > lowerUsableWidth
                ? lowerUsableWidth / lowerContentWidth
                : 1f;
            // Prefer readable size when mild overflow; never map below safe engraving scale.
            upperScale = Math.Clamp(upperScale, MinimumSafeHorizontalScale, 1f);
            lowerScale = Math.Clamp(lowerScale, MinimumSafeHorizontalScale, 1f);

            if (upperScale < 0.999f || lowerScale < 0.999f)
            {
                StaffLog($"[Staff] Screen map scale: upper={upperScale:F3} lower={lowerScale:F3} " +
                      $"(content U={upperContentWidth:F0} L={lowerContentWidth:F0})");
            }

            MapStaffLayoutToScreen(upperNoteLayouts, upperBarLayouts, safeLeft, upperStaffMargin, upperScale);
            MapStaffLayoutToScreen(lowerNoteLayouts, lowerBarLayouts, safeLeft, lowerStaffMargin, lowerScale);

            // One exact uniform fit if floor-clamped scale still left ink past the limit.
            FitMappedLayoutIntoRightLimit(upperNoteLayouts, upperBarLayouts, layoutRightLimit, safeLeft, upperStaffMargin);
            FitMappedLayoutIntoRightLimit(lowerNoteLayouts, lowerBarLayouts, layoutRightLimit, safeLeft, lowerStaffMargin);

            double upperBeatOrigin = GetStaffBeatOrigin(UpperNotes, UpperBarBeats);
            double lowerBeatOrigin = GetStaffBeatOrigin(LowerNotes, LowerBarBeats);

            // Sight Training: drop internal bars before draw (not a musical re-layout).
            if (SingleStaffLayout)
                upperBarLayouts = Array.Empty<BarLayout>();

            SanitizeLayoutPositions(upperNoteLayouts, upperBarLayouts);
            SanitizeLayoutPositions(lowerNoteLayouts, lowerBarLayouts);

            // Tuner: discard any residual bars and keep the selected note centered.
            if (IsTunerReferenceStaff)
            {
                upperBarLayouts = Array.Empty<BarLayout>();
                CenterTunerReferenceNote(
                    upperNoteLayouts, safeLeft, upperStaffMargin, layoutRightLimit);
            }

            // Tuner / Sight: keep the current pair in view. Ear Training omits this — notes
            // were already centered inside the compact panel above (or below on adult path).
            if (!OmitStaffHeader)
            {
                EnsureActivePairVisible(
                    UpperNotes, UpperNoteStates, upperNoteLayouts, upperBarLayouts,
                    safeLeft, upperStaffMargin, layoutRightLimit);
            }

            if (OmitStaffHeader)
            {
                upperBarLayouts = Array.Empty<BarLayout>();
                CenterOmitHeaderIntervalReveal(
                    UpperNotes, upperNoteLayouts,
                    ref upperTop, ref upperMid, ref upperBot,
                    dirtyRect.Height, safeLeft, layoutRightLimit);
            }
            else if (SingleStaffLayout)
            {
                // Absolute last layout step: snug double bar after final note ink (post-spacing/pan).
                upperBarLayouts = FinalizeSingleStaffEndBar(upperNoteLayouts);
            }

            LogStaffLayoutDiagnostics("Upper", UpperNotes, upperNoteLayouts, upperPreScaleX,
                upperTop, upperMid, upperBot);
            LogStaffLayoutDiagnostics("Lower", LowerNotes, lowerNoteLayouts, lowerPreScaleX,
                lowerTop, lowerMid, lowerBot);

            float upperContentRight = GetLayoutMaxRight(upperNoteLayouts, upperBarLayouts);
            float lowerContentRight = GetLayoutMaxRight(lowerNoteLayouts, lowerBarLayouts);
            float contentRight = Math.Max(upperContentRight, lowerContentRight);

            float upperEndBar = upperBarLayouts.Length > 0 ? upperBarLayouts[^1].X : 0f;
            float lowerEndBar = lowerBarLayouts.Length > 0 ? lowerBarLayouts[^1].X : 0f;
            StaffLog($"[Staff] ContentRight={contentRight:F0} (endBar U={upperEndBar:F0} L={lowerEndBar:F0}), " +
                  $"LayoutLimit={layoutRightLimit:F0}, gutter={(layoutRightLimit - contentRight):F1}, " +
                  $"pastSafeRight={(contentRight > safeRight ? contentRight - safeRight : 0f):F1}");

            if (!SingleStaffLayout)
            {
                StoreLayoutCache(layoutCacheKey, dirtyRect, ink,
                    upperTop, upperMid, upperBot, lowerTop, lowerMid, lowerBot,
                    upperNoteLayouts, upperBarLayouts, lowerNoteLayouts, lowerBarLayouts,
                    upperBeatOrigin, lowerBeatOrigin,
                    safeLeft, safeRight, layoutRightLimit, upperStaffMargin, lowerStaffMargin);
            }
            if (_layoutCache != null)
            {
                BlitStaticChromeOrDrawFallback(canvas, dirtyRect, ink, _layoutCache,
                    upperTop, upperMid, upperBot, lowerTop, lowerMid, lowerBot,
                    upperNoteLayouts, upperBarLayouts, lowerNoteLayouts, lowerBarLayouts,
                    safeLeft, safeRight, layoutRightLimit, upperStaffMargin, lowerStaffMargin);
            }
            else
            {
                DrawBothStaffsStaticChrome(canvas, dirtyRect, ink,
                    upperTop, upperMid, upperBot, lowerTop, lowerMid, lowerBot,
                    upperNoteLayouts, upperBarLayouts, lowerNoteLayouts, lowerBarLayouts,
                    safeLeft, safeRight, layoutRightLimit, upperStaffMargin, lowerStaffMargin);
            }
            DrawBothStaffsDynamic(canvas, dirtyRect, ink,
                upperTop, upperMid, upperBot, lowerTop, lowerMid, lowerBot,
                upperNoteLayouts, upperBarLayouts, lowerNoteLayouts, lowerBarLayouts,
                upperBeatOrigin, lowerBeatOrigin,
                safeLeft, safeRight, layoutRightLimit, upperStaffMargin, lowerStaffMargin);
            return;
        }

        /// <summary>
        /// Sight Training already pads the page for cutouts; Music still uses the inset.
        /// Ear Training's compact panel is a small GraphicsView — window cutout insets must
        /// not be applied again (they belong to the full window, not this canvas).
        /// </summary>
        private float ResolveDrawSafeLeft(float dirtyX, float insetLeft)
            => (SingleStaffLayout || OmitStaffHeader) ? dirtyX : dirtyX + insetLeft;

        /// <summary>
        /// Horizontal draw bounds for the current canvas. Headerless Ear Training uses the
        /// GraphicsView rectangle only so notes/staff lines fill the right-hand panel.
        /// </summary>
        private void ResolveDrawHorizontalBounds(
            RectF dirtyRect,
            out float safeLeft,
            out float safeRight,
            out float layoutRightLimit)
        {
            float viewRight = dirtyRect.X + dirtyRect.Width;
            if (OmitStaffHeader)
            {
                safeLeft = dirtyRect.X;
                safeRight = viewRight;
                layoutRightLimit = Math.Max(safeLeft + 8f, viewRight - LayoutRightPad);
                return;
            }

            var insets = _safeArea?.GetSafeAreaInsets() ?? (0f, 0f, 0f, 0f);
            float effectiveRightInset = Math.Max(0f, insets.Right - RelaxCutoutInsetRightDp);
            safeLeft = ResolveDrawSafeLeft(dirtyRect.X, insets.Left);
            safeRight = viewRight - effectiveRightInset;
            layoutRightLimit = safeRight - LayoutRightPad;
        }

        /// <summary>
        /// Horizontally pans single-staff content so the yellow/red answer pair stays in view
        /// near the left of the open staff (after the header), avoiding a lone yellow at the
        /// far right edge when earlier measures have filled the width.
        /// </summary>
        private void EnsureActivePairVisible(
            List<GeneratedNote> notes,
            StaffNoteState[] states,
            NoteLayout[] noteLayouts,
            BarLayout[] barLayouts,
            float safeLeft,
            float staffLeftMargin,
            float layoutRightLimit)
        {
            if (!SingleStaffLayout
                || notes == null
                || noteLayouts == null
                || notes.Count == 0
                || noteLayouts.Length != notes.Count)
            {
                return;
            }

            var pairIdx = new List<int>(2);
            int stateLen = states?.Length ?? 0;
            for (int i = 0; i < notes.Count; i++)
            {
                var state = i < stateLen ? states![i] : StaffNoteState.Pending;
                if (state == StaffNoteState.Current || state == StaffNoteState.Wrong)
                    pairIdx.Add(i);
            }

            // Only pan notes the quiz actually highlights — never invent a partner past
            // the active pair (that pulled a following note into view as if it were current).
            if (pairIdx.Count == 0)
                return;

            // Final interval pair: keep the fitted layout so Current notes stay on the staff
            // (panning here pulled the last yellow targets past the double bar).
            if (IsFinalSightTrainingPair(notes, pairIdx))
                return;

            float head = Math.Max(4f, _layout.NoteHeadR);
            float pairLeft = float.PositiveInfinity;
            float pairRight = float.NegativeInfinity;
            for (int p = 0; p < pairIdx.Count; p++)
            {
                int i = pairIdx[p];
                float left = noteLayouts[i].HasAccidental
                    ? Math.Min(noteLayouts[i].X, noteLayouts[i].AccidentalX) - head
                    : noteLayouts[i].X - head * 2f;
                float right = NoteTrailingRight(notes[i], noteLayouts[i].X);
                if (left < pairLeft)
                    pairLeft = left;
                if (right > pairRight)
                    pairRight = right;
            }

            if (!float.IsFinite(pairLeft) || !float.IsFinite(pairRight) || pairRight <= pairLeft)
                return;

            float visibleLeft = safeLeft + staffLeftMargin + BarLeftPadding;
            float visibleRight = layoutRightLimit - BarRightPadding;
            float visibleWidth = visibleRight - visibleLeft;
            if (visibleWidth < 48f)
                return;

            float pad = head * 2f;
            float desiredLeft = visibleLeft + pad;
            float pairWidth = pairRight - pairLeft;
            float shift;

            if (pairWidth >= visibleWidth - pad)
            {
                shift = desiredLeft - pairLeft;
            }
            else if (pairLeft >= visibleLeft - 1f && pairRight <= visibleRight + 1f)
            {
                // Both fit already — still pull left when the pair sits in the right half
                // so the next Play-style glance isn't a single yellow at the far edge.
                float mid = (visibleLeft + visibleRight) * 0.5f;
                if (pairLeft <= mid)
                    return;
                shift = desiredLeft - pairLeft;
                if (pairRight + shift > visibleRight)
                    shift = visibleRight - pairRight;
            }
            else
            {
                shift = desiredLeft - pairLeft;
                if (pairRight + shift > visibleRight)
                    shift = visibleRight - pairRight;
            }

            if (Math.Abs(shift) < 0.5f)
                return;

            ShiftNoteAndBarLayouts(noteLayouts, barLayouts, shift);
            StaffLog($"[Staff] Sight pair pan shift={shift:F1} pair=[{pairLeft:F0},{pairRight:F0}] " +
                     $"visible=[{visibleLeft:F0},{visibleRight:F0}] count={pairIdx.Count}");
        }

        /// <summary>
        /// True when the highlighted pair is the last two pitched notes in the melody
        /// (Interval Sight Training's final question).
        /// </summary>
        private static bool IsFinalSightTrainingPair(List<GeneratedNote> notes, List<int> pairIdx)
        {
            if (pairIdx.Count != 2)
                return false;

            int lastPitched = -1;
            for (int i = notes.Count - 1; i >= 0; i--)
            {
                if (!notes[i].IsRest)
                {
                    lastPitched = i;
                    break;
                }
            }

            if (lastPitched < 1)
                return false;

            int prevPitched = -1;
            for (int i = lastPitched - 1; i >= 0; i--)
            {
                if (!notes[i].IsRest)
                {
                    prevPitched = i;
                    break;
                }
            }

            if (prevPitched < 0)
                return false;

            int a = Math.Min(pairIdx[0], pairIdx[1]);
            int b = Math.Max(pairIdx[0], pairIdx[1]);
            return a == prevPitched && b == lastPitched;
        }

        private void ShiftNoteAndBarLayouts(NoteLayout[] noteLayouts, BarLayout[] barLayouts, float shift)
        {
            for (int i = 0; i < noteLayouts.Length; i++)
            {
                noteLayouts[i].X += shift;
                if (noteLayouts[i].HasAccidental)
                    SyncAccidentalX(noteLayouts, i);
                else
                    noteLayouts[i].AccidentalX = noteLayouts[i].X;
            }

            for (int i = 0; i < barLayouts.Length; i++)
                barLayouts[i].X += shift;
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
            DebugLog.WriteLine($"[Staff Draw] {ex.GetType().Name}: {detail.Message}");
            DebugLog.WriteLine($"[Staff Draw] {ex}");
            Utilities.Utils.Log($"[Staff Draw] {ex.GetType().Name}: {detail.Message}");
#endif
        }

        private static void DrawStaffErrorMessage(ICanvas canvas, RectF dirtyRect, Color ink, Exception ex)
        {
            canvas.FontColor = ink;
            canvas.FontSize = 12;
            var detail = ex;
            while (detail.InnerException != null)
                detail = detail.InnerException;
            canvas.DrawString(
                $"Notation draw error: {detail.GetType().Name}",
                dirtyRect.X + 8, dirtyRect.Y + dirtyRect.Height * 0.45f,
                dirtyRect.Width - 16, 40f,
                HorizontalAlignment.Left, VerticalAlignment.Top);
        }

        /// <summary>
        /// Screen stage for child levels 1–30: map finished staff-local Plan geometry once,
        /// then at most one uniform fit into <paramref name="layoutRightLimit"/>.
        /// No Strict / PlaceBarLines / Refinish / Reconcile / Align after mapping.
        /// </summary>
        private void FinishBeginnerHorizontalLayout(
            List<GeneratedNote> upperNotes,
            NoteLayout[] upperNoteLayouts,
            BarLayout[] upperBarLayouts,
            float upperTotalWidth,
            float upperUsableWidth,
            List<GeneratedNote> lowerNotes,
            NoteLayout[] lowerNoteLayouts,
            BarLayout[] lowerBarLayouts,
            float lowerTotalWidth,
            float lowerUsableWidth,
            float safeLeft,
            float upperStaffMargin,
            float lowerStaffMargin,
            float layoutRightLimit,
            float upperTop,
            float upperMid,
            float upperBot,
            float lowerTop,
            float lowerMid,
            float lowerBot,
            bool expandLowerToFill)
        {
            _ = upperNotes;
            _ = lowerNotes;
            _ = upperTop;
            _ = upperMid;
            _ = upperBot;
            _ = lowerTop;
            _ = lowerMid;
            _ = lowerBot;
            _ = expandLowerToFill;

            ApplyBeginnerStaffToScreen(upperNoteLayouts, upperBarLayouts, safeLeft, upperStaffMargin,
                upperTotalWidth, upperUsableWidth);
            ApplyBeginnerStaffToScreen(lowerNoteLayouts, lowerBarLayouts, safeLeft, lowerStaffMargin,
                lowerTotalWidth, lowerUsableWidth);

            FitMappedLayoutIntoRightLimit(upperNoteLayouts, upperBarLayouts, layoutRightLimit, safeLeft, upperStaffMargin);
            FitMappedLayoutIntoRightLimit(lowerNoteLayouts, lowerBarLayouts, layoutRightLimit, safeLeft, lowerStaffMargin);
        }

        private void LogBeginnerLayoutBounds(
            NoteLayout[] upperNoteLayouts,
            BarLayout[] upperBarLayouts,
            NoteLayout[] lowerNoteLayouts,
            BarLayout[] lowerBarLayouts,
            float layoutRightLimit,
            float safeRight)
        {
            float uRight = GetLayoutMaxRight(upperNoteLayouts, upperBarLayouts);
            float lRight = GetLayoutMaxRight(lowerNoteLayouts, lowerBarLayouts);
            float maxRight = Math.Max(uRight, lRight);
            float uEnd = upperBarLayouts.Length > 0 ? upperBarLayouts[^1].X : 0f;
            float lEnd = lowerBarLayouts.Length > 0 ? lowerBarLayouts[^1].X : 0f;
            StaffLog($"[Staff] ContentRight={maxRight:F0} (endBar U={uEnd:F0} L={lEnd:F0}), " +
                  $"LayoutLimit={layoutRightLimit:F0}, gutter={(layoutRightLimit - maxRight):F1}, " +
                  $"pastSafeRight={(maxRight > safeRight ? maxRight - safeRight : 0f):F1}");
        }

        private void ApplyBeginnerStaffToScreen(
            NoteLayout[] noteLayouts,
            BarLayout[] barLayouts,
            float safeLeft,
            float staffLeftMargin,
            float totalWidth,
            float usableWidth)
        {
            // totalWidth includes EffectiveRightMargin after the last bar; usableWidth is the measure lane.
            // Do not treat that accounting pad as overflow (it falsely scaled every staff ~5%).
            float contentSpan = totalWidth - staffLeftMargin;
            float allowedSpan = usableWidth + EffectiveRightMargin;
            float scale = contentSpan > allowedSpan + 0.5f && contentSpan > 0f
                ? Math.Clamp(allowedSpan / contentSpan, HorizontalCompressFloor, 1f)
                : 1f;
            MapStaffLayoutToScreen(noteLayouts, barLayouts, safeLeft, staffLeftMargin, scale);
        }

        /// <summary>
        /// Screen stage: affine map of finished staff-local X → screen X.
        /// After this returns, structural musical mutators are blocked via <see cref="_horizontalScreenStage"/>.
        /// </summary>
        private void MapStaffLayoutToScreen(
            NoteLayout[] noteLayouts, BarLayout[] barLayouts,
            float safeLeft, float staffLeftMargin, float scale = 1f)
        {
            StaffLayoutDiag.Count(nameof(MapStaffLayoutToScreen));
            for (int i = 0; i < noteLayouts.Length; i++)
            {
                float relativeX = noteLayouts[i].X - staffLeftMargin;
                noteLayouts[i].X = safeLeft + staffLeftMargin + (relativeX * scale);
                // Recompute from the new center — never leave a stale AccidentalX after scale.
                if (noteLayouts[i].HasAccidental)
                    SyncAccidentalX(noteLayouts, i);
                else
                    noteLayouts[i].AccidentalX = noteLayouts[i].X;
            }

            for (int i = 0; i < barLayouts.Length; i++)
            {
                float relativeX = barLayouts[i].X - staffLeftMargin;
                barLayouts[i].X = safeLeft + staffLeftMargin + (relativeX * scale);
            }

            // Enter screen stage: no further independent note/bar/measure mutation.
            _horizontalScreenStage = true;
        }

        private void ApplyHorizontalScale(NoteLayout[] noteLayouts, BarLayout[] barLayouts,
                                          float scale, float safeLeft, float staffLeftMargin)
            => MapStaffLayoutToScreen(noteLayouts, barLayouts, safeLeft, staffLeftMargin, scale);

        /// <summary>
        /// Screen-stage only: one uniform scale (and/or shift) so the whole staff's ink fits
        /// left of <paramref name="layoutRightLimit"/>. Applies the same affine transform to
        /// notes, accidentals, and bars — never repairs individual objects.
        /// Scale accounts for constant ink pads (notehead/accidental/bar width) that do not
        /// shrink with X, so a naive center-span scale cannot leave ink past the limit.
        /// </summary>
        private void FitMappedLayoutIntoRightLimit(
            NoteLayout[] noteLayouts,
            BarLayout[] barLayouts,
            float layoutRightLimit,
            float safeLeft,
            float staffLeftMargin)
        {
            StaffLayoutDiag.Count(nameof(FitMappedLayoutIntoRightLimit));
            float limit = layoutRightLimit;
            float floorCenter = safeLeft + staffLeftMargin;

            float minX = GetLayoutMinX(noteLayouts, barLayouts);
            float maxRight = GetLayoutMaxRight(noteLayouts, barLayouts);
            if (maxRight <= limit + 0.5f)
                return;

            // x' = floor + (x - minX) * s  ⇒  inkRight' = floor + (x - minX)*s + trail
            // Require inkRight' <= limit for every object (trail/barExtra are not scaled).
            float UnconstrainedFitScale()
            {
                float s = 1f;
                for (int i = 0; i < noteLayouts.Length; i++)
                {
                    float x = noteLayouts[i].X;
                    float trail = NoteInkRightForLayout(noteLayouts[i]) - x;
                    float denom = x - minX;
                    if (denom <= 0.01f)
                        continue;
                    float allowed = limit - floorCenter - trail;
                    if (allowed <= 0f)
                        continue;
                    s = Math.Min(s, allowed / denom);
                }

                for (int i = 0; i < barLayouts.Length; i++)
                {
                    float x = barLayouts[i].X;
                    float extra = BarLineRightEdge(barLayouts[i]) - x;
                    float denom = x - minX;
                    if (denom <= 0.01f)
                        continue;
                    float allowed = limit - floorCenter - extra;
                    if (allowed <= 0f)
                        continue;
                    s = Math.Min(s, allowed / denom);
                }

                return s;
            }

            float scale = UnconstrainedFitScale();
            if (scale < 0.999f)
            {
                // Prefer readable size, but never leave ink past the drawable right edge.
                float safeScale = Math.Clamp(scale, MinimumSafeHorizontalScale, 1f);
                ScaleLayoutOntoFloor(noteLayouts, barLayouts, minX, floorCenter, safeScale);
            }

            maxRight = GetLayoutMaxRight(noteLayouts, barLayouts);
            if (maxRight <= limit + 0.5f)
                return;

            // Residual: shift left as a unit without crossing the header floor.
            float shift = limit - maxRight;
            float newMin = GetLayoutMinX(noteLayouts, barLayouts) + shift;
            if (newMin < floorCenter)
                shift += floorCenter - newMin;

            if (Math.Abs(shift) >= 0.01f)
            {
                for (int i = 0; i < noteLayouts.Length; i++)
                {
                    noteLayouts[i].X += shift;
                    if (noteLayouts[i].HasAccidental)
                        SyncAccidentalX(noteLayouts, i);
                    else
                        noteLayouts[i].AccidentalX = noteLayouts[i].X;
                }

                for (int i = 0; i < barLayouts.Length; i++)
                    barLayouts[i].X += shift;
            }

            maxRight = GetLayoutMaxRight(noteLayouts, barLayouts);
            if (maxRight <= limit + 0.5f)
                return;

            // Last resort: go below the safe scale floor so packed content stays on-canvas.
            // Packing should have avoided this by wrapping earlier; one over-wide measure may
            // still need it rather than clipping accidentals past the staff end.
            minX = GetLayoutMinX(noteLayouts, barLayouts);
            float finalScale = UnconstrainedFitScale();
            if (finalScale < 0.999f && finalScale > 0.05f)
                ScaleLayoutOntoFloor(noteLayouts, barLayouts, minX, floorCenter, finalScale);
        }

        /// <summary>
        /// Lines up only the final bar between independent staves when both end at a similar
        /// horizontal position. Internal bars stay per-staff. Short lower/final lines keep a
        /// ragged-right end bar after their last measure instead of a far-right aligned bar.
        /// </summary>
        private void AlignIndependentStaffEndBars(
            BarLayout[] upperBarLayouts,
            NoteLayout[] upperNoteLayouts,
            BarLayout[] lowerBarLayouts,
            NoteLayout[] lowerNoteLayouts,
            float layoutRightLimit,
            float safeLeft,
            float upperStaffMargin,
            float lowerStaffMargin)
        {
            StaffLayoutDiag.Count(nameof(AlignIndependentStaffEndBars));
            if (upperBarLayouts.Length == 0 || lowerBarLayouts.Length == 0)
                return;

            // Ensure each staff's own final bar still clears its last note.
            float upperRequired = RequiredEndBarX(upperNoteLayouts);
            float lowerRequired = RequiredEndBarX(lowerNoteLayouts);
            if (upperRequired > float.NegativeInfinity)
                upperBarLayouts[^1].X = Math.Max(upperBarLayouts[^1].X, upperRequired);
            if (lowerRequired > float.NegativeInfinity)
                lowerBarLayouts[^1].X = Math.Max(lowerBarLayouts[^1].X, lowerRequired);

            bool isDouble = upperBarLayouts[^1].IsDouble || lowerBarLayouts[^1].IsDouble;
            float maxBarX = layoutRightLimit - (isDouble ? DoubleBarExtraWidth : 0f);
            float upperFloor = safeLeft + upperStaffMargin;
            float lowerFloor = safeLeft + lowerStaffMargin;

            if (ShouldKeepRaggedLowerEndBar(
                    upperBarLayouts[^1].X,
                    lowerBarLayouts[^1].X,
                    layoutRightLimit))
            {
                CapStaffEndBar(upperNoteLayouts, upperBarLayouts, maxBarX, upperFloor);
                CapStaffEndBar(lowerNoteLayouts, lowerBarLayouts, maxBarX, lowerFloor);
                return;
            }

            float endX = Math.Max(upperBarLayouts[^1].X, lowerBarLayouts[^1].X);
            if (endX > maxBarX)
            {
                FitNoteInkBefore(upperNoteLayouts, maxBarX, upperFloor);
                FitNoteInkBefore(lowerNoteLayouts, maxBarX, lowerFloor);
                endX = maxBarX;
            }

            upperBarLayouts[^1].X = endX;
            lowerBarLayouts[^1].X = endX;
        }

        /// <summary>Keeps a staff's last bar on-canvas and its note ink to the left of that bar.</summary>
        private void CapStaffEndBar(
            NoteLayout[] noteLayouts,
            BarLayout[] barLayouts,
            float maxBarX,
            float floorCenter)
        {
            if (barLayouts.Length == 0)
                return;

            float barX = Math.Min(barLayouts[^1].X, maxBarX);
            FitNoteInkBefore(noteLayouts, barX, floorCenter);
            barLayouts[^1].X = barX;
        }

        /// <summary>Scales or shifts note ink so the last trailing edge does not pass <paramref name="maxTrailingRight"/>.</summary>
        private void FitNoteInkBefore(NoteLayout[] noteLayouts, float maxTrailingRight, float floorCenter)
        {
            if (noteLayouts.Length == 0 || maxTrailingRight <= floorCenter)
                return;

            float minX = float.PositiveInfinity;
            float maxRight = float.NegativeInfinity;
            for (int i = 0; i < noteLayouts.Length; i++)
            {
                float left = NoteGroupLeftFromLayout(noteLayouts[i]);
                if (left < minX)
                    minX = left;
                float right = NoteInkRightForLayout(noteLayouts[i]);
                if (right > maxRight)
                    maxRight = right;
            }

            if (maxRight <= maxTrailingRight + 0.5f)
                return;

            float span = maxRight - minX;
            float avail = maxTrailingRight - minX;
            if (span > 1f && avail > 1f && span > avail)
            {
                float s = Math.Clamp(avail / span, HorizontalCompressFloor, 1f);
                for (int i = 0; i < noteLayouts.Length; i++)
                {
                    noteLayouts[i].X = minX + (noteLayouts[i].X - minX) * s;
                    if (noteLayouts[i].HasAccidental)
                        SyncAccidentalX(noteLayouts, i);
                    else
                        noteLayouts[i].AccidentalX = noteLayouts[i].X;
                }
            }
            else
            {
                float shift = maxTrailingRight - maxRight;
                if (minX + shift < floorCenter)
                    shift = floorCenter - minX;
                if (Math.Abs(shift) < 0.01f)
                    return;
                for (int i = 0; i < noteLayouts.Length; i++)
                {
                    noteLayouts[i].X += shift;
                    if (noteLayouts[i].HasAccidental)
                        SyncAccidentalX(noteLayouts, i);
                    else
                        noteLayouts[i].AccidentalX = noteLayouts[i].X;
                }
            }
        }

        /// <summary>
        /// Sight Training: replace all bars with a single double bar placed after the last
        /// note's trailing ink (including accidentals). Called only after final spacing/pan.
        /// </summary>
        private BarLayout[] FinalizeSingleStaffEndBar(NoteLayout[] noteLayouts)
        {
            float endX = RequiredEndBarX(noteLayouts);
            if (endX <= float.NegativeInfinity)
                endX = _headerMetrics.LeftMargin + 40f;

            // Include accidental left/right reach so nothing paints past the bar.
            for (int i = 0; i < noteLayouts.Length; i++)
            {
                float right = NoteInkRightForLayout(noteLayouts[i]);
                if (noteLayouts[i].HasAccidental)
                    right = Math.Max(right, noteLayouts[i].X + _layout.NoteHeadR);
                endX = Math.Max(endX, right + BarRightPadding);
            }

            return new[] { new BarLayout { X = endX, IsDouble = true } };
        }

        /// <summary>Legacy helper retained for call sites that mutate an existing bar array.</summary>
        private void EnsureSingleStaffDoubleEndBar(NoteLayout[] noteLayouts, BarLayout[] barLayouts)
        {
            var finalized = FinalizeSingleStaffEndBar(noteLayouts);
            if (barLayouts.Length == 0)
                return;
            for (int i = 0; i < barLayouts.Length - 1; i++)
                barLayouts[i].IsDouble = false;
            barLayouts[^1] = finalized[0];
        }

        private float RequiredEndBarX(NoteLayout[] noteLayouts)
        {
            float lastNoteRight = float.NegativeInfinity;
            for (int i = 0; i < noteLayouts.Length; i++)
            {
                float right = NoteInkRightForLayout(noteLayouts[i]);
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
            IReadOnlyList<double> barBeats,
            double beatOrigin,
            float staffTop, float staffMid, float staffBot)
        {
            if (notes.Count == 0 || barLayouts.Length == 0)
                return;

            var sortedBarBeatsRel = barBeats.Select(b => b - beatOrigin).OrderBy(b => b).ToList();

            for (int i = 0; i < notes.Count; i++)
            {
                float centerX = noteLayouts[i].X;
                if (!notes[i].IsRest)
                {
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

                var layout = noteLayouts[i];
                centerX = layout.X;
                float groupLeft = NoteGroupLeftFromLayout(layout);
                double noteBeat = (notes[i].BeatPosition ?? 0.0) - beatOrigin;

                for (int b = 0; b < barLayouts.Length - 1; b++)
                {
                    if (b < sortedBarBeatsRel.Count && noteBeat < sortedBarBeatsRel[b] - 1e-6)
                        continue;

                    float barX = barLayouts[b].X;
                    float minGroupLeft = barX + BarLeftPadding + BarStemClearance;
                    if (groupLeft < minGroupLeft - 0.5f)
                    {
                        noteLayouts[i].X += minGroupLeft - groupLeft;
                        SyncAccidentalX(noteLayouts, i);
                        groupLeft = NoteGroupLeftFromLayout(noteLayouts[i]);
                    }
                }
            }
        }

        private void FinalizeStaffBarClearance(
            List<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            BarLayout[] barLayouts,
            IReadOnlyList<double> barBeats,
            double beatOrigin,
            float staffTop, float staffMid, float staffBot)
        {
            RefinishMeasureSpacing(notes, noteLayouts, barLayouts, barBeats, beatOrigin);
            NudgeNotesClearOfBarlines(notes, noteLayouts, barLayouts, barBeats, beatOrigin, staffTop, staffMid, staffBot);
            ReconcileFinalBarLayout(noteLayouts, barLayouts);
        }

        /// <summary>Ensures the final bar clears the last note without moving internal bar lines.</summary>
        private void ReconcileFinalBarLayout(NoteLayout[] noteLayouts, BarLayout[] barLayouts)
        {
            StaffLayoutDiag.Count(nameof(ReconcileFinalBarLayout));
            if (noteLayouts.Length == 0 || barLayouts.Length == 0)
                return;

            float lastNoteRight = float.NegativeInfinity;
            for (int i = 0; i < noteLayouts.Length; i++)
            {
                float right = NoteInkRightForLayout(noteLayouts[i]);
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
        /// Places the single Tuner reference note in the middle of the open staff
        /// (after clef, before the safe right edge). No-op for multi-note music.
        /// </summary>
        private void CenterTunerReferenceNote(
            NoteLayout[] noteLayouts,
            float safeLeft,
            float staffLeftMargin,
            float layoutRightLimit)
        {
            if (noteLayouts.Length != 1)
                return;

            float left = safeLeft + staffLeftMargin + BarLeftPadding;
            float right = layoutRightLimit - BarRightPadding;
            if (right <= left)
                return;

            float targetCenter = (left + right) * 0.5f;
            float groupLeft = NoteGroupLeftFromLayout(noteLayouts[0]);
            float groupRight = noteLayouts[0].X + NoteCenterTrailingReach(
                noteLayouts[0].IsRest, noteLayouts[0].Duration);
            float groupCenter = (groupLeft + groupRight) * 0.5f;
            noteLayouts[0].X += targetCenter - groupCenter;
            SyncAccidentalX(noteLayouts, 0);
        }

        /// <summary>
        /// Ear Training compact reveal: two noteheads at fixed X positions that never depend
        /// on whether either note has an accidental. Each slot always reserves a left
        /// accidental zone wide enough for a flat; accidentals are then drawn to the left of
        /// their own notehead. The pair is centered in the panel.
        /// </summary>
        private void CenterOmitHeaderIntervalReveal(
            List<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            ref float upperTop,
            ref float upperMid,
            ref float upperBot,
            float dirtyHeight,
            float safeLeft,
            float layoutRightLimit)
        {
            if (!OmitStaffHeader || notes == null || noteLayouts == null || noteLayouts.Length == 0)
                return;

            int count = Math.Min(notes.Count, noteLayouts.Length);

            // Beat order (stable) — do not rely on whatever ExpandLayout did to X.
            var order = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                if (!notes[i].IsRest)
                    order.Add(i);
            }

            order.Sort((a, b) =>
            {
                double ba = notes[a].BeatPosition ?? a;
                double bb = notes[b].BeatPosition ?? b;
                int cmp = ba.CompareTo(bb);
                return cmp != 0 ? cmp : a.CompareTo(b);
            });

            PlaceOmitHeaderFixedNoteheads(
                noteLayouts, order, safeLeft, layoutRightLimit);

            // ── Vertical: fit heads, stems, and ledger lines inside top/bottom margins ──
            float staffTop = upperTop;
            float staffMid = upperMid;
            float staffBot = upperBot;

            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            float stemLen = _layout.StemLen;
            float headHalfH = OmitStaffHeader
                ? OmitHeaderNoteHeadHalfHeight
                : _layout.NoteHeadR * NoteHeadHeightFactor * 0.5f;
            for (int i = 0; i < count; i++)
            {
                if (notes[i].IsRest)
                    continue;

                float ny = NoteY(notes[i], staffTop, staffMid);
                bool stemUp = ny >= staffMid;
                float top = stemUp ? ny - stemLen : ny - headHalfH;
                float bot = stemUp ? ny + headHalfH : ny + stemLen;

                var (ledgerTop, ledgerBot) = EstimateLedgerVerticalExtent(
                    ny, staffTop, staffBot, _layout.Sls);
                top = Math.Min(top, ledgerTop);
                bot = Math.Max(bot, ledgerBot);

                if (i < noteLayouts.Length && noteLayouts[i].HasAccidental)
                {
                    float accHalf = OmitStaffHeader
                        ? OmitHeaderStaffSpace * OmitHeaderAccidentalHeightSpaces * 0.5f
                        : (noteLayouts[i].AccidentalIsNatural
                            ? BodyAccidentalFontSize(isFlat: false, isNatural: true)
                            : BodyAccidentalFontSize(noteLayouts[i].AccidentalIsFlat)) * 0.62f;
                    top = Math.Min(top, ny - accHalf);
                    bot = Math.Max(bot, ny + accHalf);
                }

                if (top < minY) minY = top;
                if (bot > maxY) maxY = bot;
            }

            if (!float.IsFinite(minY) || !float.IsFinite(maxY) || maxY <= minY)
                return;

            const float margin = OmitHeaderPanelMargin;
            float availTop = margin;
            float availBot = dirtyHeight - margin;
            if (availBot <= availTop + 8f)
            {
                availTop = 4f;
                availBot = dirtyHeight - 4f;
            }

            float contentH = maxY - minY;
            float availH = availBot - availTop;
            float contentMid = (minY + maxY) * 0.5f;
            float targetY = (availTop + availBot) * 0.5f;
            float dy = targetY - contentMid;

            if (contentH > availH)
                dy = availTop - minY + (availH - contentH) * 0.5f;
            else
            {
                if (minY + dy < availTop)
                    dy = availTop - minY;
                if (maxY + dy > availBot)
                    dy = availBot - maxY;
            }

            if (Math.Abs(dy) < 0.25f)
                return;

            upperTop += dy;
            upperMid += dy;
            upperBot += dy;
        }

        /// <summary>Inset used for Ear Training headerless staff lines and note clearance.</summary>
        private const float OmitHeaderPanelMargin = 12f;

        /// <summary>Hard minimum inset so accidentals cannot paint past the panel edge.</summary>
        private const float OmitHeaderMinEdgePad = 4f;

        /// <summary>
        /// Left reach from notehead center that is always reserved in the Ear Training panel:
        /// widest body accidental (flat) plus gap, independent of whether this note draws one.
        /// </summary>
        private float OmitHeaderReservedLeftReach()
            => OmitHeaderNoteHeadHalfWidth
               + OmitHeaderAccidentalGap
               + BodyAccidentalDrawWidth(isFlat: true)
               + OmitHeaderStaffSpace * 0.08f;

        /// <summary>
        /// Places Ear Training noteheads at fixed X slots. Slot geometry never uses the
        /// measured width of a particular accidental — only a reserved left zone and a
        /// trailing-ink allowance. Accidentals are synced to the left of their own head.
        /// </summary>
        private void PlaceOmitHeaderFixedNoteheads(
            NoteLayout[] noteLayouts,
            List<int> order,
            float safeLeft,
            float layoutRightLimit)
        {
            if (order.Count == 0)
                return;

            float leftReach = OmitHeaderReservedLeftReach();
            float rightReach = Math.Max(
                NoteCenterTrailingReach(false, NoteDuration.Quarter),
                OmitHeaderNoteHeadHalfWidth + OmitHeaderStemStroke);

            float minLeft = safeLeft + OmitHeaderMinEdgePad;
            float maxRight = layoutRightLimit - OmitHeaderMinEdgePad;

            float availLeft = safeLeft + OmitHeaderPanelMargin;
            float availRight = layoutRightLimit - OmitHeaderPanelMargin;
            int n = order.Count;
            float minGap = n > 1 ? 3f : 0f;
            float needed = n * (leftReach + rightReach) + minGap * Math.Max(0, n - 1);
            if (availRight <= availLeft + 8f || needed > availRight - availLeft)
            {
                availLeft = minLeft;
                availRight = maxRight;
            }
            float comfortableGap = Math.Max(MinInkGap, _layout.Sls * 0.75f);
            float gap = n > 1 ? comfortableGap : 0f;

            float PairSpan(float g)
                => n * (leftReach + rightReach) + Math.Max(0, n - 1) * g;

            float availW = availRight - availLeft;
            if (n > 1 && PairSpan(gap) > availW)
                gap = Math.Max(4f, (availW - n * (leftReach + rightReach)) / (n - 1));

            float span = PairSpan(gap);
            if (span > availW)
            {
                // Pull side padding in equally so both reserved slots stay on-canvas.
                float mid = (availLeft + availRight) * 0.5f;
                availLeft = mid - span * 0.5f;
                availRight = mid + span * 0.5f;
                if (availLeft < minLeft)
                {
                    float d = minLeft - availLeft;
                    availLeft += d;
                    availRight += d;
                }

                if (availRight > maxRight)
                {
                    float d = availRight - maxRight;
                    availLeft -= d;
                    availRight -= d;
                }

                availW = availRight - availLeft;
                if (n > 1 && PairSpan(gap) > availW)
                    gap = Math.Max(3f, (availW - n * (leftReach + rightReach)) / (n - 1));
                span = PairSpan(gap);
            }

            float extra = Math.Max(0f, availW - span);
            float x = availLeft + extra * 0.5f + leftReach;
            for (int oi = 0; oi < n; oi++)
            {
                int i = order[oi];
                noteLayouts[i].X = x;
                if (noteLayouts[i].HasAccidental)
                    SyncAccidentalX(noteLayouts, i);
                else
                    noteLayouts[i].AccidentalX = x;

                x += rightReach + gap + leftReach;
            }
        }

        /// <summary>
        /// Fits notes and bars inside [header floor, layout right limit]. Uses uniform scale when the
        /// span is too wide; otherwise a single shift. Avoids pulling past the left floor.
        /// A final exact-fit pass runs if the 0.85 compress floor still left ink past the limit.
        /// </summary>
        private void ClampLayoutToSafeRight(
            NoteLayout[] noteLayouts,
            BarLayout[] barLayouts,
            float layoutRightLimit,
            float safeLeft,
            float staffLeftMargin)
        {
            StaffLayoutDiag.Count(nameof(ClampLayoutToSafeRight));
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
                float s = Math.Clamp(avail / span, HorizontalCompressFloor, 1f);
                ScaleLayoutOntoFloor(noteLayouts, barLayouts, minX, floorCenter, s);
            }
            else if (maxRight > limit)
            {
                float shift = -(maxRight - limit);
                if (minX + shift < floorCenter)
                    shift += floorCenter - (minX + shift);

                if (Math.Abs(shift) >= 0.01f)
                {
                    for (int i = 0; i < noteLayouts.Length; i++)
                    {
                        noteLayouts[i].X += shift;
                        if (noteLayouts[i].HasAccidental)
                            SyncAccidentalX(noteLayouts, i);
                        else
                            noteLayouts[i].AccidentalX = noteLayouts[i].X;
                    }

                    for (int i = 0; i < barLayouts.Length; i++)
                        barLayouts[i].X += shift;
                }
            }

            minX = GetLayoutMinX(noteLayouts, barLayouts);
            maxRight = GetLayoutMaxRight(noteLayouts, barLayouts);
            if (maxRight <= limit + 0.5f)
                return;

            span = maxRight - minX;
            avail = limit - floorCenter;
            if (span > 1f && avail > 1f)
                ScaleLayoutOntoFloor(noteLayouts, barLayouts, minX, floorCenter, avail / span);
        }

        private void ScaleLayoutOntoFloor(
            NoteLayout[] noteLayouts,
            BarLayout[] barLayouts,
            float minX,
            float floorCenter,
            float scale)
        {
            for (int i = 0; i < noteLayouts.Length; i++)
            {
                noteLayouts[i].X = floorCenter + (noteLayouts[i].X - minX) * scale;
                if (noteLayouts[i].HasAccidental)
                    SyncAccidentalX(noteLayouts, i);
                else
                    noteLayouts[i].AccidentalX = noteLayouts[i].X;
            }

            for (int i = 0; i < barLayouts.Length; i++)
                barLayouts[i].X = floorCenter + (barLayouts[i].X - minX) * scale;
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
            StaffLayoutDiag.Count(nameof(ExpandLayoutToFillSafeRight));
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

            StaffLog($"[Staff] Expand: span {contentSpan:F0} → {targetSpan:F0}, ×{stretch:F3}");

            for (int i = 0; i < noteLayouts.Length; i++)
            {
                noteLayouts[i].X = anchor + (noteLayouts[i].X - anchor) * stretch;
                if (noteLayouts[i].HasAccidental)
                    SyncAccidentalX(noteLayouts, i);
                else
                    noteLayouts[i].AccidentalX = noteLayouts[i].X;
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
            StaffLayoutDiag.Count(nameof(PadLayoutGutterToLimit));
            float maxRight = GetLayoutMaxRight(noteLayouts, barLayouts);
            if (maxRight >= layoutRightLimit - 1f)
                return;

            float delta = layoutRightLimit - maxRight;
            for (int i = 0; i < noteLayouts.Length; i++)
            {
                noteLayouts[i].X += delta;
                if (noteLayouts[i].HasAccidental)
                    SyncAccidentalX(noteLayouts, i);
                else
                    noteLayouts[i].AccidentalX = noteLayouts[i].X;
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
                float right = NoteInkRightForLayout(noteLayouts[i]);
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

        /// <summary>Total beat span of notes on one staff (relative to that staff's beat origin).</summary>
        private static double ComputeStaffTotalBeats(
            IReadOnlyList<GeneratedNote> notes,
            IReadOnlyList<double> barBeats)
        {
            if (notes == null || notes.Count == 0)
                return 0.0;

            double beatOrigin = GetStaffBeatOrigin(notes, barBeats ?? Array.Empty<double>());
            double totalBeats = 0.0;
            for (int i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                double relBeat = (note.BeatPosition ?? 0.0) - beatOrigin;
                totalBeats = Math.Max(totalBeats, relBeat + note.BeatDuration);
            }

            return totalBeats;
        }

        /// <summary>
        /// Beat-count ratio below which the lower/final staff is treated as a short line:
        /// independent readable width, no full-staff expand, ragged-right end bar.
        /// </summary>
        private const float ShortLowerStaffBeatRatio = 0.55f;

        /// <summary>
        /// Cap on how much of the lower usable width a short final line may occupy when planned
        /// independently (avoids stretching one sparse measure across the whole staff).
        /// </summary>
        private const float ShortLowerMaxStaffFill = 0.58f;

        /// <summary>
        /// Widen factor applied to matched px/beat width for short lower staves so one measure
        /// under a full upper staff is readable (not a 20–25% strip).
        /// </summary>
        private const float ShortLowerWidenFactor = 1.85f;

        /// <summary>Absolute readable floor (px per beat) for short lower-staff planning.</summary>
        private const float ShortLowerMinPxPerBeat = 32f;

        /// <summary>
        /// True when the lower staff has substantially fewer beats than the upper
        /// (typical short final scale-walk / packed remainder line).
        /// </summary>
        internal static bool IsShortLowerStaff(double upperTotalBeats, double lowerTotalBeats)
        {
            if (upperTotalBeats < 1e-6 || lowerTotalBeats < 1e-6)
                return false;
            return lowerTotalBeats < upperTotalBeats * ShortLowerStaffBeatRatio - 1e-6;
        }

        /// <summary>
        /// When this staff has fewer beats than its peer, size it so px-per-beat matches the peer
        /// instead of stretching across the full canvas width — unless the staff is a short final
        /// line, in which case use an independent readable width (widened matched, capped fill).
        /// </summary>
        internal static float ComputeMatchedStaffUsableWidth(
            float peerContentWidth,
            double peerTotalBeats,
            double selfTotalBeats,
            float selfUsableWidth)
        {
            if (peerTotalBeats < 1e-6 || selfTotalBeats < 1e-6)
                return selfUsableWidth;
            if (selfTotalBeats >= peerTotalBeats - 1e-6)
                return selfUsableWidth;

            float pxPerBeat = peerContentWidth / (float)peerTotalBeats;
            float matched = (float)selfTotalBeats * pxPerBeat;

            if (IsShortLowerStaff(peerTotalBeats, selfTotalBeats))
            {
                float natural = Math.Max(
                    matched * ShortLowerWidenFactor,
                    (float)selfTotalBeats * ShortLowerMinPxPerBeat);
                float cap = selfUsableWidth * ShortLowerMaxStaffFill;
                return Math.Clamp(natural, 64f, Math.Min(cap, selfUsableWidth));
            }

            return Math.Clamp(matched, 64f, selfUsableWidth);
        }

        /// <summary>
        /// Keep a ragged-right lower end bar when it ends substantially before the upper end bar.
        /// Comparable staves still align.
        /// </summary>
        internal static bool ShouldKeepRaggedLowerEndBar(
            float upperEndBarX,
            float lowerEndBarX,
            float layoutRightLimit)
        {
            float gap = upperEndBarX - lowerEndBarX;
            if (gap <= 1f)
                return false;
            float threshold = Math.Max(48f, layoutRightLimit * 0.12f);
            return gap > threshold;
        }

        /// <summary>
        /// Bar lines on a regular meter grid from accumulated beat duration.
        /// Measure-index transitions are only used when a meter grid cannot be built
        /// (avoids denser off-meter MeasureIndex values drawing bars every 2 beats in 4/4).
        /// </summary>
        private List<double> ResolveStaffBarBeats(
            IReadOnlyList<GeneratedNote> notes,
            List<double> barBeats,
            double beatOrigin)
        {
            var fromMeasures = notes.Any(n => n.MeasureIndex.HasValue)
                ? BuildBarBeatsFromMeasureIndices(notes)
                : new List<double>();

            var regular = EnsureRegularBarBeats(
                notes,
                fromMeasures.Count > 0 ? fromMeasures : barBeats,
                beatOrigin);

#if DEBUG
            if (fromMeasures.Count > regular.Count && regular.Count > 0)
            {
                StaffLog(
                    $"[LayoutTest] Bar beats: regular meter grid ({regular.Count}) preferred over denser measure-index ({fromMeasures.Count})");
            }
#endif

            // Always prefer the display meter grid when it yields internal bars.
            if (regular.Count > 0)
                return regular;

            if (fromMeasures.Count > 0)
                return fromMeasures;

            return barBeats ?? new List<double>();
        }

        private static List<double> BuildBarBeatsFromMeasureIndices(IReadOnlyList<GeneratedNote> notes)
        {
            var bars = new List<double>();
            if (notes.Count == 0)
                return bars;

            int prevMeasure = -1;
            foreach (var n in notes.OrderBy(n => n.BeatPosition ?? 0.0))
            {
                int mi = n.MeasureIndex ?? (prevMeasure >= 0 ? prevMeasure : 0);
                if (prevMeasure >= 0 && mi != prevMeasure && n.BeatPosition.HasValue)
                {
                    double bp = n.BeatPosition.Value;
                    if (bars.Count == 0 || bp > bars[^1] + 1e-6)
                        bars.Add(bp);
                }
                prevMeasure = mi;
            }

            return bars;
        }

        /// <summary>
        /// Snaps internal bar lines to a regular meter grid when the supplied beats drift
        /// (e.g. per-note measure indices). Pickup and partial final measures are kept.
        /// </summary>
        private List<double> EnsureRegularBarBeats(
            IReadOnlyList<GeneratedNote> notes,
            List<double> barBeats,
            double beatOrigin)
        {
            double measureBeats = _session.GetDisplayMeasureBeats();
            if (notes.Count == 0 || measureBeats <= 0)
                return barBeats;

            double totalBeats = 0.0;
            for (int i = 0; i < notes.Count; i++)
            {
                double rel = (notes[i].BeatPosition ?? 0.0) - beatOrigin;
                totalBeats = Math.Max(totalBeats, rel + notes[i].BeatDuration);
            }

            if (totalBeats <= measureBeats + 1e-6)
                return barBeats;

            var fixedBeats = new List<double>();
            for (double bar = beatOrigin + measureBeats;
                 bar < beatOrigin + totalBeats - 1e-6;
                 bar += measureBeats)
                fixedBeats.Add(bar);

            return fixedBeats;
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

            var noteList = notes as List<GeneratedNote> ?? notes.ToList();
            for (int m = 0; m < segments.Count; m++)
            {
                if (segments[m].NoteIndices.Count == 0)
                    continue;

                float measureRight = m < internalBarCount
                    ? barLayouts[m].X
                    : barLayouts[^1].X;

                float barInnerLeft = m > 0 ? barLayouts[m - 1].X + BarLeftPadding : float.NegativeInfinity;
                float minGroupLeft = m > 0
                    ? Math.Max(barInnerLeft,
                        prevMeasureTrailing > float.NegativeInfinity
                            ? prevMeasureTrailing + _planInkGap
                            : barInnerLeft)
                    : float.NegativeInfinity;

                if (segments[m].NoteIndices.Count == 1)
                {
                    int onlyIdx = segments[m].NoteIndices[0];
                    ShiftMeasureIndicesToMinGroupLeft(noteLayouts, new[] { onlyIdx }, minGroupLeft);
                    prevMeasureTrailing = NoteTrailingRight(notes[onlyIdx], noteLayouts[onlyIdx].X);
                    continue;
                }

                var sorted = SortIndicesByBeat(noteList, segments[m].NoteIndices, beatOrigin);

                // Keep beat-proportional placement for every measure. Sequential MinInkGap
                // repacking of m>0 destroyed within-bar rhythm spacing (clusters + voids).
                ShiftMeasureIndicesToMinGroupLeft(noteLayouts, sorted, minGroupLeft);
                EnforceMeasureNoteGaps(noteList, noteLayouts, sorted);
                RedistributeMeasureLeftoverSpace(
                    noteList, noteLayouts, sorted,
                    minGroupLeft > float.NegativeInfinity ? minGroupLeft : NoteGroupLeftFromLayout(noteLayouts[sorted[0]]),
                    measureRight - BarLeftPadding,
                    beatOrigin);
                ClampMeasureTrailingBeforeBar(noteList, noteLayouts, sorted,
                    measureRight - BarLeftPadding,
                    minGroupLeft > float.NegativeInfinity ? minGroupLeft : float.NegativeInfinity,
                    beatOrigin, segments[m].EndBeat);

                int lastIdx = sorted[^1];
                prevMeasureTrailing = NoteTrailingRight(notes[lastIdx], noteLayouts[lastIdx].X);
            }
        }

        /// <summary>Ensures ink clears internal bar lines on both sides of each measure boundary. Staff-local only.</summary>
        private void EnforceMeasureBarInkMargins(
            IReadOnlyList<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            BarLayout[] barLayouts,
            IReadOnlyList<double> barBeats,
            double beatOrigin)
        {
            if (_horizontalScreenStage)
            {
                StaffLayoutDiag.Count("EnforceMeasureBarInkMargins_BLOCKED_AFTER_SCREEN_MAP");
                return;
            }
            if (notes.Count == 0 || barLayouts.Length == 0)
                return;

            var noteList = notes as List<GeneratedNote> ?? notes.ToList();
            var sortedBarBeats = barBeats.Select(b => b - beatOrigin).OrderBy(b => b).ToList();
            double totalBeats = 0;
            for (int i = 0; i < notes.Count; i++)
            {
                double rel = (notes[i].BeatPosition ?? 0.0) - beatOrigin;
                totalBeats = Math.Max(totalBeats, rel + notes[i].BeatDuration);
            }

            var segments = BuildMeasureSegments(noteList, sortedBarBeats, beatOrigin, totalBeats);
            int internalBarCount = Math.Min(sortedBarBeats.Count, barLayouts.Length);

            for (int b = 0; b < internalBarCount; b++)
            {
                float barX = barLayouts[b].X;
                float maxTrailing = barX - BarLeftPadding - BarStemClearance;
                float minLeading = barX + BarLeftPadding + BarStemClearance;

                if (b < segments.Count && segments[b].NoteIndices.Count > 0)
                {
                    var sorted = SortIndicesByBeat(noteList, segments[b].NoteIndices, beatOrigin);
                    int lastIdx = sorted[^1];
                    float trailing = NoteTrailingRight(notes[lastIdx], noteLayouts[lastIdx].X);
                    if (trailing > maxTrailing + 0.5f)
                    {
                        float innerLeft = b > 0
                            ? barLayouts[b - 1].X + BarLeftPadding
                            : NoteGroupLeftFromLayout(noteLayouts[sorted[0]]);
                        ResolveMeasureNoteSpacing(noteList, noteLayouts, sorted, innerLeft,
                            barX - BarLeftPadding * 0.5f, barX, beatOrigin, segments[b].EndBeat);
                    }
                }

                if (b + 1 < segments.Count && segments[b + 1].NoteIndices.Count > 0)
                {
                    var sortedNext = SortIndicesByBeat(noteList, segments[b + 1].NoteIndices, beatOrigin);
                    int firstIdx = sortedNext[0];
                    float groupLeft = NoteGroupLeftFromLayout(noteLayouts[firstIdx]);
                    if (groupLeft < minLeading - 0.5f)
                    {
                        float shift = minLeading - groupLeft;
                        foreach (int idx in sortedNext)
                        {
                            noteLayouts[idx].X += shift;
                            SyncAccidentalX(noteLayouts, idx);
                        }

                        EnforceGroupOrderSpacingOnIndices(notes, noteLayouts, sortedNext, MinInkGap);

                        // Shifting a whole measure right can shove its last notes past the next
                        // bar (looks like empty early beats / staircase piles). Repack in-slot.
                        float nextBarX = b + 1 < barLayouts.Length
                            ? barLayouts[b + 1].X
                            : barLayouts[^1].X;
                        float trailing = NoteTrailingRight(notes[sortedNext[^1]], noteLayouts[sortedNext[^1]].X);
                        if (trailing > nextBarX - BarLeftPadding - BarStemClearance + 0.5f)
                        {
                            ResolveMeasureNoteSpacing(
                                noteList, noteLayouts, sortedNext,
                                minLeading,
                                nextBarX - BarLeftPadding * 0.5f,
                                nextBarX,
                                beatOrigin,
                                segments[b + 1].EndBeat);
                        }
                    }
                }
            }
        }

        /// <summary>Repacks measures at <see cref="MinInkGap"/> after scale or clamp passes.</summary>
        private void RefinishMeasureSpacing(
            List<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            BarLayout[] barLayouts,
            IReadOnlyList<double> barBeats,
            double beatOrigin)
        {
            if (_horizontalScreenStage)
            {
                StaffLayoutDiag.Count("RefinishMeasureSpacing_BLOCKED_AFTER_SCREEN_MAP");
                return;
            }
            StaffLayoutDiag.Count(nameof(RefinishMeasureSpacing));
            _planInkGap = MinInkGap;
            EnforceMonotonicNoteSpacingInMeasures(notes, noteLayouts, barLayouts, barBeats, beatOrigin);
            EnforceMeasureBarInkMargins(notes, noteLayouts, barLayouts, barBeats, beatOrigin);
        }

        private static float[] CopyLayoutX(NoteLayout[] layouts)
        {
            var copy = new float[layouts.Length];
            for (int i = 0; i < layouts.Length; i++)
                copy[i] = layouts[i].X;
            return copy;
        }

        private float StemStrokeHalfWidth =>
            OmitStaffHeader ? OmitHeaderStemStroke * 0.5f : _layout.GlyphScale;

        private float NoteHeadOuterR =>
            OmitStaffHeader ? OmitHeaderNoteHeadHalfWidth : _layout.NoteHeadR + StemStrokeHalfWidth;

        /// <summary>Draw radius for noteheads; filled and half-note heads share the same outside dimensions.</summary>
        private float NoteHeadDrawR(bool filled)
            => OmitStaffHeader ? OmitHeaderNoteHeadHalfWidth : NoteHeadOuterR;

        /// <summary>
        /// Stem attach at notehead center height: up-stems — outer (right) stroke edge tangent to outer right boundary;
        /// down-stems — outer (left) stroke edge tangent to outer left boundary.
        /// </summary>
        private (float stemX, float stemY) GetStemAttachPoint(float noteX, float noteY, bool stemUp)
        {
            float halfStroke = StemStrokeHalfWidth;
            float halfW = OmitStaffHeader ? OmitHeaderNoteHeadHalfWidth : NoteHeadOuterR;
            float stemX = stemUp ? noteX + halfW - halfStroke : noteX - halfW + halfStroke;
            return (stemX, noteY);
        }

        private float ComputeStemX(GeneratedNote note, float noteCenterX, float staffTop, float staffMid, float staffBot)
        {
            if (note.IsRest)
                return noteCenterX;
            float ny = NoteY(note, staffTop, staffMid);
            bool stemUp = ny >= staffMid;
            return GetStemAttachPoint(noteCenterX, ny, stemUp).stemX;
        }

        private void LogStaffLayoutDiagnostics(
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
            var order = Enumerable.Range(0, Math.Min(notes.Count, layouts.Length))
                .OrderBy(i => (notes[i].BeatPosition ?? 0.0))
                .ThenBy(i => i)
                .Take(maxEvents)
                .ToList();

            float prevDrawnX = float.NaN;
            float prevStemX = float.NaN;

            for (int k = 0; k < order.Count; k++)
            {
                int i = order[k];
                var n = notes[i];
                float naturalX = i < preScaleX.Length ? preScaleX[i] : layouts[i].X;
                float drawnX = layouts[i].X;
                float stemX = ComputeStemX(n, drawnX, staffTop, staffMid, staffBot);
                float delta = float.IsNaN(prevDrawnX) ? 0f : drawnX - prevDrawnX;

                string label = n.IsRest ? "REST" : n.SpelledName;
                StaffLog(
                    $"[StaffLayout] {staffLabel} i={i} {label} {n.Duration} " +
                    $"naturalX={naturalX:F1} drawnX={drawnX:F1} stemX={stemX:F1} " +
                    $"prevDrawnX={(float.IsNaN(prevDrawnX) ? 0f : prevDrawnX):F1} delta={delta:F1}");

                if (!n.IsRest && !float.IsNaN(prevDrawnX))
                {
                    if (delta < minMelodyDelta)
                    {
                        StaffLog(
                            $"[StaffLayout WARN] {staffLabel} i={i}: drawn X delta {delta:F1} < {minMelodyDelta} " +
                            $"(prev={prevDrawnX:F1} cur={drawnX:F1})");
                    }

                    if (Math.Abs(stemX - prevStemX) < 0.5f)
                    {
                        StaffLog(
                            $"[StaffLayout WARN] {staffLabel} i={i}: stem X {stemX:F1} same as previous {prevStemX:F1}");
                    }

                    if (Math.Abs(drawnX - stemX) < 1f)
                    {
                        StaffLog(
                            $"[StaffLayout WARN] {staffLabel} i={i}: notehead X {drawnX:F1} equals stem X {stemX:F1}");
                    }
                }

                if (!n.IsRest)
                {
                    prevDrawnX = drawnX;
                    prevStemX = stemX;
                }
            }
        }

        private void DrawTunerEmptyStaff(ICanvas canvas, RectF dirtyRect, Color ink)
        {
            if (dirtyRect.Width < 32f || dirtyRect.Height < 32f)
                return;

            ResolveDrawHorizontalBounds(dirtyRect, out float safeLeft, out float safeRight, out float layoutRightLimit);

            ComputeLayout(dirtyRect.Height);
            _headerMetrics = ComputeHeaderMetrics(safeLeft);

            float upperTop = _layout.UpperTop;
            float upperMid = _layout.UpperMid;
            float upperBot = _layout.UpperBot;

            // Vertically center the five-line staff block in the panel.
            float staffBlockMid = (upperTop + upperBot) * 0.5f;
            float dy = dirtyRect.Height * 0.5f - staffBlockMid;
            upperTop += dy;
            upperMid += dy;
            upperBot += dy;

            if (!OmitStaffHeader)
                DrawStaffHeaderChrome(canvas, ink, upperTop, upperMid, upperBot, drawKeyAndTimeSig: false);

            const float safeEdgePad = 4f;
            float lineStart = OmitStaffHeader ? safeLeft + OmitHeaderPanelMargin : safeLeft;
            float lineEnd = OmitStaffHeader
                ? safeRight - OmitHeaderPanelMargin
                : Math.Max(
                    safeLeft + _headerMetrics.LeftMargin + 32f,
                    layoutRightLimit - safeEdgePad);
            lineEnd = Math.Min(lineEnd, safeRight - (OmitStaffHeader ? OmitHeaderPanelMargin : safeEdgePad));
            lineEnd = Math.Max(lineEnd, lineStart + 8f);

            canvas.StrokeColor = ink;
            canvas.StrokeSize = StaffLineStrokeSize;
            for (int i = 0; i < 5; i++)
            {
                float y = upperTop + i * _layout.Sls;
                canvas.DrawLine(lineStart, y, lineEnd, y);
            }
            canvas.StrokeSize = 1f;
        }

        private void DrawStaffLinesAndBars(
            ICanvas canvas, Color ink,
            float staffTop, float staffMid, float staffBot,
            NoteLayout[] noteLayouts, BarLayout[] barLayouts,
            float safeLeft, float safeRight, float layoutRightLimit,
            float staffLeftMargin)
        {
            const float safeEdgePad = 4f;
            float contentEndX = safeLeft + staffLeftMargin;
            for (int i = 0; i < noteLayouts.Length; i++)
            {
                float noteRight = NoteInkRightForLayout(noteLayouts[i]);
                if (noteRight > contentEndX)
                    contentEndX = noteRight;
            }

            BarLayout? finalDouble = null;
            for (int i = 0; i < barLayouts.Length; i++)
            {
                // Sight Training: ignore internal bars for extent and drawing.
                if (SingleStaffLayout && !barLayouts[i].IsDouble)
                    continue;

                float barRight = BarLineRightEdge(barLayouts[i]);
                if (barRight > contentEndX)
                    contentEndX = barRight;
                if (barLayouts[i].IsDouble)
                    finalDouble = barLayouts[i];
            }

            float staffLineEndX;
            if (OmitStaffHeader)
            {
                // Inset staff lines so they match the note clearance margin.
                staffLineEndX = safeRight - OmitHeaderPanelMargin;
            }
            else if (SingleStaffLayout)
            {
                // Terminate staff lines at the double-bar right edge — no overhang.
                staffLineEndX = finalDouble.HasValue
                    ? BarLineRightEdge(finalDouble.Value)
                    : contentEndX;
                staffLineEndX = Math.Min(staffLineEndX, safeRight - safeEdgePad);
            }
            else
            {
                // Stop staff lines at the last bar so they do not continue past the music.
                staffLineEndX = barLayouts.Length > 0
                    ? BarLineRightEdge(barLayouts[^1])
                    : contentEndX;
                staffLineEndX = Math.Min(staffLineEndX, safeRight - safeEdgePad);
            }
            float staffLineStartX = OmitStaffHeader
                ? safeLeft + OmitHeaderPanelMargin
                : safeLeft;
            staffLineEndX = Math.Max(staffLineEndX, staffLineStartX + (OmitStaffHeader ? 8f : staffLeftMargin));

            canvas.StrokeColor = ink;
            canvas.StrokeSize = StaffLineStrokeSize;
            for (int i = 0; i < 5; i++)
            {
                float y = staffTop + i * _layout.Sls;
                canvas.DrawLine(staffLineStartX, y, staffLineEndX, y);
            }

            canvas.StrokeColor = ink;
            foreach (var bar in barLayouts)
            {
                if (!float.IsFinite(bar.X))
                    continue;
                if (SingleStaffLayout && !bar.IsDouble)
                    continue;

                if (bar.IsDouble)
                {
                    // Same double-bar appearance as Music final staff end.
                    canvas.StrokeSize = 2f;
                    canvas.DrawLine(bar.X, staffTop - 2f, bar.X, staffBot + 2f);
                    canvas.StrokeSize = 4f;
                    canvas.DrawLine(bar.X + DoubleBarExtraWidth, staffTop - 2f, bar.X + DoubleBarExtraWidth, staffBot + 2f);
                }
                else
                {
                    canvas.StrokeSize = 2f;
                    canvas.DrawLine(bar.X, staffTop - 2f, bar.X, staffBot + 2f);
                }
            }
            canvas.StrokeSize = 1f;
        }

        private void DrawStaffHeaderChrome(
            ICanvas canvas, Color ink,
            float staffTop, float staffMid, float staffBot,
            bool drawKeyAndTimeSig,
            bool drawBpmMarking = true,
            bool drawTimeSignature = true,
            bool captureTimeSignatureHitTarget = true)
        {
            canvas.SaveState();
            canvas.FontColor = ink;
            canvas.FontSize = _layout.Sls * 5f;
            float clefH = staffBot - staffTop + _layout.Sls * 3.2f;
            canvas.DrawString("𝄞", _headerMetrics.ClefX, staffTop, _headerMetrics.ClefWidth, clefH,
                HorizontalAlignment.Left, VerticalAlignment.Top);
            canvas.RestoreState();

            if (drawBpmMarking && drawKeyAndTimeSig && drawTimeSignature)
                DrawMusicBpmMarking(canvas, ink, staffTop);

            if (drawKeyAndTimeSig)
            {
                float keySigEndX = DrawKeySignature(canvas, staffTop, staffMid, ink);
                if (drawTimeSignature && _session.Tune != "Tuner")
                    DrawTimeSignature(canvas, staffTop, staffMid, ink, keySigEndX + KeySigTimeSigGap, captureTimeSignatureHitTarget);
                else if (captureTimeSignatureHitTarget)
                    ClearTimeSignatureBounds();
            }
            else if (captureTimeSignatureHitTarget)
            {
                ClearTimeSignatureBounds();
            }
        }

        /// <summary>
        /// Width reserved/drawn for the time-signature numerals.
        /// Two-digit meters (e.g. 12/8) need more than a single-digit slot; a too-narrow
        /// DrawString box wraps and stacks digits on the same center X.
        /// </summary>
        private float TimeSignatureBoxWidth(string? timeSigDisplay = null)
        {
            timeSigDisplay ??= _session.GetDisplayTimeSignature();
            var parts = (timeSigDisplay ?? "4/4").Split('/');
            int maxDigits = 1;
            foreach (var part in parts)
            {
                int digits = 0;
                foreach (char ch in part.Trim())
                {
                    if (char.IsDigit(ch))
                        digits++;
                }
                if (digits > maxDigits)
                    maxDigits = digits;
            }

            float tsFontSize = Math.Max(10f, _layout.Sls * 1.83f);
            float singleDigit = Math.Max(24f, tsFontSize * 1.1f);
            return maxDigits <= 1
                ? singleDigit
                : Math.Max(singleDigit * maxDigits * 0.85f, tsFontSize * maxDigits * 0.72f);
        }

        /// <summary>Quarter-note = MusicBpm marking above the upper staff; "=" centered over the time signature.</summary>
        private void DrawMusicBpmMarking(ICanvas canvas, Color ink, float staffTop)
        {
            if (_session.Tune == "Tuner")
            {
                if (_lastMusicBpmMarkingBounds != null)
                {
                    _lastMusicBpmMarkingBounds = null;
                    MusicBpmMarkingBoundsChanged?.Invoke(this, EventArgs.Empty);
                }
                return;
            }

            int bpm = Math.Clamp(_session.Tempo, NoteSessionService.MinTempo, NoteSessionService.MaxTempo);
            float fontSize = Math.Max(10f, _layout.Sls * 1.6f);
            float timeSigW = TimeSignatureBoxWidth();
            float timeSigCenterX = _headerMetrics.TimeSigX + timeSigW * 0.5f;

            string bpmText = bpm.ToString();
            float equalsWidth = fontSize * 0.8f;
            float bpmWidth = Math.Max(fontSize * 2.2f, fontSize * bpmText.Length * 0.7f);
            float textHeight = fontSize * 1.35f;
            float textY = staffTop - textHeight - 2f;

            // Draw the "=" as its own centered glyph so it sits directly above the time signature.
            float equalsX = timeSigCenterX - equalsWidth * 0.5f;
            float bpmX = timeSigCenterX + equalsWidth * 0.5f + fontSize * 0.18f;

            float headR = NoteHeadDrawR(filled: true);
            float stemH = Math.Min(_layout.StemLen, fontSize * 1.15f);
            float gapBeforeEquals = fontSize * 0.35f;
            float headCx = equalsX - gapBeforeEquals - headR;
            float headCy = textY + textHeight * 0.5f;

            canvas.SaveState();
            canvas.FillColor = ink;
            canvas.StrokeColor = ink;
            canvas.StrokeSize = 2f * _layout.GlyphScale;

            canvas.FillEllipse(
                headCx - headR,
                headCy - headR * NoteHeadHeightFactor * 0.5f,
                headR * 2f,
                headR * NoteHeadHeightFactor);
            var (stemX, stemY) = GetStemAttachPoint(headCx, headCy, stemUp: true);
            canvas.DrawLine(stemX, stemY, stemX, stemY - stemH);

            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            canvas.FontSize = fontSize;
            canvas.FontColor = ink;
            canvas.DrawString("=", equalsX, textY, equalsWidth, textHeight,
                HorizontalAlignment.Center, VerticalAlignment.Center);
            canvas.DrawString(bpmText, bpmX, textY, bpmWidth, textHeight,
                HorizontalAlignment.Left, VerticalAlignment.Center);
            canvas.RestoreState();

            // Finger-friendly hit target for opening the Music-page tempo control.
            float left = headCx - headR - fontSize * 0.4f;
            float right = bpmX + bpmWidth + fontSize * 0.5f;
            float top = Math.Min(textY, headCy - headR * NoteHeadHeightFactor * 0.5f) - fontSize * 0.35f;
            float bottom = textY + textHeight + fontSize * 0.45f;
            _lastMusicBpmMarkingBounds = new RectF(left, top, Math.Max(8f, right - left), Math.Max(8f, bottom - top));
            MusicBpmMarkingBoundsChanged?.Invoke(this, EventArgs.Empty);
        }

        private RectF? _lastMusicBpmMarkingBounds;

        /// <summary>Last-drawn ♩ = BPM marking bounds in GraphicsView coordinates, if any.</summary>
        public RectF? LastMusicBpmMarkingBounds => _lastMusicBpmMarkingBounds;

        /// <summary>Raised after the BPM marking is (re)drawn so the Music page can place its hit target.</summary>
        public event EventHandler? MusicBpmMarkingBoundsChanged;

        /// <summary>
        /// True when <paramref name="x"/>/<paramref name="y"/> (GraphicsView coords) hit the
        /// last-drawn ♩ = BPM marking. Used to open the Music-page tempo control.
        /// </summary>
        public bool HitTestMusicBpmMarking(float x, float y)
        {
            if (_lastMusicBpmMarkingBounds is not RectF r)
                return false;

            // Generous padding — the glyph is small and finger taps are imprecise.
            const float pad = 28f;
            return x >= r.X - pad && x <= r.X + r.Width + pad
                && y >= r.Y - pad && y <= r.Y + r.Height + pad;
        }

        private void DrawStaffDynamic(
            ICanvas canvas, RectF dirtyRect, Color ink,
            float staffTop, float staffMid, float staffBot,
            List<GeneratedNote> notes, StaffNoteState[] states,
            NoteLayout[] noteLayouts, BarLayout[] barLayouts,
            IReadOnlyList<double> barBeats, double beatOrigin,
            float alpha,
            bool isActive, int currentIdx,
            float safeLeft, float safeRight, float layoutRightLimit,
            float staffLeftMargin,
            string staffLabel)
        {
            if (ChromaticMidi61Diagnostics.IsEnabled)
                ChromaticMidi61Diagnostics.ResetDrawPass(staffLabel);

            if (notes.Count == 0 || noteLayouts.Length == 0)
            {
                if (ChromaticMidi61Diagnostics.IsEnabled)
                {
                    ChromaticMidi61Diagnostics.RecordSkip(ChromaticMidi61Diagnostics.MidiCs4,
                        $"DrawStaffDynamic ({staffLabel}): empty notes or layouts");
                    ChromaticMidi61Diagnostics.FinalizeDrawPass();
                }
                return;
            }
            if (notes.Count != noteLayouts.Length)
            {
                StaffLog($"[Staff] Skipping staff draw: {notes.Count} notes vs {noteLayouts.Length} layouts");
                if (ChromaticMidi61Diagnostics.IsEnabled)
                {
                    ChromaticMidi61Diagnostics.RecordSkip(ChromaticMidi61Diagnostics.MidiCs4,
                        $"DrawStaffDynamic ({staffLabel}): note/layout count mismatch");
                    ChromaticMidi61Diagnostics.FinalizeDrawPass();
                }
                return;
            }

            if (_session.ShowConductorCues && _session.Tune != "Tuner" && !SingleStaffLayout
                && ConductorBeatHelper.TryParseDisplayTimeSignature(_session.GetDisplayTimeSignature(), out var conductorTs))
            {
                double? highlightedBeat = GetHighlightedConductedBeatRel(
                    notes, currentIdx, beatOrigin, barBeats, conductorTs, isActive);
                DrawConductorBeatCues(canvas, staffTop, staffMid, notes, noteLayouts, barLayouts, barBeats, beatOrigin,
                    staffLeftMargin, conductorTs, highlightedBeat);
            }

            barBeats ??= Array.Empty<double>();
            // Exclude only the drawn clef/key/time region. Using staffLeftMargin (first-note
            // center) made the paint-exclusion zone cover early note accidentals — e.g. the
            // D♮ of an E7 arpeggio in a tight first bar after four sharps.
            float headerRightAbs = OmitStaffHeader
                ? safeLeft
                : safeLeft + _headerMetrics.FullHeaderRightRel;
            var beamGroups = ComputeBeamGroups(notes, noteLayouts, staffTop, staffMid, barBeats, beatOrigin);
            var beamStemEnds = ComputeBeamStemEnds(notes, noteLayouts, beamGroups, staffTop, staffMid);

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
                {
                    if (ChromaticMidi61Diagnostics.IsEnabled && note.MidiNumber == ChromaticMidi61Diagnostics.MidiCs4)
                        ChromaticMidi61Diagnostics.RecordSkip(note.MidiNumber,
                            $"DrawStaffDynamic ({staffLabel}): non-finite layout.X at index {i}");
                    continue;
                }

                var state = (states.Length > i) ? states[i] : StaffNoteState.Pending;
                double beat = (note.BeatPosition ?? 0.0) - beatOrigin;

                if (i > 0)
                {
                    int prevMeasure = notes[i - 1].MeasureIndex ?? -1;
                    int curMeasure = note.MeasureIndex ?? prevMeasure;
                    if (curMeasure != prevMeasure)
                    {
                        accHistory.Clear();
                        barCancelledAccidentals.Clear();
                    }
                }

                ResetAccidentalStateIfCrossedBar(beat, prevBeat, barBeats, beatOrigin,
                    accHistory, barCancelledAccidentals);

                // Key-sig pitch-class cancelled only when this note explicitly contradicts the sig
                // (e.g. B♮ in F major). None does not cancel — implied key-sig pitch needs no glyph.
                if (!note.IsRest)
                {
                    var inferred = InferAccidentalFromSpelling(note);
                    if (inferred != Accidental.None
                        && !IsAccidentalInKeySig(inferred, note.Letter) && IsNoteInKeySig(note))
                    {
                        barCancelledAccidentals.Add((note.Letter, note.Octave));
                    }
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

                    float? stemEndOverride = isBeamed && beamStemEnds.TryGetValue(i, out float beamedEndY)
                        ? beamedEndY
                        : null;

                    string accidentalType = "none";
                    bool drawAccidental = false;
                    if (TryResolveBodyAccidental(note, accHistory, barCancelledAccidentals, out var effAcc, out bool drawAcc))
                    {
                        drawAccidental = drawAcc && effAcc != Accidental.None;
                        accidentalType = drawAccidental ? effAcc.ToString() : "none";
                    }

                    EstimateNoteHeadBounds(note.Duration, layout.X, ny, out float headLeft, out float headTop, out float headW, out float headH);

                    if (ChromaticMidi61Diagnostics.IsEnabled
                        && (note.MidiNumber == ChromaticMidi61Diagnostics.MidiC4
                            || note.MidiNumber == ChromaticMidi61Diagnostics.MidiCs4
                            || note.MidiNumber == ChromaticMidi61Diagnostics.MidiD4))
                    {
                        ChromaticMidi61Diagnostics.RecordDrawLoop(
                            note, i, layout.X, ny, dirtyRect, safeLeft, layoutRightLimit,
                            headLeft, headTop, headW, headH, drawAccidental, accidentalType);
                    }

                    DrawNote(canvas, note.Duration, layout.X, ny, staffTop, staffBot, ink, state, fadeAlpha,
                             forceStemUp, isBeamed, stemEndOverride,
                             out float stemTipX, out float stemTipY);
                    if (ChromaticMidi61Diagnostics.IsEnabled
                        && (note.MidiNumber == ChromaticMidi61Diagnostics.MidiC4
                            || note.MidiNumber == ChromaticMidi61Diagnostics.MidiCs4
                            || note.MidiNumber == ChromaticMidi61Diagnostics.MidiD4))
                    {
                        ChromaticMidi61Diagnostics.RecordDrawNoteHead(note.MidiNumber);
                    }

                    if (isBeamed)
                        beamStemTips[i] = (stemTipX, stemTipY, ResolveDrawnNoteColor(state, ink, fadeAlpha), note.Duration);

                    DrawLedgerLines(canvas, note, layout.X, staffTop, staffBot, ink, fadeAlpha);
                    DrawAccidental(
                        canvas, note, notes, noteLayouts, i, barLayouts, barBeats, beatOrigin,
                        ny, ink, accHistory, barCancelledAccidentals, fadeAlpha, headerRightAbs,
                        out bool accidentalDrawReached);
                    if (ChromaticMidi61Diagnostics.IsEnabled
                        && (note.MidiNumber == ChromaticMidi61Diagnostics.MidiC4
                            || note.MidiNumber == ChromaticMidi61Diagnostics.MidiCs4
                            || note.MidiNumber == ChromaticMidi61Diagnostics.MidiD4))
                    {
                        ChromaticMidi61Diagnostics.RecordDrawAccidental(note.MidiNumber, accidentalDrawReached, accidentalType);
                    }

                    var nameDisplay = _session.NoteNameDisplay;
                    bool showName = _session.Tune != "Tuner"
                        && (nameDisplay == "All notes"
                            || (nameDisplay == "Current only" && state == StaffNoteState.Current));
                    if (showName)
                        DrawNoteName(
                            canvas, note, layout.X, ny, staffTop, staffBot, ink, fadeAlpha,
                            dirtyRect.Y, dirtyRect.Y + dirtyRect.Height);
                }

                prevBeat = beat;
            }

            // Draw beams using pre-computed stem positions
            DrawBeams(canvas, beamGroups, beamStemTips, barLayouts);
            DrawTies(canvas, notes, noteLayouts, staffTop, staffMid, ink, fadeAlpha);

            if (ChromaticMidi61Diagnostics.IsEnabled)
                ChromaticMidi61Diagnostics.FinalizeDrawPass();
        }

        /// <summary>
        /// Draws a simple concave tie arc between consecutive noteheads that share a
        /// <see cref="GeneratedNote.TieGroupId"/>.
        /// </summary>
        private void DrawTies(
            ICanvas canvas,
            List<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            float staffTop,
            float staffMid,
            Color ink,
            byte fadeAlpha)
        {
            var byGroup = new Dictionary<int, List<int>>();
            for (int i = 0; i < notes.Count && i < noteLayouts.Length; i++)
            {
                if (notes[i].IsRest || notes[i].TieGroupId is not int gid)
                    continue;
                if (!byGroup.TryGetValue(gid, out var list))
                    byGroup[gid] = list = new List<int>();
                list.Add(i);
            }

            if (byGroup.Count == 0)
                return;

            canvas.SaveState();
            try
            {
                canvas.StrokeColor = ApplyAlpha(ink, fadeAlpha);
                canvas.StrokeSize = Math.Max(1.25f, _layout.Sls * 0.12f);
                canvas.StrokeLineCap = LineCap.Round;

                foreach (var indices in byGroup.Values)
                {
                    indices.Sort((a, b) => noteLayouts[a].X.CompareTo(noteLayouts[b].X));
                    for (int k = 0; k < indices.Count - 1; k++)
                    {
                        int i0 = indices[k];
                        int i1 = indices[k + 1];
                        float x0 = noteLayouts[i0].X;
                        float x1 = noteLayouts[i1].X;
                        if (x1 - x0 < 2f)
                            continue;

                        float y0 = NoteY(notes[i0], staffTop, staffMid);
                        float y1 = NoteY(notes[i1], staffTop, staffMid);
                        float y = Math.Min(y0, y1);
                        float headR = _layout.NoteHeadR;
                        float left = x0 + headR * 0.55f;
                        float right = x1 - headR * 0.55f;
                        float midX = (left + right) * 0.5f;
                        float bow = Math.Clamp((right - left) * 0.22f, _layout.Sls * 0.35f, _layout.Sls * 1.1f);
                        // Tie bows under the noteheads when stems are up (notes below midline).
                        bool bowDown = y0 >= staffMid;
                        float tipY = bowDown ? y + headR * 0.9f + bow : y - headR * 0.9f - bow;
                        float startY = bowDown ? y0 + headR * 0.55f : y0 - headR * 0.55f;
                        float endY = bowDown ? y1 + headR * 0.55f : y1 - headR * 0.55f;

                        var path = new PathF();
                        path.MoveTo(left, startY);
                        path.CurveTo(midX, tipY, midX, tipY, right, endY);
                        canvas.DrawPath(path);
                    }
                }
            }
            finally
            {
                canvas.RestoreState();
            }
        }

        private void EstimateNoteHeadBounds(
            NoteDuration duration, float x, float y,
            out float headLeft, out float headTop, out float headW, out float headH)
        {
            bool filled = duration != NoteDuration.Whole && duration != NoteDuration.Half;
            if (OmitStaffHeader)
            {
                headW = OmitHeaderStaffSpace * OmitHeaderNoteHeadWidthSpaces;
                headH = OmitHeaderStaffSpace * OmitHeaderNoteHeadHeightSpaces;
                headLeft = x - headW * 0.5f;
                headTop = y - headH * 0.5f;
            }
            else
            {
                float drawR = NoteHeadDrawR(filled);
                headW = drawR * 2f;
                headH = drawR * NoteHeadHeightFactor;
                headLeft = x - drawR;
                headTop = y - drawR * NoteHeadHeightFactor * 0.5f;
            }
        }

        private static double? GetHighlightedConductedBeatRel(
            List<GeneratedNote> notes,
            int currentIdx,
            double beatOrigin,
            IReadOnlyList<double> barBeats,
            TimeSignature timeSignature,
            bool isActive)
        {
            if (!isActive || currentIdx < 0 || currentIdx >= notes.Count)
                return null;

            double relBeat = (notes[currentIdx].BeatPosition ?? 0.0) - beatOrigin;
            var sortedMeasureStarts = barBeats.Select(b => b - beatOrigin).OrderBy(b => b).ToList();
            return ConductorBeatHelper.GetConductedBeatStartForRelativeBeat(relBeat, timeSignature, sortedMeasureStarts);
        }

        //private void DrawConductorBeatCues(
        //    ICanvas canvas,
        //    float staffTop,
        //    BarLayout[] barLayouts,
        //    IReadOnlyList<double> barBeats,
        //    double beatOrigin,
        //    float staffLeftMargin,
        //    TimeSignature timeSignature,
        //    double? highlightedConductedBeatRel)
        private void DrawConductorBeatCues(
            ICanvas                     canvas,
            float                       staffTop,
            float                       staffMid,
            IReadOnlyList<GeneratedNote> notes,
            NoteLayout[]                noteLayouts,
            BarLayout[]                 barLayouts,
            IReadOnlyList<double>       barBeats,
            double                      beatOrigin,
            float                       staffLeftMargin,
            TimeSignature               timeSignature,
            double?                     highlightedConductedBeatRel)
        { 
            // Visual simplification: only the moving major cue for the current conducted beat.
            // Minor (non-current) beat markers are not drawn.
            if (barLayouts.Length == 0 || !highlightedConductedBeatRel.HasValue)
                return;

            var conductedOffsets = ConductorBeatHelper.GetConductedBeatOffsetsInMeasure(timeSignature);
            double measureBeats = timeSignature.TotalBeats;
            var sortedMeasureStarts = barBeats.Select(b => b - beatOrigin).OrderBy(b => b).ToList();
            int measureCount = sortedMeasureStarts.Count + 1;

            for (int m = 0; m < measureCount; m++)
            {
                float measureLeft = m == 0 ? staffLeftMargin : barLayouts[m - 1].X;
                float measureRight = m < barLayouts.Length ? barLayouts[m].X : barLayouts[^1].X;
                double segmentStartBeat = m == 0 ? 0.0 : sortedMeasureStarts[m - 1];

                for (int c = 0; c < conductedOffsets.Count; c++)
                {
                    double offset = conductedOffsets[c];

                    double conductedBeatRel = segmentStartBeat + offset;

                    if (Math.Abs(highlightedConductedBeatRel.Value - conductedBeatRel) >= 0.05)
                        continue;

                    double nextOffset = c + 1 < conductedOffsets.Count
                        ? conductedOffsets[c + 1]
                        : measureBeats;

                    double conductedBeatEndRel = segmentStartBeat + nextOffset;

                    float x = TryGetFirstLayoutXInBeatWindow(
                        notes,
                        noteLayouts,
                        beatOrigin,
                        conductedBeatRel,
                        conductedBeatEndRel,
                        out float noteX)
                            ? noteX
                            : ConductorBeatHelper.BeatOffsetToXInMeasure(
                                measureLeft, measureRight, offset, measureBeats, BarLeftPadding);

                    float? highestHeadTop = TryGetHighestNoteHeadTopInBeatWindow(
                        notes, noteLayouts, beatOrigin, conductedBeatRel, conductedBeatEndRel,
                        staffTop, staffMid);

                    DrawConductorArrow(canvas, x, staffTop, highestHeadTop);
                    return;
                }
            }
        }

        //private static bool TryGetLayoutXAtBeat(  //  2026.07.10 1001  method out
        //    IReadOnlyList<GeneratedNote> notes,
        //    NoteLayout[] noteLayouts,
        //    double beatOrigin,
        //    double targetBeatRel,
        //    out float x)
        //{
        //    const double tolerance = 0.05;

        //    x = 0f;

        //    int count = Math.Min(notes.Count, noteLayouts.Length);

        //    for (int i = 0; i < count; i++)
        //    {
        //        double noteBeatRel = (notes[i].BeatPosition ?? 0.0) - beatOrigin;

        //        if (Math.Abs(noteBeatRel - targetBeatRel) <= tolerance)
        //        {
        //            x = noteLayouts[i].X;
        //            return true;
        //        }
        //    }

        //    return false;
        //}
        private static bool TryGetFirstLayoutXInBeatWindow(
    IReadOnlyList<GeneratedNote> notes,
    NoteLayout[] noteLayouts,
    double beatOrigin,
    double beatStartRel,
    double beatEndRel,
    out float x)
        {
            const double tolerance = 0.0001;

            x = 0f;

            int count = Math.Min(notes.Count, noteLayouts.Length);

            int bestIndex = -1;
            double bestBeat = double.PositiveInfinity;

            for (int i = 0; i < count; i++)
            {
                double noteBeatRel = (notes[i].BeatPosition ?? 0.0) - beatOrigin;

                if (noteBeatRel >= beatStartRel - tolerance
                    && noteBeatRel < beatEndRel - tolerance)
                {
                    if (noteBeatRel < bestBeat)
                    {
                        bestBeat = noteBeatRel;
                        bestIndex = i;
                    }
                }
            }

            if (bestIndex < 0)
                return false;

            x = noteLayouts[bestIndex].X;
            return true;
        }

        /// <summary>
        /// Top of the highest pitched note head in the conducted-beat window, or null if none.
        /// Used to raise conductor cues above ledger notes that sit above the default cue tip.
        /// </summary>
        private float? TryGetHighestNoteHeadTopInBeatWindow(
            IReadOnlyList<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            double beatOrigin,
            double beatStartRel,
            double beatEndRel,
            float staffTop,
            float staffMid)
        {
            const double tolerance = 0.0001;
            int count = Math.Min(notes.Count, noteLayouts.Length);
            float? highestTop = null;

            for (int i = 0; i < count; i++)
            {
                var note = notes[i];
                if (note.IsRest)
                    continue;

                double noteBeatRel = (note.BeatPosition ?? 0.0) - beatOrigin;
                if (noteBeatRel < beatStartRel - tolerance || noteBeatRel >= beatEndRel - tolerance)
                    continue;

                float headTop = NoteHeadTopY(note, staffTop, staffMid);
                if (!highestTop.HasValue || headTop < highestTop.Value)
                    highestTop = headTop;
            }

            return highestTop;
        }

        private float NoteHeadTopY(GeneratedNote note, float staffTop, float staffMid)
        {
            float ny = NoteY(note, staffTop, staffMid);
            float drawR = NoteHeadDrawR(filled: true);
            return ny - drawR * NoteHeadHeightFactor * 0.5f;
        }

        private static void DrawConductorArrow(
            ICanvas canvas,
            float x,
            float staffTop,
            float? highestNoteHeadTop = null)
        {
            // Major (current-beat) cue only — minor beat markers were removed.
            const float wing = 8.0f;
            const float height = 8f;
            float tipY = staffTop - 1f;

            // If the note head sits above the normal cue tip, park the tip just above the head.
            const float clearance = 2f;
            if (highestNoteHeadTop.HasValue && highestNoteHeadTop.Value < tipY)
                tipY = highestNoteHeadTop.Value - clearance;

            float baseY = tipY - height;

            canvas.StrokeColor = Colors.Red;
            canvas.StrokeSize = 2.5f;
            canvas.DrawLine(x - wing, baseY, x, tipY);
            canvas.DrawLine(x + wing, baseY, x, tipY);
            canvas.DrawLine(x - wing, baseY, x + wing, baseY);
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
        /// In 4/4 time, beam groups are formed within one-beat boundaries:
        /// - Beat 0.0-1.0, 1.0-2.0, etc.
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
            var beatOrder = Enumerable.Range(0, notes.Count)
                .OrderBy(idx => (notes[idx].BeatPosition ?? 0.0) - beatOrigin)
                .ThenBy(idx => idx)
                .ToList();
            int groupId = 0;
            int oi = 0;

            while (oi < beatOrder.Count)
            {
                int i = beatOrder[oi];
                var n = notes[i];
                double pos = (n.BeatPosition ?? 0.0) - beatOrigin;

                // Only beam eighth or sixteenth non-rest notes
                bool isBeamable = IsBeamableNote(n);

                if (!isBeamable)
                {
                    oi++;
                    continue;
                }

                int currentMeasure = GetMeasureIndexForBeat(pos, sortedBarBeatsRel);
                double measureEnd = currentMeasure < sortedBarBeatsRel.Count
                    ? sortedBarBeatsRel[currentMeasure]
                    : double.PositiveInfinity;

                // Beam within a beat so mixed eighth/sixteenth patterns render as one group.
                double groupStart = Math.Floor(pos + 1e-9);
                double groupEnd = groupStart + 1.0;
                groupEnd = Math.Min(groupEnd, measureEnd - 1e-6);
                if (groupEnd <= groupStart + 1e-6)
                {
                    oi++;
                    continue;
                }

                // Collect consecutive beamable notes within the same beat/half-beat group and measure
                var groupIndices = new List<int> { i };
                int j = oi + 1;

                while (j < beatOrder.Count)
                {
                    int noteIdx = beatOrder[j];
                    var nj = notes[noteIdx];
                    double pj = (nj.BeatPosition ?? groupEnd) - beatOrigin;

                    // Stop if we hit a rest or unbeamable duration
                    if (!IsBeamableNote(nj))
                        break;

                    // Stop if we cross into a different measure or reach the bar beat
                    if (GetMeasureIndexForBeat(pj, sortedBarBeatsRel) != currentMeasure
                        || pj >= measureEnd - 1e-6)
                        break;

                    // Stop if this note starts outside our half-open beat window [groupStart, groupEnd)
                    if (!IsInBeamBeatWindow(pj, groupStart, groupEnd))
                        break;

                    groupIndices.Add(noteIdx);
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

                oi = j > oi + 1 ? j : oi + 1;
            }

            return beamGroup;
        }

        /// <summary>Minimum stem length (in staff spaces) for at least one note in each beam group.</summary>
        private const float MinBeamedStemStaffSpaces = 3f;

        // Flats Bb Eb Ab Db Gb Cb Fb → B4 E5 A4 D5 G4 C5 F4 (Db major 2nd flat is E5 top space, not E4).
        private static readonly (char Letter, int Octave)[] KeySigFlatPitches =
            { ('B', 4), ('E', 5), ('A', 4), ('D', 5), ('G', 4), ('C', 5), ('F', 4) };

        // Sharps F# C# G# D# A# E# B# → treble staff: F5 C5 G5 D5 A4 E5 B4 (B major: F5 C5 G5 D5 A4).
        private static readonly (char Letter, int Octave)[] KeySigSharpPitches =
            { ('F', 5), ('C', 5), ('G', 5), ('D', 5), ('A', 4), ('E', 5), ('B', 4) };

        /// <summary>
        /// Computes per-note stem tip Y for beamed notes so tips lie on a sloped beam line
        /// and at least one stem in each group reaches <see cref="MinBeamedStemStaffSpaces"/>.
        /// </summary>
        private Dictionary<int, float> ComputeBeamStemEnds(
            List<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            Dictionary<int, (int groupId, bool stemUp)> beamGroups,
            float staffTop,
            float staffMid)
        {
            var byGroup = new Dictionary<int, List<int>>();
            foreach (var kv in beamGroups)
            {
                if (!byGroup.TryGetValue(kv.Value.groupId, out var list))
                    byGroup[kv.Value.groupId] = list = new();
                list.Add(kv.Key);
            }

            float minStemPx = MinBeamedStemStaffSpaces * _layout.Sls;
            float maxTilt = _layout.Sls * 1.5f;
            var result = new Dictionary<int, float>();

            foreach (var gkv in byGroup)
            {
                var indices = gkv.Value;
                if (indices.Count < 2)
                    continue;

                indices.Sort((a, b) => noteLayouts[a].X.CompareTo(noteLayouts[b].X));
                bool stemUp = beamGroups[indices[0]].stemUp;

                var attaches = new List<(int idx, float sx, float sy)>(indices.Count);
                foreach (int i in indices)
                {
                    float ny = NoteY(notes[i], staffTop, staffMid);
                    (float sx, float sy) = GetStemAttachPoint(noteLayouts[i].X, ny, stemUp);
                    attaches.Add((i, sx, sy));
                }

                float x0 = attaches[0].sx;
                float y0 = attaches[0].sy;
                float x1 = attaches[^1].sx;
                float y1 = attaches[^1].sy;

                float y0Tip = stemUp ? y0 - minStemPx : y0 + minStemPx;
                float y1Tip = stemUp ? y1 - minStemPx : y1 + minStemPx;

                if (Math.Abs(y1Tip - y0Tip) > maxTilt)
                    y1Tip = y0Tip + Math.Sign(y1Tip - y0Tip) * maxTilt;

                float BeamY(float x) => Math.Abs(x1 - x0) < 1e-3f
                    ? y0Tip
                    : y0Tip + (y1Tip - y0Tip) * ((x - x0) / (x1 - x0));

                float maxLen = 0f;
                foreach (var (_, sx, sy) in attaches)
                {
                    float len = Math.Abs(sy - BeamY(sx));
                    if (len > maxLen)
                        maxLen = len;
                }

                if (maxLen < minStemPx)
                {
                    float boost = minStemPx - maxLen;
                    y0Tip += stemUp ? -boost : boost;
                    y1Tip += stemUp ? -boost : boost;
                }

                foreach (var (idx, sx, _) in attaches)
                    result[idx] = BeamY(sx);
            }

            return result;
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
            // ~0.38 sls thick; center-to-center spacing ~0.72 sls keeps a clear gap between double beams.
            float beamThick = Math.Max(3f, _layout.Sls * 0.38f);
            float beamGap = Math.Max(2f, _layout.Sls * 0.34f);

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

                foreach (var subTips in SplitBeamTipsAtBarLines(tips, barLayouts))
                {
                    if (subTips.Count < 2) continue;

                    float x0 = subTips[0].x;
                    float y0 = subTips[0].y;
                    float x1 = subTips[^1].x;
                    float y1 = subTips[^1].y;

                    // Cap slope to 1.5 staff spaces for natural melodic contour
                    float maxTilt = _layout.Sls * 1.5f;
                    if (Math.Abs(y1 - y0) > maxTilt)
                        y1 = y0 + Math.Sign(y1 - y0) * maxTilt;

                    float BeamY(float x) => x0 == x1 ? y0 : y0 + (y1 - y0) * ((x - x0) / (x1 - x0));

                    var beamColor = subTips.FirstOrDefault(t => t.color != default).color;
                    if (beamColor == default) beamColor = ApplyAlpha(Colors.Black, 220);

                    var segments = SplitBeamAtBarLines(x0, x1, barLayouts);
                    foreach (var (segLeft, segRight) in segments)
                    {
                        float drawLeft = Math.Max(segLeft, x0);
                        float drawRight = Math.Min(segRight, x1);
                        if (drawRight - drawLeft < 1f)
                            continue;

                        canvas.SaveState();
                        canvas.StrokeColor = beamColor;

                        // Primary beam (eighth notes)
                        canvas.StrokeSize = beamThick;
                        canvas.DrawLine(drawLeft, BeamY(drawLeft), drawRight, BeamY(drawRight));

                        // Secondary beam (sixteenth notes)
                        float secondaryOffset = grpStemUp ? (beamThick + beamGap) : -(beamThick + beamGap);
                        for (int ti = 0; ti < subTips.Count; ti++)
                        {
                            if (subTips[ti].dur != NoteDuration.Sixteenth) continue;

                            int segStart = ti;
                            while (ti + 1 < subTips.Count && subTips[ti + 1].dur == NoteDuration.Sixteenth)
                                ti++;
                            int segEnd = ti;

                            float sx0 = Math.Max(subTips[segStart].x, drawLeft);
                            float sx1 = Math.Min(subTips[segEnd].x, drawRight);
                            if (sx1 - sx0 < 1f)
                                continue;

                            if (segStart == segEnd)
                            {
                                float halfSlot = (x1 - x0) / Math.Max(subTips.Count - 1, 1) * 0.5f;
                                if (segStart == 0)
                                    sx1 = Math.Min(sx0 + halfSlot, drawRight);
                                else
                                    sx0 = Math.Max(sx1 - halfSlot, drawLeft);
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
        }

        /// <summary>Splits a beam group's stem tips when their X span crosses an internal bar line.</summary>
        private static List<List<(int noteIdx, float x, float y, Color color, NoteDuration dur)>>
            SplitBeamTipsAtBarLines(
                List<(int noteIdx, float x, float y, Color color, NoteDuration dur)> tips,
                BarLayout[] barLayouts)
        {
            if (tips.Count < 2)
                return new List<List<(int, float, float, Color, NoteDuration dur)>> { tips };

            var result = new List<List<(int, float, float, Color, NoteDuration dur)>>();
            var current = new List<(int, float, float, Color, NoteDuration dur)> { tips[0] };

            for (int i = 1; i < tips.Count; i++)
            {
                float prevX = tips[i - 1].x;
                float curX = tips[i].x;
                bool split = false;
                for (int b = 0; b < barLayouts.Length; b++)
                {
                    float barX = barLayouts[b].X;
                    if (prevX < barX - 0.5f && curX > barX + 0.5f)
                    {
                        split = true;
                        break;
                    }
                }

                if (split)
                {
                    if (current.Count >= 2)
                        result.Add(current);
                    current = new List<(int, float, float, Color, NoteDuration dur)> { tips[i] };
                }
                else
                {
                    current.Add(tips[i]);
                }
            }

            if (current.Count >= 2)
                result.Add(current);

            return result;
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
                if (barX > beamLeft + 1f && barX < beamRight - 1f)
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
            /// <summary>
            /// Right edge of drawn header content (clef/key/time as applicable), relative to safeLeft.
            /// Used to clear the first note of the staff.
            /// </summary>
            public float FullHeaderRightRel { get; init; }
            /// <summary>Offset from <c>safeLeft</c> to the first note beat-0 anchor (center X).</summary>
            public float LeftMargin { get; init; }
            /// <summary>Clef-only anchor for lower staff (no key/time repeat).</summary>
            public float ClefOnlyLeftMargin { get; init; }
        }

        private StaffHeaderMetrics ComputeHeaderMetrics(float safeLeft)
        {
            const float clefPad = 2f;
            float timeSigW = TimeSignatureBoxWidth();

            // Compact Ear Training panel: no clef/key — leave a small pad before the first note.
            if (OmitStaffHeader)
            {
                float pad = OmitHeaderPanelMargin;
                float compactLeftMargin = pad + _layout.NoteHeadR * 2f;
                return new StaffHeaderMetrics
                {
                    ClefX = safeLeft,
                    ClefWidth = 0f,
                    KeySigStartX = safeLeft,
                    KeySigEndX = safeLeft,
                    TimeSigX = safeLeft,
                    TimeSigRightRel = 0f,
                    FullHeaderRightRel = pad,
                    LeftMargin = compactLeftMargin,
                    ClefOnlyLeftMargin = compactLeftMargin,
                };
            }

            float clefWidth = _layout.Sls * 4.5f;
            float clefOnlyRightRel = clefPad + clefWidth;
            float clefOnlyLeftMargin = clefOnlyRightRel + _layout.NoteHeadR + _layout.NoteHeadR;

            string key = ActiveNotationKey;
            string scale = ActiveKeySignatureScale();
            // Body accidentals must use the same suppress decision (see IsKeySignatureVisiblyDrawn).
            bool suppressKeySig = !IsKeySignatureVisiblyDrawn();
            int accCount = suppressKeySig ? 0 : KeySignatureRules.GetAccidentalCount(key, scale);

            float clefX = safeLeft + clefPad;
            float keySigStartX = safeLeft + clefWidth;
            float keySigEndX = keySigStartX + KeySigDrawnWidth(accCount);
            float timeSigX = keySigEndX + KeySigTimeSigGap;
            float timeSigRightRel = timeSigX - safeLeft + timeSigW;
            // Gap from header (time sig, or key/clef when time is omitted) to first note.
            // Sight Training omits time signature; Tuner omits key+time.
            bool suppressTimeSig = _session.Tune == "Tuner" || SingleStaffLayout;
            float fullHeaderRightRel;
            float leftMargin;
            if (_session.Tune == "Tuner")
            {
                fullHeaderRightRel = clefOnlyRightRel;
                leftMargin = clefOnlyLeftMargin;
            }
            else if (suppressTimeSig)
            {
                fullHeaderRightRel = suppressKeySig
                    ? clefOnlyRightRel
                    : (keySigEndX - safeLeft);
                leftMargin = fullHeaderRightRel + _layout.NoteHeadR + _layout.NoteHeadR;
            }
            else
            {
                fullHeaderRightRel = timeSigRightRel;
                leftMargin = timeSigRightRel + _layout.NoteHeadR + _layout.NoteHeadR;
            }

            return new StaffHeaderMetrics
            {
                ClefX = clefX,
                ClefWidth = clefWidth,
                KeySigStartX = keySigStartX,
                KeySigEndX = keySigEndX,
                TimeSigX = timeSigX,
                TimeSigRightRel = timeSigRightRel,
                FullHeaderRightRel = fullHeaderRightRel,
                LeftMargin = leftMargin,
                ClefOnlyLeftMargin = clefOnlyLeftMargin
            };
        }

        // ── Note geometry ─────────────────────────────────────────────────────────

        private float NoteY(GeneratedNote note, float staffTop, float staffMid)
        {
            var (letter, octave) = ResolveStaffLetterOctave(note);
            int steps = DiatonicStepsFromB4(letter, octave);
            return staffMid + steps * _layout.HS;
        }

        /// <summary>
        /// Letter/octave used for staff geometry. Prefers explicit fields; falls back to
        /// <see cref="GeneratedNote.SpelledName"/> when Letter was never populated.
        /// </summary>
        internal static (char Letter, int Octave) ResolveStaffLetterOctave(GeneratedNote note)
        {
            char letter = note.Letter;
            int octave = note.Octave;
            if ((letter < 'A' || letter > 'G') && !string.IsNullOrWhiteSpace(note.SpelledName))
            {
                var raw = note.SpelledName.Trim();
                letter = char.ToUpperInvariant(raw[0]);
                octave = NoteSessionService.ParseOctaveFromSpelledName(raw);
            }

            return (letter, octave);
        }

        private float KeySigLineY(char letter, int octave, float staffMid)
            => staffMid + DiatonicStepsFromB4(letter, octave) * _layout.HS;

        private float KeySigAccidentalFontSize(bool isFlat)
            => _layout.Sls * 2.4f * KeySigAccidentalScale() * ArpeggioKeySigSizeBoost() * (isFlat ? KeySigFlatSizeBoost : 1f);

        private float BodyAccidentalFontSize(bool isFlat = false, bool isNatural = false)
        {
            if (OmitStaffHeader)
                return OmitHeaderSmuFLFontSize;

            return _layout.Sls * 2.4f * BodyAccidentalGlyphScale()
               * (isFlat ? BodyFlatSizeBoost : 1f)
               * (isNatural ? BodyNaturalSizeBoost : 1f);
        }

        /// <summary>
        /// Draws a Bravura SMuFL accidental with its musical staff-line anchor (the font
        /// y = 0 baseline in Skia, i.e. the note's staff position) placed precisely at
        /// <paramref name="staffY"/>.  The ink's left edge starts at <paramref name="anchorX"/>.
        /// <para>
        /// For right-aligned body accidentals: <c>anchorX = boxRight − (m.Left + m.Width)</c>.<br/>
        /// For slot-centred key-signature accidentals: <c>anchorX = slotCentreX − m.Width / 2</c>.
        /// </para>
        /// Returns false if Bravura is unavailable; caller should use a Unicode fallback.
        /// </summary>
        private static bool TrySmuFLAccidentalAtStaffY(
            ICanvas canvas,
            string bravuraGlyph,
            float anchorX,
            float staffY,
            float fontSize,
            Color ink)
        {
            if (!SmuFLGlyphMetrics.TryMeasure(bravuraGlyph, fontSize, out var m))
                return false;
            // m.Top is negative (ink above baseline in Skia convention).
            // Adding m.Top shifts the draw box so the baseline sits exactly on staffY.
            float yTop = staffY + m.Top;
            return SmuFLRestRaster.TryDrawGlyph(canvas, bravuraGlyph, anchorX, yTop, m.Width, m.Height, fontSize, ink);
        }

        /// <summary>
        /// Draws one Bravura body accidental right-aligned to <paramref name="boxRight"/> and
        /// anchored vertically on <paramref name="staffY"/> (the notehead staff position).
        /// </summary>
        private bool TryDrawBodySmuFLAccidental(
            ICanvas canvas,
            string bravuraGlyph,
            float boxRight,
            float staffY,
            Color ink,
            bool isFlat,
            bool isNatural)
        {
            float fontSize = isNatural
                ? BodyAccidentalFontSize(isFlat: false, isNatural: true)
                : BodyAccidentalFontSize(isFlat);
            if (!SmuFLGlyphMetrics.TryMeasure(bravuraGlyph, fontSize, out var m))
                return false;
            float anchorX = boxRight - (m.Left + m.Width);
            return TrySmuFLAccidentalAtStaffY(canvas, bravuraGlyph, anchorX, staffY, fontSize, ink);
        }

        /// <summary>
        /// Draws one Bravura key-signature accidental centred in the slot and anchored at
        /// <paramref name="yLine"/> (the canonical staff-position Y for that accidental).
        /// </summary>
        private bool DrawKeySigAccidental(
            ICanvas canvas,
            string bravuraGlyph,
            float sigX,
            float symW,
            float yLine,
            float fontSize,
            Color ink,
            bool isFlat)
        {
            if (!SmuFLGlyphMetrics.TryMeasure(bravuraGlyph, fontSize, out var m))
                return false;
            float anchorX = sigX + (symW - m.Width) * 0.5f;
            return TrySmuFLAccidentalAtStaffY(canvas, bravuraGlyph, anchorX, yLine, fontSize, ink);
        }

        private static int DiatonicStepsFromB4(char letter, int octave)
        {
            int noteVal = letter switch
            {
                'C' => 0,
                'D' => 1,
                'E' => 2,
                'F' => 3,
                'G' => 4,
                'A' => 5,
                'B' => 6,
                _ => 0
            };
            int b4Val = 6 + 4 * 7;
            int thisVal = noteVal + octave * 7;
            return b4Val - thisVal;
        }

        /// <summary>
        /// Diatonic staff steps below (+) / above (−) the treble middle line (B4).
        /// Used by <see cref="NoteY"/> and by written-note display regression tests.
        /// </summary>
        internal static int GetDiatonicStepsFromB4(char letter, int octave)
            => DiatonicStepsFromB4(letter, octave);

        // ── Drawing primitives ────────────────────────────────────────────────────

        /// <summary>
        /// Tuner reference staff: keep exactly one pitched note (or none) and hide the lower staff.
        /// Does not run for Music / Sight Training.
        /// </summary>
        private void EnforceTunerSingleNoteDisplay()
        {
            if (LowerNotes.Count > 0)
            {
                LowerNotes = new List<GeneratedNote>();
                LowerBarBeats = new List<double>();
                LowerNoteStates = Array.Empty<StaffNoteState>();
                LowerAlpha = 0f;
            }

            if (UpperNotes.Count <= 1)
            {
                if (UpperNotes.Count == 1
                    && (UpperNoteStates == null || UpperNoteStates.Length != 1))
                {
                    UpperNoteStates = new[] { StaffNoteState.Correct };
                }
                return;
            }

            GeneratedNote? keep = null;
            for (int i = 0; i < UpperNotes.Count; i++)
            {
                if (!UpperNotes[i].IsRest)
                {
                    keep = UpperNotes[i];
                    break;
                }
            }

            UpperNotes = keep != null
                ? new List<GeneratedNote> { keep }
                : new List<GeneratedNote>();
            UpperBarBeats = new List<double>();
            UpperNoteStates = UpperNotes.Count > 0
                ? new[] { StaffNoteState.Correct }
                : Array.Empty<StaffNoteState>();
            UpperHasEndBar = false;
            ActiveNoteIndex = UpperNotes.Count > 0 ? 0 : -1;
            IsUpperActive = true;
        }

        private static Color ApplyAlpha(Color c, byte alpha)
            => Color.FromRgba(c.Red, c.Green, c.Blue, alpha / 255f);

        /// <summary>Tuner notes are always green; other pages keep their existing note colors.</summary>
        private Color ResolveDrawnNoteColor(StaffNoteState state, Color ink, byte fadeAlpha)
        {
            if (IsTunerReferenceStaff)
                return ApplyAlpha(Color.FromArgb("#22AA44"), fadeAlpha);

            // Match the historical DrawNote palette (Current = Gold on Music/Sight).
            return state switch
            {
                StaffNoteState.Current => ApplyAlpha(Colors.Gold, fadeAlpha),
                StaffNoteState.Correct => ApplyAlpha(Color.FromArgb("#22AA44"), fadeAlpha),
                StaffNoteState.Wrong => ApplyAlpha(Color.FromArgb("#CC2222"), fadeAlpha),
                _ => ApplyAlpha(Colors.Black, (byte)(fadeAlpha * 0.85f))
            };
        }

        private static Color GetNoteColor(StaffNoteState state, Color ink, byte fadeAlpha) => state switch
        {
            StaffNoteState.Current => ApplyAlpha(Colors.Yellow, fadeAlpha),  //  2026.06.13 1552  Color.FromArgb("#007BFF"), fadeAlpha),
            StaffNoteState.Correct => ApplyAlpha(Color.FromArgb("#22AA44"), fadeAlpha),
            StaffNoteState.Wrong => ApplyAlpha(Color.FromArgb("#CC2222"), fadeAlpha),
            _ => ApplyAlpha(Colors.Black, (byte)(fadeAlpha * 0.85f))
        };

        private void DrawNote(ICanvas canvas, NoteDuration duration, float x, float y,
                              float staffTop, float staffBot, Color ink,
                              StaffNoteState state, byte fadeAlpha,
                              bool? forceStemUp, bool isBeamed, float? beamedStemEndY,
                              out float stemTipX, out float stemTipY)
        {
            stemTipX = x;
            stemTipY = y;
            canvas.SaveState();
            try
            {
                Color noteColor = ResolveDrawnNoteColor(state, ink, fadeAlpha);

                float stroke = OmitStaffHeader
                    ? OmitHeaderStemStroke
                    : 2f * _layout.GlyphScale;
                canvas.StrokeColor = noteColor;
                canvas.StrokeSize = stroke;

                bool filled = duration != NoteDuration.Whole && duration != NoteDuration.Half;
                float headW;
                float headH;
                float headLeft;
                float headTop;
                if (OmitStaffHeader)
                {
                    headW = OmitHeaderStaffSpace * OmitHeaderNoteHeadWidthSpaces;
                    headH = OmitHeaderStaffSpace * OmitHeaderNoteHeadHeightSpaces;
                    headLeft = x - headW * 0.5f;
                    headTop = y - headH * 0.5f;
                }
                else
                {
                    float drawR = NoteHeadDrawR(filled);
                    headW = drawR * 2f;
                    headH = drawR * NoteHeadHeightFactor;
                    headLeft = x - drawR;
                    headTop = y - drawR * NoteHeadHeightFactor * 0.5f;
                }
                if (filled)
                {
                    canvas.FillColor = noteColor;
                    canvas.FillEllipse(headLeft, headTop, headW, headH);
                }
                else
                {
                    canvas.FillColor = noteColor;
                    canvas.FillEllipse(headLeft, headTop, headW, headH);

                    float inset = Math.Min(stroke, Math.Min(headW, headH) * 0.22f);
                    if (headW > inset * 2f && headH > inset * 2f)
                    {
                        canvas.FillColor = _theme.PanelBackgroundColor;
                        canvas.FillEllipse(
                            headLeft + inset,
                            headTop + inset,
                            headW - inset * 2f,
                            headH - inset * 2f);
                    }
                }

                if (duration != NoteDuration.Whole)
                {
                    // Stem direction: notes ABOVE middle line stem DOWN; notes BELOW middle stem UP
                    float staffMiddle = staffTop + (staffBot - staffTop) * 0.5f;
                    bool stemUp = forceStemUp ?? (y >= staffMiddle);  // note at/below middle → stem up
                    (float stemX, float stemY) = GetStemAttachPoint(x, y, stemUp);
                    float stemEnd = beamedStemEndY ?? (stemUp
                        ? stemY - _layout.StemLen
                        : stemY + _layout.StemLen);
                    canvas.StrokeColor = noteColor;
                    canvas.StrokeSize = stroke;
                    canvas.DrawLine(stemX, stemY, stemX, stemEnd);

                    stemTipX = stemX;
                    stemTipY = stemEnd;

                    // Individual flag — suppressed for beamed notes (beam drawn after all notes)
                    if (!isBeamed)
                    {
                        if (duration == NoteDuration.Eighth)
                            SmuFLFlagDrawer.Draw(canvas, stemUp, stemX, stemEnd, _layout.Sls, _layout.GlyphScale, noteColor);
                        else if (duration == NoteDuration.Sixteenth)
                            SmuFLFlagDrawer.DrawSixteenth(canvas, stemUp, stemX, stemEnd, _layout.Sls, _layout.GlyphScale, noteColor);
                    }
                }
            }
            finally { canvas.RestoreState(); }
        }

        private void DrawRest(ICanvas canvas, NoteDuration duration, float x,
                              float staffTop, float staffMid, float staffBot,
                              Color ink, StaffNoteState state, byte fadeAlpha)
        {
            canvas.SaveState();
            try
            {
                float r = _layout.NoteHeadR;
                if (state == StaffNoteState.Current)
                {
                    canvas.FillColor = ApplyAlpha(Color.FromArgb("#007BFF"), (byte)(fadeAlpha * 0.19f));
                    canvas.StrokeColor = ApplyAlpha(Color.FromArgb("#007BFF"), fadeAlpha);
                    canvas.StrokeSize = 1.5f;
                    canvas.FillRoundedRectangle(x - r * 2f, staffMid - r * 3f, r * 4f, r * 6f, 4f);
                }

                Color rc = state switch
                {
                    StaffNoteState.Correct => ApplyAlpha(Colors.Green, fadeAlpha),
                    StaffNoteState.Wrong => ApplyAlpha(Colors.DarkRed, fadeAlpha),
                    StaffNoteState.Current => ApplyAlpha(Color.FromArgb("#007BFF"), fadeAlpha),
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
                float staffMid = staffTop + _layout.Sls * 2f;
                float ny = NoteY(note, staffTop, staffMid);
                float ledgerHW = NoteLedgerHalfWidth();

                canvas.StrokeColor = ApplyAlpha(ink, fadeAlpha);
                canvas.StrokeSize = OmitStaffHeader
                    ? StaffLineStrokeSize
                    : 1.5f * _layout.GlyphScale;

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

        private void DrawUnicodeAccidentalFallback(
            ICanvas canvas, string glyph, float boxLeft, float boxW, float staffY, bool isFlat)
        {
            float symH = OmitStaffHeader
                ? OmitHeaderStaffSpace * OmitHeaderAccidentalHeightSpaces
                : boxW * (isFlat ? 1.1f : 1.05f);
            float yTop = staffY - symH * 0.5f;
            canvas.FontSize = BodyAccidentalFontSize(isFlat);
            canvas.DrawString(glyph, boxLeft, yTop, Math.Max(1f, boxW), symH,
                HorizontalAlignment.Right, VerticalAlignment.Center);
        }

        private void DrawAccidental(
            ICanvas canvas,
            GeneratedNote note,
            IReadOnlyList<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            int index,
            BarLayout[] barLayouts,
            IReadOnlyList<double> barBeats,
            double beatOrigin,
            float y,
            Color ink,
            Dictionary<(char, int), Accidental>? history,
            HashSet<(char, int)>? barCancelled,
            byte fadeAlpha,
            float headerRightAbs,
            out bool drawReached)
        {
            drawReached = false;
            if (index < 0 || index >= noteLayouts.Length)
            {
                if (ChromaticMidi61Diagnostics.IsEnabled && note.MidiNumber == ChromaticMidi61Diagnostics.MidiCs4)
                    ChromaticMidi61Diagnostics.RecordSkip(note.MidiNumber, "DrawAccidental: index out of range");
                return;
            }

            if (!TryResolveBodyAccidental(note, history, barCancelled, out var eff, out bool draw))
            {
                if (ChromaticMidi61Diagnostics.IsEnabled && note.MidiNumber == ChromaticMidi61Diagnostics.MidiCs4)
                    ChromaticMidi61Diagnostics.RecordSkip(note.MidiNumber, "DrawAccidental: TryResolveBodyAccidental returned false");
                return;
            }

            if (history != null && eff != Accidental.None)
                history[(note.Letter, note.Octave)] = eff;

            if (!draw || eff == Accidental.None)
            {
                if (ChromaticMidi61Diagnostics.IsEnabled && note.MidiNumber == ChromaticMidi61Diagnostics.MidiCs4)
                    ChromaticMidi61Diagnostics.RecordSkip(note.MidiNumber,
                        $"DrawAccidental: suppressed draw={draw} eff={eff}");
                return;
            }

            canvas.SaveState();
            try
            {
                string glyph = eff switch
                {
                    Accidental.Sharp => "♯",
                    Accidental.Flat => "♭",
                    Accidental.Natural => "♮",
                    Accidental.DoubleSharp => "𝄪",
                    Accidental.DoubleFlat => "𝄫",
                    _ => ""
                };
                if (string.IsNullOrEmpty(glyph))
                {
                    if (ChromaticMidi61Diagnostics.IsEnabled && note.MidiNumber == ChromaticMidi61Diagnostics.MidiCs4)
                        ChromaticMidi61Diagnostics.RecordSkip(note.MidiNumber, "DrawAccidental: empty glyph");
                    return;
                }

                bool isFlat = IsFlatBodyAccidental(eff);
                bool isNatural = eff == Accidental.Natural;
                float symW = BodyAccidentalDrawWidth(isFlat, isNatural);

                TryClearAccidentalHeaderClip(
                    notes, noteLayouts, barLayouts, barBeats, beatOrigin,
                    index, headerRightAbs, isFlat, isNatural);

                ResolveBodyAccidentalHorizontalBounds(
                    noteLayouts[index].X, isFlat, isNatural, out float boxLeft, out float boxRight);
                noteLayouts[index].AccidentalX = boxLeft;
                float boxW = Math.Max(1f, symW);

                canvas.FontColor = ApplyAlpha(ink, fadeAlpha);
                drawReached = true;

                if (isFlat)
                {
                    string smufl = eff == Accidental.DoubleFlat ? "\uE264" : "\uE260";
                    if (!TryDrawBodySmuFLAccidental(canvas, smufl, boxRight, y, ApplyAlpha(ink, fadeAlpha), isFlat: true, isNatural: false))
                    {
                        DrawUnicodeAccidentalFallback(canvas, glyph, boxLeft, boxW, y, isFlat: true);
                    }
                }
                else if (isNatural)
                {
                    if (!TryDrawBodySmuFLAccidental(canvas, "\uE261", boxRight, y, ApplyAlpha(ink, fadeAlpha), isFlat: false, isNatural: true))
                    {
                        DrawUnicodeAccidentalFallback(canvas, glyph, boxLeft, boxW, y, isFlat: false);
                    }
                }
                else
                {
                    // Sharps and double-sharps: use Bravura glyphs so positioning is
                    // consistent with flats and naturals.
                    string smufl = eff == Accidental.DoubleSharp ? "\uE263" : "\uE262";
                    if (!TryDrawBodySmuFLAccidental(canvas, smufl, boxRight, y, ApplyAlpha(ink, fadeAlpha), isFlat: false, isNatural: false))
                    {
                        DrawUnicodeAccidentalFallback(canvas, glyph, boxLeft, boxW, y, isFlat: false);
                    }
                }

                // Key-sig reminder drawn — clear cancellation for this letter+octave in the bar.
                if (IsAccidentalInKeySig(eff, note.Letter))
                    barCancelled?.Remove((note.Letter, note.Octave));
            }
            finally { canvas.RestoreState(); }
        }

        /// <summary>
        /// If a body accidental would paint under the clef/key/time header, shift this note and
        /// later notes in the same measure right — never past the following bar line.
        /// </summary>
        private void TryClearAccidentalHeaderClip(
            IReadOnlyList<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            BarLayout[] barLayouts,
            IReadOnlyList<double> barBeats,
            double beatOrigin,
            int index,
            float headerRightAbs,
            bool isFlat,
            bool isNatural)
        {
            float boxLeft = AccidentalBoxLeft(noteLayouts[index].X, isFlat, isNatural);
            if (boxLeft >= headerRightAbs - 0.5f)
                return;

            float desiredPush = headerRightAbs - boxLeft + 0.5f;
            var sortedBarBeatsRel = barBeats.Select(b => b - beatOrigin).OrderBy(b => b).ToList();
            double noteBeat = (notes[index].BeatPosition ?? 0.0) - beatOrigin;
            int measure = GetMeasureIndexForBeat(noteBeat, sortedBarBeatsRel);

            float maxCenter = float.PositiveInfinity;
            if (measure < barLayouts.Length)
            {
                maxCenter = barLayouts[measure].X
                    - BarLeftPadding
                    - NoteCenterTrailingReach(notes[index].IsRest, notes[index].Duration);
            }

            float maxPush = maxCenter - noteLayouts[index].X;
            if (maxPush < 0.5f)
                return;

            float push = Math.Min(desiredPush, maxPush);
            for (int i = index; i < notes.Count && i < noteLayouts.Length; i++)
            {
                double rel = (notes[i].BeatPosition ?? 0.0) - beatOrigin;
                if (GetMeasureIndexForBeat(rel, sortedBarBeatsRel) != measure)
                    break;
                noteLayouts[i].X += push;
                SyncAccidentalX(noteLayouts, i);
            }
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
            eff = InferAccidentalFromSpelling(note);
            draw = false;
            var pitchKey = (note.Letter, note.Octave);
            string? sigAcc = GetSignatureAccidentalForLetter(note.Letter);

            // Repeated same altered pitch after an accidental earlier in this measure.
            if (eff == Accidental.None && history != null
                && history.TryGetValue(pitchKey, out var prev)
                && IsChromaticAccidental(prev))
            {
                if (NoteMatchesAlteration(note, pitchKey, prev))
                    return true; // carry — omit glyph, keep prior alteration

                // Letter+octave changed pitch — courtesy or cancellation.
                if (sigAcc == "#")
                {
                    eff = Accidental.Sharp;
                    draw = barCancelled != null && barCancelled.Contains(pitchKey);
                }
                else if (sigAcc == "b")
                {
                    eff = Accidental.Flat;
                    draw = barCancelled != null && barCancelled.Contains(pitchKey);
                }
                else
                {
                    eff = Accidental.Natural;
                    draw = true;
                }
            }

            if (eff == Accidental.None)
                return true;

            // ♮ when cancelling a key-signature alteration, or a prior chromatic in this bar.
            // Same ♮ already active for this letter+octave — carry (do not redraw).
            if (eff == Accidental.Natural)
            {
                Accidental? priorNatural = null;
                if (history != null && history.TryGetValue(pitchKey, out var priorNat))
                    priorNatural = priorNat;
                draw = MeasureAccidentalRules.ShouldDrawNatural(sigAcc, priorNatural);
                return true;
            }

            // ♯/♭ already implied by the key signature — omit unless restored after cancellation.
            if (IsAccidentalInKeySig(eff, note.Letter))
            {
                draw = barCancelled != null && barCancelled.Contains(pitchKey);
                return true;
            }

            // Same accidental already active this measure — carry through without redrawing.
            if (history != null && history.TryGetValue(pitchKey, out var priorInBar) && priorInBar == eff)
                return true;

            draw = true;
            return true;
        }

        private static Accidental InferAccidentalFromSpelling(GeneratedNote note)
        {
            if (note.Accidental != Accidental.None)
                return note.Accidental;

            string name = note.SpelledName;
            if (name.Contains("##")) return Accidental.DoubleSharp;
            if (name.Contains("bb")) return Accidental.DoubleFlat;
            if (name.Contains('#')) return Accidental.Sharp;
            if (name.Contains('b')) return Accidental.Flat;
            return Accidental.None;
        }

        private static bool IsChromaticAccidental(Accidental acc)
            => acc is Accidental.Sharp or Accidental.Flat
                or Accidental.DoubleSharp or Accidental.DoubleFlat;

        private static int AlterationSemitones(Accidental acc) => acc switch
        {
            Accidental.Sharp => 1,
            Accidental.Flat => -1,
            Accidental.DoubleSharp => 2,
            Accidental.DoubleFlat => -2,
            _ => 0
        };

        private static bool NoteMatchesAlteration(
            GeneratedNote note, (char Letter, int Octave) pitchKey, Accidental prior)
        {
            int naturalMidi = NoteSessionService.NoteNameToMidi($"{pitchKey.Letter}{pitchKey.Octave}");
            int expected = naturalMidi + AlterationSemitones(prior);
            return note.MidiNumber == expected;
        }

        private readonly struct LayoutAccidentalInfo
        {
            public bool HasAccidental { get; init; }
            public bool IsFlat { get; init; }
            public bool IsNatural { get; init; }
        }

        /// <summary>
        /// Resolves whether a note needs accidental ink during layout, updating per-measure history
        /// the same way <see cref="DrawAccidental"/> does when rendering.
        /// </summary>
        private LayoutAccidentalInfo ResolveLayoutAccidental(
            GeneratedNote note,
            Dictionary<(char, int), Accidental> history,
            HashSet<(char, int)> barCancelled)
        {
            if (note.IsRest)
                return default;

            var inferred = InferAccidentalFromSpelling(note);
            if (inferred != Accidental.None
                && !IsAccidentalInKeySig(inferred, note.Letter) && IsNoteInKeySig(note))
            {
                barCancelled.Add((note.Letter, note.Octave));
            }

            if (!TryResolveBodyAccidental(note, history, barCancelled, out var eff, out bool draw))
                return default;

            bool hasAcc = draw && eff != Accidental.None;
            // Always record the effective accidental for the rest of the measure,
            // including carries where the glyph is omitted.
            if (eff != Accidental.None)
                history[(note.Letter, note.Octave)] = eff;

            return new LayoutAccidentalInfo
            {
                HasAccidental = hasAcc,
                IsFlat = hasAcc && IsFlatBodyAccidental(eff),
                IsNatural = hasAcc && IsNaturalBodyAccidental(eff),
            };
        }

        private void DrawNoteName(ICanvas canvas, GeneratedNote note, float x, float ny,
                                   float staffTop, float staffBot, Color ink, byte fadeAlpha,
                                   float safeTop, float safeBottom)
        {
            canvas.SaveState();
            try
            {
                canvas.FontColor = ApplyAlpha(ink, fadeAlpha);
                float nameScale = Math.Clamp(_layout.Sls / 12f, 0.5f, 2.5f);
                canvas.FontSize = 11f * nameScale;
                var label = ResolveNoteNameLabelLayout(
                    x, ny, _layout.NoteHeadR, staffTop, staffBot,
                    safeTop, safeBottom, NoteNameMinEdgeClearancePx, nameScale);
                canvas.DrawString(note.SpelledName, label.X, label.Y, label.Width, label.Height,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
            }
            finally { canvas.RestoreState(); }
        }

        /// <summary>
        /// Chooses note-name label placement: preferred outside of the staff mid-line,
        /// flipping inward when the preferred side would violate top/bottom clearance,
        /// then beside the notehead if neither vertical side is safe.
        /// Does not move the note itself.
        /// </summary>
        internal static NoteNameLabelLayout ResolveNoteNameLabelLayout(
            float noteX,
            float noteY,
            float noteHeadR,
            float staffTop,
            float staffBot,
            float safeTop,
            float safeBottom,
            float minEdgeClearance = NoteNameMinEdgeClearancePx,
            float sizeScale = 1f)
        {
            float scale = sizeScale > 0.01f ? sizeScale : 1f;
            float labelW = NoteNameLabelWidth * scale;
            float labelH = NoteNameLabelHeight * scale;
            float aboveGap = NoteNameAboveGap * scale;
            float belowGap = NoteNameBelowGap * scale;
            float besideGap = NoteNameBesideGap * scale;
            float aboveY = noteY - noteHeadR - aboveGap;
            float belowY = noteY + noteHeadR + belowGap;
            float mid = (staffTop + staffBot) / 2f;
            bool preferBelow = noteY > mid;

            bool FitsVertically(float labelY)
                => labelY >= safeTop + minEdgeClearance
                   && labelY + labelH <= safeBottom - minEdgeClearance;

            if (preferBelow)
            {
                if (FitsVertically(belowY))
                {
                    return new NoteNameLabelLayout(
                        noteX - labelW * 0.5f, belowY,
                        labelW, labelH, NoteNameLabelSide.Below);
                }

                if (FitsVertically(aboveY))
                {
                    return new NoteNameLabelLayout(
                        noteX - labelW * 0.5f, aboveY,
                        labelW, labelH, NoteNameLabelSide.Above);
                }
            }
            else
            {
                if (FitsVertically(aboveY))
                {
                    return new NoteNameLabelLayout(
                        noteX - labelW * 0.5f, aboveY,
                        labelW, labelH, NoteNameLabelSide.Above);
                }

                if (FitsVertically(belowY))
                {
                    return new NoteNameLabelLayout(
                        noteX - labelW * 0.5f, belowY,
                        labelW, labelH, NoteNameLabelSide.Below);
                }
            }

            // Neither vertical side is safe — place beside the notehead (prefer right:
            // body accidentals are drawn to the left).
            float besideY = noteY - labelH * 0.5f;
            float minY = safeTop + minEdgeClearance;
            float maxY = safeBottom - minEdgeClearance - labelH;
            if (maxY >= minY)
            {
                besideY = Math.Clamp(besideY, minY, maxY);
            }
            else
            {
                // Canvas band shorter than label+clearance — keep the box inside the canvas.
                float looseMin = safeTop;
                float looseMax = safeBottom - labelH;
                besideY = looseMax >= looseMin
                    ? Math.Clamp(besideY, looseMin, looseMax)
                    : safeTop;
            }

            float rightX = noteX + noteHeadR + besideGap;
            return new NoteNameLabelLayout(
                rightX, besideY,
                labelW, labelH, NoteNameLabelSide.BesideRight);
        }

        /// <summary>
        /// Outermost ledger Y positions matching <see cref="DrawLedgerLines"/> (for clearance tests).
        /// </summary>
        internal static (float OuterTop, float OuterBottom) EstimateLedgerVerticalExtent(
            float noteY,
            float staffTop,
            float staffBot,
            float staffSpace)
        {
            float outerTop = staffTop;
            float outerBottom = staffBot;
            if (noteY < staffTop - 2f)
            {
                float cur = staffTop - staffSpace;
                while (cur >= noteY - 2f)
                {
                    outerTop = Math.Min(outerTop, cur);
                    cur -= staffSpace;
                }
            }

            if (noteY > staffBot + 2f)
            {
                float cur = staffBot + staffSpace;
                while (cur <= noteY + 2f)
                {
                    outerBottom = Math.Max(outerBottom, cur);
                    cur += staffSpace;
                }
            }

            return (outerTop, outerBottom);
        }

        // ── Key / time signature drawing ──────────────────────────────────────────

        private string ActiveKeySignatureScale()
        {
            if (!string.IsNullOrWhiteSpace(NotationScaleOverride))
                return NotationScaleOverride!;
            // Practice / saved tunes use their authored key + scale for key-signature rules.
            if (_session.Tune == "Practice Tune" && _session.CurrentTune != null)
                return _session.GetNotationKeyAndScale().Scale;
            // Arpeggios spell notes via GetNotationKeyAndScale (Natural Minor for minor-family).
            // Forcing Major here dropped written accidentals (e.g. Ab reading as A).
            if (_session.Tune == "Arpeggio")
                return _session.GetNotationKeyAndScale().Scale;
            return string.IsNullOrWhiteSpace(_session.EffectiveScale)
                ? _session.SelectedScale
                : _session.EffectiveScale;
        }

        /// <summary>
        /// True when a key signature is actually engraved on the staff.
        /// Chromatic, Tuner, and omit-header modes draw no signature.
        /// Practice / saved tunes draw their authored key signature.
        /// Accidental-state rules must follow this flag — never the selected key alone —
        /// or flats/sharps implied by a hidden signature are wrongly suppressed.
        /// </summary>
        private bool IsKeySignatureVisiblyDrawn()
        {
            if (OmitStaffHeader)
                return false;
            if (_session.Tune == "Tuner")
                return false;
            // Chromatic walks start on the selected tonic but show no key signature.
            if (_session.Tune != "Arpeggio" && ActiveKeySignatureScale() == "Chromatic")
                return false;
            return true;
        }

        /// <summary>
        /// Key/scale used for body-accidental implication. When the signature is not drawn,
        /// treat as C major (no letter alterations) so every written accidental is explicit.
        /// </summary>
        private (string Key, string Scale) DisplayedKeySignature()
            => IsKeySignatureVisiblyDrawn()
                ? (ActiveNotationKey, ActiveKeySignatureScale())
                : ("C", "Major");

        private float DrawKeySignature(ICanvas canvas, float staffTop, float staffMid, Color ink)
        {
            float symW = KeySigGlyphWidth();
            float symSlot = KeySigSymbolSlot();

            if (!IsKeySignatureVisiblyDrawn())
                return _headerMetrics.KeySigStartX;

            string key = ActiveNotationKey;
            int accCount = KeySignatureRules.GetAccidentalCount(key, ActiveKeySignatureScale());
            if (accCount == 0)
                return _headerMetrics.KeySigStartX;

            StaffLog($"[Staff] KeySig key={key} scale={ActiveKeySignatureScale()} count={accCount}");

            bool useFlats = KeySignatureRules.KeySignatureUsesFlats(key, ActiveKeySignatureScale());
            string glyph = useFlats ? "\uE260" : "\uE262";
            float fontSize = KeySigAccidentalFontSize(useFlats);
            var pitches = useFlats ? KeySigFlatPitches : KeySigSharpPitches;

            canvas.SaveState();
            canvas.FontColor = ink;
            float sigX = _headerMetrics.KeySigStartX;
            for (int i = 0; i < Math.Min(accCount, pitches.Length); i++)
            {
                var (letter, octave) = pitches[i];
                float yLine = KeySigLineY(letter, octave, staffMid);

                canvas.SaveState();
                if (!DrawKeySigAccidental(canvas, glyph, sigX, symW, yLine, fontSize, ink, useFlats))
                {
                    // Unicode fallback: centre the draw box on yLine so the glyph's font
                    // metrics centre approximately aligns with the staff position.
                    float boxH = symW * (useFlats ? 1.1f : 1.05f);
                    float yTop = yLine - boxH * 0.5f;
                    canvas.Font = Microsoft.Maui.Graphics.Font.Default;
                    canvas.FontSize = KeySigAccidentalFontSize(useFlats);
                    canvas.DrawString(useFlats ? "♭" : "♯", sigX, yTop, symW, boxH,
                        HorizontalAlignment.Center, VerticalAlignment.Center);
                }

                canvas.RestoreState();
                sigX += symSlot;
            }
            canvas.RestoreState();
            return _headerMetrics.KeySigStartX + KeySigDrawnWidth(accCount);
        }

        private void DrawTimeSignature(ICanvas canvas, float staffTop, float staffMid,
                                       Color ink, float keySigEndX, bool captureHitTarget)
        {
            if (_session.Tune == "Tuner")
            {
                if (captureHitTarget)
                    ClearTimeSignatureBounds();
                return;
            }

            canvas.SaveState();
            try
            {
                string timeSig = _session.GetDisplayTimeSignature();
                var parts = timeSig.Split('/');
                if (parts.Length != 2)
                {
                    if (captureHitTarget)
                        ClearTimeSignatureBounds();
                    return;
                }

                float tsX = keySigEndX;
                float boxW = TimeSignatureBoxWidth(timeSig);

                float tsFontSize = Math.Max(10f, _layout.Sls * 1.83f);  // 22 at sls=12
                canvas.FontColor = ink;
                canvas.FontSize = tsFontSize;
                canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;

                float halfH = _layout.Sls * 2f;
                // One line of text only — height must not invite wrapping of multi-digit meters.
                float topY = staffTop + (halfH - tsFontSize) * 0.5f;
                float bottomY = staffMid + (halfH - tsFontSize) * 0.5f;

                canvas.DrawString(parts[0], tsX, topY, boxW, tsFontSize,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
                canvas.DrawString(parts[1], tsX, bottomY, boxW, tsFontSize,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;

                // Upper staff draws the numerals but does not own a hit target when a lower
                // staff also shows a time signature. The single target's bottom stays at this
                // staff's time-signature bottom; its top meets the bottom of the tempo hit area.
                if (!captureHitTarget)
                    return;

                float left = tsX - 4f;
                float width = Math.Max(8f, boxW + 8f);
                float glyphBottom = bottomY + tsFontSize + tsFontSize * 0.35f;
                float hitTop = Math.Min(staffTop, topY);
                if (_lastMusicBpmMarkingBounds is RectF bpm)
                {
                    float tempoBottom = bpm.Y + bpm.Height;
                    if (tempoBottom < glyphBottom)
                        hitTop = tempoBottom;
                }

                if (hitTop > glyphBottom - 8f)
                    hitTop = glyphBottom - 8f;

                _lastTimeSignatureBounds = new RectF(left, hitTop, width, Math.Max(8f, glyphBottom - hitTop));
                TimeSignatureBoundsChanged?.Invoke(this, EventArgs.Empty);
            }
            finally { canvas.RestoreState(); }
        }

        private RectF? _lastTimeSignatureBounds;

        /// <summary>Last-drawn time-signature bounds in GraphicsView coordinates, if any.</summary>
        public RectF? LastTimeSignatureBounds => _lastTimeSignatureBounds;

        /// <summary>Raised after the time signature is (re)drawn so the Music page can place its hit target.</summary>
        public event EventHandler? TimeSignatureBoundsChanged;

        /// <summary>
        /// True when <paramref name="x"/>/<paramref name="y"/> (GraphicsView coords) hit the
        /// last-drawn time signature. Used to open the Music-page time-signature control.
        /// </summary>
        public bool HitTestTimeSignature(float x, float y)
        {
            if (_lastTimeSignatureBounds is not RectF r)
                return false;

            // Horizontal slack only — vertical edges stay at tempo-bottom and time-sig bottom
            // so the target does not overlap the tempo hit area.
            const float padX = 8f;
            return x >= r.X - padX && x <= r.X + r.Width + padX
                && y >= r.Y && y <= r.Y + r.Height;
        }

        private void ClearTimeSignatureBounds()
        {
            if (_lastTimeSignatureBounds == null)
                return;
            _lastTimeSignatureBounds = null;
            TimeSignatureBoundsChanged?.Invoke(this, EventArgs.Empty);
        }

        private string? GetSignatureAccidentalForLetter(char letter)
        {
            var (key, scale) = DisplayedKeySignature();
            return KeySignatureRules.GetSignatureAccidentalForLetter(letter, key, scale);
        }

        private bool IsAccidentalInKeySig(Accidental accidental, char letter)
        {
            if (accidental == Accidental.None || accidental == Accidental.Natural) return false;

            var (key, scale) = DisplayedKeySignature();
            bool useFlats = KeySignatureRules.KeySignatureUsesFlats(key, scale);
            bool typeMatch = useFlats
                ? accidental == Accidental.Flat
                : accidental == Accidental.Sharp;
            if (!typeMatch) return false;

            return KeySignatureRules.IsLetterInKeySignature(letter, key, scale);
        }

        /// <summary>
        /// Returns true if this note's letter is governed by the <em>drawn</em> key signature
        /// (i.e. the visible signature applies a sharp or flat to this letter),
        /// regardless of the accidental currently on the note.
        /// Used to detect when a key-sig note is given a different accidental,
        /// cancelling the key sig within the bar.
        /// </summary>
        private bool IsNoteInKeySig(GeneratedNote note)
        {
            var (key, scale) = DisplayedKeySignature();
            return KeySignatureRules.IsLetterInKeySignature(note.Letter, key, scale);
        }

#if DEBUG
        private static void StaffLog(string message)
            => DebugLog.WriteLine(DebugLogCategory.StaffLog, message);

        // ── Diagnostic test runner ────────────────────────────────────────────────
        // Called from MusicPage.OnAppearing (DEBUG builds only) so the tests always
        // run regardless of which key is currently selected.

        /// <summary>
        /// Verifies key-signature and transposition logic and writes results to logcat.
        /// On Android: both <c>adb logcat</c> (Console.Error) and the VS Device Log
        /// (Debug.WriteLine) receive output.  Search for <c>[KeySigTest]</c> or
        /// <c>[TransposeTest]</c>.  Every passing line ends with <c>OK</c>.
        /// Only compiled in Debug builds.
        /// </summary>
        private static int _keySignatureTestsRun;

        public static void RunKeySignatureTests()
        {
            if (Interlocked.CompareExchange(ref _keySignatureTestsRun, 1, 0) != 0)
                return;

            Utilities.DebugTestLog.Write("[KeySigTest] OK | self-test START");

            KeySignatureRules.RunDebugSelfTests();

            // ── Instrument transposition: written key ↔ concert key ────────────────
            // TransposeOffset convention (negative = instrument sounds lower than written):
            //   Bb clarinet = -2, Eb alto sax = -9, F horn = -7
            // GetConcertKey() = TransposeKey(writtenKey, offset), so
            //   TransposeKey("D", -2) should return "C"  (Bb clarinet written D → concert C)
            // ToWrittenKey(concert, offset) = TransposeKey(concert, -offset), so
            //   ToWrittenKey("D", -2) should return "E"  (concert D → Bb clarinet written E)
            var transposeTests = new (string Written, int Offset, string Expected, string Desc)[]
            {
                ("D", -2, "C", "Bb clarinet: written D → concert C"),
                ("G", -2, "F", "Bb clarinet: written G → concert F"),
                ("A", -9, "C", "Eb alto sax: written A → concert C"),
                ("G", -7, "C", "F horn: written G → concert C"),
            };
            foreach (var t in transposeTests)
            {
                string concert = NoteSessionService.TransposeKey(t.Written, t.Offset);
                bool ok = string.Equals(concert, t.Expected, StringComparison.OrdinalIgnoreCase);
                string result = ok ? "OK" : $"FAIL: expected {t.Expected}, got {concert}";
                Utilities.DebugTestLog.Write($"[TransposeTest] {result} | {t.Desc}");
            }

            var writtenFromConcertTests = new (string Concert, int Offset, string Expected, string Desc)[]
            {
                ("D", -2, "E", "Bb clarinet: concert D → written E"),
                ("C", -2, "D", "Bb clarinet: concert C → written D"),
                ("Eb", -2, "F", "Bb clarinet: concert Eb → written F"),
                ("D", -9, "B", "Eb alto sax: concert D → written B"),
            };
            foreach (var t in writtenFromConcertTests)
            {
                string written = NoteSessionService.ToWrittenKey(t.Concert, t.Offset);
                bool ok = string.Equals(written, t.Expected, StringComparison.OrdinalIgnoreCase);
                string result = ok ? "OK" : $"FAIL: expected {t.Expected}, got {written}";
                Utilities.DebugTestLog.Write($"[TransposeTest] {result} | {t.Desc}");
            }

            // ── Key-signature staff positions (treble clef) ───────────────────────
            // Verify that KeySigFlatPitches / KeySigSharpPitches contain the canonical
            // treble-clef letter+octave for each accidental in BEADGCF / FCGDAEB order.
            var flatExpected = new[] { ('B', 4), ('E', 5), ('A', 4), ('D', 5), ('G', 4), ('C', 5), ('F', 4) };
            var sharpExpected = new[] { ('F', 5), ('C', 5), ('G', 5), ('D', 5), ('A', 4), ('E', 5), ('B', 4) };
            bool flatOk = KeySigFlatPitches.SequenceEqual(flatExpected);
            bool sharpOk = KeySigSharpPitches.SequenceEqual(sharpExpected);
            Utilities.DebugTestLog.Write($"[KeySigTest] {(flatOk ? "OK" : "FAIL: KeySigFlatPitches mismatch")} | flat staff positions (BEADGCF)");
            Utilities.DebugTestLog.Write($"[KeySigTest] {(sharpOk ? "OK" : "FAIL: KeySigSharpPitches mismatch")} | sharp staff positions (FCGDAEB)");

            Utilities.DebugTestLog.Write("[KeySigTest] OK | self-test END");
        }

        private static int _measureLayoutTestsRun;

        /// <summary>
        /// Verifies measure-based staff assignment: 8 measures × 4 quarter notes,
        /// no measure split across staves, bar lines on measure boundaries.
        /// </summary>
        public static void RunMeasureLayoutTests()
        {
            if (Interlocked.CompareExchange(ref _measureLayoutTestsRun, 1, 0) != 0)
                return;

            Utilities.DebugTestLog.Write("[MeasureLayoutTest] OK | self-test START");

            const int measureCount = 8;
            const int notesPerMeasure = 4;
            const double measureBeats = 4.0;

            var notes = new List<GeneratedNote>();
            int midi = 60;
            for (int m = 0; m < measureCount; m++)
            {
                for (int b = 0; b < notesPerMeasure; b++)
                {
                    notes.Add(new GeneratedNote
                    {
                        MidiNumber = midi,
                        Letter = 'C',
                        Octave = 4 + (midi - 60) / 12,
                        SpelledName = $"T{midi}",
                        Duration = NoteDuration.Quarter,
                        MeasureIndex = m,
                        BeatPosition = m * measureBeats + b,
                    });
                    midi++;
                }
            }

            var barBeats = new List<double>();
            for (double bar = measureBeats; bar < measureCount * measureBeats; bar += measureBeats)
                barBeats.Add(bar);

            bool notesPerMeasureOk = true;
            for (int m = 0; m < measureCount; m++)
            {
                int count = notes.Count(n => (n.MeasureIndex ?? -1) == m && !n.IsRest);
                if (count != notesPerMeasure)
                {
                    notesPerMeasureOk = false;
                    Utilities.DebugTestLog.Write(
                        $"[MeasureLayoutTest] FAIL: measure {m + 1} has {count} notes (expected {notesPerMeasure})");
                }
            }
            if (notesPerMeasureOk)
                Utilities.DebugTestLog.Write("[MeasureLayoutTest] OK | 8 measures × 4 quarter notes");

            var session = new NoteSessionService { Key = "C", SelectedScale = "Major", MeterTimeSignature = "4/4" };
            var drawable = new StaffDrawable(session, new ThemeService(), safeArea: null);
            var split = drawable.SplitMeasuresAcrossStaves(notes, barBeats, canvasWidth: 360f, canvasHeight: 480f);

            bool singleStaffOk = true;
            for (int m = 0; m < measureCount; m++)
            {
                bool onUpper = split.UpperNotes.Any(n => (n.MeasureIndex ?? -1) == m);
                bool onLower = split.LowerNotes.Any(n => (n.MeasureIndex ?? -1) == m);
                if (onUpper && onLower)
                {
                    singleStaffOk = false;
                    Utilities.DebugTestLog.Write(
                        $"[MeasureLayoutTest] FAIL: measure {m + 1} appears on both staves");
                }
            }
            if (split.UpperMeasureCount + split.LowerMeasureCount < 1)
            {
                singleStaffOk = false;
                Utilities.DebugTestLog.Write("[MeasureLayoutTest] FAIL: no measures placed");
            }
            if (singleStaffOk)
                Utilities.DebugTestLog.Write(
                    $"[MeasureLayoutTest] OK | packed upper={split.UpperMeasureCount} " +
                    $"lower={split.LowerMeasureCount} unplaced={split.UnplacedMeasureCount}");

            var upperBarBeats = new List<double>();
            double upperOrigin = split.UpperNotes.Count > 0
                ? split.UpperNotes.Min(n => n.BeatPosition ?? 0.0)
                : 0.0;
            double upperEnd = split.UpperNotes.Count > 0
                ? split.UpperNotes.Max(n => (n.BeatPosition ?? 0.0) + n.BeatDuration)
                : 0.0;
            for (double bar = upperOrigin + measureBeats; bar < upperEnd - 1e-6; bar += measureBeats)
                upperBarBeats.Add(bar);

            var lowerBarBeats = new List<double>();
            double lowerOrigin = split.LowerNotes.Count > 0
                ? split.LowerNotes.Min(n => n.BeatPosition ?? 0.0)
                : 0.0;
            double lowerEnd = split.LowerNotes.Count > 0
                ? split.LowerNotes.Max(n => (n.BeatPosition ?? 0.0) + n.BeatDuration)
                : 0.0;
            for (double bar = lowerOrigin + measureBeats; bar < lowerEnd - 1e-6; bar += measureBeats)
                lowerBarBeats.Add(bar);

            bool barlinesOk = true;
            foreach (double bar in upperBarBeats)
            {
                if (Math.Abs(bar % measureBeats) > 1e-6 && Math.Abs(bar - upperOrigin) > 1e-6)
                {
                    barlinesOk = false;
                    Utilities.DebugTestLog.Write(
                        $"[MeasureLayoutTest] FAIL: upper bar at beat {bar:F2} is not on a measure boundary");
                }
            }
            foreach (double bar in lowerBarBeats)
            {
                if (Math.Abs(bar % measureBeats) > 1e-6 && Math.Abs(bar - lowerOrigin) > 1e-6)
                {
                    barlinesOk = false;
                    Utilities.DebugTestLog.Write(
                        $"[MeasureLayoutTest] FAIL: lower bar at beat {bar:F2} is not on a measure boundary");
                }
            }
            if (barlinesOk)
                Utilities.DebugTestLog.Write("[MeasureLayoutTest] OK | barlines on measure boundaries");

            Utilities.DebugTestLog.Write("[MeasureLayoutTest] OK | self-test END");
        }
#else
        private static void StaffLog(string message) { }
        public static void RunKeySignatureTests() { }
        public static void RunMeasureLayoutTests() { }
#endif
    }
}
