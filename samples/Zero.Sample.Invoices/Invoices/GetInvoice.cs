using IQOne.Zero;
using IQOne.Zero.Messaging;
using IQOne.Zero.Web;

namespace Zero.Sample.Invoices;

/// <summary>Reads one invoice.</summary>
/// <param name="Id">Which invoice.</param>
[Get("/invoices/{id:int}", Tag = "Invoices", AllowAnonymous = true)]
public sealed record GetInvoice(int Id) : IQuery<InvoiceModel>;

/// <summary>Reads every invoice, soonest due first.</summary>
[Get("/invoices", Tag = "Invoices", AllowAnonymous = true)]
public sealed record ListInvoices : IQuery<IReadOnlyList<InvoiceModel>>;

/// <summary>Serves <see cref="GetInvoice"/>.</summary>
/// <param name="store">Where the invoices are.</param>
public sealed class GetInvoiceHandler(IInvoiceStore store) : IQueryHandler<GetInvoice, InvoiceModel>
{
    /// <inheritdoc />
    public Task<Result<InvoiceModel>> HandleAsync(GetInvoice query, CancellationToken cancellationToken)
        // Nothing here mentions HTTP. NotFound becomes a 404 at the edge, and the same
        // handler would serve a queue or a job unchanged.
        => Task.FromResult<Result<InvoiceModel>>(store.Find(query.Id) is { } invoice
            ? invoice.ToModel()
            : Error.NotFound("invoice.missing", $"No invoice with id {query.Id}."));
}

/// <summary>Serves <see cref="ListInvoices"/>.</summary>
/// <param name="store">Where the invoices are.</param>
public sealed class ListInvoicesHandler(IInvoiceStore store)
    : IQueryHandler<ListInvoices, IReadOnlyList<InvoiceModel>>
{
    /// <inheritdoc />
    public Task<Result<IReadOnlyList<InvoiceModel>>> HandleAsync(
        ListInvoices query, CancellationToken cancellationToken)
        => Task.FromResult<Result<IReadOnlyList<InvoiceModel>>>(
            store.All().Select(i => i.ToModel()).ToArray());
}
