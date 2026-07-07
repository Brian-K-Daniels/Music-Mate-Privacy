namespace musicmate.Services;

/// <summary>Shared defaults for mastery-related preferences (Settings + session service).</summary>
public static class MasteryPreferenceDefaults
{
    public const string MasteredMethod = "% Correct";
    public const int CorrectThreshold = 95;
    public const int MinCorrectCount = 6;
    public const int OmitMsAvgThreshold = 400;
    public const int StreakCrit = 3;
}
