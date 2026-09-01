using IQOne.Zero.DependencyInjection.Descriptors;

namespace IQOne.Zero.Data.Ownership;

/// <summary>
/// Tables this deployment is allowed to write.
/// </summary>
/// <remarks>
/// Three applications write to the same database, so each table must have exactly one
/// owner. In a branch deployment, tables behind synonyms are replicas: writes succeed
/// locally and are lost on the next synchronisation.
/// </remarks>
public interface IWriteOwnership : ISingleton
{
    bool CanWrite(string? schema, string table);

    IReadOnlyCollection<string> OwnedTables { get; }
}

/// <summary>Raised instead of allowing a write that would be silently discarded.</summary>
public sealed class WriteOwnershipViolationException(string table, string operation)
    : InvalidOperationException(
        $"'{table}' tablosuna '{operation}' islemi reddedildi: bu dagitim o tablonun sahibi degil. " +
        "Sahiplik matrisine bak ya da islemi sahibi olan uygulamaya yonlendir.")
{
    public string Table { get; } = table;
    public string Operation { get; } = operation;
}
