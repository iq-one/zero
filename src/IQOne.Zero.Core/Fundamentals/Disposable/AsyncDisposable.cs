namespace IQOne.Zero.Fundamentals.Disposable;

/// <summary>Supports both synchronous and asynchronous release. Prefer the async path.</summary>
/// <remarks>
/// Both paths share the base class's single disposed flag, so whichever runs first is the
/// only one that releases anything.
/// </remarks>
public abstract class AsyncDisposable : Disposable, IAsyncDisposable
{
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!TryBeginDispose()) return;

        await ReleaseManagedResourcesAsync().ConfigureAwait(false);

        ReleaseUnmanagedResources();

        GC.SuppressFinalize(this);
    }

    /// <summary>Releases managed resources asynchronously.</summary>
    protected virtual ValueTask ReleaseManagedResourcesAsync() => ValueTask.CompletedTask;
}
