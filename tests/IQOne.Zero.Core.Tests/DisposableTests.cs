using IQOne.Zero.Fundamentals.Disposable;

namespace IQOne.Zero.Tests;

/// <summary>
/// <see cref="AsyncDisposable"/> and <see cref="Disposable"/> share one flag. They used to
/// keep one each, so a call down one path did not stop the other — and a derived type that
/// frees a handle in <c>ReleaseUnmanagedResources</c>, documented as running on both paths,
/// freed it twice.
/// </summary>
public class DisposableTests
{
    private sealed class Counting : AsyncDisposable
    {
        public int Managed { get; private set; }

        public int ManagedAsync { get; private set; }

        public int Unmanaged { get; private set; }

        protected override void ReleaseManagedResources() => Managed++;

        protected override ValueTask ReleaseManagedResourcesAsync()
        {
            ManagedAsync++;

            return ValueTask.CompletedTask;
        }

        protected override void ReleaseUnmanagedResources() => Unmanaged++;
    }

    [Fact]
    public async Task DisposeAsync_then_Dispose_releases_once()
    {
        var subject = new Counting();

        await subject.DisposeAsync();
        subject.Dispose();

        subject.ManagedAsync.Should().Be(1);
        subject.Managed.Should().Be(0, "the asynchronous path already released everything");
        subject.Unmanaged.Should().Be(1);
    }

    [Fact]
    public async Task Dispose_then_DisposeAsync_releases_once()
    {
        var subject = new Counting();

        subject.Dispose();
        await subject.DisposeAsync();

        subject.Managed.Should().Be(1);
        subject.ManagedAsync.Should().Be(0);
        subject.Unmanaged.Should().Be(1);
    }

    [Fact]
    public async Task Repeating_either_call_changes_nothing()
    {
        var subject = new Counting();

        await subject.DisposeAsync();
        await subject.DisposeAsync();
        subject.Dispose();
        subject.Dispose();

        subject.Unmanaged.Should().Be(1);
    }
}
