namespace musicmate.Tests;

/// <summary>
/// Runs work on a thread that does not inherit the caller's <see cref="AsyncLocal{T}"/>.
/// </summary>
internal static class TestExecutionIsolation
{
    public static void RunOnFreshThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        })
        {
            IsBackground = true,
        };

        // Thread.Start() captures ExecutionContext by default, which would copy
        // this test's AsyncLocal hooks onto the worker. Parallel xUnit tests
        // do not share that context, so suppress flow to match them.
        using (ExecutionContext.SuppressFlow())
            thread.Start();
        thread.Join();
        if (error != null)
            throw error;
    }
}
