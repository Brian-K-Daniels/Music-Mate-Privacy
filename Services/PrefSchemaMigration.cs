namespace musicmate.Services
{
    /// <summary>
    /// One-time preference/data resets when the stored schema version is behind the app.
    /// </summary>
    internal static class PrefSchemaMigration
    {
        public const int CurrentSchemaVersion = 2;

        private const string SchemaKey = "musicmate.PrefSchemaVersion";

        public static void ApplyIfNeeded()
        {
            int schema = Preferences.Get(SchemaKey, 0);
            if (schema >= CurrentSchemaVersion)
                return;

            Preferences.Clear();
            ClearAppDataDatabases();
            Preferences.Set(SchemaKey, CurrentSchemaVersion);
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
