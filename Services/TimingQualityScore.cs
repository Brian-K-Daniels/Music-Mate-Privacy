namespace musicmate.Services;

/// <summary>
/// Continuous timing quality for one attack, separate from the T+ / T− pass-fail window.
/// </summary>
/// <remarks>
/// <para>
/// <c>r = |errorMs| / toleranceMs</c>, where <c>toleranceMs</c> is the accept window on the
/// side the attack fell (early tolerance when the attack was early, late tolerance otherwise).
/// </para>
/// <para>
/// <c>score = clamp(100 × (1 − r / 2), 0, 100)</c>.
/// A centered attack scores 100. An attack exactly at the edge of the window that
/// accepted or rejected it scores 50. An attack twice that far scores 0.
/// Every attack inside its window therefore scores at least 50.
/// </para>
/// </remarks>
public static class TimingQualityScore
{
    public static double FromErrorAndTolerance(double errorMs, double toleranceMs)
    {
        if (toleranceMs <= 0
            || double.IsNaN(toleranceMs)
            || double.IsInfinity(toleranceMs)
            || double.IsNaN(errorMs)
            || double.IsInfinity(errorMs))
            return 0;

        double r = Math.Abs(errorMs) / toleranceMs;
        double score = 100.0 * (1.0 - r / 2.0);
        if (score < 0)
            return 0;
        if (score > 100)
            return 100;
        return score;
    }

    /// <summary>
    /// Average of <see cref="FromErrorAndTolerance"/> over the samples.
    /// Null when nothing was timed.
    /// </summary>
    public static double? Average(IReadOnlyList<(double ErrorMs, double ToleranceMs)> samples)
    {
        if (samples.Count == 0)
            return null;

        double sum = 0;
        foreach (var (errorMs, toleranceMs) in samples)
            sum += FromErrorAndTolerance(errorMs, toleranceMs);
        return sum / samples.Count;
    }
}
