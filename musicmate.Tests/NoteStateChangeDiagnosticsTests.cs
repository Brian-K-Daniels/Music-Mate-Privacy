using musicmate.Diagnostics;
using musicmate.Drawables;
using musicmate.Services;

namespace musicmate.Tests;

public class NoteStateChangeDiagnosticsTests
{
    [Fact]
    public void LogWrongFeedback_WritesTaggedLine()
    {
        var lines = new List<string>();
        NoteStateChangeDiagnostics.TestSink = lines.Add;
        try
        {
            var ctx = new NoteStateChangeDiagnostics.NoteStateDiagContext(
                CurrentExpectedIndex: 0,
                SessionMs: 120,
                ExpectedStartMs: 0,
                ScheduledBeat: 0,
                DetectedNote: "G4",
                NotesToDrawCount: 15,
                WrongFeedbackCount: 1,
                TuneMode: "Practice Tune");

            NoteStateChangeDiagnostics.LogWrongFeedback(
                "TryMarkDebouncedWrong",
                "NoteSessionService.cs",
                2516,
                noteIndex: 0,
                expectedNote: "G4",
                detectedNote: "G4",
                wrongCountBefore: 0,
                wrongCountAfter: 1,
                cents: 12,
                ctx,
                "Conductor:TooEarly",
                "NoteFeedbacks.Count=1");

            Assert.Single(lines);
            Assert.Contains("[NoteStateDiag]", lines[0]);
            Assert.Contains("Kind=WrongFeedback", lines[0]);
            Assert.Contains("NoteIndex=0", lines[0]);
            Assert.Contains("Reason=Conductor:TooEarly", lines[0]);
            Assert.Contains("Relation=AtCurrent", lines[0]);
        }
        finally
        {
            NoteStateChangeDiagnostics.ResetForTests();
        }
    }

    [Fact]
    public void StaffResolver_LogsWhenResolvingWrongWithDiagContext()
    {
        var lines = new List<string>();
        NoteStateChangeDiagnostics.TestSink = lines.Add;
        try
        {
            var ctx = new NoteStateChangeDiagnostics.NoteStateDiagContext(
                3, 500, 612, 1, "G4", 15, 5, "Practice Tune");
            var feedback = new Dictionary<int, (int Wrong, int Cents)> { [2] = (1, 0) };

            var state = StaffNoteStateResolver.Resolve(
                2, 3, true, [], feedback, ctx, "A4", "test");

            Assert.Equal(StaffNoteState.Wrong, state);
            Assert.Single(lines);
            Assert.Contains("Kind=StaffResolveWrong", lines[0]);
            Assert.Contains("NoteIndex=2", lines[0]);
        }
        finally
        {
            NoteStateChangeDiagnostics.ResetForTests();
        }
    }
}
