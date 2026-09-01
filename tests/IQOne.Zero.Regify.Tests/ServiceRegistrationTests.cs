using IQOne.Zero.Regify.Tests.Harness;

namespace IQOne.Zero.Regify.Tests;

/// IQ'nun tasarim tercihi: yasam suresi ARAYUZDEN gelir, oznitelik yazilmaz.
public class ServiceRegistrationTests
{
    [Theory]
    [InlineData("IScoped", "AddScoped")]
    [InlineData("ISingleton", "AddSingleton")]
    [InlineData("ITransient", "AddTransient")]
    public void Yasam_suresi_isaret_arayuzunden_turetilir(string marker, string expected)
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
    public void Varsayilan_arayuz_konvansiyonu_uygulanir()
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
    public void IIgnoredService_kaydedilmez()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Descriptors;
            using IQOne.Zero.DependencyInjection.Services;

            namespace Test;

            public interface IThingRepository;

            public sealed class ThingRepository : IThingRepository, IScoped, IIgnoredService;
            """);

        run.GeneratedSource.Should().Contain("yasam suresi isareti tasiyan tip yok");
    }

    [Fact]
    public void ServiceTypes_ozniteligi_konvansiyonu_ezer()
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
    public void RGF006_birden_fazla_yasam_suresi_isareti()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface IThingRepository;

            public sealed class ThingRepository : IThingRepository, IScoped, ISingleton;
            """);

        run.DiagnosticIds.Should().Contain("RGF006");
    }

    [Fact]
    public void RGF007_servis_tipi_belirlenemezse()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface IAlfa;
            public interface IBeta;

            // Ne isim konvansiyonuna uyan bir arayuz var ne de tek aday
            public sealed class Karisik : IAlfa, IBeta, IScoped;
            """);

        run.DiagnosticIds.Should().Contain("RGF007");
    }

    [Fact]
    public void RGF009_singleton_scoped_bagimliligi_alirsa()
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

        run.DiagnosticIds.Should().Contain("RGF009");
        run.GeneratedSource.Should().BeEmpty();
    }

    [Fact]
    public void Singleton_singleton_bagimliligi_alabilir()
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

        run.DiagnosticIds.Should().NotContain("RGF009");
        run.HasError.Should().BeFalse();
    }
}
