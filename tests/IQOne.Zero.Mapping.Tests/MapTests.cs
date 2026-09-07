using System.Collections.Generic;
using System.Linq;

namespace IQOne.Zero.Mapping.Tests;

/// <summary>
/// The generated maps, run.
/// </summary>
/// <remarks>
/// The generator's own tests assert on the source it writes; these compile that source into a
/// real assembly and execute it, which is the only way to know the tree is the shape it looks
/// like. <c>Selector</c> is also asserted on directly, because a query provider gets the tree
/// and never the delegate.
/// </remarks>
public class MapTests
{
    private static readonly BedMap Map = new();

    private static Bed Row() => new()
    {
        Id = 5,
        Name = "Yatak 5",
        DepartmentId = 11,
        State = 2,
        RecordStamp = [1, 2, 3],
        BedType = new BedType { Id = 7, Name = "Normal", State = 1 },
        Tags = [new Tag { Id = 1, Text = "pencere" }, new Tag { Id = 2, Text = "yakin" }]
    };

    [Fact]
    public void Members_that_match_by_name_carry_across()
    {
        var model = Map.Project(Row());

        model.Id.Should().Be(5);
        model.Name.Should().Be("Yatak 5");
        model.DepartmentId.Should().Be(11);
    }

    [Fact]
    public void A_declared_member_uses_what_the_declaration_says()
    {
        Map.Project(Row()).BedState.Should().Be(BedState.Occupied);
    }

    [Fact]
    public void A_composed_object_is_filled_by_the_other_map()
    {
        var model = Map.Project(Row());

        model.BedType.Should().NotBeNull();
        model.BedType!.Id.Should().Be(7);
        model.BedType.Name.Should().Be("Normal");
    }

    [Fact]
    public void A_composed_object_is_NULL_when_the_navigation_is_absent()
    {
        // Elle yazilan ic ice bir baslangiclandirici burada Id = 0 olan bir NESNE verir.
        // Insanin atladigi sey bu.
        var row = Row();
        row.BedType = null;

        Map.Project(row).BedType.Should().BeNull();
    }

    [Fact]
    public void A_composed_collection_maps_every_element()
    {
        var model = Map.Project(Row());

        model.Tags.Should().NotBeNull();
        model.Tags!.Select(t => t.Text).Should().Equal(["pencere", "yakin"]);
        model.Tags!.Select(t => t.Id).Should().Equal([1, 2]);
    }

    [Fact]
    public void An_empty_collection_maps_to_an_empty_list()
    {
        var row = Row();
        row.Tags = [];

        Map.Project(row).Tags.Should().BeEmpty();
    }

    [Fact]
    public void The_SELECTOR_is_one_expression_a_provider_can_translate()
    {
        // Asil sinav: agacta bir metot cagrisi YOK. Olsa saglayici ceviremezdi — ve
        // kompozisyonun cocugu cagirmak yerine YAZMASININ sebebi bu.
        var text = Map.Selector.ToString();

        text.Should().Contain("new BedModel").And.Contain("new BedTypeModel");
        text.Should().NotContain("Invoke").And.NotContain("Compile");
    }

    [Fact]
    public void The_SELECTOR_and_Project_are_the_same_map()
    {
        var throughDelegate = Map.Project(Row());
        var throughTree = Map.Selector.Compile()(Row());

        throughTree.BedState.Should().Be(throughDelegate.BedState);
        throughTree.BedType!.Name.Should().Be(throughDelegate.BedType!.Name);
        throughTree.Tags!.Count.Should().Be(throughDelegate.Tags!.Count);
    }

    [Fact]
    public void The_tree_is_the_SAME_instance_every_time()
    {
        // Cift basina bir kez kuruluyor: ornek basina degil, cagri basina degil.
        ReferenceEquals(Map.Selector, new BedMap().Selector).Should().BeTrue();
    }

    [Fact]
    public void A_map_with_nothing_to_say_needs_no_Configure()
    {
        new BedTypeMap().Project(new BedType { Id = 3, Name = "Yogun" })
            .Should().BeEquivalentTo(new BedTypeModel { Id = 3, Name = "Yogun" });
    }

    [Fact]
    public void The_source_may_be_wider_than_what_is_published()
    {
        // RecordStamp modelde yok ve hicbir sey soylenmiyor: hesabi verilen uc HEDEF.
        typeof(BedModel).GetProperty("RecordStamp").Should().BeNull();

        Map.Project(Row()).Should().NotBeNull();
    }
}
