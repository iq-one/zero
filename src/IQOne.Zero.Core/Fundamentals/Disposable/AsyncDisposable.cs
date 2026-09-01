namespace IQOne.Zero.Fundamentals.Disposable;

/// <summary>Supports both synchronous and asynchronous release. Prefer the async path.</summary>
public abstract class AsyncDisposable : Disposable, IAsyncDisposable
{
    private bool _asyncDisposed;

    public async ValueTask DisposeAsync()
    {
        if (_asyncDisposed) return;

        await ReleaseManagedResourcesAsync().ConfigureAwait(false);

        ReleaseUnmanagedResources();

        _asyncDisposed = true;

        GC.SuppressFinalize(this);
    }

    protected virtual ValueTask ReleaseManagedResourcesAsync() => ValueTask.CompletedTask;
}
