namespace musicmate.Services
{
    /// <summary>
    /// Maps Shell location strings to a stable flyout destination key so same-page
    /// hamburger taps can be detected without a full navigation.
    /// </summary>
    internal static class ShellNavigationTarget
    {
        public static bool IsSameDestination(string? currentLocation, string? targetLocation)
        {
            string current = CanonicalKey(currentLocation);
            string target = CanonicalKey(targetLocation);
            return current.Length > 0
                && string.Equals(current, target, StringComparison.OrdinalIgnoreCase);
        }

        public static string CanonicalKey(string? location)
        {
            if (string.IsNullOrWhiteSpace(location))
                return string.Empty;

            string s = location.Trim();
            int query = s.IndexOf('?');
            if (query >= 0)
                s = s[..query];

            // More specific needles first.
            if (Contains(s, "TunerEntry"))
                return "Tuner";
            if (Contains(s, "IntervalSingingTraining") || Contains(s, "SingingTraining"))
                return "SingingTraining";
            if (Contains(s, "IntervalEarTraining") || Contains(s, "EarTraining"))
                return "EarTraining";
            if (Contains(s, "IntervalSightTraining"))
                return "SightTraining";
            if (Contains(s, "WhatToPlay"))
                return "WhatToPlay";
            if (Contains(s, "Statistics"))
                return "Statistics";
            if (Contains(s, "AdvancedPage") || Contains(s, "SettingsAdvanced"))
                return "SettingsAdvanced";
            if (Contains(s, "ResetOptions") || Contains(s, "SettingsReset"))
                return "SettingsReset";
            if (Contains(s, "SettingsPage") || SegmentEquals(s, "Settings"))
                return "Settings";
            if (Contains(s, "About"))
                return "About";
            if (Contains(s, "HomePage") || SegmentEquals(s, "Home"))
                return "Home";
            if (Contains(s, "DebugItems"))
                return "DebugItems";
            if (Contains(s, "MusicPage") || SegmentEquals(s, "Music"))
                return "Music";

            return LastSegment(s);
        }

        private static bool Contains(string location, string needle)
            => location.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool SegmentEquals(string location, string segment)
        {
            foreach (string part in location.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.Equals(part, segment, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string LastSegment(string location)
        {
            string[] parts = location.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? string.Empty : parts[^1];
        }
    }
}
