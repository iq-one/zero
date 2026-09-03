using IQOne.Zero.Authorization.Analyzers.Tests.Harness;

namespace IQOne.Zero.Authorization.Analyzers.Tests;

/// <summary>
/// What counts as saying who may make a request.
/// </summary>
/// <remarks>
/// The rule's value is entirely in what it REFUSES: in a codebase where most requests carry
/// a policy, the one where somebody forgot looks exactly like the rest. So most of these
/// tests assert that ZERO450 fires.
/// </remarks>
public class RequestAuthorizationAnalyzerTests
{
    private const string Preamble = """
        using System.Threading;
        using System.Threading.Tasks;
        using IQOne.Zero;
        using IQOne.Zero.Authorization;
        using IQOne.Zero.Messaging;
        using IQOne.Zero.Web;

        namespace Test;
        """;

    [Fact]
    public async Task A_request_that_says_nothing_is_reported()
    {
        var run = await AnalyzerHarness.RunAsync($$"""
            {{Preamble}}

            public sealed record GetThing : IQuery<string>;
            """);

        run.CompilerErrors.Should().BeEmpty();
        run.Ids.Should().Contain("ZERO450");
    }

    [Fact]
    public async Task A_named_policy_answers_the_question()
    {
        var run = await AnalyzerHarness.RunAsync($$"""
            {{Preamble}}

            [Authorize(Policy = "things.read")]
            public sealed record GetThing : IQuery<string>;
            """);

        run.Ids.Should().NotContain("ZERO450");
    }

    [Fact]
    public async Task A_policy_on_a_ROUTE_answers_it_too()
    {
        // Iki bildirim, tek olgu demek olurdu; rota ozniteligi bir
        // IAuthorizationDeclaration ve orada yazilan politika bu kurali karsilar.
        var run = await AnalyzerHarness.RunAsync($$"""
            {{Preamble}}

            [Post("/things", Policy = "things.read")]
            public sealed record GetThing : IQuery<string>;
            """);

        run.Ids.Should().NotContain("ZERO450");
    }

    [Fact]
    public async Task A_route_with_NO_policy_still_says_nothing()
    {
        // Onemli: bir rotanin varligi "kim cagirabilir" sorusunu cevaplamiyor. Cevapsiz
        // birakilan uc nokta yalnizca kimlik dogrulamasi ister, ve bunun kasitli mi
        // unutulmus mu oldugu ancak yazildiginda anlasilir.
        var run = await AnalyzerHarness.RunAsync($$"""
            {{Preamble}}

            [Post("/things")]
            public sealed record GetThing : IQuery<string>;
            """);

        run.Ids.Should().Contain("ZERO450");
    }

    [Fact]
    public async Task An_attribute_that_DERIVES_the_policy_can_say_so()
    {
        // 0.4.0 kendi rota ozniteligini yazmayi mumkun kildi ve degisiklik notu tam bu
        // deseni onerdi — ama analizor politikayi ARGUMANLARDAN okuyor, kurucuda
        // hesaplanani gormuyor. Yani onerilen desen kural tarafindan reddediliyordu.
        // Isaret, ozniteligin bunu SOYLEMESINI sagliyor.
        var run = await AnalyzerHarness.RunAsync($$"""
            {{Preamble}}

            [DeclaresAuthorization]
            public sealed class ServiceRouteAttribute : PostAttribute
            {
                public ServiceRouteAttribute(string pattern) : base(pattern)
                    => Policy = pattern.TrimStart('/');
            }

            [ServiceRoute("/things")]
            public sealed record GetThing : IQuery<string>;
            """);

        run.CompilerErrors.Should().BeEmpty();
        run.Ids.Should().NotContain("ZERO450");
    }

    [Fact]
    public async Task WITHOUT_the_marker_the_same_attribute_is_reported()
    {
        // Isaretin isi yaptigini gosteren yon. Kurucusunda politika atayan ama bunu
        // bildirmeyen bir oznitelik, analizor icin hicbir sey soylememis olanla ayni:
        // kural okuyabildigi seye bakiyor.
        var run = await AnalyzerHarness.RunAsync($$"""
            {{Preamble}}

            public sealed class ServiceRouteAttribute : PostAttribute
            {
                public ServiceRouteAttribute(string pattern) : base(pattern)
                    => Policy = pattern.TrimStart('/');
            }

            [ServiceRoute("/things")]
            public sealed record GetThing : IQuery<string>;
            """);

        run.Ids.Should().Contain("ZERO450");
    }

    [Fact]
    public async Task AllowAnonymous_and_a_policy_together_are_contradictory()
    {
        var run = await AnalyzerHarness.RunAsync($$"""
            {{Preamble}}

            [Authorize(Policy = "things.read")]
            [AllowAnonymous]
            public sealed record GetThing : IQuery<string>;
            """);

        run.Ids.Should().Contain("ZERO451");
    }
}
