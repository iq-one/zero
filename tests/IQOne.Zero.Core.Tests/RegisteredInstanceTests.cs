using IQOne.Zero.App;
using IQOne.Zero.App.Steps;
using IQOne.Zero.DependencyInjection.Descriptors;
using IQOne.Zero.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Tests;

/// <summary>
/// Startup step discovery reads instances out of the service collection before a provider
/// exists. A registration made by type used to be dropped silently, which is what let a
/// generated startup step compile, register, pass validation and never run.
/// </summary>
public class RegisteredInstanceTests
{
    private sealed class CountingStep : IApplicationConfigureServicesStep
    {
        public int Ran { get; private set; }

        public Task OnConfigureServicesAsync(IServiceCollection services, CancellationToken cancellationToken)
        {
            Ran++;

            return Task.CompletedTask;
        }
    }

    private sealed class NeedsADependency(string name) : IApplicationConfigureServicesStep
    {
        public string Name { get; } = name;

        public Task OnConfigureServicesAsync(IServiceCollection services, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private interface INotEarly;

    private sealed class NotEarly : INotEarly;

    [Fact]
    public void A_registration_made_by_type_is_constructed_rather_than_dropped()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IApplicationConfigureServicesStep, CountingStep>();

        services.GetRegisteredInstances<IApplicationConfigureServicesStep>()
            .Should().ContainSingle().Which.Should().BeOfType<CountingStep>();
    }

    [Fact]
    public void The_same_object_is_returned_on_every_read()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IApplicationConfigureServicesStep, CountingStep>();

        var first = services.GetRegisteredInstances<IApplicationConfigureServicesStep>().Single();
        var second = services.GetRegisteredInstances<IApplicationConfigureServicesStep>().Single();

        second.Should().BeSameAs(first,
            "a step that records what it did in one phase has to be the same object in the next");
    }

    [Fact]
    public void The_container_hands_back_the_object_that_was_produced()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IApplicationConfigureServicesStep, CountingStep>();

        var discovered = services.GetRegisteredInstances<IApplicationConfigureServicesStep>().Single();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IApplicationConfigureServicesStep>().Should().BeSameAs(discovered);
    }

    [Fact]
    public void A_factory_registration_is_read_without_a_provider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IApplicationConfigureServicesStep>(_ => new CountingStep());

        services.GetRegisteredInstances<IApplicationConfigureServicesStep>().Should().ContainSingle();
    }

    [Fact]
    public void A_factory_that_reaches_for_a_service_says_which_one()
    {
        var services = new ServiceCollection();

        // The provider does not exist yet, so the factory is handed one that answers null.
        // Before, it was handed null itself and the failure was a NullReferenceException
        // thrown from inside the framework.
        services.AddSingleton<IApplicationConfigureServicesStep>(
            provider => new NeedsADependency(provider.GetRequiredService<string>()));

        var read = () => services.GetRegisteredInstances<IApplicationConfigureServicesStep>();

        read.Should().Throw<InvalidOperationException>().WithMessage("*String*");
    }

    [Fact]
    public void A_step_that_cannot_be_built_early_says_so()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IApplicationConfigureServicesStep, NeedsADependency>();

        var read = () => services.GetRegisteredInstances<IApplicationConfigureServicesStep>();

        read.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(ISingletonInstance)}*")
            .WithMessage("*parameterless constructor*");
    }

    [Fact]
    public void A_service_that_makes_no_such_promise_is_left_to_the_provider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<INotEarly, NotEarly>();

        services.GetRegisteredInstance<INotEarly>().Should().BeNull();
    }

    [Fact]
    public void The_required_form_distinguishes_missing_from_unreadable()
    {
        var services = new ServiceCollection();

        services.AddSingleton<INotEarly, NotEarly>();

        var unreadable = () => services.GetRequiredRegisteredInstance<INotEarly>();

        // The old message said the service was not registered. It was.
        unreadable.Should().Throw<InvalidOperationException>()
            .WithMessage("*is registered*")
            .WithMessage("*before the service provider is built*");

        var missing = () => services.GetRequiredRegisteredInstance<IApplication>();

        missing.Should().Throw<InvalidOperationException>().WithMessage("No service is registered*");
    }

    [Fact]
    public void An_instance_registration_is_returned_untouched()
    {
        var services = new ServiceCollection();
        var step = new CountingStep();

        services.AddSingleton<IApplicationConfigureServicesStep>(step);

        services.GetRegisteredInstance<IApplicationConfigureServicesStep>().Should().BeSameAs(step);
    }

    [Fact]
    public void A_predicate_covers_the_searches_the_typed_overloads_do_not()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IApplicationConfigureServicesStep, CountingStep>();

        services
            .GetRegisteredInstances(d => typeof(IApplicationStep).IsAssignableFrom(d.ServiceType))
            .Should().ContainSingle();
    }
}
