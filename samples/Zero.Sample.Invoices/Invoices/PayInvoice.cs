using IQOne.Zero;
using IQOne.Zero.Messaging;
using IQOne.Zero.Web;

namespace Zero.Sample.Invoices;

/// <summary>Settles an invoice.</summary>
/// <param name="Id">Which invoice.</param>
[Post("/invoices/{id:int}/pay", Tag = "Invoices", AllowAnonymous = true)]
public sealed record PayInvoice(int Id) : ICommand;

/// <summary>Serves <see cref="PayInvoice"/>.</summary>
/// <param name="store">Where the invoices are.</param>
public sealed class PayInvoiceHandler(IInvoiceStore store) : ICommandHandler<PayInvoice>
{
    /// <inheritdoc />
    public Task<Result<Unit>> HandleAsync(PayInvoice command, CancellationToken cancellationToken)
    {
        if (store.Find(command.Id) is not { } invoice)
            return Task.FromResult<Result<Unit>>(
                Error.NotFound("invoice.missing", $"No invoice with id {command.Id}."));

        if (invoice.IsPaid)
            return Task.FromResult<Result<Unit>>(
                Error.Conflict("invoice.paid", "That invoice has already been settled."));

        store.MarkPaid(command.Id);

        // A command that produces nothing answers 204.
        return Task.FromResult(Unit.Success);
    }
}
