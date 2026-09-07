using System.Linq.Expressions;

namespace IQOne.Zero.Mapping;

/// <summary>
/// How one shape becomes another.
/// </summary>
/// <remarks>
/// <para>
/// A map is an EXPRESSION TREE, and that is the whole design. A tree can be handed to a query
/// provider, which turns it into the columns a <c>SELECT</c> reads; it can also be compiled and
/// run against an object already in memory. So one declaration serves both, and the two cannot
/// drift apart — which is what happens when a read path and a write path each fill the same
/// model in their own way.
/// </para>
/// <para>
/// The tree is WRITTEN AS SOURCE at build time, not assembled at startup. The generated file
/// sits beside this one: what a member gets is something you can open and read, and go to. That
/// is also where the accounting happens — a member of <typeparamref name="TDestination"/> that
/// nothing fills is a build error naming it, never an absent field discovered in production.
/// </para>
/// <para>
/// Declare a subclass only when there is something to SAY. Where every member matches by name
/// the generator writes the map from the call that asked for it and no file is needed at all;
/// this type is for the members that need a decision:
/// <code>
/// public sealed class BedMap : Map&lt;Bed, BedModel&gt;
/// {
///     protected override void Configure(IMapBuilder&lt;Bed, BedModel&gt; map)
///     {
///         map.Member(m =&gt; m.BedState, e =&gt; (EnumBedState)e.State);
///         map.Ignore(m =&gt; m.BuildingUnit, m =&gt; m.Department);
///     }
/// }
/// </code>
/// </para>
/// <para>
/// <see cref="Configure"/> is READ, not run — the generator takes what it declares and writes
/// the tree from it. It is ordinary C# so that the compiler checks every lambda in it, and the
/// generated file shows exactly what each one became.
/// </para>
/// </remarks>
/// <typeparam name="TSource">The shape read from.</typeparam>
/// <typeparam name="TDestination">The shape produced.</typeparam>
public abstract class Map<TSource, TDestination>
{
    /// <summary>What the generator could not work out by name.</summary>
    /// <param name="map">Where to say it.</param>
    protected virtual void Configure(IMapBuilder<TSource, TDestination> map)
    {
    }

    /// <summary>
    /// The map itself, as a tree a query provider can translate.
    /// </summary>
    /// <remarks>
    /// Implemented by the generated half of this type. Nothing is built when it is read: the
    /// tree is a field initialised once, from source written at build time.
    /// </remarks>
    public abstract Expression<Func<TSource, TDestination>> Selector { get; }

    /// <summary>
    /// The same map, for an object already in memory.
    /// </summary>
    /// <remarks>
    /// The compiled form of <see cref="Selector"/>, compiled once. Compiling a tree costs
    /// around seventy microseconds, so it is paid per process and never per call; a mapping
    /// through this is then indistinguishable from hand-written assignment.
    /// </remarks>
    public abstract Func<TSource, TDestination> Project { get; }
}
