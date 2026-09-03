using IQOne.Zero.Generators.Tests.Harness;

namespace IQOne.Zero.Generators.Tests;

/// <summary>
/// The generated mapping, and the members it refuses to discard.
/// </summary>
/// <remarks>
/// A mapping holds the SOURCE to account, which is the opposite end from a projection. A
/// projection produces the shape it was asked for, so that shape must be complete; a
/// mapping writes onto something that already exists, and the danger there is a member the
/// caller sent that nothing consumed — discarded without a word, on a request that looks
/// like it worked.
/// </remarks>
public class MappingTests
{
    private const string Preamble = """
        using IQOne.Zero.Persistence;

        namespace Test;

        public sealed class Bed : IEntity<short>
        {
            public short Id { get; set; }
            public string? Name { get; set; }
            public short BuildingUnitId { get; set; }
            public short? DepartmentId { get; set; }
            public byte State { get; set; }
            public System.DateTime CreatedDate { get; set; }
        }
        """;

    [Fact]
    public void Members_that_match_by_name_are_written()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel
            {
                public string? Name { get; set; }
                public short BuildingUnitId { get; set; }
                public short? DepartmentId { get; set; }
            }

            public sealed partial class SaveBeds
            {
                [Mapping]
                private static partial void Apply(BedModel model, Bed bed);
            }
            """);

        run.HasError.Should().BeFalse();
        run.GeneratedFileErrorMessages.Should().BeEmpty();

        run.GeneratedSource.Should()
            .Contain("bed.Name = model.Name")
            .And.Contain("bed.BuildingUnitId = model.BuildingUnitId")
            .And.Contain("bed.DepartmentId = model.DepartmentId");

        // Hedefin FAZLA uyeleri yazilmiyor: State ve CreatedDate bir kanaatin isi.
        run.GeneratedSource.Should().NotContain("bed.State").And.NotContain("bed.CreatedDate");
    }

    [Fact]
    public void The_KEY_is_never_written()
    {
        // Anahtar, satirin BULUNMA yolu. Cagiranin nesnesinden atamak en iyi halde bir
        // no-op, en kotu halde baska bir satir. IEntity<TKey> uzerinden taniniyor, adindan
        // DEGIL.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel
            {
                public short Id { get; set; }
                public string? Name { get; set; }
            }

            public sealed partial class SaveBeds
            {
                [Mapping]
                private static partial void Apply(BedModel model, Bed bed);
            }
            """);

        run.HasError.Should().BeFalse();
        run.GeneratedSource.Should().Contain("bed.Name").And.NotContain("bed.Id =");
    }

    [Fact]
    public void A_source_member_nothing_consumes_is_an_ERROR()
    {
        // Tasimada tam bu vardi: BedModel.BedState geliyor, entity'de o adda kolon yok,
        // ve COMED'in mapper'i onu sessizce atliyordu. Yani bu uc noktaya bedState
        // gondermek hicbir sey yapmiyor ve istek CALISMIS gorunuyor.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel
            {
                public string? Name { get; set; }
                public byte BedState { get; set; }
            }

            public sealed partial class SaveBeds
            {
                [Mapping]
                private static partial void Apply(BedModel model, Bed bed);
            }
            """);

        run.DiagnosticIds.Should().Contain("ZERO225");
        run.Diagnostics.Single(d => d.Id == "ZERO225").GetMessage()
            .Should().Contain("BedState").And.Contain("Ignore");

        run.GeneratedSource.Should().NotContain("bed.Name =");
    }

    [Fact]
    public void An_ignored_member_is_left_alone()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel
            {
                public string? Name { get; set; }
                public byte BedState { get; set; }
            }

            public sealed partial class SaveBeds
            {
                [Mapping(Ignore = [nameof(BedModel.BedState)])]
                private static partial void Apply(BedModel model, Bed bed);
            }
            """);

        run.HasError.Should().BeFalse();
        run.GeneratedSource.Should().Contain("bed.Name = model.Name");
    }

    [Fact]
    public void An_ignore_entry_that_matches_nothing_is_reported()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel { public string? Name { get; set; } }

            public sealed partial class SaveBeds
            {
                [Mapping(Ignore = ["BedStait"])]
                private static partial void Apply(BedModel model, Bed bed);
            }
            """);

        run.DiagnosticIds.Should().Contain("ZERO226");
    }

    [Fact]
    public void A_NULLABLE_source_into_a_non_nullable_target_is_REFUSED()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel
            {
                public short? BuildingUnitId { get; set; }
            }

            public sealed partial class SaveBeds
            {
                [Mapping]
                private static partial void Apply(BedModel model, Bed bed);
            }
            """);

        run.DiagnosticIds.Should().Contain("ZERO225");
        run.Diagnostics.Single(d => d.Id == "ZERO225").GetMessage().Should().Contain("nullable");
    }

    [Fact]
    public void A_read_only_target_member_is_REFUSED()
    {
        // Hedefte ayni adda bir uye var ama SET edilemiyor. Sessizce atlamak, cagiranin
        // gonderdigi alani yok saymak olurdu.
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.Persistence;

            namespace Test;

            public sealed class Row : IEntity<int>
            {
                public int Id { get; set; }
                public string? Computed { get; } = "x";
            }

            public sealed class RowModel { public string? Computed { get; set; } }

            public sealed partial class Save
            {
                [Mapping]
                private static partial void Apply(RowModel model, Row row);
            }
            """);

        run.DiagnosticIds.Should().Contain("ZERO225");
    }

    [Theory]
    [InlineData("private static partial int Apply(BedModel model, Bed bed);", "returns")]
    [InlineData("private static partial void Apply(BedModel model);", "parameters")]
    [InlineData("private partial void Apply(BedModel model, Bed bed);", "static")]
    public void A_signature_of_the_wrong_shape_is_reported(string signature, string says)
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel { public string? Name { get; set; } }

            public sealed partial class SaveBeds
            {
                [Mapping]
                {{signature}}
            }
            """);

        run.DiagnosticIds.Should().Contain("ZERO227");
        run.Diagnostics.Single(d => d.Id == "ZERO227").GetMessage().Should().Contain(says);
    }

    [Fact]
    public void A_non_partial_container_is_reported()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel { public string? Name { get; set; } }

            public sealed class SaveBeds
            {
                [Mapping]
                private static partial void Apply(BedModel model, Bed bed);
            }
            """);

        run.DiagnosticIds.Should().Contain("ZERO228");
    }

    [Fact]
    public void The_other_shape_PRODUCES_a_new_object()
    {
        // Ucuncu yon: entity -> model, ama bellekte. [Projection] bunu yalnizca bir
        // Specification'in Selector'u olarak yapiyor — yani veritabaninda. Kaydettikten
        // sonra elindeki entity'den model uretmek icin ifade agacini Compile() etmek
        // gerekiyordu, ki bu tam olarak eksik yetenegin isaretiydi.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel
            {
                public short Id { get; set; }
                public string? Name { get; set; }
                public short BuildingUnitId { get; set; }
            }

            public sealed partial class SaveBeds
            {
                [Mapping]
                private static partial BedModel ToModel(Bed bed);
            }
            """);

        run.HasError.Should().BeFalse();
        run.GeneratedFileErrorMessages.Should().BeEmpty();

        run.GeneratedSource.Should()
            .Contain("return new global::Test.BedModel")
            .And.Contain("Name = bed.Name")
            // Uretirken ANAHTAR YAZILIR: sonucun parcasi ve eksik birakmak eksik bir
            // nesne demek. Uzerine yazarken atlanir — orada satirin bulunma yolu.
            .And.Contain("Id = bed.Id");
    }

    [Fact]
    public void When_PRODUCING_the_RESULT_is_held_to_account()
    {
        // Yon degisince hesap veren taraf da degisiyor. Uzerine yazarken kaynak, uretirken
        // sonuc. Tek cumle: kurdugun sey tam olmali, tukettigin sey tukenmeli.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel
            {
                public string? Name { get; set; }
                public string? Tags { get; set; }        // Bed'de yok
            }

            public sealed partial class SaveBeds
            {
                [Mapping]
                private static partial BedModel ToModel(Bed bed);
            }
            """);

        run.DiagnosticIds.Should().Contain("ZERO229");
        run.Diagnostics.Single(d => d.Id == "ZERO229").GetMessage().Should().Contain("Tags");
    }

    [Fact]
    public void When_PRODUCING_the_target_may_be_narrower_than_the_source()
    {
        // Ters yon: Bed'in State ve CreatedDate'i var, model tasimiyor. Sorun degil —
        // hesap sorulan taraf sonuc.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel { public string? Name { get; set; } }

            public sealed partial class SaveBeds
            {
                [Mapping]
                private static partial BedModel ToModel(Bed bed);
            }
            """);

        run.HasError.Should().BeFalse();
        run.GeneratedSource.Should().Contain("Name = bed.Name");
    }

    [Fact]
    public void A_model_can_be_produced_from_another_MODEL()
    {
        // Ikisi de entity degil. Uretec entity kavramina bagli degil: iki tip ve bir sekil.
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.Persistence;

            namespace Test;

            public sealed class BedModel
            {
                public short Id { get; set; }
                public string? Name { get; set; }
            }

            public sealed class BedSummary
            {
                public short Id { get; set; }
                public string? Name { get; set; }
            }

            public sealed partial class Summaries
            {
                [Mapping]
                internal static partial BedSummary Of(BedModel model);
            }
            """);

        run.HasError.Should().BeFalse();
        run.GeneratedFileErrorMessages.Should().BeEmpty();
        run.GeneratedSource.Should().Contain("internal static partial global::Test.BedSummary Of");
    }

    [Fact]
    public void A_NULLABLE_parameter_is_repeated_exactly()
    {
        // Partial'in iki yarisi nullable dahil TAM eslemek zorunda (CS8611/CS8819).
        // Isaret dusurulse, BedModel? ile bildirilen bir metot gerceklestirilemez hale
        // gelirdi — ve hata uretilmis dosyayi gosterirdi.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel { public string? Name { get; set; } }

            public sealed partial class SaveBeds
            {
                [Mapping]
                private static partial BedModel? ToModel(Bed bed);
            }
            """);

        run.GeneratedFileErrorMessages.Should().BeEmpty();
        run.GeneratedSource.Should().Contain("global::Test.BedModel? ToModel");
    }

    [Fact]
    public void Two_parameters_AND_a_return_is_reported()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel { public string? Name { get; set; } }

            public sealed partial class SaveBeds
            {
                [Mapping]
                private static partial BedModel Apply(BedModel model, Bed bed);
            }
            """);

        run.DiagnosticIds.Should().Contain("ZERO227");
        run.Diagnostics.Single(d => d.Id == "ZERO227").GetMessage()
            .Should().Contain("produces a new object");
    }
}
