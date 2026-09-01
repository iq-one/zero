namespace IQOne.Zero.Data.Conventions;

/// <summary>
/// Applies model-wide mapping rules, such as marking a concurrency token or a
/// shared column set.
/// </summary>
/// <typeparam name="TModelBuilder">The provider's model builder type.</typeparam>
public interface IModelConvention<in TModelBuilder>
{
    void Apply(TModelBuilder modelBuilder);
}
