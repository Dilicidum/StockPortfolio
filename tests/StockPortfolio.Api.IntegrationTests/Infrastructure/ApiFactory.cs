using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace StockPortfolio.Api.IntegrationTests.Infrastructure;

/// <summary>Boots the real Program in-process against configuration a test supplies.</summary>
public sealed class ApiFactory(
    IReadOnlyDictionary<string, string?> settings,
    Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    /// <summary>The environment name these tests run the host under.</summary>
    public const string EnvironmentName = "Testing";

    /// <inheritdoc/>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(EnvironmentName);

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(settings));

        if (configureServices is not null)
        {
            // ConfigureTestServices runs AFTER the app's own registrations, so it can replace them.
            builder.ConfigureTestServices(configureServices);
        }
    }
}
