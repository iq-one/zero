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

        // The module is emitted anyway. Withholding it turned one error into a second one --
        // 'CS0246: Module does not exist' -- pointing at nothing the developer wrote, and a
        // team that downgraded the rule in .editorconfig lost the module altogether.
        run.GeneratedSource.Should().Contain("public sealed partial class Module");
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

/// <summary>
/// Where a lifetime came from, when two disagree.
/// </summary>
/// <remarks>
/// Zero deliberately does not let the nearest declaration win here, which is what an
/// inherited <em>attribute</em> does. A lifetime on an abstraction is part of what callers
/// read from it, so an implementation quietly overriding it makes the interface lie.
/// </remarks>
public class LifetimeSourceTests
{
    [Fact]
    public void A_type_contradicting_its_abstraction_is_ZERO011_and_names_the_interface()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface IInvoiceStore : IScoped;

            public sealed class InvoiceStore : IInvoiceStore, ISingleton;
            """);

        var reported = run.Diagnostics.Single(d => d.Id == "ZERO011");

        reported.GetMessage().Should()
            .Contain("IInvoiceStore").And.Contain("Scoped").And.Contain("Singleton");
    }

    [Fact]
    public void Two_markers_written_directly_on_one_type_stay_ZERO006()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface IInvoiceStore;

            public sealed class InvoiceStore : IInvoiceStore, IScoped, ISingleton;
            """);

        run.Diagnostics.Select(d => d.Id).Should().Contain("ZERO006").And.NotContain("ZERO011",
            "nothing was inherited here; the author simply wrote two");
    }

    [Fact]
    public void The_attribute_settles_the_contradiction_instead_of_adding_to_it()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Annotations;
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface IInvoiceStore : IScoped;

            [Singleton]
            public sealed class CachedInvoiceStore : IInvoiceStore;
            """);

        run.Diagnostics.Should().BeEmpty("this is the documented way to state the exception");
        run.GeneratedSource.Should().Contain("AddSingleton<global::Test.IInvoiceStore, global::Test.CachedInvoiceStore>");
    }

    [Fact]
    public void Taking_the_lifetime_from_the_abstraction_alone_is_silent()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface IInvoiceStore : IScoped;

            public sealed class InvoiceStore : IInvoiceStore;
            """);

        run.Diagnostics.Should().BeEmpty();
        run.GeneratedSource.Should().Contain("AddScoped<global::Test.IInvoiceStore, global::Test.InvoiceStore>");
    }

    [Fact]
    public void An_attribute_with_an_ARRAY_named_argument_does_not_crash_the_generator()
    {
        // TypedConstant.Value ATIYOR bir dizide, null dondurmuyor. Bu kod yolu tek bir
        // islenmeyen tur yuzunden butun derlemenin uretecini dusuruyordu — ve derleyici
        // sonra uretilmis dosyanin gerceklestirecegi partial metodu sikayet ediyordu,
        // yani hata yazarin yazmadigi bir dosyayi ve kaldirmadigi bir uyeyi gosteriyordu.
        //
        // Kurucu dizileri buraya hic gelmiyor (cagiran onlari duzlestiriyor,
        // [ServiceTypes(typeof(A), typeof(B))] icin gereken sey bu). ADLANDIRILMIS bir
        // dizi argumani duzlestirilmiyor, ve derlemedeki HERHANGI bir oznitelikte bir
        // tanesi olmasi yetiyordu.
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class TagsAttribute : System.Attribute
            {
                public string[] Names { get; set; } = [];
            }

            [Tags(Names = ["one", "two"])]
            public sealed class Tagged : IScoped;

            public interface ITagged;
            """);

        run.GeneratedFileErrorMessages.Should().BeEmpty();
        run.GeneratedSource.Should().Contain("Tagged");
    }
}
