using System.Globalization;
using System.Text;

namespace musicmate.Diagnostics;

/// <summary>
/// Temporary Release-safe note-grade diagnostics (no VS debugger required).
/// Filter: <c>adb logcat -s MusicMateNoteGrade</c>
/// </summary>
public static class NoteGradeDiagnostics
{
    public const string Tag = "MusicMateNoteGrade";

    public static void Log(string message)
    {
        try
        {
#if ANDROID
            global::Android.Util.Log.Info(Tag, message);
#endif
        }
        catch { /* ignore */ }

        System.Diagnostics.Debug.WriteLine($"[{Tag}] {message}");
    }

    public static void LogCandidate(
        string stage,
        int noteIndex,
        string? expected,
        double? expectedOnsetMs,
        double? candidateMs,
        double? deltaMs,
        double detectedHz,
        double? expectedHz,
        int cents,
        float rms,
        float confidence,
        bool passedRms,
        bool passedConfidence,
        bool passedPitch,
        string? timingClass,
        bool setHadEarlyCandidate,
        string? detail = null)
    {
        var sb = new StringBuilder(256);
        sb.Append(stage);
        sb.Append(" noteIdx=").Append(noteIndex);
        if (!string.IsNullOrWhiteSpace(expected))
            sb.Append(" expected=").Append(expected);
        if (expectedOnsetMs.HasValue)
            sb.Append(" expectedOnsetMs=").Append(expectedOnsetMs.Value.ToString("F0", CultureInfo.InvariantCulture));
        if (candidateMs.HasValue)
            sb.Append(" candidateMs=").Append(candidateMs.Value.ToString("F0", CultureInfo.InvariantCulture));
        if (deltaMs.HasValue)
            sb.Append(" deltaMs=").Append(deltaMs.Value.ToString("F0", CultureInfo.InvariantCulture));
        sb.Append(" hz=").Append(detectedHz.ToString("F1", CultureInfo.InvariantCulture));
        if (expectedHz.HasValue)
            sb.Append(" expectedHz=").Append(expectedHz.Value.ToString("F1", CultureInfo.InvariantCulture));
        sb.Append(" cents=").Append(cents);
        sb.Append(" rms=").Append(rms.ToString("F4", CultureInfo.InvariantCulture));
        sb.Append(" clarity=").Append(confidence.ToString("F2", CultureInfo.InvariantCulture));
        sb.Append(" passRms=").Append(passedRms ? "yes" : "no");
        sb.Append(" passClarity=").Append(passedConfidence ? "yes" : "no");
        sb.Append(" passPitch=").Append(passedPitch ? "yes" : "no");
        if (!string.IsNullOrWhiteSpace(timingClass))
            sb.Append(" timing=").Append(timingClass);
        sb.Append(" setHadEarly=").Append(setHadEarlyCandidate ? "yes" : "no");
        if (!string.IsNullOrWhiteSpace(detail))
            sb.Append(' ').Append(detail);
        Log(sb.ToString());
    }

    public static void LogFinal(
        int noteIndex,
        string? expected,
        bool pitchCorrect,
        bool? timingCorrect,
        bool overallCorrect,
        bool hadEarlyCandidate,
        string wrongReason,
        double? timingErrorMs,
        string why)
    {
        var sb = new StringBuilder(192);
        sb.Append("final noteIdx=").Append(noteIndex);
        if (!string.IsNullOrWhiteSpace(expected))
            sb.Append(" expected=").Append(expected);
        sb.Append(" pitchOk=").Append(pitchCorrect ? "yes" : "no");
        sb.Append(" timingOk=").Append(timingCorrect is null ? "n/a" : timingCorrect.Value ? "yes" : "no");
        sb.Append(" overallOk=").Append(overallCorrect ? "yes" : "no");
        sb.Append(" hadEarlyCandidate=").Append(hadEarlyCandidate ? "yes" : "no");
        sb.Append(" wrongReason=").Append(string.IsNullOrEmpty(wrongReason) ? "(none)" : wrongReason);
        if (timingErrorMs.HasValue)
            sb.Append(" timingErrorMs=").Append(timingErrorMs.Value.ToString("F0", CultureInfo.InvariantCulture));
        sb.Append(" why=").Append(why);
        Log(sb.ToString());
    }
}
