using IQOne.Zero.Generators.Internal;

namespace IQOne.Zero.Generators.Registration;

/// <summary>
/// One attribute argument, in the two forms emission needs.
/// </summary>
/// <remarks>
/// Positional and named arguments were once flattened into a single <c>Name=Value</c> string
/// list, which made three unrelated things impossible to tell apart. A route pattern
/// containing an '=' — <c>[Get("/reports/{page=1}")]</c>, a legal ASP.NET default — read as a
/// named argument, so the pattern came back empty. <c>[ServiceTypes("primary", typeof(X))]</c>
/// lost its key. And a lookup for <c>Name</c> matched anything merely starting with it.
/// </remarks>
/// <param name="Value">
/// The value as text: the fully qualified type name for a <c>typeof</c>, the underlying
/// number for an enum, otherwise the constant's invariant string form.
/// </param>
/// <param name="Expression">The C# expression that reproduces the value at the emission site.</param>
/// <param name="IsType">Whether the argument was written as <c>typeof(...)</c>.</param>
internal sealed record AttributeArgument(string? Value, string Expression, bool IsType);

/// <summary>One named argument of an attribute.</summary>
/// <param name="Name">The property or field the argument sets.</param>
/// <param name="Argument">Its value.</param>
internal sealed record NamedAttributeArgument(string Name, AttributeArgument Argument);

/// <summary>One attribute applied to a candidate, flattened to strings so the value stays equatable.</summary>
/// <param name="TypeName">The attribute's open definition, for example <c>Ns.ServiceTypesAttribute&lt;T&gt;</c>.</param>
/// <param name="ConstructorArguments">Positional arguments, with array arguments flattened in place.</param>
/// <param name="NamedArguments">Arguments that set a property or field.</param>
/// <param name="TypeArguments">
/// The attribute's own type arguments. A generic attribute passes them in a base-constructor
/// call, where they never reach <c>ConstructorArguments</c>.
/// </param>
/// <param name="BaseTypeNames">
/// The attribute's base chain, nearest first. Lets a generator recognise an attribute by
/// what it derives from rather than only by its own name — an application may declare its
/// own route attribute, and matching names alone would leave it unseen.
/// </param>
internal sealed record AttributeUsage(
    string TypeName,
    EquatableArray<AttributeArgument> ConstructorArguments,
    EquatableArray<NamedAttributeArgument> NamedArguments,
    EquatableArray<string> TypeArguments,
    EquatableArray<string> BaseTypeNames);

/// <summary>
/// One interface a candidate implements, in every form registration needs.
/// </summary>
/// <remarks>
/// The open definition alone is not enough for messaging: dispatching needs to know that a
/// handler implements <c>IRequestHandler&lt;CreateInvoice, int&gt;</c>, not merely that it
/// implements <c>IRequestHandler&lt;,&gt;</c>. Neither is the closed form alone: an open
/// generic behaviour is registered as <c>typeof(IPipelineBehavior&lt;,&gt;)</c>, which no
/// closed name can express.
/// </remarks>
/// <param name="OpenGenericName">Definition without arity or type arguments, for example <c>Ns.IValidator</c>.</param>
/// <param name="TypeArguments">
/// The type arguments as implemented, KEEPING any nullable reference annotation.
/// </param>
/// <param name="ErasedTypeArguments">
/// The same arguments with nullable reference annotations removed, for <c>typeof</c>.
/// </param>
/// <param name="ClosedName">Fully qualified, as implemented: <c>global::Ns.IValidator&lt;global::App.Invoice&gt;</c>.</param>
/// <param name="UnboundName">Fully qualified and unbound: <c>global::Ns.IValidator&lt;&gt;</c>.</param>
/// <param name="ForwardsTypeParameters">
/// Whether the type arguments are exactly the implementing type's own type parameters, in
/// order. Only such an interface can be named unbound alongside its implementation.
/// </param>
/// <remarks>
/// The two renderings of the type arguments are BOTH needed, and neither can be derived
/// from the other by string surgery: a nullable VALUE type prints as <c>int?</c> in both,
/// so stripping every <c>?</c> would turn <c>IQuery&lt;int?&gt;</c> into
/// <c>IQuery&lt;int&gt;</c>. A generic argument list needs the annotated form or the
/// emitted closed interface is not the one the class implements (CS8631); a
/// <c>typeof</c> needs the erased form because <c>typeof(T?)</c> is not legal C#
/// (CS8639). Getting either wrong produces a compiler error inside generated code.
/// </remarks>
internal sealed record InterfaceUsage(
    string OpenGenericName,
    EquatableArray<string> TypeArguments,
    EquatableArray<string> ErasedTypeArguments,
    string ClosedName,
    string UnboundName,
    bool ForwardsTypeParameters);

/// <summary>
/// A constructor parameter's type, in both the form it was written and its unbound definition.
/// </summary>
/// <remarks>
/// Captive-dependency detection matches a dependency against the service types that were
/// registered. Recording only the definition missed every closed generic — a singleton taking
/// <c>IValidator&lt;Foo&gt;</c> was never matched against the <c>IValidator&lt;Foo&gt;</c>
/// registration — and recording only the closed form would miss an open generic registration.
/// </remarks>
/// <param name="TypeName">Fully qualified, as written.</param>
/// <param name="UnboundTypeName">Fully qualified and unbound, or null when the type is not generic.</param>
internal sealed record DependencyReference(string TypeName, string? UnboundTypeName);

