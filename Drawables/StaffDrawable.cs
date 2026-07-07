using System.Diagnostics;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Skia;
using musicmate.Diagnostics;
using musicmate.Models;
using musicmate.Services;

namespace musicmate.Drawables
{
    /// <summary>
    /// Two-staff drawable.
    /// Renders an upper and a lower treble staff inside a single <see cref="ICanvas"/>.
    /// Reading order follows standard sheet music: upper staff first, then lower staff.
    /// These are two <b>independent</b> lines of music (not a grand staff): the lower staff
    /// omits key and time signatures for space but uses the same key and meter; internal bar
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

        /// <summary>Result of assigning whole measures to upper/lower staves by width.</summary>
        public sealed class StaffMeasureSplitResult
        {
            public List<GeneratedNote> UpperNotes { get; } = new();
            public List<GeneratedNote> LowerNotes { get; } = new();
            public int UpperMeasureCount { get; set; }
            public int TotalMeasureCount { get; set; }
        }

        // ── Fixed horizontal constants ─────────────────────────────────────────
        private const float ScrollPxPerBeat = 42f;   // reduced from 48 for better fit
        private const float RightMargin = 36f;

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

        private const float AccidentalRightGap = 0.5f;
        private const float DoubleBarExtraWidth = 4f;

        // Notehead ellipse is drawn with height = NoteHeadR * NoteHeadHeightFactor.
        private const float NoteHeadHeightFactor = 1.5f;
        private const float BeginnerNoteHeadSpaceRatio = 0.9f; // target: 90% of staff-space height
        private const float CompactNoteHeadRRatio = 0.32f;
        /// <summary>Child levels 31+: modest notehead enlargement without beginner-scale collision risk.</summary>
        private const float MidLevelNoteHeadRRatio = 0.41f;
        /// <summary>Horizontal gap between key signature and time signature (px).</summary>
        private const float KeySigTimeSigGap = 1f;
        private const float CompactStemLenRatio = 2.15f;
        private const float CompactAccidentalRefPx = 26f;
        private const float CompactRestScale = 0.72f;

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

