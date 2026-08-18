namespace musicmate.Services
{
    public enum IntervalSingingTrainingMode
    {
        SingInterval = 0,
        ImitateInterval = 1,
        HearAndIdentify = 2,
    }

    public enum IntervalSingingExerciseState
    {
        Idle = 0,
        Presenting = 1,
        WaitingForSinger = 2,
        Evaluating = 3,
        Incorrect = 4,
        Correct = 5,
        Revealed = 6,
    }

    public enum SungPitchVerdict
    {
        Unstable = 0,
        Correct = 1,
        ALittleHigh = 2,
        ALittleLow = 3,
        TooHigh = 4,
        TooLow = 5,
    }

    /// <summary>
    /// Interval Singing Training: level mapping, pitch judging, exercise picking,
    /// persistence, and listen-gate helpers. Interval construction reuses
    /// <see cref="IntervalEarTrainingLogic"/>.
    /// </summary>
    public static class IntervalSingingTrainingLogic
    {
        public const int MinLevel = 1;
        public const int MaxLevel = 100;
        public const int DefaultLevel = 1;
        public const string LevelPreferenceKey = "musicmate.SingingTrainingLevel";
        public const string ModePreferenceKey = "musicmate.SingingTrainingMode";
        public const string DirectionPreferenceKey = "musicmate.SingingTrainingDirection";
        public const string StartNotePreferenceKey = "musicmate.SingingTrainingStartNote";

        public const IntervalSingingTrainingMode DefaultMode = IntervalSingingTrainingMode.SingInterval;
        public const IntervalDirectionMode DefaultDirectionMode = IntervalDirectionMode.Ascending;

        /// <summary>Reuse Music/Tuner default (±50 cents) as the singing correctness window.</summary>
        public const int DefaultToleranceCents = NoteSessionService.DefaultTolerance;

        /// <summary>Near-miss band above tolerance, still reported as a little high/low.</summary>
        public const int NearMissCents = 100;

        /// <summary>Consecutive McLeod windows required before accepting a sung pitch.</summary>
        public const int RequiredStableWindows = 2;

        /// <summary>Conservative concert singing span when the instrument is not a voice profile.</summary>
        public const int ConservativeConcertLowMidi = 48;  // C3
        public const int ConservativeConcertHighMidi = 72; // C5

        public static readonly string[] ModePickerItems =
        [
            "Sing Interval",
            "Imitate Interval",
            "Hear & Identify",
        ];

        public static int ClampLevel(int level)
            => Math.Clamp(level, MinLevel, MaxLevel);

        public static int LoadPersistedLevel()
            => ClampLevel(SessionPreferences.Get(LevelPreferenceKey, DefaultLevel));

        public static void PersistLevel(int level)
            => SessionPreferences.Set(LevelPreferenceKey, ClampLevel(level));

        public static IntervalSingingTrainingMode LoadPersistedMode()
            => ParseMode(SessionPreferences.Get(ModePreferenceKey, DefaultMode.ToString()));

        public static void PersistMode(IntervalSingingTrainingMode mode)
            => SessionPreferences.Set(ModePreferenceKey, mode.ToString());

        public static IntervalDirectionMode LoadPersistedDirection()
            => IntervalEarTrainingLogic.ParseDirectionMode(
                SessionPreferences.Get(DirectionPreferenceKey, DefaultDirectionMode.ToString()));

        public static void PersistDirection(IntervalDirectionMode mode)
            => SessionPreferences.Set(DirectionPreferenceKey, mode.ToString());

        public static int? LoadPersistedStartNote()
            => IntervalEarTrainingLogic.ParsePersistedStartNote(
                SessionPreferences.Get(
                    StartNotePreferenceKey,
                    IntervalEarTrainingLogic.RandomStartNoteToken));

        public static void PersistStartNote(int? writtenMidi)
            => SessionPreferences.Set(
                StartNotePreferenceKey,
                IntervalEarTrainingLogic.PersistStartNote(writtenMidi));

        public static IntervalSingingTrainingMode ParseMode(string? value)
        {
            if (Enum.TryParse(value, ignoreCase: true, out IntervalSingingTrainingMode mode)
                && Enum.IsDefined(mode))
                return mode;
            return DefaultMode;
        }

        public static string FormatModeLabel(IntervalSingingTrainingMode mode)
            => mode switch
            {
                IntervalSingingTrainingMode.ImitateInterval => ModePickerItems[1],
                IntervalSingingTrainingMode.HearAndIdentify => ModePickerItems[2],
                _ => ModePickerItems[0],
            };

        public static IntervalSingingTrainingMode ModeFromPickerIndex(int index)
            => index switch
            {
                1 => IntervalSingingTrainingMode.ImitateInterval,
                2 => IntervalSingingTrainingMode.HearAndIdentify,
                _ => IntervalSingingTrainingMode.SingInterval,
            };

        public static int PickerIndexFromMode(IntervalSingingTrainingMode mode)
            => mode switch
            {
                IntervalSingingTrainingMode.ImitateInterval => 1,
                IntervalSingingTrainingMode.HearAndIdentify => 2,
                _ => 0,
            };

        /// <summary>
        /// Intervals allowed for automatic New Exercise at <paramref name="level"/>.
        /// Manual interval buttons are never filtered by this list.
        /// </summary>
        public static IReadOnlyList<int> GetAllowedSingingIntervals(int level)
        {
            int lv = ClampLevel(level);
            if (lv <= 15)
                return AllowedBand1;
            if (lv <= 30)
                return AllowedBand2;
            if (lv <= 45)
                return AllowedBand3;
            if (lv <= 65)
                return AllowedBand4;
            if (lv <= 85)
                return AllowedBand5;
            return AllowedAll;
        }

        private static readonly int[] AllowedBand1 = [0, 2, 3, 4];
        private static readonly int[] AllowedBand2 = [0, 2, 3, 4, 5, 7];
        private static readonly int[] AllowedBand3 = [0, 2, 3, 4, 5, 7, 12];
        private static readonly int[] AllowedBand4 = [0, 1, 2, 3, 4, 5, 7, 8, 9, 12];
        private static readonly int[] AllowedBand5 = [0, 1, 2, 3, 4, 5, 7, 8, 9, 10, 11, 12];
        private static readonly int[] AllowedAll = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];

        public static bool IsVoiceInstrument(string? instrument)
        {
            string id = InstrumentCatalog.Resolve(instrument).Id;
            return id.StartsWith("voice-", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Written MIDI range used to pick singing exercises. Voice instruments use the
        /// session/vocal range; other instruments intersect session range with a
        /// conservative concert singing span, converted through transposition once.
        /// </summary>
        public static bool TryResolveSingingWrittenRange(
            string? instrument,
            int sessionLowWrittenMidi,
            int sessionHighWrittenMidi,
            int transposeOffset,
            out int lowWrittenMidi,
            out int highWrittenMidi)
        {
            lowWrittenMidi = 0;
            highWrittenMidi = 0;
            if (sessionHighWrittenMidi < sessionLowWrittenMidi)
                (sessionLowWrittenMidi, sessionHighWrittenMidi) = (sessionHighWrittenMidi, sessionLowWrittenMidi);
            if (sessionLowWrittenMidi <= 0 || sessionHighWrittenMidi <= 0)
                return false;

            if (IsVoiceInstrument(instrument))
            {
                lowWrittenMidi = sessionLowWrittenMidi;
                highWrittenMidi = sessionHighWrittenMidi;
                return true;
            }

            int conservativeLowWritten = TunerReferenceNoteCatalog.FromConcertMidi(
                ConservativeConcertLowMidi, transposeOffset);
            int conservativeHighWritten = TunerReferenceNoteCatalog.FromConcertMidi(
                ConservativeConcertHighMidi, transposeOffset);
            if (conservativeHighWritten < conservativeLowWritten)
                (conservativeLowWritten, conservativeHighWritten) = (conservativeHighWritten, conservativeLowWritten);

            lowWrittenMidi = Math.Max(sessionLowWrittenMidi, conservativeLowWritten);
            highWrittenMidi = Math.Min(sessionHighWrittenMidi, conservativeHighWritten);
            if (highWrittenMidi < lowWrittenMidi)
            {
                lowWrittenMidi = conservativeLowWritten;
                highWrittenMidi = conservativeHighWritten;
            }

            return highWrittenMidi >= lowWrittenMidi;
        }

        public static bool TryExpectedPitches(
            int startWrittenMidi,
            int lowWrittenMidi,
            int highWrittenMidi,
            int semitones,
            bool ascending,
            out IntervalEarTrainingLogic.IntervalPitches pitches)
            => IntervalEarTrainingLogic.TryBuildIntervalFromReference(
                startWrittenMidi, lowWrittenMidi, highWrittenMidi, semitones, ascending, out pitches);

        /// <summary>
        /// Automatic exercise: Level-filtered intervals only. One bounded shuffle pass.
        /// </summary>
        public static bool TryPickAutomaticExercise(
            int level,
            int lowWrittenMidi,
            int highWrittenMidi,
            IntervalDirectionMode directionMode,
            int? startWrittenMidi,
            Random rng,
            out IntervalEarTrainingLogic.IntervalPitches pitches)
        {
            pitches = default;
            var allowed = GetAllowedSingingIntervals(level);
            var candidates = new List<int>(allowed.Count);
            candidates.AddRange(allowed);
            ShuffleInPlace(candidates, rng);

            if (startWrittenMidi is int start)
            {
                foreach (int semitones in candidates)
                {
                    if (IntervalEarTrainingLogic.TryBuildIntervalFromReference(
                            start, lowWrittenMidi, highWrittenMidi, semitones, directionMode, rng, out pitches))
                        return true;
                }

                return false;
            }

            foreach (int semitones in candidates)
            {
                if (IntervalEarTrainingLogic.TryPickInterval(
                        lowWrittenMidi, highWrittenMidi, semitones, directionMode, rng, out pitches))
                    return true;
            }

            return false;
        }

        public static bool TryPickManualInterval(
            int semitones,
            int lowWrittenMidi,
            int highWrittenMidi,
            IntervalDirectionMode directionMode,
            int? startWrittenMidi,
            Random rng,
            out IntervalEarTrainingLogic.IntervalPitches pitches)
        {
            pitches = default;
            if (startWrittenMidi is int start)
            {
                return IntervalEarTrainingLogic.TryBuildIntervalFromReference(
                    start, lowWrittenMidi, highWrittenMidi, semitones, directionMode, rng, out pitches);
            }

            return IntervalEarTrainingLogic.TryPickInterval(
                lowWrittenMidi, highWrittenMidi, semitones, directionMode, rng, out pitches);
        }

        public static int ToConcertMidi(int writtenMidi, int transposeOffset)
            => TunerReferenceNoteCatalog.ToConcertMidi(writtenMidi, transposeOffset);

        /// <summary>Cents of <paramref name="detectedHz"/> relative to the expected concert MIDI (not nearest note).</summary>
        public static double CentsOffTarget(double detectedHz, int expectedConcertMidi)
        {
            if (detectedHz <= 0 || expectedConcertMidi <= 0)
                return double.NaN;

            double expectedHz = NoteSessionService.MidiToFreqPublic(expectedConcertMidi);
            if (expectedHz <= 0)
                return double.NaN;

            return 1200.0 * Math.Log(detectedHz / expectedHz, 2.0);
        }

        public static SungPitchVerdict JudgeDetectedPitch(
            double detectedHz,
            int expectedConcertMidi,
            int toleranceCents)
        {
            if (detectedHz <= 0)
                return SungPitchVerdict.Unstable;

            double cents = CentsOffTarget(detectedHz, expectedConcertMidi);
            if (double.IsNaN(cents))
                return SungPitchVerdict.Unstable;

            int tol = Math.Max(0, toleranceCents);
            double abs = Math.Abs(cents);
            if (abs <= tol)
                return SungPitchVerdict.Correct;
            if (abs <= NearMissCents)
                return cents > 0 ? SungPitchVerdict.ALittleHigh : SungPitchVerdict.ALittleLow;
            return cents > 0 ? SungPitchVerdict.TooHigh : SungPitchVerdict.TooLow;
        }

        public static string FormatPitchFeedback(SungPitchVerdict verdict, string writtenTargetLabel)
        {
            return verdict switch
            {
                SungPitchVerdict.Correct =>
                    string.IsNullOrWhiteSpace(writtenTargetLabel)
                        ? "Correct"
                        : $"Correct — {writtenTargetLabel}",
                SungPitchVerdict.ALittleHigh => "A little high",
                SungPitchVerdict.ALittleLow => "A little low",
                SungPitchVerdict.TooHigh => "Too high — try again",
                SungPitchVerdict.TooLow => "Too low — try again",
                _ => "Could not hear a steady note",
            };
        }

        public static string FormatSingInstruction(IntervalEarTrainingLogic.IntervalPitches pitches)
        {
            string name = IntervalEarTrainingCatalog.GetName(pitches.Semitones);
            if (pitches.Semitones == 0)
                return $"Sing a {name}";
            string dir = pitches.IsAscending ? "upward" : "downward";
            return $"Sing a {name} {dir}";
        }

        public static string FormatImitateInstruction(IntervalEarTrainingLogic.IntervalPitches pitches)
        {
            string name = IntervalEarTrainingCatalog.GetName(pitches.Semitones);
            string start = TunerReferenceNoteCatalog.FormatCompactWrittenLabel(pitches.StartWrittenMidi);
            string end = TunerReferenceNoteCatalog.FormatCompactWrittenLabel(pitches.EndWrittenMidi);
            return $"Sing {start} then {end} ({name})";
        }

        public static string FormatIdentifyInstruction()
            => "Listen, then tap the interval you heard.";

        public static bool ShouldShowNotation(IntervalSingingExerciseState state)
            => state is IntervalSingingExerciseState.Correct
                or IntervalSingingExerciseState.Revealed;

        public static bool ShouldListen(
            IntervalSingingExerciseState state,
            bool pageVisible,
            bool playbackActive,
            IntervalSingingTrainingMode mode = IntervalSingingTrainingMode.SingInterval)
            => pageVisible
               && !playbackActive
               && state == IntervalSingingExerciseState.WaitingForSinger
               && mode != IntervalSingingTrainingMode.HearAndIdentify;

        public static bool CountsAsIndependentCorrect(bool revealed, bool succeeded)
            => succeeded && !revealed;

        public static string FormatDirectionWord(bool isAscending)
            => isAscending ? "Up" : "Down";

        private static void ShuffleInPlace(List<int> values, Random rng)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (values[i], values[j]) = (values[j], values[i]);
            }
        }
    }

    /// <summary>Requires consecutive matching windows before a sung pitch is accepted.</summary>
    public sealed class SungPitchStabilizer
    {
        public SungPitchStabilizer(int requiredWindows = IntervalSingingTrainingLogic.RequiredStableWindows)
        {
            RequiredWindows = Math.Max(1, requiredWindows);
        }

        public int RequiredWindows { get; }
        public int Consecutive { get; private set; }
        public SungPitchVerdict? Pending { get; private set; }

        public void Reset()
        {
            Consecutive = 0;
            Pending = null;
        }

        /// <summary>
        /// Returns a verdict only after <see cref="RequiredWindows"/> consecutive matching
        /// samples. Unstable detections never accept and reset the streak.
        /// </summary>
        public SungPitchVerdict? Observe(SungPitchVerdict verdict)
        {
            if (verdict == SungPitchVerdict.Unstable)
            {
                Reset();
                return null;
            }

            if (Pending == verdict)
            {
                Consecutive++;
            }
            else
            {
                Pending = verdict;
                Consecutive = 1;
            }

            if (Consecutive >= RequiredWindows)
            {
                var accepted = verdict;
                Reset();
                return accepted;
            }

            return null;
        }
    }

    /// <summary>Two-note imitate: pitch and order only (no rhythm grading).</summary>
    public sealed class IntervalSingingImitateTracker
    {
        public IntervalSingingImitateTracker(int firstConcertMidi, int secondConcertMidi, int toleranceCents)
        {
            FirstConcertMidi = firstConcertMidi;
            SecondConcertMidi = secondConcertMidi;
            ToleranceCents = Math.Max(0, toleranceCents);
            FirstStabilizer = new SungPitchStabilizer();
            SecondStabilizer = new SungPitchStabilizer();
        }

        public int FirstConcertMidi { get; }
        public int SecondConcertMidi { get; }
        public int ToleranceCents { get; }
        public bool FirstAccepted { get; private set; }
        public bool AwaitingReleaseFromFirst { get; private set; }
        public SungPitchStabilizer FirstStabilizer { get; }
        public SungPitchStabilizer SecondStabilizer { get; }

        public ImitateObserveResult Observe(double detectedHz)
        {
            if (!FirstAccepted)
            {
                var first = IntervalSingingTrainingLogic.JudgeDetectedPitch(
                    detectedHz, FirstConcertMidi, ToleranceCents);
                var accepted = FirstStabilizer.Observe(first);
                if (accepted is null)
                    return ImitateObserveResult.Pending;

                if (accepted == SungPitchVerdict.Correct)
                {
                    FirstAccepted = true;
                    AwaitingReleaseFromFirst = true;
                    return ImitateObserveResult.FirstCorrect;
                }

                return ImitateObserveResult.FromFirst(accepted.Value);
            }

            if (AwaitingReleaseFromFirst)
            {
                var stillFirst = IntervalSingingTrainingLogic.JudgeDetectedPitch(
                    detectedHz, FirstConcertMidi, ToleranceCents);
                if (stillFirst == SungPitchVerdict.Correct || stillFirst == SungPitchVerdict.Unstable)
                    return ImitateObserveResult.Pending;

                AwaitingReleaseFromFirst = false;
                SecondStabilizer.Reset();
            }

            var second = IntervalSingingTrainingLogic.JudgeDetectedPitch(
                detectedHz, SecondConcertMidi, ToleranceCents);
            var secondAccepted = SecondStabilizer.Observe(second);
            if (secondAccepted is null)
                return ImitateObserveResult.Pending;

            if (secondAccepted == SungPitchVerdict.Correct)
                return ImitateObserveResult.BothCorrect;

            return ImitateObserveResult.FromSecond(secondAccepted.Value);
        }

        public void NotifySilence()
        {
            if (FirstAccepted)
                AwaitingReleaseFromFirst = false;
        }
    }

    public readonly record struct ImitateObserveResult(ImitateObserveKind Kind, SungPitchVerdict Verdict)
    {
        public static ImitateObserveResult Pending { get; } =
            new(ImitateObserveKind.Pending, SungPitchVerdict.Unstable);

        public static ImitateObserveResult FirstCorrect { get; } =
            new(ImitateObserveKind.FirstCorrect, SungPitchVerdict.Correct);

        public static ImitateObserveResult BothCorrect { get; } =
            new(ImitateObserveKind.BothCorrect, SungPitchVerdict.Correct);

        public static ImitateObserveResult FromFirst(SungPitchVerdict verdict)
            => new(ImitateObserveKind.FirstIncorrect, verdict);

        public static ImitateObserveResult FromSecond(SungPitchVerdict verdict)
            => new(ImitateObserveKind.SecondIncorrect, verdict);

        public string FormatFeedback()
        {
            return Kind switch
            {
                ImitateObserveKind.BothCorrect => "Correct",
                ImitateObserveKind.FirstCorrect => "First note correct — sing the second",
                ImitateObserveKind.FirstIncorrect =>
                    $"First note {Describe(Verdict)}",
                ImitateObserveKind.SecondIncorrect =>
                    $"First note correct — second note {Describe(Verdict)}",
                _ => "Listening…",
            };
        }

        private static string Describe(SungPitchVerdict verdict)
            => verdict switch
            {
                SungPitchVerdict.ALittleHigh => "a little high",
                SungPitchVerdict.ALittleLow => "a little low",
                SungPitchVerdict.TooHigh => "too high",
                SungPitchVerdict.TooLow => "too low",
                _ => "try again",
            };
    }

    public enum ImitateObserveKind
    {
        Pending = 0,
        FirstCorrect = 1,
        FirstIncorrect = 2,
        SecondIncorrect = 3,
        BothCorrect = 4,
    }

    public sealed class IntervalSingingSessionStats
    {
        public int Attempts { get; set; }
        public int CorrectWithoutReveal { get; set; }
        public int IncorrectAttempts { get; set; }
        public int Reveals { get; set; }

        public void RecordExerciseStarted()
            => Attempts++;

        public void RecordIncorrect()
            => IncorrectAttempts++;

        public void RecordCorrect(bool revealed)
        {
            if (IntervalSingingTrainingLogic.CountsAsIndependentCorrect(revealed, succeeded: true))
                CorrectWithoutReveal++;
        }

        public void RecordReveal(bool alreadySucceeded)
        {
            if (!alreadySucceeded)
                Reveals++;
        }
    }

    /// <summary>Testable listen/playback gate so leaving the page cannot leave capture hung.</summary>
    public sealed class IntervalSingingListenGate
    {
        public bool PageVisible { get; set; }
        public bool PlaybackActive { get; set; }
        public bool CaptureActive { get; private set; }
        public IntervalSingingExerciseState State { get; set; } = IntervalSingingExerciseState.Idle;
        public IntervalSingingTrainingMode Mode { get; set; } = IntervalSingingTrainingMode.SingInterval;
        public int BusyDepth { get; private set; }

        public bool ShouldCapture
            => IntervalSingingTrainingLogic.ShouldListen(State, PageVisible, PlaybackActive, Mode);

        public bool TryStartCapture()
        {
            if (!ShouldCapture)
            {
                CaptureActive = false;
                return false;
            }

            CaptureActive = true;
            return true;
        }

        public void StopCapture()
            => CaptureActive = false;

        public void StopAll()
        {
            PlaybackActive = false;
            CaptureActive = false;
            if (State is IntervalSingingExerciseState.WaitingForSinger
                or IntervalSingingExerciseState.Presenting
                or IntervalSingingExerciseState.Evaluating)
            {
                State = IntervalSingingExerciseState.Idle;
            }

            BusyDepth = 0;
        }
    }
}
