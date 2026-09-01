using IQOne.Zero.Messaging;

namespace IQOne.Zero.Testing.Tests;

/// <summary>
/// The fake sender is handed to code under test, so what it records has to be assertable and
/// what it refuses has to explain itself. Nothing here builds a container: that is the point.
/// </summary>
public class FakeSenderTests
{
    /// <summary>Stands in for the sort of service a consumer would put under test.</summary>
    private sealed class InvoiceService(ISender sender)
    {
        public async Task<Result<int>> CreateAndCloseAsync(string reference, decimal amount)
        {
            var created = await sender.SendAsync(new CreateInvoice(reference, amount), CancellationToken.None);

            if (created.IsFailure) return created;

            var closed = await sender.SendAsync(new CloseInvoice(created.Value), CancellationToken.None);

            return closed.IsFailure ? Result<int>.Failure(closed.Errors) : created;
        }
    }

    [Fact]
    public async Task It_returns_the_outcome_scripted_for_each_request_type()
    {
        var sender = new FakeSender()
            .Returns<CreateInvoice, int>(42)
            .Succeeds<CloseInvoice>();

        var result = await new InvoiceService(sender).CreateAndCloseAsync("INV-001", 250m);

        result.ShouldHaveValue(42);
    }

    [Fact]
    public async Task An_outcome_may_be_derived_from_the_request()
    {
        var sender = new FakeSender().Returns<CreateInvoice, int>(command => command.Reference.Length);

        var result = await sender.SendAsync(new CreateInvoice("INV-001", 250m), CancellationToken.None);

        result.ShouldHaveValue(7);
    }

    [Fact]
    public async Task A_scripted_failure_comes_back_as_a_failure()
    {
        var sender = new FakeSender()
            .Fails<CreateInvoice, int>(Error.Conflict("invoice.duplicate", "That reference is taken."));

        var result = await new InvoiceService(sender).CreateAndCloseAsync("INV-001", 250m);

        result.ShouldFailWith("invoice.duplicate");
        sender.ShouldNotHaveSent<CloseInvoice>();
    }

    [Fact]
    public async Task It_records_what_was_sent_in_order()
    {
        var sender = new FakeSender().Returns<CreateInvoice, int>(42).Succeeds<CloseInvoice>();

        await new InvoiceService(sender).CreateAndCloseAsync("INV-001", 250m);

        sender.Sent.Select(request => request.GetType()).Should().Equal(typeof(CreateInvoice), typeof(CloseInvoice));
        sender.ShouldHaveSent<CreateInvoice>().Reference.Should().Be("INV-001");
        sender.ShouldHaveSent<CloseInvoice>(request => request.Id == 42);
    }

    [Fact]
    public async Task ShouldHaveSent_counts_what_went_through()
    {
        var sender = new FakeSender().Succeeds<CloseInvoice>();

        await sender.SendAsync(new CloseInvoice(1), CancellationToken.None);
        await sender.SendAsync(new CloseInvoice(2), CancellationToken.None);

        sender.ShouldHaveSent<CloseInvoice>(times: 2);
        sender.SentOf<CloseInvoice>().Select(request => request.Id).Should().Equal(1, 2);
    }

    [Fact]
    public void ShouldHaveSent_says_nothing_was_sent_when_nothing_was()
    {
        var assert = () => new FakeSender().ShouldHaveSent<CreateInvoice>();

        assert.Should().Throw<ZeroAssertionException>()
            .Which.Message.Should()
            .Contain("Expected exactly one CreateInvoice to be sent")
            .And.Contain("Nothing was sent.");
    }

    [Fact]
    public async Task ShouldHaveSent_lists_what_was_sent_when_nothing_matched()
    {
        var sender = new FakeSender().Returns<CreateInvoice, int>(42);

        await sender.SendAsync(new CreateInvoice("INV-001", 250m), CancellationToken.None);

        var assert = () => sender.ShouldHaveSent<CreateInvoice>(request => request.Amount > 1000m);

        assert.Should().Throw<ZeroAssertionException>()
            .Which.Message.Should()
            .Contain("request => request.Amount > 1000m")
            .And.Contain("Reference = INV-001");
    }

    [Fact]
    public async Task ShouldNotHaveSent_shows_the_request_it_did_not_expect()
    {
        var sender = new FakeSender().Succeeds<CloseInvoice>();

        await sender.SendAsync(new CloseInvoice(7), CancellationToken.None);

        Action assert = sender.ShouldNotHaveSent<CloseInvoice>;

        assert.Should().Throw<ZeroAssertionException>()
            .Which.Message.Should().Contain("Expected no CloseInvoice to be sent").And.Contain("Id = 7");
    }

    [Fact]
    public async Task ShouldHaveSentNothing_holds_when_the_code_under_test_did_nothing()
    {
        var sender = new FakeSender().Succeeds<CloseInvoice>();

        sender.ShouldHaveSentNothing();

        await sender.SendAsync(new CloseInvoice(7), CancellationToken.None);

        Action assert = sender.ShouldHaveSentNothing;

        assert.Should().Throw<ZeroAssertionException>();
    }

    [Fact]
    public async Task An_unscripted_request_says_what_to_script()
    {
        var send = async () => await new FakeSender().SendAsync(new CloseInvoice(1), CancellationToken.None);

        (await send.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("no outcome scripted")
            .And.Contain("Succeeds<CloseInvoice>()",
                "a fake that returned a silent default would let the test pass while the code did the wrong thing");
    }

    [Fact]
    public async Task An_unscripted_request_is_still_recorded_so_the_test_can_see_it()
    {
        var sender = new FakeSender();

        try
        {
            await sender.SendAsync(new CloseInvoice(1), CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // The throw is the point of the previous test; here only the record matters.
        }

        sender.ShouldHaveSent<CloseInvoice>().Id.Should().Be(1);
    }

    [Fact]
    public async Task The_scripted_delegate_sees_the_cancellation_token()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var observed = CancellationToken.None;

        var sender = new FakeSender().Returns<CloseInvoice, Unit>((_, token) =>
        {
            observed = token;
            return Task.FromResult(Unit.Success);
        });

        await sender.SendAsync(new CloseInvoice(1), cancellation.Token);

        observed.IsCancellationRequested.Should().BeTrue();
    }
}
