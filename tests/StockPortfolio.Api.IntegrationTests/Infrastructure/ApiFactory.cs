using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace StockPortfolio.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real <c>Program</c> in-process against configuration a test supplies.
/// </summary>
/// <param name="settings">Configuration entries, in <c>Section:Key</c> form.</param>
/// <param name="configureServices">Optional last-word service overrides.</param>
/// <remarks>
/// <para>
/// <b>Configuration has to be in place before the host builds, not after.</b>
/// <c>AddIdentityModule</c> and <c>AddStockPortfolioAuthentication</c> both throw during registration
/// when <c>ConnectionStrings:Identity</c> or <c>Jwt:SigningKey</c> is missing — deliberately, so a
/// misconfigured deployment dies at startup instead of 401-ing every request. That is why nothing
/// here mutates <c>appsettings.json</c> or pokes at a built <c>IServiceProvider</c>.
/// </para>
/// <para>
/// The environment is <c>Testing</c> rather than <c>Development</c> on purpose:
/// <c>appsettings.Development.json</c> carries a real, non-empty <c>localhost</c> connection string,
/// and if it ever won the precedence fight these tests would quietly run against whatever database
/// happens to be on the developer's machine. Under <c>Testing</c> the only competing value is the
/// empty placeholder in <c>appsettings.json</c>, so a precedence mistake fails loudly at startup
/// instead of silently passing against the wrong server.
/// </para>
/// <para>
/// Both <c>UseSetting</c> and an appended in-memory source are used. The first seeds host
/// configuration, the second is added after <c>appsettings*.json</c> and therefore wins outright;
/// belt and braces costs one line and removes an ordering question entirely.
/// </para>
/// </remarks>
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
            // ConfigureTestServices runs AFTER the application's own registrations, which is what makes
            // it possible to replace a descriptor the module already added.
            builder.ConfigureTestServices(configureServices);
        }
    }
}
