using IQOne.Zero.Generators.Tests.Harness;

namespace IQOne.Zero.Generators.Tests;

/// <summary>
/// Which service type a class is registered under. Every case here produced either code that
/// did not compile or a registration nobody could resolve, in a file the developer never
/// wrote — the two failure modes generated registration exists to prevent.
/// </summary>
public class ServiceTypeResolutionTests
{
    [Fact]
    public void A_closed_generic_interface_is_registered_closed()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public sealed class Invoice;

            public interface IRepository<T>;

            public sealed class InvoiceRepository : IRepository<Invoice>, IScoped;
            """);

        run.HasError.Should().BeFalse();

        // The open definition was emitted before, so Module.g.cs said IRepository<T> and the
        // consumer's build failed with CS0246 on a type parameter that exists nowhere.
        run.GeneratedSource.Should()
            .Contain("AddScoped<global::Test.IRepository<global::Test.Invoice>, global::Test.InvoiceRepository>")
            .And.NotContain("IRepository<T>");

        run.GeneratedFileErrorMessages.Should().BeEmpty();
    }

    [Fact]
    public void An_open_generic_behaviour_is_registered_unbound()
    {
        var run = GeneratorHarness.Run("""
            using System.Threading;
            using System.Threading.Tasks;
            using IQOne.Zero;
            using IQOne.Zero.Messaging;

            namespace Test;

            public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
                where TRequest : IRequest<TResponse>
            {
                public Task<Result<TResponse>> HandleAsync(
                    TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
                    => next();
            }
            """);

        // IPipelineBehavior itself is IScoped, so every open generic behaviour -- the shape
        // its own documentation and the messaging manifest both tell you to write -- used to
        // be a hard ZERO008.
        run.DiagnosticIds.Should().NotContain("ZERO008");
        run.HasError.Should().BeFalse();

        run.GeneratedSource.Should().Contain(
            "AddScoped(services, typeof(global::IQOne.Zero.Messaging.IPipelineBehavior<,>), " +
            "typeof(global::Test.LoggingBehavior<,>));");

        run.GeneratedFileErrorMessages.Should().BeEmpty();
    }

    [Fact]
    public void A_keyed_open_generic_uses_the_overload_that_takes_both_types()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Annotations;
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface ICache<T>;

            [ServiceTypes("memory")]
            public sealed class MemoryCache<T> : ICache<T>, ISingleton;
            """);

        run.HasError.Should().BeFalse();

        run.GeneratedSource.Should().Contain(
            "AddKeyedSingleton(services, typeof(global::Test.ICache<>), \"memory\", " +
            "typeof(global::Test.MemoryCache<>));");

