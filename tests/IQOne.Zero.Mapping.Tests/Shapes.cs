using System.Collections.Generic;

namespace IQOne.Zero.Mapping.Tests;

public enum BedState : byte { Unknown = 0, Empty = 1, Occupied = 2 }

public sealed class Bed
{
    public short Id { get; set; }
    public string? Name { get; set; }
    public short? DepartmentId { get; set; }
    public byte State { get; set; }

    /// <summary>Never published; the source is allowed to be wider.</summary>
    public byte[]? RecordStamp { get; set; }

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

public sealed class BedModel
{
    public short Id { get; set; }
    public string? Name { get; set; }
    public short? DepartmentId { get; set; }
    public BedState BedState { get; set; }
    public BedTypeModel? BedType { get; set; }
    public List<TagModel>? Tags { get; set; }
}

public sealed partial class BedTypeMap : Map<BedType, BedTypeModel>;

public sealed partial class TagMap : Map<Tag, TagModel>;

public sealed partial class BedMap : Map<Bed, BedModel>
{
    protected override void Configure(IMapBuilder<Bed, BedModel> map) => map
        // Not a column of its own: the state column read as a bed.
        .Member(m => m.BedState, e => (BedState)e.State)
        .Member(m => m.BedType, e => e.BedType.To<BedTypeModel>())
        .Member(m => m.Tags, e => e.Tags.To<List<TagModel>>());
}
