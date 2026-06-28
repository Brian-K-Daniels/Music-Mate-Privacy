using System.Diagnostics;

namespace musicmate.Services
{
    /// <summary>DEBUG timing scopes for practice session start.</summary>
    public static class PracticeSessionStartProfiler
    {
        public static IDisposable Scope(string phase)
        {
#if DEBUG
            return new PhaseScope(phase);
#else
            return NoOpScope.Instance;
#endif
        }

#if DEBUG
        private sealed class PhaseScope : IDisposable
        {
            private readonly string _phase;
            private readonly Stopwatch _sw = Stopwatch.StartNew();

            public PhaseScope(string phase) => _phase = phase;

            public void Dispose()
            {
                _sw.Stop();
                Debug.WriteLine($"[SessionStart] {_phase}: {_sw.ElapsedMilliseconds} ms");
            }
        }
#endif

        private sealed class NoOpScope : IDisposable
        {
            public static readonly NoOpScope Instance = new();
            public void Dispose() { }
        }
    }
}
