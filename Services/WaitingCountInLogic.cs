using musicmate.Models;

namespace musicmate.Services
{
    /// <summary>
    /// Pure helpers for waiting count-in beat accenting and click parameters.
    /// </summary>
    public static class WaitingCountInLogic
    {
        public readonly record struct ClickSpec(double FrequencyHz, float Volume, double DurationSeconds, bool IsAccented);

        /// <summary>Conducted beats per measure for the given display time signature.</summary>
        public static int GetBeatsPerMeasure(string? timeSignatureDisplay)
        {
            var ts = TimeSignature.FromDisplayString(timeSignatureDisplay ?? "4/4");
            return Math.Max(1, ConductorBeatHelper.GetConductedBeatCount(ts));
        }

        /// <summary>Beat 0 of each measure is accented; all others are unaccented.</summary>
        public static bool IsAccentedBeat(int beatIndexInMeasure)
            => beatIndexInMeasure == 0;

        public static bool IsAccentedBeat(long absoluteBeatIndex, int beatsPerMeasure)
        {
            if (beatsPerMeasure <= 0)
                return true;
            int inMeasure = (int)(absoluteBeatIndex % beatsPerMeasure);
            if (inMeasure < 0)
                inMeasure += beatsPerMeasure;
            return IsAccentedBeat(inMeasure);
        }

        public static double MsPerBeat(int tempoBpm)
        {
            int bpm = Math.Clamp(tempoBpm, NoteSessionService.MinTempo, NoteSessionService.MaxTempo);
            return 60000.0 / bpm;
        }

        /// <summary>Absolute grid time for beat index 0, 1, 2… from session start.</summary>
        public static double GetAbsoluteBeatStartMs(long beatIndex, double msPerBeat)
            => beatIndex * msPerBeat;

        /// <summary>1-based measure and beat numbers for diagnostics.</summary>
        public static (int MeasureNumber, int BeatNumber) GetMeasureBeatNumbers(
            long beatIndex,
            int beatsPerMeasure)
        {
            beatsPerMeasure = Math.Max(1, beatsPerMeasure);
            int beatInMeasure = (int)(beatIndex % beatsPerMeasure);
            int measure = (int)(beatIndex / beatsPerMeasure);
            return (measure + 1, beatInMeasure + 1);
        }

        /// <summary>
        /// When the scheduler is so late that sounding this beat would crowd the next grid slot,
        /// skip playback for this slot only. The grid index still advances.
        /// </summary>
        public static bool IsTooLateToSound(double nowMs, double intendedMs, double msPerBeat)
            => nowMs >= intendedMs + msPerBeat * 0.5;

        /// <summary>
        /// Click length = beat duration × percent / 100 (percent of the beat at the current tempo).
        /// </summary>
        public static double ResolveClickDurationSeconds(int beatDurationPercent, double msPerBeat)
        {
            int pct = WaitingCountInSettings.ClampDurationPercent(beatDurationPercent);
            double beatMs = Math.Max(1.0, msPerBeat);
            return (beatMs * pct / 100.0) / 1000.0;
        }

        /// <summary>
        /// Audible click length including playback tail (matches AudioPlaybackService release padding).
        /// </summary>
        public static double ResolveClickPlaybackMs(int beatDurationPercent, double msPerBeat)
        {
            double clickMs = ResolveClickDurationSeconds(beatDurationPercent, msPerBeat) * 1000.0;
            return clickMs + (clickMs >= 80.0 ? 80.0 : 15.0);
        }

        /// <summary>
        /// Nominal click duration plus release pad and a short device-start latency so
        /// <see cref="NoteSessionService.SuppressCountInClickSelfSound"/> covers energy that
        /// reaches the mic after <c>TriggerClick</c> (SoundPool / IAudioPlayer start delay).
        /// </summary>
        public static int ResolveClickSelfSoundDurationMs(int clickDurationMs)
        {
            int audible = Math.Max(0, clickDurationMs);
            int releasePad = audible >= 80 ? 80 : 15;
            // Keep small — long enough for typical output start, not a noticeable play delay.
            const int deviceStartLatencyMs = 60;
            return audible + releasePad + deviceStartLatencyMs;
        }

        public static ClickSpec BuildClick(
            long absoluteBeatIndex,
            int beatsPerMeasure,
            float accentedVolume,
            float unaccentedVolume,
            double accentedPitchHz,
            double unaccentedPitchHz,
            int beatDurationPercent,
            int tempoBpm)
        {
            bool accent = IsAccentedBeat(absoluteBeatIndex, beatsPerMeasure);
            double msPerBeat = MsPerBeat(tempoBpm);
            return new ClickSpec(
                FrequencyHz: WaitingCountInSettings.ClampPitchHz(accent ? accentedPitchHz : unaccentedPitchHz),
                Volume: WaitingCountInSettings.ClampVolume(accent ? accentedVolume : unaccentedVolume),
                DurationSeconds: ResolveClickDurationSeconds(beatDurationPercent, msPerBeat),
                IsAccented: accent);
        }

