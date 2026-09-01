using IQOne.Zero;
using IQOne.Zero.Messaging;
using IQOne.Zero.Validation;
using IQOne.Zero.Web;

namespace Zero.Sample.Invoices;

/// <summary>Raises a new invoice.</summary>
/// <param name="Reference">The customer-visible reference.</param>
/// <param name="Amount">What is owed.</param>
/// <param name="Due">When it is owed by.</param>
[Post("/invoices", Tag = "Invoices", AllowAnonymous = true)]
public sealed record CreateInvoice(string Reference, decimal Amount, DateOnly Due) : ICommand<int>;

/// <summary>What a new invoice must look like before anything reads it.</summary>
public sealed class CreateInvoiceValidator : Validator<CreateInvoice>
{
    /// <inheritdoc />
    protected override void Configure(RuleSet<CreateInvoice> rules)
    {
        rules.NotEmpty(x => x.Reference, "invoice.reference");
        rules.Length(x => x.Reference, "invoice.reference", 3, 32);
        rules.InRange(x => x.Amount, "invoice.amount", 0.01m, 1_000_000m);
    }
}

/// <summary>A rule that has to ask the store, so it is a separate validator.</summary>
/// <param name="store">Where the invoices are.</param>
public sealed class UniqueReferenceValidator(IInvoiceStore store) : Validator<CreateInvoice>
{
    /// <inheritdoc />
    protected override void Configure(RuleSet<CreateInvoice> rules)
        // A hint, not a guarantee: two requests can pass this at the same moment. The
        // handler still has to cope, and in a real application the database constraint is
        // what actually enforces it.
        => rules.Must(
            x => !store.IsReferenceTaken(x.Reference),
            "invoice.reference.taken",
            "That reference is already in use.");
}

/// <summary>Serves <see cref="CreateInvoice"/>.</summary>
/// <param name="store">Where the invoices are.</param>
public sealed class CreateInvoiceHandler(IInvoiceStore store) : ICommandHandler<CreateInvoice, int>
{
    /// <inheritdoc />
    public Task<Result<int>> HandleAsync(CreateInvoice command, CancellationToken cancellationToken)
        // The handler does not re-check what the validators checked. It does check what
        // only it can know at this moment.
        => Task.FromResult<Result<int>>(store.IsReferenceTaken(command.Reference)
            ? Error.Conflict("invoice.reference.taken", "That reference was taken a moment ago.")
            : store.Add(command.Reference, command.Amount, command.Due));
}
