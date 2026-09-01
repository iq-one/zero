using IQOne.Zero.Messaging;
using IQOne.Zero.Modules;
using IQOne.Zero.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Testing.Tests;

/// <summary>
/// A small application to test the testing package against: two requests, a handler with a
/// dependency, a validator, and a module that wires them the way generated code does.
/// </summary>
internal sealed record CreateInvoice(string Reference, decimal Amount) : ICommand<int>;

internal sealed record GetInvoice(int Id) : IQuery<InvoiceModel>;

internal sealed record CloseInvoice(int Id) : ICommand;

internal sealed record NobodyHandlesThis : ICommand;

internal sealed record InvoiceModel(int Id, decimal Total);

internal interface IInvoiceStore
{
    int Add(string reference, decimal amount);

    InvoiceModel? Find(int id);
}

internal sealed class InMemoryInvoiceStore : IInvoiceStore
{
    private readonly Dictionary<int, InvoiceModel> _invoices = [];

    public int Add(string reference, decimal amount)
    {
        var id = _invoices.Count + 1;

        _invoices[id] = new InvoiceModel(id, amount);

        return id;
    }

    public InvoiceModel? Find(int id) => _invoices.GetValueOrDefault(id);
}

internal sealed class CreateInvoiceHandler(IInvoiceStore store) : ICommandHandler<CreateInvoice, int>
{
    public Task<Result<int>> HandleAsync(CreateInvoice command, CancellationToken cancellationToken)
        => Task.FromResult<Result<int>>(store.Add(command.Reference, command.Amount));
}

internal sealed class GetInvoiceHandler(IInvoiceStore store) : IQueryHandler<GetInvoice, InvoiceModel>
{
    public Task<Result<InvoiceModel>> HandleAsync(GetInvoice query, CancellationToken cancellationToken)
        => Task.FromResult<Result<InvoiceModel>>(store.Find(query.Id) is { } invoice
            ? invoice
            : Error.NotFound("invoice.missing", $"No invoice with id {query.Id}."));
}

internal sealed class CreateInvoiceValidator : Validator<CreateInvoice>
{
    protected override void Configure(RuleSet<CreateInvoice> rules)
    {
        rules.NotEmpty(x => x.Reference, "invoice.reference");
        rules.InRange(x => x.Amount, "invoice.amount", 0.01m, 1_000_000m);
    }
}

/// <summary>A validator that takes a dependency, so the container has to build it.</summary>
internal sealed class UniqueReferenceValidator(IInvoiceStore store) : Validator<CreateInvoice>
{
    protected override void Configure(RuleSet<CreateInvoice> rules)
        => rules.Must(
            x => store.Find(1) is null || x.Reference != "DUPLICATE",
            "invoice.reference.taken",
            "That reference is already in use.");
}

/// <summary>Registers its handlers exactly as a generated module does.</summary>
internal sealed class InvoiceModule : IModule, IModuleConfigureServicesStep
{
    public string Name => "Invoices";

    public ValueTask OnConfigureServicesAsync(IModuleServiceContext context, CancellationToken cancellationToken)
    {
        context.Services.AddScoped<IRequestHandler<GetInvoice, InvoiceModel>, GetInvoiceHandler>();

        context.Requests().Add(new RequestEntry(
            typeof(GetInvoice), typeof(InvoiceModel), typeof(GetInvoiceHandler),
            static (services, request, cancellationToken) => RequestPipeline
                .RunAsync<GetInvoice, InvoiceModel>((GetInvoice)request, services, cancellationToken)));

        return default;
    }
}

/// <summary>A module that declares a request and forgets to handle it.</summary>
internal sealed class HalfBuiltModule : IModule, IModuleConfigureServicesStep
{
    public string Name => "HalfBuilt";

    public ValueTask OnConfigureServicesAsync(IModuleServiceContext context, CancellationToken cancellationToken)
    {
        context.Requests().Declare(typeof(NobodyHandlesThis));

        return default;
    }
}

/// <summary>A singleton that takes a scoped dependency: the mistake ValidateScopes exists for.</summary>
internal sealed class CaptiveHolder(IInvoiceStore store)
{
    public IInvoiceStore Store { get; } = store;
}

/// <summary>Registered as an open generic, so it wraps requests it was never told about.</summary>
internal sealed class EveryRequestBehavior<TRequest, TResponse>(IList<string> log)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public Task<Result<TResponse>> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        log.Add(typeof(TRequest).Name);

        return next();
    }
}

/// <summary>Scoped, and identifiable, so a test can tell one scope from the next.</summary>
internal sealed class ScopeMarker
{
    public Guid Id { get; } = Guid.NewGuid();
}

internal sealed class ScopeMarkingHandler(ScopeMarker marker, IList<Guid> seen) : ICommandHandler<CloseInvoice>
{
    public Task<Result<Unit>> HandleAsync(CloseInvoice command, CancellationToken cancellationToken)
    {
        seen.Add(marker.Id);

        return Task.FromResult(Unit.Success);
    }
}
