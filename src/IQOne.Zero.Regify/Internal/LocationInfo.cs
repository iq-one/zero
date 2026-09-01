using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace IQOne.Zero.Regify.Internal;

/// <summary>
/// Serializable stand-in for <see cref="Location"/>. Carrying the real type through the
/// incremental pipeline would defeat caching.
/// </summary>
internal sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

    public static LocationInfo? From(SyntaxNode? node)
    {
        if (node is null) return null;
        var span = node.GetLocation().GetLineSpan();
        return new LocationInfo(span.Path, node.Span, span.Span);
    }
}
