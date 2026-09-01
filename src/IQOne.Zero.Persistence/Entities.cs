namespace IQOne.Zero.Persistence;

/// <summary>Marks a type as something the application stores.</summary>
public interface IEntity;

/// <summary>An entity identified by a single key.</summary>
/// <typeparam name="TKey">The key's type.</typeparam>
public interface IEntity<TKey> : IEntity
{
    /// <summary>The entity's identity.</summary>
    TKey Id { get; }
}

/// <summary>
/// The entity a transaction is drawn around.
/// </summary>
/// <remarks>
/// A repository is offered for aggregate roots only. Anything reachable from a root is
/// loaded and saved with it, which is what keeps a consistency boundary from quietly
/// becoming "whatever the last query happened to touch".
/// </remarks>
public interface IAggregateRoot : IEntity;
