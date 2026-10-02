using musicmate.Drawables;

namespace musicmate.Services;

/// <summary>
/// Maps each staff note state to its color setting.
/// The four states stay distinct: a graded note is played or wrong, not "to be played".
/// </summary>
public static class NoteStateColors
{
    public static AppColorTarget TargetFor(StaffNoteState state) => state switch
    {
        StaffNoteState.Pending => AppColorTarget.NoteUnplayed,
        StaffNoteState.Current => AppColorTarget.NoteToBePlayed,
        StaffNoteState.Correct => AppColorTarget.NotePlayed,
        StaffNoteState.Wrong => AppColorTarget.NoteWrong,
        _ => AppColorTarget.NoteUnplayed,
    };
}
