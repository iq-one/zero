using Microsoft.AspNetCore.Http;

namespace IQOne.Zero.Web.Tests;

/// <summary>
/// W9: two routes conflict when they match the same calls, not when they read the same.
/// </summary>
/// <remarks>
/// This is the one check that cannot be driven over HTTP, because the application it would
/// need never starts: ASP.NET throws <c>AmbiguousMatchException</c> on the first request
/// instead, which is a 500 for a mistake that was visible while the table was being built.
/// Catching it here is the point.
/// </remarks>
public class EndpointRegistryTests
{
    private static ZeroEndpointDescriptor Endpoint(string method, string pattern, string name)
        => new(method, pattern, name, null, null, false, typeof(GetThing), typeof(ThingModel),
            static context => ZeroEndpoint.RunAsync<GetThing, ThingModel>(context));

    private static void Add(EndpointRegistry registry, string method, string pattern, string name)
        => registry.Add(Endpoint(method, pattern, name));

    [Fact]
    public void Two_routes_differing_only_in_a_parameter_name_are_the_same_route()
    {
        var registry = new EndpointRegistry();

        Add(registry, "GET", "/invoices/{id}", "One");

        var second = () => Add(registry, "GET", "/invoices/{invoiceId}", "Two");

        second.Should().Throw<InvalidOperationException>()
            .WithMessage("*match the same calls*")
            .WithMessage("*A route belongs to one request.*");
    }

    [Fact]
    public void The_message_names_both_requests_and_both_patterns()
    {
        var registry = new EndpointRegistry();

        Add(registry, "GET", "/invoices/{id:int}", "One");

        var second = () => Add(registry, "GET", "/invoices/{number:int}", "Two");

        second.Should().Throw<InvalidOperationException>()
            .WithMessage("*/invoices/{id:int}*")
            .WithMessage("*/invoices/{number:int}*");
    }

    [Fact]
    public void A_catch_all_is_not_the_same_shape_as_a_segment()
    {
        var registry = new EndpointRegistry();

        Add(registry, "GET", "/files/{path}", "One");
        Add(registry, "GET", "/files/{*path}", "Two");

        registry.Endpoints.Should().HaveCount(2);
    }

    /// <summary>
    /// Constraints stay in the key, because they are what makes two patterns different.
    /// </summary>
    /// <remarks>
    /// <c>{id:int}</c> and <c>{slug:alpha}</c> never match the same call, so refusing them
    /// would reject a perfectly good pair of routes — a normalisation that is too eager is
    /// as wrong as one that is too shy.
    /// </remarks>
    [Fact]
    public void Two_routes_with_different_constraints_are_different_routes()
    {
        var registry = new EndpointRegistry();

        Add(registry, "GET", "/invoices/{id:int}", "One");
        Add(registry, "GET", "/invoices/{slug:alpha}", "Two");

        registry.Endpoints.Should().HaveCount(2);
    }

    [Fact]
    public void The_same_shape_under_a_different_method_is_a_different_endpoint()
    {
        var registry = new EndpointRegistry();

        Add(registry, "GET", "/invoices/{id}", "One");
        Add(registry, "DELETE", "/invoices/{invoiceId}", "Two");

        registry.Endpoints.Should().HaveCount(2);
    }

    [Fact]
    public void A_literal_segment_is_still_compared_as_written()
    {
        var registry = new EndpointRegistry();

        Add(registry, "GET", "/invoices/{id}", "One");
        Add(registry, "GET", "/payments/{id}", "Two");

        registry.Endpoints.Should().HaveCount(2);
    }

    [Fact]
    public void A_frozen_table_takes_no_more_endpoints()
    {
        var registry = new EndpointRegistry();

        registry.Freeze();

        var late = () => Add(registry, "GET", "/invoices/{id}", "One");

        late.Should().Throw<InvalidOperationException>().WithMessage("*frozen*");
    }

    /// <summary>The key is normalised; what gets mapped is still what was written.</summary>
    [Fact]
    public void An_endpoint_keeps_its_pattern_exactly_as_it_was_declared()
    {
        var registry = new EndpointRegistry();

        Add(registry, "GET", "/invoices/{id:int}", "One");

        registry.Endpoints.Single().Pattern.Should().Be("/invoices/{id:int}");
    }
}
