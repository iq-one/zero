namespace IQOne.Zero.Fundamentals.Disposable;

/// <summary>Supports both synchronous and asynchronous release. Prefer the async path.</summary>
public abstract class AsyncDisposable : Disposable, IAsyncDisposable
{
    private bool _asyncDisposed;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_asyncDisposed) return;

        await ReleaseManagedResourcesAsync().ConfigureAwait(false);

        ReleaseUnmanagedResources();

        _asyncDisposed = true;

        GC.SuppressFinalize(this);
    }

    /// <summary>Releases managed resources asynchronously.</summary>
    protected virtual ValueTask ReleaseManagedResourcesAsync() => ValueTask.CompletedTask;
}
