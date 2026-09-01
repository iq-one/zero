using IQOne.Zero.Caching;
using IQOne.Zero.Messaging;
using Microsoft.Extensions.Options;

namespace IQOne.Zero.Caching.Tests;

/// <summary>
/// Drives caching through the real pipeline. A cache that can be bypassed is not a cache, and
/// one that answers the wrong question is worse than none, so these assert on what a caller
/// gets back rather than on what the store was told.
/// </summary>
public class CachingBehaviorTests
{
    [Fact]
    public async Task A_cacheable_query_is_handled_once_and_answered_from_the_cache_after_that()
    {
        using var app = Application(out var handler);

        var first = await app.SendAsync(new GetInvoice(42));
        var second = await app.SendAsync(new GetInvoice(42));

        first.Value.Should().Be("answer 1");
        second.Value.Should().Be("answer 1", "the second call was answered from the cache");
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Two_queries_that_differ_get_different_answers()
    {
        using var app = Application(out var handler);

        var first = await app.SendAsync(new GetInvoice(1));
        var second = await app.SendAsync(new GetInvoice(2));

        first.Value.Should().Be("answer 1");
        second.Value.Should().Be("answer 2", "the key carries the id, so these are different questions");
        handler.Calls.Should().Be(2);
    }

    [Fact]
    public async Task A_query_that_does_not_ask_to_be_cached_is_handled_every_time()
    {
        var builder = TestApplication.With();
        var handler = builder.Handles<GetSummary>();

        using var app = builder.Build();

        await app.SendAsync(new GetSummary(1));
        await app.SendAsync(new GetSummary(1));

        handler.Calls.Should().Be(2, "nothing is cached unless the query says it may be");
    }

    [Fact]
    public async Task A_failure_is_never_stored()
    {
        var cache = Store.Recording();
        var builder = TestApplication.With(cache);
        var handler = builder.Handles<GetInvoice>();

        handler.Refuse = Error.Unavailable("invoice.store", "The store timed out.");

        using var app = builder.Build();

        var first = await app.SendAsync(new GetInvoice(42));

        first.IsFailure.Should().BeTrue();
        cache.Writes.Should().BeEmpty("a timeout is about the moment, not about the question");

        handler.Refuse = null;

        var second = await app.SendAsync(new GetInvoice(42));

        second.Value.Should().Be("answer 2", "the second call reached the handler and got a real answer");
    }

    [Fact]
    public async Task Switching_caching_off_sends_every_call_to_the_handler()
    {
        var cache = Store.Recording();
        var builder = TestApplication.With(cache, options => options.Enabled = false);
        var handler = builder.Handles<GetInvoice>();

        using var app = builder.Build();

        await app.SendAsync(new GetInvoice(42));
        await app.SendAsync(new GetInvoice(42));

        handler.Calls.Should().Be(2);
        cache.Reads.Should().BeEmpty("the store is not even asked");
        cache.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_command_that_asks_to_be_cached_is_refused_loudly()
    {
        var builder = TestApplication.With();

        builder.Handles<CloseInvoice>();

        using var app = builder.Build();

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => app.SendAsync(new CloseInvoice(42)));

        refused.Message.Should().Contain("ZERO210");
    }

    [Fact]
    public async Task A_command_that_asks_to_be_cached_is_refused_even_while_caching_is_off()
    {
        var builder = TestApplication.With(options => options.Enabled = false);

        builder.Handles<CloseInvoice>();

        using var app = builder.Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => app.SendAsync(new CloseInvoice(42)));
    }

    [Fact]
    public async Task A_query_with_an_empty_key_is_refused()
    {
        var builder = TestApplication.With();

        builder.Handles<GetWithoutKey>();

        using var app = builder.Build();

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => app.SendAsync(new GetWithoutKey(42)));

