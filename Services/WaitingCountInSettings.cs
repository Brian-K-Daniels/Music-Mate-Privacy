namespace musicmate.Services
{
    /// <summary>
    /// Persisted waiting-count-in preferences for the Music page and Tuner metronome.
    /// </summary>
    public static class WaitingCountInSettings
    {
        public const string EnabledKey = "musicmate.CountIn.Enabled";
        public const string AccentedVolumeKey = "musicmate.CountIn.AccentedVolume";
        public const string UnaccentedVolumeKey = "musicmate.CountIn.UnaccentedVolume";
        public const string AccentedPitchHzKey = "musicmate.CountIn.AccentedPitchHz";
        public const string UnaccentedPitchHzKey = "musicmate.CountIn.UnaccentedPitchHz";
        public const string BeatDurationPercentKey = "musicmate.CountIn.BeatDurationPercent";
        /// <summary>Legacy ms key — cleared on schema migrate; never read as duration.</summary>
        public const string LegacyBeatDurationMsKey = "musicmate.CountIn.BeatDurationMs";

        public const bool DefaultEnabled = false;

        public const float DefaultAccentedVolume = 0.70f;
        public const float DefaultUnaccentedVolume = 0.40f;
        public const float MinVolume = 0f;
        public const float MaxVolume = 1f;

        public const double DefaultAccentedPitchHz = 1760.0;   // A6 — above typical beginner range
        public const double DefaultUnaccentedPitchHz = 1318.5; // E6
        public const double MinPitchHz = 440.0;
        public const double MaxPitchHz = 4000.0;

        /// <summary>Click length as a percent of one beat at the current tempo.</summary>
        public const int DefaultBeatDurationPercent = 20;
        public const int MinBeatDurationPercent = 5;
        public const int MaxBeatDurationPercent = 50;

        public static bool Enabled
        {
            get => SessionPreferences.Get(EnabledKey, DefaultEnabled);
            set => SessionPreferences.Set(EnabledKey, value);
        }

        public static float AccentedVolume
        {
            get => ClampVolume(SessionPreferences.Get(AccentedVolumeKey, DefaultAccentedVolume));
            set => SessionPreferences.Set(AccentedVolumeKey, ClampVolume(value));
        }

        public static float UnaccentedVolume
        {
            get => ClampVolume(SessionPreferences.Get(UnaccentedVolumeKey, DefaultUnaccentedVolume));
            set => SessionPreferences.Set(UnaccentedVolumeKey, ClampVolume(value));
        }

        public static double AccentedPitchHz
        {
            get => ClampPitchHz(SessionPreferences.Get(AccentedPitchHzKey, DefaultAccentedPitchHz));
            set => SessionPreferences.Set(AccentedPitchHzKey, ClampPitchHz(value));
        }

        public static double UnaccentedPitchHz
        {
            get => ClampPitchHz(SessionPreferences.Get(UnaccentedPitchHzKey, DefaultUnaccentedPitchHz));
            set => SessionPreferences.Set(UnaccentedPitchHzKey, ClampPitchHz(value));
        }

        public static int BeatDurationPercent
        {
            get => ClampDurationPercent(
                SessionPreferences.Get(BeatDurationPercentKey, DefaultBeatDurationPercent));
            set => SessionPreferences.Set(BeatDurationPercentKey, ClampDurationPercent(value));
        }

        public static float ClampVolume(float value)
            => Math.Clamp(value, MinVolume, MaxVolume);

        public static double ClampPitchHz(double value)
            => Math.Clamp(value, MinPitchHz, MaxPitchHz);

        public static int ClampDurationPercent(int value)
            => Math.Clamp(value, MinBeatDurationPercent, MaxBeatDurationPercent);

        public static void ResetToFactoryDefaults()
        {
            Enabled = DefaultEnabled;
            AccentedVolume = DefaultAccentedVolume;
            UnaccentedVolume = DefaultUnaccentedVolume;
            AccentedPitchHz = DefaultAccentedPitchHz;
            UnaccentedPitchHz = DefaultUnaccentedPitchHz;
            BeatDurationPercent = DefaultBeatDurationPercent;
        }
    }
}
