using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace IQOne.Zero.Data.EntityFramework.Connections;

public sealed class ConnectionStringProvider(IConfiguration configuration, IHostEnvironment environment)
    : IConnectionStringProvider
{
    public string Get(string name)
    {
        var value = configuration.GetConnectionString(name);

        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(
                $"'{name}' baglanti dizesi bulunamadi. Uretimde ortam degiskeni olarak verin: " +
                $"ConnectionStrings__{name}=...  Gelistirmede: dotnet user-secrets set \"ConnectionStrings:{name}\" \"...\"");

        // Parola iceren dize dosyadan gelmis olabilir; uretimde bu kabul edilmez.
        if (!environment.IsDevelopment() && ContainsPassword(value) && !FromEnvironment(name))
            throw new InvalidOperationException(
                $"'{name}' baglanti dizesi parola iceriyor ve ortam degiskeninden gelmiyor. " +
                "Parolayi ConnectionStrings__" + name + " ortam degiskenine tasiyin ya da " +
                "Integrated Security kullanin.");

        return value;
    }

    private static bool ContainsPassword(string value)
        => value.Contains("Password=", StringComparison.OrdinalIgnoreCase)
        || value.Contains("Pwd=", StringComparison.OrdinalIgnoreCase);

    private static bool FromEnvironment(string name)
        => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable($"ConnectionStrings__{name}"));
}
