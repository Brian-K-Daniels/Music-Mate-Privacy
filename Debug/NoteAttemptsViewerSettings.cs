using Microsoft.Maui.Storage;

namespace musicmate.LayoutDebug;

/// <summary>
/// DEBUG preference: show the Music page button that opens the Note Attempts viewer.
/// Always false in Release builds.
/// </summary>
public static class NoteAttemptsViewerSettings
{
    public const string PreferenceKey = "Debug.ShowNoteAttemptsViewerButton";

    public static bool IsMusicPageButtonEnabled
    {
        get =>
#if DEBUG
            Preferences.Default.Get(PreferenceKey, false);
#else
            false;
#endif
        set => Preferences.Default.Set(PreferenceKey, value);
    }
}
