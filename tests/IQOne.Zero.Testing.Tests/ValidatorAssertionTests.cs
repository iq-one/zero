namespace IQOne.Zero.Testing.Tests;

/// <summary>
/// A rule per test is the normal shape of a validator's test suite, so the two assertions
/// that shape needs have to be short to write and clear when they fail.
/// </summary>
public class ValidatorAssertionTests
{
    private static readonly CreateInvoiceValidator Validator = new();

    [Fact]
    public Task An_acceptable_value_passes()
        => Validator.ShouldAcceptAsync(new CreateInvoice("INV-001", 250m));

    [Fact]
    public async Task ShouldAccept_lists_the_rules_that_fired()
    {
        var assert = async () => await Validator.ShouldAcceptAsync(new CreateInvoice("", 0m));

        (await assert.Should().ThrowAsync<ZeroAssertionException>())
            .Which.Message.Should()
            .Contain("Expected CreateInvoiceValidator to accept")
            .And.Contain("invoice.reference")
            .And.Contain("invoice.amount");
    }

    [Fact]
    public async Task ShouldReject_hands_back_every_reason()
    {
        var errors = await Validator.ShouldRejectAsync(new CreateInvoice("", 0m));

        errors.Select(error => error.Code).Should().Equal("invoice.reference", "invoice.amount");
    }

    [Fact]
    public async Task ShouldReject_with_a_code_hands_back_that_error()
    {
        var error = await Validator.ShouldRejectAsync(new CreateInvoice("INV-001", 0m), "invoice.amount");

        error.Kind.Should().Be(ErrorKind.Validation);
    }

    [Fact]
    public async Task ShouldReject_with_a_code_names_the_codes_that_fired_instead()
    {
        var assert = async () =>
            await Validator.ShouldRejectAsync(new CreateInvoice("", 250m), "invoice.amount");

        (await assert.Should().ThrowAsync<ZeroAssertionException>())
            .Which.Message.Should()
            .Contain("with error code 'invoice.amount'")
            .And.Contain("'invoice.reference'");
    }

    [Fact]
    public async Task ShouldReject_says_so_when_nothing_was_wrong()
    {
        var assert = async () => await Validator.ShouldRejectAsync(new CreateInvoice("INV-001", 250m));

        (await assert.Should().ThrowAsync<ZeroAssertionException>())
            .Which.Message.Should().Contain("it found nothing wrong");
    }

    [Fact]
    public async Task A_validator_with_a_dependency_is_tested_the_same_way()
    {
        var store = new InMemoryInvoiceStore();
        store.Add("INV-001", 250m);

        var validator = new UniqueReferenceValidator(store);

        await validator.ShouldRejectAsync(new CreateInvoice("DUPLICATE", 250m), "invoice.reference.taken");
        await validator.ShouldAcceptAsync(new CreateInvoice("INV-002", 250m));
    }
}
