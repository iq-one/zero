using IQOne.Zero.Caching;

namespace IQOne.Zero.Caching.Tests;

/// <summary>
/// The store on its own. Everything here is about the contract <see cref="ICache"/> states,
/// not about the behaviour that usually calls it — a second implementation should be able to
/// be dropped in and pass the same list.
/// </summary>
public class InMemoryCacheTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task An_absent_key_is_a_miss()
    {
        var cache = Store.InMemory();

        var found = await cache.GetAsync<string>("nothing", CancellationToken.None);

        found.Found.Should().BeFalse();
        found.TryGetValue(out _).Should().BeFalse();
    }

    [Fact]
    public async Task What_was_stored_is_what_comes_back()
    {
        var cache = Store.InMemory();

        await cache.SetAsync("invoice:1", "one", Minute, CancellationToken.None);

        (await cache.GetAsync<string>("invoice:1", CancellationToken.None)).TryGetValue(out var value)
            .Should().BeTrue();

        value.Should().Be("one");
    }

    [Fact]
    public async Task A_stored_null_is_a_hit()
    {
        var cache = Store.InMemory();

        await cache.SetAsync<string?>("nothing:1", null, Minute, CancellationToken.None);

        var found = await cache.GetAsync<string?>("nothing:1", CancellationToken.None);

        found.Found.Should().BeTrue("a miss and an answer of null are different answers");
        found.TryGetValue(out var value).Should().BeTrue();
        value.Should().BeNull();
    }

    [Fact]
    public async Task A_key_read_as_the_wrong_type_is_a_miss_rather_than_a_cast_that_throws()
    {
        var cache = Store.InMemory();

        await cache.SetAsync("invoice:1", "one", Minute, CancellationToken.None);

        (await cache.GetAsync<int>("invoice:1", CancellationToken.None)).Found.Should().BeFalse();
    }

    [Fact]
    public async Task Storing_a_key_twice_keeps_the_second_value()
    {
        var cache = Store.InMemory();

        await cache.SetAsync("invoice:1", "one", Minute, CancellationToken.None);
        await cache.SetAsync("invoice:1", "two", Minute, CancellationToken.None);

        (await cache.GetAsync<string>("invoice:1", CancellationToken.None)).TryGetValue(out var value);

        value.Should().Be("two");
    }

    [Fact]
    public async Task A_removed_key_is_gone_and_removing_an_absent_one_is_not_an_error()
    {
        var cache = Store.InMemory();

        await cache.SetAsync("invoice:1", "one", Minute, CancellationToken.None);
        await cache.RemoveAsync("invoice:1", CancellationToken.None);
        await cache.RemoveAsync("invoice:1", CancellationToken.None);

        (await cache.GetAsync<string>("invoice:1", CancellationToken.None)).Found.Should().BeFalse();
    }

    [Fact]
    public async Task A_prefix_takes_the_branch_below_it_and_nothing_else()
    {
        var cache = Store.InMemory();

        await cache.SetAsync("invoice:1", "one", Minute, CancellationToken.None);
        await cache.SetAsync("invoice:1:lines", "lines", Minute, CancellationToken.None);
        await cache.SetAsync("invoice:2", "two", Minute, CancellationToken.None);
        await cache.SetAsync("customer:1", "customer", Minute, CancellationToken.None);

        await cache.RemoveByPrefixAsync("invoice:1", CancellationToken.None);

        (await cache.GetAsync<string>("invoice:1", CancellationToken.None)).Found.Should().BeFalse();
        (await cache.GetAsync<string>("invoice:1:lines", CancellationToken.None)).Found.Should().BeFalse();
        (await cache.GetAsync<string>("invoice:2", CancellationToken.None)).Found.Should().BeTrue();
        (await cache.GetAsync<string>("customer:1", CancellationToken.None)).Found.Should().BeTrue();
    }

    [Fact]
    public async Task A_key_that_was_replaced_can_still_be_reached_by_its_prefix()
    {
        var cache = Store.InMemory();

        await cache.SetAsync("invoice:1", "one", Minute, CancellationToken.None);
        await cache.SetAsync("invoice:1", "one again", Minute, CancellationToken.None);

        await cache.RemoveByPrefixAsync("invoice:", CancellationToken.None);

        (await cache.GetAsync<string>("invoice:1", CancellationToken.None)).Found
            .Should().BeFalse("replacing an entry must not lose track of its key");
    }

    [Fact]
    public async Task An_empty_prefix_clears_everything_this_cache_wrote()
    {
        var cache = Store.InMemory();

        await cache.SetAsync("invoice:1", "one", Minute, CancellationToken.None);
        await cache.SetAsync("customer:1", "customer", Minute, CancellationToken.None);

        await cache.RemoveByPrefixAsync(string.Empty, CancellationToken.None);

        (await cache.GetAsync<string>("invoice:1", CancellationToken.None)).Found.Should().BeFalse();
        (await cache.GetAsync<string>("customer:1", CancellationToken.None)).Found.Should().BeFalse();
    }

    [Fact]
    public async Task Every_call_honours_a_token_that_is_already_cancelled()
    {
        var cache = Store.InMemory();

        using var cancellation = new CancellationTokenSource();

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await cache.GetAsync<string>("invoice:1", cancellation.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await cache.SetAsync("invoice:1", "one", Minute, cancellation.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await cache.RemoveAsync("invoice:1", cancellation.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await cache.RemoveByPrefixAsync("invoice:", cancellation.Token));
    }
}
