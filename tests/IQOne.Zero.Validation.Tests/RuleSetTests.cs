using System.Text.RegularExpressions;
using IQOne.Zero.Validation;

namespace IQOne.Zero.Validation.Tests;

internal sealed record Invoice(string? Reference, decimal Amount, DateOnly Issued, DateOnly Due, string[]? Lines);

public class RuleSetTests
{
    private static readonly DateOnly Today = new(2026, 1, 1);

    private static Invoice Valid => new("INV-001", 100m, Today, Today.AddDays(30), ["a"]);

    private static async Task<IReadOnlyList<Error>> Run(Action<RuleSet<Invoice>> configure, Invoice invoice)
    {
        var rules = new RuleSet<Invoice>();
        configure(rules);

        return await rules.RunAsync(invoice, CancellationToken.None);
    }

    [Fact]
    public async Task A_valid_value_produces_no_errors()
    {
        var errors = await Run(r => r.NotEmpty(x => x.Reference, "reference"), Valid);

        errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NotEmpty_rejects_null_empty_and_whitespace(string? reference)
    {
        var errors = await Run(r => r.NotEmpty(x => x.Reference, "reference"), Valid with { Reference = reference });

        errors.Should().ContainSingle().Which.Code.Should().Be("reference");
    }

    [Fact]
    public async Task Every_failing_rule_is_reported_not_only_the_first()
    {
        var errors = await Run(
            r => r.NotEmpty(x => x.Reference, "reference")
                  .InRange(x => x.Amount, "amount", 1m, 100m)
                  .Must(x => x.Due > x.Issued, "due", "The due date must be after the issue date."),
            new Invoice(null, 0m, Today, Today, null));

        errors.Select(e => e.Code).Should().Equal("reference", "amount", "due");
    }

    [Fact]
    public async Task Every_error_is_classified_as_validation_so_the_transport_can_map_it()
    {
        var errors = await Run(r => r.NotEmpty(x => x.Reference, "reference"), Valid with { Reference = null });

        errors.Should().AllSatisfy(e => e.Kind.Should().Be(ErrorKind.Validation));
    }

    [Fact]
    public async Task Length_passes_a_null_so_it_composes_with_NotEmpty()
    {
        var errors = await Run(r => r.Length(x => x.Reference, "reference", 3, 8), Valid with { Reference = null });

        errors.Should().BeEmpty("a missing value is NotEmpty's business, not Length's");
    }

    [Fact]
    public async Task Length_rejects_text_outside_the_range()
    {
        var errors = await Run(r => r.Length(x => x.Reference, "reference", 3, 8), Valid with { Reference = "ab" });

        errors.Should().ContainSingle();
    }

    [Fact]
    public async Task Matches_rejects_text_that_does_not_fit_the_pattern()
    {
        var pattern = new Regex("^INV-[0-9]{3}$");

        (await Run(r => r.Matches(x => x.Reference, "reference", pattern), Valid)).Should().BeEmpty();

        (await Run(r => r.Matches(x => x.Reference, "reference", pattern), Valid with { Reference = "nope" }))
            .Should().ContainSingle();
    }

    [Fact]
    public async Task NotEmpty_on_a_collection_rejects_null_and_empty()
    {
        (await Run(r => r.NotEmpty(x => x.Lines, "lines"), Valid with { Lines = null })).Should().ContainSingle();
        (await Run(r => r.NotEmpty(x => x.Lines, "lines"), Valid with { Lines = [] })).Should().ContainSingle();
        (await Run(r => r.NotEmpty(x => x.Lines, "lines"), Valid)).Should().BeEmpty();
    }

    [Fact]
    public async Task When_applies_its_rules_only_while_the_condition_holds()
    {
        void Configure(RuleSet<Invoice> rules)
            => rules.When(x => x.Amount > 1000m, inner => inner.NotEmpty(x => x.Lines, "lines"));

        (await Run(Configure, new Invoice("x", 10m, Today, Today, null)))
            .Should().BeEmpty("the condition does not hold");

        (await Run(Configure, new Invoice("x", 5000m, Today, Today, null)))
            .Should().ContainSingle("the condition holds and the nested rule fails");
    }

    [Fact]
    public async Task When_does_not_leak_its_condition_into_later_rules()
    {
        var errors = await Run(
            r => r.When(x => false, inner => inner.NotEmpty(x => x.Reference, "inside"))
                  .NotEmpty(x => x.Reference, "outside"),
            Valid with { Reference = null });

        errors.Select(e => e.Code).Should().Equal("outside");
    }

    [Fact]
    public async Task MustAsync_can_reach_a_dependency()
    {
        var errors = await Run(
            r => r.MustAsync((x, _) => new ValueTask<bool>(false), "taken", "That reference is already in use."),
            Valid);

        errors.Should().ContainSingle().Which.Message.Should().Contain("already in use");
    }
}
