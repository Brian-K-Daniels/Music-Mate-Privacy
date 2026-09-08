using musicmate.Models;

namespace musicmate.Services
{
    /// <summary>
    /// Partitions a practice / saved tune across the Music page's two staves.
    /// Short tunes must stay on the upper staff so an empty upper system is not engraved
    /// beside a full lower system (duplicate clef / time / tempo chrome).
    /// </summary>
    public static class PracticeTuneStaffSplit
    {
        /// <summary>
        /// Half-measure split used by the Practice Tune display path.
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
