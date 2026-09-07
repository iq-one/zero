using IQOne.Zero.Generators.Tests.Harness;

namespace IQOne.Zero.Generators.Tests;

/// <summary>
/// The selector a map's declaration produces, written as source.
/// </summary>
/// <remarks>
/// Two things are being tested and they are the reason the generator exists. The DESTINATION is
/// held to account: every member of it is filled by name, filled by a declaration, or declared
/// empty, and a fourth case is a build error rather than a field found missing in production.
/// And what a member gets is READABLE — the assertions here are about generated source, because
/// that is what somebody maintaining a map will open.
/// </remarks>
public class MapTests
{
    private const string Preamble = """
        using System;
        using IQOne.Zero.Mapping;

        namespace Test;

        public enum BedState : byte { Unknown = 0, Empty = 1, Occupied = 2 }

        public sealed class Bed
        {
            public short Id { get; set; }
            public string? Name { get; set; }
            public string? Code { get; set; }
            public short BuildingUnitId { get; set; }
            public short? DepartmentId { get; set; }
            public byte State { get; set; }
            public DateTime CreatedDate { get; set; }
            public BedType? BedType { get; set; }
        }

        public sealed class BedType
        {
            public short Id { get; set; }
            public string? Name { get; set; }
        }

        public sealed class BedTypeModel
        {
            public short Id { get; set; }
            public string? Name { get; set; }
        }
        """;

    [Fact]
    public void Members_that_match_by_name_need_no_declaration()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel
            {
                public short Id { get; set; }
                public string? Name { get; set; }
                public short BuildingUnitId { get; set; }
                public short? DepartmentId { get; set; }
            }

