using IQOne.Zero.Observability.Tests.Harness;

namespace IQOne.Zero.Observability.Tests;

/// <summary>
/// What the analyzer reports, and — the harder half — what it leaves alone.
/// </summary>
/// <remarks>
/// A rule that is wrong sometimes gets the whole category suppressed, taking the rules that
/// were right with it. So there are as many tests here for handlers that are doing nothing
/// wrong as there are for the two mistakes.
/// </remarks>
public class HandlerTelemetryAnalyzerTests
{
    private const string Preamble = """
        using System.Collections.Generic;
        using System.Diagnostics;
        using System.Diagnostics.Metrics;
        using System.Threading;
        using System.Threading.Tasks;
        using IQOne.Zero;
        using IQOne.Zero.Messaging;
        using Microsoft.Extensions.Logging;

        public sealed record GetInvoice(int Id) : IQuery<string>;

        public sealed record CloseInvoice(int Id, string Reason) : ICommand<string>;

        """;

    private static async Task<AnalyzerRun> RunAsync(string body)
    {
        var run = await AnalyzerHarness.RunAsync(Preamble + body);

        run.CompilerErrors.Should().BeEmpty("the snippet under test has to be code a consumer could write");

        return run;
    }

    [Fact]
    public async Task A_handler_that_keeps_its_own_activity_source_is_reported()
    {
        var run = await RunAsync("""
            public sealed class GetInvoiceHandler : IQueryHandler<GetInvoice, string>
            {
                private static readonly ActivitySource Source = new("Acme.Invoices");

                public Task<Result<string>> HandleAsync(GetInvoice query, CancellationToken cancellationToken)
                {
                    using var activity = Source.StartActivity("GetInvoice");

                    return Task.FromResult(Result<string>.Success("an invoice"));
                }
            }
            """);

        run.Ids.Should().Equal("ZERO400");
        run.Reported[0].GetMessage().Should().Contain("GetInvoiceHandler").And.Contain("ActivitySource");
    }

    [Fact]
    public async Task A_handler_that_keeps_its_own_meter_is_reported()
    {
        var run = await RunAsync("""
            public sealed class GetInvoiceHandler : IQueryHandler<GetInvoice, string>
            {
                private readonly Meter _meter = new Meter("Acme.Invoices");

                public Task<Result<string>> HandleAsync(GetInvoice query, CancellationToken cancellationToken)
                    => Task.FromResult(Result<string>.Success("an invoice"));
            }
            """);

        run.Ids.Should().Equal("ZERO400");
        run.Reported[0].GetMessage().Should().Contain("Meter");
    }

    [Fact]
    public async Task A_source_built_inside_the_method_is_reported_too()
    {
        var run = await RunAsync("""
            public sealed class GetInvoiceHandler : IQueryHandler<GetInvoice, string>
            {
                public Task<Result<string>> HandleAsync(GetInvoice query, CancellationToken cancellationToken)
                {
                    var source = new ActivitySource("Acme.Invoices");

                    return Task.FromResult(Result<string>.Success("an invoice"));
                }
            }
            """);

        run.Ids.Should().Equal("ZERO400");
    }

    [Fact]
    public async Task A_telemetry_type_that_is_not_a_handler_is_left_alone()
    {
        // Where an application's own instruments belong: one named type a collector can be
        // told about, rather than a name invented inside a handler.
        var run = await RunAsync("""
            public static class InvoiceTelemetry
            {
                public const string SourceName = "Acme.Invoices";

                public static readonly ActivitySource Source = new(SourceName);

                public static readonly Meter Meter = new(SourceName);
            }
            """);

        run.Ids.Should().BeEmpty();
    }

