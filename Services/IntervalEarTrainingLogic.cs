using System.Globalization;

namespace musicmate.Services
{
    /// <summary>
    /// How Interval Ear Training should treat the staff: hide notes during an
    /// unanswered quiz, reveal them after a correct answer, or show them immediately
    /// for manual (non-quiz) playback.
    /// </summary>
    public enum IntervalEarTrainingInteraction
    {
        UnansweredQuiz,
        RevealedQuiz,
        ManualPlayback,
    }

    /// <summary>User-selected direction mode for Interval Ear Training.</summary>
    public enum IntervalDirectionMode
    {
        Ascending = 0,
        Descending = 1,
        Random = 2,
    }

    /// <summary>
    /// Pure pitch-selection and quiz helpers for Interval Ear Training (unit-testable).
    /// </summary>
    public static class IntervalEarTrainingLogic
    {
        /// <summary>Shortest audible note length when the user selects 0 ms.</summary>
        public const int MinAudibleNoteDurationMs = 30;

        public const int DefaultNoteDurationMs = 500;
        public const int MinNoteDurationMs = 0;
        public const int MaxNoteDurationMs = 1000;
        public const string NoteDurationPreferenceKey = "musicmate.EarTrainingNoteDurationMs";
        public const string DirectionPreferenceKey = "musicmate.EarTrainingDirection";
        public const string StartNotePreferenceKey = "musicmate.EarTrainingLowerNote";
        public const string RandomStartNoteToken = "Random";
        public const IntervalDirectionMode DefaultDirectionMode = IntervalDirectionMode.Ascending;

        public readonly record struct IntervalPitches(
            int StartWrittenMidi,
            int EndWrittenMidi,
            int Semitones,
            bool IsAscending);

        public static int ClampNoteDurationMs(int ms)
            => Math.Clamp(ms, MinNoteDurationMs, MaxNoteDurationMs);

        /// <summary>Duration actually sent to the audio engine (never zero).</summary>
        public static double ResolvePlaybackNoteSeconds(int storedDurationMs)
        {
            int ms = ClampNoteDurationMs(storedDurationMs);
            if (ms <= 0)
                ms = MinAudibleNoteDurationMs;
            return ms / 1000.0;
        }

        public static IntervalDirectionMode ParseDirectionMode(string? value)
        {
            if (Enum.TryParse(value, ignoreCase: true, out IntervalDirectionMode mode)
                && Enum.IsDefined(mode))
                return mode;
            return DefaultDirectionMode;
        }

        public static string FormatDirectionLabel(bool isAscending)
            => isAscending ? "ascending" : "descending";

        /// <summary>
        /// Resolves Ascending / Descending / Random into a concrete direction for one interval.
        /// Unison is treated as ascending (same pitch twice).
        /// </summary>
        public static bool ResolveIsAscending(IntervalDirectionMode mode, int semitones, Random rng)
        {
            if (semitones == 0)
                return true;
            return mode switch
            {
                IntervalDirectionMode.Descending => false,
                IntervalDirectionMode.Random => rng.Next(2) == 0,
                _ => true,
            };
        }

        /// <summary>
        /// Picks a random interval of <paramref name="semitones"/> that fits in
        /// [lowWrittenMidi, highWrittenMidi] inclusive, in the given direction mode.
        /// For unison, both pitches match.
        /// </summary>
        public static bool TryPickInterval(
            int lowWrittenMidi,
            int highWrittenMidi,
            int semitones,
            IntervalDirectionMode directionMode,
            Random rng,
            out IntervalPitches pitches)
        {
            bool ascending = ResolveIsAscending(directionMode, semitones, rng);
            return TryPickInterval(lowWrittenMidi, highWrittenMidi, semitones, ascending, rng, out pitches);
        }

        /// <summary>
        /// Picks a random interval of <paramref name="semitones"/> in a concrete direction.
        /// </summary>
        public static bool TryPickInterval(
            int lowWrittenMidi,
            int highWrittenMidi,
            int semitones,
            bool ascending,
            Random rng,
            out IntervalPitches pitches)
        {
            pitches = default;
            if (!IntervalEarTrainingCatalog.IsValidSemitoneCount(semitones))
                return false;
            if (highWrittenMidi < lowWrittenMidi)
                return false;

            if (ascending)
            {
                int maxStart = highWrittenMidi - semitones;
                if (maxStart < lowWrittenMidi)
                    return false;

                int start = rng.Next(lowWrittenMidi, maxStart + 1);
                pitches = new IntervalPitches(start, start + semitones, semitones, IsAscending: true);
                return true;
            }

            // Descending: first note higher, second note semitones below.
            int minStart = lowWrittenMidi + semitones;
            if (minStart > highWrittenMidi)
                return false;

            int startDown = rng.Next(minStart, highWrittenMidi + 1);
            pitches = new IntervalPitches(startDown, startDown - semitones, semitones, IsAscending: false);
            return true;
        }

