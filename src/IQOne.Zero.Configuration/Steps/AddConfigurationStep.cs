using IQOne.Zero.Configuration.Extensions;
using IQOne.Zero.App;
using IQOne.Zero.App.Steps;

namespace IQOne.Zero.Configuration.Steps;

/// Konfigurasyonu servis koleksiyonuna ekleyen uygulama adimi.
/// Diger adimlardan once calismasi icin Order negatif.
public sealed class AddConfigurationStep : IApplicationPreRunStep
{
    public int Order => -1000;

    public Task OnPreRunAsync(IApplication application, CancellationToken cancellationToken)
    {
        application.ServiceCollection.AddConfiguration();

        return Task.CompletedTask;
    }
}
