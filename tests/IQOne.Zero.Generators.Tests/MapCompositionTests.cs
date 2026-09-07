using IQOne.Zero.Generators.Tests.Harness;

namespace IQOne.Zero.Generators.Tests;

/// <summary>
/// One map written out inside another.
/// </summary>
/// <remarks>
/// Composition is what stops the same nested block being copied into every query that returns
/// the model, which is how two of them end up disagreeing. It also brings two things a
/// hand-written nested initialiser has to remember: the null check, without which a missing row
/// gives an object full of zeros rather than nothing, and a circle — which here is a build error
/// naming the loop rather than a stack overflow or a silent depth limit.
/// </remarks>
public class MapCompositionTests
{
    private const string Preamble = """
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using IQOne.Zero.Mapping;

        namespace Test;

        public sealed class Bed
        {
            public short Id { get; set; }
            public string? Name { get; set; }
            public BedType? BedType { get; set; }
            public ICollection<Tag> Tags { get; set; } = [];
        }

        public sealed class BedType
        {
            public short Id { get; set; }
            public string? Name { get; set; }
            public byte State { get; set; }
        }

        public sealed class Tag
        {
            public int Id { get; set; }
            public string? Text { get; set; }
        }

        public sealed class BedTypeModel
        {
            public short Id { get; set; }
            public string? Name { get; set; }
        }

        public sealed class TagModel
        {
            public int Id { get; set; }
            public string? Text { get; set; }
        }

        public sealed partial class BedTypeMap : Map<BedType, BedTypeModel>;

        public sealed partial class TagMap : Map<Tag, TagModel>;
        """;

    [Fact]
    public void A_nested_object_is_written_out_with_a_NULL_CHECK()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel
            {
                public short Id { get; set; }
                public string? Name { get; set; }
                public BedTypeModel? BedType { get; set; }
            }

