using IQOne.Zero.Messaging;
using IQOne.Zero.Results;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Messaging.Tests;

/// <summary>
/// The dispatch table is normally generated. These build it by hand, exactly as the
/// generated code does, so the runtime half can be tested without a compilation.
/// </summary>
public class SenderTests
{
    private static ServiceProvider Build(
        Action<IServiceCollection>? configure = null, params Type[] withoutHandlerFor)
    {
        var services = new ServiceCollection();

        services.AddScoped<IQueryHandler<Greet, string>, GreetHandler>();
        services.AddScoped<IRequestHandler<Greet, string>>(sp => sp.GetRequiredService<IQueryHandler<Greet, string>>());
        services.AddScoped<IRequestHandler<Fail, Unit>, FailHandler>();

        var registry = new RequestRegistry();

        registry.Add(new RequestEntry(typeof(Greet), typeof(string), typeof(GreetHandler),
            static (sp, request, ct) => RequestPipeline.RunAsync<Greet, string>((Greet)request, sp, ct)));

        registry.Add(new RequestEntry(typeof(Fail), typeof(Unit), typeof(FailHandler),
            static (sp, request, ct) => RequestPipeline.RunAsync<Fail, Unit>((Fail)request, sp, ct)));

        foreach (var type in withoutHandlerFor) registry.Declare(type);

        registry.Freeze();

        services.AddSingleton(registry);
        services.AddScoped<ISender, Sender>();

        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task A_request_reaches_its_handler()
    {
        var sender = Build().GetRequiredService<ISender>();

        var result = await sender.SendAsync(new Greet("Zero"), CancellationToken.None);

        result.Value.Should().Be("Hello, Zero.");
    }

    [Fact]
    public async Task A_handler_s_failure_comes_back_as_a_failure_not_an_exception()
    {
        var sender = Build().GetRequiredService<ISender>();

        var result = await sender.SendAsync(new Fail(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(FailHandler.Refused);
    }

    [Fact]
    public async Task A_request_with_no_handler_says_which_one()
    {
        var sender = Build().GetRequiredService<ISender>();

        var send = async () => await sender.SendAsync(new Unhandled(), CancellationToken.None);

        (await send.Should().ThrowAsync<MissingRequestHandlerException>())
            .Which.RequestType.Should().Be<Unhandled>();
    }

    [Fact]
    public void A_second_handler_for_one_request_is_refused_and_names_both()
    {
        var registry = new RequestRegistry();

        registry.Add(new RequestEntry(typeof(Greet), typeof(string), typeof(GreetHandler),
            static (sp, r, ct) => RequestPipeline.RunAsync<Greet, string>((Greet)r, sp, ct)));

        var again = () => registry.Add(new RequestEntry(typeof(Greet), typeof(string), typeof(FailHandler),
            static (sp, r, ct) => RequestPipeline.RunAsync<Greet, string>((Greet)r, sp, ct)));

        again.Should().Throw<InvalidOperationException>()
            .WithMessage("*GreetHandler*FailHandler*");
    }

    [Fact]
    public void The_table_reports_a_declared_request_that_nobody_handles()
    {
        var registry = new RequestRegistry();

        registry.Declare(typeof(Unhandled));
        registry.Add(new RequestEntry(typeof(Greet), typeof(string), typeof(GreetHandler),
            static (sp, r, ct) => RequestPipeline.RunAsync<Greet, string>((Greet)r, sp, ct)));

        registry.Unhandled.Should().Equal(typeof(Unhandled));
    }

    [Fact]
    public void A_frozen_table_refuses_a_late_registration()
    {
        var registry = new RequestRegistry().Freeze();

        var add = () => registry.Add(new RequestEntry(typeof(Greet), typeof(string), typeof(GreetHandler),
            static (sp, r, ct) => RequestPipeline.RunAsync<Greet, string>((Greet)r, sp, ct)));

        add.Should().Throw<InvalidOperationException>().WithMessage("*frozen*");
    }
}
