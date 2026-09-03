namespace IQOne.Zero.Persistence;

/// <summary>
/// Generates a method that writes one object's members onto another.
/// </summary>
/// <remarks>
/// <para>
/// The write-direction counterpart of <see cref="ProjectionAttribute"/>, and NOT its
/// mirror. A projection produces a new object, so the shape it produces is what must be
/// complete. A mapping writes onto an existing one, and there the object that must be
/// accounted for is the SOURCE: a member the caller sent and nothing consumed is a field
/// that was silently discarded. That is the failure this catches, and it is a real one —
/// a request carrying a state field that the mapping happens not to write looks like it
/// works.
/// </para>
/// <para>
/// Declare it as a partial method whose two parameters name the types:
/// <code>
/// public sealed partial class SaveBedsHandler
/// {
///     [Mapping]
///     private static partial void Apply(BedModel model, Bed bed);
/// }
/// </code>
/// Source first, target second, returning nothing. The types are not arguments for the
/// same reason as on a projection: the signature already names them.
/// </para>
/// <para>
/// The target's KEY is never written. A key is how the row was found; assigning it from a
/// caller's object is at best a no-op and at worst a different row. Recognised through
/// <see cref="IEntity{TKey}"/>, which is the framework's own contract for what a key is —
/// not by a name.
/// </para>
/// <para>
/// ALL OR NOTHING, as with a projection: a source member that cannot be written is a build
/// error naming it, and nothing is generated. The escape hatches are <see cref="Ignore"/>
/// for a member deliberately not written, and writing the method by hand. A member that
/// needs a decision is often best ignored and then assigned by the caller — the generated
/// call and the one exception read well together.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MappingAttribute : Attribute
{
    /// <summary>
    /// Members of the source that are deliberately not written.
    /// </summary>
    /// <remarks>
    /// Say which and the reader knows it was a choice. A field the target has no column
    /// for, a field another step owns, a field the caller may send but must not change —
    /// each is a legitimate omission and each is one somebody has to remember. The list is
    /// checked: a name that is not a member of the source is reported, because a stale
    /// entry accounts for nothing while reading as though it did.
    /// </remarks>
    public string[] Ignore { get; set; } = [];
}
