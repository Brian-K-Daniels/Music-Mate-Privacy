namespace musicmate.Services
{
    public interface IAudioCaptureService
    {
        int SampleRate { get; }
        int BufferSize { get; }

        event Action? MaxBlocksReached;

        /// <summary>
        /// The recorder stopped because the input device changed or failed, not because
        /// <see cref="StopCapture"/> was called. Count-In and Play stay running; the page reopens the mic.
        /// </summary>
        event Action? CaptureRouteLost;

        Task EnsurePermissionAsync();
        void StartCapture(Action<short[]> onBlock);
        /// <summary>Best-effort start; returns false instead of throwing when the mic cannot open.</summary>
        bool TryStartCapture(Action<short[]> onBlock, out string? error);
        void StopCapture();
        bool IsCapturing { get; }
    }
}
