using musicmate.Models;

namespace musicmate.Services;

/// <summary>
/// Splits notes that cross a bar line into tied segments whose durations sum to the
/// original note. Also repairs overfull measures (Σ onsets &gt; meter) by packing their
/// events onto the meter grid with the same split/tie rules — never duplicating duration.
/// </summary>
public static class BarLineTieNormalizer
{
    private const double Eps = 1e-9;

    /// <summary>
    /// Ensures every full measure's rendered duration equals <paramref name="beatsPerMeasure"/>
    /// (short final bars may be underfull). Crossing / overflowing notes become tied
    /// segments; segment durations always sum to the source note's duration.
    /// </summary>
    public static List<GeneratedNote> Normalize(
        IReadOnlyList<GeneratedNote> notes,
        double beatsPerMeasure)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (notes.Count == 0 || beatsPerMeasure <= Eps)
            return notes.ToList();

        if (IsAlreadyLegal(notes, beatsPerMeasure))
            return notes.ToList();

        var ordered = notes
            .OrderBy(n => n.BeatPosition ?? 0.0)
            .ThenBy(n => n.MeasureIndex ?? 0)
            .ToList();

        int nextTieId = 1;
        var splitInPlace = new List<GeneratedNote>(ordered.Count + 4);
        foreach (var source in ordered)
            splitInPlace.AddRange(SplitNoteAtBarLines(source, beatsPerMeasure, ref nextTieId));

        var sums = MeasureRenderedDurations(splitInPlace, beatsPerMeasure);
        bool stillOverfull = sums.Any(s => s > beatsPerMeasure + Eps);
        if (!stillOverfull)
            return splitInPlace;

