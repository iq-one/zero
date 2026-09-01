namespace IQOne.Zero.DependencyInjection.Annotations;

/// <summary>Selects which service types a class is registered under.</summary>
[Flags]
public enum ServiceSelectorType
{
    /// <summary>No service type.</summary>
    None = 0,

    /// <summary>The concrete class itself.</summary>
    Self = 1,

    /// <summary>The interface matching the class name, so <c>FooRepository</c> to <c>IFooRepository</c>.</summary>
    DefaultInterface = 2,

    /// <summary>Interfaces the class declares directly.</summary>
    Interfaces = DefaultInterface << 1,

    /// <summary>Every interface in the class's hierarchy, inherited ones included.</summary>
    AllInterfaces = Interfaces << 1,

    /// <summary>Every selector combined.</summary>
    All = Self | DefaultInterface | Interfaces | AllInterfaces
}

/// <summary>
/// Overrides which service types the annotated class is registered under.
/// </summary>
/// <remarks>
/// Without it a class is registered as itself and as its matching interface, so
/// <c>FooRepository</c> resolves through <c>IFooRepository</c>. Apply it when a class must
/// serve additional contracts, or when the naming convention does not hold.
/// </remarks>
/// <param name="types">Explicit service types to register in addition to the selected ones.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class ServiceTypesAttribute(params Type[] types) : Attribute
{
    /// <summary>Registers the class as a keyed service.</summary>
    /// <param name="key">The service key.</param>
    /// <param name="types">Explicit service types to register.</param>
    public ServiceTypesAttribute(object key, params Type[] types) : this(types) => Key = key;

    /// <summary>Service types stated explicitly on the attribute.</summary>
    public Type[] Types { get; } = types;

    /// <summary>Service key, when the class is registered as a keyed service.</summary>
    public object? Key { get; set; }

    /// <summary>Which service types are derived from the class itself.</summary>
    public ServiceSelectorType ServiceSelectorType { get; set; } =
        ServiceSelectorType.Self | ServiceSelectorType.DefaultInterface;
}

/// <summary>Registers the class under <typeparamref name="T"/> as well as any stated types.</summary>
/// <typeparam name="T">An additional service type.</typeparam>
/// <param name="types">Explicit service types to register.</param>
public sealed class ServiceTypesAttribute<T>(params Type[] types)
    : ServiceTypesAttribute([.. types, typeof(T)]);