            public sealed partial class BedMap : Map<Bed, BedModel>
            {
                protected override void Configure(IMapBuilder<Bed, BedModel> map)
                    => map.Member(m => m.BedType, e => e.BedType.To<BedTypeModel>());
            }
            """);

        run.DiagnosticMessages.Should().BeEmpty();
        run.GeneratedFileErrorMessages.Should().BeEmpty();

        // Cocugun agaci EBEVEYNIN icine yazildi, ve okudugu yer ebeveynin buldugu yer.
        run.GeneratedSource.Should()
            .Contain("BedType = source.BedType == null ? null : new global::Test.BedTypeModel")
            .And.Contain("Id = source.BedType.Id")
            .And.Contain("Name = source.BedType.Name");

        // Tek bir ifade agaci: cagri yok, yani saglayici cevirebilir.
        run.GeneratedSource.Should().NotContain("BedTypeMap.Selector").And.NotContain(".To<");
    }

    [Fact]
    public void The_null_check_is_there_because_a_missing_row_must_be_NOTHING()
    {
        // Elle yazilan `new BedTypeModel { Id = e.BedType.Id }`, satir yoksa Id = 0 olan bir
        // NESNE veriyor, null degil. Insanin atladigi sey bu; uretec atlamiyor.
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
                    => map.Member(m => m.BedType, e => e.BedType.To<BedTypeModel>());
            }
            """);

        run.Occurrences("== null ? null :").Should().Be(1);
    }

    [Fact]
    public void A_nested_object_whose_member_is_NOT_nullable_is_refused()
    {
        // Yoklugun ne olacagi bir karar, ve okunabilecek bir yerde durmasi gerekiyor —
        // nullable deger tipinde oldugu gibi.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel
            {
                public short Id { get; set; }
                public BedTypeModel BedType { get; set; } = new();
            }

            public sealed partial class BedMap : Map<Bed, BedModel>
            {
                protected override void Configure(IMapBuilder<Bed, BedModel> map)
                    => map.Member(m => m.BedType, e => e.BedType.To<BedTypeModel>());
            }
            """);

        run.DiagnosticIds.Should().Contain("ZERO256");
        run.DiagnosticMessages.Should().Contain(m => m.Contains("absent value becomes"));
    }

    [Fact]
    public void A_COLLECTION_is_written_out_with_Select_and_a_materialiser()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel
            {
                public short Id { get; set; }
                public List<TagModel>? Tags { get; set; }
            }

            public sealed partial class BedMap : Map<Bed, BedModel>
            {
                protected override void Configure(IMapBuilder<Bed, BedModel> map)
                    => map.Member(m => m.Tags, e => e.Tags.To<List<TagModel>>());
            }
            """);

        run.DiagnosticMessages.Should().BeEmpty();
        run.GeneratedFileErrorMessages.Should().BeEmpty();

        run.GeneratedSource.Should()
            .Contain("Tags = source.Tags.Select(")
            .And.Contain("new global::Test.TagModel")
            .And.Contain(".ToList()");

        // Diziye gore materyallestirici degisiyor; burada liste, yani ToArray YOK.
        run.GeneratedSource.Should().NotContain(".ToArray()");
    }

    [Fact]
    public void An_ARRAY_member_is_materialised_with_ToArray()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel
            {
                public short Id { get; set; }
                public TagModel[]? Tags { get; set; }
            }

            public sealed partial class BedMap : Map<Bed, BedModel>
            {
                protected override void Configure(IMapBuilder<Bed, BedModel> map)
                    => map.Member(m => m.Tags, e => e.Tags.To<TagModel[]>());
            }
            """);

        run.DiagnosticMessages.Should().BeEmpty();
        run.GeneratedFileErrorMessages.Should().BeEmpty();

        run.GeneratedSource.Should().Contain(".ToArray()");
    }

    [Fact]
    public void A_collection_is_NOT_guarded_and_that_is_a_decision()
    {
        // Sorguda alici bir alt sorgu, asla null degil; saglayicinin materyallestirdigi bir
        // entity'nin koleksiyonlari da kurulmus geliyor. Korumak, olmayan bir durum icin her
        // SELECT'e bir kontrol koymak olurdu.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel
            {
                public short Id { get; set; }
                public List<TagModel>? Tags { get; set; }
            }

            public sealed partial class BedMap : Map<Bed, BedModel>
            {
                protected override void Configure(IMapBuilder<Bed, BedModel> map)
                    => map.Member(m => m.Tags, e => e.Tags.To<List<TagModel>>());
            }
            """);

        run.GeneratedSource.Should().NotContain("== null ?");
    }

    [Fact]
    public void The_SET_can_be_filtered_and_the_generator_writes_the_shape()
    {
        // Sen KUMEYI soyluyorsun, uretec sekli yaziyor.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class BedModel
            {
                public short Id { get; set; }
                public List<TagModel>? Tags { get; set; }
            }

            public sealed partial class BedMap : Map<Bed, BedModel>
            {
                protected override void Configure(IMapBuilder<Bed, BedModel> map)
                    => map.Member(m => m.Tags, e => e.Tags.Where(t => t.Id > 0).To<List<TagModel>>());
            }
            """);

        run.DiagnosticMessages.Should().BeEmpty();
        run.GeneratedFileErrorMessages.Should().BeEmpty();

        run.GeneratedSource.Should().Contain("source.Tags.Where(t => t.Id > 0).Select(");
    }

    [Fact]
    public void A_CIRCLE_is_a_build_error_naming_the_loop()
    {
        // MaxDepth'in yapamadigi sey. Ve bunu yapan sekiller siradan: bolumu olan bir yatak,
        // yataklarini listeleyen bir bolum.
        var run = GeneratorHarness.Run("""
            using System;
            using System.Collections.Generic;
            using IQOne.Zero.Mapping;

            namespace Test;

            public sealed class Bed
            {
                public short Id { get; set; }
                public Department? Department { get; set; }
            }

            public sealed class Department
            {
                public short Id { get; set; }
                public ICollection<Bed> Beds { get; set; } = [];
            }

            public sealed class BedModel
            {
                public short Id { get; set; }
                public DepartmentModel? Department { get; set; }
            }

            public sealed class DepartmentModel
            {
                public short Id { get; set; }
                public List<BedModel>? Beds { get; set; }
            }

            public sealed partial class BedMap : Map<Bed, BedModel>
            {
                protected override void Configure(IMapBuilder<Bed, BedModel> map)
                    => map.Member(m => m.Department, e => e.Department.To<DepartmentModel>());
            }

            public sealed partial class DepartmentMap : Map<Department, DepartmentModel>
            {
                protected override void Configure(IMapBuilder<Department, DepartmentModel> map)
                    => map.Member(m => m.Beds, e => e.Beds.To<List<BedModel>>());
            }
            """);

        run.DiagnosticIds.Should().Contain("ZERO254");
        run.DiagnosticMessages.Should().Contain(m => m.Contains("Bed") && m.Contains("Department"));
    }

    [Fact]
    public void A_circle_BROKEN_with_Ignore_generates()
    {
        var run = GeneratorHarness.Run("""
            using System;
            using System.Collections.Generic;
            using IQOne.Zero.Mapping;

            namespace Test;

            public sealed class Bed
            {
                public short Id { get; set; }
                public Department? Department { get; set; }
            }

            public sealed class Department
            {
                public short Id { get; set; }
                public ICollection<Bed> Beds { get; set; } = [];
            }

            public sealed class BedModel
            {
                public short Id { get; set; }
                public DepartmentModel? Department { get; set; }
            }

            public sealed class DepartmentModel
            {
                public short Id { get; set; }
                public List<BedModel>? Beds { get; set; }
            }

            public sealed partial class BedMap : Map<Bed, BedModel>
            {
                protected override void Configure(IMapBuilder<Bed, BedModel> map)
                    => map.Member(m => m.Department, e => e.Department.To<DepartmentModel>());
            }

            public sealed partial class DepartmentMap : Map<Department, DepartmentModel>
            {
                protected override void Configure(IMapBuilder<Department, DepartmentModel> map)
                    => map.Ignore(m => m.Beds);
            }
            """);

        run.DiagnosticMessages.Should().BeEmpty();
        run.GeneratedFileErrorMessages.Should().BeEmpty();

        run.GeneratedSource.Should().Contain("Department = source.Department == null ? null :");
    }

    [Fact]
    public void A_pair_with_no_map_declared_is_reported()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class Elsewhere { public int Id { get; set; } }

            public sealed class BedModel
            {
                public short Id { get; set; }
                public Elsewhere? Other { get; set; }
            }

            public sealed partial class BedMap : Map<Bed, BedModel>
            {
                protected override void Configure(IMapBuilder<Bed, BedModel> map)
                    => map.Member(m => m.Other, e => e.BedType.To<Elsewhere>());
            }
            """);

        run.DiagnosticIds.Should().Contain("ZERO255");
        run.DiagnosticMessages.Should().Contain(m => m.Contains("Map<"));
    }

    [Fact]
    public void A_composition_buried_in_a_larger_expression_is_reported()
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
                    => map.Member(m => m.BedType,
                        e => e.BedType == null ? null : e.BedType.To<BedTypeModel>());
            }
            """);

        run.DiagnosticIds.Should().Contain("ZERO256");
        run.DiagnosticMessages.Should().Contain(m => m.Contains("whole member"));
    }

    [Fact]
    public void Two_levels_of_nesting_are_written_out()
    {
        var run = GeneratorHarness.Run("""
            using System;
            using System.Collections.Generic;
            using IQOne.Zero.Mapping;

            namespace Test;

            public sealed class Bed { public short Id { get; set; } public BedType? BedType { get; set; } }
            public sealed class BedType { public short Id { get; set; } public Service? Service { get; set; } }
            public sealed class Service { public int Id { get; set; } public string? Name { get; set; } }

            public sealed class BedModel { public short Id { get; set; } public BedTypeModel? BedType { get; set; } }
            public sealed class BedTypeModel { public short Id { get; set; } public ServiceModel? Service { get; set; } }
            public sealed class ServiceModel { public int Id { get; set; } public string? Name { get; set; } }

            public sealed partial class ServiceMap : Map<Service, ServiceModel>;

            public sealed partial class BedTypeMap : Map<BedType, BedTypeModel>
            {
                protected override void Configure(IMapBuilder<BedType, BedTypeModel> map)
                    => map.Member(m => m.Service, e => e.Service.To<ServiceModel>());
            }

            public sealed partial class BedMap : Map<Bed, BedModel>
            {
                protected override void Configure(IMapBuilder<Bed, BedModel> map)
                    => map.Member(m => m.BedType, e => e.BedType.To<BedTypeModel>());
            }
            """);

        run.DiagnosticMessages.Should().BeEmpty();
        run.GeneratedFileErrorMessages.Should().BeEmpty();

        // Ic ice ikinci seviye, ebeveynin ebeveyninden okuyor.
        run.GeneratedSource.Should().Contain("Name = source.BedType.Service.Name");
    }
}
