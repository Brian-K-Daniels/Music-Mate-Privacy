namespace musicmate.Services;

/// <summary>
/// Distinct-pitch and consecutive-repeat rules for By Level / random melodic generation.
/// Practice tunes and scale walks are intentionally exempt at the call site.
/// </summary>
public static class MelodicVarietyRules
{
    /// <summary>Maximum identical pitched notes allowed in a row for random generation.</summary>
    public const int MaxConsecutiveIdenticalPitches = 3;

    /// <summary>Cap on regenerate attempts when a tune fails variety validation.</summary>
    public const int MaxRegenerationAttempts = 8;

    /// <summary>
    /// Minimum distinct pitched MIDI numbers required in a generated random tune.
    /// Beginner levels need 2; Level 21+ need 3.
    /// </summary>
    public static int GetMinimumDistinctPitchesForLevel(int level)
    {
        if (level <= 0)
            return 2;
        return level >= 21 ? 3 : 2;
    }

    public static int CountDistinctPitches(IEnumerable<int> pitchedMidis)
        => pitchedMidis.Distinct().Count();

    /// <summary>
    /// True when more than <see cref="MaxConsecutiveIdenticalPitches"/> identical
    /// pitched MIDI values appear consecutively.
    /// </summary>
    public static bool HasExcessiveConsecutiveIdentical(
        IReadOnlyList<int> pitchedMidis,
        int maxAllowedConsecutive = MaxConsecutiveIdenticalPitches)
    {
        if (pitchedMidis.Count <= maxAllowedConsecutive)
            return false;

        int run = 1;
        for (int i = 1; i < pitchedMidis.Count; i++)
        {
            if (pitchedMidis[i] == pitchedMidis[i - 1])
            {
                run++;
                if (run > maxAllowedConsecutive)
                    return true;
            }
            else
            {
                run = 1;
            }
        }

        return false;
    }

    /// <summary>
    /// Effective minimum distinct pitches, capped by how many the eligible pool can supply.
    /// </summary>
    public static int GetEffectiveMinimumDistinct(int level, int eligibleDistinctCount)
    {
        int required = GetMinimumDistinctPitchesForLevel(level);
        if (eligibleDistinctCount <= 0)
            return required;
        return Math.Min(required, eligibleDistinctCount);
    }

    public static bool MeetsDistinctPitchRequirement(
        IReadOnlyList<int> pitchedMidis,
        int level,
        int eligibleDistinctCount)
    {
        if (pitchedMidis.Count == 0)
            return true;

        int required = GetEffectiveMinimumDistinct(level, eligibleDistinctCount);
        return CountDistinctPitches(pitchedMidis) >= required;
    }

    /// <summary>
    /// Picks the mastered MIDI closest to any remaining unmastered pitch
    /// (absolute semitone distance, then lower MIDI as tie-break).
    /// </summary>
    public static int PickClosestMasteredToUnmastered(
        IReadOnlyList<int> masteredCandidates,
        IReadOnlyList<int> unmasteredPitches)
    {
        if (masteredCandidates.Count == 0)
            throw new ArgumentException("No mastered candidates.", nameof(masteredCandidates));

        if (unmasteredPitches.Count == 0)
            return masteredCandidates.OrderBy(m => m).ElementAt(masteredCandidates.Count / 2);

        return masteredCandidates
            .OrderBy(m => unmasteredPitches.Min(u => Math.Abs(m - u)))
            .ThenBy(m => m)
            .First();
    }
}
