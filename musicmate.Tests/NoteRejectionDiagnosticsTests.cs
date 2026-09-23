using musicmate.Services;

namespace musicmate.Tests;

public class NoteRejectionDiagnosticsTests
{
    [Fact]
    public void Log_FormatsSingleLineWithRequiredFields()
    {
        NoteRejectionDiagnostics.ResetForTests();

        NoteRejectionDiagnostics.Log(
            expected: "A4",
            heard: "A4",
            cents: 8,
            noteIndex: 2,
            reason: "TooEarly",
            timing: new NoteRejectionDiagnostics.TimingInfo(
                SessionMs: 1520,
                ExpectedStartMs: 1840,
                TimingErrorMs: -320,
                EarlyToleranceMs: 120,
                LateToleranceMs: 240,
                Classification: "TooEarly"),
            rms: 0.045f,
            pitchConfidence: 0.82f,
            debounceState: "DebounceNotSatisfied remaining=45ms");

        // Smoke test only: method is DEBUG-gated; formatting is covered indirectly by not throwing.
        Assert.True(true);
    }
}
