using musicmate.Models;
using musicmate.ViewModels;

namespace musicmate.Services;

/// <summary>One-shot aggregation for the Note Mastery view.</summary>
public sealed class NoteMasterySnapshot
{
    public required IReadOnlyList<NoteMasteryItemViewModel> Notes { get; init; }
    public required string InstrumentDisplayName { get; init; }
    public required string Key { get; init; }
    public required string Scale { get; init; }
    public required string LowestNote { get; init; }
    public required string HighestNote { get; init; }
    public required bool PreferBassClef { get; init; }
    public required DateTime ComputedUtc { get; init; }
    public required StatisticsDbFingerprint Fingerprint { get; init; }

    public int TotalCount => Notes.Count;
    public int MasteredCount => Notes.Count(n => n.MasteryState == NoteMasteryState.Mastered);
    public int ImprovingCount => Notes.Count(n => n.MasteryState == NoteMasteryState.Improving);
    public int NeedsPracticeCount => Notes.Count(n => n.MasteryState == NoteMasteryState.NeedsPractice);
    public int NotYetAttemptedCount => Notes.Count(n => n.MasteryState == NoteMasteryState.NotYetAttempted);

    public double MasteredPercent =>
        TotalCount == 0 ? 0 : 100.0 * MasteredCount / TotalCount;

    public string SummaryText =>
        TotalCount == 0
            ? "No notes in the current range"
            : $"{MasteredCount} of {TotalCount} notes mastered — {MasteredPercent:0}%";
}
