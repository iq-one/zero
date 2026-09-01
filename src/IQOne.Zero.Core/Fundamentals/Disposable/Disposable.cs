namespace IQOne.Zero.Fundamentals.Disposable;

/// <summary>Standard dispose pattern. Derived types override the release methods only.</summary>
public abstract class Disposable : IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing) ReleaseManagedResources();

        ReleaseUnmanagedResources();

        _disposed = true;
    }

    protected virtual void ReleaseManagedResources() { }

    protected virtual void ReleaseUnmanagedResources() { }

    ~Disposable() => Dispose(false);
}
