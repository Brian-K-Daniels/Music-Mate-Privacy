namespace musicmate.Services;

/// <summary>
/// Dispatches Google Play Billing UI work (LaunchBillingFlow, About alerts) onto the main thread.
/// Tests inject isMainThread / invokeOnMainThread so they do not require a MAUI dispatcher.
/// </summary>
public static class BillingUiThread
{
    public static Task InvokeAsync(Action action)
        => InvokeAsync(() =>
        {
            action();
            return Task.CompletedTask;
        });

    public static Task InvokeAsync(Func<Task> action)
        => InvokeAsync(action, isMainThread: null, invokeOnMainThread: null);

    public static async Task InvokeAsync(
        Func<Task> action,
        Func<bool>? isMainThread,
        Func<Func<Task>, Task>? invokeOnMainThread)
    {
        ArgumentNullException.ThrowIfNull(action);

        bool onMain = isMainThread?.Invoke()
            ?? Microsoft.Maui.ApplicationModel.MainThread.IsMainThread;
        if (onMain)
        {
            await action().ConfigureAwait(false);
            return;
        }

        var invoke = invokeOnMainThread
            ?? new Func<Func<Task>, Task>(
                f => Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(f));
        await invoke(action).ConfigureAwait(false);
    }

    public static async Task<T> InvokeAsync<T>(
        Func<T> function,
        Func<bool>? isMainThread = null,
        Func<Func<Task>, Task>? invokeOnMainThread = null)
    {
        ArgumentNullException.ThrowIfNull(function);

        T result = default!;
        await InvokeAsync(
            () =>
            {
                result = function();
                return Task.CompletedTask;
            },
            isMainThread,
            invokeOnMainThread).ConfigureAwait(false);
        return result;
    }
}