            public sealed partial class BedMap : Map<Bed, BedModel>;
            """);

        run.DiagnosticMessages.Should().BeEmpty();
        run.GeneratedFileErrorMessages.Should().BeEmpty();

        run.GeneratedSource.Should()
            .Contain("Id = source.Id")
            .And.Contain("Name = source.Name")
            .And.Contain("BuildingUnitId = source.BuildingUnitId")
            .And.Contain("DepartmentId = source.DepartmentId");

        // Kaynagin FAZLA uyeleri hicbir sey yapmiyor: hesabi verilen uc HEDEF.
        run.GeneratedSource.Should().NotContain("State").And.NotContain("CreatedDate");
    }

    [Fact]
    public void A_destination_member_nothing_fills_is_an_ERROR()
    {
        // Bu satir butun yapinin varlik sebebi. PUSULA'nin mapper'i bu uyeyi sessizce
        // dusurup devam ediyor ve alan null donuyor.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel
            {
                public short Id { get; set; }
                public BedState BedState { get; set; }
            }

            public sealed partial class BedMap : Map<Bed, BedModel>;
            """);

        run.DiagnosticIds.Should().Contain("ZERO250");
        run.DiagnosticMessages.Should().Contain(m => m.Contains("BedState"));
    }

    [Fact]
    public void A_declaration_fills_the_member_and_is_copied_into_the_tree()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel
            {
                public short Id { get; set; }
                public BedState BedState { get; set; }
            }

            public sealed partial class BedMap : Map<Bed, BedModel>
            {
                protected override void Configure(IMapBuilder<Bed, BedModel> map)
                    => map.Member(m => m.BedState, e => (BedState)e.State);
            }
            """);

        run.DiagnosticMessages.Should().BeEmpty();
        run.GeneratedFileErrorMessages.Should().BeEmpty();

        // Yazdigin ifade, parametresi ureticinin adina cevrilmis olarak agacta duruyor.
        run.GeneratedSource.Should().Contain("BedState = (BedState)source.State");
    }

    [Fact]
    public void The_declarations_OWN_parameter_name_is_rewritten()
    {
        // Her bildirim kendi parametre adiyla yaziliyor; uretilen agacin bir tane var.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel
            {
                public short Id { get; set; }
                public BedState BedState { get; set; }
                public string? Where { get; set; }
            }

            public sealed partial class BedMap : Map<Bed, BedModel>
            {
                protected override void Configure(IMapBuilder<Bed, BedModel> map)
                {
                    map.Member(m => m.BedState, bed => (BedState)bed.State);
                    map.Member(m => m.Where, x => x.Code + "/" + x.Name);
                }
            }
            """);

        run.DiagnosticMessages.Should().BeEmpty();
        run.GeneratedFileErrorMessages.Should().BeEmpty();

        run.GeneratedSource.Should()
            .Contain("BedState = (BedState)source.State")
            .And.Contain("Where = source.Code + \"/\" + source.Name");
    }

    [Fact]
    public void An_ignored_member_is_left_out_and_still_accounted_for()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel
            {
                public short Id { get; set; }
                public BedTypeModel? BedType { get; set; }
            }

            public sealed partial class BedMap : Map<Bed, BedModel>
            {
                protected override void Configure(IMapBuilder<Bed, BedModel> map)
                    => map.Ignore(m => m.BedType);
            }
            """);

        run.DiagnosticMessages.Should().BeEmpty();
        run.GeneratedFileErrorMessages.Should().BeEmpty();

        run.GeneratedSource.Should().Contain("Id = source.Id").And.NotContain("BedType =");
    }

    [Fact]
    public void A_chain_of_declarations_reads_in_source_order()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel
            {
                public short Id { get; set; }
                public BedState BedState { get; set; }
                public BedTypeModel? BedType { get; set; }
            }

            public sealed partial class BedMap : Map<Bed, BedModel>
            {
                protected override void Configure(IMapBuilder<Bed, BedModel> map) => map
                    .Member(m => m.BedState, e => (BedState)e.State)
                    .Ignore(m => m.BedType);
            }
            """);

        run.DiagnosticMessages.Should().BeEmpty();
        run.GeneratedFileErrorMessages.Should().BeEmpty();

        run.GeneratedSource.Should().Contain("BedState = (BedState)source.State");
    }

    [Fact]
    public void A_NULLABLE_source_into_a_non_nullable_member_is_REFUSED()
    {
        // Sessiz varsayilan bir KARAR, ve okunabilecek bir yerde durmasi gerekiyor.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel
            {
                public short Id { get; set; }
                public short DepartmentId { get; set; }
            }

            public sealed partial class BedMap : Map<Bed, BedModel>;
            """);

        run.DiagnosticIds.Should().Contain("ZERO250");
        run.DiagnosticMessages.Should().Contain(m => m.Contains("absent value becomes"));
    }

    [Fact]
    public void A_member_named_twice_is_reported()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel
            {
                public short Id { get; set; }
                public BedState BedState { get; set; }
            }

            public sealed partial class BedMap : Map<Bed, BedModel>
            {
                protected override void Configure(IMapBuilder<Bed, BedModel> map) => map
                    .Member(m => m.BedState, e => (BedState)e.State)
                    .Ignore(m => m.BedState);
            }
            """);

        run.DiagnosticIds.Should().Contain("ZERO253");
    }

    [Fact]
    public void A_declaration_naming_something_the_destination_does_not_have_is_reported()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel
            {
                public short Id { get; set; }
                public string? Name { get; set; }
                public string FullName => Name ?? "";
            }

            public sealed partial class BedMap : Map<Bed, BedModel>
            {
                protected override void Configure(IMapBuilder<Bed, BedModel> map)
                    => map.Ignore(m => m.FullName);
            }
            """);

        run.DiagnosticIds.Should().Contain("ZERO252");
        run.DiagnosticMessages.Should().Contain(m => m.Contains("read-only"));
    }

    [Fact]
    public void A_body_the_generator_cannot_READ_is_reported()
    {
        // Configure okunuyor, kosmuyor. Calisma zamaninda ne maplenecegine karar veren bir
        // govdenin okunacak tek bir cevabi yok — ve sessizce atlanmasi, bu tasarimin yok
        // etmek icin var oldugu hatanin aynisi olurdu.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel
            {
                public short Id { get; set; }
                public BedState BedState { get; set; }
            }

            public sealed partial class BedMap : Map<Bed, BedModel>
            {
                protected override void Configure(IMapBuilder<Bed, BedModel> map)
                {
                    if (DateTime.Now.Year > 2020)
                        map.Member(m => m.BedState, e => (BedState)e.State);
                }
            }
            """);

        run.DiagnosticIds.Should().Contain("ZERO256");
    }

    [Fact]
    public void A_map_that_is_not_partial_is_reported()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel { public short Id { get; set; } }

            public sealed class BedMap : Map<Bed, BedModel>;
            """);

        run.DiagnosticIds.Should().Contain("ZERO258");
    }

    [Fact]
    public void The_tree_is_static_and_the_compiled_form_is_LAZY()
    {
        // Cift basina BIR agac, ornek basina degil; ve derleme yalnizca bellek yolu
        // istendiginde — olcume gore derleme 68 us, tek bir esleme 9,5 ns.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel { public short Id { get; set; } }

            public sealed partial class BedMap : Map<Bed, BedModel>;
            """);

        run.GeneratedFileErrorMessages.Should().BeEmpty();

        run.GeneratedSource.Should()
            .Contain("private static readonly")
            .And.Contain("Lazy<")
            .And.Contain("LazyThreadSafetyMode.ExecutionAndPublication")
            .And.Contain("Selector => Tree")
            .And.Contain("Project => Compiled.Value");
    }

    [Fact]
    public void The_declaring_files_USINGS_come_along()
    {
        // Bildirimin ifadesi AYNEN kopyalaniyor, yani adlari yazildigi dosyanin cozdugu
        // gibi cozuyor. Using'ler tasinmazsa import edilmis bir tipe yapilan cast
        // uretilen dosyada cozumsuz kalir — ve hata kimsenin yazmadigi bir dosyayi
        // gosterir.
        var run = GeneratorHarness.Run("""
            using System;
            using IQOne.Zero.Mapping;
            using Test.Elsewhere;

            namespace Test.Elsewhere
            {
                public enum Grade : byte { None = 0, High = 1 }
            }

            namespace Test
            {
                public sealed class Row { public byte Level { get; set; } }

                public sealed class RowModel { public Grade Level { get; set; } }

                public sealed partial class RowMap : Map<Row, RowModel>
                {
                    protected override void Configure(IMapBuilder<Row, RowModel> map)
                        => map.Member(m => m.Level, e => (Grade)e.Level);
                }
            }
            """);

        run.DiagnosticMessages.Should().BeEmpty();

        // Asil sinav: uretilen dosya DERLENIYOR mu — cast'in adi cozuluyor mu.
        run.GeneratedFileErrorMessages.Should().BeEmpty();

        run.GeneratedSource.Should()
            .Contain("using Test.Elsewhere;")
            .And.Contain("Level = (Grade)source.Level");
    }

    [Fact]
    public void A_HAND_WRITTEN_selector_makes_the_generator_stand_down()
    {
        // Kacis yolu. Uretilen koda mudahale edilemez — derleme sirasinda uretiliyor ve
        // derleyicinin geri okudugu bir dosya yok — o yuzden uretecin bir harita hakkinda
        // yanlis olmasi, kullanicisinin derlemesini durdurup elinde bir sey birakmamak
        // olurdu. Ozelligi bildirmek haritayi devraliyor.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            using System.Linq.Expressions;

            public sealed class BedModel
            {
                public short Id { get; set; }
                public string? Whatever { get; set; }   // uretecin kaynagi bulamayacagi bir uye
            }

            public sealed partial class BedMap : Map<Bed, BedModel>
            {
                public override Expression<Func<Bed, BedModel>> Selector { get; }
                    = e => new BedModel { Id = e.Id, Whatever = e.Name };
            }
            """);

        // Ne tani, ne uretilen kod: harita tamamen yazarin.
        run.DiagnosticMessages.Should().BeEmpty();
        run.GeneratedFileErrorMessages.Should().BeEmpty();
        run.GeneratedSource.Should().NotContain("partial class BedMap");
    }

    [Fact]
    public void Project_still_works_when_the_selector_is_hand_written()
    {
        // Elle secici yazan bir haritanin Project'i de yazmasi gerekmiyor: taban sinif onu
        // seciciden turetiyor.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            using System.Linq.Expressions;

            public sealed class BedModel { public short Id { get; set; } }

            public sealed partial class BedMap : Map<Bed, BedModel>
            {
                public override Expression<Func<Bed, BedModel>> Selector { get; }
                    = e => new BedModel { Id = e.Id };
            }

            public static class Use
            {
                public static BedModel Once(Bed bed) => new BedMap().Project(bed);
            }
            """);

        run.DiagnosticMessages.Should().BeEmpty();
        run.GeneratedFileErrorMessages.Should().BeEmpty();
    }
}
