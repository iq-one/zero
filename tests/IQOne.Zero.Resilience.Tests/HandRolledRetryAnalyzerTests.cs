using IQOne.Zero.Resilience.Tests.Harness;

namespace IQOne.Zero.Resilience.Tests;

public class HandRolledRetryAnalyzerTests
{
    private const string Preamble = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using IQOne.Zero;
        using IQOne.Zero.Messaging;

        namespace Test;

        public sealed record GetRate(string Pair) : IQuery<decimal>;

        public interface IRates
        {
            Task<Result<decimal>> FetchAsync(string pair, CancellationToken cancellationToken);
            Task<IReadOnlyList<decimal>> PageAsync(int page, CancellationToken cancellationToken);
        }
        """;

    private static Task<AnalyzerRun> Handler(string body) => AnalyzerHarness.RunAsync($$"""
        {{Preamble}}

        public sealed class GetRateHandler(IRates rates) : IQueryHandler<GetRate, decimal>
        {
            public async Task<Result<decimal>> HandleAsync(GetRate query, CancellationToken cancellationToken)
            {
        {{body}}
            }
        }
        """);

    [Fact]
    public async Task A_retry_loop_in_a_handler_is_ZERO600()
    {
        var run = await Handler("""
                    for (var attempt = 0; attempt < 3; attempt++)
                    {
                        var result = await rates.FetchAsync(query.Pair, cancellationToken);

                        if (result.IsSuccess) return result;

                        await Task.Delay(200, cancellationToken);
                    }

                    return Error.Unavailable("rates.down", "No answer.");
            """);

        run.CompilerErrors.Should().BeEmpty();
        run.Ids.Should().Contain("ZERO600");
    }

    [Fact]
    public async Task Letting_the_pipeline_retry_is_not_reported()
    {
        var run = await Handler("""
                    return await rates.FetchAsync(query.Pair, cancellationToken);
            """);

        run.CompilerErrors.Should().BeEmpty();
        run.Ids.Should().NotContain("ZERO600",
            "this is the fix the rule documents, so it must not itself be reported");
    }

    [Fact]
    public async Task A_loop_that_is_not_a_retry_is_left_alone()
    {
        var run = await Handler("""
                    var total = 0m;

                    for (var page = 0; page < 5; page++)
                        foreach (var rate in await rates.PageAsync(page, cancellationToken))
                            total += rate;

                    return total;
            """);

        run.CompilerErrors.Should().BeEmpty();
        run.Ids.Should().NotContain("ZERO600",
            "paging is not retrying; the rule looks for a delay repeating work that just failed");
    }
}
