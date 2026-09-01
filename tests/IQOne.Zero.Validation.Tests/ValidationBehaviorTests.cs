using IQOne.Zero.Messaging;
using IQOne.Zero.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Validation.Tests;

internal sealed record Register(string Email, int Age) : ICommand<string>;

internal sealed class RegisterHandler : ICommandHandler<Register, string>
{
    public static bool Ran { get; set; }

    public Task<Result<string>> HandleAsync(Register command, CancellationToken cancellationToken)
    {
        Ran = true;
        return Task.FromResult(Result<string>.Success(command.Email));
    }
}

internal sealed class EmailValidator : Validator<Register>
{
    protected override void Configure(RuleSet<Register> rules)
        => rules.NotEmpty(x => x.Email, "register.email");
}

internal sealed class AgeValidator : Validator<Register>
{
    protected override void Configure(RuleSet<Register> rules)
        => rules.InRange(x => x.Age, "register.age", 18, 120);
}

/// <summary>
/// Validation is only worth having if it cannot be skipped, so these drive it through the
/// real pipeline rather than calling a validator directly.
/// </summary>
public class ValidationBehaviorTests
{
    private static ISender Sender(params IValidator<Register>[] validators)
    {
        RegisterHandler.Ran = false;

        var services = new ServiceCollection();

        services.AddScoped<IRequestHandler<Register, string>, RegisterHandler>();
        services.AddZeroValidation();

        foreach (var validator in validators) services.AddScoped(_ => validator);

        services.AddZeroMessagingWithRequests(requests => requests.Add(new RequestEntry(
            typeof(Register), typeof(string), typeof(RegisterHandler),
            static (sp, r, ct) => RequestPipeline.RunAsync<Register, string>((Register)r, sp, ct))));

        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    [Fact]
    public async Task An_acceptable_request_reaches_the_handler()
    {
        var result = await Sender(new EmailValidator())
            .SendAsync(new Register("a@b.c", 30), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        RegisterHandler.Ran.Should().BeTrue();
    }

    [Fact]
    public async Task An_unacceptable_request_never_reaches_the_handler()
    {
        var result = await Sender(new EmailValidator())
            .SendAsync(new Register("", 30), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        RegisterHandler.Ran.Should().BeFalse("the handler must not run on input that was rejected");
    }

    [Fact]
    public async Task Every_validator_for_the_request_runs_and_their_failures_are_reported_together()
    {
        var result = await Sender(new EmailValidator(), new AgeValidator())
            .SendAsync(new Register("", 5), CancellationToken.None);

        result.Errors.Select(e => e.Code).Should().BeEquivalentTo(["register.email", "register.age"]);
    }

    [Fact]
    public async Task With_no_validator_registered_the_request_passes_through()
    {
        var result = await Sender().SendAsync(new Register("", 0), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        RegisterHandler.Ran.Should().BeTrue();
    }

    [Fact]
    public void The_behaviour_sits_between_authorization_and_caching()
    {
        var behavior = new ValidationBehavior<Register, string>([]);

        behavior.Order.Should().Be(BehaviorOrder.Validation);
        behavior.Order.Should().BeGreaterThan(BehaviorOrder.Authorization);
        behavior.Order.Should().BeLessThan(BehaviorOrder.Caching);
    }
}
