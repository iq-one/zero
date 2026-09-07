using System.Collections.Generic;
using System.Linq;

namespace IQOne.Zero.Mapping.Tests;

/// <summary>A positional record, and a positional record inside a collection, run.</summary>
/// <remarks>
/// This is the shape the application's claim queries actually have, and the one an object
/// initialiser cannot produce: a record with a primary constructor has no parameterless one.
/// </remarks>
public class ConstructionTests
{
    private static readonly RoleMap Map = new();

    private static Role Row() => new()
    {
        Id = 4,
        Claims =
        [
            new RoleClaim { Key = "beds.read", Value = "1" },
            new RoleClaim { Key = "beds.write", Value = null }
        ]
    };

    [Fact]
    public void A_positional_record_is_constructed_from_the_source()
    {
        var group = Map.Project(Row());

        // RoleId <- Id: adlar farkli, o yuzden bildirilmis.
        group.RoleId.Should().Be(4);
    }

    [Fact]
    public void A_collection_of_positional_records_maps_every_element()
    {
        var group = Map.Project(Row());

        group.Claims.Should().NotBeNull();
        group.Claims!.Select(c => c.Key).Should().Equal(["beds.read", "beds.write"]);
        group.Claims!.Select(c => c.Value).Should().Equal(["1", null]);
    }

    [Fact]
    public void The_selector_is_still_one_expression_a_provider_can_translate()
    {
        var text = Map.Selector.ToString();

        text.Should().Contain("new RoleClaimGroup").And.Contain("new ClaimRow");
        text.Should().NotContain("Invoke").And.NotContain("Compile");
    }
}
