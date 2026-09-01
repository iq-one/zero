using IQOne.Zero.Web.Binding;
using IQOne.Zero.Web.Writing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Web.Tests;

/// <summary>
/// What one call to the entry point has to be enough for.
/// </summary>
public class RegistrationTests
{
    private sealed class Silent : IResponseWriter
    {
        public IResult Success<TResponse>(HttpContext context, TResponse value) => Results.NoContent();

        public IResult Empty(HttpContext context) => Results.NoContent();

        public IResult Failure(HttpContext context, IReadOnlyList<Error> errors, int? status)
            => Results.NoContent();
    }

    private static ServiceProvider Provider(Action<IServiceCollection>? before = null)
    {
        var services = new ServiceCollection();

        before?.Invoke(services);
        services.AddZeroWeb();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
    }

    [Fact]
    public void The_entry_point_alone_registers_what_an_endpoint_needs()
    {
        using var provider = Provider();

        provider.GetRequiredService<IRequestBinder>().Should().BeOfType<JsonRequestBinder>();
        provider.GetRequiredService<IResponseWriter>().Should().BeOfType<JsonResponseWriter>();
    }

    /// <summary>W7: the seam is only a seam if what the application registers survives.</summary>
    [Fact]
    public void A_writer_the_application_registered_is_left_alone()
    {
        using var provider = Provider(services => services.AddSingleton<IResponseWriter, Silent>());

        provider.GetRequiredService<IResponseWriter>().Should().BeOfType<Silent>();
    }

    /// <summary>
    /// W13: a package not named for a serializer does not name one on its options.
    /// </summary>
    /// <remarks>
    /// The contract's rule, and the reason the JSON settings moved behind the two seams: the
    /// binder and the writer read the application's own <c>ConfigureHttpJsonOptions</c>, and
    /// an application that answers something other than JSON replaces them both without
    /// having to explain itself to an options type that assumes otherwise.
    /// </remarks>
    [Fact]
    public void The_options_surface_names_no_serializer()
    {
        var named = typeof(ZeroWebOptions)
            .GetProperties()
            .Select(p => p.PropertyType.Namespace ?? string.Empty)
            .Where(n => n.StartsWith("System.Text.Json", StringComparison.Ordinal));

        named.Should().BeEmpty("serialization belongs to the binder and the writer, not to the endpoint options");
    }
}
