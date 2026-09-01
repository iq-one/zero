using IQOne.Zero.DependencyInjection.Descriptors;

namespace Zero.Sample.Invoices;

/// <summary>Where this sample keeps its invoices.</summary>
/// <remarks>
/// In memory, so the sample runs with nothing installed. A real application would take
/// <c>IRepository&lt;Invoice&gt;</c> from <c>IQOne.Zero.Persistence</c> instead; the handlers
/// would not change.
/// </remarks>
public interface IInvoiceStore : ISingleton
{
    /// <summary>The invoice with this id, or null.</summary>
    /// <param name="id">The identity to look for.</param>
    Invoice? Find(int id);

    /// <summary>Every invoice, oldest due date first.</summary>
    IReadOnlyList<Invoice> All();

    /// <summary>Whether a reference is already taken.</summary>
    /// <param name="reference">The reference to check.</param>
    bool IsReferenceTaken(string reference);

    /// <summary>Stores a new invoice and returns its id.</summary>
    /// <param name="reference">The customer-visible reference.</param>
    /// <param name="amount">What is owed.</param>
    /// <param name="due">When it is owed by.</param>
    int Add(string reference, decimal amount, DateOnly due);

    /// <summary>Marks an invoice as settled.</summary>
    /// <param name="id">The invoice to settle.</param>
    void MarkPaid(int id);
}

/// <inheritdoc />
public sealed class InvoiceStore : IInvoiceStore
{
    private readonly Dictionary<int, Invoice> _invoices = [];
    private readonly Lock _gate = new();

    private int _next = 1;

    /// <inheritdoc />
    public Invoice? Find(int id)
    {
        lock (_gate) return _invoices.GetValueOrDefault(id);
    }

    /// <inheritdoc />
    public IReadOnlyList<Invoice> All()
    {
        lock (_gate) return [.. _invoices.Values.OrderBy(i => i.Due)];
    }

    /// <inheritdoc />
    public bool IsReferenceTaken(string reference)
    {
        lock (_gate) return _invoices.Values.Any(i => i.Reference == reference);
    }

    /// <inheritdoc />
    public int Add(string reference, decimal amount, DateOnly due)
    {
        lock (_gate)
        {
            var id = _next++;
            _invoices[id] = new Invoice(id, reference, amount, due, IsPaid: false);

            return id;
        }
    }

    /// <inheritdoc />
    public void MarkPaid(int id)
    {
        lock (_gate)
        {
            if (_invoices.TryGetValue(id, out var invoice)) _invoices[id] = invoice with { IsPaid = true };
        }
    }
}
