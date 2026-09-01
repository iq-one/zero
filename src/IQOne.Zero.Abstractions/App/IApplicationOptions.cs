namespace IQOne.Zero.App;

/// <summary>
/// Settings applied to the container when the application builds its service provider.
/// </summary>
public interface IApplicationOptions
{
    /// <summary>
    /// Verifies that scoped services are never resolved from the root provider.
    /// </summary>
    bool ValidateScopes { get; set; }

    /// <summary>
    /// Attempts to construct every registered service while the provider is being built,
    /// so a missing registration fails at startup rather than on the first request.
    /// </summary>
    bool ValidateOnBuild { get; set; }
}
