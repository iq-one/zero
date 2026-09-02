using System.Security.Claims;
using IQOne.Zero.Authorization;
using IQOne.Zero.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Authorization.Tests;

/// <summary>A bad setting has to stop startup, not surface on the first request that hits it.</summary>
public class AuthorizationOptionsTests
{
    [Fact]
    public void A_policy_needs_a_name()
    {
        var configure = () => new AuthorizationOptions().AddPolicy("  ", new AlwaysFails());

        configure.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_policy_with_no_requirements_is_refused_rather_than_admitting_everyone()
    {
        var configure = () => new AuthorizationOptions().AddPolicy("empty");

        configure.Should().Throw<ArgumentException>()
            .WithMessage("*every authenticated caller would pass it*");
    }

    [Fact]
    public void Two_policies_cannot_share_a_name()
    {
        var options = new AuthorizationOptions().AddPolicy("one", new AlwaysFails());

        var again = () => options.AddPolicy("one", new AlwaysThrows());

        again.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Policy_names_are_compared_exactly()
    {
        var options = new AuthorizationOptions().AddPolicy("Invoices.Close", new AlwaysFails());

        options.Policies.ContainsKey("invoices.close").Should().BeFalse();
        options.Policies.ContainsKey("Invoices.Close").Should().BeTrue();
    }

    [Fact]
    public void A_blank_role_claim_type_stops_startup()
    {
        var add = () => new ServiceCollection().AddZeroAuthorization(options => options.RoleClaimType = " ");

        add.Should().Throw<InvalidOperationException>().WithMessage("*RoleClaimType*");
    }

    [Fact]
    public void The_settings_cannot_be_changed_once_the_modules_have_been_configured()
    {
        AuthorizationOptions? captured = null;

        var services = new ServiceCollection().AddZeroAuthorization(options => captured = options);

        // Sealed by the module phase, not by Add: a module that owns a set of routes owns the
        // policies guarding them, so it has to be able to declare one first.
        captured!.AddPolicy("late", new RolesRequirement(["admin"]));

        services.AddModules();

        var change = () => captured.AddPolicy("later", new RolesRequirement(["admin"]));

        change.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void The_role_claim_type_defaults_to_the_one_ClaimsIdentity_uses()
        => new AuthorizationOptions().RoleClaimType.Should().Be(ClaimTypes.Role);
}
