using IQOne.Zero.Generators.Tests.Harness;

namespace IQOne.Zero.Generators.Tests;

/// <summary>
/// A specification's selector, taken from the map for its pair.
/// </summary>
/// <remarks>
/// A specification already names two shapes in its base type. If a map is declared for that
/// pair there is nothing left for the specification to say, and saying it again is how the two
/// come to disagree. Purely additive: a specification that writes its own selector, declines
/// generation, or has no map is left exactly as it was.
/// </remarks>
public class SpecificationSelectorTests
{
    private const string Preamble = """
        using System;
        using System.Linq.Expressions;
        using IQOne.Zero.Mapping;
        using IQOne.Zero.Persistence;

        namespace Test;

        public enum BedState : byte { Unknown = 0, Empty = 1 }

        public sealed class Bed
        {
            public short Id { get; set; }
            public string? Name { get; set; }
            public byte State { get; set; }
        }

        public sealed class BedModel
        {
            public short Id { get; set; }
            public string? Name { get; set; }
            public BedState BedState { get; set; }
        }

        public sealed partial class BedMap : Map<Bed, BedModel>
        {
            protected override void Configure(IMapBuilder<Bed, BedModel> map)
                => map.Member(m => m.BedState, e => (BedState)e.State);
        }
        """;

    [Fact]
    public void A_specification_takes_its_selector_from_the_map()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed partial class BedQuery : Specification<Bed, BedModel>
            {
                public BedQuery(short id) => Where(e => e.Id == id);
            }
            """);

        run.DiagnosticMessages.Should().BeEmpty();
        run.GeneratedFileErrorMessages.Should().BeEmpty();
        run.AuthoredErrorIds.Should().BeEmpty();

        run.GeneratedSource.Should()
            .Contain("partial class BedQuery")
            .And.Contain("private static readonly global::Test.BedMap")
            .And.Contain("Selector")
            .And.Contain(".Selector;");
    }

    [Fact]
    public void A_HAND_WRITTEN_selector_is_left_alone()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed partial class BedQuery : Specification<Bed, BedModel>
            {
                public override Expression<Func<Bed, BedModel>> Selector =>
                    e => new BedModel { Id = e.Id, Name = e.Name, BedState = BedState.Unknown };
            }
            """);

        run.DiagnosticMessages.Should().BeEmpty();
        run.GeneratedFileErrorMessages.Should().BeEmpty();
        run.GeneratedSource.Should().NotContain("partial class BedQuery");
    }

    [Fact]
    public void A_DECLINED_specification_is_left_alone()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            [IQOne.Zero.NoGenerate]
            public sealed partial class BedQuery : Specification<Bed, BedModel>
            {
                public override Expression<Func<Bed, BedModel>> Selector => e => new BedModel { Id = e.Id };
            }
            """);

        run.GeneratedSource.Should().NotContain("partial class BedQuery");
    }

    [Fact]
    public void With_NO_map_for_the_pair_nothing_is_generated_and_nothing_is_said()
    {
        // Dil zaten uyeyi zorunlu kiliyor ve CS0534 tam olarak onu soyluyor; ustune bir
        // tani koymak ayni sorun icin iki hata olurdu.
        var run = GeneratorHarness.Run("""
            using System;
            using System.Linq.Expressions;
            using IQOne.Zero.Persistence;

            namespace Test;

            public sealed class Row { public int A { get; set; } }
            public sealed class RowModel { public int A { get; set; } }

            public sealed partial class RowQuery : Specification<Row, RowModel>;
            """);

        run.DiagnosticMessages.Should().BeEmpty();
        run.GeneratedSource.Should().NotContain("partial class RowQuery");

        // Konusan derleyici.
        run.AuthoredErrorIds.Should().Contain("CS0534");
    }

    [Fact]
    public void TWO_maps_for_one_pair_are_reported()
    {
        // Cift basina bir harita, cunku ona ulasan her sey CIFT uzerinden ulasiyor: ic ice
        // bir uyeyi yazan kompozisyon, secicisini alan sartname.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed partial class OtherBedMap : Map<Bed, BedModel>
            {
                protected override void Configure(IMapBuilder<Bed, BedModel> map)
                    => map.Ignore(m => m.BedState);
            }
            """);

        run.DiagnosticIds.Should().Contain("ZERO257");
        run.DiagnosticMessages.Should().Contain(m => m.Contains("BedMap") && m.Contains("OtherBedMap"));
    }

    [Fact]
    public void A_specification_through_the_applications_OWN_base_still_finds_the_map()
    {
        // Uygulamalar araya kendi katmanini koyuyor — her sorguya sayfalama ve silinmislik
        // kurallarini uygulayan bir taban — ve o taban hala ayni iki sekli adlandiriyor.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public abstract class LegacyQuery<T, TResult> : Specification<T, TResult> where T : class
            {
                protected LegacyQuery() => Page(null, 100);
            }

            public sealed partial class BedQuery : LegacyQuery<Bed, BedModel>;
            """);

        run.DiagnosticMessages.Should().BeEmpty();
        run.AuthoredErrorIds.Should().BeEmpty();
        run.GeneratedSource.Should().Contain("partial class BedQuery");
    }
}
