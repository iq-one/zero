using System.Linq.Expressions;

namespace IQOne.Zero.Mapping;

/// <summary>
/// Where a map says what the generator could not work out by name.
/// </summary>
/// <remarks>
/// <para>
/// Every call here is READ FROM SOURCE, so each takes ordinary lambdas: the compiler checks
/// them where you wrote them, and the generator copies what you wrote into the tree it writes.
/// A body it cannot read statically — a loop, a condition, a call it does not know — is
/// reported rather than half-understood.
/// </para>
/// <para>
/// Say as little as possible. A member that matches by name needs nothing, and the two calls
/// here are for the two things that are actually decisions: a member that comes from somewhere
/// else, and a member deliberately left empty.
/// </para>
/// </remarks>
/// <typeparam name="TSource">The shape read from.</typeparam>
/// <typeparam name="TDestination">The shape produced.</typeparam>
public interface IMapBuilder<TSource, TDestination>
{
    /// <summary>Where one member comes from.</summary>
    /// <remarks>
    /// The expression is copied into the tree, so it must be one a query provider can
    /// translate if this map is ever used on a query — a cast, a coalesce, a member of a
    /// member, all fine; a call to a method of your own, not.
    /// </remarks>
    /// <typeparam name="TMember">The member's type.</typeparam>
    /// <param name="member">The member being filled.</param>
    /// <param name="source">What fills it.</param>
    /// <returns>This builder.</returns>
    IMapBuilder<TSource, TDestination> Member<TMember>(
        Expression<Func<TDestination, TMember>> member,
        Expression<Func<TSource, TMember>> source);

    /// <summary>Members deliberately left empty.</summary>
    /// <remarks>
    /// A navigation nobody loaded, a field another step owns. Saying it is the point: the
    /// member is still accounted for, and the next reader knows it was a choice rather than
    /// an oversight.
    /// </remarks>
    /// <param name="members">The members.</param>
    /// <returns>This builder.</returns>
    IMapBuilder<TSource, TDestination> Ignore(params Expression<Func<TDestination, object?>>[] members);
}