    [Fact]
    public async Task A_handler_that_writes_the_whole_request_to_the_log_is_reported()
    {
        var run = await RunAsync("""
            public sealed class CloseInvoiceHandler(ILogger<CloseInvoiceHandler> logger)
                : ICommandHandler<CloseInvoice, string>
            {
                public Task<Result<string>> HandleAsync(CloseInvoice command, CancellationToken cancellationToken)
                {
                    logger.LogInformation("Closing {Command}", command);

                    return Task.FromResult(Result<string>.Success("closed"));
                }
            }
            """);

        run.Ids.Should().Equal("ZERO401");
        run.Reported[0].GetMessage().Should().Contain("CloseInvoiceHandler").And.Contain("LogRequestContents");
    }

    [Fact]
    public async Task A_request_pushed_into_a_logging_scope_is_reported()
    {
        var run = await RunAsync("""
            public sealed class CloseInvoiceHandler(ILogger<CloseInvoiceHandler> logger)
                : ICommandHandler<CloseInvoice, string>
            {
                public Task<Result<string>> HandleAsync(CloseInvoice command, CancellationToken cancellationToken)
                {
                    using (logger.BeginScope(command))
                    {
                        return Task.FromResult(Result<string>.Success("closed"));
                    }
                }
            }
            """);

        run.Ids.Should().Equal("ZERO401");
    }

    [Fact]
    public async Task A_handler_that_logs_a_domain_event_is_left_alone()
    {
        // The case that keeps this rule narrow. An invoice being closed is something only the
        // handler knows, and reporting it would train everyone to suppress the category.
        var run = await RunAsync("""
            public sealed class CloseInvoiceHandler(ILogger<CloseInvoiceHandler> logger)
                : ICommandHandler<CloseInvoice, string>
            {
                public Task<Result<string>> HandleAsync(CloseInvoice command, CancellationToken cancellationToken)
                {
                    logger.LogInformation("Invoice {Id} closed because {Reason}", command.Id, command.Reason);

                    return Task.FromResult(Result<string>.Success("closed"));
                }
            }
            """);

        run.Ids.Should().BeEmpty();
    }

    [Fact]
    public async Task A_handler_that_merely_takes_a_logger_is_left_alone()
    {
        var run = await RunAsync("""
            public sealed class GetInvoiceHandler(ILogger<GetInvoiceHandler> logger)
                : IQueryHandler<GetInvoice, string>
            {
                public Task<Result<string>> HandleAsync(GetInvoice query, CancellationToken cancellationToken)
                {
                    logger.LogDebug("Reading from the archive");

                    return Task.FromResult(Result<string>.Success("an invoice"));
                }
            }
            """);

        run.Ids.Should().BeEmpty();
    }

    [Fact]
    public async Task Something_that_is_not_a_handler_logging_a_request_is_left_alone()
    {
        // A transport at the edge writing what it received is a different decision, taken by
        // someone who can see the whole envelope. This rule is about handlers.
        var run = await RunAsync("""
            public sealed class InvoiceEndpoint(ILogger<InvoiceEndpoint> logger)
            {
                public void Received(CloseInvoice command) => logger.LogInformation("Got {Command}", command);
            }
            """);

        run.Ids.Should().BeEmpty();
    }

    [Fact]
    public async Task A_compilation_with_no_handlers_reports_nothing()
    {
        var run = await AnalyzerHarness.RunAsync("""
            using System.Diagnostics;

            public sealed class Whatever
            {
                private static readonly ActivitySource Source = new("Acme.Whatever");
            }
            """);

        run.CompilerErrors.Should().BeEmpty();
        run.Ids.Should().BeEmpty();
    }

    [Fact]
    public async Task Both_mistakes_in_one_handler_are_both_reported()
    {
        var run = await RunAsync("""
            public sealed class CloseInvoiceHandler(ILogger<CloseInvoiceHandler> logger)
                : ICommandHandler<CloseInvoice, string>
            {
                private static readonly Meter Meter = new("Acme.Invoices");

                public Task<Result<string>> HandleAsync(CloseInvoice command, CancellationToken cancellationToken)
                {
                    logger.LogInformation("Closing {Command}", command);

                    return Task.FromResult(Result<string>.Success("closed"));
                }
            }
            """);

        run.Ids.Should().BeEquivalentTo(["ZERO400", "ZERO401"]);
    }
}
