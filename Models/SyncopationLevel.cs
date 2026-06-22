namespace musicmate.Models
{
    /// <summary>
    /// Degree of off-beat rhythmic emphasis in generated measures.
    /// </summary>
    public enum SyncopationLevel
    {
        /// <summary>Sequential on-beat fill (default).</summary>
        None,

        /// <summary>Occasional weak-beat / upbeat accents at eighth-note level.</summary>
        Simple,

        /// <summary>Stronger off-beat patterns with more frequent syncopated motifs.</summary>
        Full
    }

    public static class SyncopationLevelHelper
    {
        public static SyncopationLevel Parse(string? value) => value switch
        {
            "Simple" => SyncopationLevel.Simple,
            "Full" => SyncopationLevel.Full,
            _ => SyncopationLevel.None
        };

        public static string ToDisplayString(SyncopationLevel level) => level switch
        {
            SyncopationLevel.Simple => "Simple",
            SyncopationLevel.Full => "Full",
            _ => "None"
        };
    }
}
