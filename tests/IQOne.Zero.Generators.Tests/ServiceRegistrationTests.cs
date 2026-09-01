using IQOne.Zero.Generators.Tests.Harness;

namespace IQOne.Zero.Generators.Tests;

/// <summary>
/// Lifetime comes from the abstraction, never from an attribute at the registration site,
/// and the registration itself is generated. These tests pin both halves of that.
/// </summary>
public class ServiceRegistrationTests
{
    [Theory]
    [InlineData("IScoped", "AddScoped")]
    [InlineData("ISingleton", "AddSingleton")]
    [InlineData("ITransient", "AddTransient")]
    public void Lifetime_is_taken_from_the_marker_interface(string marker, string expected)
    {
        var run = GeneratorHarness.Run($$"""
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface IThingRepository;

            public sealed class ThingRepository : IThingRepository, {{marker}};
            """);

        run.HasError.Should().BeFalse();
        run.GeneratedSource.Should()
            .Contain($"{expected}<global::Test.IThingRepository, global::Test.ThingRepository>");
    }

    [Fact]
    public void The_matching_interface_is_chosen_over_any_other()
    {
        // ThingRepository -> IThingRepository, IDisposable degil
        var run = GeneratorHarness.Run("""
            using System;
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface IThingRepository;

            public sealed class ThingRepository : IDisposable, IThingRepository, IScoped
            {
                public void Dispose() { }
            }
            """);

        run.GeneratedSource.Should()
            .Contain("global::Test.IThingRepository, global::Test.ThingRepository")
            .And.NotContain("IDisposable, global::Test.ThingRepository");
    }

    [Fact]
    public void A_type_marked_IIgnoredService_is_not_registered()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Descriptors;
            using IQOne.Zero.DependencyInjection.Services;

            namespace Test;

            public interface IThingRepository;

            public sealed class ThingRepository : IThingRepository, IScoped, IIgnoredService;
            """);

        run.GeneratedSource.Should().Contain("No type in this assembly carries a lifetime marker.");
    }

    [Fact]
    public void ServiceTypes_overrides_the_naming_convention()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Annotations;
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface IThingRepository;
            public interface IReportSource;

            [ServiceTypes(typeof(IReportSource))]
            public sealed class ThingRepository : IThingRepository, IReportSource, IScoped;
            """);

        run.GeneratedSource.Should()
            .Contain("global::Test.IReportSource, global::Test.ThingRepository")
            .And.NotContain("global::Test.IThingRepository, global::Test.ThingRepository");
    }

    [Fact]
    public void ZERO006_is_reported_for_two_lifetime_markers()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface IThingRepository;

            public sealed class ThingRepository : IThingRepository, IScoped, ISingleton;
            """);

        run.DiagnosticIds.Should().Contain("ZERO006");
    }

    [Fact]
    public void ZERO007_is_reported_when_no_service_type_can_be_determined()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface IAlfa;
            public interface IBeta;

            // Ne isim konvansiyonuna uyan bir arayuz var ne de tek aday
            public sealed class Karisik : IAlfa, IBeta, IScoped;
            """);

        run.DiagnosticIds.Should().Contain("ZERO007");
    }

    [Fact]
    public void ZERO009_is_reported_for_a_singleton_taking_a_scoped_dependency()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface IThingRepository;
            public interface IReportCache;

            public sealed class ThingRepository : IThingRepository, IScoped;

            public sealed class ReportCache(IThingRepository repository) : IReportCache, ISingleton
            {
                private readonly IThingRepository _repository = repository;
            }
            """);

        run.DiagnosticIds.Should().Contain("ZERO009");
        run.GeneratedSource.Should().BeEmpty();
    }

    [Fact]
    public void A_singleton_may_take_another_singleton()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface IClockService;
            public interface IReportCache;

            public sealed class ClockService : IClockService, ISingleton;

            public sealed class ReportCache(IClockService clock) : IReportCache, ISingleton
            {
                private readonly IClockService _clock = clock;
            }
            """);

        run.DiagnosticIds.Should().NotContain("ZERO009");
        run.HasError.Should().BeFalse();
    }
}
