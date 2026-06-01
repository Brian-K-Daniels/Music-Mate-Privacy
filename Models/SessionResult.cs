using SQLite;

namespace musicmate.Models
{
    /// <summary>
    /// One row per completed child-home practice session.
    /// Stored in a separate SQLite table so it does not affect the existing
    /// SessionStat / NoteDatabase tables used by the advanced practice pages.
    ///
    /// FUTURE (level-up criteria): query this table for the last N sessions at
    /// the current level and decide whether to advance.  Example:
    ///   var recent = await db.GetByLevelAsync(level, last: 5);
    ///   bool ready = recent.Count == 5 && recent.All(r => r.OverallAccuracyPercent >= 80);
    /// </summary>
    public class SessionResult
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        /// <summary>UTC timestamp when the session ended.</summary>
        public DateTime DateTime { get; set; }

        /// <summary>Short instrument key, e.g. "C", "Bb" — matches NoteSessionService.Instrument.</summary>
        public string Instrument { get; set; } = "";

        /// <summary>Child difficulty level (1–100) chosen on ChildHomePage.</summary>
        public int Level { get; set; }

        /// <summary>Total note slots in the session (correct + wrong attempts combined, deduplicated to unique notes).</summary>
        public int TotalNotes { get; set; }

        /// <summary>Number of notes the player sang/played correctly (first or after retries).</summary>
        public int CorrectPitchCount { get; set; }

        /// <summary>
        /// Number of incorrect pitch attempts across all notes.
        /// A note with 0 wrong attempts is a "first-time correct".
        /// </summary>
        public int WrongPitchCount { get; set; }

        /// <summary>
        /// Pitch accuracy: CorrectPitchCount / TotalNotes * 100.
        /// A note that required retries still counts as 1 correct out of TotalNotes.
        /// </summary>
        public double PitchAccuracyPercent { get; set; }

        /// <summary>
        /// Mean absolute pitch deviation in cents across all correctly identified notes.
        /// Sourced from NoteSessionService.NoteFeedbacks[i].Cents for correct indices.
        /// 0 if no correct notes.
        /// </summary>
        public double AveragePitchErrorCents { get; set; }

        /// <summary>
        /// Mean inter-note interval in milliseconds (timing regularity).
        /// Sourced from NoteSessionService.GetFinalBpmStats — converted from BPM.
        /// Null / 0 when timing data is unavailable (e.g., fewer than 2 notes played).
        /// </summary>
        public double AverageTimingMs { get; set; }

        /// <summary>
        /// Timing standard deviation in milliseconds.
        /// Lower = more rhythmically consistent.
        /// </summary>
        public double TimingStdDevMs { get; set; }

        // ── Overall ────────────────────────────────────────────────────────────

        /// <summary>
        /// Composite score: currently equal to PitchAccuracyPercent.
        /// FUTURE: blend pitch + timing accuracy once timing scoring is tuned.
        /// </summary>
        public double OverallAccuracyPercent { get; set; }

        // ── Computed helpers (not stored) ──────────────────────────────────────

        [Ignore]
        public string Summary =>
            $"Level {Level} | {Instrument} | {OverallAccuracyPercent:F0}% | {DateTime:g}";
    }
}
