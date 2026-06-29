using musicmate.Drawables;
using musicmate.Models;

namespace musicmate.Services
{
    public enum PracticeExerciseStartAction
    {
        GenerateFresh,
        RestoreRepeatSame,
        RegenerateAfterMasteryFilter
    }

    /// <summary>
    /// Pure session-lifecycle helpers extracted from MusicPage (Repeat Same, mastery filter, snapshot).
    /// UI/audio wiring remains on the page.
    /// </summary>
    public static class PracticeSessionLifecycle
    {
        public const int MinNotesAfterMasteryFilter = 2;

        public readonly record struct ExerciseStartPlan(
            PracticeExerciseStartAction Action,
            bool LogScaleKeyWithoutChanging);

        public static ExerciseStartPlan PlanExerciseStart(
            bool repeatSameTune,
            PracticeSessionSnapshot? snapshot,
            bool forceNewNotes,
            string? scaleKeyTrigger)
        {
            bool repeatSameActive = !forceNewNotes
                && repeatSameTune
                && snapshot?.Notes.Count > 0;

            if (!repeatSameActive)
                return new ExerciseStartPlan(PracticeExerciseStartAction.GenerateFresh, false);

            bool logOnly = scaleKeyTrigger is "GoButton" or "AutoStart";
            return new ExerciseStartPlan(PracticeExerciseStartAction.RestoreRepeatSame, logOnly);
        }

        public static PracticeSessionSnapshot? CaptureSnapshot(
            NoteSessionService session,
            IReadOnlyList<NoteInfo> notes,
            StaffLayoutCapture staff)
        {
            if (notes.Count == 0)
                return null;

            return new PracticeSessionSnapshot
            {
                Notes = new List<NoteInfo>(notes),
                Key = session.Key,
                SelectedScale = session.SelectedScale,
                EffectiveScale = session.EffectiveScale,
                ScaleSelectionMode = session.ScaleSelectionMode,
                IsRandomMode = session.IsRandomMode,
                Tune = session.Tune ?? string.Empty,
                UpperNotes = new List<GeneratedNote>(staff.UpperNotes),
                LowerNotes = new List<GeneratedNote>(staff.LowerNotes),
                UpperBarBeats = new List<double>(staff.UpperBarBeats),
                LowerBarBeats = new List<double>(staff.LowerBarBeats),
                UpperPitchCount = staff.UpperPitchCount,
                UpperHasEndBar = staff.UpperHasEndBar,
            };
        }

        public static void RestoreGenerationContext(NoteSessionService session, PracticeSessionSnapshot snapshot)
        {
            session.RestoreRepeatSameGenerationContext(
                snapshot.Key,
                snapshot.SelectedScale,
                snapshot.EffectiveScale,
                snapshot.ScaleSelectionMode,
                snapshot.IsRandomMode,
                snapshot.Tune);
        }

        public static void RestoreNotesToSession(NoteSessionService session, IReadOnlyList<NoteInfo> notes)
        {
            session.NotesToDraw.Clear();
            foreach (var note in notes)
                session.NotesToDraw.Add(note);

            session.FeedbackViewModels.Clear();
            for (int i = 0; i < notes.Count; i++)
                session.FeedbackViewModels.Add(new FeedbackItem(i, 0, 0, false));
        }

        /// <summary>Restores staff drawable state from a Repeat Same snapshot (no UI invalidation).</summary>
        public static int RestoreStaffDrawable(StaffDrawable staff, PracticeSessionSnapshot snapshot)
        {
            staff.UpperNotes = new List<GeneratedNote>(snapshot.UpperNotes);
            staff.LowerNotes = new List<GeneratedNote>(snapshot.LowerNotes);
            staff.UpperBarBeats = new List<double>(snapshot.UpperBarBeats);
            staff.LowerBarBeats = new List<double>(snapshot.LowerBarBeats);
            staff.UpperHasEndBar = snapshot.UpperHasEndBar;
            staff.UpperNoteStates = new StaffNoteState[snapshot.UpperNotes.Count];
            staff.LowerNoteStates = new StaffNoteState[snapshot.LowerNotes.Count];
            staff.IsUpperActive = true;
            staff.ActiveNoteIndex = 0;
            staff.UpperAlpha = 1f;
            staff.LowerAlpha = 1f;

            for (int i = 0; i < snapshot.UpperNotes.Count; i++)
            {
                if (!snapshot.UpperNotes[i].IsRest)
                {
                    staff.UpperNoteStates[i] = StaffNoteState.Current;
                    staff.ActiveNoteIndex = i;
                    break;
                }
            }

            staff.InvalidateLayoutCache();
            return snapshot.UpperPitchCount;
        }

