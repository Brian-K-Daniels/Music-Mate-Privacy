using musicmate.Diagnostics;
using musicmate.Drawables;

namespace musicmate.Services;

/// <summary>
/// Maps session feedback to staff note colors. Only notes with explicit wrong feedback
/// render red; unattempted notes stay pending even if the timeline cursor moved past them.
/// </summary>
internal static class StaffNoteStateResolver
{
    internal static StaffNoteState Resolve(
        int sessionIndex,
        int currentNoteIndex,
        bool isActiveStaff,
        IEnumerable<int> correctIndices,
        IReadOnlyDictionary<int, (int Wrong, int Cents)> noteFeedbacks,
        NoteStateChangeDiagnostics.NoteStateDiagContext? diag = null,
        string? noteName = null,
        string? resolveReason = null)
    {
        if (correctIndices.Contains(sessionIndex))
            return StaffNoteState.Correct;
        if (noteFeedbacks.TryGetValue(sessionIndex, out var fb) && fb.Wrong > 0)
        {
            if (diag.HasValue)
            {
                NoteStateChangeDiagnostics.GetCaller(out var method, out var file, out var line);
                NoteStateChangeDiagnostics.LogStaffResolve(
                    method,
                    file,
                    line,
                    sessionIndex,
                    noteName ?? $"idx{sessionIndex}",
                    StaffNoteState.Wrong,
                    diag.Value,
                    resolveReason ?? "NoteFeedbacks.Wrong>0");
            }
            return StaffNoteState.Wrong;
        }
        if (sessionIndex == currentNoteIndex && isActiveStaff)
            return StaffNoteState.Current;
        return StaffNoteState.Pending;
    }
}
