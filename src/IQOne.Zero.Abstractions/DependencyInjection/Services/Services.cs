namespace IQOne.Zero.DependencyInjection.Services;

/// <summary>Excludes the implementing type from generated registration.</summary>
public interface IIgnoredService;

/// <summary>
/// Marks an interface family as registrable: every interface deriving from this one
/// becomes a service type for the classes that implement it.
/// </summary>
public interface IRequiredService;
