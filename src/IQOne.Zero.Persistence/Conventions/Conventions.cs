using System.Linq.Expressions;

namespace IQOne.Zero.Persistence.Conventions;

/// <summary>
/// Contributes a named filter applied to every entity it applies to.
/// </summary>
/// <remarks>
/// <para>
/// Each filter is registered under its own <see cref="Key"/> so a query can disable one
/// without the rest. This matters: asking to see deleted rows must not silently drop tenant
/// isolation as well, which is what a single unnamed filter does.
/// </para>
/// <para>
/// The framework applies whatever the application registers and defines none itself. What a
/// tenant is, and whether deletion is soft, differ per application.
/// </para>
/// </remarks>
public interface IEntityFilterConvention
{
    /// <summary>Name a specification uses to opt out of this filter.</summary>
    string Key { get; }

    /// <summary>Whether the filter applies to this entity.</summary>
    /// <param name="entityType">The entity being mapped.</param>
    /// <returns><see langword="true"/> when the filter should be applied.</returns>
    bool AppliesTo(Type entityType);

    /// <summary>Builds the filter for an entity.</summary>
    /// <param name="entityType">The entity being mapped.</param>
    /// <param name="context">
    /// The context instance. Read values through it rather than capturing them: a captured
    /// value is baked into the compiled query and reused for every later request.
    /// </param>
    /// <returns>The filter predicate, or <see langword="null"/> to leave the entity unfiltered.</returns>
    LambdaExpression? Build(Type entityType, object context);
}

/// <summary>
/// Applies model-wide mapping rules — a concurrency token, a column present on every table,
/// a naming scheme.
/// </summary>
/// <typeparam name="TModelBuilder">The provider's model builder.</typeparam>
public interface IModelConvention<in TModelBuilder>
{
    /// <summary>Applies the rule to the whole model.</summary>
    /// <param name="modelBuilder">The model being built.</param>
    void Apply(TModelBuilder modelBuilder);
}

/// <summary>
/// Adjusts tracked entities before they are written — audit stamps, turning a delete into a
/// soft delete, assigning a tenant to new rows.
/// </summary>
/// <typeparam name="TContext">The provider's context.</typeparam>
public interface ISaveChangesConvention<in TContext>
{
    /// <summary>Adjusts what is about to be written.</summary>
    /// <param name="context">The context being saved.</param>
    void Apply(TContext context);
}

/// <summary>
/// The tables this deployment may write.
/// </summary>
/// <remarks>
/// When several applications share a database, each table needs exactly one writer. The same
/// is true of a deployment reading through a replica or a synonym: the write succeeds locally
/// and is lost at the next synchronisation — worse than failing, because nothing reports it.
/// Declaring ownership turns that silent loss into an exception at the write.
/// </remarks>
public interface IWriteOwnership
{
    /// <summary>Whether this deployment may write to the table.</summary>
    /// <param name="schema">The table's schema, or null when unqualified.</param>
    /// <param name="table">The table's name.</param>
    /// <returns><see langword="true"/> when the write is permitted.</returns>
    bool CanWrite(string? schema, string table);
}

/// <summary>Raised instead of allowing a write that would be silently discarded.</summary>
/// <param name="table">The table that was written to.</param>
/// <param name="operation">The attempted operation.</param>
public sealed class WriteOwnershipViolationException(string table, string operation)
    : InvalidOperationException(
        $"'{operation}' on '{table}' was refused: this deployment does not own that table. " +
        "Route the operation to the application that owns it, or correct the ownership declaration.")
{
    /// <summary>The table that was written to.</summary>
    public string Table { get; } = table;

    /// <summary>The attempted operation.</summary>
    public string Operation { get; } = operation;
}
