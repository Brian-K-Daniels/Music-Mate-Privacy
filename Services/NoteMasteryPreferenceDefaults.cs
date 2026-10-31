namespace musicmate.Services;

/// <summary>
/// Thresholds for Note Mastery display states that are not covered by
/// <see cref="MasteryEvaluator"/>. Mastered continues to use existing mastery rules.
/// </summary>
public static class NoteMasteryPreferenceDefaults
{
    /// <summary>
    /// When not mastered, overall (or streak) progress at or above this fraction of the
    /// session CorrectThreshold / StreakCrit counts as Improving; below is Practice Next.
    /// </summary>
    public const double ImprovingProgressFraction = 0.5;

    /// <summary>
    /// Relative weight for the temporarily emphasized note in random generation (0–100).
    /// Remaining probability is shared among other eligible pitches.
    /// </summary>
    public const int EmphasizedNoteSelectionPercent = 40;

    /// <summary>
    /// Higher weight used by "Practice This Note" so the selected pitch dominates
    /// without becoming the sole pitch.
    /// </summary>
    public const int PracticeThisNoteSelectionPercent = 70;
}
