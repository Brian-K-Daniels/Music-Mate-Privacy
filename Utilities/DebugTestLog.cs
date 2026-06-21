using System.Diagnostics;

namespace musicmate.Utilities
{
    /// <summary>
    /// DEBUG-only test output. On Android writes to logcat via <c>Android.Util.Log</c>
    /// (tag <c>MusicMate</c>) so lines appear in adb logcat and the VS Android Device Log.
    /// </summary>
    public static class DebugTestLog
    {
        public const string AndroidTag = "MusicMate";

        [Conditional("DEBUG")]
        public static void Write(string message)
        {
            Debug.WriteLine(message);
#if ANDROID
            global::Android.Util.Log.Debug(AndroidTag, message);
#endif
        }
    }
}
