namespace IQOne.Zero.Guidance.Tests;

/// <summary>
/// Checks the checker.
/// </summary>
/// <remarks>
/// The guidance test ignores CS0246 on purpose: snippets name illustrative domain types
/// that do not exist. The cost of that decision is that a MISSING FRAMEWORK REFERENCE
/// looks exactly the same — the type does not resolve, every snippet using it reports
/// CS0246, and the run is green while nothing is verified. That is how a
/// <c>ConventionDbContext</c> example with its arguments in the wrong order shipped in
/// 0.1.0: the checker had no Entity Framework reference and never saw the call.
/// <para>
/// So the types the guidance leans on are named here. Resolving to zero assemblies means
/// the checker is blind to them; resolving to two means it is looking at a different
/// definition than the framework was built against, and will report correct guidance as
/// wrong.
/// </para>
/// </remarks>
public class GuidanceCheckerTests
{
    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.DbContext")]
    [InlineData("Microsoft.EntityFrameworkCore.ModelBuilder")]
    [InlineData("Microsoft.EntityFrameworkCore.DbContextOptionsBuilder")]
    [InlineData("Microsoft.AspNetCore.Http.HttpContext")]
    [InlineData("Microsoft.Extensions.DependencyInjection.IServiceCollection")]
    [InlineData("Microsoft.Extensions.Logging.ILogger`1")]
    [InlineData("IQOne.Zero.Persistence.EntityFramework.ConventionDbContext")]
    [InlineData("IQOne.Zero.Messaging.IPipelineBehavior`2")]
    [InlineData("IQOne.Zero.Web.RouteAttribute")]
    public void The_checker_sees_exactly_one_definition(string type)
    {
        var assemblies = GuidanceCompiler.DefiningAssemblies(type);

        assemblies.Should().ContainSingle(
            $"'{type}' must resolve to exactly one assembly, or the guidance check is " +
            $"either blind to it or comparing two different definitions. Found: " +
            $"[{string.Join(", ", assemblies)}]");
    }
}