        /// <summary>
        /// Count-in must ignore incorrect pitches. Only a correct first-note evaluation may stop it.
        /// App-generated click self-sound must still be suppressed separately
        /// (see <see cref="ComputeSelfSoundGuardMs"/> / IgnoreAudio during clicks).
        /// </summary>
        public static bool ShouldStopForFirstNote(bool evaluateCorrect, bool countInActive, int currentNoteIndex)
            => countInActive && currentNoteIndex == 0 && evaluateCorrect;

        /// <summary>
        /// True when heard Hz is within <paramref name="centsTolerance"/> of a Count-In click
        /// pitch, including octave equivalents (speaker bleed / McLeod octave errors).
        /// </summary>
        public static bool IsNearCountInClickFrequency(
            double heardHz,
            double accentedClickHz,
            double unaccentedClickHz,
            double centsTolerance = 100.0)
            => IsWithinCentsIncludingOctaves(heardHz, accentedClickHz, centsTolerance)
               || IsWithinCentsIncludingOctaves(heardHz, unaccentedClickHz, centsTolerance);

        /// <summary>
        /// Pitch-class / octave-aware cents distance to a reference frequency.
        /// </summary>
        public static bool IsWithinCentsIncludingOctaves(
            double heardHz,
            double referenceHz,
            double centsTolerance)
        {
            if (heardHz <= 0 || referenceHz <= 0 || centsTolerance < 0)
                return false;
            double octaves = Math.Log(heardHz / referenceHz, 2.0);
            if (double.IsNaN(octaves) || double.IsInfinity(octaves))
                return false;
            double centsFromNearestOctave = (octaves - Math.Round(octaves)) * 1200.0;
            return Math.Abs(centsFromNearestOctave) <= centsTolerance;
        }

        /// <summary>
        /// True when a correct first-note detection may end waiting Count-In and score the note.
        /// Click self-sound is rejected by frequency proximity (including octaves), not by a
        /// time window — players articulate on the beat, which overlaps the click.
        /// When frequency context is omitted, <paramref name="withinSelfSoundSuppressWindow"/>
        /// preserves the historical safe reject during suppress.
        /// </summary>
        public static bool ShouldAcceptFirstNoteToEndCountIn(
            bool evaluateCorrect,
            bool countInActive,
            int currentNoteIndex,
            bool withinSelfSoundSuppressWindow,
            double heardHz = 0,
            double accentedClickHz = 0,
            double unaccentedClickHz = 0)
        {
            if (!ShouldStopForFirstNote(evaluateCorrect, countInActive, currentNoteIndex))
                return false;
            if (heardHz > 0 && (accentedClickHz > 0 || unaccentedClickHz > 0))
            {
                // Frequency-aware path: reject only click self-sound / octave bleed.
                return !IsNearCountInClickFrequency(heardHz, accentedClickHz, unaccentedClickHz);
            }
            // No frequency context → keep historical safe reject during suppress.
            return !withinSelfSoundSuppressWindow;
        }

        /// <summary>
        /// Guard after each app-produced Count-In click so residual mic / pitch-window audio
        /// cannot satisfy the first note. Sized to one analysis window plus the half-window
        /// hop used by <see cref="PitchWindowAccumulator.HopHalf"/>.
        /// </summary>
        public static int ComputeSelfSoundGuardMs(int pitchWindowSize, int sampleRate)
        {
            pitchWindowSize = Math.Max(1, pitchWindowSize);
            sampleRate = Math.Max(1, sampleRate);
            double samplesToFlush = pitchWindowSize + (pitchWindowSize / 2.0);
            return (int)Math.Ceiling(1000.0 * samplesToFlush / sampleRate);
        }

        /// <summary>Total suppress length = audible click + residual guard.</summary>
        public static int ComputeSelfSoundSuppressMs(int clickDurationMs, int pitchWindowSize, int sampleRate)
            => Math.Max(0, clickDurationMs) + ComputeSelfSoundGuardMs(pitchWindowSize, sampleRate);

        /// <summary>
        /// Never cover an entire beat — leave a listening gap long enough to fill one
        /// pitch-analysis window after IgnoreAudio ends (otherwise notes are never detected
        /// during waiting Count-In). Also keep at least ~45% of the beat open so on-beat
        /// playing has a usable window at moderate tempos (e.g. 100 BPM / 600ms).
        /// </summary>
        public static int CapSelfSoundSuppressMs(int suppressMs, double msPerBeat, int minListeningGapMs = 0)
        {
            double beat = Math.Max(1.0, msPerBeat);
            // Default gap: ~window + half-hop at 4096/44100, or caller-provided guard.
            int gapMs = Math.Max(200, minListeningGapMs);
            // Prefer leaving nearly half the beat listenable when the beat is short.
            int halfBeatGap = (int)Math.Floor(beat * 0.45);
            gapMs = Math.Max(gapMs, halfBeatGap);
            int maxSuppress = Math.Max(20, (int)Math.Floor(beat - gapMs));
            return Math.Min(Math.Max(0, suppressMs), maxSuppress);
        }
    }
}
