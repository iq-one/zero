using System;
using System.Collections.Generic;
using System.Linq;
using IQOne.Zero.Persistence;

namespace IQOne.Zero.Mapping.Tests;

/// <summary>The bed query, whose selector is the declared map.</summary>
/// <remarks>
/// It says nothing about shape. The pair is in its base type and the map for that pair is the
/// answer — so the twenty-odd lines a hand-written selector needs are not written, and there is
/// no second copy to drift from the first.
/// </remarks>
public sealed partial class BedQuery : Specification<Bed, BedModel>
{
    public BedQuery(short? id = null)
    {
        if (id is not null) Where(e => e.Id == id);

        ReadOnly();
    }
}

/// <summary>
/// A specification and a map, run together.
/// </summary>
/// <remarks>
/// The generator's tests assert on the source; this compiles it and evaluates the selector,
/// which is the only way to know the specification really projects through the map.
/// </remarks>
public class SpecificationTests
{
    private static readonly List<Bed> Rows =
    [
        new()
        {
            Id = 1, Name = "Bir", State = 1,
            BedType = new BedType { Id = 7, Name = "Normal" },
            Tags = [new Tag { Id = 9, Text = "pencere" }]
        },
        new() { Id = 2, Name = "Iki", State = 2, BedType = null, Tags = [] }
    ];

    [Fact]
    public void The_specification_projects_through_the_map()
    {
        var selector = new BedQuery().Selector.Compile();

        var models = Rows.Select(selector).ToList();

        models[0].Name.Should().Be("Bir");
        models[0].BedState.Should().Be(BedState.Empty);
        models[0].BedType!.Name.Should().Be("Normal");
        models[0].Tags!.Single().Text.Should().Be("pencere");

        models[1].BedState.Should().Be(BedState.Occupied);
        models[1].BedType.Should().BeNull();
        models[1].Tags.Should().BeEmpty();
    }

    [Fact]
    public void It_is_the_SAME_selector_the_map_holds()
    {
        // Ikinci bir kopya yok: sartname haritanin agacini tasiyor, kendi agacini degil.
        ReferenceEquals(new BedQuery().Selector, new BedMap().Selector).Should().BeTrue();
    }

    [Fact]
    public void The_specifications_own_filters_still_apply()
    {
        var specification = new BedQuery(2);

        specification.Criteria.Should().NotBeNull();
        specification.AsNoTracking.Should().BeTrue();
    }
}
