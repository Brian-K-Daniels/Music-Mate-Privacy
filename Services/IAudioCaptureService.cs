namespace musicmate.Services
{
    public interface IAudioCaptureService
    {
        int SampleRate { get; }
        int BufferSize { get; }

        event Action? MaxBlocksReached;

        Task EnsurePermissionAsync();
        void StartCapture(Action<short[]> onBlock);
        void StopCapture();
    }
}
