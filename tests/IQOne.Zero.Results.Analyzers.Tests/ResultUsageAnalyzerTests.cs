using IQOne.Zero.Results.Analyzers.Tests.Harness;

namespace IQOne.Zero.Results.Analyzers.Tests;

/// <summary>
/// The three ways a result stops being one: dropped, read without checking, or thrown.
/// </summary>
/// <remarks>
/// Every test compiles the snippet first. A rule that reports nothing because the sample did
/// not compile is the failure mode an analyzer test has to be built against.
/// </remarks>
public class ResultUsageAnalyzerTests
{
    private const string Preamble = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using IQOne.Zero;

        public sealed class Invoice
        {
            public bool IsOpen { get; set; }
        }

        public sealed class Logger
        {
            public void LogWarning(string message, object? argument) { }
        }

        """;

    private static async Task<AnalyzerRun> Analyze(string source)
    {
        var run = await AnalyzerHarness.RunAsync(Preamble + source);

        run.CompilerErrors.Should().BeEmpty("the snippet under test has to compile");

        return run;
    }

    // ---- ZERO100: a result is discarded ---------------------------------------------------

    [Fact]
    public async Task A_call_whose_result_nobody_takes_is_ZERO100()
    {
        var run = await Analyze("""
            public static class Payments
            {
                public static Result Apply(Invoice invoice) => Result.Success();

                public static void Run(Invoice invoice)
                {
                    Apply(invoice);
                }
            }
            """);

        run.Ids.Should().Equal("ZERO100");
    }

    [Fact]
    public async Task Assigning_a_result_to_a_discard_is_ZERO100_as_the_documentation_says_it_is()
    {
        // The regression this project was created for: `_ = ...` is a simple assignment with
        // a discard target, not an invocation statement, so the rule never saw it — while
        // both ZERO100.md and errors-are-values.md printed it as the canonical violation.
        var run = await Analyze("""
            public static class Payments
            {
                public static Result<int> Apply(Invoice invoice) => 1;

                public static void Run(Invoice invoice)
                {
                    _ = Apply(invoice);
                }
            }
            """);

        run.Ids.Should().Equal("ZERO100");
        run.Messages.Single().Should().Contain("Apply");
    }

    [Fact]
    public async Task The_fix_the_rule_documents_is_not_itself_reported()
    {
        // TapError is what ZERO100.md offers as the fix. Reporting it made the documented
        // fix produce the same error as the mistake.
        var run = await Analyze("""
            public static class Payments
            {
                public static Result<int> Apply(Invoice invoice) => 1;

                public static void Run(Invoice invoice, Logger logger)
                {
                    Apply(invoice).TapError(errors => logger.LogWarning("Refused: {Errors}", errors));
                }
            }
            """);

        run.Ids.Should().BeEmpty();
    }

    [Fact]
    public async Task A_discard_of_an_outcome_that_was_read_is_not_reported()
    {
        var run = await Analyze("""
            public static class Payments
            {
                public static Result<int> Apply(Invoice invoice) => 1;

                public static void Run(Invoice invoice, Logger logger)
                {
                    _ = Apply(invoice).TapError(errors => logger.LogWarning("Refused: {Errors}", errors));
                }
            }
            """);

        run.Ids.Should().BeEmpty();
    }

    [Fact]
    public async Task An_awaited_result_nobody_takes_is_still_ZERO100()
    {
        var run = await Analyze("""
            public static class Payments
            {
                public static Task<Result> ApplyAsync(Invoice invoice) => Task.FromResult(Result.Success());

                public static async Task Run(Invoice invoice)
                {
                    await ApplyAsync(invoice);
                }
            }
            """);

        run.Ids.Should().Equal("ZERO100");
    }

    [Fact]
    public async Task A_result_that_is_returned_is_not_discarded()
    {
        var run = await Analyze("""
            public static class Payments
            {
                public static Result Apply(Invoice invoice) => Result.Success();

                public static Result Run(Invoice invoice) => Apply(invoice);
            }
            """);

        run.Ids.Should().BeEmpty();
    }

    // ---- ZERO101: a value is read without checking ----------------------------------------

    [Fact]
    public async Task Reading_Value_without_checking_is_ZERO101()
    {
        var run = await Analyze("""
            public static class Payments
            {
                public static Result<Invoice> Get(int id) => new Invoice();

                public static Invoice Run(int id)
                {
                    var result = Get(id);

                    return result.Value;
                }
            }
            """);

        run.Ids.Should().Equal("ZERO101");
    }

    [Fact]
    public async Task Reading_Value_after_a_check_is_left_alone()
    {
        var run = await Analyze("""
            public static class Payments
            {
                public static Result<Invoice> Get(int id) => new Invoice();

