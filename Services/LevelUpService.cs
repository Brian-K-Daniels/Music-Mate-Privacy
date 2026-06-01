#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;
using musicmate.Models;

namespace musicmate.Services
{
    /// <summary>
    /// Evaluates whether a child should advance to the next level after a
    /// completed practice session, and persists the new level if so.
    ///
    /// ── Level-up rules ─────────────────────────────────────────────────────
    ///
    ///  Rule 1  Child-mode only.
    ///          Level-up is never triggered for adult / free-practice sessions
    ///          (ChildLevel == 0).
    ///
    ///  Rule 2  Same instrument + same level only.
    ///          Only <see cref="SessionResult"/> rows whose Instrument and Level
    ///          fields match the current session are considered.
    ///
    ///  Rule 3  Minimum notes per session.
    ///          Sessions with fewer than <see cref="MinNotesPerSession"/> non-rest
    ///          note slots are excluded (too short to be statistically meaningful).
    ///
    ///  Rule 4  Outlier sessions excluded.
    ///          Sessions whose OverallAccuracyPercent is 0 while TotalNotes > 0
    ///          are treated as data errors and excluded (same spirit as
    ///          NoteAttempt.IsReliableForStats).
    ///
    ///  Rule 5  Require a minimum number of qualifying sessions.
    ///          If fewer than <see cref="SessionCount"/> qualifying sessions exist
    ///          after applying Rules 2-4, level-up does not fire.
    ///
    ///  Rule 6  All qualifying sessions must meet every accuracy threshold.
    ///          The most recent <see cref="SessionCount"/> qualifying rows are
    ///          examined.  Every row must satisfy:
    ///            PitchAccuracyPercent   >= <see cref="MinPitchAccuracyPercent"/>
    ///            OverallAccuracyPercent >= <see cref="MinOverallAccuracyPercent"/>
    ///          Timing accuracy is checked only when AverageTimingMs > 0
    ///          (per-note timing not yet fully implemented — see Rule 7).
    ///
    ///  Rule 7  Missing timing data is not treated as a timing failure.
    ///          When AverageTimingMs == 0 the timing threshold is skipped for
    ///          that session, consistent with NoteAttempt.IsReliableForTimingStats.
    ///
    ///  Rule 8  Level cap.
    ///          Level never exceeds 100.
    ///
    /// ── Persistence ────────────────────────────────────────────────────────
    ///   The new level is written to Preferences under "ChildHome.Level".
    ///   ChildHomePage reads this key in its constructor and on OnAppearing,
    ///   so the displayed level updates automatically on the next visit.
    ///
    /// ── How to test level-up quickly ───────────────────────────────────────
    ///   1. Set LevelUpSessionCount  = 1  (one session is enough to qualify).
    ///   2. Set LevelUpMinPitchPct   = 1  (almost any accuracy qualifies).
    ///   3. Set LevelUpMinOverallPct = 1
    ///   4. Set LevelUpMinNotes      = 1  (even a 1-note session qualifies).
    ///   Complete one child-home session and confirm the banner appears.
    ///   Reset the constants to their defaults when done.
    /// </summary>
public static class LevelUpService
{
    // ── Default values for LevelUp criteria (used by AdvancePage and reset) ──
    public const int DefaultSessionCount = 3; // Rolling window size for advancement
    public const double DefaultMinPitchAccuracyPercent = 85.0;
    public const double DefaultMinTimingAccuracyPercent = 75.0;
    public const double DefaultMinOverallAccuracyPercent = 80.0;
    public const int DefaultMinNotesPerSession = 4;
    public const double DefaultMinOverallAccuracyFloor = 60.0; // New: minimum floor for any session in group
    /// <summary>
    /// Minimum overall accuracy (%) required for any session in the rolling group (floor).
    /// Default = 60.
    /// </summary>
    public static double MinOverallAccuracyFloor
        => Preferences.Default.Get("LevelUp.MinOverallAccuracyFloor", DefaultMinOverallAccuracyFloor);
        // ── Preference keys ────────────────────────────────────────────────────
        private const string PrefLevelKey = "ChildHome.Level";

        // ── Threshold constants ────────────────────────────────────────────────
        // Adjust here to change level-up sensitivity; nowhere else.

        /// <summary>
        /// Number of consecutive qualifying sessions that must all meet the
        /// accuracy thresholds before a level-up is awarded.  Default = 3.
        /// </summary>
        public static int SessionCount
            => Preferences.Default.Get("LevelUp.SessionCount", DefaultSessionCount);

        /// <summary>
        /// Minimum pitch accuracy (%) required in every qualifying session.
        /// Default = 85.
        /// </summary>
        public static double MinPitchAccuracyPercent
            => Preferences.Default.Get("LevelUp.MinPitchPct", DefaultMinPitchAccuracyPercent);

        /// <summary>
        /// Minimum timing accuracy (%) required when timing data is available.
        /// Timing is measured as a regularity score: lower stdDev relative to
        /// meanBeat = more consistent.  Currently the stored value is 0 when
        /// fewer than 2 notes were played; in that case the check is skipped
        /// (Rule 7).  Default = 75.
        /// </summary>
        public static double MinTimingAccuracyPercent
            => Preferences.Default.Get("LevelUp.MinTimingPct", DefaultMinTimingAccuracyPercent);

        /// <summary>
        /// Minimum overall accuracy (%) required in every qualifying session.
        /// Currently overall == pitch accuracy; will blend timing once it is
        /// calibrated.  Default = 80.
        /// </summary>
        public static double MinOverallAccuracyPercent
            => Preferences.Default.Get("LevelUp.MinOverallPct", DefaultMinOverallAccuracyPercent);

