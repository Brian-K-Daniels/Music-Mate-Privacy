using musicmate.Diagnostics;
using musicmate.Models;

namespace musicmate.Services;

/// <summary>
/// Applies mastered-note omission to written-pitch MIDI candidate pools for
/// random / candidate-selection generation. When omission leaves too few distinct
/// pitches for useful melodies, temporarily reintroduces the minimum mastered notes
/// needed — without changing mastery status, stats, or level.
/// </summary>
public static class MasteredNoteOmission
{
    public enum FallbackKind
    {
        None,
        /// <summary>Fewer than the preferred minimum remain, but unmastered pitches are still used.</summary>
        UsedSmallUnmasteredPool,
        /// <summary>Every eligible pitch is mastered — deliberately allow mastered notes.</summary>
        AllowedMasteredAllEligibleMastered,
        /// <summary>
        /// Omission left too few distinct pitches; temporarily restored the minimum mastered
        /// notes needed for melodic variety (mastery status unchanged).
        /// </summary>
        RelaxedOmissionForDistinctPitches,
    }

    public readonly record struct Result(
        IReadOnlyList<int> Pool,
        FallbackKind Fallback,
        string Reason,
        int CandidateCountBefore,
        int MasteredExcludedCount,
        int CandidateCountAfter,
        IReadOnlyList<int>? TemporarilyRestoredMidis = null)
    {
        public IReadOnlyList<int> RestoredMidis
            => TemporarilyRestoredMidis ?? Array.Empty<int>();
    }

    /// <summary>
    /// Filters <paramref name="fullPool"/> by written MIDI. Emphasized pitch may remain
    /// even when mastered. When every candidate is mastered, returns the original pool
    /// with <see cref="FallbackKind.AllowedMasteredAllEligibleMastered"/>.
    /// When unmastered pitches remain but fewer than <paramref name="minDistinctPitches"/>
    /// distinct values, reintroduces the closest mastered notes until the requirement is met
    /// (or the full pool is exhausted).
    /// </summary>
    public static Result Apply(
        IReadOnlyList<int> fullPool,
        IReadOnlyCollection<int> excludedMidiNumbers,
        int? emphasizedMidi = null,
        int minDistinctPitches = 1)
    {
        var before = fullPool?.ToList() ?? new List<int>();
        if (before.Count == 0
            || excludedMidiNumbers is null
            || excludedMidiNumbers.Count == 0)
        {
            return new Result(
                before,
                FallbackKind.None,
                string.Empty,
                before.Count,
                0,
                before.Count);
        }

        var excluded = excludedMidiNumbers as HashSet<int> ?? new HashSet<int>(excludedMidiNumbers);
        int masteredInPool = before.Count(m => excluded.Contains(m));
        int eligibleDistinct = before.Distinct().Count();
        // Cap the requirement by how many distinct pitches the eligible pool can supply.
        int effectiveMinDistinct = Math.Min(
            Math.Max(1, minDistinctPitches),
            Math.Max(1, eligibleDistinct));

        var filtered = new List<int>(before.Count);
        foreach (int m in before)
        {
            if (!excluded.Contains(m)
                || (emphasizedMidi.HasValue && m == emphasizedMidi.Value))
            {
                filtered.Add(m);
            }
        }

        if (emphasizedMidi.HasValue
            && before.Contains(emphasizedMidi.Value)
            && !filtered.Contains(emphasizedMidi.Value))
        {
            filtered.Add(emphasizedMidi.Value);
        }

        if (filtered.Count == 0)
        {
            const string allMasteredReason =
                "All eligible written pitches are mastered; including mastered notes so practice can continue.";
            return new Result(
                before,
                FallbackKind.AllowedMasteredAllEligibleMastered,
                allMasteredReason,
                before.Count,
                masteredInPool,
                before.Count);
        }

        int unmasteredDistinct = filtered.Distinct().Count();
        if (unmasteredDistinct >= effectiveMinDistinct)
        {
            var kind = filtered.Count < MusicSequenceGenerator.MinPitchPoolAfterMasteryExclusion
                ? FallbackKind.UsedSmallUnmasteredPool
                : FallbackKind.None;
            string reason = kind == FallbackKind.UsedSmallUnmasteredPool
                ? $"Only {filtered.Count} unmastered written pitch(es) remain " +
                  $"(preferred minimum {MusicSequenceGenerator.MinPitchPoolAfterMasteryExclusion}); " +
                  "continuing with unmastered pitches only — mastered notes stay omitted."
                : string.Empty;

            return new Result(
                filtered,
                kind,
                reason,
                before.Count,
                masteredInPool,
                filtered.Count);
        }

        // Too few distinct unmastered pitches — temporarily restore closest mastered notes.
        var pool = new List<int>(filtered);
        var restored = new List<int>();
        var masteredOutside = before
            .Where(m => excluded.Contains(m) && !pool.Contains(m))
            .Distinct()
            .ToList();

        int eligibleBeforeRelaxation = unmasteredDistinct;

        while (pool.Distinct().Count() < effectiveMinDistinct && masteredOutside.Count > 0)
        {
            int pick = MelodicVarietyRules.PickClosestMasteredToUnmastered(masteredOutside, pool);
            pool.Add(pick);
            restored.Add(pick);
            masteredOutside.RemoveAll(m => m == pick);
        }

        string restoreNames = FormatMidiSample(restored);
        string relaxReason =
            $"Mastery omission left {eligibleBeforeRelaxation} distinct eligible pitch(es); " +
            $"temporarily restored [{restoreNames}] to reach {pool.Distinct().Count()} distinct " +
            $"(required {effectiveMinDistinct}). Mastery status unchanged.";

        DebugLog.WriteLine(
            DebugLogCategory.StaffAndSequence,
            $"[MasteryOmit] Relaxed omission: eligibleBefore={eligibleBeforeRelaxation} " +
            $"restored=[{restoreNames}] poolDistinct={pool.Distinct().Count()}");

        return new Result(
            pool,
            FallbackKind.RelaxedOmissionForDistinctPitches,
            relaxReason,
            before.Count,
            masteredInPool,
            pool.Count,
            restored);
    }

