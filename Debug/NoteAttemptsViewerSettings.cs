using Microsoft.Maui.Storage;

namespace musicmate.LayoutDebug;

/// <summary>
/// Preference: show the Music page button that opens the Note Attempts viewer.
/// Defaults to on so the nav button is available in Release and Debug.
/// </summary>
public static class NoteAttemptsViewerSettings
{
    public const string PreferenceKey = "Debug.ShowNoteAttemptsViewerButton";

    public static bool IsMusicPageButtonEnabled
    {
        get => Preferences.Default.Get(PreferenceKey, true);
        set => Preferences.Default.Set(PreferenceKey, value);
    }
}
