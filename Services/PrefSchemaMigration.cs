namespace musicmate.Services
{
    /// <summary>
    /// One-time preference/data resets when the stored schema version is behind the app.
    /// </summary>
    internal static class PrefSchemaMigration
    {
        public const int CurrentSchemaVersion = 5;

        private const string SchemaKey = "musicmate.PrefSchemaVersion";
        private const string ButtonBackgroundKey = ThemeService.ColorPreferencePrefix + nameof(AppColorTarget.ButtonBackground);
        private const string ButtonBackgroundHex = "#E7FFCC";
        private const string OmitMsAvgThresholdKey = "musicmate.OmitMsAvgThreshold";

        public static void ApplyIfNeeded()
        {
            int schema = Preferences.Get(SchemaKey, 0);
            if (schema >= CurrentSchemaVersion)
                return;

            if (schema < 2)
            {
                Preferences.Clear();
                ClearAppDataDatabases();
            }

            if (schema < 3)
            {
                // Older builds could persist lavender/purple as the button fill; use the designed light green.
                Preferences.Set(ButtonBackgroundKey, ButtonBackgroundHex);
            }

            if (schema < 4)
            {
                MigrateOmitMsAvgThreshold();
            }

            if (schema < 5)
            {
                // Beat-sound duration is now % of beat (5–50), not milliseconds.
                Preferences.Remove(WaitingCountInSettings.LegacyBeatDurationMsKey);
                if (!Preferences.ContainsKey(WaitingCountInSettings.BeatDurationPercentKey))
                    Preferences.Set(
                        WaitingCountInSettings.BeatDurationPercentKey,
                        WaitingCountInSettings.DefaultBeatDurationPercent);
            }

            Preferences.Set(SchemaKey, CurrentSchemaVersion);
        }

        /// <summary>
        /// Legacy default 400 ms blocked mastery at normal tempos; a 0–5 slider could also save tiny values.
        /// 0 disables the MsAvg gate so accuracy settings alone control mastery.
        /// </summary>
        private static void MigrateOmitMsAvgThreshold()
        {
            if (!Preferences.ContainsKey(OmitMsAvgThresholdKey))
                return;

            int value = Preferences.Get(OmitMsAvgThresholdKey, 0);
            if (value == 400 || (value > 0 && value <= 10))
                Preferences.Set(OmitMsAvgThresholdKey, 0);
        }

        private static void ClearAppDataDatabases()
        {
            try
            {
                string dir = FileSystem.AppDataDirectory;
                if (!Directory.Exists(dir))
                    return;

                foreach (string path in Directory.GetFiles(dir, "*.db3"))
                {
                    try { File.Delete(path); }
                    catch { /* best-effort */ }
                }
            }
            catch { /* best-effort */ }
        }
    }
}
