namespace musicmate.Services;

/// <summary>
/// Central post-scoring cleanup for Note Attempts after a completed Music session.
/// </summary>
public static class NoteAttemptSessionCleanup
{
    /// <summary>
    /// After session scoring/persistence has finished and attempts for
    /// <paramref name="keepSessionId"/> have been saved, optionally delete Note
    /// Attempts from every earlier session so only the just-completed session remains.
    /// When <paramref name="retainOnlyLatestSession"/> is false, this is a no-op.
    /// </summary>
    public static async Task<int> RetainOnlyCompletedSessionIfEnabledAsync(
        NoteAttemptDatabase? database,
        string? keepSessionId,
        bool retainOnlyLatestSession)
    {
        if (!retainOnlyLatestSession || database == null || string.IsNullOrWhiteSpace(keepSessionId))
            return 0;

        await database.InitializeAsync();
        return await database.DeleteExceptSessionIdAsync(keepSessionId);
    }

    /// <summary>Backward-compatible name for <see cref="RetainOnlyCompletedSessionIfEnabledAsync"/>.</summary>
    public static Task<int> ClearCompletedSessionIfEnabledAsync(
        NoteAttemptDatabase? database,
        string? sessionId,
        bool clearAfterSession)
        => RetainOnlyCompletedSessionIfEnabledAsync(database, sessionId, clearAfterSession);
}
