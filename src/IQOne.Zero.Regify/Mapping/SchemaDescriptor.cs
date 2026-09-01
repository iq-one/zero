using IQOne.Zero.Regify.Internal;

namespace IQOne.Zero.Regify.Mapping;

/// <summary>Provider-neutral facts read from a schema definition.</summary>
internal sealed record EntitySchema(
    string EntityName,
    string Table,
    string? Schema,
    string Key,
    bool Legacy,
    EquatableArray<ColumnSchema> Columns);

internal sealed record ColumnSchema(
    string Property,
    string Column,
    int? MaxLength,
    bool Required,
    string? ColumnType);
