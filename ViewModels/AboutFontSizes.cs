using System.Collections.ObjectModel;

namespace musicmate.ViewModels
{
    /// <summary>
    /// Shared About-page body/search font sizes (Settings → Display and About page).
    /// Even sizes from 6 through <see cref="Max"/>; default remains 12.
    /// </summary>
    /// <summary>Canonical even font sizes for the About guide (6…24).</summary>
    public static class AboutFontSizes
    {
        public const double Min = 6;
        public const double Max = 24;
        public const double Step = 2;
        public const double Default = 12;

        /// <summary>Preference key: "Font used in About page".</summary>
        public const string PreferenceKey = "About_FontSize";

        public static IReadOnlyList<double> All { get; } = Build();

        public static ObservableCollection<double> CreateCollection()
            => new(All);

        public static List<double> CreateList()
            => new(All);

        public static bool Contains(double size)
            => All.Contains(size);

        public static double ClampOrDefault(double size)
            => Contains(size) ? size : Default;

        private static double[] Build()
        {
            int count = (int)((Max - Min) / Step) + 1;
            var sizes = new double[count];
            for (int i = 0; i < count; i++)
                sizes[i] = Min + i * Step;
            return sizes;
        }
    }
}