        // Overfull onset-sums: re-pack the whole stream in written order so overflow
        // becomes a tie into the next bar instead of an extra head in the start bar.
        return PackSequentially(ordered, beatsPerMeasure, ref nextTieId);
    }

    /// <summary>
    /// Sum of <see cref="GeneratedNote.BeatDuration"/> for notes whose onset falls in
    /// each measure. Used by regression tests before staff draw.
    /// </summary>
    public static List<double> MeasureRenderedDurations(
        IReadOnlyList<GeneratedNote> notes,
        double beatsPerMeasure)
    {
        if (notes.Count == 0 || beatsPerMeasure <= Eps)
            return new List<double>();

        double origin = notes.Min(n => n.BeatPosition ?? 0.0);
        origin = Math.Floor(origin / beatsPerMeasure + Eps) * beatsPerMeasure;
        double contentEnd = notes.Max(n => (n.BeatPosition ?? 0.0) + n.BeatDuration);
        int measureCount = Math.Max(1, (int)Math.Ceiling((contentEnd - origin) / beatsPerMeasure - Eps));

        var sums = new double[measureCount];
        foreach (var n in notes)
        {
            double bp = n.BeatPosition ?? 0.0;
            int mi = (int)Math.Floor((bp - origin) / beatsPerMeasure + Eps);
            if (mi < 0 || mi >= measureCount)
                continue;
            sums[mi] += n.BeatDuration;
        }

        return sums.ToList();
    }

    private static List<GeneratedNote> SplitNoteAtBarLines(
        GeneratedNote source,
        double beatsPerMeasure,
        ref int nextTieId)
    {
        double start = source.BeatPosition ?? 0.0;
        double remaining = source.BeatDuration;
        if (remaining <= Eps)
            return new List<GeneratedNote>();

        double local = start - Math.Floor(start / beatsPerMeasure + Eps) * beatsPerMeasure;
        if (local + remaining <= beatsPerMeasure + Eps)
            return new List<GeneratedNote> { source };

        var result = new List<GeneratedNote>();
        double cursor = start;
        int? tieGroupId = null;
        bool emittedAny = false;

        while (remaining > Eps)
        {
            double barEnd = (Math.Floor(cursor / beatsPerMeasure + Eps) + 1.0) * beatsPerMeasure;
            double room = barEnd - cursor;
            if (room <= Eps)
            {
                cursor = barEnd;
                continue;
            }

            double placeBeats = Math.Min(remaining, room);
            foreach (var piece in NoteDurationHelper.DecomposeBeats(placeBeats))
            {
                double pieceBeats = piece.ToBeatValue();
                if (pieceBeats > remaining + Eps)
                    break;

                bool isContinuation = emittedAny;
                if (isContinuation && tieGroupId == null)
                {
                    tieGroupId = nextTieId++;
                    int prev = result.Count - 1;
                    if (prev >= 0)
                    {
                        var head = result[prev];
                        result[prev] = head.WithRhythm(
                            head.Duration,
                            head.MeasureIndex ?? 0,
                            head.BeatPosition ?? 0.0,
                            tieGroupId,
                            isTieContinuation: false);
                    }
                }

                int measureIndex = (int)Math.Floor(cursor / beatsPerMeasure + Eps);
                result.Add(source.WithRhythm(
                    piece, measureIndex, cursor, tieGroupId, isContinuation));

                cursor += pieceBeats;
                remaining -= pieceBeats;
                emittedAny = true;
            }
        }

        return result;
    }

    private static List<GeneratedNote> PackSequentially(
        IReadOnlyList<GeneratedNote> ordered,
        double beatsPerMeasure,
        ref int nextTieId)
    {
        var result = new List<GeneratedNote>(ordered.Count + 4);
        double cursor = ordered[0].BeatPosition ?? 0.0;
        cursor = Math.Floor(cursor / beatsPerMeasure + Eps) * beatsPerMeasure;

        foreach (var source in ordered)
        {
            double remaining = source.BeatDuration;
            if (remaining <= Eps)
                continue;

            int? tieGroupId = null;
            bool emittedAny = false;

            while (remaining > Eps)
            {
                double barEnd = (Math.Floor(cursor / beatsPerMeasure + Eps) + 1.0) * beatsPerMeasure;
                double room = barEnd - cursor;
                if (room <= Eps)
                {
                    cursor = barEnd;
                    continue;
                }

                double placeBeats = Math.Min(remaining, room);
                foreach (var piece in NoteDurationHelper.DecomposeBeats(placeBeats))
                {
                    double pieceBeats = piece.ToBeatValue();
                    if (pieceBeats > remaining + Eps)
                        break;

                    bool isContinuation = emittedAny;
                    if (isContinuation && tieGroupId == null)
                    {
                        tieGroupId = nextTieId++;
                        int prev = result.Count - 1;
                        if (prev >= 0)
                        {
                            var head = result[prev];
                            result[prev] = head.WithRhythm(
                                head.Duration,
                                head.MeasureIndex ?? 0,
                                head.BeatPosition ?? 0.0,
                                tieGroupId,
                                isTieContinuation: false);
                        }
                    }

                    int measureIndex = (int)Math.Floor(cursor / beatsPerMeasure + Eps);
                    result.Add(source.WithRhythm(
                        piece, measureIndex, cursor, tieGroupId, isContinuation));

                    cursor += pieceBeats;
                    remaining -= pieceBeats;
                    emittedAny = true;
                }
            }
        }

        return result;
    }

    private static bool IsAlreadyLegal(IReadOnlyList<GeneratedNote> notes, double beatsPerMeasure)
    {
        foreach (var n in notes)
        {
            double bp = n.BeatPosition ?? 0.0;
            double local = bp - Math.Floor(bp / beatsPerMeasure + Eps) * beatsPerMeasure;
            if (local + n.BeatDuration > beatsPerMeasure + Eps)
                return false;
        }

        var sums = MeasureRenderedDurations(notes, beatsPerMeasure);
        for (int i = 0; i < sums.Count; i++)
        {
            bool isLast = i == sums.Count - 1;
            if (isLast)
            {
                if (sums[i] > beatsPerMeasure + Eps)
                    return false;
            }
            else if (Math.Abs(sums[i] - beatsPerMeasure) > Eps)
            {
                return false;
            }
        }

        return true;
    }
}
