using SQLite;
using System;

namespace musicmate.Models
{
    // NoteAttemptThresholds is in NoteAttemptThresholds.cs — update constants there,
    // not here, to adjust outlier-filtering behaviour.
    /// <summary>
    /// Records one note-playing attempt from a single practice session.
    ///
    /// "The same note" is identified by the combination of WrittenNoteName and Instrument
    /// (e.g. "C4" on "Bb Clarinet").  The rolling retention limit (MaxAttemptsPerNote) is
    /// applied per that composite key so each instrument/note pair keeps its own history.
    ///
    /// Nullable fields (TimingErrorMs, WasTimingCorrect) are included for future use;
    /// timing error per individual note is not yet tracked, so they default to null/false.
    /// </summary>
    [Table("NoteAttempts")]
    public class NoteAttempt
    {
        [PrimaryKey, AutoIncrement]
        public int AttemptId { get; set; }

        public DateTime DateTime { get; set; }

        /// <summary>GUID identifying the containing practice session, set once per session start.</summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Full instrument string from NoteSessionService (e.g. "Bb Clarinet, Bb, ...").</summary>
        public string Instrument { get; set; } = string.Empty;

        /// <summary>Child practice level (0 = adult/free practice, 1–100 = child level).</summary>
        public int Level { get; set; }

        /// <summary>
        /// Written note name including octave, e.g. "C4", "F#5".
        /// Together with Instrument this is the grouping key for the rolling limit.
        /// </summary>
        public string WrittenNoteName { get; set; } = string.Empty;

        /// <summary>
        /// Concert-pitch note name, derived by adding the instrument transpose offset.
        /// Empty when the transpose offset is zero (concert-pitch instrument).
        /// </summary>
        public string ConcertPitchNoteName { get; set; } = string.Empty;

        /// <summary>Written MIDI number for the target note.</summary>
        public int MidiNumber { get; set; }

        /// <summary>
        /// Expected note duration from the music sequence (null for classic single-note mode).
        /// Stored as the beat-value float from NoteDuration, e.g. 1.0 = quarter, 0.5 = eighth.
        /// </summary>
        public double? ExpectedDurationBeats { get; set; }

        /// <summary>Pitch error in cents at the moment the note was accepted (positive = sharp, negative = flat). 0 when wrong.</summary>
        public int PitchErrorCents { get; set; }

        /// <summary>Timing error in milliseconds relative to the expected beat (null = not yet tracked per-note).</summary>
        public double? TimingErrorMs { get; set; }

        public bool WasPitchCorrect { get; set; }

        /// <summary>Reserved for future timing scoring; null until per-note timing is implemented.</summary>
        public bool? WasTimingCorrect { get; set; }

        /// <summary>True when the note was both pitch-correct (and timing-correct once implemented).</summary>
        public bool WasOverallCorrect { get; set; }

        // ── Outlier / reliability filter ──────────────────────────────────────────
        //
        // IsReliableForStats is a computed property (not stored in the DB) that
        // returns true when this attempt should be included in statistical summaries.
        //
        // Raw rows are ALWAYS kept — this property only gates aggregation, never deletion.
        //
        // Rules (matched to NoteAttemptThresholds constants):
        //
        //   Rule 1 — Pitch error magnitude
        //     Exclude if WasPitchCorrect and |PitchErrorCents| > MaxPitchErrorCentsForCorrectNote.
        //     A correctly accepted note cannot legitimately exceed the session tolerance
        //     (~40–80 ¢). Values above 200 ¢ indicate stale or mis-attributed data.
        //
        //   Rule 2 — Pitch confidence          [DEFERRED — no confidence field yet]
        //     Would exclude if PitchConfidence < MinPitchConfidence.
        //     DetectPitchMcLeod currently returns 0 for silence, frequency otherwise;
        //     no scalar confidence score is stored in NoteAttempt.
        //
        //   Rule 3 — Detected note duration    [DEFERRED — no duration field yet]
        //     Would exclude if ActualDurationMs < MinDetectedDurationMs.
        //     Very short detections (<80 ms) are more likely transients than real attempts.
        //
        //   Rule 4 — Missing timing value
        //     Exclude from timing statistics when TimingErrorMs is null.
        //     Per-note timing is not yet implemented; null is normal and must not be
        //     treated as a zero-ms or a timing failure.
        //     (IsReliableForTimingStats handles this separately below.)
        //
        //   Rule 5 — Timing error magnitude    [threshold deferred — TimingErrorMs always null now]
        //     Would exclude if |TimingErrorMs| > MaxTimingErrorMs.
        //     One full beat at 60 BPM = 1000 ms; conservative cap = 2000 ms.
        //
        //   Rule 6 — Missing timing is NOT a failure
        //     TimingErrorMs == null means "not tracked yet", not "wrong timing".
        //     WasTimingCorrect stays null for the same reason.

        /// <summary>
        /// Returns true when this attempt should be included in pitch-accuracy
        /// and pitch-error statistics.  Always true when the attempt is wrong
        /// (wrong attempts are valid data points); applies only to cents magnitude
        /// for correct notes.
        ///
        /// Raw rows are never deleted; this is a filter flag only.
        /// See class-level comments for the full rule set.
        /// </summary>
        [Ignore]
        public bool IsReliableForStats
        {
            get
            {
                // Rule 1: correct notes with a suspiciously large cents value are excluded.
                // A wrong note with any cents value is kept (the cents field for wrong notes
                // is the deviation from the wrong note, which is not used in accuracy calcs).
                if (WasPitchCorrect
                    && Math.Abs(PitchErrorCents) > NoteAttemptThresholds.MaxPitchErrorCentsForCorrectNote)
                {
                    return false;
                }

                // Rules 2 and 3 are deferred — no confidence or duration field available yet.

                // All other attempts are considered reliable for pitch statistics.
                return true;
            }
        }

        /// <summary>
        /// Returns true when this attempt should be included in timing statistics.
        /// Rule 4: null timing means "not yet measured", not a valid zero or a failure.
        /// Rule 5: timing error beyond MaxTimingErrorMs is an outlier (active once non-null).
        /// </summary>
        [Ignore]
        public bool IsReliableForTimingStats
        {
            get
            {
                // Rule 4: exclude when timing was never measured.
                if (TimingErrorMs == null) return false;

                // Rule 5: exclude extreme timing outliers.
                if (Math.Abs(TimingErrorMs.Value) > NoteAttemptThresholds.MaxTimingErrorMs)
                    return false;

                return true;
            }
        }
    }
}