                public static Invoice? Run(int id)
                {
                    var result = Get(id);

                    if (result.IsFailure) return null;

                    return result.Value;
                }
            }
            """);

        run.Ids.Should().BeEmpty();
    }

    // ---- ZERO102: an expected failure is thrown -------------------------------------------

    [Fact]
    public async Task Throwing_from_a_method_that_returns_a_result_is_ZERO102()
    {
        // Declared in Diagnostics since the first commit, advertised in the README, the
        // manifest, the rule file and the release notes — and reported by nothing.
        var run = await Analyze("""
            public static class Payments
            {
                public static Result<Invoice> Get(int id)
                {
                    if (id <= 0) throw new ArgumentException("id must be positive");

                    return new Invoice();
                }
            }
            """);

        run.Ids.Should().Equal("ZERO102");
        run.Messages.Single().Should().Contain("Get").And.Contain("ArgumentException");
    }

    [Fact]
    public async Task Throwing_from_a_method_that_returns_an_awaited_result_is_ZERO102()
    {
        var run = await Analyze("""
            public static class Payments
            {
                public static async Task<Result<Invoice>> GetAsync(int id)
                {
                    await Task.Yield();

                    if (id <= 0) throw new ArgumentException("id must be positive");

                    return new Invoice();
                }
            }
            """);

        run.Ids.Should().Equal("ZERO102");
    }

    [Fact]
    public async Task A_guard_against_a_null_that_should_have_been_impossible_is_not_reported()
    {
        var run = await Analyze("""
            public static class Payments
            {
                public static Result<Invoice> Get(Invoice? invoice, int id)
                {
                    ArgumentNullException.ThrowIfNull(invoice);

                    if (id <= 0) return Error.Validation("invoice.id", "The id must be positive.");

                    return invoice;
                }

                public static Result<Invoice> GetTheLongWay(Invoice? invoice)
                {
                    if (invoice is null) throw new ArgumentNullException(nameof(invoice));

                    return invoice;
                }
            }
            """);

        run.Ids.Should().BeEmpty();
    }

    [Fact]
    public async Task A_broken_invariant_and_an_unfinished_method_keep_throwing()
    {
        var run = await Analyze("""
            public static class Payments
            {
                public static Result<Invoice> Get(int id)
                {
                    if (id == int.MinValue) throw new InvalidOperationException("The store is corrupt.");

                    return new Invoice();
                }

                public static Result<Invoice> Later(int id) => throw new NotImplementedException();
            }
            """);

        run.Ids.Should().BeEmpty();
    }

    [Fact]
    public async Task Rethrowing_and_translating_inside_a_catch_are_not_reported()
    {
        var run = await Analyze("""
            public static class Payments
            {
                public static Result<Invoice> Get(int id)
                {
                    try
                    {
                        return new Invoice();
                    }
                    catch (FormatException)
                    {
                        throw;
                    }
                    catch (OverflowException exception)
                    {
                        throw new FormatException("The stored invoice could not be read.", exception);
                    }
                }
            }
            """);

        run.Ids.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_is_thrown_by_convention_and_stays_that_way()
    {
        var run = await Analyze("""
            public static class Payments
            {
                public static Result<Invoice> Get(int id, CancellationToken cancellationToken)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException(cancellationToken);

                    return new Invoice();
                }
            }
            """);

        run.Ids.Should().BeEmpty();
    }

    [Fact]
    public async Task A_method_that_does_not_promise_a_result_may_throw_what_it_likes()
    {
        var run = await Analyze("""
            public static class Payments
            {
                public static Invoice Get(int id)
                {
                    if (id <= 0) throw new ArgumentException("id must be positive");

                    return new Invoice();
                }
            }
            """);

        run.Ids.Should().BeEmpty();
    }

    [Fact]
    public async Task A_lambda_inside_a_result_returning_method_makes_its_own_promise()
    {
        var run = await Analyze("""
            public static class Payments
            {
                public static Result<int> Sum(IEnumerable<Invoice> invoices)
                {
                    Func<Invoice, int> total = invoice => invoice.IsOpen
                        ? 1
                        : throw new ArgumentException("A closed invoice has no total.");

                    var sum = 0;

                    foreach (var invoice in invoices) sum += total(invoice);

                    return sum;
                }
            }
            """);

        run.Ids.Should().BeEmpty();
    }
}
