using IQOne.Zero.Persistence.Conventions;
using IQOne.Zero.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Zero.Sample.Orders.Data;

/// <summary>
/// The context every module stores through.
/// </summary>
/// <remarks>
/// <para>
/// It names no entity. Each module contributes an <see cref="IModelConvention{ModelBuilder}"/>
/// that maps its own, so adding a module does not mean editing this file — which is what
/// "modular" has to mean if it is to mean anything. A context with a <c>DbSet</c> per module
/// is a context every module has to agree about.
/// </para>
/// <para>
/// A repository reaches its entity through <c>Set&lt;T&gt;()</c>, so nothing needs the
/// properties either.
/// </para>
/// </remarks>
/// <param name="options">How the context connects and behaves.</param>
/// <param name="modelConventions">The mappings and rules the modules contributed.</param>
/// <param name="filterConventions">Named filters applied to the entities they claim.</param>
public sealed class OrdersDbContext(
    DbContextOptions<OrdersDbContext> options,
    IEnumerable<IModelConvention<ModelBuilder>> modelConventions,
    IEnumerable<IEntityFilterConvention> filterConventions)
    : ConventionDbContext(options, modelConventions, filterConventions);
