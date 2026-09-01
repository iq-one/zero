namespace IQOne.Zero.Messaging.Dispatch;

/// <summary>Collects dispatch entries while modules are being configured.</summary>
public interface IServiceRegistryBuilder
{
    /// <summary>Adds one entry to the table.</summary>
    /// <param name="entry">The entry to add.</param>
    /// <exception cref="InvalidOperationException">The route is already registered.</exception>
    void Add(ServiceEntry entry);
}
