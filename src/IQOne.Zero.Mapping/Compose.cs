namespace IQOne.Zero.Mapping;

/// <summary>
/// Says, inside a map, that another map fills this member.
/// </summary>
/// <remarks>
/// <para>
/// This is a MARKER. It never runs, and calling it throws — the generator reads it out of the
/// declaration and writes the other map's tree in its place. That is what makes a nested model
/// one <c>SELECT</c> instead of a second query: a method call cannot be translated, so the
/// child cannot be *called* here; it has to be written out.
/// </para>
/// <code>
/// map.Member(m =&gt; m.BedType, e =&gt; e.BedType.To&lt;BedTypeModel&gt;());
/// map.Member(m =&gt; m.Documents, e =&gt; e.Documents.To&lt;List&lt;DocumentModel&gt;&gt;());
/// </code>
/// <para>
/// Composition is what stops the same nested block being copied into every query that returns
/// the model, which is how two of them end up disagreeing. It also brings two things a
/// hand-written nested initialiser has to remember: the null check — without it a missing row
/// gives you an object full of zeros rather than nothing — and a cycle, which here is a build
/// error naming the loop rather than a depth limit nobody set.
/// </para>
/// </remarks>
public static class Compose
{
    /// <summary>Fills this member from the map declared for the pair.</summary>
    /// <remarks>
    /// A collection type as <typeparamref name="TDestination"/> maps the elements: the receiver
    /// is the sequence, and the map for its element type fills each one.
    /// </remarks>
    /// <typeparam name="TDestination">The shape to produce, or a collection of it.</typeparam>
    /// <param name="source">
    /// What the map reads. NULLABLE on purpose: composing a navigation that may be absent is
    /// the ordinary case, and the generator writes the null check for it.
    /// </param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public static TDestination To<TDestination>(this object? source)
        => throw new NotSupportedException(
            $"{nameof(Compose)}.{nameof(To)} is read by the generator and never called. Reaching " +
            "this means the expression was compiled rather than written out — which happens when " +
            "it is used outside a map's Configure.");
}
