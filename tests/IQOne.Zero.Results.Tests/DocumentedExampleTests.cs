namespace IQOne.Zero.Results.Tests;

/// <summary>
/// The examples this package ships, compiled and run.
/// </summary>
/// <remarks>
/// The flagship chain in <c>rules/errors-are-values.md</c> and in <c>zero/capability.json</c>
/// did not compile: there was no <c>Ensure</c> on <c>Task&lt;Result&lt;T&gt;&gt;</c>, so the
/// first composition an agent or a reader would copy was CS1929. Guidance that does not
/// compile is worse than none, because it is what gets copied verbatim.
/// </remarks>
public class DocumentedExampleTests
{
    private sealed record Invoice(int Id, bool IsOpen)
    {
        public InvoiceModel ToModel() => new(Id);
    }

    private sealed record InvoiceModel(int Id);

    private static async Task<Result<Invoice>> GetAsync(int id, CancellationToken cancellationToken)
    {
        await Task.Yield();

        return id switch
        {
            1 => new Invoice(1, IsOpen: true),
            2 => new Invoice(2, IsOpen: false),
            _ => Error.NotFound("invoice.missing", $"No invoice with id {id}.")
        };
    }

    /// <summary>The example, verbatim.</summary>
    private static Task<Result<InvoiceModel>> Documented(int id, CancellationToken cancellationToken)
        => GetAsync(id, cancellationToken)
            .Ensure(i => i.IsOpen, Error.Conflict("invoice.closed", "Already closed."))
            .Map(i => i.ToModel());

    [Fact]
    public async Task The_composition_example_runs_the_whole_chain_on_a_success()
        => (await Documented(1, CancellationToken.None)).Value.Id.Should().Be(1);

    [Fact]
    public async Task The_composition_example_stops_at_the_step_that_fails()
        => (await Documented(2, CancellationToken.None)).Error.Code.Should().Be("invoice.closed");

    [Fact]
    public async Task The_composition_example_never_starts_when_the_first_step_failed()
        => (await Documented(99, CancellationToken.None)).Error.Code.Should().Be("invoice.missing");

    [Fact]
    public async Task The_longer_chain_in_the_rule_file_also_compiles_and_runs()
    {
        static Task<Result<Invoice>> ApplyAsync(Invoice invoice, decimal payment, CancellationToken cancellationToken)
            => Task.FromResult(Result<Invoice>.Success(invoice));

        var cancellationToken = CancellationToken.None;

        var model = await GetAsync(1, cancellationToken)
            .Ensure(i => i.IsOpen, Error.Conflict("invoice.closed", "This invoice is already closed."))
            .Bind(i => ApplyAsync(i, 10m, cancellationToken))
            .Map(i => i.ToModel());

        model.Value.Id.Should().Be(1);
    }

    [Fact]
    public async Task The_way_the_rule_file_says_to_read_an_outcome_compiles()
    {
        var read = await GetAsync(1, CancellationToken.None)
            .Match(invoice => $"ok {invoice.Id}", errors => $"bad {errors}");

        read.Should().Be("ok 1");
    }
}
