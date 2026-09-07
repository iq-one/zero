using IQOne.Zero.Generators.Tests.Harness;

namespace IQOne.Zero.Generators.Tests;

/// <summary>
/// Declining generation.
/// </summary>
/// <remarks>
/// Generated code cannot be edited — it is written during compilation and there is no file the
/// compiler reads back — so its user has to be able to say no. One marker for every generator,
/// because it says one thing: whatever would have been written here is not, and the members it
/// would have supplied are the author's. The compiler then names them, which is the point.
/// </remarks>
public class NoGenerateTests
{
    [Fact]
    public void A_declined_MAP_generates_nothing()
    {
        var run = GeneratorHarness.Run("""
            using System;
            using System.Linq.Expressions;
            using IQOne.Zero;
            using IQOne.Zero.Mapping;

            namespace Test;

            public sealed class Row { public int A { get; set; } public int B { get; set; } }

            /// <summary>Uretecin kurali olmayan bir sekil: konumsal kurulum.</summary>
            public sealed record RowModel(int A, int B);

            [NoGenerate]
            public sealed partial class RowMap : Map<Row, RowModel>
            {
                public override Expression<Func<Row, RowModel>> Selector { get; }
                    = r => new RowModel(r.A, r.B);
            }
            """);

        run.DiagnosticMessages.Should().BeEmpty();
        run.GeneratedFileErrorMessages.Should().BeEmpty();
        run.GeneratedSource.Should().NotContain("partial class RowMap");
    }

    [Fact]
    public void A_declined_map_is_told_what_it_took_on()
    {
        // Isaretleyip HICBIR sey yazmazsan derleyici eksik uyeyi adlandiriyor. Kacis
        // yolunun sessiz olmamasi bu: neyi ustlendigini soyluyor.
        var run = GeneratorHarness.Run("""
            using IQOne.Zero;
            using IQOne.Zero.Mapping;

            namespace Test;

            public sealed class Row { public int A { get; set; } }
            public sealed class RowModel { public int A { get; set; } }

            [NoGenerate]
            public sealed partial class RowMap : Map<Row, RowModel>;
            """);

        // Uretecin tanisi YOK; konusan derleyici.
        run.DiagnosticIds.Should().NotContain("ZERO250");
        run.AuthoredErrorIds.Should().Contain("CS0534");
    }

    [Fact]
    public void A_declined_PROJECTION_generates_nothing()
    {
        var run = GeneratorHarness.Run("""
            using System;
            using System.Linq.Expressions;
            using IQOne.Zero;
            using IQOne.Zero.Persistence;

            namespace Test;

            public sealed class Row { public int A { get; set; } }
            public sealed class RowModel { public int A { get; set; } public int Missing { get; set; } }

            [Projection]
            [NoGenerate]
            public sealed partial class RowQuery : Specification<Row, RowModel>
            {
                public override Expression<Func<Row, RowModel>> Selector => r => new RowModel { A = r.A };
            }
            """);

        // Missing hesapsiz olmasina ragmen ZERO220 YOK: uretec hic bakmadi.
        run.DiagnosticIds.Should().NotContain("ZERO220");
        run.GeneratedSource.Should().NotContain("partial class RowQuery");
    }

    [Fact]
    public void A_declined_SERVICE_is_not_registered()
    {
        // Kendini OnConfigureServices icinde kaydeden bir tip bunu boyle soyluyor.
        var run = GeneratorHarness.Run("""
            using IQOne.Zero;
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test.Module;

            public interface IThing;

            [NoGenerate]
            public sealed class Thing : IThing, IScoped;

            public interface IOther;

            public sealed class Other : IOther, IScoped;
            """);

        run.DiagnosticMessages.Should().BeEmpty();

        run.GeneratedSource.Should().Contain("Other").And.NotContain("Thing");
    }

    [Fact]
    public void An_ASSEMBLY_can_decline_all_of_it()
    {
        // Hicbir uretim istemeyen bir proje icin ayar bu, ve paketler yerinde kaliyor.
        var run = GeneratorHarness.Run("""
            using IQOne.Zero;
            using IQOne.Zero.DependencyInjection.Descriptors;
            using IQOne.Zero.Mapping;

            [assembly: NoGenerate]

            namespace Test.Module;

            public interface IThing;

            public sealed class Thing : IThing, IScoped;

            public sealed class Row { public int A { get; set; } }
            public sealed class RowModel { public int A { get; set; } }

            public sealed partial class RowMap : Map<Row, RowModel>;
            """);

        // Ne modul, ne harita. Haritanin Selector'u soyut kaldigi icin derleyici konusuyor.
        run.GeneratedSource.Should().NotContain("OnConfigureServicesAsync");
        run.GeneratedSource.Should().NotContain("partial class RowMap");
        run.AuthoredErrorIds.Should().Contain("CS0534");
    }

    [Fact]
    public void A_type_inside_a_declined_type_is_declined_too()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero;
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test.Module;

            public interface IThing;

            [NoGenerate]
            public static class Outer
            {
                public sealed class Thing : IThing, IScoped;
            }
            """);

        run.GeneratedSource.Should().NotContain("Thing");
    }
}
