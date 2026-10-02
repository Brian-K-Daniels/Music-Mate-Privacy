using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using musicmate.Drawables;

namespace musicmate.Diagnostics;

/// <summary>
/// Temporary DEBUG instrumentation for tracing why staff notes turn red.
/// Writes to Debug output, logcat (Console), and a file under app data.
/// </summary>
public static class NoteStateChangeDiagnostics
{
    public const string Tag = "[NoteStateDiag]";

    private static readonly object Gate = new();
    private static string? _logFilePath;

    /// <summary>Test hook: capture lines without writing files.</summary>
    internal static Action<string>? TestSink { get; set; }

    /// <summary>Absolute path to the rolling diagnostic log file (DEBUG builds).</summary>
    public static string LogFilePath
    {
        get
        {
            if (_logFilePath != null)
                return _logFilePath;

            try
            {
                _logFilePath = Path.Combine(
                    Microsoft.Maui.Storage.FileSystem.AppDataDirectory,
                    "note-state-diagnostics.log");
            }
            catch
            {
                _logFilePath = Path.Combine(Path.GetTempPath(), "musicmate-note-state-diagnostics.log");
            }

            return _logFilePath;
        }
    }

    public enum NoteRelationToExpected
    {
        BeforeCurrent,
        AtCurrent,
        AfterCurrent,
        Unknown,
    }

    public readonly record struct NoteStateDiagContext(
        int CurrentExpectedIndex,
        double SessionMs,
        double? ExpectedStartMs,
        double? ScheduledBeat,
        string? DetectedNote,
        int NotesToDrawCount,
        int WrongFeedbackCount,
        string TuneMode);

    [Conditional("DEBUG")]
    public static void ClearLog()
    {
        lock (Gate)
        {
            try
            {
                var path = LogFilePath;
                File.WriteAllText(path, $"{Tag} log cleared {DateTime.UtcNow:O}{Environment.NewLine}");
            }
            catch
            {
                // best-effort
            }
        }
    }

    [Conditional("DEBUG")]
    public static void LogEvent(
        string kind,
        string method,
        string sourceFile,
        int sourceLine,
        NoteStateDiagContext ctx,
        string detail)
    {
        Write(BuildLine(
            kind,
            method,
            sourceFile,
            sourceLine,
            noteIndex: ctx.CurrentExpectedIndex,
            expectedNote: "-",
            detectedNote: ctx.DetectedNote,
            oldState: "-",
            newState: "-",
            ctx,
            detail));
    }

    [Conditional("DEBUG")]
    public static void LogWrongFeedback(
        string method,
        string sourceFile,
        int sourceLine,
        int noteIndex,
        string expectedNote,
        string? detectedNote,
        int wrongCountBefore,
        int wrongCountAfter,
        int cents,
        NoteStateDiagContext ctx,
        string reason,
        string? collectionDetail = null)
    {
        Write(BuildLine(
            "WrongFeedback",
            method,
            sourceFile,
            sourceLine,
            noteIndex,
            expectedNote,
            detectedNote,
            oldState: wrongCountBefore > 0 ? "Wrong" : "NotWrong",
            newState: "Wrong",
            ctx,
            reason,
            extra: $"WrongCount={wrongCountBefore}->{wrongCountAfter} Cents={cents} {collectionDetail}"));
    }

    [Conditional("DEBUG")]
    public static void LogCorrectAccepted(
        string method,
        string sourceFile,
        int sourceLine,
        int noteIndex,
        string expectedNote,
        string? detectedNote,
        NoteStateDiagContext ctx,
        string reason)
    {
        Write(BuildLine(
            "CorrectAccepted",
            method,
            sourceFile,
            sourceLine,
            noteIndex,
            expectedNote,
            detectedNote,
            oldState: "Current/Pending",
            newState: "Correct",
            ctx,
            reason));
    }

    [Conditional("DEBUG")]
    public static void LogCurrentNoteIndexChange(
        string method,
        string sourceFile,
        int sourceLine,
        int oldIndex,
        int newIndex,
        NoteStateDiagContext ctx,
        string reason,
        string? detectedNote = null)
    {
        Write(BuildLine(
            "ExpectedIndexChange",
            method,
            sourceFile,
            sourceLine,
            noteIndex: newIndex,
            expectedNote: ExpectedNameOrDash(ctx, newIndex),
            detectedNote,
            oldState: oldIndex.ToString(CultureInfo.InvariantCulture),
            newState: newIndex.ToString(CultureInfo.InvariantCulture),
            ctx,
            reason,
            extra: $"OldExpectedIndex={oldIndex} NewExpectedIndex={newIndex} NotesToDraw={ctx.NotesToDrawCount}"));
    }

    [Conditional("DEBUG")]
    public static void LogStaffStateTransition(
        string method,
        string sourceFile,
        int sourceLine,
        string staff,
        int staffSlotIndex,
        int sessionIndex,
        string noteName,
        StaffNoteState oldState,
        StaffNoteState newState,
        NoteStateDiagContext ctx,
        string reason)
    {
        if (oldState == newState)
            return;

        Write(BuildLine(
            "StaffState",
            method,
            sourceFile,
            sourceLine,
            noteIndex: sessionIndex,
            expectedNote: noteName,
            detectedNote: ctx.DetectedNote,
            oldState: oldState.ToString(),
            newState: newState.ToString(),
            ctx,
            reason,
            extra: $"Staff={staff} StaffSlot={staffSlotIndex}"));
    }

