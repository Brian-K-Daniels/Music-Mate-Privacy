using musicmate.Services;

namespace musicmate.Tests;

public class PlaybackOutputSelectionTests
{
    [Fact]
    public void NoUsbOutput_LeavesSystemDefault()
    {
        Assert.Null(PlaybackOutputSelection.Select(
            new[] { PlaybackOutputKind.BuiltInSpeaker }));
        Assert.Null(PlaybackOutputSelection.Select(
            new[] { PlaybackOutputKind.BuiltInSpeaker, PlaybackOutputKind.BluetoothMusic }));
    }

    [Fact]
    public void UsbMicOutput_UsesPhoneSpeaker()
    {
        Assert.Equal(
            PlaybackOutputKind.BuiltInSpeaker,
            PlaybackOutputSelection.Select(new[]
            {
                PlaybackOutputKind.UsbExternal,
                PlaybackOutputKind.BuiltInSpeaker
            }));
    }

    [Fact]
    public void UsbMicOutput_PrefersWiredHeadsetOverSpeaker()
    {
        Assert.Equal(
            PlaybackOutputKind.Wired,
            PlaybackOutputSelection.Select(new[]
            {
                PlaybackOutputKind.UsbExternal,
                PlaybackOutputKind.BuiltInSpeaker,
                PlaybackOutputKind.Wired
            }));
    }

    [Fact]
    public void UsbDisconnect_ReturnsToDefault()
    {
        var withMic = PlaybackOutputSelection.Select(new[]
        {
            PlaybackOutputKind.UsbExternal,
            PlaybackOutputKind.BuiltInSpeaker
        });
        var afterUnplug = PlaybackOutputSelection.Select(
            new[] { PlaybackOutputKind.BuiltInSpeaker });

        Assert.Equal(PlaybackOutputKind.BuiltInSpeaker, withMic);
        Assert.Null(afterUnplug);
    }
}