        // A generic registration cannot be written generically, so a wrong overload here is
        // only ever found by compiling the file the generator wrote.
        run.GeneratedFileErrorMessages.Should().BeEmpty();
    }

    [Fact]
    public void An_abstract_base_is_skipped_and_its_derived_class_registered()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface IExportFormat : ITransient;

            public abstract class ExportFormat : IExportFormat;

            public sealed class CsvExportFormat : ExportFormat;
            """);

        // This is the shape ZERO008's own rule page gives as the fix, and it used to report
        // ZERO008 on the base and ZERO007 on the derived class.
        run.DiagnosticIds.Should().NotContain("ZERO008").And.NotContain("ZERO007");

        run.GeneratedSource.Should()
            .Contain("AddTransient<global::Test.IExportFormat, global::Test.CsvExportFormat>")
            .And.NotContain(", global::Test.ExportFormat>");
    }

    [Fact]
    public void The_naming_convention_requires_an_exact_match()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface IService;
            public interface IUserService;

            public sealed class UserService : IService, IUserService, IScoped;
            """);

        // 'UserService'.EndsWith("Service") matched IService first, so the class was
        // registered under IService and IUserService was never registered at all.
        run.GeneratedSource.Should()
            .Contain("AddScoped<global::Test.IUserService, global::Test.UserService>")
            .And.NotContain("global::Test.IService,");
    }

    [Fact]
    public void An_interface_deriving_from_IRequiredService_is_still_a_service_type()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Descriptors;
            using IQOne.Zero.DependencyInjection.Services;

            namespace Test;

            public interface IStartupProbe : IRequiredService, IScoped;

            public sealed class StartupProbe : IStartupProbe;
            """);

        // IRequiredService's own family feature was never implemented; the half-written
        // computation for it has been removed, and the interface resolves as any other does.
        run.HasError.Should().BeFalse();
        run.GeneratedSource.Should().Contain("AddScoped<global::Test.IStartupProbe, global::Test.StartupProbe>");
    }

    [Fact]
    public void A_requirement_handler_is_registered_under_the_closed_interface()
    {
        var run = GeneratorHarness.Run("""
            using System.Threading;
            using System.Threading.Tasks;
            using IQOne.Zero.Authorization;

            namespace Test;

            public sealed record OwnsInvoice : IAuthorizationRequirement;

            public sealed class OwnsInvoiceHandler : IRequirementHandler<OwnsInvoice>
            {
                public ValueTask<AuthorizationDecision> CheckAsync(
                    OwnsInvoice requirement, ICurrentUser user, CancellationToken cancellationToken)
                    => new(AuthorizationDecision.Allowed);
            }
            """);

        run.HasError.Should().BeFalse();

        // Without the interface in ClosedRegistrationInterfaces the single-direct-interface
        // fallback emitted IRequirementHandler<TRequirement> -- an open type parameter.
        run.GeneratedSource.Should()
            .Contain("AddScoped<global::IQOne.Zero.Authorization.IRequirementHandler<global::Test.OwnsInvoice>, " +
                     "global::Test.OwnsInvoiceHandler>")
            .And.NotContain("<TRequirement>");

        run.GeneratedFileErrorMessages.Should().BeEmpty();
    }
}

/// <summary>
/// The annotations that override what an abstraction says. Each one is documented as the
/// escape hatch for a case the abstraction cannot express, and each one did nothing.
/// </summary>
public class ServiceAnnotationTests
{
    [Fact]
    public void A_lifetime_attribute_registers_a_type_with_no_marker_interface()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Annotations;

            namespace Test;

            public interface IClock;

            [Scoped]
            public sealed class SystemClock : IClock;
            """);

        // lifetime-by-abstraction.md documents this as the escape hatch. The generator read
        // only marker interfaces, so the type was silently not registered at all.
        run.HasError.Should().BeFalse();
        run.GeneratedSource.Should().Contain("AddScoped<global::Test.IClock, global::Test.SystemClock>");
    }

    [Fact]
    public void A_lifetime_attribute_overrides_the_interface_it_would_otherwise_take()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Annotations;
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface IReportCache;

            [Singleton]
            public sealed class ReportCache : IReportCache, IScoped;
            """);

        run.GeneratedSource.Should()
            .Contain("AddSingleton<global::Test.IReportCache, global::Test.ReportCache>")
            .And.NotContain("AddScoped<global::Test.IReportCache");
    }

    [Fact]
    public void A_type_with_no_base_list_is_found_through_its_annotation_alone()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Annotations;

            namespace Test;

            [Singleton]
            [ServiceTypes(ServiceSelectorType = ServiceSelectorType.Self)]
            public sealed class Metrics;
            """);

        // Candidates were filtered to declarations with a base list, so an annotated type
        // that implements nothing was never even looked at.
        run.HasError.Should().BeFalse();
        run.GeneratedSource.Should().Contain("AddSingleton<global::Test.Metrics>(services);");
    }

    [Fact]
    public void A_service_type_stated_on_a_base_class_reaches_the_class_deriving_from_it()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Annotations;
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface IInitializeStep;
            public interface IShutdownStep;

            [ServiceTypes<IInitializeStep>]
            [ServiceTypes<IShutdownStep>]
            public abstract class Steps : IInitializeStep, IShutdownStep, ISingleton;

            public sealed class MySteps : Steps;
            """);

        // This is ApplicationSteps' shape, and the framework will ship more bases like it.
        // Roslyn returns only what a declaration wrote itself, so the derived class matched
        // two unrelated inherited interfaces, resolved to neither, and got ZERO007.
        run.DiagnosticIds.Should().NotContain("ZERO007");

        run.GeneratedSource.Should()
            .Contain("AddSingleton<global::Test.IInitializeStep, global::Test.MySteps>")
            .And.Contain("AddSingleton<global::Test.IShutdownStep, global::Test.MySteps>");
    }

    [Fact]
    public void An_attribute_on_the_derived_class_replaces_the_one_it_would_inherit()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Annotations;
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface IInitializeStep;
            public interface IShutdownStep;

            [ServiceTypes<IInitializeStep>]
            [ServiceTypes<IShutdownStep>]
            public abstract class Steps : IInitializeStep, IShutdownStep, ISingleton;

            [ServiceTypes(typeof(IShutdownStep))]
            public sealed class ShutdownOnly : Steps;
            """);

        // These annotations are overrides. Naming one's own service types means those, not
        // those and also whichever the base happened to state.
        run.GeneratedSource.Should()
            .Contain("AddSingleton<global::Test.IShutdownStep, global::Test.ShutdownOnly>")
            .And.NotContain("global::Test.IInitializeStep, global::Test.ShutdownOnly");
    }

    [Fact]
    public void A_lifetime_attribute_on_a_base_class_reaches_the_class_deriving_from_it()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Annotations;

            namespace Test;

            public interface IProbe;

            [Singleton]
            public abstract class Probe : IProbe;

            public sealed class LivenessProbe : Probe;
            """);

        run.HasError.Should().BeFalse();
        run.GeneratedSource.Should().Contain("AddSingleton<global::Test.IProbe, global::Test.LivenessProbe>");
    }

    [Fact]
    public void The_generic_ServiceTypes_attribute_states_a_service_type()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Annotations;
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface IThingRepository;
            public interface IReportSource;

            [ServiceTypes<IReportSource>]
            public sealed class ThingRepository : IThingRepository, IReportSource, IScoped;
            """);

        // The generic attribute passes typeof(T) in a base-constructor call, so it never
        // reached ConstructorArguments and the attribute registered nothing.
        run.GeneratedSource.Should()
            .Contain("global::Test.IReportSource, global::Test.ThingRepository")
            .And.NotContain("global::Test.IThingRepository, global::Test.ThingRepository");
    }

    [Fact]
    public void Undefined_declines_to_choose_a_lifetime_rather_than_picking_one()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Annotations;

            namespace Test;

            public interface IDraft;

            [Undefined]
            public sealed class Draft : IDraft;
            """);

        // "No lifetime has been chosen yet" is not a lifetime. Mapping it onto the container's
        // fallback would put a service in the container on the strength of an attribute that
        // said the opposite.
        run.HasError.Should().BeFalse();
        run.GeneratedSource.Should()
            .Contain("No type in this assembly carries a lifetime marker.")
            .And.NotContain("global::Test.Draft");
    }

    [Fact]
    public void A_positional_key_produces_a_keyed_registration()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Annotations;
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface IEmailSender;

            [ServiceTypes("primary", typeof(IEmailSender))]
            public sealed class SmtpSender : IEmailSender, IScoped;

            [ServiceTypes("backup", typeof(IEmailSender))]
            public sealed class SesSender : IEmailSender, IScoped;
            """);

        // This is the fix ZERO010's message and rule page both give. It produced two
        // non-keyed registrations, and ZERO010 went on firing.
        run.DiagnosticIds.Should().NotContain("ZERO010");

        run.GeneratedSource.Should()
            .Contain("AddKeyedScoped<global::Test.IEmailSender, global::Test.SmtpSender>(services, \"primary\")")
            .And.Contain("AddKeyedScoped<global::Test.IEmailSender, global::Test.SesSender>(services, \"backup\")");
    }

    [Fact]
    public void A_key_keeps_the_type_it_was_written_with()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Annotations;
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface IShard;

            [ServiceTypes(typeof(IShard), Key = 1)]
            public sealed class FirstShard : IShard, IScoped;
            """);

        // Emitted as "1" before, which no int-keyed resolution ever matches.
        run.GeneratedSource.Should()
            .Contain("AddKeyedScoped<global::Test.IShard, global::Test.FirstShard>(services, 1)")
            .And.NotContain("services, \"1\"");

        run.GeneratedFileErrorMessages.Should().BeEmpty();
    }
}

/// <summary>
/// What the generator reports, and what it declines to report. A diagnostic that fires on
/// correct code costs more than one that never fires: it teaches the reader to ignore it.
/// </summary>
public class RegistrationDiagnosticTests
{
    [Fact]
    public void ZERO010_is_reported_when_two_implementations_claim_one_service_type()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Annotations;
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface IExportFormat : ITransient;

            [ServiceTypes(typeof(IExportFormat))]
            public sealed class CsvExportFormat : IExportFormat;

            [ServiceTypes(typeof(IExportFormat))]
            public sealed class ExcelExportFormat : IExportFormat;
            """);

        run.DiagnosticIds.Should().Contain("ZERO010");
    }

    [Fact]
    public void ZERO010_is_not_reported_for_several_validators_of_one_request()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.Messaging;
            using IQOne.Zero.Validation;

            namespace Test;

            public sealed record Register(string Email) : ICommand;

            public sealed class EmailValidator : Validator<Register>
            {
                protected override void Configure(RuleSet<Register> rules)
                    => rules.NotEmpty(x => x.Email, "register.email");
            }

            public sealed class LengthValidator : Validator<Register>
            {
                protected override void Configure(RuleSet<Register> rules)
                    => rules.Length(x => x.Email, "register.email", 3, 256);
            }
            """);

        // ValidationBehavior resolves IEnumerable<IValidator<T>> and its own documentation
        // says several validators per request is the intended shape; the Validation
        // manifest's example ships two of them.
        run.DiagnosticIds.Should().NotContain("ZERO010");
        run.HasError.Should().BeFalse();
    }

    [Fact]
    public void ZERO009_matches_a_dependency_on_a_closed_generic()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Descriptors;
            using IQOne.Zero.Messaging;
            using IQOne.Zero.Validation;

            namespace Test;

            public sealed record Register(string Email) : ICommand;

            public sealed class RegisterValidator : Validator<Register>
            {
                protected override void Configure(RuleSet<Register> rules)
                    => rules.NotEmpty(x => x.Email, "register.email");
            }

            public interface IReportCache;

            public sealed class ReportCache(IValidator<Register> validator) : IReportCache, ISingleton
            {
                private readonly IValidator<Register> _validator = validator;
            }
            """);

        // Dependencies were recorded as their open definition while closed registrations are
        // keyed by the closed form, so nothing generic was ever matched.
        run.DiagnosticIds.Should().Contain("ZERO009");
    }

    [Fact]
    public void ZERO008_is_reported_for_an_open_generic_that_cannot_be_named_unbound()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface ICache;

            public sealed class Cache<TKey, TValue> : ICache, IScoped;
            """);

        // Two type parameters, an interface that takes none: there is no
        // typeof(IService<,>) to pair typeof(Cache<,>) with.
        run.DiagnosticIds.Should().Contain("ZERO008");
    }

    [Fact]
    public void A_partial_class_split_across_files_is_registered_once()
    {
        var run = GeneratorHarness.Run(
        [
            """
            using System.Threading;
            using System.Threading.Tasks;
            using IQOne.Zero;
            using IQOne.Zero.Messaging;

            namespace Test;

            public sealed record Greet(string Name) : IQuery<string>;

            public sealed partial class GreetHandler : IQueryHandler<Greet, string>
            {
                public Task<Result<string>> HandleAsync(Greet query, CancellationToken cancellationToken)
                    => Task.FromResult(Result<string>.Success("hi"));
            }
            """,
            """
            using System;

            namespace Test;

            public sealed partial class GreetHandler : IDisposable
            {
                public void Dispose() { }
            }
            """
        ]);

        // Each declaration produced its own candidate, so the same handler was registered
        // twice and added to the dispatch table twice -- and RequestRegistry.Add throws on
        // the second, at startup, naming the same type as both handlers.
        run.DiagnosticIds.Should().NotContain("ZERO010");
        run.Occurrences("global::Test.GreetHandler>(services)").Should().Be(1);
        run.Occurrences("builder.Add(new global::IQOne.Zero.Messaging.RequestEntry(").Should().Be(1);
    }
}
