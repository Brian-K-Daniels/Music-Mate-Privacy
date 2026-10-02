using musicmate.LayoutDebug;
using musicmate.Models;
using musicmate.Utilities;

namespace musicmate.Services
{
    /// <summary>Persists session statistics and child level-up results (no UI).</summary>
    public static class PracticeSessionPersistence
    {
        public sealed class SaveOutcome
        {
            public bool Skipped { get; init; }
            public string? SkipReason { get; init; }
            public int? NewChildLevel { get; init; }
            /// <summary>
            /// When level-up kept an activity that is not in the new level's pool,
            /// a short explanation for the user (no silent Assortment substitution).
            /// </summary>
            public string? ActivityWarning { get; init; }
        }

        public static SessionStat BuildSessionStat(NoteSessionService session)
        {
            var (correct, wrongRetries, retryWeightedPc) = session.GetSessionCorrectWrongTotals();
            double completionPc = session.GetSessionCompletionPercent();
            var detectedBpm = session.GetDetectedBpm();
            var hi = session.NotesToDraw.OrderByDescending(n => n.Midi).FirstOrDefault();
            var lo = session.NotesToDraw.OrderBy(n => n.Midi).FirstOrDefault();
            double? timingAccuracyPercent = session.GetTimingAccuracyPercent();
            // My Progress headline: completion-friendly pitch % (retries do not punish Pc%).
            double overallAccuracy = timingAccuracyPercent.HasValue
                ? (completionPc + timingAccuracyPercent.Value) / 2.0
                : completionPc;

            var (pitchRight, pitchWrong, timingRight, timingWrong,
                 overallRight, overallWrong, restRight, restWrong) = session.GetSessionSummaryCounts();

            return new SessionStat
            {
                Dt = DateTime.Now,
                Key = session.Key,
                Tune = session.Tune ?? string.Empty,
                Instrument = session.InstrumentDisplayName,
                What = PlayModePickerOptions.BuildSessionWhatLabel(
                    session,
                    LayoutTestTune.IsEnabled,
                    SessionPreferences.Get("SelectedTune", string.Empty)),
                Rand = session.IsRandomMode,
                AccPct = session.AccidentalPercent,
                Hi = hi?.Name ?? "",
                Lo = lo?.Name ?? "",
                Pc = completionPc,
                PcRaw = retryWeightedPc,
                Tp = detectedBpm ?? 0,
                Ts = 0,
                Level = session.ChildLevel,
                Pch = completionPc,
                Tmg = timingAccuracyPercent ?? 0.0,
                Ovrl = overallAccuracy,
                PitchRightCount = pitchRight,
                PitchWrongCount = pitchWrong,
                TimingRightCount = timingRight,
                TimingWrongCount = timingWrong,
                OverallRightCount = overallRight,
                OverallWrongCount = overallWrong,
                RestRightCount = restRight,
                RestWrongCount = restWrong
            };
        }

        public static SessionResult BuildSessionResult(NoteSessionService session, double pitchAccuracyPercent)
        {
            var totalNotes = session.NotesToDraw.Count(n => !n.IsRest);
            var (correctCount, _, _) = session.GetSessionCorrectWrongTotals();
            var (pitchRight, pitchWrong, timingRight, timingWrong,
                 overallRight, overallWrong, restRight, restWrong) = session.GetSessionSummaryCounts();

            double avgCents = 0;
            var correctIndices = session.CorrectNoteIndices;
            if (correctIndices.Count > 0)
            {
                var centsList = correctIndices
                    .Where(i => session.NoteFeedbacks.ContainsKey(i))
                    .Select(i => Math.Abs(session.NoteFeedbacks[i].Cents))
                    .Where(c => c <= NoteAttemptThresholds.MaxPitchErrorCentsForCorrectNote)
                    .ToList();
                if (centsList.Count > 0)
                    avgCents = centsList.Average();
            }

            double? timingAccuracyPercent = session.GetTimingAccuracyPercent();
            int? detectedBpm = session.GetDetectedBpm();
            double overallAccuracy = timingAccuracyPercent.HasValue
                ? (pitchAccuracyPercent + timingAccuracyPercent.Value) / 2.0
                : pitchAccuracyPercent;

            return new SessionResult
            {
                DateTime = DateTime.UtcNow,
                Instrument = session.InstrumentKey,
                Level = session.ChildLevel,
                TotalNotes = totalNotes,
                CorrectPitchCount = (int)correctCount,
                // Actual wrong-pitch attempt outcomes (not timing-gate retry counters).
                WrongPitchCount = pitchWrong,
                PitchAccuracyPercent = pitchAccuracyPercent,
                AveragePitchErrorCents = avgCents,
                TimingAccuracyPercent = timingAccuracyPercent,
                DetectedBpm = detectedBpm,
                OverallAccuracyPercent = overallAccuracy,
                PitchRightCount = pitchRight,
                PitchWrongCount = pitchWrong,
                TimingRightCount = timingRight,
                TimingWrongCount = timingWrong,
                OverallRightCount = overallRight,
                OverallWrongCount = overallWrong,
                RestRightCount = restRight,
                RestWrongCount = restWrong,
            };
        }

