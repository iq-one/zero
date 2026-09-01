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
        if (_disposed) return;

        if (disposing) ReleaseManagedResources();

        ReleaseUnmanagedResources();

        _disposed = true;
    }

    /// <summary>Releases managed resources. Not called from the finalizer.</summary>
    protected virtual void ReleaseManagedResources() { }

    /// <summary>Releases unmanaged resources. Called from both paths.</summary>
    protected virtual void ReleaseUnmanagedResources() { }

    /// <summary>Releases unmanaged resources if <see cref="Dispose()"/> was never called.</summary>
    ~Disposable() => Dispose(false);
}
