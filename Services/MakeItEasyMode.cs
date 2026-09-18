namespace musicmate.Services
{
    /// <summary>
    /// Beginner-friendly "Make It Easy" detection preset.
    /// Temporarily widens timing / debounce via the existing grading pipeline;
    /// does not invent a separate scoring system.
    /// </summary>
    public static class MakeItEasyMode
    {
        public const string PrefActiveKey = "musicmate.MakeItEasy.Active";
        /// <summary>Factory default and fresh-install value for Make It Easy.</summary>
        public const bool DefaultActive = true;
        public const string PrefSavedToleranceKey = "musicmate.MakeItEasy.SavedTolerance";
        public const string PrefSavedWrongDebounceMsKey = "musicmate.MakeItEasy.SavedWrongDebounceMs";
        public const string PrefSavedCooldownMsKey = "musicmate.MakeItEasy.SavedCooldownMs";

        /// <summary>
        /// Pitch cents window while Easy is on.
        /// Slightly wider than the factory 50¢ so beginners are not punished for
        /// mild intonation, but still well below a semitone (100¢) so an obviously
        /// wrong pitch class cannot count as correct.
        /// </summary>
        public const int ToleranceCents = 70;

        /// <summary>
        /// Wrong-note debounce while Easy is on (ms).
        /// First wrong sighting only arms a timer; the note turns red only if the
        /// wrong pitch remains stable for this long — ignoring brief detection glitches.
        /// </summary>
        public const int WrongDebounceMs = 1800;

        /// <summary>
        /// Post-accept audio cooldown while Easy is on (ms).
        /// Slightly above the factory 50 ms so residual buzz does not immediately
        /// score the next note, while still short enough that repeated same-pitch
        /// notes remain separable after a real re-attack / silence gate.
        /// </summary>
        public const int CooldownMs = 90;

        /// <summary>
        /// Early accept window as a fraction of one beat (~1¼ beats).
        /// Lets a correct onset that is substantially early still turn green.
        /// </summary>
        public const double EarlyToleranceBeats = 1.25;

        /// <summary>
        /// Late accept / miss window as a fraction of one beat (2 beats).
        /// Gives beginners room to hesitate without immediately marking Late/Missed,
        /// and avoids cascading red notes when they lose the beat briefly.
        /// </summary>
        public const double LateToleranceBeats = 2.0;

        /// <summary>Floor so fast tempos still leave a playable Easy window.</summary>
        public const double MinToleranceMs = 250.0;

        /// <summary>
        /// Ceiling above the normal 1200 ms clamp so slow practice tempos keep the
        /// full multi-beat Easy window instead of being truncated.
        /// </summary>
        public const double MaxToleranceMs = 5000.0;

        /// <summary>
        /// Silence gap (in beats) before the musical timeline auto-pauses / rebases.
        /// Must not exceed <see cref="LateToleranceBeats"/> — otherwise a long hesitation
        /// can expire the late window and score Late/Missed without ever rebasing, which
        /// leaves later notes measured against the original wall-clock origin.
        /// Still longer than ordinary inter-note gaps (~1 beat) so brief tonguing does
        /// not freeze the timeline.
        /// </summary>
        public const double PauseSilenceBeats = 2.0;

        public readonly record struct SettingsSnapshot(
            int Tolerance,
            int WrongDebounceMs,
            int CooldownMs);

        public static SettingsSnapshot Capture(NoteSessionService session)
            => new(session.Tolerance, session.WrongDebounceMs, session.CooldownMs);

        public static void ApplyPreset(NoteSessionService session)
        {
            session.Tolerance = ToleranceCents;
            session.WrongDebounceMs = WrongDebounceMs;
            session.CooldownMs = CooldownMs;
        }

        public static void Restore(NoteSessionService session, SettingsSnapshot snapshot)
        {
            session.Tolerance = snapshot.Tolerance;
            session.WrongDebounceMs = snapshot.WrongDebounceMs;
            session.CooldownMs = snapshot.CooldownMs;
        }

        public static void PersistSnapshot(SettingsSnapshot snapshot)
        {
            SessionPreferences.Set(PrefSavedToleranceKey, snapshot.Tolerance);
            SessionPreferences.Set(PrefSavedWrongDebounceMsKey, snapshot.WrongDebounceMs);
            SessionPreferences.Set(PrefSavedCooldownMsKey, snapshot.CooldownMs);
        }

        public static SettingsSnapshot? TryLoadSnapshot()
        {
            if (!SessionPreferences.ContainsKey(PrefSavedToleranceKey)
                || !SessionPreferences.ContainsKey(PrefSavedWrongDebounceMsKey)
                || !SessionPreferences.ContainsKey(PrefSavedCooldownMsKey))
            {
                return null;
            }

            return new SettingsSnapshot(
                SessionPreferences.Get(PrefSavedToleranceKey, NoteSessionService.DefaultTolerance),
                SessionPreferences.Get(PrefSavedWrongDebounceMsKey, NoteSessionService.DefaultDebounceMs),
                SessionPreferences.Get(PrefSavedCooldownMsKey, NoteSessionService.DefaultCooldownMs));
        }

        public static void ClearPersistedSnapshot()
        {
            SessionPreferences.Remove(PrefSavedToleranceKey);
            SessionPreferences.Remove(PrefSavedWrongDebounceMsKey);
            SessionPreferences.Remove(PrefSavedCooldownMsKey);
        }

        public static ConductorOnsetTiming.TimingWindowProfile TimingProfile { get; } =
            new(
                EarlyToleranceBeats,
                LateToleranceBeats,
                MinToleranceMs,
                MaxToleranceMs);
    }
}
