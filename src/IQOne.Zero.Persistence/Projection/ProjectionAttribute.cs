namespace IQOne.Zero.Persistence;

/// <summary>
/// Generates this specification's <c>Selector</c> from the two types it already names.
/// </summary>
/// <remarks>
/// <para>
/// A specification that reshapes rows has to say how, and for the common case the answer
/// is "member for member, by name". Writing that out is not free: a member the model has
/// and the entity does not is a silent <see langword="null"/> in the response, and nobody
/// notices until a screen is missing a column. The generator turns that into a build
/// error naming the member.
/// </para>
/// <para>
/// The types are not arguments here because the class already declares them:
/// <code>
/// [Projection]
/// public sealed partial class InvoiceQuery : Specification&lt;Invoice, InvoiceModel&gt;
/// {
///     public InvoiceQuery(int customer) => Where(e => e.CustomerId == customer);
/// }
/// </code>
/// Restating them in the attribute would let the two disagree.
/// </para>
/// <para>
/// ALL OR NOTHING, deliberately. If any member cannot be mapped the generator emits
/// nothing and reports ZERO220 naming it, rather than filling what it can: a projection
/// that is three quarters generated and one quarter silently absent is the failure this
/// exists to prevent. The escape hatches are <see cref="Ignore"/> for a member with no
/// source, and writing the <c>Selector</c> by hand for a projection that has earned it.
/// </para>
/// <para>
/// The result is an expression tree, so it is translated by the provider and only the
/// model's columns are read. That is the difference from mapping after materialisation:
/// there the whole row — and every navigation loaded with it — is fetched and most of it
/// discarded.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ProjectionAttribute : Attribute
{
    /// <summary>
    /// Members of the result that have no source on the entity, and are filled elsewhere.
    /// </summary>
    /// <remarks>
    /// Say what they are, not merely that they exist. A price read from a pricing function
    /// after the query, a tree linked up in memory, a field a sibling endpoint fills — each
    /// is a legitimate hole, and each is a member somebody has to remember is empty. Listing
    /// it here is that record, and it is checked: a name that is not a member of the result
    /// is reported as ZERO221.
    /// </remarks>
    public string[] Ignore { get; set; } = [];
}