        /// <summary>
        /// Builds an interval from a fixed reference (first) pitch. Does not re-randomize the start.
        /// When direction is <see cref="IntervalDirectionMode.Random"/> and the chosen direction
        /// does not fit the range, the opposite direction is tried.
        /// </summary>
        public static bool TryBuildIntervalFromReference(
            int referenceStartWrittenMidi,
            int lowWrittenMidi,
            int highWrittenMidi,
            int semitones,
            IntervalDirectionMode directionMode,
            Random rng,
            out IntervalPitches pitches)
        {
            pitches = default;
            if (!IntervalEarTrainingCatalog.IsValidSemitoneCount(semitones))
                return false;
            if (highWrittenMidi < lowWrittenMidi)
                return false;
            if (referenceStartWrittenMidi < lowWrittenMidi
                || referenceStartWrittenMidi > highWrittenMidi)
                return false;

            bool ascending = ResolveIsAscending(directionMode, semitones, rng);
            if (TryBuildIntervalFromReference(
                    referenceStartWrittenMidi, lowWrittenMidi, highWrittenMidi,
                    semitones, ascending, out pitches))
                return true;

            // Random mode: if the coin-flip direction cannot fit, use the other when possible.
            if (directionMode == IntervalDirectionMode.Random && semitones > 0)
            {
                return TryBuildIntervalFromReference(
                    referenceStartWrittenMidi, lowWrittenMidi, highWrittenMidi,
                    semitones, ascending: !ascending, out pitches);
            }

            return false;
        }

        /// <summary>
        /// Builds an interval from a fixed reference pitch in a concrete direction.
        /// </summary>
        public static bool TryBuildIntervalFromReference(
            int referenceStartWrittenMidi,
            int lowWrittenMidi,
            int highWrittenMidi,
            int semitones,
            bool ascending,
            out IntervalPitches pitches)
        {
            pitches = default;
            if (!IntervalEarTrainingCatalog.IsValidSemitoneCount(semitones))
                return false;
            if (highWrittenMidi < lowWrittenMidi)
                return false;
            if (referenceStartWrittenMidi < lowWrittenMidi
                || referenceStartWrittenMidi > highWrittenMidi)
                return false;

            if (semitones == 0 || ascending)
            {
                int end = referenceStartWrittenMidi + semitones;
                if (end > highWrittenMidi)
                    return false;

                pitches = new IntervalPitches(
                    referenceStartWrittenMidi, end, semitones, IsAscending: true);
                return true;
            }

            int endDown = referenceStartWrittenMidi - semitones;
            if (endDown < lowWrittenMidi)
                return false;

            pitches = new IntervalPitches(
                referenceStartWrittenMidi, endDown, semitones, IsAscending: false);
            return true;
        }

        /// <summary>Backward-compatible ascending helper.</summary>
        public static bool TryPickAscendingInterval(
            int lowWrittenMidi,
            int highWrittenMidi,
            int semitones,
            Random rng,
            out IntervalPitches pitches)
            => TryPickInterval(
                lowWrittenMidi, highWrittenMidi, semitones, ascending: true, rng, out pitches);

        /// <summary>Random interval 0–12 that fits in the range, or false if even unison cannot fit.</summary>
        public static bool TryPickRandomInterval(
            int lowWrittenMidi,
            int highWrittenMidi,
            IntervalDirectionMode directionMode,
            Random rng,
            out IntervalPitches pitches)
        {
            pitches = default;
            if (highWrittenMidi < lowWrittenMidi)
                return false;

            // Prefer intervals that fit; shuffle candidates so all 0–12 remain possible when range allows.
            var candidates = new List<int>(IntervalEarTrainingCatalog.MaxSemitones + 1);
            for (int s = IntervalEarTrainingCatalog.MinSemitones; s <= IntervalEarTrainingCatalog.MaxSemitones; s++)
            {
                if (highWrittenMidi - lowWrittenMidi >= s)
                    candidates.Add(s);
            }

            if (candidates.Count == 0)
                return false;

            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            int chosen = candidates[0];
            return TryPickInterval(lowWrittenMidi, highWrittenMidi, chosen, directionMode, rng, out pitches);
        }

        /// <summary>Backward-compatible random helper (ascending).</summary>
        public static bool TryPickRandomInterval(
            int lowWrittenMidi,
            int highWrittenMidi,
            Random rng,
            out IntervalPitches pitches)
            => TryPickRandomInterval(
                lowWrittenMidi, highWrittenMidi, IntervalDirectionMode.Ascending, rng, out pitches);

