using IQOne.Zero.Persistence;

namespace Zero.Sample.Orders.Data;

/// <summary>
/// What every entity in this application carries.
/// </summary>
/// <remarks>
/// The application's shape, not the framework's. Zero applies the conventions registered
/// against this interface; it has no opinion about whether deletion is soft or what an audit
/// stamp looks like, because those differ per application and a framework that decided them
/// would be wrong for half its users.
/// </remarks>
public interface IAuditedEntity : IEntity
{
    /// <summary>Whether the row has been deleted. Filtered out unless a query opts out.</summary>
    bool IsDeleted { get; set; }

    /// <summary>When the row was first written.</summary>
    DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the row was last changed, or null when it never was.</summary>
    DateTimeOffset? ModifiedAt { get; set; }
}
