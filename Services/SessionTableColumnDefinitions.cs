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
            "Pitch completion percentage: 100 × (notes eventually played correctly) ÷ (non-rest notes in the exercise). Retries and early timing tries do not lower this score. P+ / P− show pitch attempt outcomes separately."),
        new("Tmg", "Tmg",
            "Timing accuracy percentage from least-squares fitting of note onsets to expected beats. Each note scores 0–100 from timing error versus adaptive thresholds (good = ¼ of a sixteenth note, bad = a full sixteenth), then scores are averaged. Shown as 0.0 when timing could not be computed (fewer than 3 notes or insufficient beat variance)."),
        new("Ovrl", "Ovrl",
            "Overall accuracy percentage. When timing accuracy is available, the average of pitch completion (Pc%) and timing accuracy (Tmg); otherwise equals pitch completion alone."),
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
            "Count of non-rest note attempts marked overall wrong."),
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
