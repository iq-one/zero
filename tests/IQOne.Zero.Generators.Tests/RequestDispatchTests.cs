using IQOne.Zero.Generators.Tests.Harness;

namespace IQOne.Zero.Generators.Tests;

/// <summary>
/// The compile-time half of messaging. Dispatch is generated, so a request reaches its
/// handler through a dictionary read rather than reflection, and a request nobody handles
/// is known before the application starts.
/// </summary>
public class RequestDispatchTests
{
    private const string Preamble = """
        using System.Threading;
        using System.Threading.Tasks;
        using IQOne.Zero.Messaging;
        using IQOne.Zero;

        namespace Test;
        """;

    [Fact]
    public void A_handler_becomes_a_dispatch_row_with_both_types_closed()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed record Greet(string Name) : IQuery<string>;

            public sealed class GreetHandler : IQueryHandler<Greet, string>
            {
                public Task<Result<string>> HandleAsync(Greet query, CancellationToken cancellationToken)
                    => Task.FromResult(Result<string>.Success("hi"));
            }
            """);

        run.HasError.Should().BeFalse();

        run.GeneratedSource.Should()
            .Contain("RequestPipeline.RunAsync<global::Test.Greet, string>")
            .And.Contain("typeof(global::Test.GreetHandler)");
    }

    [Fact]
    public void A_request_declares_itself_so_startup_can_report_one_nobody_handles()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed record Orphan : ICommand;
            """);

        run.GeneratedSource.Should().Contain("builder.Declare(typeof(global::Test.Orphan));");
    }

    [Fact]
    public void A_handler_is_registered_under_the_closed_interface_the_pipeline_resolves()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed record Greet(string Name) : IQuery<string>;

            public sealed class GreetHandler : IQueryHandler<Greet, string>
            {
                public Task<Result<string>> HandleAsync(Greet query, CancellationToken cancellationToken)
                    => Task.FromResult(Result<string>.Success("hi"));
            }
            """);

        // Not the open IQueryHandler<TQuery, TResponse> it declares: nothing can be
        // registered as an open generic, and the pipeline asks for the closed one.
        run.GeneratedSource.Should()
            .Contain("AddScoped<global::IQOne.Zero.Messaging.IRequestHandler<global::Test.Greet, string>, global::Test.GreetHandler>")
            .And.NotContain("<TQuery, TResponse>");
    }

    [Fact]
    public void Dispatch_is_wired_into_the_module_s_configure_step()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed record Greet(string Name) : IQuery<string>;
            """);

        run.GeneratedSource.Should().Contain("RegisterRequests(");
    }
}

/// <summary>
/// The compile-time half of the web layer: a route attribute on a request becomes a real
/// endpoint, described in generated code rather than discovered at startup.
/// </summary>
public class EndpointGenerationTests
{
    private const string Preamble = """
        using System.Threading;
        using System.Threading.Tasks;
        using IQOne.Zero;
        using IQOne.Zero.Messaging;
        using IQOne.Zero.Web;

        namespace Test;
        """;

