#if ANDROID
using System.Runtime.Versioning;
using Android.Media;
using musicmate.Diagnostics;

namespace musicmate.Services
{
    /// <summary>
    /// Keeps Play, Count-In, and metronome tones on the phone speaker (or a real
    /// headset) while an external microphone is the input. Clears that pin when
    /// the external device is gone so routing cannot stay stuck.
    /// </summary>
    [SupportedOSPlatform("android23.0")]
    internal static class AndroidPlaybackRoute
    {
        private static readonly object Gate = new();
        private static AudioManager? _manager;
        private static DeviceCallback? _callback;
        private static AudioDeviceInfo? _preferred;
        private static string? _lastSummary;
        private static bool _pinned;

        public static bool HasPinnedOutput
        {
            get { lock (Gate) return _pinned; }
        }

        public static void Apply(string reason)
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(23))
                return;

            try
            {
                var manager = Manager();
                if (manager == null)
                    return;

                EnsureCallback(manager);

                var outputs = manager.GetDevices(GetDevicesTargets.Outputs) ?? System.Array.Empty<AudioDeviceInfo>();
                var inputs = manager.GetDevices(GetDevicesTargets.Inputs) ?? System.Array.Empty<AudioDeviceInfo>();
                string modeBefore = manager.Mode.ToString() ?? "";

                // A communications mode sends media toward the external mic's
                // nonexistent speaker or the earpiece. Do not touch an active call.
                if (manager.Mode == Mode.InCommunication)
                {
                    manager.Mode = Mode.Normal;
                }

                var kinds = new PlaybackOutputKind[outputs.Length];
                for (int i = 0; i < outputs.Length; i++)
                    kinds[i] = outputs[i] == null ? PlaybackOutputKind.Other : Map(outputs[i]!);

                PlaybackOutputKind? choice = PlaybackOutputSelection.Select(kinds);
                AudioDeviceInfo? device = choice == null ? null : Find(outputs, choice.Value);

                lock (Gate)
                {
                    _preferred = device;
                    _pinned = device != null;
                }

                string summary =
                    $"AUDIO ROUTE: reason={reason} mode={modeBefore}-> {manager.Mode} " +
                    $"pin={(device == null ? "default" : Describe(device))} " +
                    $"inputs=[{Join(inputs)}] outputs=[{Join(outputs)}]";
                if (!string.Equals(summary, _lastSummary, StringComparison.Ordinal))
                {
                    _lastSummary = summary;
                    ListeningStartupLog.Write(summary);
                }
            }
            catch (Exception ex)
            {
                ListeningStartupLog.Write($"AUDIO ROUTE: failed reason={reason} {ex.Message}");
            }
        }

        public static void ApplyTo(AudioTrack track)
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(23))
                return;

            AudioDeviceInfo? device;
            lock (Gate)
                device = _preferred;

            try
            {
                track.SetPreferredDevice(device);
            }
            catch (Exception ex)
            {
                ListeningStartupLog.Write($"AUDIO ROUTE: track device failed {ex.Message}");
            }
        }

        private static AudioDeviceInfo? Find(AudioDeviceInfo[] outputs, PlaybackOutputKind kind)
        {
            foreach (var device in outputs)
            {
                if (device != null && Map(device) == kind)
                    return device;
            }

            return null;
        }

        private static PlaybackOutputKind Map(AudioDeviceInfo device)
            => device.Type switch
            {
                AudioDeviceType.BuiltinSpeaker => PlaybackOutputKind.BuiltInSpeaker,
                AudioDeviceType.WiredHeadphones or AudioDeviceType.WiredHeadset => PlaybackOutputKind.Wired,
                AudioDeviceType.BluetoothA2dp => PlaybackOutputKind.BluetoothMusic,
                AudioDeviceType.UsbDevice or AudioDeviceType.UsbHeadset or AudioDeviceType.UsbAccessory
                    => PlaybackOutputKind.UsbExternal,
                _ => PlaybackOutputKind.Other
            };

        private static string Join(AudioDeviceInfo[] devices)
        {
            if (devices.Length == 0)
                return "none";
            var parts = new string[devices.Length];
            for (int i = 0; i < devices.Length; i++)
                parts[i] = devices[i] == null ? "?" : Describe(devices[i]!);
            return string.Join(", ", parts);
        }

        private static string Describe(AudioDeviceInfo device)
            => $"{device.Type}#{device.Id}:{device.ProductName}";

        private static AudioManager? Manager()
        {
            if (_manager != null)
                return _manager;

            var context = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity
                ?? Android.App.Application.Context;
            if (context == null)
                return null;

            _manager = context.GetSystemService(Android.Content.Context.AudioService) as AudioManager;
            return _manager;
        }

        private static void EnsureCallback(AudioManager manager)
        {
            if (_callback != null)
                return;

            _callback = new DeviceCallback();
            manager.RegisterAudioDeviceCallback(_callback, null);
        }

        private sealed class DeviceCallback : AudioDeviceCallback
        {
            public override void OnAudioDevicesAdded(AudioDeviceInfo[]? addedDevices)
                => Apply("device-added");

            public override void OnAudioDevicesRemoved(AudioDeviceInfo[]? removedDevices)
                => Apply("device-removed");
        }
    }
}
#endif
