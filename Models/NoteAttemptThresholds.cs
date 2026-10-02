namespace musicmate.Models
{
    /// <summary>
    /// Central location for all outlier-filtering thresholds used by
    /// <see cref="NoteAttempt.IsReliableForStats"/>.
    ///
    /// Constants are grouped into regions that match the rule numbers documented
    /// in NoteAttempt.IsReliableForStats so they are easy to find and adjust.
    ///
    /// IMPORTANT: changing a value here affects statistics calculations going
    /// forward but does NOT retroactively change already-saved rows.  Raw rows
    /// are never deleted by outlier filtering — only excluded from aggregations.
    /// </summary>
    public static class NoteAttemptThresholds
    {
        // ── Rule 1: Pitch error magnitude ────────────────────────────────────
        // A correct note accepted by Evaluate() was within the session tolerance
        // (typically 40–80 cents).  If a stored cents value exceeds this limit the
        // row is likely stale, race-condition data, or was written by a future code
        // path that stores error-from-target for wrong notes.
        // 200 cents = 2 semitones — conservatively above any realistic tolerance.
        public const int MaxPitchErrorCentsForCorrectNote = 200;

        // ── Rule 2: Pitch confidence ──────────────────────────────────────────
        // DEFERRED: DetectPitchMcLeod() returns only a frequency (0 = no pitch).
        // No confidence score is stored in NoteAttempt yet.
        // When a confidence field is added, add a threshold here.
        // public const double MinPitchConfidence = 0.9;

        // ── Rule 3: Detected note duration ────────────────────────────────────
        // DEFERRED: No per-note measured duration is stored in NoteAttempt yet.
        // When an ActualDurationMs field is added, add a threshold here, e.g.:
        // public const double MinDetectedDurationMs = 80.0;

        // ── Rule 5: Timing error magnitude ────────────────────────────────────
        // TimingErrorMs is currently always null (per-note timing not yet
        // implemented).  This threshold will apply once it is populated.
        // One beat at 60 BPM = 1000 ms; at 120 BPM = 500 ms.  Using a fixed
        // conservative cap: if the stored error exceeds this it is an outlier.
        public const double MaxTimingErrorMs = 2000.0;
    }
}
