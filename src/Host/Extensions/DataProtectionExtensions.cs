using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Options;

using StockPortfolio.Host.Adapters;
using StockPortfolio.Modules.MarketData.Application.Abstractions;

namespace StockPortfolio.Host.Extensions;

internal static class DataProtectionExtensions
{
    public static IServiceCollection AddStockPortfolioDataProtection(this IServiceCollection services)
    {
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();

        // Concrete type, not IXmlRepository: the framework reads the repository off KeyManagementOptions, never from DI.
        services.AddSingleton<KeyRingXmlRepository>();

        services.AddDataProtection()
            .SetApplicationName("StockPortfolio")
            .Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
                new ConfigureOptions<KeyManagementOptions>(options =>
                    options.XmlRepository = sp.GetRequiredService<KeyRingXmlRepository>()));

        return services;
    }

    /// <summary>Fails at startup, not on the first saved key, if the host forgot to call AddStockPortfolioDataProtection.</summary>
    public static IServiceCollection ValidateSecretProtectorIsRegistered(this IServiceCollection services)
    {
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(ISecretProtector)))
        {
            throw new InvalidOperationException(
                "ISecretProtector has no registration. AddStockPortfolioDataProtection must run before this "
                + "check, or every saved provider key silently has nothing to encrypt it with.");
        }

        return services;
    }
}
