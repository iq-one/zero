namespace IQOne.Zero.Fundamentals.Disposable;

/// <summary>Standard dispose pattern. Derived types override the release methods only.</summary>
public abstract class Disposable : IDisposable
{
    private bool _disposed;

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases resources once, whether called or finalized.</summary>
    /// <param name="disposing">True when called from <see cref="Dispose()"/>, false from the finalizer.</param>
    protected void Dispose(bool disposing)
    {
        if (!TryBeginDispose()) return;

        if (disposing) ReleaseManagedResources();

        ReleaseUnmanagedResources();
    }

    /// <summary>
    /// Claims the single release this object gets, whichever path asked for it.
    /// </summary>
    /// <remarks>
    /// One flag for both the synchronous and the asynchronous path. Two flags let
    /// <c>DisposeAsync</c> followed by a defensive <c>Dispose</c> release everything twice,
    /// and a derived type that frees a handle in
    /// <see cref="ReleaseUnmanagedResources"/> — documented as running on both paths — frees
    /// it twice.
    /// </remarks>
    /// <returns><see langword="true"/> for the first caller, <see langword="false"/> after that.</returns>
    private protected bool TryBeginDispose()
    {
        if (_disposed) return false;

        _disposed = true;

        return true;
    }

    /// <summary>Releases managed resources. Not called from the finalizer.</summary>
    protected virtual void ReleaseManagedResources() { }

    /// <summary>Releases unmanaged resources. Called from both paths, exactly once.</summary>
    protected virtual void ReleaseUnmanagedResources() { }

    /// <summary>Releases unmanaged resources if <see cref="Dispose()"/> was never called.</summary>
    ~Disposable() => Dispose(false);
}
