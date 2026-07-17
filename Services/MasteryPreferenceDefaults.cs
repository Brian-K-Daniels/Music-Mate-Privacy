namespace musicmate.Services;

/// <summary>Shared defaults for mastery-related preferences (Settings + session service).</summary>
public static class MasteryPreferenceDefaults
{
    public const string MasteredMethod = "% Correct";
    public const int CorrectThreshold = 60;
    public const int MinCorrectCount = 6;
    public const int OmitMsAvgThreshold = 0;
    public const int StreakCrit = 3;
    /// <summary>Factory default: mastery may influence Random / Repeat-Same generation.</summary>
    public const bool UseNoteMasteryForGeneration = true;
}
