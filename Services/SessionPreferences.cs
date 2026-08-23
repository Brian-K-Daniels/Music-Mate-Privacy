using Microsoft.Maui.Storage;

namespace musicmate.Services
{
    /// <summary>
    /// Thin wrapper around MAUI <see cref="Preferences"/> with an in-memory
    /// <see cref="TestStore"/> so unit tests can construct <see cref="NoteSessionService"/>
    /// without a packaged app host.
    /// </summary>
    internal static class SessionPreferences
    {
        /// <summary>When non-null, all Get/Set/Remove go here instead of MAUI Preferences.</summary>
        internal static Dictionary<string, object?>? TestStore { get; set; }

        public static string Get(string key, string defaultValue)
        {
            if (TestStore != null)
                return TestStore.TryGetValue(key, out var value) && value is string s ? s : defaultValue;
            try { return Preferences.Get(key, defaultValue); }
            catch { return defaultValue; }
        }

        public static int Get(string key, int defaultValue)
        {
            if (TestStore != null)
                return TestStore.TryGetValue(key, out var value) ? ConvertToInt(value, defaultValue) : defaultValue;
            try { return Preferences.Get(key, defaultValue); }
            catch { return defaultValue; }
        }

        public static bool Get(string key, bool defaultValue)
        {
            if (TestStore != null)
                return TestStore.TryGetValue(key, out var value) && value is bool b ? b : defaultValue;
            try { return Preferences.Get(key, defaultValue); }
            catch { return defaultValue; }
        }

        public static double Get(string key, double defaultValue)
        {
            if (TestStore != null)
                return TestStore.TryGetValue(key, out var value) ? ConvertToDouble(value, defaultValue) : defaultValue;
            try { return Preferences.Get(key, defaultValue); }
            catch { return defaultValue; }
        }

        public static float Get(string key, float defaultValue)
        {
            if (TestStore != null)
                return TestStore.TryGetValue(key, out var value) ? ConvertToFloat(value, defaultValue) : defaultValue;
            try { return Preferences.Get(key, defaultValue); }
            catch { return defaultValue; }
        }

        public static void Set(string key, string value)
        {
            if (TestStore != null) { TestStore[key] = value; return; }
            try { Preferences.Set(key, value); } catch { }
        }

        public static void Set(string key, int value)
        {
            if (TestStore != null) { TestStore[key] = value; return; }
            try { Preferences.Set(key, value); } catch { }
        }

        public static void Set(string key, bool value)
        {
            if (TestStore != null) { TestStore[key] = value; return; }
            try { Preferences.Set(key, value); } catch { }
        }

        public static void Set(string key, double value)
        {
            if (TestStore != null) { TestStore[key] = value; return; }
            try { Preferences.Set(key, value); } catch { }
        }

        public static void Set(string key, float value)
        {
            if (TestStore != null) { TestStore[key] = value; return; }
            try { Preferences.Set(key, value); } catch { }
        }

        public static void Remove(string key)
        {
            if (TestStore != null) { TestStore.Remove(key); return; }
            try { Preferences.Remove(key); } catch { }
        }

        private static int ConvertToInt(object? value, int defaultValue) => value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            float f => (int)f,
            string s when int.TryParse(s, out var i) => i,
            _ => defaultValue
        };

        private static double ConvertToDouble(object? value, double defaultValue) => value switch
        {
            double d => d,
            float f => f,
            int i => i,
            long l => l,
            string s when double.TryParse(s, out var d) => d,
            _ => defaultValue
        };

        private static float ConvertToFloat(object? value, float defaultValue) => value switch
        {
            float f => f,
            double d => (float)d,
            int i => i,
            string s when float.TryParse(s, out var f) => f,
            _ => defaultValue
        };
    }
}
