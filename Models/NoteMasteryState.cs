namespace musicmate.Models;

/// <summary>Visual / practice state for a written note on the Note Mastery staff.</summary>
public enum NoteMasteryState
{
    NotYetAttempted,
    NeedsPractice,
    Improving,
    Mastered,
}

public static class NoteMasteryStateLabels
{
    public static string DisplayName(NoteMasteryState state) => state switch
    {
        NoteMasteryState.Mastered => "Mastered",
        NoteMasteryState.Improving => "Improving",
        NoteMasteryState.NeedsPractice => "Practice Next",
        NoteMasteryState.NotYetAttempted => "Not Tried Yet",
        _ => state.ToString(),
    };

    /// <summary>Compact legend / marker character (non-color cue for accessibility).</summary>
    public static string Marker(NoteMasteryState state) => state switch
    {
        NoteMasteryState.Mastered => "★",
        NoteMasteryState.Improving => "▲",
        NoteMasteryState.NeedsPractice => "●",
        NoteMasteryState.NotYetAttempted => "○",
        _ => "?",
    };

    /// <summary>Short explanation for legend tooltips / accessibility hints.</summary>
    public static string Description(NoteMasteryState state) => state switch
    {
        NoteMasteryState.Mastered =>
            "Fully meets your current mastery settings (streak or accuracy thresholds).",
        NoteMasteryState.Improving =>
            "Making progress toward mastery, but not fully mastered yet.",
        NoteMasteryState.NeedsPractice =>
            "Needs practice next — accuracy or streak is still below the improving threshold.",
        NoteMasteryState.NotYetAttempted =>
            "No practice attempts recorded for this written note yet.",
        _ => string.Empty,
    };

    /// <summary>Legend text with marker, e.g. "★ Mastered".</summary>
    public static string LegendLabel(NoteMasteryState state)
        => $"{Marker(state)} {DisplayName(state)}";
}
