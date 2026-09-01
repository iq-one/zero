using IQOne.Zero.App;

namespace IQOne.Zero.App;

public class ApplicationOptions : IApplicationOptions
{
    public bool ValidateScopes { get; set; } = true;

    public bool ValidateOnBuild { get; set; } = true;
}