        /// <summary>Draw/layout scale for accidentals attached to notes (after the key signature).</summary>
        private float BodyAccidentalGlyphScale()
        {
            float keyScale = KeySigAccidentalScale();
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

        /// <summary>Minimum ink gap between consecutive notes inside a beam group.</summary>
        private const float BeamedInternalInkGap = 2f;

        /// <summary>Clear ink between body-accidental box and notehead left edge (px).</summary>
        private float BodyAccidentalRightGapPx =>
            Math.Max(3.5f * BodyAccidentalGlyphScale(), AccidentalRightGap * _layout.Sls * 0.1f);

        private float KeySigSymbolWidth()
            => CompactAccidentalRefPx * (_layout.Sls / 12f) * KeySigAccidentalScale() * ArpeggioKeySigSizeBoost();

        private float BodyAccidentalSymbolWidth(bool isFlat = false)
            => CompactAccidentalRefPx * (_layout.Sls / 12f) * BodyAccidentalGlyphScale()
                * (isFlat ? BodyFlatSizeBoost : 1f);

        private static bool IsFlatBodyAccidental(Accidental acc)
            => acc == Accidental.Flat || acc == Accidental.DoubleFlat;

        private static bool IsNaturalBodyAccidental(Accidental acc)
            => acc == Accidental.Natural;

        /// <summary>Layout/draw width for a body accidental beside the notehead.</summary>
        private float BodyAccidentalDrawWidth(bool isFlat, bool isNatural = false)
            => isFlat
                ? BodyAccidentalSymbolWidth(isFlat: true)
                : BodyAccidentalSymbolWidth() * (isNatural ? 0.92f : 1.0f);

        private float NoteHeadLeft(float centerX) => centerX - _layout.NoteHeadR;

        /// <summary>Gap before notehead; naturals use a tighter gap than sharps/flats.</summary>
        private float BodyAccidentalRightGapPxFor(bool isNatural)
            => isNatural
                ? Math.Max(2f * BodyAccidentalGlyphScale(), AccidentalRightGap * _layout.Sls * 0.06f)
                : BodyAccidentalRightGapPx;

        private float AccidentalBoxRight(float noteCenterX, bool isNatural = false)
            => NoteHeadLeft(noteCenterX) - BodyAccidentalRightGapPxFor(isNatural);

        /// <summary>Left edge of accidental draw box; right edge is <see cref="AccidentalRightGap"/> before notehead.</summary>
        private float AccidentalBoxLeft(float noteCenterX, bool isFlat, bool isNatural = false)
            => AccidentalBoxRight(noteCenterX, isNatural) - BodyAccidentalDrawWidth(isFlat, isNatural);

        /// <summary>Leftmost ink edge of a note/rest group (accidental or notehead).</summary>
        private float NoteGroupLeft(float centerX, bool isRest, bool hasAcc, bool isFlatAcc, bool isNaturalAcc = false)
            => hasAcc ? AccidentalBoxLeft(centerX, isFlatAcc, isNaturalAcc) : centerX - NoteHalfWidth(isRest);

        private float NoteGroupLeftFromLayout(NoteLayout layout)
            => NoteGroupLeft(layout.X, layout.IsRest, layout.HasAccidental, layout.AccidentalIsFlat, layout.AccidentalIsNatural);

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
        private float NoteCenterLeftReach(bool isRest, bool hasAcc, bool isFlatAcc, bool isNaturalAcc = false)
            => hasAcc
                ? _layout.NoteHeadR + BodyAccidentalRightGapPxFor(isNaturalAcc) + BodyAccidentalDrawWidth(isFlatAcc, isNaturalAcc)
                : NoteHalfWidth(isRest);

        /// <summary>Distance from note center to its right drawable edge.</summary>
        private float NoteCenterTrailingReach(bool isRest)
            => isRest ? _layout.NoteHeadR * 1.05f : _layout.NoteHeadR + Math.Max(5f, 3f * _layout.GlyphScale);

        private void SyncAccidentalX(NoteLayout[] noteLayouts, int index)
        {
            if (noteLayouts[index].HasAccidental)
            {
                noteLayouts[index].AccidentalX = AccidentalBoxLeft(
                    noteLayouts[index].X,
                    noteLayouts[index].AccidentalIsFlat,
                    noteLayouts[index].AccidentalIsNatural);
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

        private float NoteHalfWidth(bool isRest)
            => isRest ? _layout.NoteHeadR * 0.95f : _layout.NoteHeadR + StemStrokeHalfWidth;

        /// <summary>Right edge after note/rest center (stem-ward for notes).</summary>
        private float NoteTrailingRight(bool isRest, float centerX)
            => centerX + NoteCenterTrailingReach(isRest);

        private float NoteTrailingRight(GeneratedNote note, float centerX)
            => NoteTrailingRight(note.IsRest, centerX);

        /// <summary>Minimum center X after <paramref name="prevRight"/> for the next item.</summary>
        private float MinCenterAfterPrevRight(float prevRight, bool isRest, bool hasAcc, bool isFlatAcc, bool isNaturalAcc = false, float? inkGap = null)
            => prevRight + (inkGap ?? _planInkGap) + NoteCenterLeftReach(isRest, hasAcc, isFlatAcc, isNaturalAcc);

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

            public bool Equals(LayoutCacheKey other) =>
                Width == other.Width && Height == other.Height
                && SafeLeftInset == other.SafeLeftInset && SafeRightInset == other.SafeRightInset
                && UpperNotesHash == other.UpperNotesHash && LowerNotesHash == other.LowerNotesHash
                && UpperBarHash == other.UpperBarHash && LowerBarHash == other.LowerBarHash
                && UpperHasEndBar == other.UpperHasEndBar && BeginnerLayout == other.BeginnerLayout
                && ChildLevel == other.ChildLevel
                && SessionKey == other.SessionKey && SessionScale == other.SessionScale
                && SessionTune == other.SessionTune
                && TimeSig == other.TimeSig && MusicBpm == other.MusicBpm;

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
                SessionKey = _session.Key ?? string.Empty,
                SessionScale = string.IsNullOrWhiteSpace(_session.EffectiveScale)
                    ? (_session.SelectedScale ?? string.Empty)
                    : _session.EffectiveScale,
                SessionTune = _session.Tune ?? string.Empty,
                TimeSig = _session.GetDisplayTimeSignature(),
                MusicBpm = _session.MusicBpm,
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
            DrawStaffLinesAndBars(canvas, ink, upperTop, upperMid, upperBot,
                upperNoteLayouts, upperBarLayouts,
                safeLeft, safeRight, layoutRightLimit, upperStaffMargin);

            if (_session.Tune != "Tuner")
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
            DrawStaffHeaderChrome(canvas, ink, upperTop, upperMid, upperBot, drawKeyAndTimeSig: true);
            if (_session.Tune != "Tuner")
            {
                DrawStaffHeaderChrome(canvas, ink, lowerTop, lowerMid, lowerBot,
                    drawKeyAndTimeSig: UpperNotes.Count == 0);
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
                safeLeft, safeRight, layoutRightLimit, upperStaffMargin);

            if (_session.Tune != "Tuner")
            {
                DrawStaffDynamic(canvas, dirtyRect, ink, lowerTop, lowerMid, lowerBot,
                    LowerNotes, LowerNoteStates, lowerNoteLayouts, lowerBarLayouts,
                    LowerBarBeats, lowerBeatOrigin,
                    LowerAlpha, !IsUpperActive, !IsUpperActive ? ActiveNoteIndex : -1,
                    safeLeft, safeRight, layoutRightLimit, lowerStaffMargin);
            }
        }

        // ── Constructor ───────────────────────────────────────────────────────────
        public StaffDrawable(NoteSessionService session, ThemeService theme, ISafeAreaService? safeArea = null)
        {
            _session = session;
            _theme = theme;
            _safeArea = safeArea;
        }

        /// <summary>
        /// Measure-based two-staff assignment: compute each measure's minimum width,
        /// pack whole measures onto the upper staff until the next measure will not fit,
        /// then place remaining measures on the lower staff.  Never splits a measure.
        /// </summary>
        public StaffMeasureSplitResult SplitMeasuresAcrossStaves(
            List<GeneratedNote> allNotes,
            IReadOnlyList<double> barBeats,
            float canvasWidth,
            float canvasHeight)
        {
            var result = new StaffMeasureSplitResult();
            if (allNotes.Count == 0)
                return result;

            var insets = _safeArea?.GetSafeAreaInsets() ?? (0f, 0f, 0f, 0f);
            float effectiveRightInset = Math.Max(0f, insets.Right - RelaxCutoutInsetRightDp);
            float safeLeft = insets.Left;
            float layoutRightLimit = canvasWidth - effectiveRightInset - LayoutRightPad;
            float safeWidth = Math.Max(64f, layoutRightLimit - safeLeft);

            ComputeLayout(Math.Max(canvasHeight, 120f));
            _headerMetrics = ComputeHeaderMetrics(safeLeft);
            float upperUsableWidth = Math.Max(64f, safeWidth - _headerMetrics.LeftMargin - RightMargin);
            float lowerUsableWidth = Math.Max(64f, safeWidth - _headerMetrics.ClefOnlyLeftMargin - RightMargin);

            var notes = allNotes;
            double beatOrigin = GetStaffBeatOrigin(notes, barBeats);
            var barBeatsList = barBeats is List<double> list ? new List<double>(list) : barBeats.ToList();
            barBeatsList = ResolveStaffBarBeats(notes, barBeatsList, beatOrigin);

            double totalBeats = 0.0;
            for (int i = 0; i < notes.Count; i++)
            {
                double rel = (notes[i].BeatPosition ?? 0.0) - beatOrigin;
                totalBeats = Math.Max(totalBeats, rel + notes[i].BeatDuration);
            }

            var sortedBarBeats = barBeatsList.Select(b => b - beatOrigin).OrderBy(b => b).ToList();
            var segments = BuildMeasureSegments(notes, sortedBarBeats, beatOrigin, totalBeats);
            result.TotalMeasureCount = segments.Count;

            if (segments.Count == 0)
            {
                result.UpperNotes.AddRange(notes);
                return result;
            }

            var minWidths = new float[segments.Count];
            for (int m = 0; m < segments.Count; m++)
                minWidths[m] = ComputeMeasureMinWidth(notes, segments[m], beatOrigin);

            int upperMeasureCount = PackMeasuresOntoStaff(
                segments, minWidths, upperUsableWidth, startMeasureIndex: 0, isLowerStaff: false);

            if (upperMeasureCount < segments.Count)
            {
                PackMeasuresOntoStaff(
                    segments, minWidths, lowerUsableWidth,
                    startMeasureIndex: upperMeasureCount, isLowerStaff: true);
            }

            var upperIndices = new HashSet<int>();
            for (int m = 0; m < upperMeasureCount; m++)
            {
                foreach (int idx in segments[m].NoteIndices)
                    upperIndices.Add(idx);
            }

            for (int i = 0; i < notes.Count; i++)
            {
                if (upperIndices.Contains(i))
                    result.UpperNotes.Add(notes[i]);
                else
                    result.LowerNotes.Add(notes[i]);
            }

            result.UpperMeasureCount = upperMeasureCount;
            return result;
        }

        /// <summary>
        /// Packs consecutive whole measures onto one staff row.  Returns the number of
        /// measures placed (may be zero when <paramref name="startMeasureIndex"/> is past the end).
        /// </summary>
        private int PackMeasuresOntoStaff(
            List<MeasureSegment> segments,
            float[] minWidths,
            float usableWidth,
            int startMeasureIndex,
            bool isLowerStaff)
        {
            float usedWidth = 0f;
            int placed = 0;

            for (int m = startMeasureIndex; m < segments.Count; m++)
            {
                float measureWidth = minWidths[m];
                float remainingStaffWidth = usableWidth - usedWidth;
                bool mustWrap = placed > 0 && measureWidth > remainingStaffWidth + 0.5f;
                bool wrappedFromPreviousStaff = isLowerStaff && placed == 0;

#if DEBUG
                LogMeasureLayout(m + 1, measureWidth, remainingStaffWidth, mustWrap || wrappedFromPreviousStaff);
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

        /// <summary>
        /// Steps 1–8: determine note ranges, derive staff-line spacing so the complete
        /// note range (both staffs + gap) fills <paramref name="availH"/> exactly, then
        /// compute all staff Y positions.  Must be called before any drawing.
        /// </summary>
        private void ComputeLayout(float availH)
        {
            if (availH <= 0f) availH = 300f;

            // Reserve space for the OS Practice-indicator bar so notes are never hidden behind it.
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
            int eB1 = Math.Max(0, maxS1 - 4) + breathing;
            int eA2 = Math.Max(0, -4 - minS2) + breathing;
            int eB2 = Math.Max(0, maxS2 - 4) + breathing;

            // The MusicBpm quarter-note marking lives above the upper staff; reserve enough
            // room for its tangential stem so it does not clip at the top of the canvas.
            if (_session.Tune != "Tuner")
                eA1 = Math.Max(eA1, 9);

            // Total half-spaces consumed by the staff/staves (each staff = 8 hs for 5 lines / 4 spaces).
            bool tunerSingleStaff = _session.Tune == "Tuner";
            float totalHalfSpaces = tunerSingleStaff
                ? (float)(8 + eA1 + eB1)
                : (float)(8 + eA1 + eB1 + 8 + eA2 + eB2);

            // Derive sls so the two staffs fill usableH, then clamp to a comfortable range.
            float sls = usableH / (totalHalfSpaces / 2f);
            sls = Math.Clamp(sls, 6f, 12f);
            float hs = sls / 2f;

            float compactNoteHeadR = sls * CompactNoteHeadRRatio;
            float noteHeadR = UseBeginnerNotationScale
                ? sls * BeginnerNoteHeadSpaceRatio / NoteHeadHeightFactor
                : _session.ChildLevel > 30
                    ? sls * MidLevelNoteHeadRRatio
                    : compactNoteHeadR;
            float glyphScale = noteHeadR / compactNoteHeadR;
            float stemLen = UseBeginnerNotationScale
                ? 3.5f * sls
                : sls * CompactStemLenRatio * glyphScale;

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
            float slack = Math.Max(0f, usableH - contentH);
            float vOffset = slack / 2f;

            _layout = new StaffLayout
            {
                Sls = sls,
                HS = hs,
                NoteHeadR = noteHeadR,
                StemLen = stemLen,
                GlyphScale = glyphScale,
                UpperTop = upperTop + vOffset,
                UpperMid = upperMid + vOffset,
                UpperBot = upperBot + vOffset,
                LowerTop = lowerTop + vOffset,
                LowerMid = lowerMid + vOffset,
                LowerBot = lowerBot + vOffset,
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
            IReadOnlyDictionary<int, int> beamGroups, int prevIdx, int curIdx, float externalGap)
            => ShareLayoutBeamGroup(beamGroups, prevIdx, curIdx) ? BeamedInternalInkGap : externalGap;

        private float ComputeMeasureMinWidth(
            List<GeneratedNote> notes,
            MeasureSegment segment,
            double beatOrigin)
        {
            if (segment.NoteIndices.Count == 0)
                return BarLeftPadding + 16f;

            var sorted = SortIndicesByBeat(notes, segment.NoteIndices, beatOrigin);
            var beamGroups = ComputeLayoutBeamGroupIds(notes, sorted, beatOrigin, segment.EndBeat);
            float width = BarLeftPadding;
            var accHistory = new Dictionary<(char, int), Accidental>();
            var barCancelled = new HashSet<(char, int)>();

            for (int k = 0; k < sorted.Count; k++)
            {
                int i = sorted[k];
                var note = notes[i];
                var acc = ResolveLayoutAccidental(note, accHistory, barCancelled);

                if (k == 0)
                {
                    double relBeat = (note.BeatPosition ?? 0.0) - beatOrigin - segment.StartBeat;
                    float startPad = BarStemClearance + (relBeat < 1e-6 ? MeasureStartExtraPad : 0f);
                    width += NoteCenterLeftReach(note.IsRest, acc.HasAccidental, acc.IsFlat, acc.IsNatural) + startPad;
                }
                else
                {
                    width += InkGapBetween(beamGroups, sorted[k - 1], i, MinNoteGap(note.IsRest))
                             + NoteCenterLeftReach(note.IsRest, acc.HasAccidental, acc.IsFlat, acc.IsNatural);
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
                    center = NoteCenterLeftReach(note.IsRest, acc.HasAccidental, acc.IsFlat, acc.IsNatural);
                }
                else
                {
                    float gap = InkGapBetween(beamGroups, sorted[k - 1], i, _planInkGap);
                    center = MinCenterAfterPrevRight(prevRight, note.IsRest, acc.HasAccidental, acc.IsFlat, acc.IsNatural, gap);
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
                    float minLeft = prevRight + inkGap;
                    float curLeft = NoteGroupLeft(noteLayouts[i].X, isRest, hasAcc, isFlat, isNatural);
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
            CompressMeasureNoteSpan(notes, noteLayouts, sorted, compressTarget, beatOrigin, measureEndBeat);
            EnforceMeasureNoteGaps(notes, noteLayouts, sorted);
            ClampMeasureTrailingBeforeBar(notes, noteLayouts, sorted, maxTrailing, float.NegativeInfinity,
                beatOrigin, measureEndBeat);
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
        /// Uniform measure scale that fits trailing ink inside <paramref name="available"/> width.
        /// Avoids <see cref="Math.Clamp(Single,Single,Single)"/> when min packed span ≈ current span (fp).
        /// </summary>
        private static float ComputeMeasureFitScale(float available, float span, float minSpan)
        {
            if (span <= 1e-3f)
                return 1f;

            float raw = available / span;
            if (available <= 0f)
                return Math.Min(1f, raw);

            // When even MinInkGap packing cannot fit the measure, compress below MinInkGap.
            if (minSpan > available + 0.5f)
                return Math.Clamp(raw, 0.25f, 1f);

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
            float inkGap = MinInkGap,
            IReadOnlyList<double>? barBeats = null)
            => EnforceGroupOrderSpacing(notes, noteLayouts, beatOrigin, inkGap, barBeats);

        /// <summary>Beat-order spacing that never resets at bar lines (prevents cross-measure overlap).</summary>
        private void EnforceStrictBeatOrderSpacing(
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
                    center = innerLeft + NoteCenterLeftReach(note.IsRest, acc.HasAccidental, acc.IsFlat, acc.IsNatural);
                    if (isFirstMeasureOnStaff && k == 0)
                    {
                        float headerEdge = _planUseFullHeader
                            ? _headerMetrics.TimeSigRightRel
                            : _planStaffLeftMargin;
                        center = Math.Max(center, headerEdge + NoteCenterLeftReach(note.IsRest, acc.HasAccidental, acc.IsFlat, acc.IsNatural));
                    }
                }
                else
                {
                    center = MinCenterAfterPrevRight(prevRight, note.IsRest, acc.HasAccidental, acc.IsFlat, acc.IsNatural);
                }

                noteLayouts[i] = new NoteLayout
                {
                    X = center,
                    AccidentalX = center,
                    HasAccidental = acc.HasAccidental,
                    AccidentalIsFlat = acc.IsFlat,
                    AccidentalIsNatural = acc.IsNatural,
                    IsRest = note.IsRest
                };
                SyncAccidentalX(noteLayouts, i);
                prevRight = NoteTrailingRight(note, center);
            }

            float maxTrailing = barLineX - BarLeftPadding;
            ClampMeasureTrailingBeforeBar(notes, noteLayouts, sorted, maxTrailing,
                float.NegativeInfinity, beatOrigin, measureEndBeat);
        }

        /// <summary>
        /// Beat-proportional placement: equal measure widths, note/rest centers at beat-slot
        /// midpoints, beam groups reserve external width only, uniform header shift when needed.
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
                    ? _headerMetrics.TimeSigRightRel
                    : _planStaffLeftMargin;
                for (int k = 0; k < sorted.Count; k++)
                {
                    float headerMin = headerEdge + NoteCenterLeftReach(isRestList[k], hasAccList[k], isFlatList[k], isNaturalList[k])
                                      + StaffStartExtraPad;
                    if (centers[k] < headerMin)
                        centers[k] = headerMin;
                }
            }

            if (sorted.Count > 0)
            {
                int lastK = sorted.Count - 1;
                int lastIdx = indices[lastK];
                float trailing = NoteTrailingRight(notes[lastIdx], centers[lastK]);
                float span = trailing - laneLeft;
                float available = laneRight - laneLeft;
                if (span > available + 0.5f && span > 1f)
                {
                    float fitScale = Math.Clamp(available / span, 0.35f, 1f);
                    for (int k = 0; k < centers.Length; k++)
                        centers[k] = laneLeft + (centers[k] - laneLeft) * fitScale;
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
                    IsRest = isRestList[k]
                };
                SyncAccidentalX(noteLayouts, i);
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
            float startCenter = innerLeft + NoteCenterLeftReach(firstNote.IsRest, firstAcc.HasAccidental, firstAcc.IsFlat, firstAcc.IsNatural);
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

                var acc = k == 0
                    ? firstAcc
                    : ResolveLayoutAccidental(note, accHistory, barCancelled);

                float idealX = startCenter + frac * spread;

                if (relBeat < 1e-6)
                {
                    float onBarMin = measureLeft + BarLeftPadding + MeasureStartExtraPad + BarStemClearance
                                     + NoteCenterLeftReach(note.IsRest, acc.HasAccidental, acc.IsFlat, acc.IsNatural);
                    idealX = Math.Max(idealX, onBarMin);

                    if (isFirstMeasureOnStaff && k == 0)
                    {
                        float headerEdge = _planUseFullHeader
                            ? _headerMetrics.TimeSigRightRel
                            : _planStaffLeftMargin;
                        float headerMin = headerEdge + NoteCenterLeftReach(note.IsRest, acc.HasAccidental, acc.IsFlat, acc.IsNatural);
                        idealX = Math.Max(idealX, headerMin);
                    }
                }

                if (prevRight > float.NegativeInfinity)
                {
                    float minCenter = MinCenterAfterPrevRight(prevRight, note.IsRest, acc.HasAccidental, acc.IsFlat, acc.IsNatural);
                    idealX = Math.Max(idealX, minCenter);
                }

                noteLayouts[i] = new NoteLayout
                {
                    X = idealX,
                    AccidentalX = idealX,
                    HasAccidental = acc.HasAccidental,
                    AccidentalIsFlat = acc.IsFlat,
                    AccidentalIsNatural = acc.IsNatural,
                    IsRest = note.IsRest
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
                float equalShare = availableWidth / Math.Max(1, segments.Count);
                for (int m = 0; m < segments.Count; m++)
                {
                    double segBeats = segments[m].EndBeat - segments[m].StartBeat;
                    if (segBeats < 1e-9)
                        segBeats = 1;
                    float beatLaneMin = (float)segBeats * (_layout.NoteHeadR * 2.2f + _planInkGap * 0.45f)
                                        + BarLeftPadding * 2f;
                    minWidths[m] = Math.Max(
                        equalShare,
                        Math.Max(beatLaneMin, ComputeMeasureMinWidth(notes, segments[m], beatOrigin)));
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
                    _planInkGap = MinInkGap * (availableWidth / sumMin);
                    for (int m = 0; m < segments.Count; m++)
                        minWidths[m] = ComputeMeasureMinWidth(notes, segments[m], beatOrigin);
                }

                measureWidths = AllocateMeasureWidths(segments, minWidths, availableWidth);
            }

            var noteLayouts = new NoteLayout[notes.Count];
            var barList = new List<BarLayout>();
            float x = staffLeftMargin;

            for (int m = 0; m < segments.Count; m++)
            {
                if (beginner)
                    LayoutBeginnerNotesInMeasure(notes, noteLayouts, segments[m], beatOrigin, x, measureWidths[m], m == 0);
                else
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

            if (!beginner)
            {
                EnforceGlobalBeatOrderSpacing(notes, noteLayouts, beatOrigin, _planInkGap);
                EnforceStrictBeatOrderSpacing(notes, noteLayouts, beatOrigin, MinInkGap);
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

            if (dirtyRect.Width < 32f || dirtyRect.Height < 32f)
                return;

            if (UpperNotes.Count == 0 && LowerNotes.Count == 0)
            {
                if (_session.Tune == "Tuner")
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
            var insets = _safeArea?.GetSafeAreaInsets() ?? (0f, 0f, 0f, 0f);
            float viewRight = dirtyRect.X + dirtyRect.Width;
            float effectiveRightInset = Math.Max(0f, insets.Right - RelaxCutoutInsetRightDp);
            float safeLeft = dirtyRect.X + insets.Left;
            float safeRight = viewRight - effectiveRightInset;
            float layoutRightLimit = safeRight - LayoutRightPad;
            float safeWidth = layoutRightLimit - safeLeft;

            var layoutCacheKey = BuildLayoutCacheKey(dirtyRect.Width, dirtyRect.Height, insets.Left, insets.Right);
            if (TryDrawFromLayoutCache(canvas, dirtyRect, ink, layoutCacheKey))
                return;

            StaffLog($"[Staff] Canvas={dirtyRect.Width:F0}x{dirtyRect.Height:F0}, " +
                  $"Insets=L{insets.Left:F0},R{insets.Right:F0}, relaxR={RelaxCutoutInsetRightDp:F0}, " +
                  $"ViewRight={viewRight:F0}, SafeRight={safeRight:F0}, LayoutLimit={layoutRightLimit:F0}, " +
                  $"SafeWidth={safeWidth:F0}");

            // Step 1: Compute vertical layout
            ComputeLayout(dirtyRect.Height);
            _headerMetrics = ComputeHeaderMetrics(safeLeft);
            _leftMargin = _headerMetrics.LeftMargin;
            // Independent staves: lower omits key/time sig (clef-only margin) but shares key/meter with upper.
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
                    upperTop, upperMid, upperBot, lowerTop, lowerMid, lowerBot);

                SanitizeLayoutPositions(upperNoteLayouts, upperBarLayouts);
                SanitizeLayoutPositions(lowerNoteLayouts, lowerBarLayouts);

                LogStaffLayoutDiagnostics("Upper", UpperNotes, upperNoteLayouts, upperPreScaleX,
                    upperTop, upperMid, upperBot);
                LogStaffLayoutDiagnostics("Lower", LowerNotes, lowerNoteLayouts, lowerPreScaleX,
                    lowerTop, lowerMid, lowerBot);

                LogBeginnerLayoutBounds(upperNoteLayouts, upperBarLayouts, lowerNoteLayouts, lowerBarLayouts,
                    layoutRightLimit, safeRight);

                double upperBeatOriginBeginner = GetStaffBeatOrigin(UpperNotes, UpperBarBeats);
                double lowerBeatOriginBeginner = GetStaffBeatOrigin(LowerNotes, LowerBarBeats);
                StoreLayoutCache(layoutCacheKey, dirtyRect, ink,
                    upperTop, upperMid, upperBot, lowerTop, lowerMid, lowerBot,
                    upperNoteLayouts, upperBarLayouts, lowerNoteLayouts, lowerBarLayouts,
                    upperBeatOriginBeginner, lowerBeatOriginBeginner,
                    safeLeft, safeRight, layoutRightLimit, upperStaffMargin, lowerStaffMargin);
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
                StaffLog($"[Staff] Compression needed: contentWidth={maxContentWidth:F0}, " +
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

            RefinishMeasureSpacing(UpperNotes, upperNoteLayouts, upperBarLayouts, UpperBarBeats, upperBeatOrigin);
            RefinishMeasureSpacing(LowerNotes, lowerNoteLayouts, lowerBarLayouts, LowerBarBeats, lowerBeatOrigin);
            EnforceGlobalBeatOrderSpacing(UpperNotes, upperNoteLayouts, upperBeatOrigin, MinInkGap, UpperBarBeats);
            EnforceGlobalBeatOrderSpacing(LowerNotes, lowerNoteLayouts, lowerBeatOrigin, MinInkGap, LowerBarBeats);

            if (!compressed)
            {
                _planInkGap = upperPlanInkGap;
                EnforceGlobalBeatOrderSpacing(UpperNotes, upperNoteLayouts, upperBeatOrigin, upperPlanInkGap, UpperBarBeats);
                _planInkGap = lowerPlanInkGap;
                EnforceGlobalBeatOrderSpacing(LowerNotes, lowerNoteLayouts, lowerBeatOrigin, lowerPlanInkGap, LowerBarBeats);
            }

            NudgeNotesClearOfBarlines(UpperNotes, upperNoteLayouts, upperBarLayouts, UpperBarBeats, upperBeatOrigin, upperTop, upperMid, upperBot);
            NudgeNotesClearOfBarlines(LowerNotes, lowerNoteLayouts, lowerBarLayouts, LowerBarBeats, lowerBeatOrigin, lowerTop, lowerMid, lowerBot);
            ReconcileFinalBarLayout(upperNoteLayouts, upperBarLayouts);
            ReconcileFinalBarLayout(lowerNoteLayouts, lowerBarLayouts);

            ClampLayoutToSafeRight(upperNoteLayouts, upperBarLayouts, layoutRightLimit, safeLeft, upperStaffMargin);
            ClampLayoutToSafeRight(lowerNoteLayouts, lowerBarLayouts, layoutRightLimit, safeLeft, lowerStaffMargin);
            RefinishMeasureSpacing(UpperNotes, upperNoteLayouts, upperBarLayouts, UpperBarBeats, upperBeatOrigin);
            RefinishMeasureSpacing(LowerNotes, lowerNoteLayouts, lowerBarLayouts, LowerBarBeats, lowerBeatOrigin);
            ClampLayoutToSafeRight(upperNoteLayouts, upperBarLayouts, layoutRightLimit, safeLeft, upperStaffMargin);
            ClampLayoutToSafeRight(lowerNoteLayouts, lowerBarLayouts, layoutRightLimit, safeLeft, lowerStaffMargin);

            if (!compressed)
            {
                ExpandLayoutToFillSafeRight(upperNoteLayouts, upperBarLayouts, layoutRightLimit, safeLeft, upperStaffMargin);
                ExpandLayoutToFillSafeRight(lowerNoteLayouts, lowerBarLayouts, layoutRightLimit, safeLeft, lowerStaffMargin);
                _planInkGap = upperPlanInkGap;
                EnforceGlobalBeatOrderSpacing(UpperNotes, upperNoteLayouts, upperBeatOrigin, upperPlanInkGap, UpperBarBeats);
                _planInkGap = lowerPlanInkGap;
                EnforceGlobalBeatOrderSpacing(LowerNotes, lowerNoteLayouts, lowerBeatOrigin, lowerPlanInkGap, LowerBarBeats);
            }

            ReconcileFinalBarLayout(upperNoteLayouts, upperBarLayouts);
            ReconcileFinalBarLayout(lowerNoteLayouts, lowerBarLayouts);
            PadLayoutGutterToLimit(upperNoteLayouts, upperBarLayouts, layoutRightLimit);
            PadLayoutGutterToLimit(lowerNoteLayouts, lowerBarLayouts, layoutRightLimit);
            ReconcileFinalBarLayout(upperNoteLayouts, upperBarLayouts);
            ReconcileFinalBarLayout(lowerNoteLayouts, lowerBarLayouts);
            AlignIndependentStaffEndBars(upperBarLayouts, upperNoteLayouts, lowerBarLayouts, lowerNoteLayouts, layoutRightLimit);
            ClampLayoutToSafeRight(upperNoteLayouts, upperBarLayouts, layoutRightLimit, safeLeft, upperStaffMargin);
            ClampLayoutToSafeRight(lowerNoteLayouts, lowerBarLayouts, layoutRightLimit, safeLeft, lowerStaffMargin);
            EnforceStrictBeatOrderSpacing(UpperNotes, upperNoteLayouts, upperBeatOrigin, MinInkGap);
            EnforceStrictBeatOrderSpacing(LowerNotes, lowerNoteLayouts, lowerBeatOrigin, MinInkGap);
            ClampLayoutToSafeRight(upperNoteLayouts, upperBarLayouts, layoutRightLimit, safeLeft, upperStaffMargin);
            ClampLayoutToSafeRight(lowerNoteLayouts, lowerBarLayouts, layoutRightLimit, safeLeft, lowerStaffMargin);
            ReconcileFinalBarLayout(upperNoteLayouts, upperBarLayouts);
            ReconcileFinalBarLayout(lowerNoteLayouts, lowerBarLayouts);
            AlignIndependentStaffEndBars(upperBarLayouts, upperNoteLayouts, lowerBarLayouts, lowerNoteLayouts, layoutRightLimit);

            FinalizeStaffBarClearance(UpperNotes, upperNoteLayouts, upperBarLayouts, UpperBarBeats, upperBeatOrigin, upperTop, upperMid, upperBot);
            FinalizeStaffBarClearance(LowerNotes, lowerNoteLayouts, lowerBarLayouts, LowerBarBeats, lowerBeatOrigin, lowerTop, lowerMid, lowerBot);

            SanitizeLayoutPositions(upperNoteLayouts, upperBarLayouts);
            SanitizeLayoutPositions(lowerNoteLayouts, lowerBarLayouts);

            EnforceStrictBeatOrderSpacing(UpperNotes, upperNoteLayouts, upperBeatOrigin, MinInkGap);
            EnforceStrictBeatOrderSpacing(LowerNotes, lowerNoteLayouts, lowerBeatOrigin, MinInkGap);

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

            StoreLayoutCache(layoutCacheKey, dirtyRect, ink,
                upperTop, upperMid, upperBot, lowerTop, lowerMid, lowerBot,
                upperNoteLayouts, upperBarLayouts, lowerNoteLayouts, lowerBarLayouts,
                upperBeatOrigin, lowerBeatOrigin,
                safeLeft, safeRight, layoutRightLimit, upperStaffMargin, lowerStaffMargin);
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

        /// <summary>Maps planned layout to screen for child levels 1–30 (single uniform scale if needed).</summary>
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
            float lowerBot)
        {
            ApplyBeginnerStaffToScreen(upperNoteLayouts, upperBarLayouts, safeLeft, upperStaffMargin,
                upperTotalWidth, upperUsableWidth);
            ApplyBeginnerStaffToScreen(lowerNoteLayouts, lowerBarLayouts, safeLeft, lowerStaffMargin,
                lowerTotalWidth, lowerUsableWidth);

            ExpandLayoutToFillSafeRight(upperNoteLayouts, upperBarLayouts, layoutRightLimit, safeLeft, upperStaffMargin);
            ExpandLayoutToFillSafeRight(lowerNoteLayouts, lowerBarLayouts, layoutRightLimit, safeLeft, lowerStaffMargin);
            PadLayoutGutterToLimit(upperNoteLayouts, upperBarLayouts, layoutRightLimit);
            PadLayoutGutterToLimit(lowerNoteLayouts, lowerBarLayouts, layoutRightLimit);

            ReconcileFinalBarLayout(upperNoteLayouts, upperBarLayouts);
            ReconcileFinalBarLayout(lowerNoteLayouts, lowerBarLayouts);
            AlignIndependentStaffEndBars(upperBarLayouts, upperNoteLayouts, lowerBarLayouts, lowerNoteLayouts, layoutRightLimit);
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
            float contentSpan = totalWidth - staffLeftMargin;
            float scale = contentSpan > usableWidth && contentSpan > 0f
                ? usableWidth / contentSpan
                : 1f;
            MapStaffLayoutToScreen(noteLayouts, barLayouts, safeLeft, staffLeftMargin, scale);
        }

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
        /// Lines up only the final bar between independent staves; internal bars stay per-staff.
        /// </summary>
        private void AlignIndependentStaffEndBars(
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

            StaffLog($"[Staff] Expand: span {contentSpan:F0} → {targetSpan:F0}, ×{stretch:F3}");

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

        /// <summary>
        /// Bar lines on a regular meter grid from accumulated beat duration.  Measure-index
        /// transitions are used only when they place more boundaries (e.g. pickup/anacrusis).
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
            if (fromMeasures.Count > 0 && regular.Count > fromMeasures.Count)
            {
                StaffLog(
                    $"[LayoutTest] Bar beats: regular grid ({regular.Count}) replaces sparse measure-index ({fromMeasures.Count})");
            }
#endif

            // Prefer beat-duration meter grid when measure-index transitions skip boundaries
            // (common after two-staff splits where only a note subset is on one staff).
            if (regular.Count >= fromMeasures.Count && regular.Count > 0)
                return regular;

            if (fromMeasures.Count > 0)
                return fromMeasures;

            return regular;
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
                if (m == 0)
                {
                    EnforceMeasureNoteGaps(noteList, noteLayouts, sorted);
                    ClampMeasureTrailingBeforeBar(noteList, noteLayouts, sorted,
                        measureRight - BarLeftPadding, float.NegativeInfinity, beatOrigin, segments[m].EndBeat);
                }
                else
                {
                    var firstNote = noteList[sorted[0]];
                    var firstAccHistory = new Dictionary<(char, int), Accidental>();
                    var firstBarCancelled = new HashSet<(char, int)>();
                    var firstAcc = ResolveLayoutAccidental(firstNote, firstAccHistory, firstBarCancelled);
                    float firstReach = NoteCenterLeftReach(firstNote.IsRest, firstAcc.HasAccidental, firstAcc.IsFlat, firstAcc.IsNatural);
                    float packInnerLeft = minGroupLeft - firstReach;

                    LayoutNotesSequentialInMeasure(noteList, noteLayouts, sorted, packInnerLeft, measureRight, false,
                        beatOrigin, segments[m].EndBeat);
                    ClampMeasureTrailingBeforeBar(noteList, noteLayouts, sorted,
                        measureRight - BarLeftPadding, minGroupLeft, beatOrigin, segments[m].EndBeat);
                }

                int lastIdx = sorted[^1];
                prevMeasureTrailing = NoteTrailingRight(notes[lastIdx], noteLayouts[lastIdx].X);
            }
        }

        /// <summary>Ensures ink clears internal bar lines on both sides of each measure boundary.</summary>
        private void EnforceMeasureBarInkMargins(
            IReadOnlyList<GeneratedNote> notes,
            NoteLayout[] noteLayouts,
            BarLayout[] barLayouts,
            IReadOnlyList<double> barBeats,
            double beatOrigin)
        {
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

        private float StemStrokeHalfWidth => _layout.GlyphScale;

        private float NoteHeadOuterR => _layout.NoteHeadR + StemStrokeHalfWidth;

        /// <summary>Draw radius for noteheads; filled and half-note heads share the same outside dimensions.</summary>
        private float NoteHeadDrawR(bool filled) => NoteHeadOuterR;

        /// <summary>
        /// Stem attach at notehead center height: up-stems — outer (right) stroke edge tangent to outer right boundary;
        /// down-stems — outer (left) stroke edge tangent to outer left boundary.
        /// </summary>
        private (float stemX, float stemY) GetStemAttachPoint(float noteX, float noteY, bool stemUp)
        {
            float halfStroke = StemStrokeHalfWidth;
            float outerRight = noteX + NoteHeadOuterR;
            float outerLeft = noteX - NoteHeadOuterR;
            float stemX = stemUp ? outerRight - halfStroke : outerLeft + halfStroke;
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

            var insets = _safeArea?.GetSafeAreaInsets() ?? (0f, 0f, 0f, 0f);
            float safeLeft = dirtyRect.X + insets.Left;
            float viewRight = dirtyRect.X + dirtyRect.Width;
            float effectiveRightInset = Math.Max(0f, insets.Right - RelaxCutoutInsetRightDp);
            float safeRight = viewRight - effectiveRightInset;
            float layoutRightLimit = safeRight - LayoutRightPad;

            ComputeLayout(dirtyRect.Height);
            _headerMetrics = ComputeHeaderMetrics(safeLeft);

            float upperTop = _layout.UpperTop;
            float upperMid = _layout.UpperMid;
            float upperBot = _layout.UpperBot;

            DrawStaffHeaderChrome(canvas, ink, upperTop, upperMid, upperBot, drawKeyAndTimeSig: false);

            const float safeEdgePad = 4f;
            float lineEnd = Math.Max(
                safeLeft + _headerMetrics.LeftMargin + 32f,
                layoutRightLimit - safeEdgePad);
            lineEnd = Math.Min(lineEnd, safeRight - safeEdgePad);

            canvas.StrokeColor = ink;
            canvas.StrokeSize = 1.5f;
            for (int i = 0; i < 5; i++)
            {
                float y = upperTop + i * _layout.Sls;
                canvas.DrawLine(safeLeft, y, lineEnd, y);
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

            canvas.StrokeColor = ink;
            canvas.StrokeSize = 1.5f;
            for (int i = 0; i < 5; i++)
            {
                float y = staffTop + i * _layout.Sls;
                canvas.DrawLine(safeLeft, y, staffLineEndX, y);
            }

            canvas.StrokeColor = ink;
            foreach (var bar in barLayouts)
            {
                if (!float.IsFinite(bar.X))
                    continue;

                if (bar.IsDouble)
                {
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
            bool drawKeyAndTimeSig)
        {
            canvas.SaveState();
            canvas.FontColor = ink;
            canvas.FontSize = _layout.Sls * 5f;
            float clefH = staffBot - staffTop + _layout.Sls * 3.2f;
            canvas.DrawString("𝄞", _headerMetrics.ClefX, staffTop, _headerMetrics.ClefWidth, clefH,
                HorizontalAlignment.Left, VerticalAlignment.Top);
            canvas.RestoreState();

            if (drawKeyAndTimeSig)
            {
                DrawMusicBpmMarking(canvas, ink, staffTop);
                float keySigEndX = DrawKeySignature(canvas, staffTop, staffMid, ink);
                if (_session.Tune != "Tuner")
                    DrawTimeSignature(canvas, staffTop, staffMid, ink, keySigEndX + KeySigTimeSigGap);
            }
        }

        /// <summary>Quarter-note = MusicBpm marking above the upper staff; "=" centered over the time signature.</summary>
        private void DrawMusicBpmMarking(ICanvas canvas, Color ink, float staffTop)
        {
            if (_session.Tune == "Tuner")
                return;

            int bpm = Math.Clamp(_session.MusicBpm, 30, 200);
            float fontSize = Math.Max(10f, _layout.Sls * 1.6f);
            const float timeSigW = 24f;
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
            float staffLeftMargin)
        {
            if (notes.Count == 0 || noteLayouts.Length == 0)
                return;
            if (notes.Count != noteLayouts.Length)
            {
                StaffLog($"[Staff] Skipping staff draw: {notes.Count} notes vs {noteLayouts.Length} layouts");
                return;
            }

            if (_session.ShowConductorCues && _session.Tune != "Tuner"
                && ConductorBeatHelper.TryParseDisplayTimeSignature(_session.GetDisplayTimeSignature(), out var conductorTs))
            {
                double? highlightedBeat = GetHighlightedConductedBeatRel(
                    notes, currentIdx, beatOrigin, barBeats, conductorTs, isActive);
                DrawConductorBeatCues(canvas, staffTop, barLayouts, barBeats, beatOrigin,
                    staffLeftMargin, conductorTs, highlightedBeat);
            }

            barBeats ??= Array.Empty<double>();
            float headerRightAbs = safeLeft + staffLeftMargin - _layout.NoteHeadR;
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
                    continue;

                var state = (states.Length > i) ? states[i] : StaffNoteState.Pending;
                double beat = (note.BeatPosition ?? 0.0) - beatOrigin;

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
                    DrawNote(canvas, note.Duration, layout.X, ny, staffTop, staffBot, ink, state, fadeAlpha,
                             forceStemUp, isBeamed, stemEndOverride,
                             out float stemTipX, out float stemTipY);

                    if (isBeamed)
                        beamStemTips[i] = (stemTipX, stemTipY, GetNoteColor(state, ink, fadeAlpha), note.Duration);

                    DrawLedgerLines(canvas, note, layout.X, staffTop, staffBot, ink, fadeAlpha);
                    DrawAccidental(canvas, note, layout, ny, ink, accHistory, barCancelledAccidentals, fadeAlpha, headerRightAbs);

                    var nameDisplay = _session.NoteNameDisplay;
                    bool showName = nameDisplay == "All notes"
                        || (nameDisplay == "Current only" && state == StaffNoteState.Current);
                    if (showName)
                        DrawNoteName(canvas, note, layout.X, ny, staffTop, staffBot, ink, fadeAlpha);
                }

                prevBeat = beat;
            }

            // Draw beams using pre-computed stem positions
            DrawBeams(canvas, beamGroups, beamStemTips, barLayouts);
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

        private void DrawConductorBeatCues(
            ICanvas canvas,
            float staffTop,
            BarLayout[] barLayouts,
            IReadOnlyList<double> barBeats,
            double beatOrigin,
            float staffLeftMargin,
            TimeSignature timeSignature,
            double? highlightedConductedBeatRel)
        {
            if (barLayouts.Length == 0)
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

                foreach (double offset in conductedOffsets)
                {
                    double conductedBeatRel = segmentStartBeat + offset;
                    float x = ConductorBeatHelper.BeatOffsetToXInMeasure(
                        measureLeft, measureRight, offset, measureBeats, BarLeftPadding);
                    bool isCurrent = highlightedConductedBeatRel.HasValue
                        && Math.Abs(highlightedConductedBeatRel.Value - conductedBeatRel) < 0.05;
                    DrawConductorArrow(canvas, x, staffTop, isCurrent);
                }
            }
        }

        private static void DrawConductorArrow(ICanvas canvas, float x, float staffTop, bool isCurrent)
        {
            float wing = isCurrent ? 5.5f : 4f;
            float height = isCurrent ? 8f : 5.5f;
            float tipY = staffTop - (isCurrent ? 1f : 4f);
            float baseY = tipY - height;

            canvas.StrokeColor = Colors.Red;
            canvas.StrokeSize = isCurrent ? 2.5f : 1.8f;
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
            /// <summary>Offset from <c>safeLeft</c> to the first note beat-0 anchor (center X).</summary>
            public float LeftMargin { get; init; }
            /// <summary>Clef-only anchor for lower staff (no key/time repeat).</summary>
            public float ClefOnlyLeftMargin { get; init; }
        }

        private StaffHeaderMetrics ComputeHeaderMetrics(float safeLeft)
        {
            const float clefPad = 2f;
            const float timeSigW = 24f;

            float clefWidth = _layout.Sls * 4.5f;
            float clefOnlyRightRel = clefPad + clefWidth;
            float clefOnlyLeftMargin = clefOnlyRightRel + _layout.NoteHeadR + _layout.NoteHeadR;

            string key = _session.Key;
            string scale = ActiveKeySignatureScale();
            bool suppressKeySig = _session.Tune == "Tuner"
                || _session.Tune == "Practice Tune"
                || (_session.Tune != "Arpeggio" && _session.SelectedScale == "Chromatic");
            int accCount = suppressKeySig ? 0 : KeySignatureRules.GetAccidentalCount(key, scale);

            float clefX = safeLeft + clefPad;
            float keySigStartX = safeLeft + clefWidth;
            float keySigEndX = keySigStartX + KeySigDrawnWidth(accCount);
            float timeSigX = keySigEndX + KeySigTimeSigGap;
            float timeSigRightRel = timeSigX - safeLeft + timeSigW;
            // Gap from time sig to first item (note/rest/accidental) ≈ one note-head width.
            bool suppressTimeSig = _session.Tune == "Tuner";
            float leftMargin = suppressTimeSig
                ? clefOnlyLeftMargin
                : timeSigRightRel + _layout.NoteHeadR + _layout.NoteHeadR;

            return new StaffHeaderMetrics
            {
                ClefX = clefX,
                ClefWidth = clefWidth,
                KeySigStartX = keySigStartX,
                KeySigEndX = keySigEndX,
                TimeSigX = timeSigX,
                TimeSigRightRel = timeSigRightRel,
                LeftMargin = leftMargin,
                ClefOnlyLeftMargin = clefOnlyLeftMargin
            };
        }

        // ── Note geometry ─────────────────────────────────────────────────────────

        private float NoteY(GeneratedNote note, float staffTop, float staffMid)
        {
            int steps = DiatonicStepsFromB4(note.Letter, note.Octave);
            return staffMid + steps * _layout.HS;
        }

        private float KeySigLineY(char letter, int octave, float staffMid)
            => staffMid + DiatonicStepsFromB4(letter, octave) * _layout.HS;

        private float KeySigAccidentalFontSize(bool isFlat)
            => _layout.Sls * 2.4f * KeySigAccidentalScale() * ArpeggioKeySigSizeBoost() * (isFlat ? KeySigFlatSizeBoost : 1f);

        private float BodyAccidentalFontSize(bool isFlat = false, bool isNatural = false)
            => _layout.Sls * 2.4f * BodyAccidentalGlyphScale()
               * (isFlat ? BodyFlatSizeBoost : 1f)
               * (isNatural ? BodyNaturalSizeBoost : 1f);

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

        // ── Drawing primitives ────────────────────────────────────────────────────

        private static Color ApplyAlpha(Color c, byte alpha)
            => Color.FromRgba(c.Red, c.Green, c.Blue, alpha / 255f);

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
                Color noteColor;
                switch (state)
                {
                    case StaffNoteState.Current:
                        noteColor = ApplyAlpha(Colors.Gold, fadeAlpha);  //  2026.06.13 1601  Color.FromArgb("#007BFF"), fadeAlpha);
                        break;
                    case StaffNoteState.Correct:
                        noteColor = ApplyAlpha(Color.FromArgb("#22AA44"), fadeAlpha);
                        break;
                    case StaffNoteState.Wrong:
                        noteColor = ApplyAlpha(Color.FromArgb("#CC2222"), fadeAlpha);
                        break;
                    default:
                        noteColor = ApplyAlpha(Colors.Black, (byte)(fadeAlpha * 0.85f));
                        break;
                }

                float stroke = 2f * _layout.GlyphScale;
                canvas.StrokeColor = noteColor;
                canvas.StrokeSize = stroke;

                bool filled = duration != NoteDuration.Whole && duration != NoteDuration.Half;
                float drawR = NoteHeadDrawR(filled);
                float headTop = y - drawR * NoteHeadHeightFactor * 0.5f;
                float headW = drawR * 2f;
                float headH = drawR * NoteHeadHeightFactor;
                if (filled)
                {
                    canvas.FillColor = noteColor;
                    canvas.FillEllipse(x - drawR, headTop, headW, headH);
                }
                else
                {
                    canvas.FillColor = noteColor;
                    canvas.FillEllipse(x - drawR, headTop, headW, headH);

                    float inset = Math.Min(stroke, drawR * 0.45f);
                    if (headW > inset * 2f && headH > inset * 2f)
                    {
                        canvas.FillColor = _theme.PanelBackgroundColor;
                        canvas.FillEllipse(
                            x - drawR + inset,
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
                float ledgerHW = _layout.NoteHeadR * 2.2f;

                canvas.StrokeColor = ApplyAlpha(ink, fadeAlpha);
                canvas.StrokeSize = 1.5f * _layout.GlyphScale;

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
                    Accidental.Sharp => "♯",
                    Accidental.Flat => "♭",
                    Accidental.Natural => "♮",
                    Accidental.DoubleSharp => "𝄪",
                    Accidental.DoubleFlat => "𝄫",
                    _ => ""
                };
                if (string.IsNullOrEmpty(glyph)) return;

                bool isFlat = IsFlatBodyAccidental(eff);
                bool isNatural = eff == Accidental.Natural;
                float symW = BodyAccidentalDrawWidth(isFlat, isNatural);
                float boxRight = AccidentalBoxRight(layout.X, isNatural);
                float boxLeft = boxRight - symW;
                float boxW = Math.Max(1f, symW);

                // Never paint body accidentals over this staff's header (clef, or clef+key+time).
                if (boxLeft < headerRightAbs)
                    return;

                canvas.FontColor = ApplyAlpha(ink, fadeAlpha);

                float bodyFont = isNatural
                    ? BodyAccidentalFontSize(isFlat: false, isNatural: true)
                    : BodyAccidentalFontSize(isFlat);

                if (isFlat)
                {
                    string smufl = eff == Accidental.DoubleFlat ? "\uE264" : "\uE260";
                    if (!TryDrawBodySmuFLAccidental(canvas, smufl, boxRight, y, ApplyAlpha(ink, fadeAlpha), isFlat: true, isNatural: false))
                    {
                        float symH = symW * 1.1f;
                        float yTop = y - symH * 0.5f;
                        canvas.FontSize = BodyAccidentalFontSize(isFlat: true);
                        canvas.DrawString(glyph, boxLeft, yTop, boxW, symH,
                            HorizontalAlignment.Right, VerticalAlignment.Center);
                    }
                }
                else if (isNatural)
                {
                    if (!TryDrawBodySmuFLAccidental(canvas, "\uE261", boxRight, y, ApplyAlpha(ink, fadeAlpha), isFlat: false, isNatural: true))
                    {
                        float symH = symW * 1.2f;
                        float yTop = y - symH * 0.5f;
                        canvas.FontSize = bodyFont;
                        canvas.DrawString(glyph, boxLeft, yTop, boxW, symH,
                            HorizontalAlignment.Right, VerticalAlignment.Center);
                    }
                }
                else
                {
                    // Sharps and double-sharps: use Bravura glyphs so positioning is
                    // consistent with flats and naturals.
                    string smufl = eff == Accidental.DoubleSharp ? "\uE263" : "\uE262";
                    if (!TryDrawBodySmuFLAccidental(canvas, smufl, boxRight, y, ApplyAlpha(ink, fadeAlpha), isFlat: false, isNatural: false))
                    {
                        float symH = symW * 1.05f;
                        float yTop = y - symH * 0.5f;
                        canvas.FontSize = bodyFont;
                        canvas.DrawString(glyph, boxLeft, yTop, boxW, symH,
                            HorizontalAlignment.Right, VerticalAlignment.Center);
                    }
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
            if (eff == Accidental.Natural)
            {
                if (sigAcc != null)
                    draw = true;
                else if (history != null && history.TryGetValue(pitchKey, out var prior)
                         && IsChromaticAccidental(prior))
                    draw = true;
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
            if (hasAcc)
                history[(note.Letter, note.Octave)] = eff;

            return new LayoutAccidentalInfo
            {
                HasAccidental = hasAcc,
                IsFlat = hasAcc && IsFlatBodyAccidental(eff),
                IsNatural = hasAcc && IsNaturalBodyAccidental(eff),
            };
        }

        private void DrawNoteName(ICanvas canvas, GeneratedNote note, float x, float ny,
                                   float staffTop, float staffBot, Color ink, byte fadeAlpha)
        {
            canvas.SaveState();
            try
            {
                canvas.FontColor = ApplyAlpha(ink, fadeAlpha);
                canvas.FontSize = 11;
                float labelY = ny > (staffTop + staffBot) / 2f
                    ? ny + _layout.NoteHeadR + 5f
                    : ny - _layout.NoteHeadR - 15f;
                canvas.DrawString(note.SpelledName, x - 14f, labelY, 28f, 14f,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
            }
            finally { canvas.RestoreState(); }
        }

        // ── Key / time signature drawing ──────────────────────────────────────────

        private string ActiveKeySignatureScale()
        {
            if (_session.Tune == "Arpeggio")
                return "Major";
            return string.IsNullOrWhiteSpace(_session.EffectiveScale)
                ? _session.SelectedScale
                : _session.EffectiveScale;
        }

        private float DrawKeySignature(ICanvas canvas, float staffTop, float staffMid, Color ink)
        {
            float symW = KeySigGlyphWidth();
            float symSlot = KeySigSymbolSlot();

            if (_session.Tune == "Tuner"
                || _session.Tune == "Practice Tune"
                || (_session.Tune != "Arpeggio" && _session.SelectedScale == "Chromatic"))
                return _headerMetrics.KeySigStartX;

            int accCount = KeySignatureRules.GetAccidentalCount(_session.Key, ActiveKeySignatureScale());
            if (accCount == 0)
                return _headerMetrics.KeySigStartX;

            StaffLog($"[Staff] KeySig key={_session.Key} scale={_session.SelectedScale} count={accCount}");

            bool useFlats = KeySignatureRules.KeySignatureUsesFlats(_session.Key, ActiveKeySignatureScale());
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
                                       Color ink, float keySigEndX)
        {
            if (_session.Tune == "Tuner")
                return;

            canvas.SaveState();
            try
            {
                string timeSig = _session.GetDisplayTimeSignature();
                var parts = timeSig.Split('/');
                if (parts.Length != 2) return;

                float tsX = keySigEndX;
                float boxW = 24f;

                float tsFontSize = Math.Max(10f, _layout.Sls * 1.83f);  // 22 at sls=12
                canvas.FontColor = ink;
                canvas.FontSize = tsFontSize;
                canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;

                float halfH = _layout.Sls * 2f;
                float topY = staffTop + (halfH - tsFontSize) * 0.5f;
                float bottomY = staffMid + (halfH - tsFontSize) * 0.5f;

                canvas.DrawString(parts[0], tsX, topY, boxW, tsFontSize, HorizontalAlignment.Center, VerticalAlignment.Top);
                canvas.DrawString(parts[1], tsX, bottomY, boxW, tsFontSize, HorizontalAlignment.Center, VerticalAlignment.Top);
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            }
            finally { canvas.RestoreState(); }
        }

        private string? GetSignatureAccidentalForLetter(char letter)
            => KeySignatureRules.GetSignatureAccidentalForLetter(letter, _session.Key, ActiveKeySignatureScale());

        private bool IsAccidentalInKeySig(Accidental accidental, char letter)
        {
            if (accidental == Accidental.None || accidental == Accidental.Natural) return false;

            string scale = ActiveKeySignatureScale();
            bool useFlats = KeySignatureRules.KeySignatureUsesFlats(_session.Key, scale);
            bool typeMatch = useFlats
                ? accidental == Accidental.Flat
                : accidental == Accidental.Sharp;
            if (!typeMatch) return false;

            return KeySignatureRules.IsLetterInKeySignature(letter, _session.Key, scale);
        }

        /// <summary>
        /// Returns true if this note's letter is governed by the key signature
        /// (i.e. the key sig applies a sharp or flat to this pitch-class),
        /// regardless of the accidental currently on the note.
        /// Used to detect when a key-sig note is given a different accidental,
        /// cancelling the key sig within the bar.
        /// </summary>
        private bool IsNoteInKeySig(GeneratedNote note)
            => KeySignatureRules.IsLetterInKeySignature(note.Letter, _session.Key, _session.SelectedScale);

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

            // ── Instrument transposition: written key → concert key ────────────────
            // TransposeOffset convention (negative = instrument sounds lower than written):
            //   Bb clarinet = -2, Eb alto sax = -9, F horn = -7
            // GetConcertKey() = TransposeKey(writtenKey, offset), so
            //   TransposeKey("D", -2) should return "C"  (Bb clarinet written D → concert C)
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
                else if (!onUpper && !onLower)
                {
                    singleStaffOk = false;
                    Utilities.DebugTestLog.Write(
                        $"[MeasureLayoutTest] FAIL: measure {m + 1} missing from both staves");
                }
            }
            if (singleStaffOk)
                Utilities.DebugTestLog.Write("[MeasureLayoutTest] OK | no measure split across staves");

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
