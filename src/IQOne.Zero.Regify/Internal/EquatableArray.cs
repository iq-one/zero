using System.Collections;
using System.Collections.Immutable;

namespace IQOne.Zero.Regify.Internal;

/// <summary>
/// Value-equality wrapper over <see cref="ImmutableArray{T}"/>, required for incremental
/// caching. Avoids APIs unavailable on netstandard2.0.
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> _array;

    public EquatableArray(ImmutableArray<T> array) => _array = array;

    public static readonly EquatableArray<T> Empty = new(ImmutableArray<T>.Empty);

    private ImmutableArray<T> Safe => _array.IsDefault ? ImmutableArray<T>.Empty : _array;

    public int Count => Safe.Length;

    public T[] ToArray() => [.. Safe];

    public bool Equals(EquatableArray<T> other)
    {
        var a = Safe;
        var b = other.Safe;

        if (a.Length != b.Length) return false;

        for (var i = 0; i < a.Length; i++)
            if (!a[i].Equals(b[i]))
                return false;

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var item in Safe)
                hash = hash * 31 + (item?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Safe).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static implicit operator EquatableArray<T>(ImmutableArray<T> array) => new(array);
}
