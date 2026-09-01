using System.Security.Claims;
using IQOne.Zero.Authorization;

namespace IQOne.Zero.Authorization.Tests;

public class CurrentUserTests
{
    [Fact]
    public void Nobody_is_not_authenticated_and_has_no_identifier()
    {
        CurrentUser.Anonymous.IsAuthenticated.Should().BeFalse();
        CurrentUser.Anonymous.Id.Should().BeNull();
        CurrentUser.Anonymous.Claims.Should().BeEmpty();
    }

    [Fact]
    public void A_claim_type_is_matched_without_case_and_a_value_exactly()
    {
        var user = Callers.Known("u-1", new Claim("Tenant", "north"));

        user.FindFirst("tenant").Should().Be("north", "claim types arrive as URIs from several issuers");
        user.HasClaim("tenant", "north").Should().BeTrue();
        user.HasClaim("tenant", "North").Should().BeFalse("a value that differs in case is a different value");
    }

    [Fact]
    public void Every_value_of_a_repeated_claim_is_readable()
    {
        var user = Callers.Known("u-1", new Claim("scope", "read"), new Claim("scope", "write"));

        user.FindAll("scope").Should().Equal("read", "write");
    }

    [Fact]
    public void A_role_is_read_from_whichever_claim_the_application_says_carries_roles()
    {
        var user = Callers.Known("u-1", new Claim("roles", "admin"));

        user.IsInRole("admin").Should().BeFalse("the default is the WS-Federation claim, and this token uses 'roles'");
        user.IsInRole("admin", "roles").Should().BeTrue();
    }

    [Fact]
    public void A_principal_with_an_authenticated_identity_is_a_caller()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "u-9"), new Claim(ClaimTypes.Role, "admin")],
            authenticationType: "test");

        var user = new ClaimsPrincipalCurrentUser(new ClaimsPrincipal(identity));

        user.IsAuthenticated.Should().BeTrue();
        user.Id.Should().Be("u-9");
        user.IsInRole("admin").Should().BeTrue();
    }

    [Fact]
    public void An_OpenID_Connect_subject_is_read_when_there_is_no_name_identifier()
    {
        var identity = new ClaimsIdentity([new Claim("sub", "auth0|42")], authenticationType: "test");

        new ClaimsPrincipalCurrentUser(new ClaimsPrincipal(identity)).Id.Should().Be("auth0|42");
    }

    [Fact]
    public void The_claim_carrying_the_identifier_can_be_named()
    {
        var identity = new ClaimsIdentity(
            [new Claim("sub", "auth0|42"), new Claim("employee_no", "E-7")], authenticationType: "test");

        new ClaimsPrincipalCurrentUser(new ClaimsPrincipal(identity), "employee_no").Id.Should().Be("E-7");
    }

    [Fact]
    public void An_unauthenticated_principal_reports_no_identifier_even_when_it_carries_one()
    {
        // No authentication type means ClaimsIdentity.IsAuthenticated is false. A claim on such
        // a principal is a claim nobody vouched for.
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "u-9")]);

        var user = new ClaimsPrincipalCurrentUser(new ClaimsPrincipal(identity));

        user.IsAuthenticated.Should().BeFalse();
        user.Id.Should().BeNull("a rule that only checks Id must not mistake this for a known caller");
    }

    [Fact]
    public void An_empty_principal_is_nobody()
        => new ClaimsPrincipalCurrentUser(new ClaimsPrincipal()).IsAuthenticated.Should().BeFalse();
}
