namespace musicmate.Models
{
    /// <summary>
    /// Static definition of an arpeggio shape.  Future generation can transpose these
    /// semitone intervals from a chosen root, then spell notes for the active key/level.
    /// </summary>
    public sealed record ArpeggioPattern(
        string Id,
        string DisplayName,
        string Quality,
        IReadOnlyList<int> SemitoneIntervals,
        IReadOnlyList<string> DegreeLabels,
        int FirstAvailableLevel,
        bool IncludesSeventh = false);

    /// <summary>Scale-degree root option for level-based arpeggio practice.</summary>
    public sealed record ArpeggioRootOption(
        int ScaleDegree,
        string DisplayName,
        int Weight);

    /// <summary>
    /// Inspectable level model for arpeggios.  This is catalog/availability data only;
    /// Random generation will consume it in a later change.
    /// </summary>
    public sealed record ArpeggioLevelAvailability(
        int Level,
        IReadOnlyList<ArpeggioPattern> Patterns,
        IReadOnlyList<ArpeggioRootOption> RootOptions,
        bool AllowsInversions,
        int MaxOctaves,
        string Summary);
}
