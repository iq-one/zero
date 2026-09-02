using IQOne.Zero.BackgroundWork.Tests.Harness;

namespace IQOne.Zero.BackgroundWork.Tests;

public class RecurringJobAnalyzerTests
{
    private const string Preamble = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using IQOne.Zero;
        using IQOne.Zero.BackgroundWork;

        namespace Test;
        """;

    private static Task<AnalyzerRun> Job(string body) => AnalyzerHarness.RunAsync($$"""
        {{Preamble}}

        public sealed class Sweeper(TimeProvider time) : IRecurringJob
        {
            public Task<Result> RunAsync(JobRunContext context, CancellationToken cancellationToken)
            {
        {{body}}
            }
        }
        """);

    [Fact]
    public async Task Reading_the_clock_in_a_job_is_ZERO550()
    {
        var run = await Job("""
                    var since = time.GetUtcNow().AddMinutes(-15);

                    return Task.FromResult(Result.Success());
            """);

        run.CompilerErrors.Should().BeEmpty();
        run.Ids.Should().Contain("ZERO550");
    }

    [Fact]
    public async Task DateTime_Now_in_a_job_is_ZERO550_too()
    {
        var run = await Job("""
                    var since = DateTime.UtcNow;

                    return Task.FromResult(Result.Success());
            """);

        run.Ids.Should().Contain("ZERO550");
    }

    [Fact]
    public async Task Taking_the_window_from_the_occurrence_is_not_reported()
    {
        var run = await Job("""
                    var since = context.ScheduledFor.AddMinutes(-15);

                    return Task.FromResult(Result.Success());
            """);

        run.CompilerErrors.Should().BeEmpty();
        run.Ids.Should().NotContain("ZERO550",
            "this is the fix the rule documents, so it must not itself be reported");
    }

    [Fact]
    public async Task A_job_that_never_uses_its_token_is_ZERO551()
    {
        var run = await Job("""
                    return Task.FromResult(Result.Success());
            """);

        run.Ids.Should().Contain("ZERO551");
    }

    [Fact]
    public async Task Passing_the_token_on_satisfies_the_rule()
    {
        var run = await Job("""
                    return Task.Delay(1, cancellationToken).ContinueWith(_ => Result.Success(), cancellationToken);
            """);

        run.CompilerErrors.Should().BeEmpty();
        run.Ids.Should().NotContain("ZERO551");
    }

    [Fact]
    public async Task Neither_rule_fires_outside_a_job()
    {
        var run = await AnalyzerHarness.RunAsync($$"""
            {{Preamble}}

            public sealed class Ordinary(TimeProvider time)
            {
                public DateTimeOffset When() => time.GetUtcNow();
            }
            """);

        run.Ids.Should().BeEmpty("reading a clock is perfectly ordinary everywhere else");
    }
}
