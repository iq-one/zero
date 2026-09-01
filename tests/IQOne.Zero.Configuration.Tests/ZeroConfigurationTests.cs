using System.ComponentModel.DataAnnotations;
using IQOne.Zero.App;
using IQOne.Zero.Configuration.Extensions;
using IQOne.Zero.Configuration.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IQOne.Zero.Configuration.Tests;

/// <summary>
/// "Configuration is validated before traffic arrives" was false in a Zero-hosted
/// application: <c>ValidateOnStart</c> only registers a validator, and nothing in the
/// framework resolved it. Nothing put an <see cref="IConfiguration"/> into the collection
/// either. These pin both halves.
/// </summary>
public class ZeroConfigurationTests
{
    private sealed class MailOptions
    {
        [Required] public string Host { get; set; } = string.Empty;

        [Range(1, 65535)] public int Port { get; set; } = 25;
    }

    private static Action<IConfigurationBuilder> Settings(params (string Key, string Value)[] settings)
        => builder => builder.AddInMemoryCollection(
            settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)));

    [Fact]
    public void The_entry_point_alone_registers_a_configuration()
    {
        var services = new ServiceCollection();

        services.AddZeroConfiguration();

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        provider.GetRequiredService<IConfiguration>().Should().NotBeNull();
    }

    [Fact]
    public void Sources_added_through_the_entry_point_are_readable()
    {
        var services = new ServiceCollection();

        services.AddZeroConfiguration(Settings(("MailOptions:Host", "smtp.example.com")));

        services.BuildServiceProvider()
            .GetRequiredService<IConfiguration>()["MailOptions:Host"]
            .Should().Be("smtp.example.com");
    }

    [Fact]
    public void A_configuration_the_host_already_built_is_kept()
    {
        var services = new ServiceCollection();

        // What ASP.NET has already done by the time Zero is added: appsettings, the
        // environment overlay and the command line are in there and must not be dropped.
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>("MailOptions:Host", "from-the-host")])
            .Build());

        services.AddZeroConfiguration(Settings(("MailOptions:Port", "2525")));

        var configuration = services.BuildServiceProvider().GetRequiredService<IConfiguration>();

        configuration["MailOptions:Host"].Should().Be("from-the-host");
        configuration["MailOptions:Port"].Should().Be("2525");
    }

    [Fact]
    public async Task A_bad_setting_stops_a_zero_hosted_application_before_it_runs()
    {
        var services = new ServiceCollection();

        services.AddZeroConfiguration(Settings(("MailOptions:Port", "70000")));
        services.AddValidatedOptions<MailOptions>();

        await using var application = new Application(services);

        var start = () => application.RunAsync();

        // Previously this started happily and failed on the first request that read the port.
        (await start.Should().ThrowAsync<OptionsValidationException>())
            .Which.Message.Should().Contain(nameof(MailOptions.Port));
    }

    [Fact]
    public async Task Valid_settings_let_the_application_start()
    {
        var services = new ServiceCollection();

        services.AddZeroConfiguration(Settings(
            ("MailOptions:Host", "smtp.example.com"), ("MailOptions:Port", "587")));

        services.AddValidatedOptions<MailOptions>();

        await using var application = new Application(services);

        await application.RunAsync();

        application.ServiceProvider.GetRequiredService<IOptions<MailOptions>>().Value.Port.Should().Be(587);
    }

    [Fact]
    public void A_named_section_binds_to_that_section_and_not_below_it()
    {
        // The deleted Configure<TOptions>("Mail") overload bound "Mail:MailOptions" instead,
        // silently, with no error and empty options. One binder, and it reads what it is told.
        var services = new ServiceCollection();

        services.AddZeroConfiguration(Settings(("Mail:Host", "smtp.example.com"), ("Mail:Port", "465")));
        services.AddValidatedOptions<MailOptions>("Mail");

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<MailOptions>>().Value;

        options.Host.Should().Be("smtp.example.com");
        options.Port.Should().Be(465);
    }

    [Fact]
    public void Capability_options_bind_by_convention_when_a_configuration_exists()
    {
        var services = new ServiceCollection();

        services.AddZeroConfiguration(Settings(("MailOptions:Host", "smtp.example.com")));
        services.AddZeroOptions<MailOptions>();

        services.BuildServiceProvider()
            .GetRequiredService<IOptions<MailOptions>>().Value.Host
            .Should().Be("smtp.example.com");
    }

    [Fact]
    public void Capability_options_do_not_need_a_configuration_at_all()
    {
        var services = new ServiceCollection();

        services.AddZeroOptions<MailOptions>(options => options.Host = "in-code");

        services.BuildServiceProvider()
            .GetRequiredService<IOptions<MailOptions>>().Value.Host
            .Should().Be("in-code");
    }

    [Fact]
    public void The_delegate_wins_over_the_configuration()
    {
        var services = new ServiceCollection();

        services.AddZeroOptions<MailOptions>(options => options.Port = 2525);
        services.AddZeroConfiguration(Settings(("MailOptions:Host", "smtp.example.com"), ("MailOptions:Port", "587")));

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<MailOptions>>().Value;

        // Registered before the configuration was, and it still binds: the lookup happens
        // when the options are read, not when they are registered.
        options.Host.Should().Be("smtp.example.com");
        options.Port.Should().Be(2525);
    }

    [Fact]
    public void Capability_options_are_validated_too()
    {
        var services = new ServiceCollection();

        services.AddZeroOptions<MailOptions>(options => options.Port = 70000);

        var read = () => services.BuildServiceProvider().GetRequiredService<IOptions<MailOptions>>().Value;

        read.Should().Throw<OptionsValidationException>().WithMessage($"*{nameof(MailOptions.Port)}*");
    }
}
