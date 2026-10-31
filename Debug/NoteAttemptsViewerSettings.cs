using Microsoft.Maui.Storage;

namespace musicmate.LayoutDebug;

    /// <summary>
    /// Preference: show the Music page button that opens the Note Attempts viewer.
    /// The viewer is a development tool. It is compiled into Debug and LocalRelease,
    /// and it stays off until someone turns it on.
    /// </summary>
    public static class NoteAttemptsViewerSettings
    {
        public const string PreferenceKey = "Debug.ShowNoteAttemptsViewerButton";

        public static bool IsCompiledIn
        {
            get
            {
#if DEBUG || LOCAL_RELEASE
                return true;
#else
                return false;
#endif
            }
        }

        public static bool IsMusicPageButtonEnabled
        {
            get => IsCompiledIn && Preferences.Default.Get(PreferenceKey, false);
            set => Preferences.Default.Set(PreferenceKey, value);
        }
    }