        public static async Task<List<NoteInfo>> FilterNotesExcludingMasteredAsync(
            IEnumerable<NoteInfo> notes,
            NoteSessionService session,
            NoteDatabase? db)
        {
            var list = notes.ToList();
            if (db == null)
                return list;

            await db.InitializeAsync();
            var stats = (await db.GetAllAsync()).ToDictionary(s => s.WrittenName, s => s);
            return list.Where(n =>
            {
                if (!stats.TryGetValue(n.Name, out var stat))
                    return true;
                return !MasteryEvaluator.IsFullyMastered(stat, session);
            }).ToList();
        }

        public static async Task<List<NoteInfo>> ResolveRepeatSameNotesAsync(
            PracticeSessionSnapshot snapshot,
            NoteSessionService session,
            NoteDatabase? db)
        {
            return await FilterNotesExcludingMasteredAsync(snapshot.Notes, session, db);
        }

        /// <summary>Cancels any in-flight session start and returns a fresh token source.</summary>
        public static CancellationTokenSource ReplaceSessionStartCancellation(CancellationTokenSource? current)
        {
            current?.Cancel();
            return new CancellationTokenSource();
        }

        public static string NewSessionId() => Guid.NewGuid().ToString();

        public enum StopToggleAction
        {
            StopRestoreRepeatSame,
            StopRegenerateFresh,
            StartListening
        }

        public readonly record struct StopTogglePlan(
            StopToggleAction Action,
            bool ForceNewNotes,
            string? ScaleKeyTrigger,
            bool ClearRepeatSameSnapshot);

        public static StopTogglePlan PlanStopToggle(bool isRunning, bool repeatSameTune, PracticeSessionSnapshot? snapshot)
        {
            bool hasSnapshot = snapshot?.Notes.Count > 0;

            if (isRunning)
            {
                if (repeatSameTune && hasSnapshot)
                    return new StopTogglePlan(StopToggleAction.StopRestoreRepeatSame, false, null, false);

                return new StopTogglePlan(StopToggleAction.StopRegenerateFresh, false, null, true);
            }

            bool repeatSameStart = repeatSameTune && hasSnapshot;
            return new StopTogglePlan(
                StopToggleAction.StartListening,
                ForceNewNotes: !repeatSameStart,
                ScaleKeyTrigger: "GoButton",
                ClearRepeatSameSnapshot: false);
        }

        public readonly record struct CompletionSummaryStats(
            double Correct,
            double Wrong,
            double AccuracyPercent,
            int? DetectedBpm);

        public static CompletionSummaryStats CaptureCompletionSummary(NoteSessionService session)
        {
            var (correct, wrong, apc) = session.GetSessionCorrectWrongTotals();
            return new CompletionSummaryStats(correct, wrong, apc, session.GetDetectedBpm());
        }

        public static string FormatSessionResultBanner(
            CompletionSummaryStats stats,
            int? newChildLevel)
        {
            var total = (int)(stats.Correct + stats.Wrong);
            var bpmText = stats.DetectedBpm.HasValue
                ? $"  ·  Detected {stats.DetectedBpm.Value} BPM"
                : string.Empty;
            var levelUpText = newChildLevel.HasValue
                ? $"  🎉 Great job! You advanced to Level {newChildLevel.Value}!"
                : string.Empty;
            return $"✓ {stats.AccuracyPercent:F0}% correct  ({(int)stats.Correct}/{total}){bpmText}{levelUpText}";
        }

        public static bool ShouldAutoRepeat(bool autoRepeatEnabled, string tune)
            => autoRepeatEnabled && tune != "Tuner";

        /// <summary>
        /// Repeat New (auto-repeat with <paramref name="repeatSameTune"/> false) must
        /// regenerate at the current level; Repeat Same restores the saved snapshot.
        /// </summary>
        public static bool ShouldForceNewNotesForRepeatMode(bool repeatSameTune)
            => !repeatSameTune;

        public static int GetAutoRepeatDelayMs(double repeatDelaySeconds)
            => (int)(repeatDelaySeconds * 1000);
    }

    public readonly record struct StaffLayoutCapture(
        IReadOnlyList<GeneratedNote> UpperNotes,
        IReadOnlyList<GeneratedNote> LowerNotes,
        IReadOnlyList<double> UpperBarBeats,
        IReadOnlyList<double> LowerBarBeats,
        int UpperPitchCount,
        bool UpperHasEndBar);
}
