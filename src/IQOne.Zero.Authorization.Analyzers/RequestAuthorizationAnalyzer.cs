using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace IQOne.Zero.Authorization.Analyzers;

/// <summary>
/// Reports requests whose authorization was never decided, contradicted, or written where
/// nothing reads it.
/// </summary>
/// <remarks>
/// The package refuses an undeclared request at run time, which is the safe answer but a
/// poor place to learn it: the refusal names a request, not a file, and it arrives from a
/// caller rather than a build. Reporting it here turns the same rule into a one-attribute
/// fix at the moment the request is written.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RequestAuthorizationAnalyzer : DiagnosticAnalyzer
{
    private const string AuthorizeAttributeType = "IQOne.Zero.Authorization.AuthorizeAttribute";
    private const string AllowAnonymousAttributeType = "IQOne.Zero.Authorization.AllowAnonymousAttribute";
    private const string DeclarationType = "IQOne.Zero.Authorization.IAuthorizationDeclaration";
    private const string DeclaresType = "IQOne.Zero.Authorization.DeclaresAuthorizationAttribute";
    private const string RequestType = "IQOne.Zero.Messaging.IRequest`1";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [Diagnostics.Undeclared, Diagnostics.Contradictory, Diagnostics.NotARequest];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(static start =>
        {
            var authorize = start.Compilation.GetTypeByMetadataName(AuthorizeAttributeType);
            var anonymous = start.Compilation.GetTypeByMetadataName(AllowAnonymousAttributeType);

            // Nothing to do in a compilation that does not use the package. Note that this is
            // also what keeps the analyzer from reporting on projects that never installed it.
            if (authorize is null || anonymous is null) return;

            var request = start.Compilation.GetTypeByMetadataName(RequestType);
            var declaration = start.Compilation.GetTypeByMetadataName(DeclarationType);
            var declares = start.Compilation.GetTypeByMetadataName(DeclaresType);

            var known = new KnownTypes(authorize, anonymous, request, declaration, declares);

            start.RegisterSymbolAction(c => Analyze(c, known), SymbolKind.NamedType);
        });
    }

    private static void Analyze(SymbolAnalysisContext context, KnownTypes known)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        // Only what someone could actually send. An abstract base or an interface carrying the
        // attribute is a shared declaration, and the concrete request is where the rule applies.
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct)) return;
        if (type.IsAbstract || type.IsImplicitlyDeclared) return;

        var declarations = type.GetAttributes();

        // Any attribute implementing IAuthorizationDeclaration counts, not one fixed type.
        // A route attribute is one, so a policy named on a route satisfies this rule -- which
        // is the point: two declarations of one fact are two that can disagree.
        var isAuthorized = declarations.Any(a => known.Is(a, known.Authorize) || known.Declares(a));
        var isAnonymous = declarations.Any(a => known.Is(a, known.Anonymous) || known.DeclaresAnonymous(a));
        var isRequest = known.IsRequest(type);

        if (isAuthorized && isAnonymous)
        {
            Report(context, Diagnostics.Contradictory, type);
            return;
        }

        if ((isAuthorized || isAnonymous) && !isRequest)
        {
            Report(context, Diagnostics.NotARequest, type);
            return;
        }

        if (isRequest && !isAuthorized && !isAnonymous) Report(context, Diagnostics.Undeclared, type);
    }

    private static void Report(SymbolAnalysisContext context, DiagnosticDescriptor descriptor, INamedTypeSymbol type)
    {
        // The first source location only: a partial declaration is one type, and reporting it
        // once per part would be three copies of the same fix.
        var location = type.Locations.FirstOrDefault(l => l.IsInSource);

        if (location is null) return;

        context.ReportDiagnostic(Diagnostic.Create(descriptor, location, type.Name));
    }

    private readonly struct KnownTypes(
        INamedTypeSymbol authorize,
        INamedTypeSymbol anonymous,
        INamedTypeSymbol? request,
        INamedTypeSymbol? declaration,
        INamedTypeSymbol? declares)
    {
        // Copied out of the primary constructor: a struct's lambda cannot capture one.
        private readonly INamedTypeSymbol? _declaration = declaration;
        private readonly INamedTypeSymbol? _declares = declares;

        /// <summary>Whether the attribute states a policy or a role set of its own.</summary>
        /// <remarks>
        /// Either written at the request, or promised by the attribute type with
        /// <c>[DeclaresAuthorization]</c> — which is how an attribute that DERIVES the policy
        /// says so, since a computed value is not an argument the analyzer can read.
        /// </remarks>
        public bool Declares(AttributeData attribute)
            => Implements(attribute)
            && (Named(attribute, "Policy") is string { Length: > 0 }
                || Named(attribute, "Roles") is string { Length: > 0 }
                || Promises(attribute));

        /// <summary>Whether the attribute's own type promises to decide authorization.</summary>
        private bool Promises(AttributeData attribute)
        {
            var wanted = _declares;

            return wanted is not null
                && attribute.AttributeClass is { } applied
                && applied.GetAttributes().Any(a =>
                    SymbolEqualityComparer.Default.Equals(a.AttributeClass, wanted));
        }

        /// <summary>Whether the attribute states that anyone may make the request.</summary>
        public bool DeclaresAnonymous(AttributeData attribute)
            => Implements(attribute) && Named(attribute, "AllowAnonymous") is true;

        private bool Implements(AttributeData attribute)
        {
            var wanted = _declaration;

            return wanted is not null
                && attribute.AttributeClass is { } applied
                && applied.AllInterfaces.Any(i =>
                    SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, wanted));
        }

        private static object? Named(AttributeData attribute, string name)
            => attribute.NamedArguments
                .FirstOrDefault(argument => argument.Key == name)
                .Value.Value;

        private readonly INamedTypeSymbol? _request = request;

        public INamedTypeSymbol Authorize { get; } = authorize;

        public INamedTypeSymbol Anonymous { get; } = anonymous;

        public bool Is(AttributeData attribute, INamedTypeSymbol wanted)
            => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, wanted);

        public bool IsRequest(INamedTypeSymbol type)
        {
            var request = _request;

            return request is not null
                && type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, request));
        }
    }
}
