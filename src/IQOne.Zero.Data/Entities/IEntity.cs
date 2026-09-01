namespace IQOne.Zero.Data.Entities;

public interface IEntity;

public interface IEntity<TKey> : IEntity
{
    TKey Id { get; set; }
}

/// <summary>
/// Marks rows scoped to a tenant. A null tenant means the row is shared across tenants.
/// </summary>
public interface ITenantScoped
{
    int? TenantId { get; set; }
}
