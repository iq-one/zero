using System.ComponentModel.DataAnnotations;
using IQOne.Zero.Configuration.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IQOne.Zero.Configuration.Tests;

/// <summary>
/// The point of validated options is that a bad setting stops startup rather than failing
/// on the request that first happens to read it. These tests pin that behaviour.
/// </summary>
public class ValidatedOptionsTests
{
    private sealed class MailOptions
    {
        [Required] public string Host { get; set; } = string.Empty;

        [Range(1, 65535)] public int Port { get; set; }
    }

    private static ServiceProvider Build(params (string Key, string Value)[] settings)
    {
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build());

        services.AddValidatedOptions<MailOptions>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void The_section_name_defaults_to_the_type_name()
    {
        var provider = Build(("MailOptions:Host", "smtp.example.com"), ("MailOptions:Port", "587"));

        var options = provider.GetRequiredService<IOptions<MailOptions>>().Value;

        options.Host.Should().Be("smtp.example.com");
        options.Port.Should().Be(587);
    }

    [Fact]
    public void A_missing_required_setting_fails_and_the_message_names_it()
    {
        var provider = Build(("MailOptions:Port", "587"));

        var read = () => provider.GetRequiredService<IOptions<MailOptions>>().Value;

        read.Should().Throw<OptionsValidationException>()
            .Which.Message.Should().Contain(nameof(MailOptions.Host),
                "the message must say which setting is missing");
    }

    [Fact]
    public void A_value_outside_the_allowed_range_is_rejected()
    {
        var provider = Build(("MailOptions:Host", "smtp.example.com"), ("MailOptions:Port", "70000"));

        var read = () => provider.GetRequiredService<IOptions<MailOptions>>().Value;

        read.Should().Throw<OptionsValidationException>()
            .Which.Message.Should().Contain(nameof(MailOptions.Port));
    }

    [Fact]
    public void A_business_rule_predicate_is_applied_too()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection([
                new KeyValuePair<string, string?>("MailOptions:Host", "localhost"),
                new KeyValuePair<string, string?>("MailOptions:Port", "25")
            ])
            .Build());

        services.AddValidatedOptions<MailOptions>(
            o => !o.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase),
            "MailOptions:Host cannot be localhost outside development.");

        var read = () => services.BuildServiceProvider().GetRequiredService<IOptions<MailOptions>>().Value;

        read.Should().Throw<OptionsValidationException>()
            .Which.Message.Should().Contain("cannot be localhost");
    }

    [Fact]
    public void An_explicit_section_name_overrides_the_convention()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection([
                new KeyValuePair<string, string?>("Smtp:Host", "smtp.example.com"),
                new KeyValuePair<string, string?>("Smtp:Port", "465")
            ])
            .Build());

        services.AddValidatedOptions<MailOptions>("Smtp");

        var options = services.BuildServiceProvider().GetRequiredService<IOptions<MailOptions>>().Value;

        options.Host.Should().Be("smtp.example.com");
        options.Port.Should().Be(465);
    }
}
