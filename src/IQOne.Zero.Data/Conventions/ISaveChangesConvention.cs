namespace IQOne.Zero.Data.Conventions;

/// <summary>
/// Adjusts tracked entities before they are persisted, for concerns such as audit
/// stamping or converting deletes into soft deletes.
/// </summary>
/// <typeparam name="TContext">The provider's context type.</typeparam>
public interface ISaveChangesConvention<in TContext>
{
    void Apply(TContext context);
}