    [Fact]
    public void A_routed_request_becomes_an_endpoint()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            [Get("/things/{id:int}", Tag = "Things")]
            public sealed record GetThing(int Id) : IQuery<string>;
            """);

        run.HasError.Should().BeFalse();

        run.GeneratedSource.Should()
            .Contain("\"GET\"")
            .And.Contain("\"/things/{id:int}\"")
            .And.Contain("\"Things\"")
            .And.Contain("ZeroEndpoint.RunAsync<global::Test.GetThing, string>");
    }

    [Fact]
    public void An_unrouted_request_produces_no_endpoint()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed record Quiet : ICommand;
            """);

        run.GeneratedSource.Should().Contain("No request in this assembly declares a route.");
    }

    [Fact]
    public void A_route_on_something_that_is_not_a_request_is_reported()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            [Get("/things")]
            public sealed record NotARequest(int Id);
            """);

        run.DiagnosticIds.Should().Contain("ZERO300");
    }

    [Fact]
    public void An_empty_route_pattern_is_reported()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            [Get("")]
            public sealed record Rootless : IQuery<string>;
            """);

        run.DiagnosticIds.Should().Contain("ZERO301");
    }

    [Fact]
    public void A_route_pattern_containing_an_equals_sign_is_still_a_pattern()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            [Get("/reports/{page=1}")]
            public sealed record ListReports : IQuery<string>;
            """);

        // A default value in a route segment is legal ASP.NET. Positional and named arguments
        // were flattened into one 'Name=Value' list, so the pattern read as a named argument
        // and ZERO301 fired on a correct route.
        run.DiagnosticIds.Should().NotContain("ZERO301");
        run.GeneratedSource.Should().Contain("\"/reports/{page=1}\"");
    }

    [Fact]
    public void The_first_positional_argument_is_the_pattern_whatever_it_looks_like()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            [Get("Name=oops")]
            public sealed record Odd : IQuery<string>;
            """);

        // The old reader took the first argument without an '=' as the pattern and anything
        // starting with 'Name=' as the endpoint name, so this route had neither.
        run.DiagnosticIds.Should().NotContain("ZERO301");
        run.GeneratedSource.Should().Contain("\"Name=oops\"").And.NotContain("\"oops\"");
    }

    [Fact]
    public void A_route_pattern_is_emitted_as_an_escaped_literal()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            [Get(@"/products/{sku:regex(^\d{3}$)}")]
            public sealed record GetProduct(string Sku) : IQuery<string>;
            """);

        // Interpolated raw, the backslash reached Module.g.cs as a C# escape and failed the
        // build with CS1009 in a file the developer did not write.
        run.GeneratedFileErrorMessages.Should().BeEmpty();
        run.GeneratedSource.Should().Contain(@"""/products/{sku:regex(^\\d{3}$)}""");
    }

    [Fact]
    public void An_endpoint_name_defaults_to_the_namespace_qualified_type_name()
    {
        var run = GeneratorHarness.Run($$"""
            using IQOne.Zero;
            using IQOne.Zero.Messaging;
            using IQOne.Zero.Web;

            namespace App.Billing;

            [Get("/invoices/{id:int}")]
            public sealed record GetInvoice(int Id) : IQuery<string>;
            """);

        // App.Billing.GetInvoice and App.Sales.GetInvoice both defaulted to "GetInvoice", and
        // ASP.NET throws 'Duplicate endpoint name' on the first LinkGenerator or OpenAPI use.
        run.GeneratedSource.Should().Contain("\"App.Billing.GetInvoice\"");
    }

    [Fact]
    public void A_route_is_not_inherited_by_a_request_deriving_from_a_routed_one()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            [Get("/things")]
            public record ListThings : IQuery<string>;

            public sealed record ListThingsV2 : ListThings;
            """);

        // Attributes reach derived types now, and a route says Inherited = false for exactly
        // this reason: a second endpoint on the same method and pattern throws when the
        // endpoint table is built, naming two requests that both claim the route.
        run.Occurrences("new global::IQOne.Zero.Web.ZeroEndpointDescriptor(").Should().Be(1);
    }

    [Fact]
    public void The_web_diagnostics_carry_the_category_their_rule_pages_document()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            [Get("/things")]
            public sealed record NotARequest(int Id);
            """);

        // Configuring severity by the documented category affected nothing while the code
        // said Zero.Registration and the pages said Zero.Web.
        run.Diagnostics.Single(d => d.Id == "ZERO300").Descriptor.Category.Should().Be("Zero.Web");
    }
}

/// <summary>
/// A validator implements its interface through a base class, so the naming convention
/// finds nothing. It has to be registered under the closed generic the behaviour resolves.
/// </summary>
public class ClosedGenericRegistrationTests
{
    [Fact]
    public void A_validator_is_registered_under_the_closed_interface_even_though_it_derives_from_a_base()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.Messaging;
            using IQOne.Zero.Validation;

            namespace Test;

            public sealed record Register(string Email) : ICommand;

            public sealed class RegisterValidator : Validator<Register>
            {
                protected override void Configure(RuleSet<Register> rules)
                    => rules.NotEmpty(x => x.Email, "register.email");
            }
            """);

        run.HasError.Should().BeFalse();

        run.GeneratedSource.Should().Contain(
            "AddScoped<global::IQOne.Zero.Validation.IValidator<global::Test.Register>, global::Test.RegisterValidator>");
    }
}
