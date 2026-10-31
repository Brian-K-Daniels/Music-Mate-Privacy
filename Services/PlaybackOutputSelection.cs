namespace musicmate.Services
{
    public enum PlaybackOutputKind
    {
        BuiltInSpeaker,
        Wired,
        BluetoothMusic,
        UsbExternal,
        Other
    }

    /// <summary>
    /// Chooses where generated tones go when an external microphone is attached.
    /// A USB mic often also registers as an output with no speaker. Media routed
    /// there is silent. With no USB output present, leave the system default alone.
    /// </summary>
    public static class PlaybackOutputSelection
    {
        public static PlaybackOutputKind? Select(IReadOnlyList<PlaybackOutputKind> outputs)
        {
            if (outputs == null || outputs.Count == 0)
                return null;
            if (!outputs.Contains(PlaybackOutputKind.UsbExternal))
                return null;
            if (outputs.Contains(PlaybackOutputKind.Wired))
                return PlaybackOutputKind.Wired;
            if (outputs.Contains(PlaybackOutputKind.BluetoothMusic))
                return PlaybackOutputKind.BluetoothMusic;
            if (outputs.Contains(PlaybackOutputKind.BuiltInSpeaker))
                return PlaybackOutputKind.BuiltInSpeaker;
            return null;
        }
    }
}
