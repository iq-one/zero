namespace IQOne.Zero.Persistence;

/// <summary>
/// Generates a method that writes one object's members onto another.
/// </summary>
/// <remarks>
/// <para>
/// Member for member by name, in memory, between any two types. Declared as a partial
/// method whose signature names them — the types are not arguments for the same reason as
/// on a projection: the signature already says.
/// </para>
/// <para>
/// TWO SHAPES, and the shape decides everything else:
/// <code>
/// // writes onto something that already exists
/// [Mapping]
/// private static partial void Apply(BedModel model, Bed bed);
///
/// // produces a new object
/// [Mapping]
/// private static partial BedModel ToModel(Bed bed);
/// </code>
/// The second covers the directions the first cannot: an entity to a model outside a
/// query, a model to another model, anything to anything.
/// </para>
/// <para>
/// WHICH END IS HELD TO ACCOUNT follows from the shape, and one sentence covers both:
/// what you construct must be complete, what you consume must be consumed.
/// <list type="bullet">
///   <item>
///     Producing — the RESULT is checked, as on a projection. A member nobody fills is an
///     absent field with nothing in the code to explain it. The source may be far wider;
///     entities usually are.
///   </item>
///   <item>
///     Writing onto — the SOURCE is checked. A member the caller sent and nothing consumed
///     is a field discarded without a word, on a request that looks like it worked. The
///     target may be wider: its key, its audit columns, whatever a convention fills.
///   </item>
/// </list>
/// </para>
/// <para>
/// This is not <see cref="ProjectionAttribute"/> with more shapes. A projection produces an
/// EXPRESSION TREE, which the provider translates so that only the result's columns are
/// read; a mapping is plain code over objects already in memory. Same rules, different place
/// they run — and a projection is the one to reach for when the source is a query.
/// </para>
/// <para>
/// The KEY is written when producing and skipped when writing onto. Producing, it is part
/// of what the caller receives; writing onto, it is how the row was found, and assigning it
/// from the caller's object is a no-op at best and a different row at worst. Recognised
/// through <see cref="IEntity{TKey}"/> — the framework's own contract for what a key is,
/// not a name.
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
    /// Members deliberately left out — of the source when writing onto, of the result when
    /// producing.
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