    /// <summary>
    /// Convenience overload: derives the minimum distinct-pitch requirement from child level.
    /// </summary>
    public static Result ApplyForLevel(
        IReadOnlyList<int> fullPool,
        IReadOnlyCollection<int> excludedMidiNumbers,
        int childLevel,
        int? emphasizedMidi = null)
        => Apply(
            fullPool,
            excludedMidiNumbers,
            emphasizedMidi,
            MelodicVarietyRules.GetMinimumDistinctPitchesForLevel(childLevel));

    /// <summary>
    /// Returns true when a generated pitched note should have been omitted.
    /// </summary>
    public static bool IsUnexpectedMasteredPitch(
        int midi,
        IReadOnlyCollection<int> excludedMidiNumbers,
        FallbackKind fallback,
        int? emphasizedMidi = null)
    {
        if (excludedMidiNumbers is null || excludedMidiNumbers.Count == 0)
            return false;
        if (fallback is FallbackKind.AllowedMasteredAllEligibleMastered
            or FallbackKind.RelaxedOmissionForDistinctPitches)
            return false;
        if (emphasizedMidi.HasValue && midi == emphasizedMidi.Value)
            return false;
        return excludedMidiNumbers.Contains(midi);
    }

    public static void LogFilter(
        string activityType,
        bool omissionEnabled,
        IEnumerable<int> candidatesBefore,
        IEnumerable<int> masteredMidis,
        Result result,
        IEnumerable<int>? finalGeneratedMidis = null)
    {
        string finalSample = finalGeneratedMidis is null
            ? "(not yet generated)"
            : FormatMidiSample(finalGeneratedMidis);
        int finalDistinct = finalGeneratedMidis is null
            ? -1
            : MelodicVarietyRules.CountDistinctPitches(finalGeneratedMidis);

        DebugLog.WriteLine(
            DebugLogCategory.StaffAndSequence,
            $"[MasteryOmit] activity={activityType} omissionOn={omissionEnabled} " +
            $"candidatesBefore=[{FormatMidiSample(candidatesBefore)}] " +
            $"masteredFound=[{FormatMidiSample(masteredMidis)}] " +
            $"candidatesAfter=[{FormatMidiSample(result.Pool)}] " +
            $"restored=[{FormatMidiSample(result.RestoredMidis)}] " +
            $"counts before={result.CandidateCountBefore} masteredInPool={result.MasteredExcludedCount} " +
            $"after={result.CandidateCountAfter} fallback={result.Fallback} " +
            $"finalDistinct={finalDistinct} reason=\"{result.Reason}\" final=[{finalSample}]");
    }

    public static string FormatMidiSample(IEnumerable<int> midis)
    {
        var names = midis
            .Where(m => m >= 0)
            .Distinct()
            .OrderBy(m => m)
            .Take(32)
            .Select(m => NoteSessionService.MidiToNoteName(m, flats: false));
        return string.Join(",", names);
    }
}
