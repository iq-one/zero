using IQOne.Zero.Data.Ownership;
using Microsoft.Extensions.Options;

namespace IQOne.Zero.Data.EntityFramework.Context;

/// <summary>
/// Ownership matrix for this deployment. Central and branch deployments run the same
/// code with different matrices.
/// </summary>
public sealed class WriteOwnershipOptions
{
    /// <summary>Empty means unrestricted, which suits single-deployment installations.</summary>
    public HashSet<string> OwnedTables { get; set; } = [];
}

public sealed class DeploymentWriteOwnership(IOptions<WriteOwnershipOptions> options) : IWriteOwnership
{
    private readonly HashSet<string> _owned =
        new(options.Value.OwnedTables, StringComparer.OrdinalIgnoreCase);

    public bool CanWrite(string? schema, string table)
        => _owned.Count == 0 || _owned.Contains(table) || _owned.Contains($"{schema}.{table}");

    public IReadOnlyCollection<string> OwnedTables => _owned;
}
