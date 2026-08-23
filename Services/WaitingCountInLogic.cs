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

        /// <summary>
        /// Click length = beat duration × percent / 100 (percent of the beat at the current tempo).
        /// </summary>
        public static double ResolveClickDurationSeconds(int beatDurationPercent, double msPerBeat)
        {
            int pct = WaitingCountInSettings.ClampDurationPercent(beatDurationPercent);
            double beatMs = Math.Max(1.0, msPerBeat);
            return (beatMs * pct / 100.0) / 1000.0;
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
        /// </summary>
        public static bool ShouldStopForFirstNote(bool evaluateCorrect, bool countInActive, int currentNoteIndex)
            => countInActive && currentNoteIndex == 0 && evaluateCorrect;
    }
}
