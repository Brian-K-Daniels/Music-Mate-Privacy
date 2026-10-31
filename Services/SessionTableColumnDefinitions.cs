namespace musicmate.Services;

/// <summary>
/// Display heading and explanation for each Session table column on My Progress.
/// Definitions are derived from <see cref="PracticeSessionPersistence.BuildSessionStat"/>
/// and the session statistics logic in <see cref="NoteSessionService"/>.
/// </summary>
public sealed record SessionTableColumnDefinition(
    string ColumnKey,
    string Heading,
    string Definition);

public static class SessionTableColumnDefinitions
{
    public static IReadOnlyList<SessionTableColumnDefinition> All { get; } =
    [
        new("Date", "Date",
            "When the session was saved (local date and time at the end of practice)."),
        new("Level", "Level",
            "Child difficulty level used during the session (0 means adult mode)."),
        new("CorrectPercent", "Pc%",
            "Pitch completion percentage: 100 × (notes eventually played correctly) ÷ (non-rest notes in the exercise). Retries and early timing tries do not lower this score. P+ / P− show pitch attempt outcomes separately. Rests are counted in R+ and R−, not here."),
        new("Tmg", "Tmg",
            "Timing quality percentage (0–100) for non-rest notes that were compared with the beat. Each attack is scored by how far it was from its expected onset, relative to the timing window on that side (early or late). A centered attack scores 100. An attack at the edge of the accepted window scores 50. An attack twice that far scores 0. T+ and T− count whether the attack was inside the window; Tmg measures how close it was. Rests are not included. Shown as 0.0 when no note had a timing comparison."),
        new("Ovrl", "Ovrl",
            "Overall accuracy percentage. When timing quality is available, the average of pitch completion (Pc%) and timing quality (Tmg); otherwise equals pitch completion alone. Rests are reported in R+ and R− and are not part of this average."),
        new("PitchRight", "P+",
            "Count of non-rest note attempts where pitch was correct."),
        new("PitchWrong", "P−",
            "Count of non-rest note attempts where pitch was wrong (wrong pitch class). Timing-only rejects with correct pitch are not counted here."),
        new("TimingRight", "T+",
            "Count of non-rest note attempts marked timing correct (TimingCorrect = true). Attempts with no timing evaluation are excluded."),
        new("TimingWrong", "T−",
            "Count of non-rest note attempts marked timing wrong (TimingCorrect = false)."),
        new("OverallRight", "O+",
            "Count of non-rest note attempts marked overall correct. Pitch must be correct; when timing affects mastery at this level, timing must also be correct."),
        new("OverallWrong", "O−",
            "Count of non-rest note attempts marked overall wrong. Rest mistakes are counted in R−, not here."),
        new("RestRight", "R+",
            "Count of rest measures performed correctly (for example, silence held during a rest)."),
        new("RestWrong", "R−",
            "Count of rest measures performed incorrectly (for example, sound detected during a rest)."),
        new("Key", "Key",
            "The musical key setting for the session."),
        new("What", "What",
            "Abbreviated label for the What to Play selection (scale, arpeggio, tune, random mode, tuner, rhythm exercise, and similar choices)."),
        new("Rand", "Rand",
            "Whether random note selection was enabled (Y = yes, N = no)."),
        new("AccPct", "Acc%",
            "The accidental percentage setting used when generating the exercise."),
        new("Hi", "Hi",
            "The name of the highest note in the exercise material."),
        new("Lo", "Lo",
            "The name of the lowest note in the exercise material."),
        new("Instrument", "Inst",
            "The instrument display name used for the session."),
    ];

    public static SessionTableColumnDefinition? GetByColumnKey(string columnKey)
    {
        if (string.IsNullOrWhiteSpace(columnKey))
            return null;

        foreach (var definition in All)
        {
            if (string.Equals(definition.ColumnKey, columnKey, StringComparison.Ordinal))
                return definition;
        }

        return null;
    }
}
