using IQOne.Zero.Caching;
using IQOne.Zero.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IQOne.Zero.Caching.Tests;

/// <summary>
/// One Add call has to be enough. A capability that needs a second one is a capability whose
/// documentation is load-bearing, and documentation is the part nobody reads.
/// </summary>
public class CachingRegistrationTests
{
    [Fact]
    public void The_entry_point_alone_makes_every_public_type_resolvable()
    {
        var services = new ServiceCollection();

        services.AddZeroCaching();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });

        provider.GetRequiredService<ICache>().Should().NotBeNull();
        provider.GetRequiredService<ICacheInvalidator>().Should().NotBeNull();
        provider.GetRequiredService<IOptions<CachingOptions>>().Value.Should().NotBeNull();

        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetServices<IPipelineBehavior<GetInvoice, string>>()
            .Should().ContainSingle().Which.Should().BeOfType<CachingBehavior<GetInvoice, string>>();
    }

    [Fact]
    public void The_defaults_are_the_ones_most_applications_would_choose()
    {
        var services = new ServiceCollection();

        services.AddZeroCaching();

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<CachingOptions>>().Value;

        options.Enabled.Should().BeTrue();
        options.DefaultLifetime.Should().Be(TimeSpan.FromMinutes(5));
        options.KeyPrefix.Should().Be("zero:");
    }

    [Fact]
    public void An_application_that_brings_its_own_store_keeps_it()
    {
        var mine = Store.Recording();

        var services = new ServiceCollection();

        services.AddSingleton<ICache>(mine);
        services.AddZeroCaching();

        services.BuildServiceProvider().GetRequiredService<ICache>().Should().BeSameAs(mine);
    }

    [Fact]
    public void Adding_it_twice_does_not_wrap_the_pipeline_twice()
    {
        var services = new ServiceCollection();

        services.AddZeroCaching();
        services.AddZeroCaching();

        using var scope = services.BuildServiceProvider().CreateScope();

        scope.ServiceProvider.GetServices<IPipelineBehavior<GetInvoice, string>>().Should().ContainSingle();
    }

    [Fact]
    public void A_lifetime_of_zero_is_refused_rather_than_silently_caching_nothing()
    {
        var services = new ServiceCollection();

        services.AddZeroCaching(options => options.DefaultLifetime = TimeSpan.Zero);

        var provider = services.BuildServiceProvider();

        var refused = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<CachingOptions>>().Value);

        refused.Message.Should().Contain(nameof(CachingOptions.DefaultLifetime));
    }

    [Fact]
    public void A_null_key_prefix_is_refused()
    {
        var services = new ServiceCollection();

        services.AddZeroCaching(options => options.KeyPrefix = null!);

        var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<CachingOptions>>().Value);
    }
}
