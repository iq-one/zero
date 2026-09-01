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
}
