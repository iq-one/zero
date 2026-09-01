namespace IQOne.Zero.App;

/// <summary>
/// Default container settings. Both validations are on, so a wiring mistake stops startup
/// instead of surfacing on the request that first happens to hit it.
/// </summary>
public class ApplicationOptions : IApplicationOptions
{
    /// <inheritdoc />
    public bool ValidateScopes { get; set; } = true;

    /// <inheritdoc />
    public bool ValidateOnBuild { get; set; } = true;
}