/// <summary>
/// One marker interface reached through a directly-implemented interface.
/// </summary>
/// <remarks>
/// Raw, like everything else the collector produces: which interface carries which marker,
/// with no opinion about what the marker means. Emission decides that. It exists so a
/// diagnostic can name the abstraction a lifetime came from rather than saying "several".
/// </remarks>
internal sealed record InheritedMarker(string Interface, string Marker);

/// <summary>Raw registration facts; lifetime interfaces are matched during emission.</summary>
/// <param name="ImplementationTypeName">Fully qualified, as declared.</param>
/// <param name="UnboundImplementationTypeName">
/// Fully qualified and unbound, for an open generic. Equal to <paramref name="ImplementationTypeName"/> otherwise.
/// </param>
/// <param name="IsAbstract">Whether the container could never construct it.</param>
/// <param name="Arity">Number of type parameters; zero for a non-generic type.</param>
/// <param name="AllInterfaces">Open names of every interface in the hierarchy, inherited ones included.</param>
/// <param name="DeclaredInterfaces">
/// Open names reachable from this type's own base list. What is here and not in
/// <paramref name="AllInterfaces"/>'s remainder is what the author actually wrote.
/// </param>
/// <param name="DirectInterfaces">Interfaces named in this type's own base list.</param>
/// <param name="InheritedInterfaces">Interfaces a base class names in its base list.</param>
/// <param name="TypeName">The simple name, used by the naming convention and in diagnostics.</param>
/// <param name="Attributes">Attributes applied directly to this type.</param>
/// <param name="ConstructorDependencies">Parameter types of the widest public constructor.</param>
/// <param name="ClosedInterfaces">Generic interfaces with their type arguments kept.</param>
/// <param name="InheritedMarkers">
/// Which of this type's own interfaces carries which marker. Flattening loses that, and it
/// is the difference between "you wrote two lifetime markers" and "your abstraction already
/// declared one" — two mistakes with different fixes.
/// </param>
/// <param name="Location">Where to point a diagnostic.</param>
internal sealed record ServiceCandidate(
    string ImplementationTypeName,
    string UnboundImplementationTypeName,
    bool IsAbstract,
    int Arity,
    EquatableArray<string> AllInterfaces,
    EquatableArray<string> DeclaredInterfaces,
    EquatableArray<InterfaceUsage> DirectInterfaces,
    EquatableArray<InterfaceUsage> InheritedInterfaces,
    string TypeName,
    EquatableArray<AttributeUsage> Attributes,
    EquatableArray<DependencyReference> ConstructorDependencies,
    EquatableArray<InterfaceUsage> ClosedInterfaces,
    EquatableArray<InheritedMarker> InheritedMarkers,
    LocationInfo? Location)
{
    /// <summary>Whether the container can construct it and dispatch can name it.</summary>
    public bool IsConcrete => !IsAbstract && Arity == 0;
}

/// <summary>A request and the handler that serves it, ready for emission.</summary>
/// <remarks>
/// The response type appears twice, and the difference matters:
/// <c>ResponseTypeName</c> keeps a nullable annotation and goes in generic argument
/// lists, while <c>ErasedResponseTypeName</c> drops it and goes in <c>typeof</c>. See
/// <see cref="InterfaceUsage"/> for why neither can be derived from the other.
/// </remarks>
internal sealed record RequestDescriptor(
    string RequestTypeName,
    string ResponseTypeName,
    string ErasedResponseTypeName,
    string HandlerTypeName,
    LocationInfo? Location);

/// <summary>An HTTP endpoint and the request behind it, ready for emission.</summary>
/// <remarks>
/// As on <see cref="RequestDescriptor"/>: the annotated response type goes in generic
/// argument lists, the erased one in <c>typeof</c>.
/// </remarks>
internal sealed record EndpointDescriptor(
    string Method,
    string Pattern,
    string Name,
    string? Tag,
    string? Policy,
    bool AllowAnonymous,
    string RequestTypeName,
    string ResponseTypeName,
    string ErasedResponseTypeName,
    LocationInfo? Location);

/// <summary>One type's registration, ready for emission.</summary>
/// <param name="ImplementationTypeName">
/// What goes on the right of the registration. Unbound for an open generic, so that
/// <c>typeof(...)</c> names it.
/// </param>
/// <param name="ServiceTypeNames">What the type is resolvable as.</param>
/// <param name="Lifetime">The container lifetime, as the method-name suffix.</param>
/// <param name="Key">The service key as a C# expression, or null when the registration is not keyed.</param>
/// <param name="RegisterSelf">Whether the concrete type is registered as itself as well.</param>
/// <param name="IsOpenGeneric">Whether the registration has to use the <c>Type</c> overloads.</param>
/// <param name="ConstructorDependencies">What the type asks for, for captive-dependency detection.</param>
/// <param name="Location">Where to point a diagnostic.</param>
internal sealed record ServiceRegistrationDescriptor(
    string ImplementationTypeName,
    EquatableArray<string> ServiceTypeNames,
    string Lifetime,
    string? Key,
    bool RegisterSelf,
    bool IsOpenGeneric,
    EquatableArray<DependencyReference> ConstructorDependencies,
    LocationInfo? Location);
