using IQOne.Zero.App;
using IQOne.Zero.App.Steps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IQOne.Zero.Configuration.Steps;

/// <summary>
/// Runs the options validation that <c>ValidateOnStart</c> only registered.
/// </summary>
/// <remarks>
/// Registered by <c>AddZeroConfiguration</c>. The validator is optional: an application that
/// validates no options has none registered, and that is not an error.
/// </remarks>
internal sealed class ValidateOptionsOnStartStep : IApplicationInitializeStep
{
    /// <summary>Runs before any other initialize step, so nothing reads a setting first.</summary>
    public int Order => -1000;

    /// <inheritdoc />
    public Task OnInitializeAsync(IApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        cancellationToken.ThrowIfCancellationRequested();

        application.ServiceProvider.GetService<IStartupValidator>()?.Validate();

        return Task.CompletedTask;
    }
}