        /// <summary>
        /// Chromatic written MIDIs in [low, high] that also belong to the instrument set
        /// (when provided). Highest pitch first (picker top), lowest last.
        /// </summary>
        public static IReadOnlyList<int> BuildStartNoteMidis(
            int lowWrittenMidi,
            int highWrittenMidi,
            IReadOnlyList<int>? instrumentMidis)
        {
            if (highWrittenMidi < lowWrittenMidi)
                return Array.Empty<int>();

            HashSet<int>? allowed = instrumentMidis is { Count: > 0 }
                ? new HashSet<int>(instrumentMidis)
                : null;

            var list = new List<int>(highWrittenMidi - lowWrittenMidi + 1);
            for (int midi = highWrittenMidi; midi >= lowWrittenMidi; midi--)
            {
                if (allowed == null || allowed.Contains(midi))
                    list.Add(midi);
            }

            return list;
        }

        /// <summary>null = Random; otherwise a written MIDI in 1–127.</summary>
        public static int? ParsePersistedStartNote(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || string.Equals(value.Trim(), RandomStartNoteToken, StringComparison.OrdinalIgnoreCase))
                return null;

            if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int midi)
                && midi > 0 && midi < 128)
                return midi;

            int fromName = NoteSessionService.NoteNameToMidi(value.Trim());
            return fromName > 0 ? fromName : null;
        }

        public static string PersistStartNote(int? writtenMidi)
            => writtenMidi is int midi && midi > 0 && midi < 128
                ? midi.ToString(CultureInfo.InvariantCulture)
                : RandomStartNoteToken;

        /// <summary>
        /// Whether <paramref name="semitones"/> can be formed from a fixed start note
        /// without leaving [low, high]. Random direction is feasible if either way fits.
        /// </summary>
        public static bool CanFormIntervalFromStart(
            int startWrittenMidi,
            int lowWrittenMidi,
            int highWrittenMidi,
            int semitones,
            IntervalDirectionMode directionMode)
        {
            if (directionMode == IntervalDirectionMode.Descending)
            {
                return TryBuildIntervalFromReference(
                    startWrittenMidi, lowWrittenMidi, highWrittenMidi,
                    semitones, ascending: false, out _);
            }

            if (directionMode == IntervalDirectionMode.Ascending)
            {
                return TryBuildIntervalFromReference(
                    startWrittenMidi, lowWrittenMidi, highWrittenMidi,
                    semitones, ascending: true, out _);
            }

            return TryBuildIntervalFromReference(
                    startWrittenMidi, lowWrittenMidi, highWrittenMidi,
                    semitones, ascending: true, out _)
                || TryBuildIntervalFromReference(
                    startWrittenMidi, lowWrittenMidi, highWrittenMidi,
                    semitones, ascending: false, out _);
        }

        /// <summary>
        /// Picks a random feasible interval that starts on <paramref name="startWrittenMidi"/>.
        /// Never retries unboundedly: scans 0–12 once.
        /// </summary>
        public static bool TryPickRandomIntervalFromStart(
            int startWrittenMidi,
            int lowWrittenMidi,
            int highWrittenMidi,
            IntervalDirectionMode directionMode,
            Random rng,
            out IntervalPitches pitches)
        {
            pitches = default;
            var candidates = new List<int>(IntervalEarTrainingCatalog.MaxSemitones + 1);
            for (int s = IntervalEarTrainingCatalog.MinSemitones; s <= IntervalEarTrainingCatalog.MaxSemitones; s++)
            {
                if (CanFormIntervalFromStart(
                        startWrittenMidi, lowWrittenMidi, highWrittenMidi, s, directionMode))
                    candidates.Add(s);
            }

            if (candidates.Count == 0)
                return false;

            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            foreach (int chosen in candidates)
            {
                if (TryBuildIntervalFromReference(
                        startWrittenMidi, lowWrittenMidi, highWrittenMidi,
                        chosen, directionMode, rng, out pitches))
                    return true;
            }

            return false;
        }

        public static bool IsAnswerCorrect(int expectedSemitones, int answeredSemitones)
            => expectedSemitones == answeredSemitones;

        /// <summary>
        /// Graphic noteheads (and accidentals) stay hidden until the quiz is answered
        /// correctly, or when the user is exploring an interval they chose themselves.
        /// </summary>
        public static bool ShouldShowIntervalNotes(IntervalEarTrainingInteraction interaction)
            => interaction != IntervalEarTrainingInteraction.UnansweredQuiz;
    }
}
