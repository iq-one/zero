namespace Zero.Sample.Invoices;

/// <summary>An invoice, as this sample models one.</summary>
/// <param name="Id">Its identity.</param>
/// <param name="Reference">The customer-visible reference.</param>
/// <param name="Amount">What is owed.</param>
/// <param name="Due">When it is owed by.</param>
/// <param name="IsPaid">Whether it has been settled.</param>
public sealed record Invoice(int Id, string Reference, decimal Amount, DateOnly Due, bool IsPaid);

/// <summary>What the API returns for an invoice.</summary>
/// <param name="Id">Its identity.</param>
/// <param name="Reference">The customer-visible reference.</param>
/// <param name="Amount">What is owed.</param>
/// <param name="Due">When it is owed by.</param>
/// <param name="IsPaid">Whether it has been settled.</param>
public sealed record InvoiceModel(int Id, string Reference, decimal Amount, DateOnly Due, bool IsPaid);

/// <summary>Turns the stored shape into the published one.</summary>
public static class InvoiceMapping
{
    /// <summary>The model an API caller sees.</summary>
    /// <param name="invoice">The stored invoice.</param>
    /// <returns>The published shape.</returns>
    public static InvoiceModel ToModel(this Invoice invoice)
        => new(invoice.Id, invoice.Reference, invoice.Amount, invoice.Due, invoice.IsPaid);
}
