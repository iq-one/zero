using IQOne.Zero.Caching.Tests.Harness;

namespace IQOne.Zero.Caching.Tests;

/// <summary>
/// The two mistakes that produce a plausible wrong answer instead of a failure. Both are
/// reported at the point they were written, because neither leaves anything at run time that
/// would lead someone back to it.
/// </summary>
public class CacheableUsageAnalyzerTests
{
    private const string Preamble = """
        using System;
        using IQOne.Zero;
        using IQOne.Zero.Caching;
        using IQOne.Zero.Messaging;

        """;

    private static async Task<AnalyzerRun> Analyze(string source)
    {
        var run = await AnalyzerHarness.RunAsync(Preamble + source);

        run.CompilerErrors.Should().BeEmpty("the snippet under test has to compile");

        return run;
    }

    [Fact]
    public async Task A_query_whose_key_carries_its_parameters_is_left_alone()
    {
        var run = await Analyze("""
            public sealed record GetInvoice(int Id) : IQuery<string>, ICacheable
            {
                public string CacheKey => $"invoice:{Id}";
            }
            """);

        run.Ids.Should().BeEmpty();
    }

    [Fact]
    public async Task A_command_that_asks_to_be_cached_is_ZERO210()
    {
        var run = await Analyze("""
            public sealed record CloseInvoice(int Id) : ICommand<string>, ICacheable
            {
                public string CacheKey => $"invoice:{Id}";
            }
            """);

        run.Ids.Should().Equal("ZERO210");
    }

    [Fact]
    public async Task Something_that_is_not_a_request_at_all_is_ZERO210()
    {
        var run = await Analyze("""
            public sealed record InvoiceModel(int Id) : ICacheable
            {
                public string CacheKey => $"invoice:{Id}";
            }
            """);

        run.Ids.Should().Equal("ZERO210");
    }

    [Fact]
    public async Task An_interface_that_gathers_ICacheable_is_a_shape_a_consumer_may_declare()
    {
        var run = await Analyze("""
            public interface ICacheableQuery<TResponse> : IQuery<TResponse>, ICacheable;

            public sealed record GetInvoice(int Id) : ICacheableQuery<string>
            {
                public string CacheKey => $"invoice:{Id}";
            }
            """);

        run.Ids.Should().BeEmpty();
    }

    [Fact]
    public async Task A_constant_key_on_a_query_that_takes_parameters_is_ZERO211()
    {
        var run = await Analyze("""
            public sealed record GetInvoice(int Id) : IQuery<string>, ICacheable
            {
                public string CacheKey => "invoice";
            }
            """);

        run.Ids.Should().Equal("ZERO211");
    }

    [Fact]
    public async Task A_constant_key_assigned_rather_than_computed_is_also_ZERO211()
    {
        var run = await Analyze("""
            public sealed record GetInvoice(int Id) : IQuery<string>, ICacheable
            {
                public string CacheKey { get; } = "invoice";
            }
            """);

        run.Ids.Should().Equal("ZERO211");
    }

    [Fact]
    public async Task A_class_that_keeps_its_arguments_private_is_still_ZERO211()
    {
        var run = await Analyze("""
            public sealed class GetInvoice(int id) : IQuery<string>, ICacheable
            {
                private readonly int _id = id;

                public string CacheKey => "invoice";
            }
            """);

        run.Ids.Should().Equal("ZERO211");
    }

    [Fact]
    public async Task A_query_with_nothing_to_vary_on_may_have_a_constant_key()
    {
        var run = await Analyze("""
            public sealed record GetCurrencies : IQuery<string>, ICacheable
            {
                public string CacheKey => "currencies";
            }
            """);

        run.Ids.Should().BeEmpty("there is nothing the key could have left out");
    }

    [Fact]
    public async Task A_key_that_branches_on_a_parameter_is_left_alone()
    {
        var run = await Analyze("""
            public sealed record GetInvoices(bool Drafts) : IQuery<string>, ICacheable
            {
                public string CacheKey
                {
                    get
                    {
                        if (Drafts) return "invoices:drafts";

                        return "invoices";
                    }
                }
            }
            """);

        run.Ids.Should().BeEmpty("the author already thought about what the key depends on");
    }

    [Fact]
    public async Task A_conditional_key_is_left_alone()
    {
        var run = await Analyze("""
            public sealed record GetInvoices(bool Drafts) : IQuery<string>, ICacheable
            {
                public string CacheKey => Drafts ? "invoices:drafts" : "invoices";
            }
            """);

        run.Ids.Should().BeEmpty();
    }

    [Fact]
    public async Task A_query_that_never_asked_to_be_cached_is_not_the_analyzer_s_business()
    {
        var run = await Analyze("""
            public sealed record GetInvoice(int Id) : IQuery<string>;
            """);

        run.Ids.Should().BeEmpty();
    }
}
