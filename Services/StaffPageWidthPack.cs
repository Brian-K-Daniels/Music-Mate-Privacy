using musicmate.Drawables;
using musicmate.Models;

namespace musicmate.Services;

/// <summary>
/// One generated staff page retained so width settlement can re-pack without regenerating.
/// </summary>
public sealed class StaffPagePackState
{
    public required List<GeneratedNote> PageNotes { get; init; }
    public required List<double> PageBarBeats { get; init; }
    public required double MeasureBeats { get; init; }
    /// <summary>True when this page is a two-octave scale walk (peak-based staff split).</summary>
    public bool IsTwoOctaveScaleCut { get; init; }
    /// <summary>True when packed with <see cref="StaffPageWidthPolicy.FallbackWidthDip"/> because GraphicsView.Width was unknown.</summary>
    public bool IsProvisional { get; set; }
    /// <summary>Canvas width actually passed to <see cref="StaffDrawable.SplitMeasuresAcrossStaves"/>.</summary>
    public float PackedCanvasWidth { get; set; }
    public float PackedCanvasHeight { get; set; }
}

/// <summary>
/// Policy for provisional vs settled staff packing. Visual-only width repacks must not regenerate music.
/// </summary>
public static class StaffPageWidthPolicy
{
    public const float FallbackWidthDip = 360f;
    public const float FallbackHeightDip = 480f;
    public const float MaterialWidthDeltaDip = 2f;

    public static (float Width, float Height, bool IsProvisional) ResolveCanvasSize(
        double graphicsWidth,
        double graphicsHeight)
    {
        bool provisional = graphicsWidth <= 0;
        float width = provisional ? FallbackWidthDip : (float)graphicsWidth;
        float height = graphicsHeight > 0 ? (float)graphicsHeight : FallbackHeightDip;
        return (width, height, provisional);
    }

    /// <summary>
    /// Whether the current packed layout must be replaced at <paramref name="settledWidth"/>.
    /// </summary>
    public static bool NeedsRepackForSettledWidth(
        StaffPagePackState? state,
        double staffWidthUsedForLayout,
        double settledWidth)
    {
        if (state == null || settledWidth <= 0)
            return false;

        if (state.IsProvisional)
            return true;

        if (staffWidthUsedForLayout <= 0)
            return true;

        return Math.Abs(settledWidth - staffWidthUsedForLayout) >= MaterialWidthDeltaDip;
    }

    /// <summary>
    /// Result / freeze screens must keep their painted staff. Running sessions may repack visually.
    /// </summary>
    public static bool CanRepackNow(bool freezeStaff, bool holdResultForChildSession)
        => !freezeStaff && !holdResultForChildSession;

    /// <summary>
    /// Width stamped into <c>_staffWidthUsedForLayout</c> only after an actual pack/repack.
    /// </summary>
    public static double WidthRecordedAfterPack(float packedCanvasWidth)
        => packedCanvasWidth;

    /// <summary>
    /// Index of the scale-walk peak / turnaround note (last occurrence of the highest MIDI).
    /// Returns -1 when there are no pitched notes.
    /// </summary>
    public static int FindScaleWalkPeakNoteIndex(IReadOnlyList<GeneratedNote> notes)
    {
        int peakIdx = -1;
        int peakMidi = int.MinValue;
        for (int i = 0; i < notes.Count; i++)
        {
            if (notes[i].IsRest)
                continue;
            int m = notes[i].MidiNumber;
            if (m >= peakMidi)
            {
                peakMidi = m;
                peakIdx = i;
            }
        }

        return peakIdx;
    }

    /// <summary>
    /// Two-octave scale staff break: ascending through the peak on the upper staff,
    /// descending on the lower. Does not use Assortment balanced measure packing.
    /// Preserves the full generated sequence (no notes dropped).
    /// </summary>
    public static StaffDrawable.StaffMeasureSplitResult SplitTwoOctaveScaleAtPeak(
        IReadOnlyList<GeneratedNote> pageNotes)
    {
        var result = new StaffDrawable.StaffMeasureSplitResult();
        if (pageNotes == null || pageNotes.Count == 0)
            return result;

        int peakIdx = FindScaleWalkPeakNoteIndex(pageNotes);
        if (peakIdx < 0)
        {
            result.UpperNotes.AddRange(pageNotes);
            result.UpperMeasureCount = CountDistinctMeasures(pageNotes);
            result.TotalMeasureCount = result.UpperMeasureCount;
            return result;
        }

        for (int i = 0; i <= peakIdx; i++)
            result.UpperNotes.Add(pageNotes[i]);
        for (int i = peakIdx + 1; i < pageNotes.Count; i++)
            result.LowerNotes.Add(pageNotes[i]);

        result.UpperMeasureCount = CountDistinctMeasures(result.UpperNotes);
        result.LowerMeasureCount = CountDistinctMeasures(result.LowerNotes);
        result.TotalMeasureCount = CountDistinctMeasures(pageNotes);
        result.UnplacedMeasureCount = Math.Max(
            0,
            result.TotalMeasureCount - result.UpperMeasureCount - result.LowerMeasureCount);
        return result;
    }

    private static int CountDistinctMeasures(IReadOnlyList<GeneratedNote> notes)
    {
        var set = new HashSet<int>();
        foreach (var n in notes)
        {
            if (n.MeasureIndex is int mi)
                set.Add(mi);
        }

        if (set.Count > 0)
            return set.Count;

        // No MeasureIndex: treat as a single staff line of content.
        return notes.Count > 0 ? 1 : 0;
    }

    /// <summary>
    /// Safety net after a peak-based split: keep ascending and pure descending lower content.
    /// Only trims when a descent has already begun and pitch turns upward again
    /// (second ascent). Does not truncate merely because the lower staff still ascends
    /// toward the peak — that was the old bug with Assortment-balanced cuts.
    /// </summary>
    public static List<GeneratedNote> ApplyTwoOctaveLowerCut(IReadOnlyList<GeneratedNote> lowerFlat)
    {
        if (lowerFlat == null || lowerFlat.Count == 0)
            return new List<GeneratedNote>();

        int cutIdx = lowerFlat.Count;
        int prevPitchMidi = -1;
        bool seenDescent = false;
        for (int li = 0; li < lowerFlat.Count; li++)
        {
            if (lowerFlat[li].IsRest)
                continue;
            int m = lowerFlat[li].MidiNumber;
            if (prevPitchMidi >= 0)
            {
                if (m < prevPitchMidi)
                    seenDescent = true;
                else if (seenDescent && m > prevPitchMidi)
                {
                    cutIdx = li;
                    break;
                }
            }

            prevPitchMidi = m;
        }

        return lowerFlat.Take(cutIdx).ToList();
    }

    /// <summary>
    /// Re-splits a cached page at a new canvas width. Does not generate music.
    /// Two-octave scale pages keep the peak/turnaround note split (width-independent).
    /// Assortment pages re-run balanced measure packing at the new width.
    /// </summary>
    public static StaffDrawable.StaffMeasureSplitResult SplitCachedPage(
        StaffDrawable drawable,
        StaffPagePackState state,
        float canvasWidth,
        float canvasHeight)
    {
        ArgumentNullException.ThrowIfNull(drawable);
        ArgumentNullException.ThrowIfNull(state);

        if (state.IsTwoOctaveScaleCut)
            return SplitTwoOctaveScaleAtPeak(state.PageNotes);

        return drawable.SplitMeasuresAcrossStaves(
            state.PageNotes,
            state.PageBarBeats,
            canvasWidth,
            canvasHeight);
    }
}
