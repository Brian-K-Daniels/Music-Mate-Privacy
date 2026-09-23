using System.Globalization;
using System.Text;

namespace musicmate.Diagnostics;

/// <summary>
/// Temporary Release-safe first-note diagnostics for Android logcat (no VS debugger required).
/// Filter: <c>adb logcat MusicMateFirstNote:I *:S</c> or <c>adb logcat | findstr MusicMateFirstNote</c>
/// Uses Android.Util.Log Info priority so entries survive default logcat filters that drop Debug.
/// </summary>
public static class FirstNoteAndroidReleaseLog
{
    public const string Tag = "MusicMateFirstNote";

    private static int _firstNoteAccepted;
    private static long _lastNonAcceptTicks;
    private const int MinNonAcceptIntervalMs = 250;

    public static void ResetForNewSession()
    {
        Interlocked.Exchange(ref _firstNoteAccepted, 0);
        Interlocked.Exchange(ref _lastNonAcceptTicks, 0);
    }

    public static bool StillWaitingForFirstAccept
        => Volatile.Read(ref _firstNoteAccepted) == 0;

    /// <summary>Always emits (not gated on first-note accept). Use for arming / mic failures.</summary>
    public static void WriteAlways(string stage, string detail)
    {
        // SPECIAL DEBUG FOR ANDROID LOG IN RELEASE MODE
        Emit($"{stage} {detail}");
    }

    public static void Log(
        string stage,
        bool isFirstNote,
        string? expectedWritten,
        int? expectedMidi,
        string? heardNote,
        double? heardHz,
        double? expectedHz,
        int? pitchErrorCents,
        float? rms,
        float? confidence,
        double? timingDeltaMs,
        string? earlyLate,
        bool? pitchPassed,
        bool? timingPassed,
        bool accepted,
        string? rejectReason,
        int? tempoBpm,
        string? countInOrConductorState,
        string? extra = null)
    {
        if (!isFirstNote || !StillWaitingForFirstAccept)
            return;

        if (accepted)
            Interlocked.Exchange(ref _firstNoteAccepted, 1);
        else if (!ShouldEmitNonAccept())
            return;

        var sb = new StringBuilder(384);
        sb.Append(stage);
        sb.Append(" firstNote=").Append(isFirstNote ? "yes" : "no");
        if (!string.IsNullOrWhiteSpace(expectedWritten))
            sb.Append(" expected=").Append(expectedWritten);
        if (expectedMidi.HasValue)
            sb.Append(" expectedMidi=").Append(expectedMidi.Value);
        if (!string.IsNullOrWhiteSpace(heardNote))
            sb.Append(" heard=").Append(heardNote);
        if (heardHz.HasValue)
            sb.Append(" heardHz=").Append(heardHz.Value.ToString("F1", CultureInfo.InvariantCulture));
        if (expectedHz.HasValue)
            sb.Append(" expectedHz=").Append(expectedHz.Value.ToString("F1", CultureInfo.InvariantCulture));
        if (pitchErrorCents.HasValue)
            sb.Append(" cents=").Append(pitchErrorCents.Value);
        if (rms.HasValue)
            sb.Append(" rms=").Append(rms.Value.ToString("F4", CultureInfo.InvariantCulture));
        if (confidence.HasValue)
            sb.Append(" confidence=").Append(confidence.Value.ToString("F2", CultureInfo.InvariantCulture));
        if (timingDeltaMs.HasValue)
            sb.Append(" timingDeltaMs=").Append(timingDeltaMs.Value.ToString("F0", CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(earlyLate))
            sb.Append(" earlyLate=").Append(earlyLate);
        if (pitchPassed.HasValue)
            sb.Append(" pitchPass=").Append(pitchPassed.Value ? "yes" : "no");
        if (timingPassed.HasValue)
            sb.Append(" timingPass=").Append(timingPassed.Value ? "yes" : "no");
        sb.Append(" accepted=").Append(accepted ? "yes" : "no");
        if (!string.IsNullOrWhiteSpace(rejectReason))
            sb.Append(" reason=").Append(rejectReason);
        if (tempoBpm.HasValue)
            sb.Append(" tempo=").Append(tempoBpm.Value);
        if (!string.IsNullOrWhiteSpace(countInOrConductorState))
            sb.Append(" cue=").Append(countInOrConductorState);
        if (!string.IsNullOrWhiteSpace(extra))
            sb.Append(' ').Append(extra);

        // SPECIAL DEBUG FOR ANDROID LOG IN RELEASE MODE
        Emit(sb.ToString());
    }

    private static void Emit(string line)
    {
#if ANDROID
        try
        {
            // Info (not Debug): many logcat captures / OEM filters drop Debug by default.
            global::Android.Util.Log.Info(Tag, line);
            global::Android.Util.Log.Debug(Tag, line);
        }
        catch { /* ignore */ }
#endif
        System.Diagnostics.Debug.WriteLine($"[{Tag}] {line}");
    }

    private static bool ShouldEmitNonAccept()
    {
        long now = Environment.TickCount64;
        long last = Volatile.Read(ref _lastNonAcceptTicks);
        if (last != 0 && now - last < MinNonAcceptIntervalMs)
            return false;
        Interlocked.Exchange(ref _lastNonAcceptTicks, now);
        return true;
    }
}