        public static async Task<SaveOutcome> SaveSessionStatAsync(
            NoteSessionService session,
            SessionDatabase? sessionDb,
            SessionResultDatabase? sessionResultDb,
            bool collectSessionStats,
            long maxSessionDbSizeBytes)
        {
            if (sessionDb == null)
            {
                Utils.Log("[LevelUpDebug] sessionDb is null");
                return new SaveOutcome { Skipped = true, SkipReason = "no session db" };
            }

            if (session.Tune == "Tuner")
            {
                Utils.Log("[LevelUpDebug] Tuner session, skipping");
                return new SaveOutcome { Skipped = true, SkipReason = "tuner" };
            }

            if (!collectSessionStats)
            {
                Utils.Log("[LevelUpDebug] CollectSessionStats is false");
                return new SaveOutcome { Skipped = true, SkipReason = "prefs" };
            }

            await sessionDb.InitializeAsync();

            var stat = BuildSessionStat(session);
            await sessionDb.InsertAsync(stat);
            await sessionDb.PruneToSizeLimitAsync(maxSessionDbSizeBytes);
            ServiceHelper.GetService<StatisticsCacheService>()?.InvalidateSessionStats();

            Utils.Log($"[LevelUpDebug] _session.ChildLevel={session.ChildLevel}, sessionResultDb null?={sessionResultDb == null}");
            if (session.ChildLevel <= 0)
                return new SaveOutcome { Skipped = false };

            // Central gate: saved tunes keep SessionStat / note-attempt stats, but must not
            // write SessionResult (level-up progress) or call CheckAndApplyLevelUpAsync.
            if (!LevelUpService.CountsTowardLevelAdvancement(session))
            {
                Utils.Log(
                    "[LevelUpDebug] Saved tune session — SessionStat recorded; " +
                    "skipping SessionResult and level-up evaluation");
                return new SaveOutcome { Skipped = false, SkipReason = "saved-tune" };
            }

            // Level-up uses attempt-based pitch accuracy (wrong pitch only), not retry-weighted Pc%.
            double levelUpPitchPct = session.GetSessionLevelUpPitchAccuracyPercent();
            await SaveSessionResultAsync(session, sessionResultDb, levelUpPitchPct);

            if (sessionResultDb == null)
            {
                Utils.Log("[LevelUpDebug] sessionResultDb is null inside ChildLevel>0 block");
                return new SaveOutcome { Skipped = false };
            }

            var shortInstrument = session.InstrumentKey;
            Utils.Log($"[LevelUpDebug] Calling CheckAndApplyLevelUpAsync: level={session.ChildLevel}, instrument={shortInstrument}");
            var newLevel = await LevelUpService.CheckAndApplyLevelUpAsync(
                sessionResultDb, session.ChildLevel, shortInstrument);

            if (newLevel.HasValue)
            {
                Utils.Log($"[LevelUpDebug] Level up! New level={newLevel.Value}");
                session.ChildLevel = newLevel.Value;
                DifficultyLevelMapper.ApplyLevelUpToSession(
                    newLevel.Value, session, out var activityWarning);
                return new SaveOutcome
                {
                    Skipped = false,
                    NewChildLevel = newLevel,
                    ActivityWarning = activityWarning,
                };
            }

            Utils.Log("[LevelUpDebug] No level up this session.");
            return new SaveOutcome { Skipped = false };
        }

        public static async Task SaveSessionResultAsync(
            NoteSessionService session,
            SessionResultDatabase? sessionResultDb,
            double pitchAccuracyPercent)
        {
            if (sessionResultDb == null)
                return;

            try
            {
                await sessionResultDb.InitializeAsync();
                var result = BuildSessionResult(session, pitchAccuracyPercent);
                await sessionResultDb.InsertAsync(result);

                Utils.Log($"[SessionResult] Saved: Level={result.Level}, " +
                          $"Correct={result.CorrectPitchCount}/{result.TotalNotes}, " +
                          $"Pitch={result.PitchAccuracyPercent:F1}%, " +
                          $"AvgCents={result.AveragePitchErrorCents:F1}, " +
                          $"Timing={result.TimingAccuracyPercent?.ToString("F1") ?? "N/A"}%");
            }
            catch (Exception ex)
            {
                Utils.Log($"[SessionResult] SaveSessionResultAsync error: {ex}");
            }
        }
    }
}