        refused.Message.Should().Contain("CacheKey");
    }

    [Fact]
    public async Task A_query_with_a_lifetime_of_zero_is_refused()
    {
        var builder = TestApplication.With();

        builder.Handles<GetExpiredOnArrival>();

        using var app = builder.Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => app.SendAsync(new GetExpiredOnArrival(42)));
    }

    [Fact]
    public async Task The_query_decides_its_own_lifetime_and_the_options_supply_the_rest()
    {
        var cache = Store.Recording();
        var builder = TestApplication.With(cache, options => options.DefaultLifetime = TimeSpan.FromSeconds(90));

        builder.Handles<GetInvoice>();
        builder.Handles<GetInvoiceLines>();

        using var app = builder.Build();

        await app.SendAsync(new GetInvoice(42));
        await app.SendAsync(new GetInvoiceLines(42));

        cache.Writes.Select(w => w.Lifetime).Should().Equal(TimeSpan.FromSeconds(90), TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task Every_key_carries_the_configured_prefix()
    {
        var cache = Store.Recording();
        var builder = TestApplication.With(cache, options => options.KeyPrefix = "billing:");

        builder.Handles<GetInvoice>();

        using var app = builder.Build();

        await app.SendAsync(new GetInvoice(42));

        cache.Reads.Should().Equal("billing:invoice:42");
        cache.Writes.Select(w => w.Key).Should().Equal("billing:invoice:42");
    }

    [Fact]
    public async Task The_caller_s_token_reaches_both_the_store_and_the_handler()
    {
        var cache = Store.Recording();
        var builder = TestApplication.With(cache);
        var handler = builder.Handles<GetInvoice>();

        using var app = builder.Build();
        using var cancellation = new CancellationTokenSource();

        await app.SendAsync(new GetInvoice(42), cancellation.Token);

        cache.Tokens.Should().NotBeEmpty().And.AllSatisfy(t => t.Should().Be(cancellation.Token));
        handler.Token.Should().Be(cancellation.Token);
    }

    [Fact]
    public async Task A_token_that_is_already_cancelled_stops_the_request_at_the_store()
    {
        var builder = TestApplication.With();
        var handler = builder.Handles<GetInvoice>();

        using var app = builder.Build();
        using var cancellation = new CancellationTokenSource();

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => app.SendAsync(new GetInvoice(42), cancellation.Token));

        handler.Calls.Should().Be(0);
    }

    [Fact]
    public async Task An_answer_that_is_null_is_a_hit_rather_than_a_miss_forever()
    {
        var builder = TestApplication.With();
        var handler = builder.Handles<GetNothing, string?>(_ => null);

        using var app = builder.Build();

        await app.SendAsync(new GetNothing(1));
        await app.SendAsync(new GetNothing(1));

        handler.Calls.Should().Be(1, "null is an answer, and re-asking for it forever is a leak of work");
    }

    [Fact]
    public async Task An_answer_survives_until_a_command_says_otherwise()
    {
        using var app = Application(out var handler);

        await app.SendAsync(new GetInvoice(42));
        await app.SendAsync(new GetInvoice(42));

        handler.Calls.Should().Be(1);

        // What a command does after it changes the data behind a family of queries.
        await app.Invalidator.InvalidateByPrefixAsync("invoice:", CancellationToken.None);

        var afterwards = await app.SendAsync(new GetInvoice(42));

        handler.Calls.Should().Be(2);
        afterwards.Value.Should().Be("answer 2");
    }

    [Fact]
    public async Task One_answer_can_be_dropped_without_taking_its_neighbours()
    {
        var builder = TestApplication.With();
        var invoice = builder.Handles<GetInvoice>();
        var lines = builder.Handles<GetInvoiceLines>();

        using var app = builder.Build();

        await app.SendAsync(new GetInvoice(42));
        await app.SendAsync(new GetInvoiceLines(42));

        await app.Invalidator.InvalidateAsync("invoice:42", CancellationToken.None);

        await app.SendAsync(new GetInvoice(42));
        await app.SendAsync(new GetInvoiceLines(42));

        invoice.Calls.Should().Be(2);
        lines.Calls.Should().Be(1, "only the key that was named was dropped");
    }

    [Fact]
    public void The_behaviour_sits_between_validation_and_the_transaction()
    {
        var behavior = new CachingBehavior<GetInvoice, string>(
            Store.InMemory(),
            Options.Create(new CachingOptions()));

        behavior.Order.Should().Be(BehaviorOrder.Caching);
        behavior.Order.Should().BeGreaterThan(BehaviorOrder.Validation);
        behavior.Order.Should().BeLessThan(BehaviorOrder.Transaction);
    }

    private static RunningApplication Application(out CountingHandler<GetInvoice, string> handler)
    {
        var builder = TestApplication.With();

        handler = builder.Handles<GetInvoice>();

        return builder.Build();
    }
}
