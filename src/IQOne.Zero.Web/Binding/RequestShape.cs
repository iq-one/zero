using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Reflection;

namespace IQOne.Zero.Web.Binding;

/// <summary>
/// What the binder needs to know about a request type before it overlays anything.
/// </summary>
/// <remarks>
/// A query string carries no types: <c>?id=1&amp;id=2</c> and <c>?tags=a&amp;tags=b</c> look
/// identical on the wire, and only the request decides whether repetition means "the last
/// one" or "both". Asking the type is what lets a scalar keep working when a key is repeated
/// and a collection keep working when it is not.
/// </remarks>
internal static class RequestShape
{
    private static readonly ConcurrentDictionary<Type, FrozenSet<string>> Many = new();

    /// <summary>Whether the named member of this request holds several values.</summary>
    /// <param name="requestType">The request being bound.</param>
    /// <param name="member">The route or query key.</param>
    /// <returns><see langword="true"/> when the member is a collection.</returns>
    public static bool BindsMany(Type requestType, string member)
        => Many.GetOrAdd(requestType, Discover).Contains(member);

    /// <remarks>
    /// Properties only, matched without regard to case. The serializer binds a constructor
    /// parameter through the property of the same name, so the property set is the whole
    /// contract; and route and query keys are matched case-insensitively by ASP.NET, so
    /// matching them any other way here would answer differently for <c>?Tags=</c>.
    /// </remarks>
    private static FrozenSet<string> Discover(Type requestType)
        => requestType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => IsMany(p.PropertyType))
            .Select(p => p.Name)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsMany(Type type)
        => type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
}
