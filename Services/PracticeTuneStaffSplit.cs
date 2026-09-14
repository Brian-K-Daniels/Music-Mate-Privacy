using musicmate.Drawables;
using musicmate.Models;

namespace musicmate.Services
{
    /// <summary>
    /// Partitions a practice / saved tune across the Music page's two staves.
    /// Short tunes must stay on the upper staff so an empty upper system is not engraved
    /// beside a full lower system (duplicate clef / time / tempo chrome).
    /// Longer tunes use width-aware packing (fill upper first) instead of a blind half-split.
    /// </summary>
    public static class PracticeTuneStaffSplit
    {
        /// <summary>
        /// Half-measure split used by legacy tests and as the short-tune fallback.
        /// When there are fewer than two measures (or the split would leave the upper
        /// staff empty), all notes stay on the upper staff.
        /// </summary>
        public static (List<GeneratedNote> Upper, List<GeneratedNote> Lower) PartitionByMeasureHalf(
            IReadOnlyList<GeneratedNote> allNotes,
            int measureCount,
            double splitBeat,
            Func<IReadOnlyList<GeneratedNote>, double, List<GeneratedNote>> shiftBeatPositions)
        {
            ArgumentNullException.ThrowIfNull(allNotes);
            ArgumentNullException.ThrowIfNull(shiftBeatPositions);

            if (allNotes.Count == 0)
                return (new List<GeneratedNote>(), new List<GeneratedNote>());

            // One-measure (and empty-upper) tunes: single clean upper system.
            if (measureCount < 2 || splitBeat <= 1e-9)
                return (allNotes.ToList(), new List<GeneratedNote>());

            var upper = allNotes.Where(n => (n.BeatPosition ?? 0.0) < splitBeat).ToList();
            var lowerSource = allNotes.Where(n => (n.BeatPosition ?? 0.0) >= splitBeat).ToList();
            var lower = shiftBeatPositions(lowerSource, splitBeat);

            if (upper.Count == 0 && lower.Count > 0)
                return (allNotes.ToList(), new List<GeneratedNote>());

            return (upper, lower);
        }

        /// <summary>
        /// Width-aware partition for saved / practice / layout-test tunes.
        /// Short tunes (&lt; 2 measures) stay on the upper staff only.
        /// Longer tunes fill the upper staff first, then the lower, at engraved min-width.
        /// Any measures that still will not fit are appended to the lower staff so the
        /// full saved tune remains visible (screen fit may scale that staff).
        /// </summary>
        public static StaffDrawable.StaffMeasureSplitResult PartitionForDisplay(
            StaffDrawable drawable,
            List<GeneratedNote> allNotes,
            IReadOnlyList<double> barBeats,
            int measureCount,
            float canvasWidth,
            float canvasHeight)
        {
            ArgumentNullException.ThrowIfNull(drawable);
            ArgumentNullException.ThrowIfNull(allNotes);
            ArgumentNullException.ThrowIfNull(barBeats);

            if (allNotes.Count == 0 || measureCount < 2)
            {
                var single = new StaffDrawable.StaffMeasureSplitResult
                {
                    UpperMeasureCount = allNotes.Count == 0 ? 0 : Math.Max(1, measureCount),
                    LowerMeasureCount = 0,
                    UnplacedMeasureCount = 0,
                };
                single.TotalMeasureCount = single.UpperMeasureCount;
                single.UpperNotes.AddRange(allNotes);
                return single;
            }

            var split = drawable.SplitMeasuresAcrossStaves(
                allNotes,
                barBeats,
                canvasWidth,
                canvasHeight,
                StaffDrawable.StaffMeasureSplitMode.FillUpperFirst);

            return KeepAllNotesVisible(split);
        }

        /// <summary>
        /// Moves unplaced measures onto the lower staff so a saved tune is never truncated.
        /// </summary>
        public static StaffDrawable.StaffMeasureSplitResult KeepAllNotesVisible(
            StaffDrawable.StaffMeasureSplitResult split)
        {
            ArgumentNullException.ThrowIfNull(split);
            if (split.UnplacedNotes.Count == 0)
                return split;

            split.LowerNotes.AddRange(split.UnplacedNotes);
            split.LowerMeasureCount += split.UnplacedMeasureCount;
            split.UnplacedNotes.Clear();
            split.UnplacedMeasureCount = 0;
            return split;
        }

        /// <summary>
        /// How many staff systems should be engraved (lines + header chrome).
        /// Empty staves must not draw clef / time / tempo or they look like corrupt overlays.
        /// </summary>
        public static int CountEngravedStaffSystems(int upperNoteCount, int lowerNoteCount)
        {
            int n = 0;
            if (upperNoteCount > 0) n++;
            if (lowerNoteCount > 0) n++;
            return n;
        }

        public static bool ShouldEngraveStaff(int noteCount) => noteCount > 0;
    }
}
