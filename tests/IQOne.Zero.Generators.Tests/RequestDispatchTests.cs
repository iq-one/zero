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
        using IQOne.Zero.Results;

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
