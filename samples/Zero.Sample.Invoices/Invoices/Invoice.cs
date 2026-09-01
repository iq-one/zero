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
