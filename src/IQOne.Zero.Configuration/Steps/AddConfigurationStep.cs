using IQOne.Zero.App;
using IQOne.Zero.App.Steps;
using IQOne.Zero.Configuration.Extensions;

namespace IQOne.Zero.Configuration.Steps;

/// <summary>Puts configuration into the service collection before anything reads it.</summary>
public sealed class AddConfigurationStep : IApplicationPreRunStep
{
    /// <summary>Runs first: other steps bind options against this configuration.</summary>
    public int Order => -1000;

    /// <inheritdoc />
    public Task OnPreRunAsync(IApplication application, CancellationToken cancellationToken)
    {
        application.ServiceCollection.AddConfiguration();

        return Task.CompletedTask;
    }
}
