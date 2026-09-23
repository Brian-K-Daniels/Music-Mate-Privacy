using System.Diagnostics;
using System.Globalization;
using System.Text;
using musicmate.Services;

#if DEBUG

namespace musicmate.Diagnostics;

/// <summary>
/// One-shot DEBUG trace when the first practice pitch is detected.
/// </summary>
public static class FirstSoundTimingDiagnostics
{
    public const string Tag = "[FirstSoundDiag]";

    internal static Action<string>? TestSink { get; set; }

    public readonly record struct TimingMarkers(
        DateTime? PlaybackArmUtc,
        DateTime? CountInStartUtc,
        DateTime? CountInEndUtc,
        DateTime? FirstPitchUtc);

    [Conditional("DEBUG")]
    public static void LogAnalysis(
        NoteSessionService session,
        TimingMarkers markers,
        int indexBefore,
        int indexAfter,
        string? detectedNote,
        double freq,
        int cents,
        double elapsedAtFirstSoundMs,
        string processingSummary)
    {
        int bpm = session.GetConductorTimingBpmPublic();
        double msPerBeat = ConductorOnsetTiming.MsPerBeat(bpm);
        double elapsedMs = elapsedAtFirstSoundMs;
        int noteCount = Math.Min(10, session.NotesToDraw.Count);

        var sb = new StringBuilder();
        sb.AppendLine($"{Tag} ===== FIRST DETECTED SOUND =====");
        sb.AppendLine($"{Tag} PlaybackArmUtc={Fmt(markers.PlaybackArmUtc)}");
        sb.AppendLine($"{Tag} CountInStartUtc={Fmt(markers.CountInStartUtc)}");
        sb.AppendLine($"{Tag} CountInEndUtc={Fmt(markers.CountInEndUtc)}");
        sb.AppendLine($"{Tag} FirstPitchDetectedUtc={Fmt(markers.FirstPitchUtc)}");
        sb.AppendLine($"{Tag} TempoBpm={bpm} BeatDurationMs={msPerBeat.ToString("F1", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"{Tag} ClockRunning={session.IsListeningClockRunning} ElapsedTuneMs={elapsedMs.ToString("F0", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"{Tag} DetectedPitch={detectedNote ?? "-"} Freq={freq.ToString("F1", CultureInfo.InvariantCulture)} Cents={cents}");
        sb.AppendLine($"{Tag} ExpectedIndexBefore={indexBefore} ExpectedIndexAfter={indexAfter}");
        sb.AppendLine($"{Tag} ProcessingSummary={processingSummary}");
        sb.AppendLine($"{Tag} Note | Name | ScheduledMs | ElapsedMs | Delta | FeedbackWrong | Window | ReasonIfRed");
        sb.AppendLine($"{Tag} ---- | ---- | ----------- | --------- | ----- | ------------- | ------ | ------------");

        for (int i = 0; i < noteCount; i++)
        {
            var note = session.NotesToDraw[i];
            double scheduledMs = session.GetConductorExpectedOnsetMsPublic(i);
            double delta = elapsedMs - scheduledMs;
            int wrong = session.NoteFeedbacks.TryGetValue(i, out var fb) ? fb.Wrong : 0;
            string window = ClassifyWindow(session, i, elapsedMs, scheduledMs);
            string reason = wrong > 0 ? InferRedReason(session, i, window) : "-";
            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1,4} | {2,-4} | {3,11:F0} | {4,9:F0} | {5,5:F0} | {6,13} | {7,6} | {8}",
                Tag,
                i,
                Trunc(note.Name, 4),
                scheduledMs,
                elapsedMs,
                delta,
                wrong,
                window,
                reason));
        }

        Write(sb.ToString());
    }

    internal static void ResetForTests() => TestSink = null;

    private static string ClassifyWindow(
        NoteSessionService session,
        int noteIndex,
        double elapsedMs,
        double scheduledMs)
    {
        int bpm = session.GetConductorTimingBpmPublic();
        double earlyTol = ConductorOnsetTiming.EarlyToleranceMs(bpm);
        double lateTol = ConductorOnsetTiming.LateToleranceMs(bpm);

        if (ConductorOnsetTiming.IsTooEarly(elapsedMs, scheduledMs, earlyTol))
            return "EARLY";
        if (ConductorOnsetTiming.IsWindowExpired(elapsedMs, scheduledMs, lateTol))
            return "MISSED";
        if (ConductorOnsetTiming.IsWithinTimingWindow(elapsedMs, scheduledMs, earlyTol, lateTol))
            return "ON_TIME";
        if (elapsedMs > scheduledMs + lateTol)
            return "LATE";
        return "WAITING";
    }

    private static string InferRedReason(NoteSessionService session, int noteIndex, string window)
        => window switch
        {
            "MISSED" => "Conductor window expired (Missed)",
            "EARLY" => "Before earliest acceptable onset",
            "LATE" => "Late beyond tolerance",
            _ => "Marked wrong (see NoteStateDiag)",
        };

    private static string Fmt(DateTime? utc)
        => utc.HasValue ? utc.Value.ToString("O", CultureInfo.InvariantCulture) : "-";

    private static string Trunc(string? s, int max)
    {
        if (string.IsNullOrEmpty(s))
            return "-";
        return s.Length <= max ? s : s[..max];
    }

    private static void Write(string text)
    {
        if (TestSink != null)
        {
            TestSink(text);
            return;
        }

        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            DebugLog.WriteLine(DebugLogCategory.MusicPageFlow, line);
            Console.WriteLine(line);
            try
            {
                File.AppendAllText(NoteStateChangeDiagnostics.LogFilePath, line + Environment.NewLine);
            }
            catch
            {
                // best-effort
            }
        }
    }
}
#endif