        /// <summary>
        /// Minimum number of non-rest note slots a session must contain to be
        /// counted as a qualifying session.  Very short sessions are excluded
        /// because their accuracy percentages are statistically unreliable.
        /// Default = 8.
        /// </summary>
        public static int MinNotesPerSession
            => Preferences.Default.Get("LevelUp.MinNotes", DefaultMinNotesPerSession);

        // ── Entry point ────────────────────────────────────────────────────────

        /// <summary>
        /// Checks whether the child should advance a level after the session
        /// whose result has just been saved.
        ///
        /// Returns the new level if a level-up occurred, or null if it did not.
        /// The caller is responsible for showing any UI feedback.
        /// </summary>
        /// <param name="resultDb">The session-result database to query.</param>
        /// <param name="currentLevel">The level that was just practised.</param>
        /// <param name="instrument">
        ///   Short instrument key, e.g. "Bb" — matches SessionResult.Instrument.
        /// </param>
        public static async Task<int?> CheckAndApplyLevelUpAsync(
            SessionResultDatabase resultDb,
            int currentLevel,
            string instrument)
        {
            // Rule 1: adult / free-practice sessions are never eligible.
            if (currentLevel <= 0) { Utilities.Utils.Log($"[LevelUpDebug] Not child mode: currentLevel={currentLevel}"); return null; }

            // Rule 8: already at the cap.
            if (currentLevel >= 100) { Utilities.Utils.Log($"[LevelUpDebug] Already at cap: currentLevel={currentLevel}"); return null; }

            try
            {
                Utilities.Utils.Log($"[LevelUpDebug] Entered CheckAndApplyLevelUpAsync: currentLevel={currentLevel}, instrument={instrument}");
                // Fetch all results for this level + instrument, newest first.
                var rows = await resultDb.GetByLevelAndInstrumentAsync(currentLevel, instrument);
                Utilities.Utils.Log($"[LevelUpDebug] Found {rows.Count} session results for level={currentLevel}, instrument={instrument}");

                // Rules 3 & 4: exclude sessions that are too short or look like data errors.
                var qualifying = rows
                    .Where(r => r.TotalNotes >= MinNotesPerSession)                 // Rule 3
                    .Where(r => !(r.TotalNotes > 0 && r.OverallAccuracyPercent == 0)) // Rule 4
                    .ToList();
                Utilities.Utils.Log($"[LevelUpDebug] {qualifying.Count} qualifying sessions after filters (min notes, not outlier)");

                // Rule 5: not enough qualifying sessions yet.
                if (qualifying.Count < SessionCount) { Utilities.Utils.Log($"[LevelUpDebug] Not enough qualifying sessions: {qualifying.Count} < {SessionCount}"); return null; }

                // Take the most recent N qualifying sessions
                var recent = qualifying.Take(SessionCount).ToList();

                // New: No session in the group can have overall accuracy below the floor
                double minFloor = MinOverallAccuracyFloor;
                if (recent.Any(r => r.OverallAccuracyPercent < minFloor))
                {
                    Utilities.Utils.Log($"[LevelUpDebug] At least one session in the group is below the overall accuracy floor: {minFloor}");
                    return null;
                }

                // Compute rolling averages
                double avgPitch = recent.Average(r => r.PitchAccuracyPercent);
                double avgOverall = recent.Average(r => r.OverallAccuracyPercent);
                // For timing, only include sessions with timing data
                var timingSessions = recent.Where(r => r.AverageTimingMs > 0).ToList();
                double avgTiming = timingSessions.Count > 0
                    ? timingSessions.Average(r => {
                        double cv = r.TimingStdDevMs / r.AverageTimingMs * 100.0;
                        return Math.Max(0, 100.0 - cv);
                    })
                    : 100.0; // If no timing data, treat as perfect

                Utilities.Utils.Log($"[LevelUpDebug] Rolling averages: Pitch={avgPitch:F1}, Timing={avgTiming:F1}, Overall={avgOverall:F1}");

                if (avgPitch < MinPitchAccuracyPercent)
                {
                    Utilities.Utils.Log($"[LevelUpDebug] Rolling average pitch accuracy below threshold: {avgPitch} < {MinPitchAccuracyPercent}");
                    return null;
                }
                if (avgOverall < MinOverallAccuracyPercent)
                {
                    Utilities.Utils.Log($"[LevelUpDebug] Rolling average overall accuracy below threshold: {avgOverall} < {MinOverallAccuracyPercent}");
                    return null;
                }
                if (avgTiming < MinTimingAccuracyPercent)
                {
                    Utilities.Utils.Log($"[LevelUpDebug] Rolling average timing accuracy below threshold: {avgTiming} < {MinTimingAccuracyPercent}");
                    return null;
                }

                // All criteria met — advance the level.
                int newLevel = Math.Min(currentLevel + 1, 100);  // Rule 8: cap at 100
                Preferences.Default.Set(PrefLevelKey, newLevel); // persist immediately
                Utilities.Utils.Log($"[LevelUp] Level {currentLevel} → {newLevel} " +
                                    $"(instrument={instrument}, sessions checked={SessionCount})");
                return newLevel;
            }
            catch (Exception ex)
            {
                Utilities.Utils.Log($"[LevelUpService] CheckAndApplyLevelUpAsync error: {ex}");
                return null;
            }
        }
    }
}