    [Conditional("DEBUG")]
    public static void LogStaffBulkAssign(
        string method,
        string sourceFile,
        int sourceLine,
        string staff,
        StaffNoteState[]? previous,
        StaffNoteState[] next,
        int[] sessionIndices,
        string[] noteNames,
        NoteStateDiagContext ctx,
        string reason)
    {
        int wrongCount = next.Count(s => s == StaffNoteState.Wrong);
        var summary = SummarizeStates(next);
        var prevSummary = previous != null ? SummarizeStates(previous) : "null";
        Write(BuildLine(
            "StaffBulkAssign",
            method,
            sourceFile,
            sourceLine,
            noteIndex: ctx.CurrentExpectedIndex,
            expectedNote: ExpectedNameOrDash(ctx, ctx.CurrentExpectedIndex),
            detectedNote: ctx.DetectedNote,
            oldState: prevSummary,
            newState: summary,
            ctx,
            reason,
            extra: $"Staff={staff} ArrayLen={next.Length} WrongCount={wrongCount} SessionIndices=[{string.Join(',', sessionIndices)}] Names=[{string.Join(',', noteNames)}]"));
    }

    [Conditional("DEBUG")]
    public static void LogCollectionMutation(
        string method,
        string sourceFile,
        int sourceLine,
        string collectionName,
        string operation,
        int countBefore,
        int countAfter,
        NoteStateDiagContext ctx,
        string detail)
    {
        Write(BuildLine(
            "Collection",
            method,
            sourceFile,
            sourceLine,
            noteIndex: ctx.CurrentExpectedIndex,
            expectedNote: "-",
            detectedNote: ctx.DetectedNote,
            oldState: countBefore.ToString(CultureInfo.InvariantCulture),
            newState: countAfter.ToString(CultureInfo.InvariantCulture),
            ctx,
            operation,
            extra: $"Collection={collectionName} {detail}"));
    }

    [Conditional("DEBUG")]
    public static void LogStaffResolve(
        string method,
        string sourceFile,
        int sourceLine,
        int sessionIndex,
        string noteName,
        StaffNoteState resolved,
        NoteStateDiagContext ctx,
        string reason)
    {
        if (resolved != StaffNoteState.Wrong)
            return;

        Write(BuildLine(
            "StaffResolveWrong",
            method,
            sourceFile,
            sourceLine,
            sessionIndex,
            noteName,
            ctx.DetectedNote,
            oldState: "Pending/Current",
            newState: "Wrong",
            ctx,
            reason));
    }

    internal static NoteRelationToExpected ClassifyIndex(int noteIndex, int currentExpectedIndex)
    {
        if (currentExpectedIndex < 0)
            return NoteRelationToExpected.Unknown;
        if (noteIndex < currentExpectedIndex)
            return NoteRelationToExpected.BeforeCurrent;
        if (noteIndex == currentExpectedIndex)
            return NoteRelationToExpected.AtCurrent;
        return NoteRelationToExpected.AfterCurrent;
    }

    internal static void ResetForTests()
    {
        TestSink = null;
        lock (Gate)
            _logFilePath = null;
    }

    private static string ExpectedNameOrDash(NoteStateDiagContext ctx, int index)
        => index >= 0 && index < ctx.NotesToDrawCount ? $"idx{index}" : "-";

    private static string SummarizeStates(StaffNoteState[] states)
    {
        int p = 0, c = 0, w = 0, u = 0;
        foreach (var s in states)
        {
            switch (s)
            {
                case StaffNoteState.Pending: p++; break;
                case StaffNoteState.Current: c++; break;
                case StaffNoteState.Correct: u++; break;
                case StaffNoteState.Wrong: w++; break;
            }
        }

        return $"P={p} Cur={c} Ok={u} Wrong={w}";
    }

    private static string BuildLine(
        string kind,
        string method,
        string sourceFile,
        int sourceLine,
        int noteIndex,
        string expectedNote,
        string? detectedNote,
        string oldState,
        string newState,
        NoteStateDiagContext ctx,
        string reason,
        string? extra = null)
    {
        var rel = ClassifyIndex(noteIndex, ctx.CurrentExpectedIndex);
        var parts = new List<string>
        {
            Tag,
            $"Kind={kind}",
            $"Method={method}",
            $"At={Path.GetFileName(sourceFile)}:{sourceLine}",
            $"NoteIndex={noteIndex}",
            $"ExpectedIndex={ctx.CurrentExpectedIndex}",
            $"Relation={rel}",
            $"ExpectedNote={expectedNote}",
            $"DetectedNote={detectedNote ?? "-"}",
            $"Old={oldState}",
            $"New={newState}",
            $"SessionMs={ctx.SessionMs.ToString("F0", CultureInfo.InvariantCulture)}",
        };

        if (ctx.ExpectedStartMs.HasValue)
            parts.Add($"ScheduledMs={ctx.ExpectedStartMs.Value.ToString("F0", CultureInfo.InvariantCulture)}");
        if (ctx.ScheduledBeat.HasValue)
            parts.Add($"ScheduledBeat={ctx.ScheduledBeat.Value.ToString("F2", CultureInfo.InvariantCulture)}");
        parts.Add($"WrongFeedbackCount={ctx.WrongFeedbackCount}");
        parts.Add($"Tune={ctx.TuneMode}");
        parts.Add($"Reason={reason}");
        if (!string.IsNullOrWhiteSpace(extra))
            parts.Add(extra);

        return string.Join(' ', parts);
    }

    private static void Write(string line)
    {
        if (TestSink != null)
        {
            TestSink(line);
            return;
        }

        DebugLog.WriteLine(DebugLogCategory.MusicPageFlow, line);
        Console.WriteLine(line);

        lock (Gate)
        {
            try
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
            catch
            {
                // best-effort when filesystem unavailable
            }
        }
    }

    internal static void GetCaller(
        out string method,
        out string file,
        out int line,
        [CallerMemberName] string member = "",
        [CallerFilePath] string path = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        method = member;
        file = path;
        line = lineNumber;
    }
}
